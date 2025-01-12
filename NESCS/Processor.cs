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

        public bool Halted { get; private set; } = false;

        public readonly Registers CpuRegisters = new();
        public readonly Memory SystemMemory = new();

        /// <summary>
        /// Read and execute a single instruction from memory based on the current PC register state.
        /// PC will be automatically incremented to the start of the next instruction.
        /// </summary>
        /// <returns>
        /// The number of CPU clock cycles the instruction would have taken to execute on real hardware.
        /// A value of 0 means that an illegal opcode causing the CPU to crash was executed.
        /// </returns>
        public int InterpretNextInstruction()
        {
            byte opcode = SystemMemory.InternalRAM[CpuRegisters.PC++];

            byte instructionGroup = (byte)(opcode & 0b11);
            byte addressingModeCode = (byte)((opcode >> 2) & 0b111);
            byte instructionCode = (byte)(opcode >> 5);

            AddressingMode addressingMode = GetAddressingMode(instructionGroup, addressingModeCode, instructionCode);

            bool cancelPCIncrement = false;

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
                // Group Two
                case 0b10:
                    if (addressingModeCode == 0b100 || (addressingModeCode == 0b000 && instructionCode <= 0b011))
                    {
                        // UNOFFICIAL - Crash/halt the processor (often referred to as STP/KIL/JAM/HLT)
                        Halted = true;
                        return 0;
                    }

                    switch (instructionCode)
                    {
                        // ASL
                        case 0b000:
                            byte initialValue = ReadOperand(addressingMode);
                            byte result = (byte)(initialValue << 1);
                            WriteOperand(addressingMode, result);

                            SetZNFlagsFromValue(result);
                            if ((initialValue & SignBit) == 0)
                            {
                                CpuRegisters.P &= StatusFlags.Carry;
                            }
                            else
                            {
                                CpuRegisters.P |= ~StatusFlags.Carry;
                            }
                            break;
                        // ROL
                        case 0b001:
                            initialValue = ReadOperand(addressingMode);
                            result = (byte)((initialValue << 1) | (int)(CpuRegisters.P & StatusFlags.Carry));
                            WriteOperand(addressingMode, result);

                            SetZNFlagsFromValue(result);
                            if ((initialValue & SignBit) == 0)
                            {
                                CpuRegisters.P &= StatusFlags.Carry;
                            }
                            else
                            {
                                CpuRegisters.P |= ~StatusFlags.Carry;
                            }
                            break;
                        // LSR
                        case 0b010:
                            initialValue = ReadOperand(addressingMode);
                            result = (byte)(initialValue >> 1);
                            WriteOperand(addressingMode, result);

                            SetZNFlagsFromValue(result);
                            if ((initialValue & 1) == 0)
                            {
                                CpuRegisters.P &= StatusFlags.Carry;
                            }
                            else
                            {
                                CpuRegisters.P |= ~StatusFlags.Carry;
                            }
                            break;
                        // ROR
                        case 0b011:
                            initialValue = ReadOperand(addressingMode);
                            result = (byte)((initialValue >> 1) | ((int)(CpuRegisters.P & StatusFlags.Carry) << 7));
                            WriteOperand(addressingMode, result);

                            SetZNFlagsFromValue(result);
                            if ((initialValue & 1) == 0)
                            {
                                CpuRegisters.P &= StatusFlags.Carry;
                            }
                            else
                            {
                                CpuRegisters.P |= ~StatusFlags.Carry;
                            }
                            break;
                        // STX
                        // TXA
                        // TXS
                        // UNOFFICIAL - SHX
                        case 0b100:
                            if (addressingMode == AddressingMode.Implicit)
                            {
                                // TXS
                                CpuRegisters.S = CpuRegisters.X;
                            }
                            else
                            {
                                WriteOperand(addressingMode, addressingMode == AddressingMode.AbsoluteYIndexed
                                    ? (byte)(CpuRegisters.X & (GetAddressFromOperand(addressingMode) >> 8))  // UNOFFICIAL - SHX
                                    : CpuRegisters.X);  // STX & TXA
                            }
                            break;
                        // LDX
                        // TAX
                        // TSX
                        case 0b101:
                            CpuRegisters.X = addressingMode == AddressingMode.Implicit
                                ? CpuRegisters.S  // TSX
                                : ReadOperand(addressingMode);  // LDX & TAX

                            SetZNFlagsFromValue(CpuRegisters.X);
                            break;
                        // DEC
                        // DEX
                        case 0b110:
                            if (addressingModeCode == 0b010)
                            {
                                // DEX
                                result = --CpuRegisters.X;
                            }
                            else
                            {
                                // DEC
                                result = (byte)(ReadOperand(addressingMode) - 1);
                                WriteOperand(addressingMode, result);
                            }

                            SetZNFlagsFromValue(result);
                            break;
                        // INC
                        // NOP
                        case 0b111:
                            if (addressingMode == AddressingMode.Implicit)
                            {
                                // NOP
                                break;
                            }

                            // INC
                            result = (byte)(ReadOperand(addressingMode) + 1);
                            WriteOperand(addressingMode, result);

                            SetZNFlagsFromValue(result);
                            break;
                    }
                    break;
                // Group Three
                case 0b00:
                    if (addressingMode == AddressingMode.Relative)
                    {
                        // Conditional branch instructions (BPL, BMI, BVC, BVS, BCC, BCS, BNE, BEQ)
                        StatusFlags flagToCheck = (instructionCode & 0b110) switch
                        {
                            0b000 => StatusFlags.Negative,
                            0b010 => StatusFlags.Overflow,
                            0b100 => StatusFlags.Carry,
                            0b110 => StatusFlags.Zero,
                            _ => throw new Exception()
                        };
                        bool invert = (instructionCode & 1) == 0;

                        if (((CpuRegisters.P & flagToCheck) != 0) ^ invert)
                        {
                            CpuRegisters.PC = GetAddressFromOperand(addressingMode);
                            cancelPCIncrement = true;
                        }

                        break;
                    }

                    if (addressingModeCode == 0b010)
                    {
                        switch (instructionCode)
                        {
                            // PHP
                            case 0b000:
                                CpuRegisters.P |= StatusFlags.Break;
                                PushStack((byte)CpuRegisters.P);
                                break;
                            // PLP
                            case 0b001:
                                CpuRegisters.P = ((StatusFlags)PopStack() | StatusFlags.Always) & ~StatusFlags.Break;
                                break;
                            // PHA
                            case 0b010:
                                PushStack(CpuRegisters.A);
                                break;
                            // PLA
                            case 0b011:
                                CpuRegisters.A = PopStack();
                                SetZNFlagsFromAccumulator();
                                break;
                            // DEY
                            case 0b100:
                                SetZNFlagsFromValue(--CpuRegisters.Y);
                                break;
                            // TAY
                            case 0b101:
                                CpuRegisters.Y = CpuRegisters.A;
                                SetZNFlagsFromAccumulator();
                                break;
                            // INY
                            case 0b110:
                                SetZNFlagsFromValue(++CpuRegisters.Y);
                                break;
                            // INX
                            case 0b111:
                                SetZNFlagsFromValue(++CpuRegisters.X);
                                break;
                        }

                        break;
                    }

                    if (addressingModeCode == 0b110)
                    {
                        if (instructionCode == 0b100)
                        {
                            // TYA
                            CpuRegisters.A = CpuRegisters.Y;
                            SetZNFlagsFromAccumulator();
                            break;
                        }

                        // Manual flag change (CLC, SEC, CLI, SEI, CLV, CLD, SED)
                        StatusFlags flagToChange = (instructionCode & 0b110) switch
                        {
                            0b000 => StatusFlags.Carry,
                            0b010 => StatusFlags.InterruptDisable,
                            0b100 => StatusFlags.Overflow,
                            0b110 => StatusFlags.Decimal,
                            _ => throw new Exception()
                        };
                        bool clear = (instructionCode & 1) == 0;

                        if (clear)
                        {
                            CpuRegisters.P &= ~flagToChange;
                        }
                        else
                        {
                            CpuRegisters.P |= flagToChange;
                        }

                        break;
                    }

                    switch (instructionCode)
                    {
                        // BRK
                        case 0b000:
                            if (addressingModeCode == 0b000)
                            {
                                PushStackTwoByte((ushort)(CpuRegisters.PC + 1));
                                CpuRegisters.P |= StatusFlags.Break;
                                PushStack((byte)CpuRegisters.P);
                                CpuRegisters.P |= StatusFlags.InterruptDisable;
                                CpuRegisters.PC = 0xFFFE;

                                cancelPCIncrement = true;
                            }
                            break;
                        // JSR
                        // BIT
                        case 0b001:
                            if (addressingModeCode == 0b000)
                            {
                                // JSR
                                PushStackTwoByte((ushort)(CpuRegisters.PC + 1));
                                break;
                            }

                            // BIT
                            byte result = (byte)(CpuRegisters.A & ReadOperand(addressingMode));

                            SetZNFlagsFromValue(result);

                            if ((result & 0b01000000) == 0)
                            {
                                CpuRegisters.P &= ~StatusFlags.Overflow;
                            }
                            else
                            {
                                CpuRegisters.P |= StatusFlags.Overflow;
                            }
                            break;
                        // RTI
                        // RTS
                        // JMP
                        case 0b010:
                        case 0b011:
                            if (addressingModeCode == 0b000)
                            {
                                // RTI & RTS
                                if (instructionCode == 0b010)
                                {
                                    // RTI
                                    CpuRegisters.P = ((StatusFlags)PopStack() | StatusFlags.Always) & ~StatusFlags.Break;
                                }

                                CpuRegisters.PC = PopStackTwoByte();

                                if (instructionCode == 0b011)
                                {
                                    // RTS
                                    CpuRegisters.PC++;
                                }
                            }
                            else if (addressingModeCode == 0b011)
                            {
                                // JMP
                                CpuRegisters.PC = GetAddressFromOperand(addressingMode);
                                cancelPCIncrement = true;
                            }
                            break;
                        // STY
                        // UNOFFICIAL - SHY
                        case 0b100:
                            WriteOperand(addressingMode, addressingMode == AddressingMode.AbsoluteXIndexed
                                ? (byte)(CpuRegisters.Y & (GetAddressFromOperand(addressingMode) >> 8))  // UNOFFICIAL - SHY
                                : CpuRegisters.Y);  // STY
                            break;
                        // LDY
                        case 0b101:
                            CpuRegisters.Y = ReadOperand(addressingMode);
                            SetZNFlagsFromValue(CpuRegisters.Y);
                            break;
                        // CPY
                        case 0b110:
                            byte initialValue = CpuRegisters.Y;
                            result = (byte)(CpuRegisters.Y - ReadOperand(addressingMode));

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
                        // CPX
                        case 0b111:
                            initialValue = CpuRegisters.X;
                            result = (byte)(CpuRegisters.X - ReadOperand(addressingMode));

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
                    }
                    break;
            }

            if (!cancelPCIncrement)
            {
                IncrementPCPastOperand(addressingMode);
            }

            return 0;
        }

        /// <summary>
        /// Get the address that an instruction operand will operate on for the given addressing mode at the current PC register value.
        /// </summary>
        private ushort GetAddressFromOperand(AddressingMode mode)
        {
            return mode switch
            {
                AddressingMode.ZeroPage => SystemMemory[CpuRegisters.PC],
                AddressingMode.ZeroPageXIndexed => (byte)(SystemMemory[CpuRegisters.PC] + CpuRegisters.X),
                AddressingMode.ZeroPageYIndexed => (byte)(SystemMemory[CpuRegisters.PC] + CpuRegisters.Y),
                AddressingMode.Relative => (ushort)(SystemMemory[CpuRegisters.PC] + CpuRegisters.PC + 2),
                AddressingMode.Absolute => SystemMemory.ReadTwoBytes(CpuRegisters.PC),
                AddressingMode.AbsoluteXIndexed => (ushort)(SystemMemory.ReadTwoBytes(CpuRegisters.PC) + CpuRegisters.X),
                AddressingMode.AbsoluteYIndexed => (ushort)(SystemMemory.ReadTwoBytes(CpuRegisters.PC) + CpuRegisters.Y),
                AddressingMode.Indirect => SystemMemory.ReadTwoBytesIndirectBug(SystemMemory.ReadTwoBytes(CpuRegisters.PC)),
                AddressingMode.IndexedIndirect => SystemMemory.ReadTwoBytes((byte)(SystemMemory[CpuRegisters.PC] + CpuRegisters.X)),
                AddressingMode.IndirectIndexed => (ushort)(SystemMemory.ReadTwoBytes(SystemMemory[CpuRegisters.PC]) + CpuRegisters.Y),
                _ => throw new ArgumentException($"The given AddressingMode ({mode}) does not operate on memory.", nameof(mode))
            };
        }

        /// <summary>
        /// Read an instruction operand for the given addressing mode at the current PC register value.
        /// </summary>
        /// <returns>
        /// One of the following:
        /// <list type="bullet">
        ///     <item>The value at the calculated memory address for addressing modes that read from memory.</item>
        ///     <item>The immediate value for the Immediate addressing mode.</item>
        /// </list>
        /// </returns>
        private byte ReadOperand(AddressingMode mode)
        {
            return mode switch
            {
                AddressingMode.Immediate => SystemMemory[CpuRegisters.PC],
                AddressingMode.Accumulator => CpuRegisters.A,
                AddressingMode.ZeroPage
                    or AddressingMode.ZeroPageXIndexed
                    or AddressingMode.ZeroPageYIndexed
                    or AddressingMode.Relative
                    or AddressingMode.Absolute
                    or AddressingMode.AbsoluteXIndexed
                    or AddressingMode.AbsoluteYIndexed
                    or AddressingMode.Indirect
                    or AddressingMode.IndexedIndirect
                    or AddressingMode.IndirectIndexed
                    => SystemMemory[GetAddressFromOperand(mode)],
                AddressingMode.Implicit => 0,  // UNOFFICIAL - Reading implicit operand is a NOP
                _ => throw new ArgumentException($"The given AddressingMode ({mode}) does not have an operand.", nameof(mode))
            };
        }

        /// <summary>
        /// Write to an instruction operand for the given addressing mode at the current PC register value.
        /// </summary>
        private void WriteOperand(AddressingMode mode, byte value)
        {
            switch (mode)
            {
                case AddressingMode.Accumulator:
                    CpuRegisters.A = value;
                    break;
                case AddressingMode.ZeroPage:
                case AddressingMode.ZeroPageXIndexed:
                case AddressingMode.ZeroPageYIndexed:
                case AddressingMode.Relative:
                case AddressingMode.Absolute:
                case AddressingMode.AbsoluteXIndexed:
                case AddressingMode.AbsoluteYIndexed:
                case AddressingMode.Indirect:
                case AddressingMode.IndexedIndirect:
                case AddressingMode.IndirectIndexed:
                    SystemMemory[GetAddressFromOperand(mode)] = value;
                    break;
                case AddressingMode.Immediate:
                    // UNOFFICIAL - Writing to an immediate operand is a NOP
                    break;
                default:
                    throw new ArgumentException($"The given AddressingMode ({mode}) does not have an operand.", nameof(mode));
            }
        }

        private byte PopStack()
        {
            return SystemMemory[(ushort)(0x0100 + CpuRegisters.S++)];
        }

        private void PushStack(byte value)
        {
            SystemMemory[(ushort)(0x0100 + --CpuRegisters.S)] = value;
        }

        private ushort PopStackTwoByte()
        {
            return (ushort)((SystemMemory[(ushort)(0x0100 + CpuRegisters.S++)] << 8)
                & (SystemMemory[(ushort)(0x0100 + CpuRegisters.S++)]));
        }

        private void PushStackTwoByte(ushort value)
        {
            SystemMemory[(ushort)(0x0100 + --CpuRegisters.S)] = (byte)(value >> 8);
            SystemMemory[(ushort)(0x0100 + --CpuRegisters.S)] = (byte)(value & 0xFF);
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

        private void IncrementPCPastOperand(AddressingMode mode)
        {
            CpuRegisters.PC += mode switch
            {
                AddressingMode.ZeroPageXIndexed => 1,
                AddressingMode.ZeroPageYIndexed => 1,
                AddressingMode.AbsoluteXIndexed => 2,
                AddressingMode.AbsoluteYIndexed => 2,
                AddressingMode.IndexedIndirect => 1,
                AddressingMode.IndirectIndexed => 1,
                AddressingMode.Accumulator => 0,
                AddressingMode.Immediate => 1,
                AddressingMode.ZeroPage => 1,
                AddressingMode.Absolute => 2,
                AddressingMode.Relative => 2,
                AddressingMode.Indirect => 2,
                AddressingMode.Implicit => 0,
                _ => 0
            };
        }

        private static AddressingMode GetAddressingMode(byte instructionGroup, byte addressingModeCode, byte instructionCode)
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
                    0b010 when instructionCode >= 0b110 => AddressingMode.Implicit,
                    0b010 => AddressingMode.Accumulator,
                    0b011 => AddressingMode.Absolute,
                    0b100 => AddressingMode.Implicit,  // UNOFFICIAL
                    0b101 when instructionCode is 0b100 or 0b101 => AddressingMode.ZeroPageYIndexed,  // STX or LDX
                    0b101 => AddressingMode.ZeroPageXIndexed,
                    0b110 => AddressingMode.Implicit,
                    0b111 when instructionCode is 0b100 or 0b101 => AddressingMode.AbsoluteYIndexed,  // SHX (UNOFFICIAL) or LDX
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
