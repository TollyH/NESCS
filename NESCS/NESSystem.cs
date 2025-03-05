using System.Diagnostics;

namespace NESCS
{
    public readonly record struct CycleClock(double FramesPerSecond, double CpuClocksPerPpuDot)
    {
        public readonly long TicksPerFrame = (long)((1 / FramesPerSecond) * Stopwatch.Frequency);
    };

    public readonly record struct FrameTiming(
        TimeSpan PpuProcessTime,
        TimeSpan CpuProcessTime,
        TimeSpan ApuProcessTime,
        TimeSpan FrameCompleteCallbackTime,
        TimeSpan TotalTime);

    public class NESSystem
    {
        public static readonly CycleClock NtscClockPreset = new(60, 1 / 3d);
        public static readonly CycleClock PalClockPreset = new(50, 1 / 3.2);

        public CycleClock CurrentClock { get; set; } = NtscClockPreset;

        /// <summary>
        /// The amount of time taken to process the last clock tick, including any delay time enforcing the frame rate limit.
        /// Only updated when using the <see cref="StartClock"/> method to provide clock timing.
        /// </summary>
        public TimeSpan LastClockTime { get; private set; }

        /// <summary>
        /// The amount of time taken to process each component in the last frame, not including any delay time to enforce the frame rate limit.
        /// If using <see cref="StartClock"/> to provide clock timing,
        /// <see cref="LastClockTime"/> can be used instead to access the actual rate that frames are being rendered at.
        /// </summary>
        public FrameTiming LastFrameProcessTime { get; private set; }

        public IMapper InsertedCartridgeMapper { get; set; } = new Mappers.Empty();

        public IController ControllerOne { get; set; } = new Controllers.Empty();
        public IController ControllerTwo { get; set; } = new Controllers.Empty();

        public readonly Memory SystemMemory;

        public readonly CPU CpuCore;

        public readonly PPU PpuCore;

        public readonly APU ApuCore;

        public event Action<NESSystem>? FrameComplete;

        // This is floating point to account for clocks (like PAL)
        // where the CPU and PPU don't cleanly align
        private double pendingCpuCycles = 0;

        public NESSystem()
        {
            PpuCore = new PPU(this, PPU.NtscScanlinesPerFrame);
            SystemMemory = new Memory(this);
            CpuCore = new CPU(SystemMemory);
            ApuCore = new APU(this);
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
                long clockStartTime = Stopwatch.GetTimestamp();

                long nextFrame = clockStartTime + CurrentClock.TicksPerFrame;

                ProcessFrame();

                while (Stopwatch.GetTimestamp() < nextFrame) { }

                LastClockTime = Stopwatch.GetElapsedTime(clockStartTime);
            }
        }

        public void ProcessFrame()
        {
            long frameStartTime = Stopwatch.GetTimestamp();

            TimeSpan ppuTotalTime = TimeSpan.Zero;
            TimeSpan cpuTotalTime = TimeSpan.Zero;
            TimeSpan apuTotalTime = TimeSpan.Zero;

            // Keep cycling both processors until the PPU signals that the frame is complete
            bool frameComplete = false;
            while (!frameComplete)
            {
                long ppuStartTime = Stopwatch.GetTimestamp();
                frameComplete = PpuCore.ProcessNextDot();
                ppuTotalTime += Stopwatch.GetElapsedTime(ppuStartTime);

                pendingCpuCycles += CurrentClock.CpuClocksPerPpuDot;

                int cpuCyclesThisDot = (int)pendingCpuCycles;
                pendingCpuCycles -= cpuCyclesThisDot;  // Keep just the fractional part

                for (int cycle = 0; cycle < cpuCyclesThisDot; cycle++)
                {
                    long cpuStartTime = Stopwatch.GetTimestamp();
                    CpuCore.ExecuteClockCycle();
                    cpuTotalTime += Stopwatch.GetElapsedTime(cpuStartTime);

                    long apuStartTime = Stopwatch.GetTimestamp();
                    ApuCore.ClockSequencer();
                    apuTotalTime += Stopwatch.GetElapsedTime(apuStartTime);
                }
            }

            long callbackStartTime = Stopwatch.GetTimestamp();
            FrameComplete?.Invoke(this);
            TimeSpan callbackTotalTime = Stopwatch.GetElapsedTime(callbackStartTime);

            // Drop APU output samples every frame to prevent the buffer filling indefinitely
            ApuCore.OutputSamples.Clear();

            LastFrameProcessTime = new FrameTiming(
                ppuTotalTime,
                cpuTotalTime,
                apuTotalTime,
                callbackTotalTime,
                Stopwatch.GetElapsedTime(frameStartTime));
        }
    }
}
