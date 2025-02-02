namespace NESCS
{
    public class Memory(PPURegisters ppuRegisters, Mapper insertedCartridgeMapper)
    {
        public readonly byte[] InternalRAM = new byte[0x0800];
        public readonly PPURegisters PpuRegisters = ppuRegisters;
        public readonly Mapper InsertedCartridgeMapper = insertedCartridgeMapper;

        public byte this[ushort address]
        {
            get
            {
                return address switch
                {
                    <= 0x1FFF => InternalRAM[(ushort)(address & 0x07FF)],
                    <= 0x3FFF => PpuRegisters[(ushort)(address & 0x2007)],
                    <= 0x4017 => throw new NotImplementedException(), // ApuIoRegisters, (ushort)(address - 0x4000),
                    <= 0x401F => throw new NotImplementedException(), // TestModeRegisters, (ushort)(address - 0x4018),
                    <= 0xFFFF => InsertedCartridgeMapper.MappedCPURead((ushort)(address - 0x4020))
                };
            }
            set
            {
                switch (address)
                {
                    case <= 0x1FFF:
                        InternalRAM[(ushort)(address & 0x07FF)] = value;
                        break;
                    case <= 0x3FFF:
                        PpuRegisters[(ushort)(address & 0x0007)] = value;
                        break;
                    case <= 0x4017:
                        throw new NotImplementedException();
                    case <= 0x401F:
                        throw new NotImplementedException();
                    case <= 0xFFFF:
                        InsertedCartridgeMapper.MappedCPUWrite((ushort)(address - 0x4020), value);
                        break;
                }
            }
        }

        public ushort ReadTwoBytes(ushort address)
        {
            return (ushort)(this[address] | (this[(ushort)(address + 1)] << 8));
        }

        // Same as ReadTwoBytes, except if the address ends in 0xFF, the second address read will have 0x0100 subtracted from it.
        // For example: reading 0x03FF will read 0x03FF and 0x0300, instead of 0x0400.
        // This is used to replicate a bug with the indirect addressing mode in the 6502 processor.
        public ushort ReadTwoBytesIndirectBug(ushort address)
        {
            return (ushort)(this[address] | (this[(ushort)((address & 0xFF) == 0xFF ? (address + 1) - 0x0100 : address + 1)] << 8));
        }

        public void WriteTwoBytes(ushort address, ushort value)
        {
            this[address] = (byte)(value & 0xFF);
            this[(ushort)(address + 1)] = (byte)(value >> 8);
        }
    }
}
