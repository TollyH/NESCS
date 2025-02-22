namespace NESCS
{
    public interface IMapper
    {
        public byte MappedCPURead(ushort address);
        public void MappedCPUWrite(ushort address, byte value);

        public byte MappedPPURead(ushort address);
        public void MappedPPUWrite(ushort address, byte value);
    }
}
