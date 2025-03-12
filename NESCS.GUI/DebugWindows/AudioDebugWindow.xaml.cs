using System.Windows;
using System.Windows.Media;

namespace NESCS.GUI.DebugWindows
{
    /// <summary>
    /// Interaction logic for AudioDebugWindow.xaml
    /// </summary>
    public partial class AudioDebugWindow : Window
    {
        public AudioChannel SelectedAudioChannel =>
            isolateChannelRadioPulse1.IsChecked ?? false
            ? AudioChannel.Pulse1
            : isolateChannelRadioPulse2.IsChecked ?? false
            ? AudioChannel.Pulse2
            : isolateChannelRadioTriangle.IsChecked ?? false
            ? AudioChannel.Triangle
            : isolateChannelRadioNoise.IsChecked ?? false
            ? AudioChannel.Noise
            : isolateChannelRadioDmc.IsChecked ?? false
            ? AudioChannel.Dmc
            : AudioChannel.Mix;

        private int lagCounter = 0;

        public AudioDebugWindow()
        {
            InitializeComponent();
        }

        public void UpdateDisplays(BufferedSampleProvider sampleBuffer, NESSystem nesSystem, int samplesPerFrame, int targetSampleRate)
        {
            bufferSizeText.Text = $"Buffered Sample Count: {sampleBuffer.BufferedSampleCount:N0}";
            bufferLatencyText.Text = $"Buffer Read Latency: {sampleBuffer.LastMeasuredReadLatency.TotalMilliseconds:N1} ms";
            nesSampleRateText.Text = $"Source Sample Rate: {nesSystem.AudioSampleRate:N0} Hz ({samplesPerFrame:N1} s/f)";

            double downsampleFactor = (double)targetSampleRate / nesSystem.AudioSampleRate;
            outputSampleRateText.Text = $"Output Sample Rate: {targetSampleRate:N0} Hz ({downsampleFactor * samplesPerFrame:N1} s/f)";

            if (sampleBuffer.BufferedSampleCount == 0)
            {
                lagCounter++;

                bufferHealthText.Text = "Buffer is Starved";
                bufferHealthText.Foreground = Brushes.DarkRed;
            }
            else
            {
                bufferHealthText.Text = "Buffer is Healthy";
                bufferHealthText.Foreground = Brushes.Green;
            }

            lagCounterText.Text = $"Starved frames: {lagCounter}";
        }
    }
}
