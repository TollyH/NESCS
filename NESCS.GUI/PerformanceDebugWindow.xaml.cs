using System.Windows;
using System.Windows.Media;

namespace NESCS.GUI
{
    /// <summary>
    /// Interaction logic for PerformanceDebugWindow.xaml
    /// </summary>
    public partial class PerformanceDebugWindow : Window
    {
        public PerformanceDebugWindow()
        {
            InitializeComponent();
        }

        public void UpdateDisplays(NESSystem system)
        {
            double targetMsPerFrame = 1000 / system.CurrentClock.FramesPerSecond;

            frameRateText.Text = $"Frame Rate: {1 / system.LastClockTime.TotalSeconds:N1} FPS";
            frameTimeText.Text = $"Frame Time: {system.LastClockTime.TotalMilliseconds:N1} ms";
            frameTargetText.Text = $"Target: {system.CurrentClock.FramesPerSecond:N1} FPS / {targetMsPerFrame:N1} ms";

            bool onTarget = system.LastFrameProcessTime.TotalTime.TotalMilliseconds <= targetMsPerFrame;

            if (onTarget)
            {
                double waitTime = targetMsPerFrame - system.LastFrameProcessTime.TotalTime.TotalMilliseconds;

                targetHitText.Text = "On Target";
                targetHitText.Foreground = Brushes.Green;

                targetDetailText.Text = $"{waitTime:N1} ms ({waitTime / targetMsPerFrame:P1}) to spare";
                targetDetailText.Foreground = Brushes.Green;
            }
            else
            {
                double overTime = system.LastFrameProcessTime.TotalTime.TotalMilliseconds - targetMsPerFrame;

                targetHitText.Text = "Behind Target (Running Slow)";
                targetHitText.Foreground = Brushes.DarkRed;

                targetDetailText.Text = $"Taking {overTime:N1} ms ({overTime / targetMsPerFrame:P1}) too long";
                targetDetailText.Foreground = Brushes.DarkRed;
            }

            TimeSpan otherTime = system.LastFrameProcessTime.TotalTime
                - system.LastFrameProcessTime.PpuProcessTime
                - system.LastFrameProcessTime.CpuProcessTime
                - system.LastFrameProcessTime.FrameCompleteCallbackTime;

            double ppuProportion = system.LastFrameProcessTime.PpuProcessTime / system.LastClockTime;
            double cpuProportion = system.LastFrameProcessTime.CpuProcessTime / system.LastClockTime;
            double callbackProportion = system.LastFrameProcessTime.FrameCompleteCallbackTime / system.LastClockTime;
            double otherProportion = otherTime / system.LastClockTime;

            ppuTimeBar.Width = ppuProportion * barPanel.ActualWidth;
            cpuTimeBar.Width = cpuProportion * barPanel.ActualWidth;
            callbackTimeBar.Width = callbackProportion * barPanel.ActualWidth;
            otherTimeBar.Width = otherProportion * barPanel.ActualWidth;

            ppuTimeText.Text = $"{system.LastFrameProcessTime.PpuProcessTime.TotalMilliseconds:N1} ms ({ppuProportion:P1})";
            cpuTimeText.Text = $"{system.LastFrameProcessTime.CpuProcessTime.TotalMilliseconds:N1} ms ({cpuProportion:P1})";
            callbackTimeText.Text = $"{system.LastFrameProcessTime.FrameCompleteCallbackTime.TotalMilliseconds:N1} ms ({callbackProportion:P1})";
            otherTimeText.Text = $"{otherTime.TotalMilliseconds:N1} ms ({otherProportion:P1})";
            totalTimeText.Text = $"{system.LastFrameProcessTime.TotalTime.TotalMilliseconds:N1} ms ({system.LastFrameProcessTime.TotalTime / system.LastClockTime:P1})";
        }
    }
}
