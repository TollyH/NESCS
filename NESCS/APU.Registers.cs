namespace NESCS
{
    [Flags]
    public enum FrameCounterFlags : byte
    {
        None = 0,

        DisableFrameInterrupt = 0b1000000,
        FiveStepSequence = 0b10000000
    }

    [Flags]
    public enum StatusControlFlags : byte
    {
        None = 0,

        EnablePulse1 = 0b1,
        EnablePulse2 = 0b10,
        EnableTriangle = 0b100,
        EnableNoise = 0b1000,
        EnableDmc = 0b10000,
        FrameInterrupt = 0b1000000,
        DmcInterrupt = 0b10000000

    }

    public class APURegisters(NESSystem nesSystem)
    {
        public const ushort MappedPulse1SoundConfigAddress = 0x4000;
        public const ushort MappedPulse1SweepAddress = 0x4001;
        public const ushort MappedPulse1TimerLowAddress = 0x4002;
        public const ushort MappedPulse1LengthTimerHighAddress = 0x4003;

        public const ushort MappedPulse2SoundConfigAddress = 0x4004;
        public const ushort MappedPulse2SweepAddress = 0x4005;
        public const ushort MappedPulse2TimerLowAddress = 0x4006;
        public const ushort MappedPulse2LengthTimerHighAddress = 0x4007;

        public const ushort MappedTriangleSoundConfigAddress = 0x4008;
        public const ushort MappedTriangleTimerLowAddress = 0x400A;
        public const ushort MappedTriangleLengthTimerHighAddress = 0x400B;

        public const ushort MappedNoiseSoundConfigAddress = 0x400C;
        public const ushort MappedNoiseLoopPeriodAddress = 0x400E;
        public const ushort MappedNoiseLengthAddress = 0x400F;

        public const ushort MappedDmcSoundConfigAddress = 0x4010;
        public const ushort MappedDmcDirectLoadAddress = 0x4011;
        public const ushort MappedDmcSampleAddressRawAddress = 0x4012;
        public const ushort MappedDmcSampleLengthRawAddress = 0x4013;

        public const ushort MappedOAMDMAAddress = 0x4014;

        public const ushort MappedJOY1Address = 0x4016;
        public const ushort MappedJOY2Address = 0x4017;

        public const ushort MappedStatusControlAddress = 0x4015;
        public const ushort MappedFrameCounterAddress = 0x4017;

        public readonly PulseChannelRegisters Pulse1Registers = new();
        public readonly PulseChannelRegisters Pulse2Registers = new();
        public readonly TriangleChannelRegisters TriangleRegisters = new();
        public readonly NoiseChannelRegisters NoiseRegisters = new();
        public readonly DMCChannelRegisters DmcRegisters = new();

        public StatusControlFlags StatusControl;
        public FrameCounterFlags FrameCounter;

        public byte this[ushort mappedAddress]
        {
            get
            {
                switch (mappedAddress)
                {
                    case MappedStatusControlAddress:
                        byte returnValue = (byte)((byte)StatusControl | ((mappedAddress >> 8) & 0b00100000));
                        // Reading $4015 unsets interrupt flag
                        StatusControl &= ~StatusControlFlags.FrameInterrupt;
                        // Bit 5 of status register is open bus
                        return returnValue;
                    case MappedJOY1Address:
                        // Top 3-bits of controller input are open bus
                        return (byte)(((mappedAddress >> 8) & 0b11100000) | (nesSystem.ControllerOne.ReadClock() ? 1 : 0));
                    case MappedJOY2Address:
                        return (byte)(((mappedAddress >> 8) & 0b11100000) | (nesSystem.ControllerTwo.ReadClock() ? 1 : 0));
                    default:
                        return (byte)(mappedAddress >> 8); // Open bus
                }
            }
            set
            {
                switch (mappedAddress)
                {
                    // Channel-specific APU registers
                    // Pulse 1
                    case MappedPulse1SoundConfigAddress:
                        Pulse1Registers.SoundConfig = value;
                        break;
                    case MappedPulse1SweepAddress:
                        Pulse1Registers.Sweep = value;
                        nesSystem.ApuCore.Pulse1.OnSweepWrite();
                        break;
                    case MappedPulse1TimerLowAddress:
                        Pulse1Registers.TimerLow = value;
                        break;
                    case MappedPulse1LengthTimerHighAddress:
                        Pulse1Registers.LengthTimerHigh = value;
                        nesSystem.ApuCore.Pulse1.OnLengthCounterLoadWrite();
                        break;
                    // Pulse 2
                    case MappedPulse2SoundConfigAddress:
                        Pulse2Registers.SoundConfig = value;
                        break;
                    case MappedPulse2SweepAddress:
                        Pulse2Registers.Sweep = value;
                        nesSystem.ApuCore.Pulse2.OnSweepWrite();
                        break;
                    case MappedPulse2TimerLowAddress:
                        Pulse2Registers.TimerLow = value;
                        break;
                    case MappedPulse2LengthTimerHighAddress:
                        Pulse2Registers.LengthTimerHigh = value;
                        nesSystem.ApuCore.Pulse2.OnLengthCounterLoadWrite();
                        break;
                    // Triangle
                    case MappedTriangleSoundConfigAddress:
                        TriangleRegisters.SoundConfig = value;
                        break;
                    case MappedTriangleTimerLowAddress:
                        TriangleRegisters.TimerLow = value;
                        break;
                    case MappedTriangleLengthTimerHighAddress:
                        TriangleRegisters.LengthTimerHigh = value;
                        nesSystem.ApuCore.Triangle.OnLengthCounterLoadWrite();
                        break;
                    // Noise
                    case MappedNoiseSoundConfigAddress:
                        NoiseRegisters.SoundConfig = value;
                        break;
                    case MappedNoiseLoopPeriodAddress:
                        NoiseRegisters.LoopPeriod = value;
                        break;
                    case MappedNoiseLengthAddress:
                        NoiseRegisters.Length = value;
                        nesSystem.ApuCore.Noise.OnLengthCounterLoadWrite();
                        break;
                    // DMC
                    case MappedDmcSoundConfigAddress:
                        DmcRegisters.SoundConfig = value;
                        break;
                    case MappedDmcDirectLoadAddress:
                        DmcRegisters.DirectLoadRaw = value;
                        break;
                    case MappedDmcSampleAddressRawAddress:
                        DmcRegisters.SampleAddressRaw = value;
                        break;
                    case MappedDmcSampleLengthRawAddress:
                        DmcRegisters.SampleLengthRaw = value;
                        break;
                    // Global APU/extra CPU registers
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
                        nesSystem.ApuCore.ResetCurrentCycle();
                        break;
                }
            }
        }
    }

    public interface ILengthCounterChannelRegisters
    {
        public bool HaltLengthCounter { get; }
        public byte LengthCounterReload { get; }
    }

    public interface IEnvelopedChannelRegisters : ILengthCounterChannelRegisters
    {
        public byte Volume { get; }
    }

    public class PulseChannelRegisters : IEnvelopedChannelRegisters
    {
        // Mapped write-only registers
        public byte SoundConfig;
        public byte Sweep;
        public byte TimerLow;
        public byte LengthTimerHigh;

        // Extracted values
        public byte DutyCycle
        {
            get => (byte)(SoundConfig >> 6);
            set => SoundConfig = (byte)((SoundConfig & 0b00111111) | (value << 6));
        }

        public bool HaltLengthCounter
        {
            get => (SoundConfig & 0b00100000) != 0;
            set
            {
                if (value)
                {
                    SoundConfig |= 0b00100000;
                }
                else
                {
                    SoundConfig &= 0b11011111;
                }
            }
        }

        public bool ConstantVolume
        {
            get => (SoundConfig & 0b00100000) != 0;
            set
            {
                if (value)
                {
                    SoundConfig |= 0b00100000;
                }
                else
                {
                    SoundConfig &= 0b11011111;
                }
            }
        }

        public byte Volume
        {
            get => (byte)(SoundConfig & 0b00001111);
            set => SoundConfig = (byte)((SoundConfig & 0b11110000) | (value & 0b00001111));
        }

        public bool SweepEnabled
        {
            get => (Sweep & 0b10000000) != 0;
            set
            {
                if (value)
                {
                    Sweep |= 0b10000000;
                }
                else
                {
                    Sweep &= 0b01111111;
                }
            }
        }

        public byte SweepPeriod
        {
            get => (byte)((Sweep & 0b01110000) >> 4);
            set => Sweep = (byte)((Sweep & 0b10001111) | ((value & 0b111) << 4));
        }

        public bool SweepNegate
        {
            get => (Sweep & 0b00001000) != 0;
            set
            {
                if (value)
                {
                    Sweep |= 0b00001000;
                }
                else
                {
                    Sweep &= 0b11110111;
                }
            }
        }

        public byte SweepShiftCount
        {
            get => (byte)(SoundConfig & 0b00000111);
            set => SoundConfig = (byte)((SoundConfig & 0b11111000) | (value & 0b00000111));
        }

        public ushort Timer
        {
            get => (ushort)(TimerLow | ((LengthTimerHigh & 0b111) << 8));
            set
            {
                TimerLow = (byte)value;
                LengthTimerHigh = (byte)((LengthTimerHigh & 0b11111000) | ((value & 0b11100000000) >> 8));
            }
        }

        public byte LengthCounterReload
        {
            get => (byte)(LengthTimerHigh >> 3);
            set => LengthTimerHigh = (byte)((LengthTimerHigh & 0b00000111) | (value << 3));
        }
    }

    public class TriangleChannelRegisters : ILengthCounterChannelRegisters
    {
        // Mapped write-only registers
        public byte SoundConfig;
        public byte TimerLow;
        public byte LengthTimerHigh;

        // Extracted values
        public bool HaltLengthCounter
        {
            get => (SoundConfig & 0b10000000) != 0;
            set
            {
                if (value)
                {
                    SoundConfig |= 0b10000000;
                }
                else
                {
                    SoundConfig &= 0b01111111;
                }
            }
        }

        public byte CounterReloadValue
        {
            get => (byte)(SoundConfig & 0b01111111);
            set => SoundConfig = (byte)((SoundConfig & 0b1000000) | (value & 0b01111111));
        }

        public ushort Timer
        {
            get => (ushort)(TimerLow | ((LengthTimerHigh & 0b111) << 8));
            set
            {
                TimerLow = (byte)value;
                LengthTimerHigh = (byte)((LengthTimerHigh & 0b11111000) | ((value & 0b11100000000) >> 8));
            }
        }

        public byte LengthCounterReload
        {
            get => (byte)(LengthTimerHigh >> 3);
            set => LengthTimerHigh = (byte)((LengthTimerHigh & 0b00000111) | (value << 3));
        }
    }

    public class NoiseChannelRegisters : IEnvelopedChannelRegisters
    {
        // Mapped write-only registers
        public byte SoundConfig;
        public byte LoopPeriod;
        public byte Length;

        // Extracted values
        public bool HaltLengthCounter
        {
            get => (SoundConfig & 0b00100000) != 0;
            set
            {
                if (value)
                {
                    SoundConfig |= 0b00100000;
                }
                else
                {
                    SoundConfig &= 0b11011111;
                }
            }
        }

        public bool ConstantVolume
        {
            get => (SoundConfig & 0b00100000) != 0;
            set
            {
                if (value)
                {
                    SoundConfig |= 0b00100000;
                }
                else
                {
                    SoundConfig &= 0b11011111;
                }
            }
        }

        public byte Volume
        {
            get => (byte)(SoundConfig & 0b00001111);
            set => SoundConfig = (byte)((SoundConfig & 0b11110000) | (value & 0b00001111));
        }

        public bool LoopMode
        {
            get => (LoopPeriod & 0b10000000) != 0;
            set
            {
                if (value)
                {
                    LoopPeriod |= 0b10000000;
                }
                else
                {
                    LoopPeriod &= 0b01111111;
                }
            }
        }

        public byte Period
        {
            get => (byte)(LoopPeriod & 0b00001111);
            set => LoopPeriod = (byte)((LoopPeriod & 0b11110000) | (value & 0b00001111));
        }

        public byte LengthCounterReload
        {
            get => (byte)(Length >> 3);
            set => Length = (byte)((Length & 0b00000111) | (value << 3));
        }
    }

    public class DMCChannelRegisters
    {
        // Mapped write-only registers
        public byte SoundConfig;
        public byte DirectLoadRaw;
        public byte SampleAddressRaw;
        public byte SampleLengthRaw;

        // Extracted values
        public bool IrqEnable
        {
            get => (SoundConfig & 0b10000000) != 0;
            set
            {
                if (value)
                {
                    SoundConfig |= 0b10000000;
                }
                else
                {
                    SoundConfig &= 0b01111111;
                }
            }
        }

        public bool LoopSample
        {
            get => (SoundConfig & 0b01000000) != 0;
            set
            {
                if (value)
                {
                    SoundConfig |= 0b01000000;
                }
                else
                {
                    SoundConfig &= 0b10111111;
                }
            }
        }

        public byte FrequencyIndex
        {
            get => (byte)(SoundConfig & 0b00001111);
            set => SoundConfig = (byte)((SoundConfig & 0b11110000) | (value & 0b00001111));
        }

        public byte DirectLoad
        {
            get => (byte)(DirectLoadRaw & 0b01111111);
            set => DirectLoadRaw = (byte)(value & 0b01111111);
        }

        public ushort SampleAddress => (ushort)(0b1100000000000000 | (SampleAddressRaw << 6));
        public ushort SampleLength => (ushort)((SampleLengthRaw << 4) | 1);
    }
}
