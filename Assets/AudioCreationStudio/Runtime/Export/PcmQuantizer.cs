using System;
using AudioCreationStudio.DSP;

namespace AudioCreationStudio.Export
{
    /// <summary>
    /// Float → integer PCM with the same scaling as <see cref="WavEncoder"/>:
    /// sample = clamp(round(x · 2^(bits-1)), −2^(bits-1), 2^(bits-1) − 1), with optional TPDF ±1 LSB dither.
    /// Call <see cref="Quantize"/> in interleaved order so the dither sequence matches across containers.
    /// </summary>
    public sealed class PcmQuantizer
    {
        private readonly double _scale;
        private readonly long _max, _min;
        private readonly DeterministicRandom _rng;

        public int Bits { get; }

        public PcmQuantizer(int bits, bool dither = false, int ditherSeed = 1)
        {
            if (bits != 16 && bits != 24) throw new ArgumentOutOfRangeException(nameof(bits), "Only 16- and 24-bit PCM are supported.");
            Bits = bits;
            _scale = 1L << (bits - 1);
            _max = (1L << (bits - 1)) - 1;
            _min = -_max - 1;
            _rng = dither ? new DeterministicRandom(ditherSeed) : null;
        }

        public int Quantize(float x)
        {
            double v = DspMath.Sanitize(x) * _scale;
            if (_rng != null) v += _rng.NextDouble() - _rng.NextDouble();
            long s = (long)Math.Round(v, MidpointRounding.AwayFromZero);
            if (s > _max) s = _max; else if (s < _min) s = _min;
            return (int)s;
        }

        public static int BitsFor(Models.WavSampleFormat f) => f == Models.WavSampleFormat.Pcm16 ? 16 : 24;
    }
}
