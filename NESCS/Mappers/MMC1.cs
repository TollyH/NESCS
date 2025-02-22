namespace NESCS.Mappers
{
    public class MMC1(NESSystem nesSystem, int prgRomSize, int chrRomSize, bool prgRamPresent) : IMapper
    {
        public enum SwitchMode
        {
            Single32Kb,
            FirstFixSecondSwitch,
            FirstSwitchSecondFix
        }

        public byte[] PrgRom { get; } = new byte[prgRomSize];
        public byte[] ChrRom { get; } = new byte[chrRomSize > 0 ? chrRomSize : 0x20000];  // If CHR-ROM size is 0, it's CHR-RAM
        public byte[] PrgRam { get; } = new byte[0x2000];

        public bool PrgRamPresent { get; } = prgRamPresent;
        public bool ChrRamPresent { get; } = chrRomSize == 0;

        public byte ShiftRegister { get; private set; }

        // MMC1 is controlled by writing to a shift register one bit at a time, until 5 bits have been written,
        // when the value is committed to one of the internal configuration registers.
        private int shiftRegisterWriteNumber = 0;

        private byte _controlRegister = 0b01100;
        public byte ControlRegister
        {
            get => (byte)(_controlRegister & 0b11111);
            private set => _controlRegister = (byte)(value & 0b11111);
        }

        private byte _chrBank0 = 0;
        public byte ChrBank0
        {
            get => (byte)(_chrBank0 & 0b11111);
            private set => _chrBank0 = (byte)(value & 0b11111);
        }

        private byte _chrBank1 = 0;
        public byte ChrBank1
        {
            get => (byte)(_chrBank1 & 0b11111);
            private set => _chrBank1 = (byte)(value & 0b11111);
        }

        private byte _prgBank = 0;
        public byte PrgBank
        {
            get => (byte)(_prgBank & 0b11111);
            private set => _prgBank = (byte)(value & 0b11111);
        }

        public Mirroring NametableMirroring => (ControlRegister & 0b11) switch
        {
            0b00 => Mirroring.SingleLower,
            0b01 => Mirroring.SingleUpper,
            0b10 => Mirroring.Vertical,
            0b11 => Mirroring.Horizontal,
            _ => throw new Exception()
        };

        public SwitchMode PrgRomSwitchMode => (ControlRegister & 0b1100) switch
        {
            0b0000 or 0b0100 => SwitchMode.Single32Kb,
            0b1000 => SwitchMode.FirstFixSecondSwitch,
            0b1100 => SwitchMode.FirstSwitchSecondFix,
            _ => throw new Exception()
        };

        public bool ChrRom4KbMode => (ControlRegister & 0b10000) != 0;

        public byte MappedCPURead(ushort address)
        {
            return address switch
            {
                < 0x6000 => (byte)(address >> 8),  // Open bus
                <= 0x7FFF => PrgRamPresent
                    ? PrgRam[address % PrgRam.Length]
                    : (byte)(address >> 8),  // Open bus
                <= 0xFFFF => PrgRom[GetBankedPrgRomAddress(address) % PrgRom.Length]
            };
        }

        public void MappedCPUWrite(ushort address, byte value)
        {
            switch (address)
            {
                case < 0x6000:
                    break; // Open bus
                case <= 0x7FFF:
                    if (PrgRamPresent)
                    {
                        PrgRam[address % PrgRam.Length] = value;
                    }
                    break;
                case <= 0xFFFF:
                    // Interface with the mapper chip
                    if ((value & 0b10000000) != 0)
                    {
                        // Reset bit is set
                        ResetShiftRegister();
                        ControlRegister |= 0b01100;
                    }
                    else
                    {
                        // The lower bit of the register is written first
                        ShiftRegister >>= 1;
                        ShiftRegister |= (byte)((value & 1) << 4);
                        if (++shiftRegisterWriteNumber == 5)
                        {
                            switch (address & 0b110000000000000)
                            {
                                case 0b00 << 13:
                                    ControlRegister = ShiftRegister;
                                    break;
                                case 0b01 << 13:
                                    ChrBank0 = ShiftRegister;
                                    break;
                                case 0b10 << 13:
                                    ChrBank1 = ShiftRegister;
                                    break;
                                case 0b11 << 13:
                                    PrgBank = ShiftRegister;
                                    break;
                            }
                            ResetShiftRegister();
                        }
                    }
                    break;
            }
        }

        public byte MappedPPURead(ushort address)
        {
            return address switch
            {
                <= 0x1FFF => ChrRom[GetBankedChrRomAddress(address)],
                <= 0x3EFF => nesSystem.SystemMemory.CiRam[GetMirroredCiRamAddress(address)],
                _ => (byte)(address >> 8)  // Open bus
            };
        }

        public void MappedPPUWrite(ushort address, byte value)
        {
            switch (address)
            {
                case <= 0x1FFF:
                    if (ChrRamPresent)
                    {
                        ChrRom[GetBankedChrRomAddress(address)] = value;
                    }
                    break;
                case <= 0x3EFF:
                    nesSystem.SystemMemory.CiRam[GetMirroredCiRamAddress(address)] = value;
                    break;
            }
        }

        private int GetMirroredCiRamAddress(ushort address)
        {
            address &= 0xFFF;
            return NametableMirroring switch
            {
                Mirroring.Horizontal => address switch
                {
                    <= 0x3FF => address - 0x000,
                    <= 0xBFF => address - 0x400,
                    <= 0xFFF => address - 0x800,
                    _ => throw new ArgumentException("Invalid nametable address")
                },
                Mirroring.Vertical => address switch
                {
                    <= 0x7FF => address - 0x000,
                    <= 0xFFF => address - 0x800,
                    _ => throw new ArgumentException("Invalid nametable address")
                },
                Mirroring.SingleLower => address & 0x3FF,
                Mirroring.SingleUpper => address | 0x400,
                _ => throw new ArgumentException("Invalid mirroring type, must be either Horizontal, Vertical, SingleLower or SingleUpper.")
            };
        }

        private int GetBankedPrgRomAddress(ushort address)
        {
            // Mapped PRG-ROM starts at $8000, convert to an index starting at $0.
            address &= 0x7FFF;

            return PrgRomSwitchMode switch
            {
                SwitchMode.Single32Kb => (((PrgBank & 0b1110) >> 1) * 32768) + address,
                SwitchMode.FirstFixSecondSwitch => address >= 0x4000
                    ? (PrgBank & 0b1111) * 16384 + (address & 0x3FFF)
                    : address,
                SwitchMode.FirstSwitchSecondFix => address >= 0x4000
                    ? PrgRom.Length - 16384 + (address & 0x3FFF)
                    : (PrgBank & 0b1111) * 16384 + address,
                _ => throw new Exception()
            };
        }

        private int GetBankedChrRomAddress(ushort address)
        {
            if (ChrRom4KbMode)
            {
                return address >= 0x1000
                    ? (ChrBank1 * 4096) + (address & 0xFFF)
                    : (ChrBank0 * 4096) + address;
            }

            return ((ChrBank0 & 0b11110) >> 1) * 8192 + address;
        }

        private void ResetShiftRegister()
        {
            ShiftRegister = 0;
            shiftRegisterWriteNumber = 0;
        }
    }
}
