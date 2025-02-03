namespace NESCS
{
    public class PPU
    {
        public const int NtscScanlinesPerFrame = 262;
        public const int PalScanlinesPerFrame = 312;
        public const int CyclesPerScanline = 341;

        public readonly PPURegisters Registers = new();
        public readonly NESSystem NesSystem;

        public readonly byte[] PaletteRAM = new byte[0x20];
        public readonly byte[] ObjectAttributeMemory = new byte[0xFF];

        public int ScanlinesPerFrame { get; set; } = NtscScanlinesPerFrame;

        public bool OddFrame { get; private set; } = false;

        public int Scanline { get; private set; }
        public int Cycle { get; private set; }

        // Stores whether the vertical blanking NMI was enabled on the previous frame.
        // Used to trigger an NMI if the flag is toggled on midway through the blanking interval.
        private bool wasNMIEnabledPreviously = false;

        public PPU(NESSystem system)
        {
            NesSystem = system;

            Reset(true);
        }

        /// <summary>
        /// Read/write a byte from the PPU's mapped memory.
        /// </summary>
        public byte this[ushort address]
        {
            get => address <= 0x3EFF
                ? NesSystem.InsertedCartridgeMapper.MappedPPURead(address)
                : PaletteRAM[address & 0b11111];
            set
            {
                if (address <= 0x3EFF)
                {
                    NesSystem.InsertedCartridgeMapper.MappedPPUWrite(address, value);
                }
                else
                {
                    PaletteRAM[address & 0b11111] = value;
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
                Registers.PPUADDR = 0;
            }

            Registers.PPUCTRL = 0;
            Registers.PPUMASK = 0;
            Registers.PPUSCROLL = 0;
            Registers.PPUDATA = 0;

            Registers.W = false;

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
            if (!wasNMIEnabledPreviously && Registers.PPUCTRL.HasFlag(PPUCTRLFlags.VerticalBlankNmiEnable))
            {
                // Turning on vertical blanking interrupts midway through the blanking interval will immediately fire the NMI.
                NesSystem.CpuCore.NonMaskableInterrupt();
            }

            wasNMIEnabledPreviously = Registers.PPUCTRL.HasFlag(PPUCTRLFlags.VerticalBlankNmiEnable);

            if (Cycle == 0)
            {
                // Idle cycle
                return;
            }

            switch (Scanline)
            {
                // Pre-render scanline
                case -1:
                    if (Cycle == 1)
                    {
                        Registers.PPUSTATUS = 0;
                    }
                    break;
                // Visible scanlines
                case <= 239:
                    break;
                // Post-render scanline
                case 240:
                    // Idle scanline
                    break;
                // Vertical blanking start
                case 241:
                    if (Cycle == 1)
                    {
                        Registers.PPUSTATUS |= PPUSTATUSFlags.VerticalBlank;
                        NesSystem.CpuCore.NonMaskableInterrupt();
                    }
                    break;
                // Vertical blanking interval
                default:
                    break;
            }
        }
    }
}
