namespace NESCS
{
    public class PPU
    {
        public readonly PPURegisters Registers = new();
        public readonly Mapper InsertedCartridgeMapper;

        public readonly byte[] PaletteRAM = new byte[0x20];
        public readonly byte[] ObjectAttributeMemory = new byte[0xFF];

        public bool OddFrame { get; private set; } = false;

        public PPU(Mapper insertedCartridgeMapper)
        {
            InsertedCartridgeMapper = insertedCartridgeMapper;

            Reset(true);
        }

        /// <summary>
        /// Read/write a byte from the PPU's mapped memory.
        /// </summary>
        public byte this[ushort address]
        {
            get => address <= 0x3EFF
                ? InsertedCartridgeMapper.MappedPPURead(address)
                : PaletteRAM[address & 0b11111];
            set
            {
                if (address <= 0x3EFF)
                {
                    InsertedCartridgeMapper.MappedPPUWrite(address, value);
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
                Registers.PPUSTATUS = 0b10100000;
                Registers.OAMADDR = 0;
                Registers.PPUADDR = 0;
            }

            Registers.PPUCTRL = 0;
            Registers.PPUMASK = 0;
            Registers.PPUSCROLL = 0;
            Registers.PPUDATA = 0;

            Registers.W = false;

            OddFrame = false;
        }

        public void ProcessNextDot()
        {

        }
    }
}
