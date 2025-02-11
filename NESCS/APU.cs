namespace NESCS
{
    public class APU(CPU cpuCore)
    {
        public readonly APURegisters Registers = new(cpuCore);
    }
}
