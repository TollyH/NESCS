namespace NESCS
{
    public class PPU
    {
        public readonly PPURegisters Registers = new();

        public bool OddFrame { get; private set; } = false;

        public PPU()
        {
            Reset(true);
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
