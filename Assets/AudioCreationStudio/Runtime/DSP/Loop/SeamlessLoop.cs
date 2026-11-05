using System;
using AudioCreationStudio.Models;

namespace AudioCreationStudio.DSP
{
    public enum LoopVerdict { Seamless, Minor, Click }

    public struct LoopBoundaryReport
    {
        /// <summary>Wrap discontinuity divided by the signal's typical sample-to-sample change.</summary>
        public float JumpRatio;
        /// <summary>RMS level difference between the last and first 10 ms (dB).</summary>
        public float LevelMismatchDb;
        public float SlopeMismatch;
        public LoopVerdict Verdict;
        public int InternalClicks;
    }

    /// <summary>
    /// Creates loop-ready audio from a region. Zero crossings alone do not guarantee a seamless loop
    /// (slope and spectral content still jump), so the boundary is always crossfaded:
    /// • Pre-roll method (enough audio before the loop start): the loop tail is crossfaded with the audio
    ///   that precedes the loop start, so wrapping end → start is continuous and the length is preserved.
    /// • Overlap method (no pre-roll): the head is crossfaded with the tail and the loop is shortened by
    ///   the crossfade length.
    /// </summary>
    public static class SeamlessLoop
    {
        public static AudioBuffer Create(AudioBuffer src, long start, long end, int crossfade, FadeCurve curve)
        {
            start = DspMath.Clamp(start, 0, src.Frames);
            end = DspMath.Clamp(end, start, src.Frames);
            int len = (int)(end - start);
            if (len <= 2) return src.Slice((int)start, len);
            crossfade = Math.Max(0, Math.Min(crossfade, len / 2));
            if (crossfade == 0) return src.Slice((int)start, len);

            bool preRoll = start >= crossfade;
            int outLen = preRoll ? len : len - crossfade;
            var dst = new AudioBuffer(src.Channels, outLen, src.SampleRate);
            for (int c = 0; c < src.Channels; c++)
            {
                var s = src.GetChannel(c);
                var d = dst.GetChannel(c);
                if (preRoll)
                {
                    Array.Copy(s, start, d, 0, len);
                    for (int i = 0; i < crossfade; i++)
                    {
                        double x = (i + 0.5) / crossfade;
                        Gains(curve, x, out float fin, out float fout);
                        int di = len - crossfade + i;
                        d[di] = s[end - crossfade + i] * fout + s[start - crossfade + i] * fin;
                    }
                }
                else
                {
                    Array.Copy(s, start, d, 0, outLen);
                    for (int i = 0; i < crossfade; i++)
                    {
                        double x = (i + 0.5) / crossfade;
                        Gains(curve, x, out float fin, out float fout);
                        d[i] = s[start + i] * fin + s[end - crossfade + i] * fout;
                    }
                }
            }
            return dst;
        }

        private static void Gains(FadeCurve curve, double x, out float fin, out float fout)
        {
            if (curve == FadeCurve.EqualPower)
            {
                fin = (float)Math.Sin(x * DspMath.HalfPi);
                fout = (float)Math.Cos(x * DspMath.HalfPi);
            }
            else
            {
                fin = (float)x;
                fout = (float)(1 - x);
            }
        }

        /// <summary>Repeats a loop buffer <paramref name="times"/> times (for repeated-playback preview).</summary>
        public static AudioBuffer Repeat(AudioBuffer loop, int times)
        {
            times = Math.Max(1, times);
            var dst = new AudioBuffer(loop.Channels, loop.Frames * times, loop.SampleRate);
            for (int c = 0; c < loop.Channels; c++)
                for (int k = 0; k < times; k++)
                    Array.Copy(loop.GetChannel(c), 0, dst.GetChannel(c), k * loop.Frames, loop.Frames);
            return dst;
        }

        /// <summary>Analyzes the wrap-around boundary of a loop buffer for clicks and level jumps.</summary>
        public static LoopBoundaryReport AnalyzeBoundary(AudioBuffer loop)
        {
            var r = new LoopBoundaryReport();
            int n = loop.Frames;
            if (n < 16) { r.Verdict = LoopVerdict.Click; return r; }
            int win = Math.Max(8, Math.Min(n / 4, loop.SampleRate / 100));
            float worstJump = 0, worstSlope = 0, worstLevel = 0;
            for (int c = 0; c < loop.Channels; c++)
            {
                var d = loop.GetChannel(c);
                double typical = 0;
                for (int i = 1; i < n; i++) typical += Math.Abs(d[i] - d[i - 1]);
                typical = Math.Max(1e-5, typical / (n - 1));

                // Predict the first sample from the last two (linear extrapolation).
                double predicted = d[n - 1] + (d[n - 1] - d[n - 2]);
                double jump = Math.Abs(d[0] - predicted) / typical;
                double slopeEnd = d[n - 1] - d[n - 2], slopeStart = d[1] - d[0];
                double slope = Math.Abs(slopeEnd - slopeStart) / typical;
                double rmsEnd = SignalAnalysis.Rms(d, n - win, win), rmsStart = SignalAnalysis.Rms(d, 0, win);
                float lvl = Math.Abs(DspMath.GainToDb((float)rmsEnd) - DspMath.GainToDb((float)rmsStart));
                if (rmsEnd < 1e-5 && rmsStart < 1e-5) lvl = 0;
                worstJump = Math.Max(worstJump, (float)jump);
                worstSlope = Math.Max(worstSlope, (float)slope);
                worstLevel = Math.Max(worstLevel, lvl);
            }
            r.JumpRatio = worstJump;
            r.SlopeMismatch = worstSlope;
            r.LevelMismatchDb = worstLevel;

            // Internal clicks, examined on a 2x repeat so the boundary itself is included.
            var twice = Repeat(loop, 2);
            r.InternalClicks = SignalAnalysis.DetectClicks(twice.GetChannel(0), twice.Frames).Count;

            if (worstJump < 3f && worstLevel < 3f) r.Verdict = LoopVerdict.Seamless;
            else if (worstJump < 8f && worstLevel < 6f) r.Verdict = LoopVerdict.Minor;
            else r.Verdict = LoopVerdict.Click;
            return r;
        }
    }
}
