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

        // When the length counter load register is written,
        // the actual value of the length counter is loaded from this table using the written value as an index.
        public static readonly byte[] LengthCounterLoadTable = new byte[0x20]
        {
            0x0A, 0xFE, 0x14, 0x02, 0x28, 0x04, 0x50, 0x06, 0xA0, 0x08, 0x3C, 0x0A, 0x0E, 0x0C, 0x1A, 0x0E,
            0x0C, 0x10, 0x18, 0x12, 0x30, 0x14, 0x60, 0x16, 0xC0, 0x18, 0x48, 0x1A, 0x10, 0x1C, 0x20, 0x1E
        };

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

        /// <summary>
        /// All audio samples produced in the current frame. Each sample is between 0.0 and 1.0.
        /// </summary>
        public readonly List<float> OutputSamples = new();

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

            OutputSamples.Add(GetMixedOutputSample());

            if ((Registers.StatusControl & StatusControlFlags.FrameInterrupt) != 0)
            {
                nesSystem.CpuCore.InterruptRequest();
            }

            CurrentCycle++;
        }

        private float GetMixedOutputSample()
        {
            // These formulas replicate the weighting that the real NES puts on each channel in the final mix.
            // The final sample will always be between 0.0 and 1.0.
            float pulseOutput = 95.88f / (8128.0f / (Pulse1.GetSample() + Pulse2.GetSample()) + 100);
            float tndOutput = 159.79f / (1 / ((Triangle.GetSample() / 8227.0f) + (Noise.GetSample() / 12241.0f) + (Dmc.GetSample() / 22638.0f)) + 100);
            return pulseOutput + tndOutput;
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
        public byte GetSample();

        public void ClockTimer();

        public void ClockEnvelope();
    }

    /// <summary>
    /// Represents an audio channel that supports volume envelope (Pulse and Noise in the NES).
    /// </summary>
    /// <typeparam name="T">The type that contains that channel's registers</typeparam>
    public abstract class EnvelopedChannel<T>(T registers) : IChannel
        where T : IEnvelopedChannelRegisters
    {
        protected const int envelopeDecayValueReset = 15;

        public readonly T Registers = registers;

        protected bool envelopeStartFlag = true;
        protected byte envelopeDecayLevel = envelopeDecayValueReset;
        protected int envelopeDivider = 0;

        public abstract byte GetSample();

        public abstract void ClockTimer();

        public virtual void ClockEnvelope()
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

        public virtual void OnLengthCounterLoadWrite()
        {
            envelopeStartFlag = true;
        }
    }

    public class PulseChannel(PulseChannelRegisters registers, bool twosComplementSweep) : EnvelopedChannel<PulseChannelRegisters>(registers)
    {
        public const int DutyCycles = 4;
        public const int DutyCycleSequenceLength = 8;

        public static readonly byte[,] DutyCycleSequences = new byte[DutyCycles, DutyCycleSequenceLength]
        {
            { 0, 1, 0, 0, 0, 0, 0, 0 },
            { 0, 1, 1, 0, 0, 0, 0, 0 },
            { 0, 1, 1, 1, 1, 0, 0, 0 },
            { 1, 0, 0, 1, 1, 1, 1, 1 },
        };

        // Pulse channel only updates every other CPU cycle
        private bool oddClock = true;

        private int timer = 0;
        private int cycleSequenceIndex = 0;

        private bool sweepReloadFlag = true;
        private int sweepDivider = 0;
        private ushort sweepTargetPeriod = 0;

        private byte currentSample = 0;

        private bool isMuted => Registers.Timer < 8 || sweepTargetPeriod > 0x7FF;

        public override byte GetSample()
        {
            if (isMuted || Registers.LengthCounter <= 0)
            {
                return 0;
            }

            return (byte)((Registers.ConstantVolume ? Registers.Volume : envelopeDecayLevel) * currentSample);
        }

        public override void ClockTimer()
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

        public override void OnLengthCounterLoadWrite()
        {
            base.OnLengthCounterLoadWrite();

            cycleSequenceIndex = 0;
            Registers.LengthCounter = APU.LengthCounterLoadTable[Registers.LengthCounter];
        }

        public void OnSweepWrite()
        {
            sweepReloadFlag = true;
        }
    }

    public class TriangleChannel(TriangleChannelRegisters registers) : IChannel
    {
        public const int SampleSequenceLength = 0x20;

        public static readonly byte[] SampleSequence = new byte[SampleSequenceLength]
        {
            0x0F, 0x0E, 0x0D, 0x0C, 0x0B, 0x0A, 0x09, 0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01, 0x00,
            0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F
        };

        public readonly TriangleChannelRegisters Registers = registers;

        private int timer = 0;
        private int cycleSequenceIndex = 0;

        private bool linearCounterReloadFlag = true;
        private int linearCounter = 0;

        private byte currentSample = 0;

        public byte GetSample()
        {
            return currentSample;
        }

        public void ClockTimer()
        {
            if (--timer < 0)
            {
                // Timer wraps around from 0 to the current Timer register value.
                // This causes the next cycle in the sequence to be selected, thus controlling the frequency.
                timer = Registers.Timer;
                if (linearCounter != 0 && Registers.LengthCounter != 0)
                {
                    currentSample = SampleSequence[cycleSequenceIndex++];
                    cycleSequenceIndex %= SampleSequenceLength;
                }
            }
        }

        public void ClockEnvelope()
        {
            // Envelope clock is used to clock Triangle's linear counter
            if (linearCounterReloadFlag)
            {
                linearCounter = Registers.CounterReloadValue;
            }
            else if (linearCounter != 0)
            {
                linearCounter--;
            }

            if (!Registers.ControlFlag)
            {
                linearCounterReloadFlag = false;
            }
        }

        public void ClockLengthCounter(bool enabled)
        {
            if (!enabled)
            {
                Registers.LengthCounter = 0;
            }
            else if (Registers is { LengthCounter: > 0, ControlFlag: false })
            {
                Registers.LengthCounter--;
            }
        }

        public void OnLengthCounterLoadWrite()
        {
            linearCounterReloadFlag = true;
        }
    }

    public class NoiseChannel(NoiseChannelRegisters registers) : EnvelopedChannel<NoiseChannelRegisters>(registers)
    {
        public static readonly int[] NtscTimerPeriods = new int[0x10]
        {
            4, 8, 16, 32, 64, 96, 128, 160, 202, 254, 380, 508, 762, 1016, 2034, 4068
        };
        public static readonly int[] PalTimerPeriods = new int[0x10]
        {
            4, 8, 14, 30, 60, 88, 118, 148, 188, 236, 354, 472, 708,  944, 1890, 3778
        };

        public int[] CurrentTimerPeriods { get; set; } = NtscTimerPeriods;

        private int timer = 0;

        private int shiftRegister = 1;

        public override byte GetSample()
        {
            if (Registers.LengthCounter <= 0 || (shiftRegister & 1) != 0)
            {
                return 0;
            }

            return Registers.ConstantVolume ? Registers.Volume : envelopeDecayLevel;
        }

        public override void ClockTimer()
        {
            if (--timer < 0)
            {
                timer = CurrentTimerPeriods[Registers.Period];

                int feedback = (byte)((shiftRegister & 1) ^ (Registers.LoopMode ? ((shiftRegister & 0b1000000) >> 6) : ((shiftRegister & 0b10) >> 1)));
                shiftRegister >>= 1;
                shiftRegister |= feedback << 14;
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
        }
    }

    public class DMCChannel(DMCChannelRegisters registers) : IChannel
    {
        public readonly DMCChannelRegisters Registers = registers;

        public byte GetSample()
        {
            return 0;
        }

        public void ClockTimer()
        {

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
