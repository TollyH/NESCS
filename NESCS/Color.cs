namespace NESCS
{
    public readonly record struct Color(byte R, byte G, byte B)
    {
        public Color(uint packedValue) : this((byte)(packedValue >> 16), (byte)(packedValue >> 8), (byte)packedValue) { }
    }
}
