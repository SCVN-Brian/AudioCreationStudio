using System;
using System.IO;
using System.Text;
using System.Threading;
using AudioCreationStudio.DSP;
using AudioCreationStudio.Models;

namespace AudioCreationStudio.Export
{
    public sealed class AiffInfo
    {
        public bool IsAifc;
        public string Compression = "NONE";
        public int Channels;
        public int SampleRate;
        public int BitsPerSample;
        public long Frames;
        public long DataOffset;
    }

    /// <summary>
    /// AIFF encoder (big-endian PCM 16/24-bit) and AIFF-C encoder for 32-bit float ('fl32').
    /// Uses the same quantization and dither as <see cref="WavEncoder"/>.
    /// </summary>
    public static class AiffEncoder
    {
        public static void Write(Stream stream, AudioBuffer buffer, WavSampleFormat format, bool dither = false, int ditherSeed = 1,
            CancellationToken ct = default, IProgress<float> progress = null)
        {
            int ch = buffer.Channels;
            bool isFloat = format == WavSampleFormat.Float32;
            int bps = isFloat ? 4 : WavEncoder.BytesPerSample(format);
            long frames = buffer.Frames;
            long dataBytes = frames * ch * bps;
            if (dataBytes > int.MaxValue - 1024) throw new InvalidOperationException("Audio too long for an AIFF file.");

            byte[] compName = Encoding.ASCII.GetBytes("32-bit floating point");
            int pstrLen = 1 + compName.Length; // count byte + chars
            int pstrPadded = pstrLen + (pstrLen & 1);
            int commSize = isFloat ? 18 + 4 + pstrPadded : 18;
            long ssndSize = 8 + dataBytes;
            long formSize = 4 + (isFloat ? 8 + 4 : 0) + 8 + commSize + 8 + ssndSize + (ssndSize & 1);

            var w = new BeWriter(stream);
            w.Id("FORM"); w.U32((uint)formSize); w.Id(isFloat ? "AIFC" : "AIFF");
            if (isFloat) { w.Id("FVER"); w.U32(4); w.U32(0xA2805140); }

            w.Id("COMM"); w.U32((uint)commSize);
            w.U16((ushort)ch); w.U32((uint)frames); w.U16((ushort)(bps * 8)); w.Extended(buffer.SampleRate);
            if (isFloat)
            {
                w.Id("fl32");
                w.Byte((byte)compName.Length); w.Bytes(compName, compName.Length);
                if ((pstrLen & 1) == 1) w.Byte(0);
            }

            w.Id("SSND"); w.U32((uint)ssndSize); w.U32(0); w.U32(0);

            var q = isFloat ? null : new PcmQuantizer(bps * 8, dither, ditherSeed);
            const int chunkFrames = 4096;
            var bytes = new byte[chunkFrames * ch * bps];
            for (long f0 = 0; f0 < frames; f0 += chunkFrames)
            {
                ct.ThrowIfCancellationRequested();
                int n = (int)Math.Min(chunkFrames, frames - f0);
                int bi = 0;
                for (int i = 0; i < n; i++)
                {
                    for (int c = 0; c < ch; c++)
                    {
                        float x = buffer.GetChannel(c)[f0 + i];
                        if (isFloat)
                        {
                            int b = BitConverter.SingleToInt32Bits(DspMath.Sanitize(x));
                            bytes[bi++] = (byte)(b >> 24); bytes[bi++] = (byte)(b >> 16); bytes[bi++] = (byte)(b >> 8); bytes[bi++] = (byte)b;
                            continue;
                        }
                        int s = q.Quantize(x);
                        if (bps == 3) bytes[bi++] = (byte)(s >> 16);
                        bytes[bi++] = (byte)(s >> 8);
                        bytes[bi++] = (byte)s;
                    }
                }
                w.Bytes(bytes, bi);
                progress?.Report((float)(f0 + n) / Math.Max(1, frames));
            }
            if ((ssndSize & 1) == 1) w.Byte(0);
            w.Flush();
        }

        public static byte[] Encode(AudioBuffer buffer, WavSampleFormat format, bool dither = false)
        {
            using (var ms = new MemoryStream()) { Write(ms, buffer, format, dither); return ms.ToArray(); }
        }

        private sealed class BeWriter
        {
            private readonly Stream _s;
            private readonly byte[] _tmp = new byte[10];
            public BeWriter(Stream s) { _s = s; }
            public void Id(string id) { for (int i = 0; i < 4; i++) _s.WriteByte((byte)id[i]); }
            public void Byte(byte b) => _s.WriteByte(b);
            public void Bytes(byte[] b, int n) => _s.Write(b, 0, n);
            public void U16(ushort v) { _tmp[0] = (byte)(v >> 8); _tmp[1] = (byte)v; _s.Write(_tmp, 0, 2); }
            public void U32(uint v) { _tmp[0] = (byte)(v >> 24); _tmp[1] = (byte)(v >> 16); _tmp[2] = (byte)(v >> 8); _tmp[3] = (byte)v; _s.Write(_tmp, 0, 4); }
            /// <summary>80-bit IEEE 754 extended, exact for integer sample rates.</summary>
            public void Extended(int rate)
            {
                if (rate <= 0) { Array.Clear(_tmp, 0, 10); _s.Write(_tmp, 0, 10); return; }
                int exp = 16383 + 63;
                ulong mant = (ulong)rate;
                while ((mant & 0x8000000000000000UL) == 0) { mant <<= 1; exp--; }
                _tmp[0] = (byte)(exp >> 8); _tmp[1] = (byte)exp;
                for (int i = 0; i < 8; i++) _tmp[2 + i] = (byte)(mant >> (56 - 8 * i));
                _s.Write(_tmp, 0, 10);
            }
            public void Flush() => _s.Flush();
        }
    }

    /// <summary>AIFF / AIFF-C reader for PCM 8/16/24/32 and 'fl32'/'FL32' float; used for export validation and tests.</summary>
    public static class AiffDecoder
    {
        public static AiffInfo ReadInfo(Stream s)
        {
            var r = new BeReader(s);
            if (s.Length < 12 || r.Id() != "FORM") throw new InvalidDataException("Missing FORM header.");
            r.U32();
            string type = r.Id();
            if (type != "AIFF" && type != "AIFC") throw new InvalidDataException("Not an AIFF/AIFF-C file.");
            var info = new AiffInfo { IsAifc = type == "AIFC" };
            bool haveComm = false, haveData = false;
            while (s.Position + 8 <= s.Length)
            {
                string id = r.Id();
                uint size = r.U32();
                long next = s.Position + size + (size & 1);
                if (id == "COMM")
                {
                    info.Channels = r.U16();
                    info.Frames = r.U32();
                    info.BitsPerSample = r.U16();
                    info.SampleRate = (int)Math.Round(r.Extended());
                    if (info.IsAifc && size >= 22) info.Compression = r.Id();
                    haveComm = true;
                }
                else if (id == "SSND")
                {
                    uint offset = r.U32();
                    r.U32();
                    info.DataOffset = s.Position + offset;
                    haveData = true;
                }
                if (next > s.Length) break;
                s.Position = next;
            }
            if (!haveComm || !haveData) throw new InvalidDataException("Missing COMM or SSND chunk.");
            if (info.Channels <= 0 || info.BitsPerSample <= 0) throw new InvalidDataException("Invalid format.");
            return info;
        }

        public static AudioBuffer Decode(byte[] bytes) { using (var ms = new MemoryStream(bytes)) return Decode(ms, out _); }

        public static AudioBuffer Decode(Stream s, out AiffInfo info)
        {
            info = ReadInfo(s);
            string comp = info.Compression;
            bool isFloat = comp == "fl32" || comp == "FL32";
            if (!isFloat && comp != "NONE" && comp != "twos") throw new InvalidDataException($"Unsupported AIFF-C compression '{comp}'.");
            int bytesPer = (info.BitsPerSample + 7) / 8;
            if (info.Frames > int.MaxValue) throw new InvalidDataException("File too long.");
            int ch = info.Channels;
            var buf = new AudioBuffer(Math.Min(ch, 8), (int)info.Frames, info.SampleRate);
            s.Position = info.DataOffset;
            var data = new byte[info.Frames * ch * bytesPer];
            int read = 0;
            while (read < data.Length) { int n = s.Read(data, read, data.Length - read); if (n <= 0) break; read += n; }
            int idx = 0;
            for (int f = 0; f < info.Frames; f++)
            {
                for (int c = 0; c < ch; c++)
                {
                    float v;
                    if (isFloat)
                    {
                        int b = (data[idx] << 24) | (data[idx + 1] << 16) | (data[idx + 2] << 8) | data[idx + 3];
                        v = BitConverter.Int32BitsToSingle(b);
                    }
                    else switch (bytesPer)
                    {
                        case 1: v = (sbyte)data[idx] / 128f; break;
                        case 2: v = (short)((data[idx] << 8) | data[idx + 1]) / 32768f; break;
                        case 3:
                        {
                            int x = (data[idx] << 16) | (data[idx + 1] << 8) | data[idx + 2];
                            if ((x & 0x800000) != 0) x |= unchecked((int)0xFF000000);
                            v = x / 8388608f;
                            break;
                        }
                        case 4: v = ((data[idx] << 24) | (data[idx + 1] << 16) | (data[idx + 2] << 8) | data[idx + 3]) / 2147483648f; break;
                        default: throw new InvalidDataException("Unsupported bit depth.");
                    }
                    if (c < buf.Channels) buf.GetChannel(c)[f] = v;
                    idx += bytesPer;
                }
            }
            return buf;
        }

        private sealed class BeReader
        {
            private readonly Stream _s;
            private readonly byte[] _b = new byte[10];
            public BeReader(Stream s) { _s = s; }
            private void Fill(int n) { int r = 0; while (r < n) { int k = _s.Read(_b, r, n - r); if (k <= 0) throw new EndOfStreamException(); r += k; } }
            public string Id() { Fill(4); return Encoding.ASCII.GetString(_b, 0, 4); }
            public ushort U16() { Fill(2); return (ushort)((_b[0] << 8) | _b[1]); }
            public uint U32() { Fill(4); return (uint)((_b[0] << 24) | (_b[1] << 16) | (_b[2] << 8) | _b[3]); }
            public double Extended()
            {
                Fill(10);
                int exp = ((_b[0] & 0x7F) << 8) | _b[1];
                ulong mant = 0;
                for (int i = 0; i < 8; i++) mant = (mant << 8) | _b[2 + i];
                if (exp == 0 && mant == 0) return 0;
                double v = mant * Math.Pow(2, exp - 16383 - 63);
                return (_b[0] & 0x80) != 0 ? -v : v;
            }
        }
    }
}
