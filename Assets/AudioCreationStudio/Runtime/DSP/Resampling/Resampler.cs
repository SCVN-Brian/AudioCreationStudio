using System;
using System.Threading;

namespace AudioCreationStudio.DSP
{
    /// <summary>Offline sample-rate converter built on <see cref="ISampleInterpolator"/>.</summary>
    public static class Resampler
    {
        /// <summary>Anti-aliasing margin applied when downsampling (cutoff = ratio * margin).</summary>
        public const float DownsampleMargin = 0.95f;

        public static AudioBuffer Resample(AudioBuffer src, int targetRate, ISampleInterpolator interpolator = null,
            CancellationToken ct = default)
        {
            if (targetRate <= 0) throw new ArgumentOutOfRangeException(nameof(targetRate));
            if (src.SampleRate == targetRate) return src.Clone();
            interpolator ??= SincInterpolator.Shared;

            double ratio = (double)targetRate / src.SampleRate;
            int outFrames = (int)Math.Round(src.Frames * ratio);
            var dst = new AudioBuffer(src.Channels, outFrames, targetRate);
            double step = 1.0 / ratio;
            float cutoff = ratio < 1.0 ? (float)(ratio * DownsampleMargin) : 1f;

            for (int c = 0; c < src.Channels; c++)
            {
                var s = src.GetChannel(c);
                var d = dst.GetChannel(c);
                for (int i = 0; i < outFrames; i++)
                {
                    if ((i & 4095) == 0) ct.ThrowIfCancellationRequested();
                    d[i] = interpolator.Sample(s, src.Frames, i * step, cutoff, false);
                }
            }
            return dst;
        }
    }
}
