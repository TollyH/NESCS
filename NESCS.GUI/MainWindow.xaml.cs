using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace NESCS.GUI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, IDisposable
    {
        public NESSystem EmulatedNesSystem { get; }

        public bool EmulationRunning => emulationThread is { IsAlive: true };

        private readonly Controllers.StandardController controller = new();

        private readonly WriteableBitmap nesDisplayBitmap = new(
            PPU.VisibleCyclesPerFrame,
            PPU.VisibleScanlinesPerFrame,
            96,
            96,
            PixelFormats.Rgb24,
            null);

        private Thread? emulationThread = null;

        private CancellationTokenSource emulationCancellationTokenSource = new();

        private TimingDebugWindow? timingDebugWindow;

        private static readonly Int32Rect displayRect =
            new(0, 0, PPU.VisibleCyclesPerFrame, PPU.VisibleScanlinesPerFrame);

        public MainWindow()
        {
            EmulatedNesSystem = new NESSystem()
            {
                ControllerOne = controller
            };
            EmulatedNesSystem.FrameComplete += EmulatedNesSystem_FrameComplete;

            InitializeComponent();

            nesDisplay.Source = nesDisplayBitmap;
            SetDisplayScale(1.0);
        }

        ~MainWindow()
        {
            Dispose();
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            StopEmulation();
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

            // Block until emulation actually stops
            while (EmulationRunning) { }

            emulationCancellationTokenSource = new CancellationTokenSource();

            fpsStatusLabel.Content = "FPS: Paused";
        }

        public void ResetEmulation(bool powerCycle)
        {
            bool emulationWasRunning = EmulationRunning;

            StopEmulation();

            EmulatedNesSystem.Reset(powerCycle);

            if (emulationWasRunning)
            {
                StartEmulation();
            }
        }

        public void SetDisplayScale(double scale)
        {
            nesDisplay.Width = scale * PPU.VisibleCyclesPerFrame;
            nesDisplay.Height = scale * PPU.VisibleScanlinesPerFrame;
        }

        public void LoadROMFile(string file, bool reset)
        {
            try
            {
                ROMFile rom = new(File.ReadAllBytes(file));
                EmulatedNesSystem.InsertedCartridgeMapper = rom.InitializeNewMapper(EmulatedNesSystem);
                if (reset)
                {
                    ResetEmulation(false);
                }
            }
            catch (Exception exc)
            {
                MessageBox.Show(this,
                    $"An error occured loading the specified ROM file:\n{exc.Message}",
                    "ROM Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void PromptROMLoad(bool reset)
        {
            OpenFileDialog dialog = new()
            {
                CheckFileExists = true,
                Filter = "NES ROM Files (*.nes)|*.nes"
            };

            if (!dialog.ShowDialog(this) ?? false)
            {
                return;
            }

            LoadROMFile(dialog.FileName, reset);
        }

        private void OpenTimingDebugWindow()
        {
            if (timingDebugWindow is not null)
            {
                // Timing debug window is already open, focus it instead.
                timingDebugWindow.Focus();
                return;
            }

            timingDebugWindow = new TimingDebugWindow();
            timingDebugWindow.Closed += timingDebugWindow_Closed;
            timingDebugWindow.Show();
        }

        private void EmulationThreadStart()
        {
            EmulatedNesSystem.StartClock(emulationCancellationTokenSource.Token);
        }

        private unsafe void RenderDisplay(Color[,] pixels)
        {
            nesDisplayBitmap.Lock();

            Color* backBuffer = (Color*)nesDisplayBitmap.BackBuffer;
            fixed (Color* outputPixels = pixels)
            {
                for (int i = 0; i < pixels.Length; i++)
                {
                    backBuffer[i] = outputPixels[i];
                }
            }

            nesDisplayBitmap.AddDirtyRect(displayRect);

            nesDisplayBitmap.Unlock();
        }

        private void EmulatedNesSystem_FrameComplete(NESSystem nesSystem)
        {
            if (emulationCancellationTokenSource.IsCancellationRequested)
            {
                return;
            }

            try
            {
                Dispatcher.Invoke(() =>
                {
                    RenderDisplay(nesSystem.PpuCore.OutputPixels);

                    // This will be the frame time of the last frame, not this one,
                    // as this frame complete method is included in the process time.
                    fpsStatusLabel.Content = $"FPS: {1 / nesSystem.LastClockTime.TotalSeconds:N2}";

                    timingDebugWindow?.UpdateDisplays(nesSystem);
                }, DispatcherPriority.Render, emulationCancellationTokenSource.Token);
            }
            catch (TaskCanceledException) { }
        }

        private void ScaleItem_Click(object sender, RoutedEventArgs e)
        {
            foreach (MenuItem item in scaleMenuItem.Items.OfType<MenuItem>())
            {
                // Scale menu items should behave like radio buttons,
                // clicking on an item selects it and deselects everything else
                item.IsChecked = item == sender;
            }

            if (sender is MenuItem { IsChecked: true, Tag: double scale })
            {
                SetDisplayScale(scale);
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                // Emulator shortcuts
                case Key.O when e.KeyboardDevice.Modifiers == ModifierKeys.Control:
                    PromptROMLoad(true);
                    break;
                case Key.O when e.KeyboardDevice.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift):
                    PromptROMLoad(false);
                    break;
                case Key.P when e.KeyboardDevice.Modifiers == ModifierKeys.Control:
                    StartEmulation();
                    break;
                case Key.P when e.KeyboardDevice.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift):
                    StopEmulation();
                    break;
                case Key.R when e.KeyboardDevice.Modifiers == ModifierKeys.Control:
                    ResetEmulation(false);
                    break;
                case Key.R when e.KeyboardDevice.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift):
                    ResetEmulation(true);
                    break;
                // Debug shortcuts
                case Key.T when e.KeyboardDevice.Modifiers == (ModifierKeys.Control | ModifierKeys.Alt):
                    OpenTimingDebugWindow();
                    break;
                // Controller input
                // TODO: Make configurable
                case Key.W:
                    controller.HeldButtons |= Controllers.StandardControllerButtons.Up;
                    break;
                case Key.A:
                    controller.HeldButtons |= Controllers.StandardControllerButtons.Left;
                    break;
                case Key.S:
                    controller.HeldButtons |= Controllers.StandardControllerButtons.Down;
                    break;
                case Key.D:
                    controller.HeldButtons |= Controllers.StandardControllerButtons.Right;
                    break;
                case Key.U:
                    controller.HeldButtons |= Controllers.StandardControllerButtons.Select;
                    break;
                case Key.I:
                    controller.HeldButtons |= Controllers.StandardControllerButtons.Start;
                    break;
                case Key.J:
                    controller.HeldButtons |= Controllers.StandardControllerButtons.A;
                    break;
                case Key.K:
                    controller.HeldButtons |= Controllers.StandardControllerButtons.B;
                    break;
            }
        }

        private void Window_KeyUp(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                // Controller input
                // TODO: Make configurable
                case Key.W:
                    controller.HeldButtons &= ~Controllers.StandardControllerButtons.Up;
                    break;
                case Key.A:
                    controller.HeldButtons &= ~Controllers.StandardControllerButtons.Left;
                    break;
                case Key.S:
                    controller.HeldButtons &= ~Controllers.StandardControllerButtons.Down;
                    break;
                case Key.D:
                    controller.HeldButtons &= ~Controllers.StandardControllerButtons.Right;
                    break;
                case Key.U:
                    controller.HeldButtons &= ~Controllers.StandardControllerButtons.Select;
                    break;
                case Key.I:
                    controller.HeldButtons &= ~Controllers.StandardControllerButtons.Start;
                    break;
                case Key.J:
                    controller.HeldButtons &= ~Controllers.StandardControllerButtons.A;
                    break;
                case Key.K:
                    controller.HeldButtons &= ~Controllers.StandardControllerButtons.B;
                    break;
            }
        }

        private void LoadItem_Click(object sender, RoutedEventArgs e)
        {
            PromptROMLoad(true);
        }

        private void LoadNoResetItem_Click(object sender, RoutedEventArgs e)
        {
            PromptROMLoad(false);
        }

        private void StartItem_Click(object sender, RoutedEventArgs e)
        {
            StartEmulation();
        }

        private void StopItem_Click(object sender, RoutedEventArgs e)
        {
            StopEmulation();
        }

        private void SoftResetItem_Click(object sender, RoutedEventArgs e)
        {
            ResetEmulation(false);
        }

        private void HardResetItem_Click(object sender, RoutedEventArgs e)
        {
            ResetEmulation(true);
        }

        private void OpenTimingDebugItem_Click(object sender, RoutedEventArgs e)
        {
            OpenTimingDebugWindow();
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            StopEmulation();
        }

        private void timingDebugWindow_Closed(object? sender, EventArgs e)
        {
            timingDebugWindow = null;
        }
    }
}