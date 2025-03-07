using NAudio.Wave;

namespace NESCS.GUI
{
    public class BufferedSampleProvider(int sampleRate) : ISampleProvider
    {
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1);

        private readonly List<float> sampleBuffer = new();

        public void BufferSamples(IEnumerable<float> samples)
        {
            sampleBuffer.AddRange(samples);
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

            return sampleCount;
        }
    }
}
