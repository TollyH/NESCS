namespace NESCS
{
    public enum AddressingMode
    {
        // Indexed addressing
        ZeroPageXIndex,  // ONE-BYTE[(operand + X) & 0xFF], operand = 1 byte
        ZeroPageYIndex,  // ONE-BYTE[(operand + Y) & 0xFF], operand = 1 byte
        AbsoluteXIndexed,  // ONE-BYTE[operand + X], operand = 2 bytes
        AbsoluteYIndexed,  // ONE-BYTE[operand + Y], operand = 2 bytes
        IndexedIndirect,  // ONE-BYTE[TWO-BYTE[(operand + X) & 0xFF]], operand = 1 byte
        IndirectIndexed,  // ONE-BYTE[TWO-BYTE[operand] + Y], operand = 1 byte

        // Other addressing
        Accumulator,  // operand = 0 bytes
        Immediate,  // operand = 1 bytes
        ZeroPage,  // operand = 1 byte
        Absolute,  // operand = 2 bytes
        Relative,  // operand = 1 byte
        Indirect,  // operand = 2 bytes
        Implicit  // operand = 0 bytes
    }

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
            byte addressingModeCode = (byte)((opcode >> 2) & 0b111);
            byte instructionCode = (byte)(opcode >> 5);

            AddressingMode addressingMode = GetAddressingMode(instructionGroup, addressingModeCode, instructionCode);

            return 0;
        }

        public static AddressingMode GetAddressingMode(byte instructionGroup, byte addressingModeCode, byte instructionCode)
        {
            return instructionGroup switch
            {
                // Group 1
                0b01 => addressingModeCode switch
                {
                    0b000 => AddressingMode.IndexedIndirect,
                    0b001 => AddressingMode.ZeroPage,
                    0b010 => AddressingMode.Immediate,
                    0b011 => AddressingMode.Absolute,
                    0b100 => AddressingMode.IndirectIndexed,
                    0b101 => AddressingMode.ZeroPageXIndex,
                    0b110 => AddressingMode.AbsoluteYIndexed,
                    0b111 => AddressingMode.AbsoluteXIndexed,
                    _ => throw new Exception()
                },
                // Group 2
                0b10 => addressingModeCode switch
                {
                    0b000 => AddressingMode.Immediate,
                    0b001 => AddressingMode.ZeroPage,
                    0b010 => AddressingMode.Accumulator,
                    0b011 => AddressingMode.Absolute,
                    0b100 => AddressingMode.Implicit,  // UNOFFICIAL
                    0b101 when instructionCode is 0b100 or 0b101 => AddressingMode.ZeroPageYIndex,  // STX or LDX
                    0b101 => AddressingMode.ZeroPageXIndex,
                    0b110 => AddressingMode.Implicit,
                    0b111 when instructionCode == 0b101 => AddressingMode.AbsoluteYIndexed,  // LDX
                    0b111 => AddressingMode.AbsoluteXIndexed,
                    _ => throw new Exception()
                },
                // Group 3
                0b00 => addressingModeCode switch
                {
                    0b000 when instructionCode == 0b001 => AddressingMode.Absolute,
                    0b000 when instructionCode <= 0b011 => AddressingMode.Implicit,
                    0b000 => AddressingMode.Immediate,
                    0b001 => AddressingMode.ZeroPage,
                    0b010 => AddressingMode.Implicit,
                    0b011 when instructionCode == 0b011 => AddressingMode.Indirect,  // Second of 2 JMP instructions
                    0b011 => AddressingMode.Absolute,
                    0b100 => AddressingMode.Relative,  // Conditional Branches
                    0b101 => AddressingMode.ZeroPageXIndex,
                    0b110 => AddressingMode.Implicit,
                    0b111 => AddressingMode.AbsoluteXIndexed,
                    _ => throw new Exception()
                },
                _ => throw new Exception()
            };
        }
    }
}
