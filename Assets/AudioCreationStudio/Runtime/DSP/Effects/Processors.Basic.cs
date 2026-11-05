using System;

namespace AudioCreationStudio.DSP
{
    /// <summary>Gain with a 10 ms one-pole ramp so automation never produces zipper noise.</summary>
    public sealed class GainEffect : EffectProcessorBase
    {
        private double _current = 1, _target = 1, _coef;
        private bool _first = true;

        protected override void OnPrepare(int maxBlockFrames) => _coef = DspMath.OnePoleCoefficient(0.01, SampleRate);

        protected override void OnParametersChanged()
        {
            _target = DspMath.DbToGain((double)P(0));
            if (_first) { _current = _target; _first = false; }
        }

        public override void Reset() { _current = _target; }

        public override void Process(AudioBuffer buffer, int offset, int frames)
        {
            int ch = Math.Min(Channels, buffer.Channels);
            double g = _current;
            for (int i = 0; i < frames; i++)
            {
                g = _target + (g - _target) * _coef;
                for (int c = 0; c < ch; c++) buffer.GetChannel(c)[offset + i] *= (float)g;
            }
            _current = Math.Abs(g - _target) < 1e-7 ? _target : g;
        }
    }

    /// <summary>Resonant low/high-pass biquad. Cutoff is smoothed in the log domain per 32-sample sub-block.</summary>
    public sealed class FilterEffect : EffectProcessorBase
    {
        private const int SubBlock = 32;
        private readonly bool _highPass;
        private readonly BiquadFilter _filter = new BiquadFilter();
        private double _logCutoff, _targetLogCutoff, _q, _coef;
        private bool _first = true;
        private double _appliedLog = double.NaN, _appliedQ = double.NaN;

        public FilterEffect(bool highPass) { _highPass = highPass; }

        protected override void OnPrepare(int maxBlockFrames)
        {
            _filter.Prepare(Math.Max(1, Channels));
            _coef = Math.Exp(-SubBlock / (0.015 * SampleRate));
        }

        protected override void OnParametersChanged()
        {
            _targetLogCutoff = Math.Log(Math.Max(10f, P(0)));
            _q = P(1);
            if (_first) { _logCutoff = _targetLogCutoff; _first = false; UpdateCoefficients(); }
        }

        public override void Reset()
        {
            _filter.Reset();
            _logCutoff = _targetLogCutoff;
            UpdateCoefficients();
        }

        private void UpdateCoefficients()
        {
            if (Math.Abs(_logCutoff - _appliedLog) < 1e-5 && _q == _appliedQ) return;
            double f = Math.Exp(_logCutoff);
            _filter.Coefficients = _highPass ? BiquadCoefficients.HighPass(SampleRate, f, _q) : BiquadCoefficients.LowPass(SampleRate, f, _q);
            _appliedLog = _logCutoff;
            _appliedQ = _q;
        }

        public override void Process(AudioBuffer buffer, int offset, int frames)
        {
            int ch = Math.Min(Channels, buffer.Channels);
            for (int pos = 0; pos < frames; pos += SubBlock)
            {
                int n = Math.Min(SubBlock, frames - pos);
                _logCutoff = _targetLogCutoff + (_logCutoff - _targetLogCutoff) * _coef;
                UpdateCoefficients();
                for (int c = 0; c < ch; c++) _filter.Process(buffer.GetChannel(c), offset + pos, n, c);
            }
        }

        public BiquadCoefficients CurrentCoefficients => _filter.Coefficients;
    }

    /// <summary>Four-band parametric EQ: low shelf → peak → peak → high shelf.</summary>
    public sealed class EqualizerEffect : EffectProcessorBase
    {
        private readonly BiquadFilter[] _bands = { new BiquadFilter(), new BiquadFilter(), new BiquadFilter(), new BiquadFilter() };
        private readonly bool[] _active = new bool[4];

        protected override void OnPrepare(int maxBlockFrames)
        {
            foreach (var b in _bands) b.Prepare(Math.Max(1, Channels));
        }

        public static BiquadCoefficients[] Design(float[] v, int sr)
        {
            return new[]
            {
                BiquadCoefficients.LowShelf(sr, v[0], v[1]),
                BiquadCoefficients.Peaking(sr, v[2], v[4], v[3]),
                BiquadCoefficients.Peaking(sr, v[5], v[7], v[6]),
                BiquadCoefficients.HighShelf(sr, v[8], v[9])
            };
        }

        protected override void OnParametersChanged()
        {
            if (Values.Length < 10) return;
            var k = Design(Values, SampleRate);
            float[] gains = { Values[1], Values[3], Values[6], Values[9] };
            for (int i = 0; i < 4; i++)
            {
                _bands[i].Coefficients = k[i];
                _active[i] = Math.Abs(gains[i]) > 0.001f;
            }
        }

        public override void Reset() { foreach (var b in _bands) b.Reset(); }

        public override void Process(AudioBuffer buffer, int offset, int frames)
        {
            int ch = Math.Min(Channels, buffer.Channels);
            for (int b = 0; b < 4; b++)
            {
                if (!_active[b]) continue;
                for (int c = 0; c < ch; c++) _bands[b].Process(buffer.GetChannel(c), offset, frames, c);
            }
        }
    }

    /// <summary>Waveshaping distortion (soft tanh, hard clip, triangle fold) followed by a one-pole tone filter.</summary>
    public sealed class DistortionEffect : EffectProcessorBase
    {
        private double[] _lp = new double[2];
        private double _toneCoef;

        protected override void OnPrepare(int maxBlockFrames) => _lp = new double[Math.Max(1, Channels)];

        protected override void OnParametersChanged()
        {
            double fc = DspMath.Clamp(P(2), 20f, SampleRate * 0.45f);
            _toneCoef = Math.Exp(-DspMath.TwoPi * fc / SampleRate);
        }

        public override void Reset() => Array.Clear(_lp, 0, _lp.Length);

        public static float Shape(int mode, float x)
        {
            switch (mode)
            {
                case 1: return x > 1f ? 1f : (x < -1f ? -1f : x);
                case 2:
                {
                    double t = DspMath.Mod(x + 1.0, 4.0);
                    return (float)(t < 2.0 ? t - 1.0 : 3.0 - t);
                }
                default: return (float)Math.Tanh(x);
            }
        }

        public override void Process(AudioBuffer buffer, int offset, int frames)
        {
            int mode = (int)Math.Round(P(0));
            float drive = DspMath.DbToGain(P(1));
            float mix = P(3);
            float outGain = DspMath.DbToGain(P(4));
            int ch = Math.Min(Channels, buffer.Channels);
            double a = _toneCoef;
            for (int c = 0; c < ch; c++)
            {
                var d = buffer.GetChannel(c);
                double lp = _lp[c];
                for (int i = offset, end = offset + frames; i < end; i++)
                {
                    float dry = d[i];
                    float wet = Shape(mode, dry * drive);
                    lp = wet + (lp - wet) * a;
                    d[i] = (float)((dry * (1f - mix) + lp * mix) * outGain);
                }
                _lp[c] = DspMath.FlushDenormal(lp);
            }
        }
    }
}
