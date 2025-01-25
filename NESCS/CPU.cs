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

    public class CPU
    {
        public const byte SignBit = unchecked((byte)sbyte.MinValue);

        public const ushort NonMaskableInterruptVector = 0xFFFA;
        public const ushort ResetVector = 0xFFFC;
        public const ushort InterruptRequestVector = 0xFFFE;

        public bool Halted { get; private set; } = false;

        public readonly CPURegisters Registers = new();
        public readonly Memory SystemMemory;

        private bool nmiQueued = false;
        private bool irqQueued = false;

        // The number of cycles to wait before executing the decoded instruction
        // - used to emulate instructions taking multiple clock cycles.
        private int waitingCycles = 0;

        // Decoded opcode data
        private bool fetchNextInstruction = true;
        private byte opcode;
        private byte instructionGroup;
        private byte addressingModeCode;
        private byte instructionCode;
        private AddressingMode addressingMode;

        // Used to prevent some UNOFFICIAL NOPs from affecting status flags
        private bool lockFlags = false;

        private readonly int[] baseCycleCounts = new int[256]
        {
         // 0  1  2  3  4  5  6  7  8  9  A  B  C  D  E  F
            7, 6, 0, 8, 3, 3, 5, 5, 3, 2, 2, 2, 4, 4, 6, 6,  // 00
            2, 5, 0, 8, 4, 4, 6, 6, 2, 4, 2, 7, 4, 4, 7, 7,  // 10
            6, 6, 0, 8, 3, 3, 5, 5, 4, 2, 2, 2, 4, 4, 6, 6,  // 20
            2, 5, 0, 8, 4, 4, 6, 6, 2, 4, 2, 7, 4, 4, 7, 7,  // 30
            6, 6, 0, 8, 3, 3, 5, 5, 3, 2, 2, 2, 3, 4, 6, 6,  // 40
            2, 5, 0, 8, 4, 4, 6, 6, 2, 4, 2, 7, 4, 4, 7, 7,  // 50
            6, 6, 0, 8, 3, 3, 5, 5, 4, 2, 2, 2, 5, 4, 6, 6,  // 60
            2, 5, 0, 8, 4, 4, 6, 6, 2, 4, 2, 7, 4, 4, 7, 7,  // 70
            2, 6, 2, 6, 3, 3, 3, 3, 2, 2, 2, 2, 4, 4, 4, 4,  // 80
            2, 6, 0, 6, 4, 4, 4, 4, 2, 5, 2, 5, 5, 5, 5, 5,  // 90
            2, 6, 2, 6, 3, 3, 3, 3, 2, 2, 2, 2, 4, 4, 4, 4,  // A0
            2, 5, 0, 5, 4, 4, 4, 4, 2, 4, 2, 4, 4, 4, 4, 4,  // B0
            2, 6, 2, 8, 3, 3, 5, 5, 2, 2, 2, 2, 4, 4, 6, 6,  // C0
            2, 5, 0, 8, 4, 4, 6, 6, 2, 4, 2, 7, 4, 4, 7, 7,  // D0
            2, 6, 2, 8, 3, 3, 5, 5, 2, 2, 2, 2, 4, 4, 6, 6,  // E0
            2, 5, 0, 8, 4, 4, 6, 6, 2, 4, 2, 7, 4, 4, 7, 7   // F0
        };

        private readonly bool[] incrementCyclesOnCarryOut = new bool[256]
        {
         // 0      1      2      3      4      5      6      7      8      9      A      B      C      D      E      F
            false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false,  // 00
            false, true , false, false, false, false, false, false, false, true , false, false, true , true , false, false,  // 10
            false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false,  // 20
            false, true , false, false, false, false, false, false, false, true , false, false, true , true , false, false,  // 30
            false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false,  // 40
            false, true , false, false, false, false, false, false, false, true , false, false, true , true , false, false,  // 50
            false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false,  // 60
            false, true , false, false, false, false, false, false, false, true , false, false, true , true , false, false,  // 70
            false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false,  // 80
            false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false,  // 90
            false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false,  // A0
            false, true , false, true , false, false, false, false, false, true , false, true , true , true , true , true ,  // B0
            false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false,  // C0
            false, true , false, false, false, false, false, false, false, true , false, false, true , true , false, false,  // D0
            false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false,  // E0
            false, true , false, false, false, false, false, false, false, true , false, false, true , true , false, false   // F0
        };

        public CPU(Memory systemMemory)
        {
            SystemMemory = systemMemory;

            Reset(true);
        }

        /// <summary>
        /// Simulate a reset of the processor, either via the reset line or via a power cycle.
        /// </summary>
        public void Reset(bool powerCycle)
        {
            if (powerCycle)
            {
                Registers.A = 0;
                Registers.X = 0;
                Registers.Y = 0;
                Registers.P = StatusFlags.Always;
            }

            Registers.P |= StatusFlags.InterruptDisable;

            Registers.PC = SystemMemory.ReadTwoBytes(ResetVector);

            if (powerCycle)
            {
                Registers.S = 0xFD;
            }
            else
            {
                InterruptStatePush();
            }
        }

        /// <summary>
        /// Execute a single CPU cycle.
        /// For multi-cycle instructions, the effect of the instruction will not be seen
        /// until this method has been executed enough times to clear all the cycles.
        /// </summary>
        public void ExecuteClockCycle()
        {
            if (Halted)
            {
                return;
            }

            if (fetchNextInstruction)
            {
                lockFlags = false;
                waitingCycles += ReadNextOpcode();
                fetchNextInstruction = false;
            }

            if (--waitingCycles == 0)
            {
                fetchNextInstruction = true;

                bool irqDisableSetBeforeExecute = (Registers.P & StatusFlags.InterruptDisable) != 0;

                ExecutePendingInstruction();

                if (nmiQueued)
                {
                    nmiQueued = false;

                    StartInterruptHandler(NonMaskableInterruptVector);
                }

                if (irqQueued)
                {
                    irqQueued = false;

                    // RTI changing the IRQ disable flag takes immediate effect, everything else waits for one instruction
                    bool irqDisabled = opcode == 0x40  // RTI
                        ? (Registers.P & StatusFlags.InterruptDisable) != 0
                        : irqDisableSetBeforeExecute;

                    if (irqDisabled)
                    {
                        return;
                    }

                    StartInterruptHandler(InterruptRequestVector);
                }
            }
        }

        public void NonMaskableInterrupt()
        {
            nmiQueued = true;
        }

        public void InterruptRequest()
        {
            irqQueued = true;
        }

        /// <summary>
        /// Read and decode the next opcode to execute from memory.
        /// The PC register will be incremented to the address of the first operand, or the next instruction if there are none.
        /// </summary>
        /// <returns>
        /// The number of cycles the instruction would take to execute on a real CPU.
        /// </returns>
        private int ReadNextOpcode()
        {
            opcode = SystemMemory[Registers.PC++];

            instructionGroup = (byte)(opcode & 0b11);
            addressingModeCode = (byte)((opcode >> 2) & 0b111);
            instructionCode = (byte)(opcode >> 5);

            addressingMode = GetAddressingMode(instructionGroup, addressingModeCode, instructionCode);

            int cycles = baseCycleCounts[opcode];
            if (incrementCyclesOnCarryOut[opcode] && OperandCausesCarryOut(addressingMode))
            {
                cycles++;
            }
            return cycles;
        }

        /// <summary>
        /// Execute the instruction for the last read opcode.
        /// The PC register will be automatically incremented to the start of the next instruction.
        /// </summary>
        private void ExecutePendingInstruction()
        {
            bool cancelPCIncrement = false;

            StatusFlags startFlags = Registers.P;

            switch (instructionGroup)
            {
                // Group One
                case 0b01:
                    switch (instructionCode)
                    {
                        // ORA
                        case 0b000:
                            Registers.A |= ReadOperand(addressingMode);
                            SetZNFlagsFromAccumulator();
                            break;
                        // AND
                        case 0b001:
                            Registers.A &= ReadOperand(addressingMode);
                            SetZNFlagsFromAccumulator();
                            break;
                        // EOR
                        case 0b010:
                            Registers.A ^= ReadOperand(addressingMode);
                            SetZNFlagsFromAccumulator();
                            break;
                        // ADC
                        case 0b011:
                            byte initialValue = Registers.A;
                            byte operand = ReadOperand(addressingMode);
                            Registers.A += (byte)(operand + (byte)(Registers.P & StatusFlags.Carry));

                            SetZNFlagsFromAccumulator();

                            if (Registers.A < initialValue)
                            {
                                Registers.P |= StatusFlags.Carry;
                            }
                            else
                            {
                                Registers.P &= ~StatusFlags.Carry;
                            }

                            int initialSign = initialValue & SignBit;
                            if (initialSign == (operand & SignBit) && initialSign != (Registers.A & SignBit))
                            {
                                Registers.P |= StatusFlags.Overflow;
                            }
                            else
                            {
                                Registers.P &= ~StatusFlags.Overflow;
                            }
                            break;
                        // STA
                        case 0b100:
                            WriteOperand(addressingMode, Registers.A);
                            break;
                        // LDA
                        case 0b101:
                            Registers.A = ReadOperand(addressingMode);
                            SetZNFlagsFromAccumulator();
                            break;
                        // CMP
                        case 0b110:
                            initialValue = Registers.A;
                            byte result = (byte)(Registers.A - ReadOperand(addressingMode));

                            SetZNFlagsFromValue(result);

                            if (result <= initialValue)
                            {
                                Registers.P |= StatusFlags.Carry;
                            }
                            else
                            {
                                Registers.P &= ~StatusFlags.Carry;
                            }
                            break;
                        // SBC
                        case 0b111:
                            initialValue = Registers.A;
                            operand = ReadOperand(addressingMode);
                            Registers.A -= (byte)(operand + (byte)((Registers.P & StatusFlags.Carry) ^ StatusFlags.Carry));

                            SetZNFlagsFromAccumulator();

                            if (Registers.A <= initialValue)
                            {
                                Registers.P |= StatusFlags.Carry;
                            }
                            else
                            {
                                Registers.P &= ~StatusFlags.Carry;
                            }

                            initialSign = initialValue & SignBit;
                            if (initialSign != (operand & SignBit) && initialSign != (Registers.A & SignBit))
                            {
                                Registers.P |= StatusFlags.Overflow;
                            }
                            else
                            {
                                Registers.P &= ~StatusFlags.Overflow;
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
                        return;
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
                                Registers.P &= ~StatusFlags.Carry;
                            }
                            else
                            {
                                Registers.P |= StatusFlags.Carry;
                            }
                            break;
                        // ROL
                        case 0b001:
                            initialValue = ReadOperand(addressingMode);
                            result = (byte)((initialValue << 1) | (int)(Registers.P & StatusFlags.Carry));
                            WriteOperand(addressingMode, result);

                            SetZNFlagsFromValue(result);
                            if ((initialValue & SignBit) == 0)
                            {
                                Registers.P &= ~StatusFlags.Carry;
                            }
                            else
                            {
                                Registers.P |= StatusFlags.Carry;
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
                                Registers.P &= ~StatusFlags.Carry;
                            }
                            else
                            {
                                Registers.P |= StatusFlags.Carry;
                            }
                            break;
                        // ROR
                        case 0b011:
                            initialValue = ReadOperand(addressingMode);
                            result = (byte)((initialValue >> 1) | ((int)(Registers.P & StatusFlags.Carry) << 7));
                            WriteOperand(addressingMode, result);

                            SetZNFlagsFromValue(result);
                            if ((initialValue & 1) == 0)
                            {
                                Registers.P &= ~StatusFlags.Carry;
                            }
                            else
                            {
                                Registers.P |= StatusFlags.Carry;
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
                                Registers.S = Registers.X;
                            }
                            else
                            {
                                WriteOperand(addressingMode, addressingMode == AddressingMode.AbsoluteYIndexed
                                    ? (byte)(Registers.X & (GetAddressFromOperand(addressingMode) >> 8))  // UNOFFICIAL - SHX
                                    : Registers.X);  // STX & TXA

                                if (addressingMode == AddressingMode.Accumulator)
                                {
                                    SetZNFlagsFromAccumulator();
                                }
                            }
                            break;
                        // LDX
                        // TAX
                        // TSX
                        case 0b101:
                            Registers.X = addressingMode == AddressingMode.Implicit
                                ? Registers.S  // TSX
                                : ReadOperand(addressingMode);  // LDX & TAX

                            SetZNFlagsFromValue(Registers.X);
                            break;
                        // DEC
                        // DEX
                        case 0b110:
                            if (addressingModeCode == 0b010)
                            {
                                // DEX
                                result = --Registers.X;
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

                        if (((Registers.P & flagToCheck) != 0) ^ invert)
                        {
                            ushort newAddress = GetAddressFromOperand(addressingMode);
                            // A branch being taken requires an additional clock cycle
                            waitingCycles++;
                            if (((Registers.PC + 1) & 0xFF00) != (newAddress & 0xFF00))
                            {
                                // Branching to a different page requires 2 additional cycles
                                waitingCycles++;
                            }

                            Registers.PC = newAddress;
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
                                PushStack((byte)(Registers.P | StatusFlags.Break));
                                break;
                            // PLP
                            case 0b001:
                                Registers.P = ((StatusFlags)PopStack() | StatusFlags.Always) & ~StatusFlags.Break;
                                break;
                            // PHA
                            case 0b010:
                                PushStack(Registers.A);
                                break;
                            // PLA
                            case 0b011:
                                Registers.A = PopStack();
                                SetZNFlagsFromAccumulator();
                                break;
                            // DEY
                            case 0b100:
                                SetZNFlagsFromValue(--Registers.Y);
                                break;
                            // TAY
                            case 0b101:
                                Registers.Y = Registers.A;
                                SetZNFlagsFromAccumulator();
                                break;
                            // INY
                            case 0b110:
                                SetZNFlagsFromValue(++Registers.Y);
                                break;
                            // INX
                            case 0b111:
                                SetZNFlagsFromValue(++Registers.X);
                                break;
                        }

                        break;
                    }

                    if (addressingModeCode == 0b110)
                    {
                        if (instructionCode == 0b100)
                        {
                            // TYA
                            Registers.A = Registers.Y;
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

                        if (clear || flagToChange == StatusFlags.Overflow)
                        {
                            Registers.P &= ~flagToChange;
                        }
                        else
                        {
                            Registers.P |= flagToChange;
                        }

                        break;
                    }

                    switch (instructionCode)
                    {
                        // BRK
                        case 0b000:
                            if (addressingModeCode == 0b000)
                            {
                                IncrementPCPastOperand(addressingMode);
                                StartInterruptHandler(InterruptRequestVector);
                                cancelPCIncrement = true;
                            }
                            break;
                        // JSR
                        // BIT
                        case 0b001:
                            if (addressingModeCode == 0b000)
                            {
                                // JSR
                                PushStackTwoByte((ushort)(Registers.PC + 1));
                                Registers.PC = GetAddressFromOperand(addressingMode);
                                cancelPCIncrement = true;
                                break;
                            }

                            if (addressingModeCode is 0b101 or 0b111)
                            {
                                // UNOFFICIAL - NOP
                                break;
                            }

                            // BIT
                            byte operand = ReadOperand(addressingMode);

                            if ((Registers.A & operand) == 0)
                            {
                                Registers.P |= StatusFlags.Zero;
                            }
                            else
                            {
                                Registers.P &= ~StatusFlags.Zero;
                            }

                            if ((operand & SignBit) == 0)
                            {
                                Registers.P &= ~StatusFlags.Negative;
                            }
                            else
                            {
                                Registers.P |= StatusFlags.Negative;
                            }

                            if ((operand & 0b01000000) == 0)
                            {
                                Registers.P &= ~StatusFlags.Overflow;
                            }
                            else
                            {
                                Registers.P |= StatusFlags.Overflow;
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
                                    Registers.P = ((StatusFlags)PopStack() | StatusFlags.Always) & ~StatusFlags.Break;
                                }

                                Registers.PC = PopStackTwoByte();

                                if (instructionCode == 0b011)
                                {
                                    // RTS
                                    Registers.PC++;
                                }
                            }
                            else if (addressingModeCode == 0b011)
                            {
                                // JMP
                                Registers.PC = GetAddressFromOperand(addressingMode);
                                cancelPCIncrement = true;
                            }
                            break;
                        // STY
                        // UNOFFICIAL - SHY
                        case 0b100:
                            WriteOperand(addressingMode, addressingMode == AddressingMode.AbsoluteXIndexed
                                ? (byte)(Registers.Y & (GetAddressFromOperand(addressingMode) >> 8))  // UNOFFICIAL - SHY
                                : Registers.Y);  // STY
                            break;
                        // LDY
                        case 0b101:
                            Registers.Y = ReadOperand(addressingMode);
                            SetZNFlagsFromValue(Registers.Y);
                            break;
                        // CPY
                        case 0b110:
                            if (addressingModeCode is 0b101 or 0b111)
                            {
                                // UNOFFICIAL - NOP
                                break;
                            }

                            byte initialValue = Registers.Y;
                            byte result = (byte)(Registers.Y - ReadOperand(addressingMode));

                            SetZNFlagsFromValue(result);

                            if (result <= initialValue)
                            {
                                Registers.P |= StatusFlags.Carry;
                            }
                            else
                            {
                                Registers.P &= ~StatusFlags.Carry;
                            }
                            break;
                        // CPX
                        case 0b111:
                            if (addressingModeCode is 0b101 or 0b111)
                            {
                                // UNOFFICIAL - NOP
                                break;
                            }

                            initialValue = Registers.X;
                            result = (byte)(Registers.X - ReadOperand(addressingMode));

                            SetZNFlagsFromValue(result);

                            if (result <= initialValue)
                            {
                                Registers.P |= StatusFlags.Carry;
                            }
                            else
                            {
                                Registers.P &= ~StatusFlags.Carry;
                            }
                            break;
                    }
                    break;
                // UNOFFICIAL Group
                case 0b11:
                    // ANC
                    // ALR
                    // ARR
                    // XAA
                    // LAX
                    // AXS
                    // SBC
                    // (Addressing Mode Code == 0b010)
                    if (addressingMode == AddressingMode.Immediate)
                    {
                        switch (instructionCode)
                        {
                            // ANC
                            case 0b000:
                            case 0b001:
                                Registers.A &= ReadOperand(addressingMode);
                                SetZNFlagsFromAccumulator();

                                if ((Registers.P & StatusFlags.Negative) == 0)
                                {
                                    Registers.P &= ~StatusFlags.Carry;
                                }
                                else
                                {
                                    Registers.P |= StatusFlags.Carry;
                                }
                                break;
                            // ALR
                            case 0b010:
                                Registers.A &= ReadOperand(addressingMode);

                                byte initialValue = Registers.A;
                                byte result = (byte)(initialValue >> 1);
                                Registers.A = result;

                                SetZNFlagsFromValue(result);
                                if ((initialValue & 1) == 0)
                                {
                                    Registers.P &= ~StatusFlags.Carry;
                                }
                                else
                                {
                                    Registers.P |= StatusFlags.Carry;
                                }
                                break;
                            // ARR
                            case 0b011:
                                Registers.A &= ReadOperand(addressingMode);

                                initialValue = Registers.A;
                                result = (byte)((initialValue >> 1) | ((int)(Registers.P & StatusFlags.Carry) << 7));
                                Registers.A = result;

                                SetZNFlagsFromValue(result);

                                if ((initialValue & 0b01000000) == 0)
                                {
                                    Registers.P &= ~StatusFlags.Carry;
                                }
                                else
                                {
                                    Registers.P |= StatusFlags.Carry;
                                }

                                if (((initialValue & 0b01000000) ^ (initialValue & 0b00100000)) == 0)
                                {
                                    Registers.P &= ~StatusFlags.Overflow;
                                }
                                else
                                {
                                    Registers.P |= StatusFlags.Overflow;
                                }
                                break;
                            // XAA
                            case 0b100:
                                Registers.A &= (byte)(ReadOperand(addressingMode) & Registers.X);
                                SetZNFlagsFromAccumulator();
                                break;
                            // LAX
                            case 0b101:
                                Registers.A &= ReadOperand(addressingMode);
                                Registers.X = Registers.A;
                                SetZNFlagsFromAccumulator();
                                break;
                            // AXS
                            case 0b110:
                                initialValue = Registers.X;
                                Registers.X = (byte)((Registers.A & Registers.X) - ReadOperand(addressingMode));

                                SetZNFlagsFromValue(Registers.X);

                                if (Registers.X <= initialValue)
                                {
                                    Registers.P |= StatusFlags.Carry;
                                }
                                else
                                {
                                    Registers.P &= ~StatusFlags.Carry;
                                }
                                break;
                            // SBC - effectively identical to the official version
                            case 0b111:
                                initialValue = Registers.A;
                                byte operand = ReadOperand(addressingMode);
                                Registers.A -= (byte)(operand + (byte)((Registers.P & StatusFlags.Carry) ^ StatusFlags.Carry));

                                SetZNFlagsFromAccumulator();

                                if (Registers.A <= initialValue)
                                {
                                    Registers.P |= StatusFlags.Carry;
                                }
                                else
                                {
                                    Registers.P &= ~StatusFlags.Carry;
                                }

                                int initialSign = initialValue & SignBit;
                                if (initialSign != (operand & SignBit) && initialSign != (Registers.A & SignBit))
                                {
                                    Registers.P |= StatusFlags.Overflow;
                                }
                                else
                                {
                                    Registers.P &= ~StatusFlags.Overflow;
                                }
                                break;
                        }
                        break;
                    }

                    switch (instructionCode)
                    {
                        // SLO
                        case 0b000:
                            byte initialValue = ReadOperand(addressingMode);
                            byte result = (byte)(initialValue << 1);
                            WriteOperand(addressingMode, result);

                            Registers.A |= result;

                            SetZNFlagsFromAccumulator();
                            if ((initialValue & SignBit) == 0)
                            {
                                Registers.P &= ~StatusFlags.Carry;
                            }
                            else
                            {
                                Registers.P |= StatusFlags.Carry;
                            }
                            break;
                        // RLA
                        case 0b001:
                            initialValue = ReadOperand(addressingMode);
                            result = (byte)((initialValue << 1) | (int)(Registers.P & StatusFlags.Carry));
                            WriteOperand(addressingMode, result);

                            Registers.A &= result;

                            SetZNFlagsFromValue(result);
                            if ((initialValue & SignBit) == 0)
                            {
                                Registers.P &= ~StatusFlags.Carry;
                            }
                            else
                            {
                                Registers.P |= StatusFlags.Carry;
                            }
                            break;
                        // SRE
                        case 0b010:
                            initialValue = ReadOperand(addressingMode);
                            result = (byte)(initialValue >> 1);
                            WriteOperand(addressingMode, result);

                            Registers.A ^= result;

                            SetZNFlagsFromAccumulator();
                            if ((initialValue & 1) == 0)
                            {
                                Registers.P &= ~StatusFlags.Carry;
                            }
                            else
                            {
                                Registers.P |= StatusFlags.Carry;
                            }
                            break;
                        // RRA
                        case 0b011:
                            initialValue = ReadOperand(addressingMode);
                            result = (byte)((initialValue >> 1) | ((int)(Registers.P & StatusFlags.Carry) << 7));
                            WriteOperand(addressingMode, result);

                            if ((initialValue & 1) == 0)
                            {
                                Registers.P &= ~StatusFlags.Carry;
                            }
                            else
                            {
                                Registers.P |= StatusFlags.Carry;
                            }

                            initialValue = Registers.A;
                            Registers.A += (byte)(result + (byte)(Registers.P & StatusFlags.Carry));

                            SetZNFlagsFromAccumulator();

                            if (Registers.A < initialValue)
                            {
                                Registers.P |= StatusFlags.Carry;
                            }
                            else
                            {
                                Registers.P &= ~StatusFlags.Carry;
                            }

                            int initialSign = initialValue & SignBit;
                            if (initialSign == (result & SignBit) && initialSign != (Registers.A & SignBit))
                            {
                                Registers.P |= StatusFlags.Overflow;
                            }
                            else
                            {
                                Registers.P &= ~StatusFlags.Overflow;
                            }

                            break;
                        // SAX
                        // AHX
                        // TAS
                        case 0b100:
                            if (addressingMode == AddressingMode.IndirectIndexed || addressingModeCode == 0b111)
                            {
                                // AHX
                                WriteOperand(addressingMode, (byte)(Registers.A & Registers.X & (GetAddressFromOperand(addressingMode) >> 8)));
                                break;
                            }

                            if (addressingMode == AddressingMode.AbsoluteYIndexed)
                            {
                                // TAS
                                Registers.S = (byte)(Registers.A & Registers.X);
                                WriteOperand(addressingMode, (byte)(Registers.S & (GetAddressFromOperand(addressingMode) >> 8)));
                                break;
                            }

                            // SAX
                            WriteOperand(addressingMode, (byte)(Registers.A & Registers.X));
                            break;
                        // LAX
                        // LAS
                        case 0b101:
                            if (addressingModeCode == 0b110)
                            {
                                // LAS
                                Registers.S &= ReadOperand(addressingMode);
                                Registers.A = Registers.S;
                                Registers.X = Registers.S;
                            }
                            else
                            {
                                // LAX
                                Registers.A = ReadOperand(addressingMode);
                                Registers.X = Registers.A;
                            }

                            SetZNFlagsFromAccumulator();
                            break;
                        // DCP
                        case 0b110:
                            result = (byte)(ReadOperand(addressingMode) - 1);
                            WriteOperand(addressingMode, result);

                            initialValue = Registers.A;
                            result = (byte)(Registers.A - result);

                            SetZNFlagsFromValue(result);

                            if (result <= initialValue)
                            {
                                Registers.P |= StatusFlags.Carry;
                            }
                            else
                            {
                                Registers.P &= ~StatusFlags.Carry;
                            }
                            break;
                        // ISC
                        case 0b111:
                            result = (byte)(ReadOperand(addressingMode) + 1);
                            WriteOperand(addressingMode, result);

                            initialValue = Registers.A;
                            byte operand = ReadOperand(addressingMode);
                            Registers.A -= (byte)(operand + (byte)((Registers.P & StatusFlags.Carry) ^ StatusFlags.Carry));

                            SetZNFlagsFromAccumulator();

                            if (Registers.A <= initialValue)
                            {
                                Registers.P |= StatusFlags.Carry;
                            }
                            else
                            {
                                Registers.P &= ~StatusFlags.Carry;
                            }

                            initialSign = initialValue & SignBit;
                            if (initialSign != (operand & SignBit) && initialSign != (Registers.A & SignBit))
                            {
                                Registers.P |= StatusFlags.Overflow;
                            }
                            else
                            {
                                Registers.P &= ~StatusFlags.Overflow;
                            }
                            break;
                    }
                    break;
            }

            if (lockFlags)
            {
                Registers.P = startFlags;
            }

            if (!cancelPCIncrement)
            {
                IncrementPCPastOperand(addressingMode);
            }
        }

        private void StartInterruptHandler(ushort vector)
        {
            InterruptStatePush();

            Registers.PC = SystemMemory.ReadTwoBytes(vector);
        }

        private void InterruptStatePush()
        {
            PushStackTwoByte(Registers.PC);
            PushStack((byte)(Registers.P | StatusFlags.Break));
            Registers.P |= StatusFlags.InterruptDisable;
        }

        /// <summary>
        /// Determines whether the given operand caused a page boundary to be crossed during its calculation.
        /// </summary>
        private bool OperandCausesCarryOut(AddressingMode mode)
        {
            return mode switch
            {
                AddressingMode.AbsoluteXIndexed => SystemMemory[Registers.PC] + Registers.X >= 0x0100,
                AddressingMode.AbsoluteYIndexed => SystemMemory[Registers.PC] + Registers.Y >= 0x0100,
                AddressingMode.IndirectIndexed => SystemMemory[SystemMemory[Registers.PC]] + Registers.Y >= 0x0100,
                _ => false
            };
        }

        /// <summary>
        /// Get the address that an instruction operand will operate on for the given addressing mode at the current PC register value.
        /// </summary>
        private ushort GetAddressFromOperand(AddressingMode mode)
        {
            return mode switch
            {
                AddressingMode.ZeroPage => SystemMemory[Registers.PC],
                AddressingMode.ZeroPageXIndexed => (byte)(SystemMemory[Registers.PC] + Registers.X),
                AddressingMode.ZeroPageYIndexed => (byte)(SystemMemory[Registers.PC] + Registers.Y),
                AddressingMode.Relative => (ushort)(Registers.PC + (sbyte)SystemMemory[Registers.PC] + 1),
                AddressingMode.Absolute => SystemMemory.ReadTwoBytes(Registers.PC),
                AddressingMode.AbsoluteXIndexed => (ushort)(SystemMemory.ReadTwoBytes(Registers.PC) + Registers.X),
                AddressingMode.AbsoluteYIndexed => (ushort)(SystemMemory.ReadTwoBytes(Registers.PC) + Registers.Y),
                AddressingMode.Indirect => SystemMemory.ReadTwoBytesIndirectBug(SystemMemory.ReadTwoBytes(Registers.PC)),
                AddressingMode.IndexedIndirect => SystemMemory.ReadTwoBytesIndirectBug((byte)(SystemMemory[Registers.PC] + Registers.X)),
                AddressingMode.IndirectIndexed => (ushort)(SystemMemory.ReadTwoBytesIndirectBug(SystemMemory[Registers.PC]) + Registers.Y),
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
            if (mode == AddressingMode.Implicit)
            {
                // UNOFFICIAL - Reading implicit operand is a NOP
                lockFlags = true;
                return 0;
            }

            return mode switch
            {
                AddressingMode.Immediate => SystemMemory[Registers.PC],
                AddressingMode.Accumulator => Registers.A,
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
                    Registers.A = value;
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
                case AddressingMode.Implicit:
                    // UNOFFICIAL - Writing to an immediate/implicit operand is a NOP
                    lockFlags = true;
                    break;
                default:
                    throw new ArgumentException($"The given AddressingMode ({mode}) does not have an operand.", nameof(mode));
            }
        }

        private byte PopStack()
        {
            return SystemMemory[(ushort)(0x0100 + ++Registers.S)];
        }

        private void PushStack(byte value)
        {
            SystemMemory[(ushort)(0x0100 + Registers.S--)] = value;
        }

        private ushort PopStackTwoByte()
        {
            return (ushort)((SystemMemory[(ushort)(0x0100 + ++Registers.S)])
                | (SystemMemory[(ushort)(0x0100 + ++Registers.S)] << 8));
        }

        private void PushStackTwoByte(ushort value)
        {
            SystemMemory[(ushort)(0x0100 + Registers.S--)] = (byte)(value >> 8);
            SystemMemory[(ushort)(0x0100 + Registers.S--)] = (byte)(value & 0xFF);
        }

        private void SetZNFlagsFromValue(byte value)
        {
            if (value == 0)
            {
                Registers.P |= StatusFlags.Zero;
            }
            else
            {
                Registers.P &= ~StatusFlags.Zero;
            }

            if ((value & SignBit) == 0)
            {
                Registers.P &= ~StatusFlags.Negative;
            }
            else
            {
                Registers.P |= StatusFlags.Negative;
            }
        }

        private void SetZNFlagsFromAccumulator()
        {
            SetZNFlagsFromValue(Registers.A);
        }

        private void IncrementPCPastOperand(AddressingMode mode)
        {
            Registers.PC += mode switch
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
                AddressingMode.Relative => 1,
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
                // UNOFFICIAL Group
                0b11 => addressingModeCode switch
                {
                    0b000 => AddressingMode.IndexedIndirect,
                    0b001 => AddressingMode.ZeroPage,
                    0b010 => AddressingMode.Immediate,
                    0b011 => AddressingMode.Absolute,
                    0b100 => AddressingMode.IndirectIndexed,
                    0b101 when instructionCode is 0b100 or 0b101 => AddressingMode.ZeroPageYIndexed, // SAX & LAX
                    0b101 => AddressingMode.ZeroPageXIndexed,
                    0b110 => AddressingMode.AbsoluteYIndexed,
                    0b111 when instructionCode is 0b100 or 0b101 => AddressingMode.AbsoluteYIndexed, // AHX & LAX
                    0b111 => AddressingMode.AbsoluteXIndexed,
                    _ => throw new Exception()
                },
                _ => throw new Exception()
            };
        }

        public override string ToString()
        {
            return $"{Registers.PC:X4} A:{Registers.A:X2} X:{Registers.X:X2} Y:{Registers.Y:X2} P:{(byte)Registers.P:X2} SP:{Registers.S:X2}";
        }
    }
}
