namespace NESCS
{
    [Flags]
    public enum CPUStatusFlags : byte
    {
        None = 0,

        Carry = 0b1,
        Zero = 0b10,
        InterruptDisable = 0b100,
        Decimal = 0b1000,
        Break = 0b10000,
        Always = 0b100000,
        Overflow = 0b1000000,
        Negative = 0b10000000
    }

    [Flags]
    public enum PPUCTRLFlags : byte
    {
        None = 0,

        SelectSecondNametableX = 0b1,
        SelectSecondNametableY = 0b10,
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
        ColorEmphasisBlue = 0b10000000,

        RenderingEnable = BackgroundEnable | SpriteEnable
    }

    [Flags]
    public enum PPUSTATUSFlags : byte
    {
        None = 0,

        SpriteOverflow = 0b100000,
        SpriteZeroHit = 0b1000000,
        VerticalBlank = 0b10000000
    }

    public record CPURegisters
    {
        /// <summary>
        /// Accumulator
        /// </summary>
        public byte A;
        /// <summary>
        /// X Index
        /// </summary>
        public byte X;
        /// <summary>
        /// Y Index
        /// </summary>
        public byte Y;
        /// <summary>
        /// Program Counter
        /// </summary>
        public ushort PC;
        /// <summary>
        /// Stack Pointer
        /// </summary>
        public byte S;
        /// <summary>
        /// Status
        /// </summary>
        public CPUStatusFlags P = CPUStatusFlags.Always;
    }

    public record PPURegisters
    {
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
        /// <summary>
        /// Mapped to 0x2004 - Sprite RAM data
        /// </summary>
        public byte OAMDATA;
        /// <summary>
        /// X and Y scroll - Mapped to 0x2005, written one byte at a time
        /// </summary>
        public ushort PPUSCROLL;
        /// <summary>
        /// VRAM address - Mapped to 0x2006, written one byte at a time
        /// </summary>
        public ushort PPUADDR;
        /// <summary>
        /// VRAM data - Mapped to 0x2007
        /// </summary>
        public byte PPUDATA;

        /// <summary>
        /// Internal - Current VRAM address / Scroll position
        /// </summary>
        public short V;
        /// <summary>
        /// Internal - Temporary VRAM address / Scroll position
        /// </summary>
        public short T;
        /// <summary>
        /// Internal - Fine X scroll
        /// </summary>
        public byte X;
        /// <summary>
        /// Internal - Write latch (first or second write)
        /// </summary>
        public bool W;

        private byte dataBus = 0;

        public byte this[ushort mappedAddress]
        {
            get
            {
                if (mappedAddress == 0x2002)
                {
                    // Reading PPUSTATUS resets write latch
                    W = false;
                }

                return mappedAddress switch
                {
                    0x2000 or 0x2001 or 0x2003 or 0x2005 or 0x2006 or 0x4014 => dataBus,  // Write-only registers
                    0x2002 => (byte)PPUSTATUS,
                    0x2004 => OAMDATA,
                    0x2007 => PPUDATA,
                    _ => throw new ArgumentException("The given address is not a valid mapped PPU register address.", nameof(mappedAddress))
                };
            }
            set
            {
                dataBus = value;

                switch (mappedAddress)
                {
                    case 0x2000:
                        PPUCTRL = (PPUCTRLFlags)dataBus;
                        break;
                    case 0x2001:
                        PPUMASK = (PPUMASKFlags)dataBus;
                        break;
                    case 0x2002:
                        // PPUSTATUS is Read-only
                        break;
                    case 0x2003:
                        OAMADDR = dataBus;
                        break;
                    case 0x2004:
                        OAMDATA = dataBus;
                        break;
                    case 0x2005:
                        Set16BitsWith8BitsLatched(ref PPUSCROLL, dataBus);
                        break;
                    case 0x2006:
                        Set16BitsWith8BitsLatched(ref PPUADDR, dataBus);
                        break;
                    case 0x2007:
                        PPUDATA = dataBus;
                        break;
                    default:
                        throw new ArgumentException("The given address is not a valid mapped PPU register address.", nameof(mappedAddress));
                }
            }
        }

        /// <summary>
        /// Set either the lower or upper byte of a 16-bit value based on the current value of the <see cref="W"/> register.
        /// Inverts the register after writing.
        /// </summary>
        public void Set16BitsWith8BitsLatched(ref ushort target, byte value)
        {
            if (W)
            {
                target = (ushort)((target & 0xFF00) | value);
            }
            else
            {
                target = (ushort)((value << 8) | (target & 0xFF));
            }

            W = !W;
        }
    }
}
