namespace NESCS
{
    [Flags]
    public enum StatusFlags : byte
    {
        None = 0,

        Carry = 0b1,
        Zero = 0b10,
        InterruptDisable = 0b100,
        Decimal = 0b1000,
        Break = 0b10000,
        Always = 0b100000,
        Overflow = 0b1000000,
        Negative = 0b10000000
    }

    public record Registers
    {
        public byte A;
        public byte X;
        public byte Y;
        public ushort PC;
        public byte S;
        public StatusFlags P;
    }
}
