using System;
using System.Threading;

namespace AudioCreationStudio.DSP
{
    /// <summary>
    /// Multi-resolution min/max/RMS peak pyramid for waveform display. Level 0 aggregates
    /// <see cref="BaseBucket"/> frames per bucket; each further level halves the resolution.
    /// Channels are combined (min of mins, max of maxes). Immutable once built → safe to share across threads.
    /// </summary>
    public sealed class WaveformPeaks
    {
        public const int BaseBucket = 32;

        private readonly float[][] _min, _max, _rms;
        private readonly AudioBuffer _source;

        public int Frames { get; }
        public int SampleRate { get; }
        public int Levels => _min.Length;
        public float Peak { get; }

        private WaveformPeaks(AudioBuffer source, float[][] min, float[][] max, float[][] rms, float peak)
        {
            _source = source; _min = min; _max = max; _rms = rms;
            Frames = source.Frames; SampleRate = source.SampleRate; Peak = peak;
        }

        public static WaveformPeaks Build(AudioBuffer src, CancellationToken ct = default)
        {
            int buckets = Math.Max(1, (src.Frames + BaseBucket - 1) / BaseBucket);
            int levels = 1;
            for (int n = buckets; n > 1; n = (n + 1) / 2) levels++;
            var min = new float[levels][];
            var max = new float[levels][];
            var rms = new float[levels][];
            min[0] = new float[buckets]; max[0] = new float[buckets]; rms[0] = new float[buckets];
            float peak = 0;

            for (int b = 0; b < buckets; b++)
            {
                if ((b & 1023) == 0) ct.ThrowIfCancellationRequested();
                int s = b * BaseBucket, e = Math.Min(src.Frames, s + BaseBucket);
                float mn = float.MaxValue, mx = float.MinValue;
                double sq = 0;
                for (int c = 0; c < src.Channels; c++)
                {
                    var d = src.GetChannel(c);
                    for (int i = s; i < e; i++)
                    {
                        float v = d[i];
                        if (v < mn) mn = v;
                        if (v > mx) mx = v;
                        sq += v * v;
                    }
                }
                if (e <= s) { mn = mx = 0; }
                min[0][b] = mn; max[0][b] = mx;
                rms[0][b] = (float)Math.Sqrt(sq / Math.Max(1, (e - s) * src.Channels));
                peak = Math.Max(peak, Math.Max(Math.Abs(mn), Math.Abs(mx)));
            }

            for (int l = 1; l < levels; l++)
            {
                int n = (min[l - 1].Length + 1) / 2;
                min[l] = new float[n]; max[l] = new float[n]; rms[l] = new float[n];
                for (int i = 0; i < n; i++)
                {
                    int a = i * 2, b = Math.Min(a + 1, min[l - 1].Length - 1);
                    min[l][i] = Math.Min(min[l - 1][a], min[l - 1][b]);
                    max[l][i] = Math.Max(max[l - 1][a], max[l - 1][b]);
                    float ra = rms[l - 1][a], rb = rms[l - 1][b];
                    rms[l][i] = (float)Math.Sqrt(0.5 * (ra * ra + rb * rb));
                }
            }
            return new WaveformPeaks(src, min, max, rms, peak);
        }

        /// <summary>Returns min/max/rms over source frames [start, end). Chooses the coarsest adequate level.</summary>
        public void Query(double start, double end, out float min, out float max, out float rms)
        {
            if (end < start) { var t = start; start = end; end = t; }
            long s = (long)Math.Floor(start), e = (long)Math.Ceiling(end);
            if (e <= s) e = s + 1;
            s = Math.Max(0, s); e = Math.Min(Frames, e);
            if (e <= s) { min = max = rms = 0; return; }

            long span = e - s;
            if (span < BaseBucket * 2)
            {
                // Direct read for deep zoom (at most 64 samples per column, never per-sample drawing).
                float mn = float.MaxValue, mx = float.MinValue; double sq = 0;
                for (int c = 0; c < _source.Channels; c++)
                {
                    var d = _source.GetChannel(c);
                    for (long i = s; i < e; i++) { float v = d[i]; if (v < mn) mn = v; if (v > mx) mx = v; sq += v * v; }
                }
                min = mn; max = mx; rms = (float)Math.Sqrt(sq / (span * _source.Channels));
                return;
            }

            int level = 0;
            long bucket = BaseBucket;
            while (level + 1 < Levels && bucket * 2 * 2 <= span) { level++; bucket *= 2; }
            long b0 = s / bucket, b1 = Math.Max(b0 + 1, (e + bucket - 1) / bucket);
            var mnA = _min[level]; var mxA = _max[level]; var rA = _rms[level];
            b1 = Math.Min(b1, mnA.Length);
            float rmin = float.MaxValue, rmax = float.MinValue; double rs = 0;
            for (long b = b0; b < b1; b++)
            {
                if (mnA[b] < rmin) rmin = mnA[b];
                if (mxA[b] > rmax) rmax = mxA[b];
                rs += rA[b] * rA[b];
            }
            min = rmin == float.MaxValue ? 0 : rmin;
            max = rmax == float.MinValue ? 0 : rmax;
            rms = (float)Math.Sqrt(rs / Math.Max(1, b1 - b0));
        }
    }
}
