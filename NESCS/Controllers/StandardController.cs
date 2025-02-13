namespace NESCS.Controllers
{
    [Flags]
    public enum StandardControllerButtons : byte
    {
        None = 0,

        A = 0b1,
        B = 0b10,
        Select = 0b100,
        Start = 0b1000,
        Up = 0b10000,
        Down = 0b100000,
        Left = 0b1000000,
        Right = 0b10000000,
    }

    public class StandardController : IController
    {
        private bool _strobeLatch = false;
        public bool StrobeLatch
        {
            private get => _strobeLatch;
            set
            {
                _strobeLatch = value;
                RefreshShiftRegister();
            }
        }

        private StandardControllerButtons _heldButtons = StandardControllerButtons.None;
        public StandardControllerButtons HeldButtons
        {
            get => _heldButtons;
            set
            {
                _heldButtons = value;
                RefreshShiftRegister();
            }
        }

        private byte readShiftRegister = 0;

        public bool ReadClock()
        {
            RefreshShiftRegister();

            bool buttonPressed = (readShiftRegister & 1) != 0;
            readShiftRegister >>= 1;
            return buttonPressed;
        }

        private void RefreshShiftRegister()
        {
            if (StrobeLatch)
            {
                readShiftRegister = (byte)HeldButtons;
            }
        }
    }
}
