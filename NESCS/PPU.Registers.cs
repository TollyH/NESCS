namespace NESCS
{
    [Flags]
    public enum PPUCTRLFlags : byte
    {
        None = 0,

        IncrementMode = 0b100,
        SpriteTileSelect = 0b1000,
        BackgroundTileSelect = 0b10000,
        SpriteHeight = 0b100000,
        ExtPinDirection = 0b1000000,
        VerticalBlankNmiEnable = 0b10000000
    }

    [Flags]
    public enum PPUMASKFlags : byte
    {
        None = 0,

        Greyscale = 0b1,
        BackgroundLeftColumnEnable = 0b10,
        SpriteLeftColumnEnable = 0b100,
        BackgroundEnable = 0b1000,
        SpriteEnable = 0b10000,
        ColorEmphasisRed = 0b100000,
        ColorEmphasisGreen = 0b1000000,
        ColorEmphasisBlue = 0b10000000
    }

    [Flags]
    public enum PPUSTATUSFlags : byte
    {
        None = 0,

        SpriteOverflow = 0b100000,
        SpriteZeroHit = 0b1000000,
        VerticalBlank = 0b10000000
    }

    public class PPURegisters(PPU ppuCore)
    {
        public const ushort MappedPPUCTRLAddress = 0x2000;
        public const ushort MappedPPUMASKAddress = 0x2001;
        public const ushort MappedPPUSTATUSAddress = 0x2002;
        public const ushort MappedOAMADDRAddress = 0x2003;
        public const ushort MappedOAMDATAAddress = 0x2004;
        public const ushort MappedPPUSCROLLAddress = 0x2005;
        public const ushort MappedPPUADDRAddress = 0x2006;
        public const ushort MappedPPUDATAAddress = 0x2007;

        private const byte mirroredPPUCTRLAddress = 0x00;
        private const byte mirroredPPUMASKAddress = 0x01;
        private const byte mirroredPPUSTATUSAddress = 0x02;
        private const byte mirroredOAMADDRAddress = 0x03;
        private const byte mirroredOAMDATAAddress = 0x04;
        private const byte mirroredPPUSCROLLAddress = 0x05;
        private const byte mirroredPPUADDRAddress = 0x06;
        private const byte mirroredPPUDATAAddress = 0x07;

        /// <summary>
        /// Mapped to 0x2000 - Miscellaneous settings
        /// </summary>
        public PPUCTRLFlags PPUCTRL;
        /// <summary>
        /// Mapped to 0x2001 - Rendering settings
        /// </summary>
        public PPUMASKFlags PPUMASK;
        /// <summary>
        /// Mapped to 0x2002 - Rendering events
        /// </summary>
        public PPUSTATUSFlags PPUSTATUS;
        /// <summary>
        /// Mapped to 0x2003 - Sprite RAM address
        /// </summary>
        public byte OAMADDR;

        private ushort _v;
        /// <summary>
        /// Internal - Current VRAM address / Scroll position (15 bits)
        /// </summary>
        public ushort V
        {
            get => (ushort)(_v & 0b111111111111111);
            set => _v = (ushort)(value & 0b111111111111111);
        }

        private ushort _t;
        /// <summary>
        /// Internal - Temporary VRAM address / Scroll position (15 bits)
        /// </summary>
        public ushort T
        {
            get => (ushort)(_t & 0b111111111111111);
            set => _t = (ushort)(value & 0b111111111111111);
        }

        private byte _x;
        /// <summary>
        /// Internal - Fine X scroll (3 bits)
        /// </summary>
        public byte X
        {
            get => (byte)(_x & 0b111);
            set => _x = (byte)(value & 0b111);
        }

        /// <summary>
        /// Internal - Write latch (first or second write)
        /// </summary>
        public bool W { get; set; }

        public byte CoarseXScroll
        {
            get => (byte)(V & 0b11111);
            set => V = (ushort)((V & 0b111111111100000) | (value & 0b11111));
        }

        public byte CoarseYScroll
        {
            get => (byte)((V & 0b1111100000) >> 5);
            set => V = (ushort)((V & 0b111110000011111) | ((value & 0b11111) << 5));
        }

        public byte FineYScroll
        {
            get => (byte)((V & 0b111000000000000) >> 12);
            set => V = (ushort)((V & 0b000111111111111) | ((value & 0b111) << 12));
        }

        public bool UseSecondHorizontalNametable
        {
            get => (V & 0b000010000000000) != 0;
            set
            {
                if (value)
                {
                    V |= 0b000010000000000;
                }
                else
                {
                    V &= unchecked((ushort)~0b000010000000000);
                }
            }
        }

        public bool UseSecondVerticalNametable
        {
            get => (V & 0b000100000000000) != 0;
            set
            {
                if (value)
                {
                    V |= 0b000100000000000;
                }
                else
                {
                    V &= unchecked((ushort)~0b000100000000000);
                }
            }
        }

        // Used to implement 1-byte delay of PPUDATA reads
        internal byte readBuffer = 0;

        private byte dataBus = 0;

        public byte this[ushort mappedAddress]
        {
            get
            {
                switch (mappedAddress & 0b111)
                {
                    case mirroredPPUCTRLAddress or mirroredPPUMASKAddress or mirroredOAMADDRAddress or mirroredPPUSCROLLAddress or mirroredPPUADDRAddress:
                        return dataBus;  // Write-only registers
                    case mirroredPPUSTATUSAddress:
                        PPUSTATUSFlags beforeChange = PPUSTATUS;
                        // Reading PPUSTATUS resets write latch and clears VBlank flag
                        W = false;
                        PPUSTATUS &= ~PPUSTATUSFlags.VerticalBlank;
                        return (byte)beforeChange;
                    case mirroredOAMDATAAddress:
                        return ppuCore.ObjectAttributeMemory[OAMADDR];
                    case mirroredPPUDATAAddress:
                        byte returnValue = readBuffer;
                        readBuffer = ppuCore[(ushort)(V & 0b11111111111111)];
                        IncrementPPUADDR();
                        return returnValue;
                    default:
                        throw new ArgumentException("The given address is not a valid mapped PPU register address.", nameof(mappedAddress));
                }
            }
            set
            {
                dataBus = value;

                switch (mappedAddress & 0b111)
                {
                    case mirroredPPUCTRLAddress:
                        PPUCTRL = (PPUCTRLFlags)(dataBus & 0b11111100);
                        // Nametable selection bits are stored separately
                        T = (ushort)((T & 0b111001111111111) | ((dataBus & 0b11) << 10));
                        break;
                    case mirroredPPUMASKAddress:
                        PPUMASK = (PPUMASKFlags)dataBus;
                        break;
                    case mirroredPPUSTATUSAddress:
                        // PPUSTATUS is Read-only
                        break;
                    case mirroredOAMADDRAddress:
                        OAMADDR = dataBus;
                        break;
                    case mirroredOAMDATAAddress:
                        ppuCore.ObjectAttributeMemory[OAMADDR] = dataBus;
                        OAMADDR++;
                        break;
                    case mirroredPPUSCROLLAddress:
                        if (!W)
                        {
                            // First write
                            T = (ushort)((T & 0b111111111100000) | (dataBus >> 3));
                            X = dataBus;
                            W = true;
                        }
                        else
                        {
                            // Second write
                            T = (ushort)((T & 0b000110000011111) | ((dataBus & 0b111) << 12) | ((dataBus & 0b11111000) << 2));
                            W = false;
                        }
                        break;
                    case mirroredPPUADDRAddress:
                        if (!W)
                        {
                            // First write
                            T = (ushort)((T & 0b000000011111111) | (dataBus << 8));
                            W = true;
                        }
                        else
                        {
                            // Second write
                            T = (ushort)((T & 0b111111100000000) | dataBus);
                            V = T;
                            W = false;
                        }
                        break;
                    case mirroredPPUDATAAddress:
                        ppuCore[V] = dataBus;
                        IncrementPPUADDR();
                        break;
                    default:
                        throw new ArgumentException("The given address is not a valid mapped PPU register address.", nameof(mappedAddress));
                }
            }
        }

        public void CoarseXScrollIncrement()
        {
            CoarseXScroll++;
            if (CoarseXScroll == 0)
            {
                UseSecondHorizontalNametable = !UseSecondHorizontalNametable;
            }
        }

        public void YScrollIncrement()
        {
            FineYScroll++;
            if (FineYScroll == 0)
            {
                CoarseYScroll++;
                if (CoarseYScroll >= 30)
                {
                    CoarseYScroll = 0;
                    if (CoarseYScroll == 30)
                    {
                        UseSecondVerticalNametable = !UseSecondVerticalNametable;
                    }
                }
            }
        }

        private void IncrementPPUADDR()
        {
            if (ppuCore.IsCurrentlyRendering)
            {
                CoarseXScrollIncrement();
                YScrollIncrement();
            }
            else
            {
                // IncrementMode being set means writes are ordered vertically, being unset means writes are ordered horizontally
                V += (ushort)(PPUCTRL.HasFlag(PPUCTRLFlags.IncrementMode) ? 32 : 1);
            }
        }
    }
}
