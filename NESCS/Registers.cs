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
        /// <summary>
        /// Accumulator
        /// </summary>
        public byte A;
        /// <summary>
        /// X Index
        /// </summary>
        public byte X;
        /// <summary>
        /// Y Index
        /// </summary>
        public byte Y;
        /// <summary>
        /// Program Counter
        /// </summary>
        public ushort PC;
        /// <summary>
        /// Stack Pointer
        /// </summary>
        public byte S;
        /// <summary>
        /// Status
        /// </summary>
        public StatusFlags P = StatusFlags.Always;
    }
}
