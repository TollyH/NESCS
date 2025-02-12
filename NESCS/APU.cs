namespace NESCS
{
    public class APU(NESSystem nesSystem)
    {
        public readonly APURegisters Registers = new(nesSystem);
    }
}
