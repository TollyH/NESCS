using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NESCS.GUI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public NESSystem EmulatedNesSystem { get; private set; }

        public bool EmulationRunning => emulationThread is { IsAlive: true };

        private readonly WriteableBitmap nesDisplayBitmap = new(
            PPU.VisibleCyclesPerFrame,
            PPU.VisibleScanlinesPerFrame,
            96,
            96,
            PixelFormats.Rgb24,
            null);

        private Thread? emulationThread = null;

        private CancellationTokenSource emulationCancellationTokenSource = new();

        private static readonly Int32Rect displayRect =
            new(0, 0, PPU.VisibleCyclesPerFrame, PPU.VisibleScanlinesPerFrame);

        public MainWindow()
        {
            EmulatedNesSystem = new NESSystem();
            EmulatedNesSystem.FrameComplete += EmulatedNesSystem_FrameComplete;

            InitializeComponent();

            nesDisplay.Source = nesDisplayBitmap;
            SetDisplayScale(1.0);
        }

        public void StartEmulation()
        {
            if (EmulationRunning)
            {
                // Emulator is already running - nothing to do
                return;
            }

            emulationThread = new Thread(EmulationThreadStart);
            emulationThread.Start();
        }

        public void StopEmulation()
        {
            emulationCancellationTokenSource.Cancel();
            emulationCancellationTokenSource = new CancellationTokenSource();

            // Block until emulation actually stops
            while (EmulationRunning) { }
        }

        public void ResetEmulation(bool powerCycle)
        {
            StopEmulation();

            EmulatedNesSystem.Reset(powerCycle);
        }

        public void SetDisplayScale(double scale)
        {
            nesDisplay.Width = scale * PPU.VisibleCyclesPerFrame;
            nesDisplay.Height = scale * PPU.VisibleScanlinesPerFrame;
        }

        private unsafe void EmulatedNesSystem_FrameComplete(NESSystem nesSystem)
        {
            Dispatcher.Invoke(() =>
            {
                nesDisplayBitmap.Lock();

                Color* backBuffer = (Color*)nesDisplayBitmap.BackBuffer;
                fixed (Color* outputPixels = nesSystem.PpuCore.OutputPixels)
                {
                    for (int i = 0; i < nesSystem.PpuCore.OutputPixels.Length; i++)
                    {
                        backBuffer[i] = outputPixels[i];
                    }
                }

                nesDisplayBitmap.AddDirtyRect(displayRect);

                nesDisplayBitmap.Unlock();
            });
        }

        private void EmulationThreadStart()
        {
            EmulatedNesSystem.StartClock(emulationCancellationTokenSource.Token);
        }
    }
}