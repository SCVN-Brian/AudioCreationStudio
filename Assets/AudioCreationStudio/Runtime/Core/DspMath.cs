using System;
using System.Runtime.CompilerServices;

namespace AudioCreationStudio.DSP
{
    /// <summary>Numeric helpers shared by every DSP stage. Pure C#, thread-safe.</summary>
    public static class DspMath
    {
        /// <summary>Gain values at or below this dB level are treated as silence.</summary>
        public const float MinDb = -144f;
        public const double TwoPi = Math.PI * 2.0;
        public const double HalfPi = Math.PI * 0.5;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float DbToGain(float db) => db <= MinDb ? 0f : (float)Math.Pow(10.0, db / 20.0);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double DbToGain(double db) => db <= MinDb ? 0.0 : Math.Pow(10.0, db / 20.0);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float GainToDb(float gain)
        {
            float a = Math.Abs(gain);
            if (a <= 1e-8f || float.IsNaN(a)) return MinDb;
            return Math.Max(MinDb, (float)(20.0 * Math.Log10(a)));
        }

        /// <summary>Replaces NaN / Infinity with silence so invalid values never propagate.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Sanitize(float x) => float.IsNaN(x) || float.IsInfinity(x) ? 0f : x;

        /// <summary>Flushes denormal-range values in recursive filter state to zero.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double FlushDenormal(double x) => (x > -1e-25 && x < 1e-25) || double.IsNaN(x) || double.IsInfinity(x) ? 0.0 : x;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Clamp(double v, double min, double max) => v < min ? min : (v > max ? max : v);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long Clamp(long v, long min, long max) => v < min ? min : (v > max ? max : v);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Lerp(float a, float b, float t) => a + (b - a) * t;

        public static double SemitonesToRatio(double semitones) => Math.Pow(2.0, semitones / 12.0);

        public static double RatioToSemitones(double ratio) => ratio <= 0 ? 0 : 12.0 * Math.Log(ratio, 2.0);

        /// <summary>One-pole smoothing coefficient reaching ~63% in <paramref name="timeSeconds"/>.</summary>
        public static double OnePoleCoefficient(double timeSeconds, int sampleRate)
        {
            if (timeSeconds <= 0) return 0.0;
            return Math.Exp(-1.0 / (timeSeconds * sampleRate));
        }

        /// <summary>Positive modulo for doubles.</summary>
        public static double Mod(double x, double m)
        {
            double r = x % m;
            return r < 0 ? r + m : r;
        }

        public static long Mod(long x, long m)
        {
            long r = x % m;
            return r < 0 ? r + m : r;
        }
    }
}
