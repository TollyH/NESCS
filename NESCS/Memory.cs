namespace NESCS
{
    public class Memory(NESSystem nesSystem)
    {
        public readonly byte[] InternalRam = new byte[0x0800];
        public readonly byte[] CiRam = new byte[0x800];

        public byte this[ushort address]
        {
            get
            {
                return address switch
                {
                    <= 0x1FFF => InternalRam[(ushort)(address & 0x07FF)],
                    <= 0x3FFF => nesSystem.PpuCore.Registers[address],
                    <= 0x4017 => nesSystem.ApuCore.Registers[address],
                    <= 0xFFFF => nesSystem.InsertedCartridgeMapper.MappedCPURead(address)
                };
            }
            set
            {
                switch (address)
                {
                    case <= 0x1FFF:
                        InternalRam[(ushort)(address & 0x07FF)] = value;
                        break;
                    case <= 0x3FFF:
                        nesSystem.PpuCore.Registers[address] = value;
                        break;
                    case <= 0x4017:
                        nesSystem.ApuCore.Registers[address] = value;
                        break;
                    case <= 0xFFFF:
                        nesSystem.InsertedCartridgeMapper.MappedCPUWrite(address, value);
                        break;
                }
            }
        }

        public void Reset()
        {
            for (int i = 0; i < InternalRam.Length; i++)
            {
                InternalRam[i] = 0;
            }

            for (int i = 0; i < CiRam.Length; i++)
            {
                CiRam[i] = 0;
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
