using System;

namespace AudioCreationStudio.DSP
{
    /// <summary>
    /// Feedback delay with a smoothed, fractional (linearly interpolated) delay time, a one-pole
    /// low-pass in the feedback path and optional ping-pong routing.
    /// </summary>
    public sealed class DelayEffect : EffectProcessorBase
    {
        private const double MaxSeconds = 2.05;
        private float[][] _lines = Array.Empty<float[]>();
        private double[] _damp = Array.Empty<double>();
        private int _write, _size;
        private double _delay, _targetDelay, _smooth, _dampCoef;
        private bool _first = true;

        protected override void OnPrepare(int maxBlockFrames)
        {
            _size = (int)(MaxSeconds * SampleRate) + 4;
            int ch = Math.Max(1, Channels);
            _lines = new float[ch][];
            for (int c = 0; c < ch; c++) _lines[c] = new float[_size];
            _damp = new double[ch];
            _smooth = DspMath.OnePoleCoefficient(0.05, SampleRate);
        }

        protected override void OnParametersChanged()
        {
            _targetDelay = DspMath.Clamp(P(0) / 1000.0 * SampleRate, 1.0, _size - 4);
            if (_first) { _delay = _targetDelay; _first = false; }
            double fc = DspMath.Clamp(P(3), 20f, SampleRate * 0.45f);
            _dampCoef = Math.Exp(-DspMath.TwoPi * fc / SampleRate);
        }

        public override void Reset()
        {
            foreach (var l in _lines) Array.Clear(l, 0, l.Length);
            Array.Clear(_damp, 0, _damp.Length);
            _write = 0;
            _delay = _targetDelay;
        }

        public override int GetTailFrames()
        {
            double fb = P(1);
            double delaySamples = P(0) / 1000.0 * SampleRate;
            double repeats = fb <= 0.001 ? 1 : Math.Ceiling(Math.Log(0.001) / Math.Log(fb)) + 1;
            return (int)Math.Min(delaySamples * repeats, SampleRate * 10.0);
        }

        private float Read(float[] line, double delay)
        {
            double rp = _write - delay;
            if (rp < 0) rp += _size;
            int i0 = (int)rp;
            double f = rp - i0;
            int i1 = i0 + 1 >= _size ? 0 : i0 + 1;
            return (float)(line[i0] + (line[i1] - line[i0]) * f);
        }

        public override void Process(AudioBuffer buffer, int offset, int frames)
        {
            float fb = P(1), mix = P(2);
            bool ping = P(4) >= 0.5f && Channels >= 2 && buffer.Channels >= 2;
            int ch = Math.Min(Channels, buffer.Channels);
            for (int i = offset, end = offset + frames; i < end; i++)
            {
                _delay = _targetDelay + (_delay - _targetDelay) * _smooth;
                if (ping)
                {
                    var l = buffer.GetChannel(0);
                    var r = buffer.GetChannel(1);
                    float wl = Read(_lines[0], _delay), wr = Read(_lines[1], _delay);
                    _damp[0] = wl + (_damp[0] - wl) * _dampCoef;
                    _damp[1] = wr + (_damp[1] - wr) * _dampCoef;
                    float inMono = 0.5f * (l[i] + r[i]);
                    _lines[0][_write] = inMono + (float)_damp[1] * fb;
                    _lines[1][_write] = (float)_damp[0] * fb;
                    l[i] = l[i] * (1f - mix) + wl * mix;
                    r[i] = r[i] * (1f - mix) + wr * mix;
                }
                else
                {
                    for (int c = 0; c < ch; c++)
                    {
                        var d = buffer.GetChannel(c);
                        float wet = Read(_lines[c], _delay);
                        _damp[c] = wet + (_damp[c] - wet) * _dampCoef;
                        _lines[c][_write] = d[i] + (float)_damp[c] * fb;
                        d[i] = d[i] * (1f - mix) + wet * mix;
                    }
                }
                if (++_write >= _size) _write = 0;
            }
            for (int c = 0; c < _damp.Length; c++) _damp[c] = DspMath.FlushDenormal(_damp[c]);
        }
    }

    /// <summary>
    /// Freeverb-style reverb: 8 parallel damped comb filters into 4 series all-passes per channel, with
    /// the right channel's delay lines offset by a stereo spread. Delay lengths scale with sample rate.
    /// </summary>
    public sealed class ReverbEffect : EffectProcessorBase
    {
        private static readonly int[] CombTuning = { 1116, 1188, 1277, 1356, 1422, 1491, 1557, 1617 };
        private static readonly int[] AllpassTuning = { 556, 441, 341, 225 };
        private const int StereoSpread = 23;
        private const float FixedGain = 0.015f;

        private sealed class Comb
        {
            public float[] Buf; public int Idx; public float Store;
            public float Process(float input, float feedback, float damp)
            {
                float output = Buf[Idx];
                Store = output * (1f - damp) + Store * damp;
                if (Store > -1e-20f && Store < 1e-20f) Store = 0f;
                Buf[Idx] = input + Store * feedback;
                if (++Idx >= Buf.Length) Idx = 0;
                return output;
            }
        }

        private sealed class Allpass
        {
            public float[] Buf; public int Idx;
            public float Process(float input)
            {
                float bufout = Buf[Idx];
                float output = -input + bufout;
                Buf[Idx] = input + bufout * 0.5f;
                if (++Idx >= Buf.Length) Idx = 0;
                return output;
            }
        }

        private Comb[][] _combs;
        private Allpass[][] _allpasses;
        private float[][] _pre;
        private int _preWrite, _preSize;

        protected override void OnPrepare(int maxBlockFrames)
        {
            double scale = SampleRate / 44100.0;
            _combs = new Comb[2][];
            _allpasses = new Allpass[2][];
            for (int s = 0; s < 2; s++)
            {
                int spread = s * StereoSpread;
                _combs[s] = new Comb[CombTuning.Length];
                for (int i = 0; i < CombTuning.Length; i++)
                    _combs[s][i] = new Comb { Buf = new float[Math.Max(1, (int)((CombTuning[i] + spread) * scale))] };
                _allpasses[s] = new Allpass[AllpassTuning.Length];
                for (int i = 0; i < AllpassTuning.Length; i++)
                    _allpasses[s][i] = new Allpass { Buf = new float[Math.Max(1, (int)((AllpassTuning[i] + spread) * scale))] };
            }
            _preSize = (int)(0.21 * SampleRate) + 2;
            _pre = new[] { new float[_preSize], new float[_preSize] };
        }

        public override void Reset()
        {
            if (_combs == null) return;
            for (int s = 0; s < 2; s++)
            {
                foreach (var c in _combs[s]) { Array.Clear(c.Buf, 0, c.Buf.Length); c.Store = 0; c.Idx = 0; }
                foreach (var a in _allpasses[s]) { Array.Clear(a.Buf, 0, a.Buf.Length); a.Idx = 0; }
                Array.Clear(_pre[s], 0, _preSize);
            }
            _preWrite = 0;
        }

        private float Feedback => P(0) * 0.28f + 0.7f;

        public override int GetTailFrames()
        {
            double g = Feedback;
            double loop = 1617.0 / 44100.0;
            double t60 = loop * Math.Log(0.001) / Math.Log(g);
            return (int)Math.Min((t60 + P(3) / 1000.0) * SampleRate, SampleRate * 8.0);
        }

        public override void Process(AudioBuffer buffer, int offset, int frames)
        {
            float feedback = Feedback;
            float damp = P(1) * 0.4f;
            float width = P(2);
            int preDelay = (int)DspMath.Clamp(P(3) / 1000f * SampleRate, 0, _preSize - 2);
            float mix = P(4);
            float wet = mix * 3f;
            float wet1 = wet * (width * 0.5f + 0.5f);
            float wet2 = wet * ((1f - width) * 0.5f);
            float dry = 1f - mix;
            bool stereo = buffer.Channels >= 2 && Channels >= 2;
            var l = buffer.GetChannel(0);
            var r = stereo ? buffer.GetChannel(1) : null;

            for (int i = offset, end = offset + frames; i < end; i++)
            {
                float inL = l[i], inR = stereo ? r[i] : inL;
                float input = (inL + inR) * FixedGain;
                _pre[0][_preWrite] = input;
                int rd = _preWrite - preDelay;
                if (rd < 0) rd += _preSize;
                float delayed = _pre[0][rd];
                if (++_preWrite >= _preSize) _preWrite = 0;

                float outL = 0f, outR = 0f;
                var cl = _combs[0];
                var cr = _combs[1];
                for (int k = 0; k < cl.Length; k++)
                {
                    outL += cl[k].Process(delayed, feedback, damp);
                    if (stereo) outR += cr[k].Process(delayed, feedback, damp);
                }
                foreach (var a in _allpasses[0]) outL = a.Process(outL);
                if (stereo)
                {
                    foreach (var a in _allpasses[1]) outR = a.Process(outR);
                    l[i] = outL * wet1 + outR * wet2 + inL * dry;
                    r[i] = outR * wet1 + outL * wet2 + inR * dry;
                }
                else
                {
                    l[i] = outL * wet + inL * dry;
                }
            }
        }
    }
}
