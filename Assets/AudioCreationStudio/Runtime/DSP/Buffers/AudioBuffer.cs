using System;

namespace AudioCreationStudio.DSP
{
    /// <summary>
    /// Planar 32-bit float audio buffer. The buffer owns its channel arrays.
    /// Not thread-safe: a buffer is either owned by one render job or treated as immutable once published.
    /// </summary>
    public sealed class AudioBuffer
    {
        private readonly float[][] _channels;

        public int Channels => _channels.Length;
        public int Frames { get; private set; }
        public int Capacity => _channels[0].Length;
        public int SampleRate { get; set; }
        public double DurationSeconds => (double)Frames / SampleRate;

        public AudioBuffer(int channels, int frames, int sampleRate)
        {
            if (channels < 1 || channels > 8) throw new ArgumentOutOfRangeException(nameof(channels));
            if (frames < 0) throw new ArgumentOutOfRangeException(nameof(frames));
            if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
            _channels = new float[channels][];
            for (int c = 0; c < channels; c++) _channels[c] = new float[frames];
            Frames = frames;
            SampleRate = sampleRate;
        }

        public float[] GetChannel(int channel) => _channels[channel];

        public float this[int channel, int frame]
        {
            get => _channels[channel][frame];
            set => _channels[channel][frame] = value;
        }

        public void Clear()
        {
            foreach (var ch in _channels) Array.Clear(ch, 0, Frames);
        }

        public void Clear(int offset, int count)
        {
            if (count <= 0) return;
            foreach (var ch in _channels) Array.Clear(ch, offset, count);
        }

        /// <summary>Changes the logical length, growing capacity (content preserved) if necessary.</summary>
        public void SetFrames(int frames)
        {
            if (frames < 0) throw new ArgumentOutOfRangeException(nameof(frames));
            if (frames > Capacity)
            {
                for (int c = 0; c < _channels.Length; c++)
                {
                    var arr = _channels[c];
                    Array.Resize(ref arr, frames);
                    _channels[c] = arr;
                }
            }
            else if (frames > Frames)
            {
                // Ensure stale data beyond the old logical end is silent.
                Clear(Frames, frames - Frames);
            }
            Frames = frames;
        }

        public AudioBuffer Clone() => Slice(0, Frames);

        public AudioBuffer Slice(int start, int count)
        {
            start = Math.Max(0, start);
            count = Math.Max(0, Math.Min(count, Frames - start));
            var b = new AudioBuffer(Channels, count, SampleRate);
            for (int c = 0; c < Channels; c++) Array.Copy(_channels[c], start, b._channels[c], 0, count);
            return b;
        }

        public static AudioBuffer FromInterleaved(float[] interleaved, int channels, int sampleRate)
            => FromInterleaved(interleaved, interleaved.Length, channels, sampleRate);

        public static AudioBuffer FromInterleaved(float[] interleaved, int sampleCount, int channels, int sampleRate)
        {
            int frames = sampleCount / channels;
            var b = new AudioBuffer(channels, frames, sampleRate);
            for (int c = 0; c < channels; c++)
            {
                var dst = b._channels[c];
                for (int i = 0, j = c; i < frames; i++, j += channels) dst[i] = interleaved[j];
            }
            return b;
        }

        public float[] ToInterleaved()
        {
            var result = new float[Frames * Channels];
            ToInterleaved(result, 0, Frames);
            return result;
        }

        public void ToInterleaved(float[] dest, int frameOffset, int frames)
        {
            int ch = Channels;
            for (int c = 0; c < ch; c++)
            {
                var src = _channels[c];
                for (int i = 0, j = c; i < frames; i++, j += ch) dest[j] = src[frameOffset + i];
            }
        }

        /// <summary>Maximum absolute sample value over all channels.</summary>
        public float GetPeak() => GetPeak(0, Frames);

        public float GetPeak(int start, int count)
        {
            float peak = 0f;
            int end = Math.Min(Frames, start + count);
            foreach (var ch in _channels)
                for (int i = Math.Max(0, start); i < end; i++)
                {
                    float a = ch[i] < 0 ? -ch[i] : ch[i];
                    if (a > peak) peak = a;
                }
            return peak;
        }

        public void Scale(float gain)
        {
            foreach (var ch in _channels)
                for (int i = 0; i < Frames; i++) ch[i] *= gain;
        }

        /// <summary>Replaces NaN/Inf with zero. Returns the number of invalid samples found.</summary>
        public int Sanitize()
        {
            int bad = 0;
            foreach (var ch in _channels)
                for (int i = 0; i < Frames; i++)
                {
                    float v = ch[i];
                    if (float.IsNaN(v) || float.IsInfinity(v)) { ch[i] = 0f; bad++; }
                }
            return bad;
        }

        /// <summary>Adds <paramref name="src"/> into this buffer. Channel counts must match.</summary>
        public void AddFrom(AudioBuffer src, int srcOffset, int dstOffset, int frames, float gain = 1f)
        {
            if (src.Channels != Channels) throw new ArgumentException("Channel count mismatch");
            frames = Math.Min(frames, Math.Min(src.Frames - srcOffset, Frames - dstOffset));
            for (int c = 0; c < Channels; c++)
            {
                var s = src._channels[c];
                var d = _channels[c];
                for (int i = 0; i < frames; i++) d[dstOffset + i] += s[srcOffset + i] * gain;
            }
        }
    }
}
