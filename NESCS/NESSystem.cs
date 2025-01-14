namespace NESCS
{
    public record struct CycleClock(double ClockHz, double CpuClockDivisor, double PpuClockDivisor, double ApuFrameCounterDivisor);

    public class NESSystem
    {
        public static readonly CycleClock NtscClockPreset = new(236250000 / 11d, 12, 4, 3937500);  // 60 Hz
        public static readonly CycleClock PalClockPreset = new(26601712.5, 16, 5, 532034.25);  // 50 Hz

        public CycleClock CurrentClock { get; set; } = NtscClockPreset;

        public readonly Memory SystemMemory = new();

        public readonly Processor CpuCore;

        public NESSystem()
        {
            CpuCore = new Processor(SystemMemory);
        }
    }
}
