namespace NESCS
{
    [Flags]
    public enum FrameCounterFlags : byte
    {
        None = 0,

        DisableFrameInterrupt = 0b1000000,
        FiveFrameSequence = 0b10000000
    }

    [Flags]
    public enum StatusControlFlags : byte
    {
        None = 0,

        LengthCountdownPulse1 = 0b1,
        LengthCountdownPulse2 = 0b10,
        LengthCountdownTriangle = 0b100,
        LengthCountdownNoise = 0b1000,
        DmcEnable = 0b10000,
        FrameInterrupt = 0b1000000,
        DmcInterrupt = 0b10000000

    }

    public class APURegisters(NESSystem nesSystem)
    {
        public const ushort MappedOAMDMAAddress = 0x4014;

        public const ushort MappedJOY1Address = 0x4016;
        public const ushort MappedJOY2Address = 0x4017;

        public const ushort MappedStatusControlAddress = 0x4015;
        public const ushort MappedFrameCounterAddress = 0x4017;

        public StatusControlFlags StatusControl;
        public FrameCounterFlags FrameCounter;

        public byte this[ushort mappedAddress]
        {
            get
            {
                return mappedAddress switch
                {
                    // Bit 5 of status register is open bus
                    MappedStatusControlAddress => (byte)((byte)StatusControl | ((mappedAddress >> 8) & 0b00100000)),
                    // Top 3-bits of controller input are open bus
                    MappedJOY1Address => (byte)(((mappedAddress >> 8) & 0b11100000) | (nesSystem.ControllerOne.ReadClock() ? 1 : 0)),
                    MappedJOY2Address => (byte)(((mappedAddress >> 8) & 0b11100000) | (nesSystem.ControllerTwo.ReadClock() ? 1 : 0)),
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
                    case MappedStatusControlAddress:
                        StatusControl = (StatusControlFlags)(value & ~(byte)(StatusControlFlags.FrameInterrupt | StatusControlFlags.DmcInterrupt));
                        break;
                    case MappedFrameCounterAddress:
                        FrameCounter = (FrameCounterFlags)value;
                        break;
                }
            }
        }
    }
}
