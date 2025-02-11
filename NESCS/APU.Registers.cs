namespace NESCS
{
    public class APURegisters(CPU cpuCore)
    {
        public const ushort MappedOAMDMAAddress = 0x4014;

        public byte this[ushort mappedAddress]
        {
            // Open bus
            get => (byte)(mappedAddress >> 8);
            set
            {
                if (mappedAddress == MappedOAMDMAAddress)
                {
                    cpuCore.StartOAMDMACopy(value);
                }
            }
        }
    }
}
