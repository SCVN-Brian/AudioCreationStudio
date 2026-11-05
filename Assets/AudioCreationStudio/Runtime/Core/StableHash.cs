using System;

namespace AudioCreationStudio.DSP
{
    /// <summary>FNV-1a 64-bit hash builder used for render-cache keys. Stable across sessions.</summary>
    public struct StableHash
    {
        private const ulong Offset = 14695981039346656037UL;
        private const ulong Prime = 1099511628211UL;
        private ulong _h;
        private bool _init;

        public ulong Value => _init ? _h : Offset;

        private void Mix(ulong v)
        {
            if (!_init) { _h = Offset; _init = true; }
            for (int i = 0; i < 8; i++)
            {
                _h ^= (byte)(v >> (i * 8));
                _h *= Prime;
            }
        }

        public void Add(long v) => Mix((ulong)v);
        public void Add(ulong v) => Mix(v);
        public void Add(int v) => Mix((ulong)(uint)v);
        public void Add(bool v) => Mix(v ? 1UL : 0UL);
        public void Add(float v) => Mix((ulong)(uint)BitConverter.SingleToInt32Bits(v));
        public void Add(double v) => Mix((ulong)BitConverter.DoubleToInt64Bits(v));

        public void Add(string s)
        {
            if (s == null) { Mix(0xFFFFFFFFFFFFFFFFUL); return; }
            Mix((ulong)s.Length);
            foreach (char c in s) Mix(c);
        }

        public void Add(float[] values)
        {
            if (values == null) { Mix(0); return; }
            Mix((ulong)values.Length);
            foreach (var v in values) Add(v);
        }
    }
}
