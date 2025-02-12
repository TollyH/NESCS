namespace NESCS.Controllers
{
    public class Empty : IController
    {
        public bool StrobeLatch { set { } }

        public bool ReadClock()
        {
            return false;
        }
    }
}
