﻿namespace NESCS
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

            ClockAllChannelTimer();

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

        private void ClockAllChannelTimer()
        {
            foreach (IChannel channel in Channels)
            {
                channel.ClockTimer();
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
            Pulse1.ClockLengthCounter((Registers.StatusControl & StatusControlFlags.LengthCountdownPulse1) != 0);
            Pulse2.ClockLengthCounter((Registers.StatusControl & StatusControlFlags.LengthCountdownPulse2) != 0);
            Triangle.ClockLengthCounter((Registers.StatusControl & StatusControlFlags.LengthCountdownTriangle) != 0);
            Noise.ClockLengthCounter((Registers.StatusControl & StatusControlFlags.LengthCountdownNoise) != 0);
        }
    }

    public interface IChannel
    {
        public double GetSample();

        public void ClockTimer();

        public void ClockEnvelope();
    }

    public class PulseChannel(PulseChannelRegisters registers, bool twosComplementSweep) : IChannel
    {
        public const int DutyCycles = 4;
        public const int DutyCycleSequenceLength = 8;

        private const int envelopeDecayValueReset = 15;

        public static readonly double[,] DutyCycleSequences = new double[DutyCycles, DutyCycleSequenceLength]
        {
            { 0, 1, 0, 0, 0, 0, 0, 0 },
            { 0, 1, 1, 0, 0, 0, 0, 0 },
            { 0, 1, 1, 1, 1, 0, 0, 0 },
            { 1, 0, 0, 1, 1, 1, 1, 1 },
        };

        public readonly PulseChannelRegisters Registers = registers;

        // Pulse channel only updates every other CPU cycle
        private bool oddClock = true;

        private int timer = 0;
        private int cycleSequenceIndex = 0;

        private bool envelopeStartFlag = true;
        private int envelopeDecayLevel = envelopeDecayValueReset;
        private int envelopeDivider = 0;

        private bool sweepReloadFlag = true;
        private int sweepDivider = 0;
        private ushort sweepTargetPeriod = 0;

        private double currentSample = 0;

        private bool isMuted => Registers.Timer < 8 || sweepTargetPeriod > 0x7FF;

        public double GetSample()
        {
            if (isMuted || Registers.LengthCounter <= 0)
            {
                return 0;
            }

            return (Registers.ConstantVolume ? Registers.Volume : envelopeDecayLevel) * currentSample;
        }

        public void ClockTimer()
        {
            oddClock = !oddClock;

            if (oddClock)
            {
                int sweepChange = timer >> Registers.SweepShiftCount;
                if (Registers.SweepNegate)
                {
                    if (twosComplementSweep)
                    {
                        // NEGATE = two's complement (used by Pulse 2)
                        sweepChange = -sweepChange;
                    }
                    else
                    {
                        // NOT = one's complement (used by Pulse 1)
                        sweepChange = ~sweepChange;
                    }
                }
                sweepChange += timer;
                sweepTargetPeriod = (ushort)(sweepChange < 0 ? 0 : sweepChange);

                if (--timer < 0)
                {
                    // Timer wraps around from 0 to the current Timer register value.
                    // This causes the next cycle in the sequence to be selected, thus controlling the frequency.
                    timer = Registers.Timer;
                    currentSample = DutyCycleSequences[Registers.DutyCycle, cycleSequenceIndex++];
                    cycleSequenceIndex %= DutyCycleSequenceLength;
                }
            }
        }

        public void ClockEnvelope()
        {
            if (envelopeStartFlag)
            {
                envelopeDecayLevel = envelopeDecayValueReset;
                envelopeStartFlag = false;
                envelopeDivider = Registers.Volume;
            }
            else if (--envelopeDivider < 0)
            {
                envelopeDivider = Registers.Volume;
                if (envelopeDecayLevel > 0)
                {
                    envelopeDecayLevel--;
                    // Length counter halt being active also activates the envelope loop mode
                    if (envelopeDecayLevel == 0 && Registers.HaltLengthCounter)
                    {
                        envelopeDecayLevel = envelopeDecayValueReset;
                    }
                }
            }
        }

        public void ClockLengthCounter(bool enabled)
        {
            if (!enabled)
            {
                Registers.LengthCounter = 0;
            }
            else if (Registers is { LengthCounter: > 0, HaltLengthCounter: false })
            {
                Registers.LengthCounter--;
            }

            // Pulse Sweep is clocked at the same time as the length counter
            if (Registers.SweepEnabled && sweepDivider == 0 && Registers.SweepShiftCount != 0 && !isMuted)
            {
                Registers.Timer = sweepTargetPeriod;
            }

            if (sweepReloadFlag || sweepDivider == 0)
            {
                sweepDivider = Registers.SweepPeriod;
                sweepReloadFlag = false;
            }
            else
            {
                sweepDivider--;
            }
        }

        public void OnLengthCounterLoadWrite()
        {
            envelopeStartFlag = true;
            cycleSequenceIndex = 0;
        }

        public void OnSweepWrite()
        {
            sweepReloadFlag = true;
        }
    }

    public class TriangleChannel(TriangleChannelRegisters registers) : IChannel
    {
        public readonly TriangleChannelRegisters Registers = registers;

        public double GetSample()
        {
            throw new NotImplementedException();
        }

        public void ClockTimer()
        {
            throw new NotImplementedException();
        }

        public void ClockEnvelope()
        {
            // The Triangle channel does not have an envelope
        }

        public void ClockLengthCounter(bool enabled)
        {
            throw new NotImplementedException();
        }

        public void OnLengthCounterLoadWrite()
        {
            throw new NotImplementedException();
        }
    }

    public class NoiseChannel(NoiseChannelRegisters registers) : IChannel
    {
        public readonly NoiseChannelRegisters Registers = registers;

        public double GetSample()
        {
            throw new NotImplementedException();
        }

        public void ClockTimer()
        {
            throw new NotImplementedException();
        }

        public void ClockEnvelope()
        {
            throw new NotImplementedException();
        }

        public void ClockLengthCounter(bool enabled)
        {
            throw new NotImplementedException();
        }

        public void OnLengthCounterLoadWrite()
        {
            throw new NotImplementedException();
        }
    }

    public class DMCChannel(DMCChannelRegisters registers) : IChannel
    {
        public readonly DMCChannelRegisters Registers = registers;

        public double GetSample()
        {
            throw new NotImplementedException();
        }

        public void ClockTimer()
        {
            throw new NotImplementedException();
        }

        public void ClockEnvelope()
        {
            // The DMC channel does not have an envelope
        }

        public void ClockLengthCounter(bool enabled)
        {
            // The DMC channel does not have a length counter
        }
    }
}
