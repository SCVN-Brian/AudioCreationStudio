using System;

namespace AudioCreationStudio.DSP
{
    public interface ISampleInterpolator
    {
        /// <summary>
        /// Reads <paramref name="data"/> at fractional frame <paramref name="position"/>.
        /// <paramref name="cutoff"/> is the low-pass cutoff relative to the source Nyquist (0..1];
        /// values below 1 band-limit the read for downsampling / pitching up.
        /// </summary>
        float Sample(float[] data, int length, double position, float cutoff, bool wrap);
    }

    /// <summary>
    /// Blackman-windowed sinc interpolator with a precomputed kernel table. Used both for preview and
    /// export (identical code path), for playback-rate changes and sample-rate conversion.
    /// Weights are normalised so constant (DC) signals are reproduced exactly.
    /// </summary>
    public sealed class SincInterpolator : ISampleInterpolator
    {
        public const int DefaultHalfWidth = 8;
        private const int Resolution = 512;

        private readonly int _halfWidth;
        private readonly float[] _table;

        public static readonly SincInterpolator Shared = new SincInterpolator();

        public int HalfWidth => _halfWidth;

        public SincInterpolator(int halfWidth = DefaultHalfWidth)
        {
            _halfWidth = Math.Max(2, halfWidth);
            int n = _halfWidth * Resolution + 2;
            _table = new float[n];
            for (int i = 0; i < n; i++)
            {
                double x = (double)i / Resolution;
                if (x >= _halfWidth) { _table[i] = 0f; continue; }
                double sinc = x < 1e-12 ? 1.0 : Math.Sin(Math.PI * x) / (Math.PI * x);
                double t = x / _halfWidth; // 0..1
                double window = 0.42 + 0.5 * Math.Cos(Math.PI * t) + 0.08 * Math.Cos(2.0 * Math.PI * t);
                _table[i] = (float)(sinc * window);
            }
        }

        private float Kernel(double x)
        {
            if (x < 0) x = -x;
            if (x >= _halfWidth) return 0f;
            double idx = x * Resolution;
            int i = (int)idx;
            float f = (float)(idx - i);
            return _table[i] + (_table[i + 1] - _table[i]) * f;
        }

        private static float Fetch(float[] data, int length, long idx, bool wrap)
        {
            if (wrap)
            {
                long m = idx % length;
                if (m < 0) m += length;
                return data[m];
            }
            return idx < 0 || idx >= length ? 0f : data[idx];
        }

        public float Sample(float[] data, int length, double position, float cutoff, bool wrap)
        {
            if (length <= 0) return 0f;
            long center = (long)Math.Floor(position);
            double frac = position - center;

            if (cutoff >= 0.999f)
            {
                if (frac < 1e-9) return Fetch(data, length, center, wrap);
                double sum = 0, wsum = 0;
                for (int k = -_halfWidth + 1; k <= _halfWidth; k++)
                {
                    float w = Kernel(k - frac);
                    sum += w * Fetch(data, length, center + k, wrap);
                    wsum += w;
                }
                return wsum > 1e-9 ? (float)(sum / wsum) : 0f;
            }
            else
            {
                double c = Math.Max(0.01, cutoff);
                double reach = _halfWidth / c;
                long lo = (long)Math.Ceiling(position - reach);
                long hi = (long)Math.Floor(position + reach);
                double sum = 0, wsum = 0;
                for (long idx = lo; idx <= hi; idx++)
                {
                    float w = Kernel((idx - position) * c);
                    if (w == 0f) continue;
                    sum += w * Fetch(data, length, idx, wrap);
                    wsum += w;
                }
                return wsum > 1e-9 ? (float)(sum / wsum) : 0f;
            }
        }
    }
}
