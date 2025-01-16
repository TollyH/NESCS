using System.Diagnostics;

namespace NESCS
{
    public readonly record struct CycleClock(double ClockHz, int CpuClockDivisor, int PpuClockDivisor, int ApuFrameCounterDivisor)
    {
        public readonly long TicksPerClock = (long)((1 / ClockHz) * Stopwatch.Frequency);
    };

    public class NESSystem
    {
        public static readonly CycleClock NtscClockPreset = new(236250000 / 11d, 12, 4, 3937500);  // 60 Hz
        public static readonly CycleClock PalClockPreset = new(26601712.5, 16, 5, 532034);  // 50 Hz

        public CycleClock CurrentClock { get; set; } = NtscClockPreset;

        public readonly Memory SystemMemory = new();

        public readonly Processor CpuCore;

        private int ticksUntilCpuTick = 0;

        public NESSystem()
        {
            CpuCore = new Processor(SystemMemory);
        }

        public void StartClock()
        {
            long nextTick = Stopwatch.GetTimestamp() + CurrentClock.TicksPerClock;
            ClockTick();

            while (Stopwatch.GetTimestamp() < nextTick) { }
        }

        public void ClockTick()
        {
            if (--ticksUntilCpuTick <= 0)
            {
                CpuCore.ExecuteClockCycle();
                ticksUntilCpuTick = CurrentClock.CpuClockDivisor;
            }
        }
    }
}
