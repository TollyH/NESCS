namespace NESCS
{
    public class APURegisters(NESSystem nesSystem)
    {
        public const ushort MappedOAMDMAAddress = 0x4014;

        public const ushort MappedJOY1Address = 0x4016;
        public const ushort MappedJOY2Address = 0x4017;

        public byte this[ushort mappedAddress]
        {
            // Open bus
            get
            {
                return mappedAddress switch
                {
                    // Top 3-bits of controller input are open bus
                    MappedJOY1Address => (byte)((mappedAddress & 0b11100000) | (nesSystem.ControllerOne.ReadClock() ? 1 : 0)),
                    MappedJOY2Address => (byte)((mappedAddress & 0b11100000) | (nesSystem.ControllerTwo.ReadClock() ? 1 : 0)),
                    _ => (byte)(mappedAddress >> 8)  // Open bus
                };
            }
            set
            {
                switch (mappedAddress)
                {
                    case MappedOAMDMAAddress:
                        nesSystem.CpuCore.StartOAMDMACopy(value);
                        break;
                    case MappedJOY1Address:
                        bool latch = (value & 1) != 0;
                        nesSystem.ControllerOne.StrobeLatch = latch;
                        nesSystem.ControllerTwo.StrobeLatch = latch;
                        break;
                }
            }
        }
    }
}
