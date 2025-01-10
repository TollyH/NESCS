namespace NESCS
{
    public static class Extensions
    {
        public static ushort PostIncrementTwo(this ref ushort value)
        {
            ushort oldValue = value;
            value += 2;
            return oldValue;
        }
    }
}
