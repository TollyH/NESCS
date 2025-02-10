namespace NESCS
{
    public class PPU
    {
        private readonly record struct FetchedBackgroundData(byte NametableData, byte AttributeTableData, byte PatternTableDataLow, byte PatternTableDataHigh);
        private readonly record struct FetchedSpriteData(byte XPosition, byte YPosition, byte SpriteAttributeData, byte PatternTableDataLow, byte PatternTableDataHigh);

        public const int NtscScanlinesPerFrame = 262;
        public const int PalScanlinesPerFrame = 312;
        public const int VisibleScanlinesPerFrame = 240;
        public const int CyclesPerScanline = 341;

        public const int SpriteFetchStartCycle = 257;
        public const int NextScanlineTileStartCycle = 321;

        public const int VerticalScrollResetStartCycle = 280;
        public const int VerticalScrollResetEndCycle = 304;

        public const int SpriteZeroHitStartCycle = 2;
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
        public readonly byte[] ObjectAttributeMemory = new byte[0xFF];

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

        public bool IsRenderingEnabled => Registers.PPUMASK.HasFlag(PPUMASKFlags.BackgroundEnable)
            || Registers.PPUMASK.HasFlag(PPUMASKFlags.SpriteEnable);

        public bool IsCurrentlyRendering => Scanline < VisibleScanlinesPerFrame && IsRenderingEnabled;

        public int SpriteHeight => Registers.PPUCTRL.HasFlag(PPUCTRLFlags.SpriteHeight) ? 16 : 8;

        // Stores whether the vertical blanking NMI was enabled on the previous frame.
        // Used to trigger an NMI if the flag is toggled on midway through the blanking interval.
        private bool wasNMIEnabledPreviously = false;

        private byte fetchedNametableData = 0;
        private byte fetchedAttributeTableData = 0;
        private byte fetchedPatternTableDataLow = 0;
        private byte fetchedPatternTableDataHigh = 0;

        private int fetchingSpriteIndex = 0;

        private readonly byte[] secondaryOAM = new byte[MaximumSpritesPerScanline * SpriteDataSize];
        private int spritesOnScanline = 0;

        private readonly Queue<FetchedBackgroundData> pendingBackgroundData = new();
        private readonly List<FetchedSpriteData> pendingSpriteData = new();

        public PPU(NESSystem system, int scanlinesPerFrame)
        {
            nesSystem = system;

            Registers = new PPURegisters(this);

            ScanlinesPerFrame = scanlinesPerFrame;
            OutputPixels = new Color[VisibleScanlinesPerFrame, SpriteFetchStartCycle - 1];

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
                    PaletteRAM[address & 0b11111] = (byte)(value & 0b111111);
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
                || (OddFrame && Cycle == CyclesPerScanline - 1))
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
            if (!wasNMIEnabledPreviously && Registers.PPUCTRL.HasFlag(PPUCTRLFlags.VerticalBlankNmiEnable)
                && Scanline > VisibleScanlinesPerFrame)
            {
                // Turning on vertical blanking interrupts midway through the blanking interval will immediately fire the NMI.
                nesSystem.CpuCore.NonMaskableInterrupt();
            }

            wasNMIEnabledPreviously = Registers.PPUCTRL.HasFlag(PPUCTRLFlags.VerticalBlankNmiEnable);

            if (Cycle == 0)
            {
                // Idle cycle
                Registers.OAMADDR = 0;
                spritesOnScanline = 0;
                pendingSpriteData.Clear();
                fetchingSpriteIndex = 0;
                return;
            }

            if (!IsRenderingEnabled)
            {
                if (Scanline is >= 0 and < VisibleScanlinesPerFrame
                    && Cycle is >= 1 and <= SpriteFetchStartCycle)
                {
                    // The colour displayed when rendering is disabled is usually the backdrop colour,
                    // unless V is currently in palette RAM in which case that colour is used
                    OutputPixels[Scanline, Cycle - 1] = Registers.V is >= PaletteRAMStartAddress and <= PaletteRAMEndAddress
                        ? CurrentPalette[this[Registers.V]]
                        : CurrentPalette[this[PaletteRAMStartAddress]];
                }
                return;
            }

            switch (Scanline)
            {
                // Visible scanlines & Pre-render scanline
                case <= VisibleScanlinesPerFrame - 1:
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

                            pendingBackgroundData.Clear();
                        }
                    }

                    if (Cycle is < SpriteFetchStartCycle or >= NextScanlineTileStartCycle)
                    {
                        RunBackgroundFetchDotLogic();

                        if (Scanline != -1)
                        {
                            if (Cycle < SpriteEvaluationStartCycle)
                            {
                                secondaryOAM[Cycle - 1] = 0xFF;
                            }
                            else if (Cycle < SpriteFetchStartCycle)
                            {
                                RunSpriteEvaluationDotLogic();
                            }
                        }

                        if ((Cycle & 0b111) == 0)
                        {
                            // All background data needed to render a tile has been fetched, store it and increment to the next tile
                            pendingBackgroundData.Enqueue(new FetchedBackgroundData(
                                fetchedNametableData, fetchedAttributeTableData, fetchedPatternTableDataLow, fetchedPatternTableDataHigh));

                            Registers.CoarseXScrollIncrement();
                            if (Cycle == SpriteFetchStartCycle - 1)
                            {
                                Registers.YScrollIncrement();
                            }
                        }
                    }
                    else
                    {
                        RunSpriteFetchDotLogic();

                        if (Cycle == SpriteFetchStartCycle)
                        {
                            // Restore horizontal components of V from T.
                            Registers.V = (ushort)((Registers.V & 0b111101111100000) | (Registers.T & 0b000010000011111));
                        }

                        if ((Cycle & 0b111) == 1)
                        {
                            if (fetchingSpriteIndex < spritesOnScanline)
                            {
                                int startIndex = fetchingSpriteIndex * SpriteDataSize;

                                // All sprite data has been fetched, store it and increment to next
                                pendingSpriteData.Add(new FetchedSpriteData(
                                    secondaryOAM[startIndex + 3], secondaryOAM[startIndex], secondaryOAM[startIndex + 2], fetchedPatternTableDataLow, fetchedPatternTableDataHigh));

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
        }

        private void RunBackgroundFetchDotLogic()
        {
            switch ((Cycle - 1) & 0b111)
            {
                case 0b001:
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
                    int scanline = Scanline;
                    if (Cycle >= NextScanlineTileStartCycle)
                    {
                        // Fetching data for next scanline
                        scanline++;
                    }
                    ushort patternAddress = (ushort)((fetchedNametableData << 4) | (scanline & 0b111));
                    if (Registers.PPUCTRL.HasFlag(PPUCTRLFlags.BackgroundTileSelect))
                    {
                        patternAddress |= 0b1000000000000;
                    }
                    fetchedPatternTableDataLow = this[patternAddress];
                    break;
                case 0b111:
                    scanline = Scanline;
                    if (Cycle >= NextScanlineTileStartCycle)
                    {
                        // Fetching data for next scanline
                        scanline++;
                    }
                    patternAddress = (ushort)((fetchedNametableData << 4) | (scanline & 0b111) | 0b1000);
                    if (Registers.PPUCTRL.HasFlag(PPUCTRLFlags.BackgroundTileSelect))
                    {
                        patternAddress |= 0b1000000000000;
                    }
                    fetchedPatternTableDataHigh = this[patternAddress];
                    break;
            }
        }

        private void RunSpriteFetchDotLogic()
        {
            switch (Cycle & 0b111)
            {
                case 0b110:
                    byte tileIndexData = secondaryOAM[fetchingSpriteIndex * SpriteDataSize + 1];
                    ushort patternAddress = (ushort)(((tileIndexData & 0b1111110) << 3) | ((Scanline + 1) & 0b111));
                    if ((Registers.PPUCTRL.HasFlag(PPUCTRLFlags.SpriteHeight) && (tileIndexData & 1) != 0)
                        // 8x8 sprites ignore the bank selection in the sprite's tile index and instead use the table selected in PPUCTRL
                        || (!Registers.PPUCTRL.HasFlag(PPUCTRLFlags.SpriteHeight) && Registers.PPUCTRL.HasFlag(PPUCTRLFlags.SpriteTileSelect)))
                    {
                        patternAddress |= 0b1000000000000;
                    }
                    fetchedPatternTableDataLow = this[patternAddress];
                    break;
                case 0b000:
                    tileIndexData = secondaryOAM[fetchingSpriteIndex * SpriteDataSize + 1];
                    patternAddress = (ushort)(((tileIndexData & 0b1111110) << 3) | ((Scanline + 1) & 0b111) | 0b1000);
                    if ((Registers.PPUCTRL.HasFlag(PPUCTRLFlags.SpriteHeight) && (tileIndexData & 1) != 0)
                        // 8x8 sprites ignore the bank selection in the sprite's tile index and instead use the table selected in PPUCTRL
                        || (!Registers.PPUCTRL.HasFlag(PPUCTRLFlags.SpriteHeight) && Registers.PPUCTRL.HasFlag(PPUCTRLFlags.SpriteTileSelect)))
                    {
                        patternAddress |= 0b1000000000000;
                    }
                    fetchedPatternTableDataHigh = this[patternAddress];
                    break;
            }
        }

        private void RunSpriteEvaluationDotLogic()
        {
            if (Cycle % 2 != 0)
            {
                byte yCoord = ObjectAttributeMemory[Registers.OAMADDR];
                int yDiff = yCoord - Scanline;
                if (yDiff > 0 && yDiff <= SpriteHeight)
                {
                    if (spritesOnScanline >= MaximumSpritesPerScanline)
                    {
                        Registers.PPUSTATUS |= PPUSTATUSFlags.SpriteOverflow;
                    }
                    else
                    {
                        int startIndex = spritesOnScanline * SpriteDataSize;
                        for (int i = 0; i < SpriteDataSize; i++)
                        {
                            secondaryOAM[startIndex + i] = ObjectAttributeMemory[Registers.OAMADDR + i];
                        }
                        spritesOnScanline++;
                    }
                }
                Registers.OAMADDR += SpriteDataSize;
            }
        }
    }
}
