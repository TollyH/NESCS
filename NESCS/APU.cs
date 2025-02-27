namespace NESCS
{
    public class APU(NESSystem nesSystem)
    {
        public ulong ExecutedCycles { get; private set; }

        public readonly APURegisters Registers = new(nesSystem);

        public void ClockSequencer()
        {
            ExecutedCycles++;
        }
    }
}
