using System;
using System.Collections.Generic;

namespace AudioCreationStudio.DSP
{
    public struct LevelReading
    {
        public float PeakL, PeakR, RmsL, RmsR;
        public bool Clipped;
        public static readonly LevelReading Silent = default;
    }

    public static class SignalAnalysis
    {
        public static double Rms(float[] data, int start, int count)
        {
            double sq = 0;
            int end = Math.Min(data.Length, start + count);
            int n = 0;
            for (int i = Math.Max(0, start); i < end; i++) { sq += data[i] * data[i]; n++; }
            return n == 0 ? 0 : Math.Sqrt(sq / n);
        }

        /// <summary>Measures peak/RMS for the window ending at <paramref name="frame"/> (meters read real rendered audio).</summary>
        public static LevelReading Measure(AudioBuffer buf, long frame, int window)
        {
            var r = new LevelReading();
            if (buf == null || buf.Frames == 0) return r;
            int end = (int)Math.Min(buf.Frames, Math.Max(0, frame));
            int start = Math.Max(0, end - window);
            if (end <= start) return r;
            for (int c = 0; c < Math.Min(2, buf.Channels); c++)
            {
                var d = buf.GetChannel(c);
                float pk = 0; double sq = 0;
                for (int i = start; i < end; i++)
                {
                    float a = Math.Abs(d[i]);
                    if (a > pk) pk = a;
                    sq += d[i] * d[i];
                }
                float rms = (float)Math.Sqrt(sq / (end - start));
                if (c == 0) { r.PeakL = pk; r.RmsL = rms; } else { r.PeakR = pk; r.RmsR = rms; }
                if (pk >= 1f) r.Clipped = true;
            }
            if (buf.Channels == 1) { r.PeakR = r.PeakL; r.RmsR = r.RmsL; }
            return r;
        }

        /// <summary>First and last frame (exclusive) whose level exceeds <paramref name="thresholdDb"/>.</summary>
        public static bool FindSignalBounds(AudioBuffer buf, float thresholdDb, out int first, out int lastExclusive)
        {
            float thr = DspMath.DbToGain(thresholdDb);
            first = -1; lastExclusive = 0;
            for (int i = 0; i < buf.Frames && first < 0; i++)
                for (int c = 0; c < buf.Channels; c++)
                    if (Math.Abs(buf.GetChannel(c)[i]) > thr) { first = i; break; }
            if (first < 0) { first = 0; return false; }
            for (int i = buf.Frames - 1; i >= first; i--)
            {
                bool hit = false;
                for (int c = 0; c < buf.Channels; c++)
                    if (Math.Abs(buf.GetChannel(c)[i]) > thr) { hit = true; break; }
                if (hit) { lastExclusive = i + 1; break; }
            }
            return true;
        }

        /// <summary>Gain that brings the peak to <paramref name="targetDb"/>. Returns 1 for silence.</summary>
        public static float NormalizationGain(AudioBuffer buf, float targetDb)
        {
            float peak = buf.GetPeak();
            return peak <= 1e-9f ? 1f : DspMath.DbToGain(targetDb) / peak;
        }

        /// <summary>
        /// Detects likely clicks: samples whose second difference greatly exceeds the local average second
        /// difference (±256 samples). Local context keeps noisy material (machines, wind) from being flagged
        /// while isolated discontinuities stand out. Returns frame indices (at most <paramref name="maxResults"/>).
        /// </summary>
        public static List<int> DetectClicks(float[] d, int length, float sensitivity = 10f, int maxResults = 32)
        {
            var result = new List<int>();
            if (length < 16) return result;
            var d2 = new float[length];
            for (int i = 2; i < length; i++) d2[i] = Math.Abs(d[i] - 2 * d[i - 1] + d[i - 2]);
            const int half = 256;
            double sum = 0;
            int lo = 0, hi = -1;
            for (int i = 2; i < length && result.Count < maxResults; i++)
            {
                int wantLo = Math.Max(2, i - half), wantHi = Math.Min(length - 1, i + half);
                while (hi < wantHi) sum += d2[++hi];
                while (lo < wantLo) sum -= d2[lo++];
                int n = hi - lo + 1;
                double localMean = (sum - d2[i]) / Math.Max(1, n - 1);
                if (d2[i] > 0.05f && d2[i] > localMean * sensitivity)
                {
                    result.Add(i - 1);
                    i += 64;
                }
            }
            return result;
        }
    }

    public static class ZeroCrossing
    {
        /// <summary>Nearest rising zero crossing to <paramref name="position"/> within ±<paramref name="radius"/> frames (summed channels).</summary>
        public static long FindNearest(AudioBuffer buf, long position, int radius, bool rising = true)
        {
            long best = position;
            long bestDist = long.MaxValue;
            long lo = Math.Max(1, position - radius), hi = Math.Min(buf.Frames - 1, position + radius);
            for (long i = lo; i <= hi; i++)
            {
                float a = Sum(buf, i - 1), b = Sum(buf, i);
                bool cross = rising ? (a < 0 && b >= 0) : ((a < 0) != (b < 0));
                if (!cross) continue;
                long pick = Math.Abs(a) < Math.Abs(b) ? i - 1 : i;
                long dist = Math.Abs(pick - position);
                if (dist < bestDist) { bestDist = dist; best = pick; }
            }
            return best;
        }

        private static float Sum(AudioBuffer b, long i)
        {
            float s = 0;
            for (int c = 0; c < b.Channels; c++) s += b.GetChannel(c)[i];
            return s;
        }
    }
}
