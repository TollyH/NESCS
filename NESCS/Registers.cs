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
        BreakFlag = 0b10000,
        Always = 0b100000,
        Overflow = 0b1000000,
        Negative = 0b10000000
    }

    public record Registers
    {
        public byte A { get; set; }
        public byte X { get; set; }
        public byte Y { get; set; }
        public ushort PC { get; set; }
        public byte S { get; set; }
        public StatusFlags P { get; set; }
    }
}
