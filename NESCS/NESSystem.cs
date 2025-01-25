using System.Diagnostics;

namespace NESCS
{
    public readonly record struct CycleClock(double FramesPerSecond, int PpuDotsPerFrame, double CpuClocksPerPpuDot)
    {
        public readonly long TicksPerFrame = (long)((1 / FramesPerSecond) * Stopwatch.Frequency);
    };

    public class NESSystem
    {
        private const int minimumSleepMs = 50;

        public static readonly CycleClock NtscClockPreset = new(
            60,
            89341,  // pre-render line is one dot shorter in every odd frame
            1 / 3d);
        public static readonly CycleClock PalClockPreset = new(
            50,
            106392,
            1 / 3.2);

        public CycleClock CurrentClock { get; set; } = NtscClockPreset;

        public readonly Memory SystemMemory;

        public readonly CPU CpuCore;

        public readonly PPU PpuCore;

        // This is floating point to account for clocks (like PAL)
        // where the CPU and PPU don't cleanly align
        private double pendingCpuCycles = 0;

        public NESSystem()
        {
            PpuCore = new PPU();
            SystemMemory = new Memory(PpuCore.Registers);
            CpuCore = new CPU(SystemMemory);
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
