using System;

namespace AudioCreationStudio.DSP
{
    /// <summary>
    /// Sample-accurate time conversions. Timeline positions are stored as integer sample counts at the
    /// recipe sample rate; seconds are only used at the UI boundary, so no floating-point drift accumulates.
    /// </summary>
    public static class SampleTime
    {
        public static long SecondsToSamples(double seconds, int sampleRate) => (long)Math.Round(seconds * sampleRate);

        public static double SamplesToSeconds(long samples, int sampleRate) => sampleRate <= 0 ? 0 : (double)samples / sampleRate;

        /// <summary>Converts a sample position between rates with round-half-up integer arithmetic.</summary>
        public static long Rescale(long samples, int fromRate, int toRate)
        {
            if (fromRate == toRate || fromRate <= 0) return samples;
            // Use decimal-free integer math; audio lengths never approach overflow (2^63 / 192000 ~ 1.5M years).
            long scaled = samples * toRate;
            long half = fromRate / 2;
            return scaled >= 0 ? (scaled + half) / fromRate : -((-scaled + half) / fromRate);
        }

        public static string Format(long samples, int sampleRate, bool includeMillis = true)
        {
            double s = SamplesToSeconds(samples, sampleRate);
            if (s < 0) s = 0;
            int minutes = (int)(s / 60);
            double rem = s - minutes * 60;
            return includeMillis ? $"{minutes:00}:{rem:00.000}" : $"{minutes:00}:{(int)rem:00}";
        }
    }
}
