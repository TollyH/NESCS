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
                EmulatedNesSystem.InsertedCartridgeMapper = rom.InitializeNewMapper();
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

        private void EmulationThreadStart()
        {
            EmulatedNesSystem.StartClock(emulationCancellationTokenSource.Token);
        }

        private unsafe void EmulatedNesSystem_FrameComplete(NESSystem nesSystem)
        {
            if (emulationCancellationTokenSource.IsCancellationRequested)
            {
                return;
            }

            try
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

        private void Window_Closed(object sender, EventArgs e)
        {
            StopEmulation();
        }
    }
}