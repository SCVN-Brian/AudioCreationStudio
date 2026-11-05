using System;
using System.Threading;
using AudioCreationStudio.Models;

namespace AudioCreationStudio.DSP
{
    /// <summary>
    /// Deterministic procedural SFX synthesizer:
    /// band-limited oscillator (PolyBLEP) + sub oscillator + white/pink noise → tremolo →
    /// state-variable filter (envelope/LFO modulated) → drive → ADSR → volume.
    /// The same functions (<see cref="EnvelopeAt"/>, <see cref="PitchSemitonesAt"/>, <see cref="OscillatorValue"/>)
    /// drive the editor visualizations, so what you see is what is rendered.
    /// </summary>
    public static class ProceduralSynth
    {
        public const float MaxDurationSeconds = 30f;

        /// <param name="sample">Decoded mono source used when <c>s.waveform == Sample</c> (null renders silence for the oscillator).</param>
        public static AudioBuffer Render(GeneratorSettings s, int sampleRate, CancellationToken ct = default, AudioBuffer sample = null)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            double duration = DspMath.Clamp(s.durationSeconds, 0.005, MaxDurationSeconds);
            int frames = Math.Max(1, (int)Math.Round(duration * sampleRate));
            var buffer = new AudioBuffer(1, frames, sampleRate);
            var outData = buffer.GetChannel(0);

            var rng = new DeterministicRandom(s.seed);
            var pink = new PinkNoise();
            var svf = new StateVariableFilter();
            double phase = 0, subPhase = 0;
            double fmNoise = 0, fmTarget = 0;
            int fmCounter = 0;
            int fmPeriod = Math.Max(1, sampleRate / 40);
            double nyquistLimit = sampleRate * 0.45;
            float volume = DspMath.DbToGain(s.volumeDb);
            double driveAmt = 1.0 + DspMath.Clamp(s.drive, 0f, 1f) * 9.0;
            double driveNorm = Math.Tanh(driveAmt);
            bool useFilter = s.filterType != GeneratorFilterType.None;
            double fmSmooth = Math.Exp(-DspMath.TwoPi * 40.0 / sampleRate);
            bool useSample = s.waveform == OscillatorWaveform.Sample;
            float[] smp = useSample && sample != null ? sample.GetChannel(0) : null;
            int smpLen = smp != null ? sample.Frames : 0;
            double smpRate = smp != null ? sample.SampleRate / (double)sampleRate : 1.0;
            double spos = smp != null ? Math.Max(0, s.sampleStart) * sample.SampleRate : 0;

            for (int i = 0; i < frames; i++)
            {
                if ((i & 8191) == 0) ct.ThrowIfCancellationRequested();
                double t = (double)i / sampleRate;
                double env = EnvelopeAt(s, t, duration);

                // Noise used for frequency modulation (smoothed sample & hold).
                if (s.noiseFm > 0)
                {
                    if (fmCounter-- <= 0) { fmTarget = rng.NextSigned(); fmCounter = fmPeriod; }
                    fmNoise = fmTarget + (fmNoise - fmTarget) * fmSmooth;
                }

                double semis = PitchSemitonesAt(s, t) + s.noiseFm * fmNoise;
                double freq = DspMath.Clamp(s.frequency * Math.Pow(2.0, semis / 12.0), 1.0, nyquistLimit);
                double dt = freq / sampleRate;

                double white = rng.NextSigned();
                double pinkV = pink.Next(white);
                double osc;
                switch (s.waveform)
                {
                    case OscillatorWaveform.Sample:
                    {
                        // Transposition only (the base frequency does not apply to samples).
                        double step = Math.Pow(2.0, semis / 12.0) * smpRate;
                        osc = smp != null && spos < smpLen ? SincInterpolator.Shared.Sample(smp, smpLen, spos, step > 1 ? (float)(Resampler.DownsampleMargin / step) : 1f, false) : 0.0;
                        spos += step;
                        break;
                    }
                    case OscillatorWaveform.WhiteNoise: osc = white; break;
                    case OscillatorWaveform.PinkNoise: osc = pinkV; break;
                    default: osc = OscillatorValue(s.waveform, phase, dt, s.dutyCycle); break;
                }
                phase += dt;
                if (phase >= 1.0) phase -= Math.Floor(phase);

                double x = osc;
                if (s.subOscMix > 0 && !useSample)
                {
                    x += Math.Sin(DspMath.TwoPi * subPhase) * s.subOscMix;
                    subPhase += dt * 0.5;
                    if (subPhase >= 1.0) subPhase -= 1.0;
                }
                if (s.noiseMix > 0)
                {
                    double n = s.noiseColor == NoiseColor.Pink ? pinkV : white;
                    x = x * (1.0 - s.noiseMix) + n * s.noiseMix;
                }
                if (s.tremoloDepth > 0 && s.tremoloRate > 0)
                    x *= 1.0 - s.tremoloDepth * 0.5 * (1.0 - Math.Cos(DspMath.TwoPi * s.tremoloRate * t));

                if (useFilter)
                {
                    if ((i & 15) == 0) svf.SetParameters(FilterCutoffAt(s, t, env), s.filterResonance, sampleRate);
                    svf.Process(x, out var low, out var band, out var high);
                    x = s.filterType == GeneratorFilterType.LowPass ? low : s.filterType == GeneratorFilterType.HighPass ? high : band;
                }

                if (s.drive > 0) x = Math.Tanh(x * driveAmt) / driveNorm;

                outData[i] = DspMath.Sanitize((float)(x * env * volume));
            }
            return buffer;
        }

        /// <summary>ADSR amplitude envelope (0..1) at time t, with a 2 ms anti-click tail at the end.</summary>
        public static double EnvelopeAt(GeneratorSettings s, double t, double duration)
        {
            double a = Math.Max(0.0005, s.attack);
            double d = Math.Max(0.0, s.decay);
            double r = Math.Max(0.0005, s.release);
            double sus = DspMath.Clamp(s.sustain, 0.0, 1.0);
            double gateEnd = s.oneShot ? a + d : Math.Max(a + d, duration - r);

            double level;
            if (t < a) level = t / a;
            else if (t < a + d)
            {
                double k = (t - a) / Math.Max(1e-9, d);
                level = sus + (1.0 - sus) * (1.0 - k) * (1.0 - k);
            }
            else if (t < gateEnd) level = sus;
            else
            {
                double x = (t - gateEnd) / r;
                level = x >= 1.0 ? 0.0 : sus * (1.0 - x) * (1.0 - x);
            }

            double tailStart = duration - 0.002;
            if (t > tailStart) level *= Math.Max(0.0, (duration - t) / 0.002);
            return level;
        }

        /// <summary>Deterministic pitch offset in semitones (sweep + jump + vibrato; noise FM excluded).</summary>
        public static double PitchSemitonesAt(GeneratorSettings s, double t)
        {
            double sweep = s.pitchSweepTime > 0 ? DspMath.Clamp(t / s.pitchSweepTime, 0.0, 1.0) : 1.0;
            double curve = Math.Pow(sweep, Math.Max(0.05, s.pitchCurve));
            double semis = s.pitchStart + (s.pitchEnd - s.pitchStart) * curve;
            if (s.pitchJump != 0 && t >= s.pitchJumpTime) semis += s.pitchJump;
            if (s.vibratoDepth > 0 && s.vibratoRate > 0) semis += s.vibratoDepth * Math.Sin(DspMath.TwoPi * s.vibratoRate * t);
            return semis;
        }

        public static double FilterCutoffAt(GeneratorSettings s, double t, double env)
        {
            double oct = s.filterEnvOctaves * env;
            if (s.filterLfoOctaves != 0 && s.filterLfoRate > 0) oct += s.filterLfoOctaves * Math.Sin(DspMath.TwoPi * s.filterLfoRate * t);
            return s.filterCutoff * Math.Pow(2.0, oct);
        }

        /// <summary>Band-limited oscillator value at normalized phase [0,1) with phase increment dt.</summary>
        public static double OscillatorValue(OscillatorWaveform w, double phase, double dt, double duty)
        {
            switch (w)
            {
                case OscillatorWaveform.Square:
                {
                    duty = DspMath.Clamp(duty, 0.02, 0.98);
                    double v = phase < duty ? 1.0 : -1.0;
                    v += PolyBlep(phase, dt);
                    v -= PolyBlep(DspMath.Mod(phase + 1.0 - duty, 1.0), dt);
                    return v;
                }
                case OscillatorWaveform.Sawtooth:
                    return 2.0 * phase - 1.0 - PolyBlep(phase, dt);
                case OscillatorWaveform.Triangle:
                    // Naive triangle: harmonics fall at 12 dB/oct so aliasing is negligible for SFX use.
                    return 1.0 - 4.0 * Math.Abs(phase - 0.5);
                default:
                    return Math.Sin(DspMath.TwoPi * phase);
            }
        }

        private static double PolyBlep(double t, double dt)
        {
            if (dt <= 0) return 0;
            if (t < dt)
            {
                t /= dt;
                return t + t - t * t - 1.0;
            }
            if (t > 1.0 - dt)
            {
                t = (t - 1.0) / dt;
                return t * t + t + t + 1.0;
            }
            return 0.0;
        }

        /// <summary>Paul Kellet's economy pink-noise filter (−3 dB/oct) applied to white noise.</summary>
        private sealed class PinkNoise
        {
            private double _b0, _b1, _b2;
            public double Next(double white)
            {
                _b0 = 0.99765 * _b0 + white * 0.0990460;
                _b1 = 0.96300 * _b1 + white * 0.2965164;
                _b2 = 0.57000 * _b2 + white * 1.0526913;
                return (_b0 + _b1 + _b2 + white * 0.1848) * 0.22;
            }
        }
    }
}
