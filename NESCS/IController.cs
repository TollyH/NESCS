namespace NESCS
{
    public interface IController
    {
        public bool StrobeLatch { set; }

        public bool ReadClock();
    }
}
