namespace NESCS
{
    public readonly record struct APUSequenceTiming(
        uint Step1Cycle,
        uint Step2Cycle,
        uint Step3Cycle,
        uint Step4Cycle,
        uint Step5Cycle,
        uint Step6Cycle);

    public class APU
    {
        public static readonly APUSequenceTiming Ntsc4StepSequenceTiming = new(
            7457,
            14913,
            22371,
            29828,
            29829,
            29830);
        public static readonly APUSequenceTiming Pal4StepSequenceTiming = new(
            8313,
            16627,
            24939,
            33252,
            33253,
            33254);

        public static readonly APUSequenceTiming Ntsc5StepSequenceTiming = new(
            7457,
            14913,
            22371,
            29829,
            37281,
            37282);
        public static readonly APUSequenceTiming Pal5StepSequenceTiming = new(
            8313,
            16627,
            24939,
            33253,
            41565,
            41566);

        public APUSequenceTiming Current4StepSequenceTiming { get; set; } = Ntsc4StepSequenceTiming;
        public APUSequenceTiming Current5StepSequenceTiming { get; set; } = Ntsc5StepSequenceTiming;

        public ulong CurrentCycle { get; private set; }

        public readonly APURegisters Registers;

        public readonly PulseChannel Pulse1;
        public readonly PulseChannel Pulse2;
        public readonly TriangleChannel Triangle;
        public readonly NoiseChannel Noise;
        public readonly DMCChannel Dmc;

        public readonly IChannel[] Channels;

        private readonly NESSystem nesSystem;

        public APU(NESSystem nesSystem)
        {
            this.nesSystem = nesSystem;

            Registers = new APURegisters(nesSystem);

            Pulse1 = new PulseChannel(Registers.Pulse1Registers, false);
            Pulse2 = new PulseChannel(Registers.Pulse2Registers, true);
            Triangle = new TriangleChannel(Registers.TriangleRegisters);
            Noise = new NoiseChannel(Registers.NoiseRegisters);
            Dmc = new DMCChannel(Registers.DmcRegisters);

            Channels = new IChannel[5]
            {
                Pulse1, Pulse2, Triangle, Noise, Dmc
            };
        }

        public void ClockSequencer()
        {
            RunNextSequenceStep();

            if ((Registers.StatusControl & StatusControlFlags.FrameInterrupt) != 0)
            {
                nesSystem.CpuCore.InterruptRequest();
            }

            CurrentCycle++;
        }

        private void RunNextSequenceStep()
        {
            APUSequenceTiming currentTiming = (Registers.FrameCounter & FrameCounterFlags.FiveStepSequence) != 0
                ? Current5StepSequenceTiming
                : Current4StepSequenceTiming;

            if (CurrentCycle == currentTiming.Step1Cycle
                || CurrentCycle == currentTiming.Step2Cycle
                || CurrentCycle == currentTiming.Step3Cycle
                || CurrentCycle == currentTiming.Step5Cycle)
            {
                ClockAllChannelEnvelope();
            }

            if (CurrentCycle == currentTiming.Step2Cycle
                || CurrentCycle == currentTiming.Step5Cycle)
            {
                ClockAllChannelLengthCounter();
            }

            if ((Registers.FrameCounter & FrameCounterFlags.DisableFrameInterrupt) != 0)
            {
                Registers.StatusControl &= ~StatusControlFlags.FrameInterrupt;
            }
            // Only 4-step sequence can raise IRQ
            else if ((Registers.FrameCounter & FrameCounterFlags.FiveStepSequence) == 0
                && (CurrentCycle == currentTiming.Step4Cycle
                    || CurrentCycle == currentTiming.Step5Cycle
                    || CurrentCycle == currentTiming.Step6Cycle))
            {
                Registers.StatusControl |= StatusControlFlags.FrameInterrupt;
            }

            if (CurrentCycle >= currentTiming.Step6Cycle)
            {
                CurrentCycle = 0;
            }
        }

        private void ClockAllChannelEnvelope()
        {
            foreach (IChannel channel in Channels)
            {
                channel.ClockEnvelope();
            }
        }

        private void ClockAllChannelLengthCounter()
        {
            foreach (IChannel channel in Channels)
            {
                channel.ClockLengthCounter();
            }
        }
    }

    public interface IChannel
    {
        public void ClockEnvelope();

        public void ClockLengthCounter();
    }

    public class PulseChannel(PulseChannelRegisters registers, bool twosComplementSweep) : IChannel
    {
        public void ClockEnvelope()
        {
            throw new NotImplementedException();
        }

        public void ClockLengthCounter()
        {
            throw new NotImplementedException();
        }
    }

    public class TriangleChannel(TriangleChannelRegisters registers) : IChannel
    {
        public void ClockEnvelope()
        {
            throw new NotImplementedException();
        }

        public void ClockLengthCounter()
        {
            throw new NotImplementedException();
        }
    }

    public class NoiseChannel(NoiseChannelRegisters registers) : IChannel
    {
        public void ClockEnvelope()
        {
            throw new NotImplementedException();
        }

        public void ClockLengthCounter()
        {
            throw new NotImplementedException();
        }
    }

    public class DMCChannel(DMCChannelRegisters registers) : IChannel
    {
        public void ClockEnvelope()
        {
            throw new NotImplementedException();
        }

        public void ClockLengthCounter()
        {
            throw new NotImplementedException();
        }
    }
}
