namespace NESCS
{
    public abstract class Mapper
    {
        public abstract byte MappedCPURead(ushort address);
        public abstract void MappedCPUWrite(ushort address, byte value);

        public abstract byte MappedPPURead(ushort address);
        public abstract void MappedPPUWrite(ushort address, byte value);
    }
}
