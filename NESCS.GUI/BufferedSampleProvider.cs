using System.Diagnostics;
using NAudio.Wave;

namespace NESCS.GUI
{
    public class BufferedSampleProvider(int sampleRate) : ISampleProvider
    {
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1);

        public int BufferedSampleCount => sampleBuffer.Count;

        public int MaxBufferSize { get; set; } = int.MaxValue;

        /// <summary>
        /// This is the last measured real-time between a sample being added to the buffer and being read back off.
        /// The time between a sample being read and being played back to the user is not included.
        /// </summary>
        public TimeSpan LastMeasuredReadLatency { get; private set; }

        private readonly List<float> sampleBuffer = new();

        private long latencyMeasure;
        private int latencyMeasureSamplesRemaining;

        public void BufferSamples(IEnumerable<float> samples)
        {
            sampleBuffer.AddRange(samples);

            if (sampleBuffer.Count > MaxBufferSize)
            {
                sampleBuffer.RemoveRange(0, sampleBuffer.Count - MaxBufferSize);
            }

            // Latency is measured by seeing how much time there is between samples being
            // added to the buffer and the last of those samples being read off the buffer.
            // It does not include the latency between samples being
            // read off the buffer and being played back to the user.
            // Only one of these measurements needs to occur at once,
            // hence wait for any current measurement to end before starting another one.
            if (latencyMeasureSamplesRemaining <= 0)
            {
                latencyMeasure = Stopwatch.GetTimestamp();
                latencyMeasureSamplesRemaining = sampleBuffer.Count;
            }
        }

        public int Read(float[] buffer, int offset, int sampleCount)
        {
            if (sampleCount > sampleBuffer.Count)
            {
                sampleCount = sampleBuffer.Count;
            }

            if (offset + sampleCount > buffer.Length)
            {
                throw new IndexOutOfRangeException("Target buffer is too small to take the specified number of samples at the given offset");
            }

            for (int i = 0; i < sampleCount; i++)
            {
                buffer[i + offset] = sampleBuffer[i];
            }

            sampleBuffer.RemoveRange(0, sampleCount);

            latencyMeasureSamplesRemaining -= sampleCount;
            if (latencyMeasureSamplesRemaining <= 0)
            {
                LastMeasuredReadLatency = Stopwatch.GetElapsedTime(latencyMeasure);
            }

            return sampleCount;
        }
    }
}
