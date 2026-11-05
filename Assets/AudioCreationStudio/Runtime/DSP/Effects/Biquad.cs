using System;

namespace AudioCreationStudio.DSP
{
    /// <summary>RBJ "Audio EQ Cookbook" biquad coefficients (normalised so a0 = 1).</summary>
    public struct BiquadCoefficients
    {
        public double b0, b1, b2, a1, a2;

        public static BiquadCoefficients Identity => new BiquadCoefficients { b0 = 1 };

        private static void Prewarp(int sr, double freq, double q, out double cosw, out double alpha, out double sinw)
        {
            freq = DspMath.Clamp(freq, 5.0, sr * 0.49);
            q = Math.Max(0.025, q);
            double w0 = DspMath.TwoPi * freq / sr;
            cosw = Math.Cos(w0);
            sinw = Math.Sin(w0);
            alpha = sinw / (2.0 * q);
        }

        private static BiquadCoefficients Make(double b0, double b1, double b2, double a0, double a1, double a2)
        {
            return new BiquadCoefficients { b0 = b0 / a0, b1 = b1 / a0, b2 = b2 / a0, a1 = a1 / a0, a2 = a2 / a0 };
        }

        public static BiquadCoefficients LowPass(int sr, double freq, double q)
        {
            Prewarp(sr, freq, q, out var c, out var alpha, out _);
            return Make((1 - c) / 2, 1 - c, (1 - c) / 2, 1 + alpha, -2 * c, 1 - alpha);
        }

        public static BiquadCoefficients HighPass(int sr, double freq, double q)
        {
            Prewarp(sr, freq, q, out var c, out var alpha, out _);
            return Make((1 + c) / 2, -(1 + c), (1 + c) / 2, 1 + alpha, -2 * c, 1 - alpha);
        }

        /// <summary>Constant 0 dB peak gain band-pass.</summary>
        public static BiquadCoefficients BandPass(int sr, double freq, double q)
        {
            Prewarp(sr, freq, q, out var c, out var alpha, out _);
            return Make(alpha, 0, -alpha, 1 + alpha, -2 * c, 1 - alpha);
        }

        public static BiquadCoefficients Peaking(int sr, double freq, double q, double gainDb)
        {
            Prewarp(sr, freq, q, out var c, out var alpha, out _);
            double A = Math.Pow(10, gainDb / 40.0);
            return Make(1 + alpha * A, -2 * c, 1 - alpha * A, 1 + alpha / A, -2 * c, 1 - alpha / A);
        }

        public static BiquadCoefficients LowShelf(int sr, double freq, double gainDb)
        {
            Prewarp(sr, freq, 0.7071, out var c, out _, out var s);
            double A = Math.Pow(10, gainDb / 40.0);
            double alpha = s / 2.0 * Math.Sqrt(2.0);
            double sq = 2 * Math.Sqrt(A) * alpha;
            return Make(A * ((A + 1) - (A - 1) * c + sq), 2 * A * ((A - 1) - (A + 1) * c), A * ((A + 1) - (A - 1) * c - sq),
                (A + 1) + (A - 1) * c + sq, -2 * ((A - 1) + (A + 1) * c), (A + 1) + (A - 1) * c - sq);
        }

        public static BiquadCoefficients HighShelf(int sr, double freq, double gainDb)
        {
            Prewarp(sr, freq, 0.7071, out var c, out _, out var s);
            double A = Math.Pow(10, gainDb / 40.0);
            double alpha = s / 2.0 * Math.Sqrt(2.0);
            double sq = 2 * Math.Sqrt(A) * alpha;
            return Make(A * ((A + 1) + (A - 1) * c + sq), -2 * A * ((A - 1) + (A + 1) * c), A * ((A + 1) + (A - 1) * c - sq),
                (A + 1) - (A - 1) * c + sq, 2 * ((A - 1) - (A + 1) * c), (A + 1) - (A - 1) * c - sq);
        }

        /// <summary>Magnitude response |H(e^jω)| at <paramref name="freq"/>. Used by the UI response curves.</summary>
        public double MagnitudeAt(double freq, int sr)
        {
            double w = DspMath.TwoPi * freq / sr;
            double c1 = Math.Cos(w), s1 = Math.Sin(w), c2 = Math.Cos(2 * w), s2 = Math.Sin(2 * w);
            double nr = b0 + b1 * c1 + b2 * c2, ni = -(b1 * s1 + b2 * s2);
            double dr = 1 + a1 * c1 + a2 * c2, di = -(a1 * s1 + a2 * s2);
            double num = nr * nr + ni * ni, den = dr * dr + di * di;
            return den <= 1e-30 ? 0 : Math.Sqrt(num / den);
        }
    }

    /// <summary>Multi-channel transposed direct form II biquad with double-precision state.</summary>
    public sealed class BiquadFilter
    {
        private double[] _z1 = new double[2], _z2 = new double[2];
        public BiquadCoefficients Coefficients = BiquadCoefficients.Identity;

        public void Prepare(int channels)
        {
            _z1 = new double[channels];
            _z2 = new double[channels];
        }

        public void Reset()
        {
            Array.Clear(_z1, 0, _z1.Length);
            Array.Clear(_z2, 0, _z2.Length);
        }

        public void Process(float[] data, int offset, int count, int channel)
        {
            var k = Coefficients;
            double z1 = _z1[channel], z2 = _z2[channel];
            for (int i = offset, end = offset + count; i < end; i++)
            {
                double x = data[i];
                double y = k.b0 * x + z1;
                z1 = k.b1 * x - k.a1 * y + z2;
                z2 = k.b2 * x - k.a2 * y;
                data[i] = (float)y;
            }
            _z1[channel] = DspMath.FlushDenormal(z1);
            _z2[channel] = DspMath.FlushDenormal(z2);
        }

        public float ProcessSample(float x, int channel)
        {
            var k = Coefficients;
            double y = k.b0 * x + _z1[channel];
            _z1[channel] = k.b1 * x - k.a1 * y + _z2[channel];
            _z2[channel] = k.b2 * x - k.a2 * y;
            return (float)y;
        }
    }

    /// <summary>
    /// Topology-preserving-transform state variable filter (Zavalishin). Stable under fast cutoff
    /// modulation, used by the procedural generator. Its magnitude response equals the RBJ biquad of
    /// the same cutoff/Q (both are bilinear transforms of the analog prototype with prewarping).
    /// </summary>
    public sealed class StateVariableFilter
    {
        private double _ic1, _ic2, _g, _k, _a1, _a2, _a3;

        public void Reset() { _ic1 = _ic2 = 0; }

        public void SetParameters(double cutoff, double q, int sampleRate)
        {
            cutoff = DspMath.Clamp(cutoff, 10.0, sampleRate * 0.45);
            _g = Math.Tan(Math.PI * cutoff / sampleRate);
            _k = 1.0 / Math.Max(0.05, q);
            _a1 = 1.0 / (1.0 + _g * (_g + _k));
            _a2 = _g * _a1;
            _a3 = _g * _a2;
        }

        /// <summary>Processes one sample, returning (low, band, high).</summary>
        public void Process(double v0, out double low, out double band, out double high)
        {
            double v3 = v0 - _ic2;
            double v1 = _a1 * _ic1 + _a2 * v3;
            double v2 = _ic2 + _a2 * _ic1 + _a3 * v3;
            _ic1 = DspMath.FlushDenormal(2 * v1 - _ic1);
            _ic2 = DspMath.FlushDenormal(2 * v2 - _ic2);
            low = v2;
            band = v1;
            high = v0 - _k * v1 - v2;
        }
    }
}
