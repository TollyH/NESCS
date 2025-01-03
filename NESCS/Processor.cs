namespace NESCS
{
    public class Processor
    {
        public const long NtscClockTicks = 1789773 * TimeSpan.TicksPerSecond;
        public const long PalClockTicks = 1662607 * TimeSpan.TicksPerSecond;

        public TimeSpan CurrentClock { get; set; } = TimeSpan.FromTicks(NtscClockTicks);

        public readonly Registers CpuRegisters = new();
        public readonly Memory SystemMemory = new();

        /// <summary>
        /// Read and execute a single instruction from memory based on the current PC register state.
        /// PC will be automatically incremented to the start of the next instruction.
        /// </summary>
        /// <returns>
        /// The number of CPU clock cycles the instruction would have taken to execute on real hardware.
        /// </returns>
        public int InterpretNextInstruction()
        {
            byte opcode = SystemMemory.InternalRAM[CpuRegisters.PC++];

            byte instructionGroup = (byte)(opcode & 0b11);
            byte addressingMode = (byte)((opcode >> 2) & 0b111);
            byte instructionCode = (byte)(opcode >> 5);

            switch (instructionCode)
            {
                case 0b01:
                    // Group 1
                    break;
                case 0b10:
                    // Group 2
                    break;
                case 0b00:
                    // Group 3
                    break;
            }
        }
    }
}
