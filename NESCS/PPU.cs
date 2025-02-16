namespace NESCS
{
    public class PPU
    {
        private readonly record struct FetchedBackgroundData(byte CoarseXScroll, byte AttributeTableData, byte PatternTableDataLow, byte PatternTableDataHigh);
        private readonly record struct FetchedSpriteData(byte XPosition, byte SpriteAttributeData, byte PatternTableDataLow, byte PatternTableDataHigh);

        public const int NtscScanlinesPerFrame = 262;
        public const int PalScanlinesPerFrame = 312;
        public const int VisibleScanlinesPerFrame = 240;
        public const int CyclesPerScanline = 341;
        public const int VisibleCyclesPerFrame = SpriteFetchStartCycle - 1;

        public const int SpriteFetchStartCycle = 257;
        public const int NextScanlineTileStartCycle = 321;

        public const int VerticalScrollResetStartCycle = 280;
        public const int VerticalScrollResetEndCycle = 304;

        public const int SpriteZeroHitStartCycle = 2;
        public const int SpriteZeroHitEndCycle = 254;
        public const int SpriteEvaluationStartCycle = 65;

        public const ushort NametableStartAddress = 0x2000;
        public const ushort AttributeTableStartAddress = NametableStartAddress + 0x03C0;
        public const ushort PaletteRAMStartAddress = 0x3F00;
        public const ushort PaletteRAMEndAddress = 0x3FFF;

        public const int MaximumSpritesPerScanline = 8;

        public const byte SpriteDataSize = 4;

        public readonly PPURegisters Registers;

        private readonly NESSystem nesSystem;

        public readonly byte[] PaletteRAM = new byte[0x20];
        public readonly byte[] ObjectAttributeMemory = new byte[0x100];

        public readonly byte[] SecondaryOAM = new byte[MaximumSpritesPerScanline * SpriteDataSize];

        public Color[] CurrentPalette { get; } = new Color[0x40]
        {
            new(0x626262), new(0x002E98), new(0x0C11C2), new(0x3B00C2), new(0x650098), new(0x7D004E), new(0x7D0000), new(0x651900),
            new(0x3B3600), new(0x0C4F00), new(0x005B00), new(0x005900), new(0x00494E), new(0x000000), new(0x000000), new(0x000000),

            new(0xABABAB), new(0x0064F4), new(0x353CFF), new(0x761BFF), new(0xAE0AF4), new(0xCF0C8F), new(0xCF231C), new(0xAE4700),
            new(0x766F00), new(0x359000), new(0x00A100), new(0x009E1C), new(0x00888F), new(0x000000), new(0x000000), new(0x000000),

            new(0xFFFFFF), new(0x4AB5FF), new(0x858CFF), new(0xC86AFF), new(0xFF58FF), new(0xFF5BE2), new(0xFF726A), new(0xFF9702),
            new(0xC8C100), new(0x85E300), new(0x4AF502), new(0x29F26A), new(0x29DBE2), new(0x4E4E4E), new(0x000000), new(0x000000),

            new(0xFFFFFF), new(0xB6E1FF), new(0xCED1FF), new(0xE9C3FF), new(0xFFBCFF), new(0xFFBDF4), new(0xFFC6C3), new(0xFFD59A),
            new(0xE9E681), new(0xCEF481), new(0xB6FB9A), new(0xA9FAC3), new(0xA9F0F4), new(0xB8B8B8), new(0x000000), new(0x000000)
        };

        public int ScanlinesPerFrame { get; }

        public bool OddFrame { get; private set; } = false;

        public int Scanline { get; private set; }
        public int Cycle { get; private set; }

        /// <summary>
        /// Indexed first by scanline, then by cycle.
        /// </summary>
        public Color[,] OutputPixels { get; }

        public bool IsRenderingEnabled => (Registers.PPUMASK & PPUMASKFlags.BackgroundEnable) != 0
            || (Registers.PPUMASK & PPUMASKFlags.SpriteEnable) != 0;

        public bool IsCurrentlyRendering => Scanline < VisibleScanlinesPerFrame && IsRenderingEnabled;

        public int SpriteHeight => (Registers.PPUCTRL & PPUCTRLFlags.SpriteHeight) != 0 ? 16 : 8;

        // Stores whether the vertical blanking NMI was enabled on the previous frame.
        // Used to trigger an NMI if the flag is toggled on midway through the blanking interval.
        private bool wasNMIEnabledPreviously = false;

        private byte fetchedCoarseX = 0;
        private byte fetchedNametableData = 0;
        private byte fetchedAttributeTableData = 0;
        private byte fetchedPatternTableDataLow = 0;
        private byte fetchedPatternTableDataHigh = 0;

        private int fetchingSpriteIndex = 0;

        private int spritesOnScanline = 0;

        // Background data is fetched two tiles in advance.
        // Once data is fetch fetched, it takes another fetch cycle before it is usually rendered,
        // however scrolling on the X axis may mean that data from the next tile is accessed earlier
        // (before the second fetch cycle has finished).
        private FetchedBackgroundData[] fetchedBackgroundData = new FetchedBackgroundData[2];
        private int pendingSpriteCount = 0;
        private readonly FetchedSpriteData[] pendingSpriteData = new FetchedSpriteData[MaximumSpritesPerScanline];

        public PPU(NESSystem system, int scanlinesPerFrame)
        {
            nesSystem = system;

            Registers = new PPURegisters(this);

            ScanlinesPerFrame = scanlinesPerFrame;
            OutputPixels = new Color[VisibleScanlinesPerFrame, VisibleCyclesPerFrame];

            Reset(true);
        }

        /// <summary>
        /// Read/write a byte from the PPU's mapped memory.
        /// </summary>
        public byte this[ushort address]
        {
            get => address <= 0x3EFF
                ? nesSystem.InsertedCartridgeMapper.MappedPPURead(address)
                : (byte)(PaletteRAM[address & 0b11111] & 0b111111);
            set
            {
                if (address <= 0x3EFF)
                {
                    nesSystem.InsertedCartridgeMapper.MappedPPUWrite(address, value);
                }
                else
                {
                    // Palette RAM
                    value &= 0b111111;
                    if ((address & 0b11) == 0)
                    {
                        // Entry 0 in each palette is mirrored across both sprites and the background
                        PaletteRAM[address & 0b1111] = value;
                        PaletteRAM[(address & 0b1111) | 0b10000] = value;
                    }
                    else
                    {
                        PaletteRAM[address & 0b11111] = value;
                    }
                }
            }
        }

        /// <summary>
        /// Simulate a reset of the processor, either via the reset line or via a power cycle.
        /// </summary>
        public void Reset(bool powerCycle)
        {
            if (powerCycle)
            {
                Registers.PPUSTATUS = 0;
                Registers.OAMADDR = 0;
                Registers.V = 0;
            }

            Registers.PPUCTRL = 0;
            Registers.PPUMASK = 0;

            Registers.T = 0;
            Registers.X = 0;
            Registers.W = false;

            Registers.readBuffer = 0;

            OddFrame = false;

            Scanline = 0;
            Cycle = 0;
        }

        /// <summary>
        /// Run a single cycle of the PPU logic.
        /// </summary>
        /// <returns><see langword="true"/> if this was the last cycle of the frame, otherwise <see langword="false"/>.</returns>
        public bool ProcessNextDot()
        {
            RunDotLogic();

            return IncrementCycle();
        }

        /// <summary>
        /// Increment the current cycle and, if necessary, the current scanline of the PPU.
        /// One/both are automatically wrapped back to 0 if this is the end of the current scanline/frame.
        /// </summary>
        /// <returns><see langword="true"/> if this was the last cycle of the frame, otherwise <see langword="false"/>.</returns>
        public bool IncrementCycle()
        {
            Cycle++;
            if (Cycle >= CyclesPerScanline
                // Every other frame skips the last cycle of the pre-render scanline
                || (OddFrame && Scanline == -1 && Cycle == CyclesPerScanline - 1))
            {
                Cycle = 0;
                Scanline++;
                if (Scanline >= ScanlinesPerFrame - 1)
                {
                    Scanline = -1;
                    OddFrame = !OddFrame;
                    return true;
                }
            }

            return false;
        }

        private void RunDotLogic()
        {
            if (!wasNMIEnabledPreviously && (Registers.PPUCTRL & PPUCTRLFlags.VerticalBlankNmiEnable) != 0
                && Scanline > VisibleScanlinesPerFrame)
            {
                // Turning on vertical blanking interrupts midway through the blanking interval will immediately fire the NMI.
                nesSystem.CpuCore.NonMaskableInterrupt();
            }

            wasNMIEnabledPreviously = (Registers.PPUCTRL & PPUCTRLFlags.VerticalBlankNmiEnable) != 0;

            switch (Scanline)
            {
                // Visible scanlines & Pre-render scanline
                case <= VisibleScanlinesPerFrame - 1:
                    if (Cycle == 0)
                    {
                        // Idle cycle
                        // Reset per-scanline internal state specific to this emulator here
                        pendingSpriteCount = spritesOnScanline;
                        spritesOnScanline = 0;
                        fetchingSpriteIndex = 0;
                        return;
                    }

                    if (!IsRenderingEnabled)
                    {
                        if (Scanline is >= 0 and < VisibleScanlinesPerFrame
                            && Cycle is >= 1 and <= VisibleCyclesPerFrame)
                        {
                            // The colour displayed when rendering is disabled is usually the backdrop colour,
                            // unless V is currently in palette RAM in which case that colour is used
                            OutputPixels[Scanline, Cycle - 1] = Registers.V is >= PaletteRAMStartAddress and <= PaletteRAMEndAddress
                                ? CurrentPalette[this[Registers.V]]
                                : CurrentPalette[this[PaletteRAMStartAddress]];
                        }
                        return;
                    }

                    if (Scanline == -1)
                    {
                        // Pre-render scanline
                        if (Cycle == 1)
                        {
                            Registers.PPUSTATUS = 0;
                        }
                        else if (Cycle is >= VerticalScrollResetStartCycle and <= VerticalScrollResetEndCycle)
                        {
                            // Restore vertical components of V from T.
                            Registers.V = (ushort)((Registers.V & 0b000010000011111) | (Registers.T & 0b111101111100000));
                        }
                    }

                    if (Cycle is < SpriteFetchStartCycle or >= NextScanlineTileStartCycle)
                    {
                        RunBackgroundFetchDotLogic();

                        if (Scanline != -1)
                        {
                            if (Cycle < SpriteEvaluationStartCycle)
                            {
                                // Secondary OAM clear
                                if (Cycle % 2 == 1)
                                {
                                    SecondaryOAM[(Cycle - 1) / 2] = 0xFF;
                                }
                            }
                            else if (Cycle < SpriteFetchStartCycle)
                            {
                                RunSpriteEvaluationDotLogic();
                            }
                        }

                        // There is no newly fetched background data on the first active cycle to move into storage for rendering,
                        // all the data for the first two tiles was moved in the previous scanline.
                        if (Cycle > 1)
                        {
                            switch (Cycle & 0b111)
                            {
                                case 0:
                                    // Last fetch for tile was just completed, increment to next one
                                    Registers.CoarseXScrollIncrement();
                                    break;
                                case 1:
                                    // First dot of tile will be rendered this cycle, store background data needed for rendering
                                    fetchedBackgroundData[0] = fetchedBackgroundData[1];
                                    fetchedBackgroundData[1] = new FetchedBackgroundData(
                                        fetchedCoarseX, fetchedAttributeTableData, fetchedPatternTableDataLow, fetchedPatternTableDataHigh);
                                    break;
                            }
                        }

                        if (Cycle == SpriteFetchStartCycle - 1)
                        {
                            Registers.YScrollIncrement();
                        }
                    }
                    else
                    {
                        Registers.OAMADDR = 0;

                        RunSpriteFetchDotLogic();

                        if (Cycle == SpriteFetchStartCycle)
                        {
                            // Restore horizontal components of V from T.
                            Registers.V = (ushort)((Registers.V & 0b111101111100000) | (Registers.T & 0b000010000011111));
                        }

                        if ((Cycle & 0b111) == 0)
                        {
                            if (fetchingSpriteIndex < spritesOnScanline)
                            {
                                int startIndex = fetchingSpriteIndex * SpriteDataSize;

                                // All sprite data has been fetched, store it and increment to next
                                pendingSpriteData[fetchingSpriteIndex] = new FetchedSpriteData(
                                    SecondaryOAM[startIndex + 3], SecondaryOAM[startIndex + 2], fetchedPatternTableDataLow, fetchedPatternTableDataHigh);

                                fetchingSpriteIndex++;
                            }
                        }
                    }
                    break;
                // Post-render scanline
                case VisibleScanlinesPerFrame:
                    // Idle scanline
                    break;
                // Vertical blanking start
                case VisibleScanlinesPerFrame + 1:
                    if (Cycle == 1)
                    {
                        Registers.PPUSTATUS |= PPUSTATUSFlags.VerticalBlank;
                        nesSystem.CpuCore.NonMaskableInterrupt();
                    }
                    break;
                // Vertical blanking interval
                default:
                    break;
            }

            if (Scanline is >= 0 and < VisibleScanlinesPerFrame && Cycle <= VisibleCyclesPerFrame)
            {
                RenderDot();
            }
        }

        private void RunBackgroundFetchDotLogic()
        {
            int cycleRem = (Cycle - 1) & 0b111;
            switch (cycleRem)
            {
                case 0b001:
                    fetchedCoarseX = Registers.CoarseXScroll;
                    fetchedNametableData =
                        this[(ushort)(NametableStartAddress | (Registers.V & 0b000111111111111))];
                    break;
                case 0b011:
                    fetchedAttributeTableData =
                        this[(ushort)(AttributeTableStartAddress
                            | (Registers.V & 0b000110000000000)
                            | ((Registers.V & 0b000001110000000) >> 4)
                            | ((Registers.V & 0b000000000011100) >> 2))];
                    break;
                case 0b101:
                case 0b111:
                    ushort patternAddress = (ushort)((fetchedNametableData << 4) | Registers.FineYScroll);
                    if ((Registers.PPUCTRL & PPUCTRLFlags.BackgroundTileSelect) != 0)
                    {
                        patternAddress |= 0b1000000000000;
                    }

                    if (cycleRem == 0b111)
                    {
                        patternAddress |= 0b1000;
                        fetchedPatternTableDataHigh = this[patternAddress];
                    }
                    else
                    {
                        fetchedPatternTableDataLow = this[patternAddress];
                    }
                    break;
            }
        }

        private void RunSpriteFetchDotLogic()
        {
            int cycleRem = Cycle & 0b111;
            if (cycleRem is 0b110 or 0b000 && fetchingSpriteIndex < spritesOnScanline)
            {
                bool tallSprites = (Registers.PPUCTRL & PPUCTRLFlags.SpriteHeight) != 0;

                int startIndex = fetchingSpriteIndex * SpriteDataSize;
                byte yCoord = SecondaryOAM[startIndex];
                byte tileIndexData = SecondaryOAM[startIndex + 1];
                byte attributes = SecondaryOAM[startIndex + 2];
                int yDiff = (Registers.CoarseYScroll * 8 + Registers.FineYScroll - yCoord - 1) & 0b1111;
                if ((attributes & 0b10000000) != 0)
                {
                    // Vertical flip
                    yDiff ^= 0b111;
                    if (tallSprites)
                    {
                        yDiff ^= 0b1000;
                    }
                }
                if ((yDiff & 0b1000) != 0)
                {
                    // Second tile of 8x16 sprite
                    tileIndexData += 0b10;
                }

                // 8x8 sprites use the sprite's entire tile index and use PPUCTRL to select the tile bank.
                // 8x16 sprites instead use the least significant bit of the tile index as a bank selection and ignore PPUCTRL.
                ushort patternAddress = (ushort)((tallSprites ? ((tileIndexData & 0b1111110) << 3) : (tileIndexData << 4)) | (yDiff & 0b111));
                if ((tallSprites && (tileIndexData & 1) != 0)
                    || (!tallSprites && (Registers.PPUCTRL & PPUCTRLFlags.SpriteTileSelect) != 0))
                {
                    patternAddress |= 0b1000000000000;
                }

                if (cycleRem == 0)
                {
                    patternAddress |= 0b1000;
                    fetchedPatternTableDataHigh = this[patternAddress];
                }
                else
                {
                    fetchedPatternTableDataLow = this[patternAddress];
                }
            }
        }

        private void RunSpriteEvaluationDotLogic()
        {
            if (Cycle % 3 == 2)
            {
                byte yCoord = ObjectAttributeMemory[Registers.OAMADDR];
                int yDiff = Registers.CoarseYScroll * 8 + Registers.FineYScroll - yCoord;
                if (yDiff < 0 || yDiff >= SpriteHeight)
                {
                    // Sprite is not on this scanline - skip it
                    Registers.OAMADDR += SpriteDataSize;
                    return;
                }

                if (spritesOnScanline >= MaximumSpritesPerScanline)
                {
                    Registers.PPUSTATUS |= PPUSTATUSFlags.SpriteOverflow;
                }
                else
                {
                    int startIndex = spritesOnScanline * SpriteDataSize;
                    for (int i = 0; i < SpriteDataSize; i++)
                    {
                        SecondaryOAM[startIndex + i] = ObjectAttributeMemory[Registers.OAMADDR++];
                    }
                    spritesOnScanline++;
                }
            }
        }

        private void RenderDot()
        {
            int tileXPosition = fetchedBackgroundData[0].CoarseXScroll * 8;
            int screenXPosition = tileXPosition + Registers.X + ((Cycle - 1) & 0b111);

            int bgPaletteIndex = 0;
            int bgPalette = 0;
            if ((Registers.PPUMASK & PPUMASKFlags.BackgroundEnable) != 0)
            {
                // If fine X scroll has moved into the next tile, use that data instead
                FetchedBackgroundData backgroundData = (screenXPosition & 0b1000) != (tileXPosition & 0b1000)
                    ? fetchedBackgroundData[1]
                    : fetchedBackgroundData[0];

                // Left most (first) pixel is stored in most significant (last) bit
                int bgXOffset = 7 - (screenXPosition & 0b111);
                int bgBit = 1 << bgXOffset;
                bgPaletteIndex = ((backgroundData.PatternTableDataLow & bgBit) >> bgXOffset)
                    | (((backgroundData.PatternTableDataHigh & bgBit) >> bgXOffset) << 1);

                // Get the current 16x16 quadrant from the 32x32 attribute data
                // (each 4x4 tile area is packed in the same attribute byte, split into 2x2 tile areas that can be individually modified)
                bgPalette = (backgroundData.AttributeTableData >> (((screenXPosition & 0b10000) >> 3) | ((Registers.CoarseYScroll & 0b10) << 1))) & 0b11;
            }

            int spritePaletteIndex = 0;
            bool spriteBehindBackground = false;
            int spritePalette = 0;
            if ((Registers.PPUMASK & PPUMASKFlags.SpriteEnable) != 0)
            {
                for (int spriteIndex = 0; spriteIndex < pendingSpriteCount; spriteIndex++)
                {
                    FetchedSpriteData spriteData = pendingSpriteData[spriteIndex];

                    if (screenXPosition - spriteData.XPosition is < 0 or >= 8)
                    {
                        // Sprite out of horizontal range
                        continue;
                    }

                    // Left most (first) pixel is stored in most significant (last) bit
                    int spriteXOffset = 7 - ((Registers.X + screenXPosition - spriteData.XPosition) & 0b111);
                    if ((spriteData.SpriteAttributeData & 0b1000000) != 0)
                    {
                        // Flip horizontally
                        spriteXOffset ^= 0b111;
                    }
                    int spriteBit = 1 << spriteXOffset;
                    spritePaletteIndex = ((spriteData.PatternTableDataLow & spriteBit) >> spriteXOffset)
                        | (((spriteData.PatternTableDataHigh & spriteBit) >> spriteXOffset) << 1);

                    if (spritePaletteIndex != 0)
                    {
                        if (spriteIndex == 0 && bgPaletteIndex != 0
                            && Cycle is >= SpriteZeroHitStartCycle and <= SpriteZeroHitEndCycle)
                        {
                            Registers.PPUSTATUS |= PPUSTATUSFlags.SpriteZeroHit;
                        }
                        spriteBehindBackground = (spriteData.SpriteAttributeData & 0b100000) != 0;
                        spritePalette = spriteData.SpriteAttributeData & 0b11;
                        // First sprites in memory take priority. This pixel was not transparent, so no more sprites need to be evaluated.
                        // Upholds the sprite priority quirk.
                        break;
                    }
                }
            }

            // Palette indices of 0 are always transparent regardless of the contents of palette RAM at the corresponding address
            int paletteIndexToRender;
            if (bgPaletteIndex != 0 && (spriteBehindBackground || spritePaletteIndex == 0))
            {
                // Background has priority
                paletteIndexToRender = this[(ushort)(PaletteRAMStartAddress + ((bgPalette << 2) | bgPaletteIndex))];
            }
            else if (spritePaletteIndex != 0)
            {
                // Sprite has priority
                paletteIndexToRender = this[(ushort)(PaletteRAMStartAddress + (0b10000 | (spritePalette << 2) | spritePaletteIndex))];
            }
            else
            {
                // Neither background nor sprite are present on pixel - use backdrop colour
                paletteIndexToRender = this[PaletteRAMStartAddress];
            }

            OutputPixels[Scanline, Cycle - 1] = CurrentPalette[paletteIndexToRender];
        }
    }
}
