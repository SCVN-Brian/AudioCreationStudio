using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace AudioCreationStudio.DSP
{
    /// <summary>Descriptive analysis of a sound, used to brief an AI sound designer and to compare candidates with a reference.</summary>
    public sealed class SoundAnalysis
    {
        public double DurationSeconds;
        public int SampleRate;
        public float PeakDb, RmsDb;

        /// <summary>RMS envelope in dBFS at <see cref="EnvelopeStepSeconds"/> spacing.</summary>
        public float[] EnvelopeDb = Array.Empty<float>();
        public double EnvelopeStepSeconds;
        public float AttackMs, DecayMs, SustainRatio;
        public List<float> OnsetTimes = new List<float>();

        /// <summary>Per-frame fundamental frequency (0 = unvoiced / noise) and its confidence 0..1.</summary>
        public float[] PitchHz = Array.Empty<float>();
        public float[] PitchConfidence = Array.Empty<float>();
        public float[] CentroidHz = Array.Empty<float>();
        public float[] Flatness = Array.Empty<float>();
        public double FrameStepSeconds;
        public float VoicedRatio, MedianPitchHz, AvgCentroidHz, AvgFlatness;

        public static readonly float[] BandCenters = { 63, 125, 250, 500, 1000, 2000, 4000, 8000, 16000 };
        public float[] BandDb = new float[BandCenters.Length];

        /// <summary>Log-frequency spectrogram in dB: [frame, bin], bins spaced log between <see cref="SpecMinHz"/> and <see cref="SpecMaxHz"/>.</summary>
        public float[,] Spectrogram = new float[0, 0];
        public double SpecMinHz = 50, SpecMaxHz = 16000;

        private static string F(double v, string fmt = "0.##") => v.ToString(fmt, CultureInfo.InvariantCulture);

        /// <summary>Compact textual description for a language model (numbers, not prose).</summary>
        public string ToPromptText(string title)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"### {title}");
            sb.AppendLine($"duration_s: {F(DurationSeconds, "0.000")}, peak_dbfs: {F(PeakDb, "0.0")}, rms_dbfs: {F(RmsDb, "0.0")}");
            sb.AppendLine($"attack_ms: {F(AttackMs, "0")}, decay_to_-20dB_ms: {F(DecayMs, "0")}, time_above_-12dB_of_peak: {F(SustainRatio * 100, "0")}%");
            sb.AppendLine($"onsets (distinct hits) at seconds: [{string.Join(", ", OnsetTimes.ConvertAll(t => F(t, "0.000")))}]");
            sb.Append($"amplitude_envelope_dbfs (every {F(EnvelopeStepSeconds * 1000, "0")} ms): [");
            for (int i = 0; i < EnvelopeDb.Length; i++) sb.Append(i == 0 ? "" : ", ").Append(F(Math.Max(-90, EnvelopeDb[i]), "0"));
            sb.AppendLine("]");
            sb.AppendLine($"voiced (pitched) fraction: {F(VoicedRatio * 100, "0")}%, median_pitch_hz: {(MedianPitchHz > 0 ? F(MedianPitchHz, "0") : "none")}");
            sb.Append($"pitch_contour_hz (every {F(FrameStepSeconds * 1000, "0")} ms, 0 = noisy/unpitched): [");
            var sub = Subsample(PitchHz.Length, 32);
            for (int k = 0; k < sub.Count; k++) sb.Append(k == 0 ? "" : ", ").Append(PitchHz[sub[k]] > 0 ? F(PitchHz[sub[k]], "0") : "0");
            sb.AppendLine($"] (shown at {sub.Count} evenly spaced frames)");
            sb.Append("spectral_centroid_hz (brightness) contour: [");
            for (int k = 0; k < sub.Count; k++) sb.Append(k == 0 ? "" : ", ").Append(F(CentroidHz[sub[k]], "0"));
            sb.AppendLine("]");
            sb.Append("spectral_flatness contour (0 = tonal, 1 = white noise): [");
            for (int k = 0; k < sub.Count; k++) sb.Append(k == 0 ? "" : ", ").Append(F(Flatness[sub[k]], "0.00"));
            sb.AppendLine("]");
            sb.AppendLine($"average centroid_hz: {F(AvgCentroidHz, "0")}, average flatness: {F(AvgFlatness, "0.00")}");
            sb.Append("octave_band_energy_db (relative to loudest band): {");
            for (int b = 0; b < BandCenters.Length; b++) sb.Append(b == 0 ? "" : ", ").Append($"{BandCenters[b]}Hz: {F(BandDb[b], "0")}");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static List<int> Subsample(int n, int max)
        {
            var r = new List<int>();
            if (n <= 0) return r;
            int count = Math.Min(n, max);
            for (int i = 0; i < count; i++) r.Add((int)Math.Round(i * (n - 1) / (double)Math.Max(1, count - 1)));
            return r;
        }
    }

    /// <summary>
    /// Pure-C# analysis: RMS envelope, onsets, FFT-autocorrelation pitch tracking, spectral centroid and
    /// flatness, octave-band energy and a log-frequency spectrogram. Thread-safe (no shared state).
    /// </summary>
    public static class SoundAnalyzer
    {
        private const int Fft = 2048;

        public static SoundAnalysis Analyze(AudioBuffer buffer, int envelopePoints = 40, int specBins = 128, int maxSpecFrames = 160)
        {
            var a = new SoundAnalysis { SampleRate = buffer.SampleRate, DurationSeconds = buffer.DurationSeconds };
            int sr = buffer.SampleRate;
            int n = buffer.Frames;
            var x = new float[n];
            for (int c = 0; c < buffer.Channels; c++)
            {
                var d = buffer.GetChannel(c);
                for (int i = 0; i < n; i++) x[i] += d[i] / buffer.Channels;
            }
            if (n == 0) return a;

            float peak = 0; double sq = 0;
            foreach (var v in x) { peak = Math.Max(peak, Math.Abs(v)); sq += v * v; }
            a.PeakDb = DspMath.GainToDb(peak);
            a.RmsDb = DspMath.GainToDb((float)Math.Sqrt(sq / n));

            // Fine envelope (5 ms) for attack/decay/onsets; coarse envelope for the prompt.
            int fineWin = Math.Max(1, sr / 200);
            var fine = Envelope(x, fineWin);
            double fineStep = fineWin / (double)sr;
            int coarseWin = Math.Max(1, n / Math.Max(1, envelopePoints));
            a.EnvelopeDb = Envelope(x, coarseWin);
            a.EnvelopeStepSeconds = coarseWin / (double)sr;

            int peakIdx = 0;
            for (int i = 1; i < fine.Length; i++) if (fine[i] > fine[peakIdx]) peakIdx = i;
            float pk = fine.Length > 0 ? fine[peakIdx] : -144;
            int startIdx = 0;
            while (startIdx < peakIdx && fine[startIdx] < pk - 30) startIdx++;
            a.AttackMs = (float)((peakIdx - startIdx) * fineStep * 1000);
            int decIdx = peakIdx;
            while (decIdx < fine.Length - 1 && fine[decIdx] > pk - 20) decIdx++;
            a.DecayMs = (float)((decIdx - peakIdx) * fineStep * 1000);
            int above = 0;
            foreach (var v in fine) if (v > pk - 12) above++;
            a.SustainRatio = fine.Length > 0 ? above / (float)fine.Length : 0;
            a.OnsetTimes = DetectOnsets(fine, fineStep, pk);

            // Spectral frames.
            int frames = Math.Max(1, Math.Min(maxSpecFrames, (n + Fft / 4) / (Fft / 4)));
            int hop = Math.Max(1, (n - 1) / Math.Max(1, frames - 1));
            if (n <= Fft) { frames = 1; hop = n; }
            a.FrameStepSeconds = hop / (double)sr;
            a.PitchHz = new float[frames];
            a.PitchConfidence = new float[frames];
            a.CentroidHz = new float[frames];
            a.Flatness = new float[frames];
            a.Spectrogram = new float[frames, specBins];
            var bandSum = new double[SoundAnalysis.BandCenters.Length];
            var re = new double[Fft * 2];
            var im = new double[Fft * 2];
            var window = new double[Fft];
            for (int i = 0; i < Fft; i++) window[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (Fft - 1));
            double binHz = sr / (double)Fft;
            double logMin = Math.Log(a.SpecMinHz), logMax = Math.Log(Math.Min(a.SpecMaxHz, sr * 0.5));
            var voicedPitches = new List<float>();
            double centroidSum = 0, flatSum = 0; int energetic = 0;

            for (int f = 0; f < frames; f++)
            {
                int center = Math.Min(n - 1, f * hop);
                int s0 = center - Fft / 2;
                double frameSq = 0;
                for (int i = 0; i < Fft; i++)
                {
                    int idx = s0 + i;
                    double v = idx >= 0 && idx < n ? x[idx] : 0;
                    frameSq += v * v;
                    re[i] = v * window[i];
                    im[i] = 0;
                }
                float frameDb = DspMath.GainToDb((float)Math.Sqrt(frameSq / Fft));
                for (int i = Fft; i < re.Length; i++) { re[i] = 0; im[i] = 0; }
                FftInPlace(re, im, Fft, false);

                // Magnitude spectrum, centroid, flatness, bands, spectrogram.
                double num = 0, den = 0, logSum = 0, linSum = 0; int cnt = 0;
                var mag = new double[Fft / 2];
                for (int k = 1; k < Fft / 2; k++)
                {
                    double p = re[k] * re[k] + im[k] * im[k];
                    double m = Math.Sqrt(p);
                    mag[k] = m;
                    double hz = k * binHz;
                    num += hz * m; den += m;
                    if (hz >= 100 && hz <= 16000) { logSum += Math.Log(p + 1e-20); linSum += p; cnt++; }
                    for (int b = 0; b < bandSum.Length; b++)
                    {
                        double lo = SoundAnalysis.BandCenters[b] / Math.Sqrt(2), hi = SoundAnalysis.BandCenters[b] * Math.Sqrt(2);
                        if (hz >= lo && hz < hi) { bandSum[b] += p; break; }
                    }
                }
                a.CentroidHz[f] = den > 1e-12 ? (float)(num / den) : 0;
                a.Flatness[f] = cnt > 0 && linSum > 1e-20 ? (float)(Math.Exp(logSum / cnt) / (linSum / cnt)) : 0;
                if (frameDb > a.PeakDb - 40) { centroidSum += a.CentroidHz[f]; flatSum += a.Flatness[f]; energetic++; }
                for (int b = 0; b < specBins; b++)
                {
                    double hzLo = Math.Exp(logMin + (logMax - logMin) * b / specBins);
                    double hzHi = Math.Exp(logMin + (logMax - logMin) * (b + 1) / specBins);
                    int k0 = Math.Max(1, (int)(hzLo / binHz)), k1 = Math.Max(k0 + 1, (int)Math.Ceiling(hzHi / binHz));
                    double mx = 0;
                    for (int k = k0; k < k1 && k < Fft / 2; k++) mx = Math.Max(mx, mag[k]);
                    a.Spectrogram[f, b] = DspMath.GainToDb((float)(mx / (Fft / 4.0)));
                }

                // Pitch: normalized autocorrelation via FFT of the power spectrum (zero-padded, linear ACF).
                if (frameDb > a.PeakDb - 35 && frameDb > -60)
                {
                    for (int i = 0; i < Fft * 2; i++)
                    {
                        int idx = s0 + i;
                        re[i] = i < Fft && idx >= 0 && idx < n ? x[idx] * window[i] : 0;
                        im[i] = 0;
                    }
                    FftInPlace(re, im, Fft * 2, false);
                    for (int k = 0; k < Fft * 2; k++) { re[k] = re[k] * re[k] + im[k] * im[k]; im[k] = 0; }
                    FftInPlace(re, im, Fft * 2, true);
                    double r0 = re[0];
                    int minLag = Math.Max(2, sr / 2000), maxLag = Math.Min(Fft - 2, sr / 50);
                    int best = -1; double bestV = 0;
                    for (int lag = minLag; lag <= maxLag; lag++)
                    {
                        double v = re[lag] / (r0 * (Fft - lag) / Fft + 1e-20); // unbiased normalisation
                        if (v > bestV && re[lag] >= re[lag - 1] && re[lag] >= re[lag + 1]) { bestV = v; best = lag; }
                    }
                    if (best > 0 && bestV > 0.55)
                    {
                        // Prefer the shortest lag that is nearly as strong (avoids octave-down errors).
                        for (int div = 4; div >= 2; div--)
                        {
                            int l = (int)Math.Round(best / (double)div);
                            if (l < minLag) continue;
                            double v = re[l] / (r0 * (Fft - l) / Fft + 1e-20);
                            if (v > bestV * 0.9) { best = l; bestV = v; break; }
                        }
                        double ym = re[best - 1], y0 = re[best], yp = re[best + 1];
                        double denom = ym - 2 * y0 + yp;
                        double shift = Math.Abs(denom) > 1e-20 ? 0.5 * (ym - yp) / denom : 0;
                        a.PitchHz[f] = (float)(sr / (best + shift));
                        a.PitchConfidence[f] = (float)Math.Min(1, bestV);
                        voicedPitches.Add(a.PitchHz[f]);
                    }
                }
            }

            double maxBand = 1e-30;
            foreach (var v in bandSum) maxBand = Math.Max(maxBand, v);
            for (int b = 0; b < bandSum.Length; b++) a.BandDb[b] = (float)Math.Max(-90, 10 * Math.Log10(bandSum[b] / maxBand + 1e-12));
            int voicedFrames = 0, energeticFrames = 0;
            for (int f = 0; f < frames; f++) { if (a.PitchHz[f] > 0) voicedFrames++; }
            energeticFrames = Math.Max(1, energetic);
            a.VoicedRatio = Math.Min(1f, voicedFrames / (float)energeticFrames);
            if (voicedPitches.Count > 0) { voicedPitches.Sort(); a.MedianPitchHz = voicedPitches[voicedPitches.Count / 2]; }
            a.AvgCentroidHz = energetic > 0 ? (float)(centroidSum / energetic) : 0;
            a.AvgFlatness = energetic > 0 ? (float)(flatSum / energetic) : 0;
            return a;
        }

        private static float[] Envelope(float[] x, int win)
        {
            int count = Math.Max(1, (x.Length + win - 1) / win);
            var e = new float[count];
            for (int w = 0; w < count; w++)
            {
                double s = 0; int c = 0;
                for (int i = w * win; i < Math.Min(x.Length, (w + 1) * win); i++) { s += x[i] * x[i]; c++; }
                e[w] = DspMath.GainToDb((float)Math.Sqrt(s / Math.Max(1, c)));
            }
            return e;
        }

        /// <summary>Onsets: rises of ≥ 9 dB within 60 ms that reach within 24 dB of the peak, at least 80 ms apart.</summary>
        private static List<float> DetectOnsets(float[] env, double step, float peak)
        {
            var r = new List<float>();
            int look = Math.Max(1, (int)(0.06 / step));
            int gap = Math.Max(1, (int)(0.08 / step));
            int last = -gap;
            for (int i = 1; i < env.Length; i++)
            {
                if (env[i] < peak - 24 || i - last < gap) continue;
                float min = env[i];
                for (int k = Math.Max(0, i - look); k < i; k++) min = Math.Min(min, env[k]);
                bool rising = env[i] - min >= 9 && (i + 1 >= env.Length || env[i + 1] <= env[i] + 3);
                bool first = r.Count == 0 && i <= look && env[i] > peak - 12; // sound starts immediately
                if (rising || first)
                {
                    // Move to the local maximum of this hit.
                    int j = i;
                    while (j + 1 < env.Length && env[j + 1] > env[j]) j++;
                    r.Add((float)(Math.Max(0, i - 1) * step));
                    last = j;
                    i = j;
                }
            }
            return r;
        }

        /// <summary>Iterative radix-2 FFT (length must be a power of two). Inverse is unnormalised by 1/n.</summary>
        public static void FftInPlace(double[] re, double[] im, int n, bool inverse)
        {
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j)
                {
                    (re[i], re[j]) = (re[j], re[i]);
                    (im[i], im[j]) = (im[j], im[i]);
                }
            }
            for (int len = 2; len <= n; len <<= 1)
            {
                double ang = 2 * Math.PI / len * (inverse ? 1 : -1);
                double wr = Math.Cos(ang), wi = Math.Sin(ang);
                for (int i = 0; i < n; i += len)
                {
                    double cr = 1, ci = 0;
                    for (int k = 0; k < len / 2; k++)
                    {
                        int a = i + k, b = a + len / 2;
                        double tr = re[b] * cr - im[b] * ci, ti = re[b] * ci + im[b] * cr;
                        re[b] = re[a] - tr; im[b] = im[a] - ti;
                        re[a] += tr; im[a] += ti;
                        double ncr = cr * wr - ci * wi;
                        ci = cr * wi + ci * wr;
                        cr = ncr;
                    }
                }
            }
            if (inverse) for (int i = 0; i < n; i++) { re[i] /= n; im[i] /= n; }
        }
    }
}
