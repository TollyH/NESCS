namespace NESCS
{
    public readonly record struct MemoryRef(byte[] Region, ushort Offset)
    {
        public byte ReadByte()
        {
            return Region[Offset];
        }

        public void WriteByte(byte value)
        {
            Region[Offset] = value;
        }
    }

    public class Memory
    {
        public readonly byte[] InternalRAM = new byte[0x0800];
        public readonly byte[] PpuRegisters = new byte[0x0008];
        public readonly byte[] ApuIoRegisters = new byte[0x0018];
        public readonly byte[] TestModeRegisters = new byte[0x0008];
        public readonly byte[] CartridgeRAM = new byte[0xBFE0];

        public MemoryRef MapAddress(ushort address)
        {
            return address switch
            {
                <= 0x1FFF => new MemoryRef(InternalRAM, (ushort)(address & 0x07FF)),
                <= 0x3FFF => new MemoryRef(PpuRegisters, (ushort)(address & 0x0007)),
                <= 0x4017 => new MemoryRef(ApuIoRegisters, (ushort)(address - 0x4000)),
                <= 0x401F => new MemoryRef(TestModeRegisters, (ushort)(address - 0x4018)),
                <= 0xFFFF => new MemoryRef(CartridgeRAM, (ushort)(address - 0x4020))
            };
        }

        public byte this[ushort address]
        {
            get
            {
                MemoryRef memRef = MapAddress(address);
                return memRef.ReadByte();
            }
            set
            {
                MemoryRef memRef = MapAddress(address);
                memRef.WriteByte(value);
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
