using System;

namespace AudioCreationStudio.DSP
{
    /// <summary>
    /// Platform-independent deterministic PRNG (SplitMix64 seeding + xorshift64*).
    /// Identical seeds produce identical sequences on every machine, which keeps
    /// procedural generation and variations reproducible.
    /// </summary>
    public sealed class DeterministicRandom
    {
        private ulong _state;

        public DeterministicRandom(int seed) : this((ulong)(uint)seed) { }

        public DeterministicRandom(ulong seed)
        {
            _state = SplitMix(seed + 0x9E3779B97F4A7C15UL);
            if (_state == 0) _state = 0x2545F4914F6CDD1DUL;
        }

        /// <summary>Derives a child seed for item <paramref name="index"/> of a seeded family.</summary>
        public static int DeriveSeed(int seed, int index)
        {
            ulong h = SplitMix(((ulong)(uint)seed << 32) ^ (uint)index ^ 0xD1B54A32D192ED03UL);
            return (int)(h & 0x7FFFFFFF);
        }

        private static ulong SplitMix(ulong z)
        {
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        public ulong NextULong()
        {
            _state ^= _state >> 12;
            _state ^= _state << 25;
            _state ^= _state >> 27;
            return _state * 0x2545F4914F6CDD1DUL;
        }

        /// <summary>Uniform double in [0, 1).</summary>
        public double NextDouble() => (NextULong() >> 11) * (1.0 / 9007199254740992.0);

        public float NextFloat() => (float)NextDouble();

        /// <summary>Uniform value in [-1, 1).</summary>
        public float NextSigned() => (float)(NextDouble() * 2.0 - 1.0);

        public float Range(float min, float max) => (float)(min + (max - min) * NextDouble());

        public double Range(double min, double max) => min + (max - min) * NextDouble();

        public int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) return minInclusive;
            return minInclusive + (int)(NextULong() % (ulong)(maxExclusive - minInclusive));
        }

        public bool Chance(double probability) => NextDouble() < probability;
    }
}
