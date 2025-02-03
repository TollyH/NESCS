namespace NESCS.Mappers
{
    public class Empty : IMapper
    {
        public string Name => "Empty";
        public string[] OtherNames => Array.Empty<string>();
        public string Description => "Represents an empty cartridge slot";

        public byte MappedCPURead(ushort address)
        {
            return (byte)address;
        }

        public void MappedCPUWrite(ushort address, byte value) { }

        public byte MappedPPURead(ushort address)
        {
            return (byte)address;
        }

        public void MappedPPUWrite(ushort address, byte value) { }
    }
}
