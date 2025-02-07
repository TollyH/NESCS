using System.Diagnostics;

namespace NESCS
{
    public readonly record struct CycleClock(double FramesPerSecond, double CpuClocksPerPpuDot)
    {
        public readonly long TicksPerFrame = (long)((1 / FramesPerSecond) * Stopwatch.Frequency);
    };

    public class NESSystem
    {
        public static readonly CycleClock NtscClockPreset = new(60, 1 / 3d);
        public static readonly CycleClock PalClockPreset = new(50, 1 / 3.2);

        public CycleClock CurrentClock { get; set; } = NtscClockPreset;

        public IMapper InsertedCartridgeMapper { get; set; } = new Mappers.Empty();

        public readonly Memory SystemMemory;

        public readonly CPU CpuCore;

        public readonly PPU PpuCore;

        public event Action<NESSystem>? FrameComplete;

        // This is floating point to account for clocks (like PAL)
        // where the CPU and PPU don't cleanly align
        private double pendingCpuCycles = 0;

        public NESSystem()
        {
            PpuCore = new PPU(this);
            SystemMemory = new Memory(this);
            CpuCore = new CPU(SystemMemory);
        }

        /// <summary>
        /// Simulate a reset of the system, either via the reset button or via a power cycle.
        /// </summary>
        public void Reset(bool powerCycle)
        {
            CpuCore.Reset(powerCycle);
            PpuCore.Reset(powerCycle);
        }

        public void StartClock(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                long nextFrame = Stopwatch.GetTimestamp() + CurrentClock.TicksPerFrame;

                ProcessFrame();

                while (Stopwatch.GetTimestamp() < nextFrame) { }
            }
        }

        public void ProcessFrame()
        {
            // Keep cycling both processors until the PPU signals that the frame is complete
            while (!PpuCore.ProcessNextDot())
            {
                pendingCpuCycles += CurrentClock.CpuClocksPerPpuDot;

                int cpuCyclesThisDot = (int)pendingCpuCycles;
                pendingCpuCycles -= cpuCyclesThisDot;  // Keep just the fractional part

                for (int cycle = 0; cycle < cpuCyclesThisDot; cycle++)
                {
                    CpuCore.ExecuteClockCycle();
                }
            }

            FrameComplete?.Invoke(this);
        }
    }
}
