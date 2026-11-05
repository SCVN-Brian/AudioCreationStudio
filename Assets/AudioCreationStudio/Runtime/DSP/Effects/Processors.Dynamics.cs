using System;

namespace AudioCreationStudio.DSP
{
    /// <summary>
    /// Stereo-linked feed-forward compressor. Peak detector → soft-knee gain computer (dB domain) →
    /// attack/release ballistics on the gain reduction → makeup and parallel mix.
    /// </summary>
    public sealed class CompressorEffect : EffectProcessorBase
    {
        private double _envDb;
        private double _att, _rel;
        public float LastGainReductionDb { get; private set; }

        protected override void OnParametersChanged()
        {
            _att = DspMath.OnePoleCoefficient(P(2) / 1000.0, SampleRate);
            _rel = DspMath.OnePoleCoefficient(P(3) / 1000.0, SampleRate);
        }

        public override void Reset() { _envDb = 0; }

        public static double ComputeGainReduction(double levelDb, double threshold, double ratio, double knee)
        {
            double over = levelDb - threshold;
            double slope = 1.0 / Math.Max(1.0, ratio) - 1.0;
            if (knee > 0 && Math.Abs(over) * 2 <= knee)
            {
                double x = over + knee * 0.5;
                return slope * x * x / (2 * knee);
            }
            return over > 0 ? slope * over : 0.0;
        }

        public override void Process(AudioBuffer buffer, int offset, int frames)
        {
            double thr = P(0), ratio = P(1), knee = P(4);
            double makeup = P(5);
            float mix = P(6);
            int ch = Math.Min(Channels, buffer.Channels);
            double env = _envDb, minEnv = 0;
            for (int i = offset, end = offset + frames; i < end; i++)
            {
                float peak = 0f;
                for (int c = 0; c < ch; c++)
                {
                    float a = Math.Abs(buffer.GetChannel(c)[i]);
                    if (a > peak) peak = a;
                }
                double level = DspMath.GainToDb(peak);
                double target = ComputeGainReduction(level, thr, ratio, knee);
                double coef = target < env ? _att : _rel;
                env = target + (env - target) * coef;
                if (env < minEnv) minEnv = env;
                float g = (float)DspMath.DbToGain(env + makeup);
                float wetGain = mix * g + (1f - mix);
                for (int c = 0; c < ch; c++) buffer.GetChannel(c)[i] *= wetGain;
            }
            _envDb = DspMath.FlushDenormal(env);
            LastGainReductionDb = (float)minEnv;
        }
    }

    /// <summary>
    /// Zero-latency brickwall limiter: the gain envelope can never exceed the gain required by the
    /// current sample (instant attack), and recovers with the release time. Output is guaranteed to
    /// stay at or below the ceiling, with no latency so timeline alignment is preserved.
    /// </summary>
    public sealed class LimiterEffect : EffectProcessorBase
    {
        private double _env = 1, _rel;

        protected override void OnParametersChanged() => _rel = DspMath.OnePoleCoefficient(P(2) / 1000.0, SampleRate);

        public override void Reset() { _env = 1; }

        public override void Process(AudioBuffer buffer, int offset, int frames)
        {
            float ceiling = DspMath.DbToGain(P(0));
            float input = DspMath.DbToGain(P(1));
            int ch = Math.Min(Channels, buffer.Channels);
            double env = _env;
            for (int i = offset, end = offset + frames; i < end; i++)
            {
                float peak = 0f;
                for (int c = 0; c < ch; c++)
                {
                    float a = Math.Abs(buffer.GetChannel(c)[i] * input);
                    if (a > peak) peak = a;
                }
                double required = peak > ceiling ? ceiling / peak : 1.0;
                env = 1.0 + (env - 1.0) * _rel; // release toward unity
                if (required < env) env = required;
                float g = (float)(env * input);
                for (int c = 0; c < ch; c++)
                {
                    var d = buffer.GetChannel(c);
                    float y = d[i] * g;
                    // Guard against float rounding just above the ceiling.
                    if (y > ceiling) y = ceiling; else if (y < -ceiling) y = -ceiling;
                    d[i] = y;
                }
            }
            _env = env;
        }
    }
}
