using System;
using AudioCreationStudio.Models;

namespace AudioCreationStudio.DSP
{
    public static class FadeCurves
    {
        /// <summary>Gain for normalized fade position x ∈ [0,1] (0 = silent, 1 = full).</summary>
        public static float Evaluate(FadeCurve curve, double x)
        {
            if (x <= 0) return 0f;
            if (x >= 1) return 1f;
            switch (curve)
            {
                case FadeCurve.EqualPower: return (float)Math.Sin(x * DspMath.HalfPi);
                case FadeCurve.Exponential: return (float)(x * x);
                case FadeCurve.SCurve: return (float)(0.5 - 0.5 * Math.Cos(Math.PI * x));
                default: return (float)x;
            }
        }

        /// <summary>
        /// Combined fade gain for a clip-local sample index. Fade-in starts at exactly 0 on the first
        /// sample; fade-out reaches exactly 0 on the last sample.
        /// </summary>
        public static float ClipFadeGain(ClipData clip, long local)
        {
            float g = 1f;
            long len = clip.lengthSamples;
            var fi = clip.fadeIn;
            if (fi != null && fi.lengthSamples > 0 && local < fi.lengthSamples)
                g *= Evaluate(fi.curve, (double)local / fi.lengthSamples);
            var fo = clip.fadeOut;
            if (fo != null && fo.lengthSamples > 0 && local >= len - fo.lengthSamples)
                g *= Evaluate(fo.curve, (double)(len - 1 - local) / fo.lengthSamples);
            return g;
        }
    }
}
