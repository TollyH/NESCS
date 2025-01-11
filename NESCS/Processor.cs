namespace NESCS
{
    public enum AddressingMode
    {
        // Indexed addressing
        ZeroPageXIndexed,  // ONE-BYTE[(operand + X) & 0xFF], operand = 1 byte
        ZeroPageYIndexed,  // ONE-BYTE[(operand + Y) & 0xFF], operand = 1 byte
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
        public const byte SignBit = unchecked((byte)sbyte.MinValue);

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

            switch (instructionGroup)
            {
                // Group One
                case 0b01:
                    switch (instructionCode)
                    {
                        // ORA
                        case 0b000:
                            CpuRegisters.A |= ReadOperand(addressingMode);
                            SetZNFlagsFromAccumulator();
                            break;
                        // AND
                        case 0b001:
                            CpuRegisters.A &= ReadOperand(addressingMode);
                            SetZNFlagsFromAccumulator();
                            break;
                        // EOR
                        case 0b010:
                            CpuRegisters.A ^= ReadOperand(addressingMode);
                            SetZNFlagsFromAccumulator();
                            break;
                        // ADC
                        case 0b011:
                            byte initialValue = CpuRegisters.A;
                            byte operand = ReadOperand(addressingMode);
                            CpuRegisters.A += (byte)(operand + (byte)(CpuRegisters.P & StatusFlags.Carry));

                            SetZNFlagsFromAccumulator();

                            if (CpuRegisters.A < initialValue)
                            {
                                CpuRegisters.P |= StatusFlags.Carry;
                            }
                            else
                            {
                                CpuRegisters.P &= ~StatusFlags.Carry;
                            }

                            int initialSign = initialValue & SignBit;
                            if (initialSign == (operand & SignBit) && initialSign != (CpuRegisters.A & SignBit))
                            {
                                CpuRegisters.P &= ~StatusFlags.Overflow;
                            }
                            else
                            {
                                CpuRegisters.P |= StatusFlags.Overflow;
                            }
                            break;
                        // STA
                        case 0b100:
                            WriteOperand(addressingMode, CpuRegisters.A);
                            break;
                        // LDA
                        case 0b101:
                            CpuRegisters.A = ReadOperand(addressingMode);
                            SetZNFlagsFromAccumulator();
                            break;
                        // CMP
                        case 0b110:
                            initialValue = CpuRegisters.A;
                            byte result = (byte)(CpuRegisters.A - ReadOperand(addressingMode));

                            SetZNFlagsFromValue(result);

                            if (result > initialValue)
                            {
                                CpuRegisters.P |= StatusFlags.Carry;
                            }
                            else
                            {
                                CpuRegisters.P &= ~StatusFlags.Carry;
                            }
                            break;
                        // SBC
                        case 0b111:
                            initialValue = CpuRegisters.A;
                            operand = ReadOperand(addressingMode);
                            CpuRegisters.A -= (byte)(operand - (byte)(CpuRegisters.P & StatusFlags.Carry));

                            SetZNFlagsFromAccumulator();

                            if (CpuRegisters.A > initialValue)
                            {
                                CpuRegisters.P |= StatusFlags.Carry;
                            }
                            else
                            {
                                CpuRegisters.P &= ~StatusFlags.Carry;
                            }

                            initialSign = initialValue & SignBit;
                            if (initialSign != (operand & SignBit) && initialSign != (CpuRegisters.A & SignBit))
                            {
                                CpuRegisters.P &= ~StatusFlags.Overflow;
                            }
                            else
                            {
                                CpuRegisters.P |= StatusFlags.Overflow;
                            }
                            break;
                    }
                    break;
            }

            return 0;
        }

        /// <summary>
        /// Read an instruction operand for the given addressing mode at the current PC register value.
        /// PC will be automatically incremented to the start of the next instruction.
        /// </summary>
        /// <returns>
        /// One of the following:
        /// <list type="bullet">
        ///     <item>The value at the calculated memory address for addressing modes that read from memory.</item>
        ///     <item>The immediate value for the Immediate addressing mode.</item>
        /// </list>
        /// </returns>
        public byte ReadOperand(AddressingMode mode)
        {
            return mode switch
            {
                AddressingMode.Immediate => SystemMemory[CpuRegisters.PC++],
                AddressingMode.ZeroPage => SystemMemory[SystemMemory[CpuRegisters.PC++]],
                AddressingMode.ZeroPageXIndexed => SystemMemory[(byte)(SystemMemory[CpuRegisters.PC++] + CpuRegisters.X)],
                AddressingMode.ZeroPageYIndexed => SystemMemory[(byte)(SystemMemory[CpuRegisters.PC++] + CpuRegisters.Y)],
                AddressingMode.Relative => SystemMemory[(ushort)(SystemMemory[CpuRegisters.PC++] + CpuRegisters.PC)],
                AddressingMode.Absolute => SystemMemory[SystemMemory.ReadTwoBytes(CpuRegisters.PC.PostIncrementTwo())],
                AddressingMode.AbsoluteXIndexed => SystemMemory[(ushort)(SystemMemory.ReadTwoBytes(CpuRegisters.PC.PostIncrementTwo()) + CpuRegisters.X)],
                AddressingMode.AbsoluteYIndexed => SystemMemory[(ushort)(SystemMemory.ReadTwoBytes(CpuRegisters.PC.PostIncrementTwo()) + CpuRegisters.Y)],
                AddressingMode.Indirect => SystemMemory[SystemMemory.ReadTwoBytes(SystemMemory.ReadTwoBytes(CpuRegisters.PC.PostIncrementTwo()))],
                AddressingMode.IndexedIndirect => SystemMemory[SystemMemory.ReadTwoBytes((byte)(SystemMemory[CpuRegisters.PC++] + CpuRegisters.X))],
                AddressingMode.IndirectIndexed => SystemMemory[(ushort)(SystemMemory.ReadTwoBytes(SystemMemory[CpuRegisters.PC++]) + CpuRegisters.Y)],
                _ => throw new ArgumentException($"The given AddressingMode ({mode}) does not have an operand.", nameof(mode))
            };
        }

        /// <summary>
        /// Write to an instruction operand for the given addressing mode at the current PC register value.
        /// PC will be automatically incremented to the start of the next instruction.
        /// </summary>
        public void WriteOperand(AddressingMode mode, byte value)
        {
            switch (mode)
            {
                case AddressingMode.ZeroPage:
                    SystemMemory[SystemMemory[CpuRegisters.PC++]] = value;
                    break;
                case AddressingMode.ZeroPageXIndexed:
                    SystemMemory[(byte)(SystemMemory[CpuRegisters.PC++] + CpuRegisters.X)] = value;
                    break;
                case AddressingMode.ZeroPageYIndexed:
                    SystemMemory[(byte)(SystemMemory[CpuRegisters.PC++] + CpuRegisters.Y)] = value;
                    break;
                case AddressingMode.Relative:
                    SystemMemory[(ushort)(SystemMemory[CpuRegisters.PC++] + CpuRegisters.PC)] = value;
                    break;
                case AddressingMode.Absolute:
                    SystemMemory[SystemMemory.ReadTwoBytes(CpuRegisters.PC.PostIncrementTwo())] = value;
                    break;
                case AddressingMode.AbsoluteXIndexed:
                    SystemMemory[(ushort)(SystemMemory.ReadTwoBytes(CpuRegisters.PC.PostIncrementTwo()) + CpuRegisters.X)] = value;
                    break;
                case AddressingMode.AbsoluteYIndexed:
                    SystemMemory[(ushort)(SystemMemory.ReadTwoBytes(CpuRegisters.PC.PostIncrementTwo()) + CpuRegisters.Y)] = value;
                    break;
                case AddressingMode.Indirect:
                    SystemMemory[SystemMemory.ReadTwoBytes(SystemMemory.ReadTwoBytes(CpuRegisters.PC.PostIncrementTwo()))] = value;
                    break;
                case AddressingMode.IndexedIndirect:
                    SystemMemory[SystemMemory.ReadTwoBytes((byte)(SystemMemory[CpuRegisters.PC++] + CpuRegisters.X))] = value;
                    break;
                case AddressingMode.IndirectIndexed:
                    SystemMemory[(ushort)(SystemMemory.ReadTwoBytes(SystemMemory[CpuRegisters.PC++]) + CpuRegisters.Y)] = value;
                    break;
                default:
                    throw new ArgumentException($"The given AddressingMode ({mode}) does not have a writeable operand.", nameof(mode));
            }
        }

        private void SetZNFlagsFromValue(byte value)
        {
            if (value == 0)
            {
                CpuRegisters.P |= StatusFlags.Zero;
            }
            else
            {
                CpuRegisters.P &= ~StatusFlags.Zero;
            }

            if ((value & SignBit) == 0)
            {
                CpuRegisters.P &= ~StatusFlags.Negative;
            }
            else
            {
                CpuRegisters.P |= StatusFlags.Negative;
            }
        }

        private void SetZNFlagsFromAccumulator()
        {
            SetZNFlagsFromValue(CpuRegisters.A);
        }

        public static AddressingMode GetAddressingMode(byte instructionGroup, byte addressingModeCode, byte instructionCode)
        {
            return instructionGroup switch
            {
                // Group One
                0b01 => addressingModeCode switch
                {
                    0b000 => AddressingMode.IndexedIndirect,
                    0b001 => AddressingMode.ZeroPage,
                    0b010 => AddressingMode.Immediate,
                    0b011 => AddressingMode.Absolute,
                    0b100 => AddressingMode.IndirectIndexed,
                    0b101 => AddressingMode.ZeroPageXIndexed,
                    0b110 => AddressingMode.AbsoluteYIndexed,
                    0b111 => AddressingMode.AbsoluteXIndexed,
                    _ => throw new Exception()
                },
                // Group Two
                0b10 => addressingModeCode switch
                {
                    0b000 => AddressingMode.Immediate,
                    0b001 => AddressingMode.ZeroPage,
                    0b010 => AddressingMode.Accumulator,
                    0b011 => AddressingMode.Absolute,
                    0b100 => AddressingMode.Implicit,  // UNOFFICIAL
                    0b101 when instructionCode is 0b100 or 0b101 => AddressingMode.ZeroPageYIndexed,  // STX or LDX
                    0b101 => AddressingMode.ZeroPageXIndexed,
                    0b110 => AddressingMode.Implicit,
                    0b111 when instructionCode == 0b101 => AddressingMode.AbsoluteYIndexed,  // LDX
                    0b111 => AddressingMode.AbsoluteXIndexed,
                    _ => throw new Exception()
                },
                // Group Three
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
                    0b101 => AddressingMode.ZeroPageXIndexed,
                    0b110 => AddressingMode.Implicit,
                    0b111 => AddressingMode.AbsoluteXIndexed,
                    _ => throw new Exception()
                },
                _ => throw new Exception()
            };
        }
    }
}
