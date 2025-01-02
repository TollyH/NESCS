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
                <= 0x2007 => new MemoryRef(PpuRegisters, (ushort)(address & 0x0008)),
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
    }
}
