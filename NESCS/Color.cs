using System.Runtime.InteropServices;

namespace NESCS
{
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct Color(byte r, byte g, byte b)
    {
        public readonly byte R = r;
        public readonly byte G = g;
        public readonly byte B = b;

        public Color(uint packedValue) : this((byte)(packedValue >> 16), (byte)(packedValue >> 8), (byte)packedValue) { }
    }
}
