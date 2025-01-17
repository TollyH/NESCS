using System.Diagnostics;

namespace NESCS
{
    public readonly record struct CycleClock(double FramesPerSecond, double PpuDotsPerFrame, double CpuClocksPerPpuDot)
    {
        public readonly long TicksPerFrame = (long)((1 / FramesPerSecond) * Stopwatch.Frequency);
    };

    public class NESSystem
    {
        public const double NtscFramesPerSecond = 60;
        public const double NtscMainClockHz = 236250000 / 11d;
        public const double NtscPpuClockDivisor = 4;
        public const double NtscCpuClocksPerPpuDot = 1 / 3d;

        public const double PalFramesPerSecond = 50;
        public const double PalMainClockHz = 26601712.5;
        public const double PalPpuClockDivisor = 5;
        public const double PalCpuClocksPerPpuDot = 1 / 3.2;

        private const int minimumSleepMs = 50;

        public static readonly CycleClock NtscClockPreset = new(
            NtscFramesPerSecond,
            NtscMainClockHz / NtscPpuClockDivisor / NtscFramesPerSecond,
            NtscCpuClocksPerPpuDot);
        public static readonly CycleClock PalClockPreset = new(
            PalFramesPerSecond,
            PalMainClockHz / PalPpuClockDivisor / PalFramesPerSecond,
            PalCpuClocksPerPpuDot);

        public CycleClock CurrentClock { get; set; } = NtscClockPreset;

        public readonly Memory SystemMemory = new();

        public readonly CPU CpuCore;

        public readonly PPU PpuCore;

        // This is floating point to account for clocks (like PAL)
        // where the CPU and PPU don't cleanly align
        private double pendingCpuCycles = 0;

        public NESSystem()
        {
            CpuCore = new CPU(SystemMemory);
            PpuCore = new PPU(SystemMemory);
        }

        public void StartClock(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                long nextFrame = Stopwatch.GetTimestamp() + CurrentClock.TicksPerFrame;

                ProcessFrame();

                long currentTimestamp;
                while ((currentTimestamp = Stopwatch.GetTimestamp()) < nextFrame)
                {
                    int msDelay = (int)((nextFrame - currentTimestamp) * Stopwatch.Frequency / 1000);
                    if (msDelay >= minimumSleepMs)
                    {
                        // Delay for slightly less than required to prevent sleeping too long
                        Thread.Sleep(msDelay - minimumSleepMs);
                    }
                }
            }
        }

        public void ProcessFrame()
        {
            for (int dot = 0; dot < CurrentClock.PpuDotsPerFrame; dot++)
            {
                PpuCore.ProcessNextDot();

                int cpuCyclesThisDot = (int)pendingCpuCycles;
                pendingCpuCycles -= cpuCyclesThisDot;  // Keep just the fractional part
                pendingCpuCycles += CurrentClock.CpuClocksPerPpuDot;

                for (int cycle = 0; cycle < cpuCyclesThisDot; cycle++)
                {
                    CpuCore.ExecuteClockCycle();
                }
            }
        }
    }
}
