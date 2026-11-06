using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using AudioCreationStudio.DSP;
using AudioCreationStudio.Models;

namespace AudioCreationStudio.Export
{
    public sealed class FlacInfo
    {
        public int SampleRate;
        public int Channels;
        public int BitsPerSample;
        public long Frames;
        public byte[] Md5 = new byte[16];
        /// <summary>Set by the decoder: true when the decoded audio matches the STREAMINFO MD5 (or the MD5 is unset).</summary>
        public bool Md5Matches;
    }

    /// <summary>
    /// Lossless FLAC encoder in pure C#: 16- or 24-bit, 1–8 channels, fixed 4096-frame blocks,
    /// FIXED predictors (order 0–4) with partitioned Rice residuals, CONSTANT/VERBATIM fallbacks and
    /// stereo decorrelation (left/side, right/side, mid/side). Writes STREAMINFO with MD5 and frame sizes.
    /// Float32 input is stored as 24-bit (FLAC has no float mode).
    /// </summary>
    public static class FlacEncoder
    {
        public const int BlockSize = 4096;
        private const int MaxPartitionOrder = 8;

        public static void Write(Stream stream, AudioBuffer buffer, WavSampleFormat format, bool dither = false, int ditherSeed = 1,
            CancellationToken ct = default, IProgress<float> progress = null)
        {
            int ch = buffer.Channels;
            if (ch < 1 || ch > 8) throw new InvalidOperationException("FLAC supports 1–8 channels.");
            if (buffer.SampleRate <= 0 || buffer.SampleRate > 655350) throw new InvalidOperationException("Sample rate not representable in FLAC.");
            int bps = PcmQuantizer.BitsFor(format);
            int frames = buffer.Frames;

            // Quantize in interleaved order so dither matches the WAV/AIFF encoders.
            var q = new PcmQuantizer(bps, dither && bps == 16, ditherSeed);
            var pcm = new int[ch][];
            for (int c = 0; c < ch; c++) pcm[c] = new int[frames];
            for (int i = 0; i < frames; i++)
                for (int c = 0; c < ch; c++) pcm[c][i] = q.Quantize(buffer.GetChannel(c)[i]);

            var md5 = Md5Of(pcm, frames, bps);
            var body = new BitWriter(frames * ch * bps / 16 + 1024);
            int minFrame = int.MaxValue, maxFrame = 0;
            var work = new Workspace(BlockSize);
            var block = new int[ch][];
            for (int c = 0; c < ch; c++) block[c] = new int[BlockSize];
            uint frameNo = 0;

            for (int f0 = 0; f0 < frames; f0 += BlockSize, frameNo++)
            {
                ct.ThrowIfCancellationRequested();
                int n = Math.Min(BlockSize, frames - f0);
                for (int c = 0; c < ch; c++) Array.Copy(pcm[c], f0, block[c], 0, n);
                int start = body.Length;
                WriteFrame(body, block, n, ch, bps, buffer.SampleRate, frameNo, work);
                int size = body.Length - start;
                if (size < minFrame) minFrame = size;
                if (size > maxFrame) maxFrame = size;
                progress?.Report((float)(f0 + n) / Math.Max(1, frames));
            }
            if (minFrame == int.MaxValue) minFrame = 0;

            var head = new BitWriter(64);
            head.Write(0x664C6143, 32); // "fLaC"
            head.Write(1, 1); head.Write(0, 7); head.Write(34, 24); // last metadata block, STREAMINFO, length
            head.Write(BlockSize, 16); head.Write(BlockSize, 16);
            head.Write((uint)minFrame, 24); head.Write((uint)maxFrame, 24);
            head.Write((uint)buffer.SampleRate, 20);
            head.Write((uint)(ch - 1), 3);
            head.Write((uint)(bps - 1), 5);
            head.Write((uint)((ulong)frames >> 32), 4); head.Write((uint)frames, 32);
            for (int i = 0; i < 16; i++) head.Write(md5[i], 8);
            stream.Write(head.Buffer, 0, head.Length);
            stream.Write(body.Buffer, 0, body.Length);
            stream.Flush();
        }

        public static byte[] Encode(AudioBuffer buffer, WavSampleFormat format, bool dither = false)
        {
            using (var ms = new MemoryStream()) { Write(ms, buffer, format, dither); return ms.ToArray(); }
        }

        internal static byte[] Md5Of(int[][] pcm, int frames, int bps)
        {
            int bytesPer = bps / 8, ch = pcm.Length;
            using (var md5 = MD5.Create())
            {
                var chunk = new byte[4096 * ch * bytesPer];
                for (int f0 = 0; f0 < frames; f0 += 4096)
                {
                    int n = Math.Min(4096, frames - f0), bi = 0;
                    for (int i = 0; i < n; i++)
                        for (int c = 0; c < ch; c++)
                        {
                            int s = pcm[c][f0 + i];
                            for (int b = 0; b < bytesPer; b++) chunk[bi++] = (byte)(s >> (8 * b));
                        }
                    md5.TransformBlock(chunk, 0, bi, null, 0);
                }
                md5.TransformFinalBlock(chunk, 0, 0);
                return md5.Hash;
            }
        }

        // ------------------------------------------------------------------ frame

        private static void WriteFrame(BitWriter w, int[][] block, int n, int ch, int bps, int rate, uint frameNo, Workspace ws)
        {
            int start = w.Length;
            int assignment = ch - 1;
            Plan[] plans = new Plan[ch];
            int[][] sources = new int[ch][];
            int[] sbps = new int[ch];

            if (ch == 2)
            {
                var l = block[0]; var r = block[1];
                var mid = ws.Mid; var side = ws.Side;
                for (int i = 0; i < n; i++) { long a = l[i], b = r[i]; side[i] = (int)(a - b); mid[i] = (int)((a + b) >> 1); }
                var pl = BestSubframe(l, n, bps, ws, 0);
                var pr = BestSubframe(r, n, bps, ws, 1);
                var pm = BestSubframe(mid, n, bps, ws, 2);
                var ps = BestSubframe(side, n, bps + 1, ws, 3);
                long ind = pl.Bits + pr.Bits, ls = pl.Bits + ps.Bits, rs = ps.Bits + pr.Bits, ms = pm.Bits + ps.Bits;
                long best = Math.Min(Math.Min(ind, ls), Math.Min(rs, ms));
                if (best == ind) { assignment = 1; plans[0] = pl; plans[1] = pr; sources[0] = l; sources[1] = r; sbps[0] = bps; sbps[1] = bps; }
                else if (best == ls) { assignment = 8; plans[0] = pl; plans[1] = ps; sources[0] = l; sources[1] = side; sbps[0] = bps; sbps[1] = bps + 1; }
                else if (best == rs) { assignment = 9; plans[0] = ps; plans[1] = pr; sources[0] = side; sources[1] = r; sbps[0] = bps + 1; sbps[1] = bps; }
                else { assignment = 10; plans[0] = pm; plans[1] = ps; sources[0] = mid; sources[1] = side; sbps[0] = bps; sbps[1] = bps + 1; }
            }
            else
            {
                for (int c = 0; c < ch; c++) { plans[c] = BestSubframe(block[c], n, bps, ws, c % 4); sources[c] = block[c]; sbps[c] = bps; }
            }

            int bsCode = n == BlockSize ? 12 : n <= 256 ? 6 : 7;
            w.Write(0xFFF8, 16);
            w.Write((uint)bsCode, 4);
            w.Write((uint)RateCode(rate), 4);
            w.Write((uint)assignment, 4);
            w.Write(bps == 16 ? 4u : 6u, 3);
            w.Write(0, 1);
            WriteUtf8(w, frameNo);
            if (bsCode == 6) w.Write((uint)(n - 1), 8);
            else if (bsCode == 7) w.Write((uint)(n - 1), 16);
            w.Write(Crc.Crc8(w.Buffer, start, w.Length - start), 8);

            for (int c = 0; c < ch; c++) WriteSubframe(w, sources[c], n, sbps[c], plans[c], ws);
            w.AlignToByte();
            w.Write(Crc.Crc16(w.Buffer, start, w.Length - start), 16);
        }

        private static int RateCode(int rate)
        {
            switch (rate)
            {
                case 88200: return 1;
                case 176400: return 2;
                case 192000: return 3;
                case 8000: return 4;
                case 16000: return 5;
                case 22050: return 6;
                case 24000: return 7;
                case 32000: return 8;
                case 44100: return 9;
                case 48000: return 10;
                case 96000: return 11;
                default: return 0; // from STREAMINFO
            }
        }

        private static void WriteUtf8(BitWriter w, uint v)
        {
            if (v < 0x80) { w.Write(v, 8); return; }
            int bytes = v < 0x800 ? 2 : v < 0x10000 ? 3 : v < 0x200000 ? 4 : v < 0x4000000 ? 5 : 6;
            uint first = (uint)((0xFF << (8 - bytes)) & 0xFF) | (v >> (6 * (bytes - 1)));
            w.Write(first & 0xFF, 8);
            for (int i = bytes - 2; i >= 0; i--) w.Write(0x80u | ((v >> (6 * i)) & 0x3F), 8);
        }

        // ------------------------------------------------------------------ subframes

        private enum Kind { Constant, Verbatim, Fixed }

        private struct Plan
        {
            public Kind Kind;
            public int Order;
            public int PartitionOrder;
            public bool Rice2;
            public long Bits;
            public int Slot;
        }

        private sealed class Workspace
        {
            public readonly int[] Mid, Side;
            public readonly long[] Residual;
            /// <summary>Best Rice parameters per slot (one per candidate subframe in a frame).</summary>
            public readonly int[][] Params = new int[4][];
            public readonly int[] Scratch = new int[1 << MaxPartitionOrder];
            public Workspace(int n)
            {
                Mid = new int[n]; Side = new int[n]; Residual = new long[n];
                for (int i = 0; i < 4; i++) Params[i] = new int[1 << MaxPartitionOrder];
            }
        }

        private static Plan BestSubframe(int[] x, int n, int bps, Workspace ws, int slot)
        {
            bool constant = true;
            for (int i = 1; i < n && constant; i++) constant = x[i] == x[0];
            if (constant) return new Plan { Kind = Kind.Constant, Bits = 8 + bps, Slot = slot };

            var best = new Plan { Kind = Kind.Verbatim, Bits = 8 + (long)n * bps, Slot = slot };
            int maxOrder = Math.Min(4, n - 1);
            for (int order = 0; order <= maxOrder; order++)
            {
                ComputeResidual(x, n, order, ws.Residual);
                long bits = 8 + (long)order * bps + BestPartition(ws.Residual, n, order, ws.Scratch, out int po, out bool rice2);
                if (bits < best.Bits)
                {
                    best = new Plan { Kind = Kind.Fixed, Order = order, PartitionOrder = po, Rice2 = rice2, Bits = bits, Slot = slot };
                    Array.Copy(ws.Scratch, ws.Params[slot], 1 << po);
                }
            }
            return best;
        }

        private static void ComputeResidual(int[] x, int n, int order, long[] r)
        {
            switch (order)
            {
                case 0: for (int i = 0; i < n; i++) r[i] = x[i]; break;
                case 1: for (int i = 1; i < n; i++) r[i] = (long)x[i] - x[i - 1]; break;
                case 2: for (int i = 2; i < n; i++) r[i] = (long)x[i] - 2L * x[i - 1] + x[i - 2]; break;
                case 3: for (int i = 3; i < n; i++) r[i] = (long)x[i] - 3L * x[i - 1] + 3L * x[i - 2] - x[i - 3]; break;
                default: for (int i = 4; i < n; i++) r[i] = (long)x[i] - 4L * x[i - 1] + 6L * x[i - 2] - 4L * x[i - 3] + x[i - 4]; break;
            }
        }

        private static ulong Fold(long r) => r >= 0 ? (ulong)(r << 1) : (ulong)((-r << 1) - 1);

        /// <summary>Picks the partition order and per-partition Rice parameters; params land in <paramref name="outParams"/>.</summary>
        private static long BestPartition(long[] r, int n, int order, int[] outParams, out int bestPo, out bool bestRice2)
        {
            long bestBits = long.MaxValue;
            bestPo = 0; bestRice2 = false;
            var tmp = new int[1 << MaxPartitionOrder];
            for (int po = 0; po <= MaxPartitionOrder; po++)
            {
                int parts = 1 << po;
                if (n % parts != 0) break;
                int psize = n >> po;
                if (psize <= order) break;
                long bits = 2 + 4;
                int maxK = 0;
                for (int p = 0; p < parts; p++)
                {
                    int s0 = p == 0 ? order : p * psize, s1 = (p + 1) * psize;
                    ulong sum = 0;
                    for (int i = s0; i < s1; i++) sum += Fold(r[i]);
                    int count = s1 - s0;
                    int k = 0;
                    long kb = RiceCost(sum, count, 0);
                    for (int t = 1; t <= 30; t++)
                    {
                        long c = RiceCost(sum, count, t);
                        if (c < kb) { kb = c; k = t; } else break;
                    }
                    tmp[p] = k;
                    if (k > maxK) maxK = k;
                    bits += kb;
                }
                bool rice2 = maxK > 14;
                bits += parts * (rice2 ? 5 : 4);
                if (bits < bestBits)
                {
                    bestBits = bits; bestPo = po; bestRice2 = rice2;
                    Array.Copy(tmp, outParams, parts);
                }
            }
            return bestBits;
        }

        private static long RiceCost(ulong sum, int count, int k) => (long)count * (k + 1) + (long)(sum >> k);

        private static void WriteSubframe(BitWriter w, int[] x, int n, int bps, Plan p, Workspace ws)
        {
            w.Write(0, 1);
            switch (p.Kind)
            {
                case Kind.Constant:
                    w.Write(0, 6); w.Write(0, 1);
                    w.WriteSigned(x[0], bps);
                    return;
                case Kind.Verbatim:
                    w.Write(1, 6); w.Write(0, 1);
                    for (int i = 0; i < n; i++) w.WriteSigned(x[i], bps);
                    return;
            }
            w.Write((uint)(8 + p.Order), 6); w.Write(0, 1);
            for (int i = 0; i < p.Order; i++) w.WriteSigned(x[i], bps);
            ComputeResidual(x, n, p.Order, ws.Residual);
            var r = ws.Residual;
            w.Write(p.Rice2 ? 1u : 0u, 2);
            w.Write((uint)p.PartitionOrder, 4);
            int parts = 1 << p.PartitionOrder, psize = n >> p.PartitionOrder;
            var pars = ws.Params[p.Slot];
            for (int part = 0; part < parts; part++)
            {
                int k = pars[part];
                w.Write((uint)k, p.Rice2 ? 5 : 4);
                int s0 = part == 0 ? p.Order : part * psize, s1 = (part + 1) * psize;
                for (int i = s0; i < s1; i++)
                {
                    ulong u = Fold(r[i]);
                    w.WriteUnary(u >> k);
                    if (k > 0) w.Write((uint)(u & ((1UL << k) - 1)), k);
                }
            }
        }
    }

    /// <summary>
    /// FLAC decoder: STREAMINFO, CONSTANT/VERBATIM/FIXED/LPC subframes, wasted bits, Rice/Rice2 with escapes,
    /// all stereo decorrelation modes. Verifies frame CRC-8/CRC-16 and the STREAMINFO MD5.
    /// </summary>
    public static class FlacDecoder
    {
        public static AudioBuffer Decode(byte[] bytes) => Decode(bytes, out _);

        public static AudioBuffer Decode(string path, out FlacInfo info) => Decode(File.ReadAllBytes(path), out info);

        public static AudioBuffer Decode(byte[] data, out FlacInfo info)
        {
            try { return DecodeCore(data, out info); }
            catch (IndexOutOfRangeException ex) { throw new InvalidDataException("Corrupt FLAC stream.", ex); }
            catch (ArgumentException ex) { throw new InvalidDataException("Corrupt FLAC stream.", ex); }
            catch (OverflowException ex) { throw new InvalidDataException("Corrupt FLAC stream.", ex); }
        }

        private static AudioBuffer DecodeCore(byte[] data, out FlacInfo info)
        {
            var r = new BitReader(data);
            if (data.Length < 42 || r.Read(32) != 0x664C6143) throw new InvalidDataException("Missing fLaC marker.");
            info = null;
            bool last = false;
            while (!last)
            {
                last = r.Read(1) == 1;
                uint type = r.Read(7);
                int len = (int)r.Read(24);
                int next = r.BytePos + len;
                if (next > data.Length) throw new InvalidDataException("Truncated metadata block.");
                if (type == 0)
                {
                    info = new FlacInfo();
                    r.Read(16); r.Read(16); r.Read(24); r.Read(24);
                    info.SampleRate = (int)r.Read(20);
                    info.Channels = (int)r.Read(3) + 1;
                    info.BitsPerSample = (int)r.Read(5) + 1;
                    info.Frames = ((long)r.Read(4) << 32) | r.Read(32);
                    for (int i = 0; i < 16; i++) info.Md5[i] = (byte)r.Read(8);
                }
                r.SeekByte(next);
            }
            if (info == null) throw new InvalidDataException("Missing STREAMINFO.");
            if (info.Frames > int.MaxValue) throw new InvalidDataException("File too long.");
            int ch = info.Channels, total = (int)info.Frames;
            var pcm = new int[ch][];
            for (int c = 0; c < ch; c++) pcm[c] = new int[total];
            int pos = 0;
            var tmp = new int[ch][];

            while (pos < total && r.BytePos + 2 <= data.Length)
            {
                int frameStart = r.BytePos;
                if (r.Read(14) != 0x3FFE) throw new InvalidDataException($"Lost frame sync at byte {frameStart}.");
                r.Read(1); r.Read(1);
                uint bsCode = r.Read(4), srCode = r.Read(4), assign = r.Read(4), ssCode = r.Read(3);
                r.Read(1);
                ReadUtf8(r);
                int n;
                if (bsCode == 1) n = 192;
                else if (bsCode >= 2 && bsCode <= 5) n = 576 << (int)(bsCode - 2);
                else if (bsCode == 6) n = (int)r.Read(8) + 1;
                else if (bsCode == 7) n = (int)r.Read(16) + 1;
                else if (bsCode >= 8) n = 256 << (int)(bsCode - 8);
                else throw new InvalidDataException("Reserved block size.");
                if (srCode == 12) r.Read(8); else if (srCode == 13 || srCode == 14) r.Read(16);
                int hdrEnd = r.BytePos;
                uint crc8 = r.Read(8);
                if (crc8 != Crc.Crc8(data, frameStart, hdrEnd - frameStart)) throw new InvalidDataException("Frame header CRC mismatch.");
                int bps = ssCode == 0 ? info.BitsPerSample : ssCode == 1 ? 8 : ssCode == 2 ? 12 : ssCode == 4 ? 16 : ssCode == 5 ? 20 : ssCode == 6 ? 24 : ssCode == 7 ? 32 : -1;
                if (bps < 0) throw new InvalidDataException("Reserved sample size.");
                int fch = assign < 8 ? (int)assign + 1 : 2;
                if (fch != ch) throw new InvalidDataException("Channel count changed mid-stream.");
                for (int c = 0; c < ch; c++)
                {
                    if (tmp[c] == null || tmp[c].Length < n) tmp[c] = new int[n];
                    bool side = (assign == 8 && c == 1) || (assign == 9 && c == 0) || (assign == 10 && c == 1);
                    DecodeSubframe(r, tmp[c], n, bps + (side ? 1 : 0));
                }
                if (assign == 8) for (int i = 0; i < n; i++) tmp[1][i] = tmp[0][i] - tmp[1][i];
                else if (assign == 9) for (int i = 0; i < n; i++) tmp[0][i] += tmp[1][i];
                else if (assign == 10)
                    for (int i = 0; i < n; i++)
                    {
                        long side = tmp[1][i], mid = ((long)tmp[0][i] << 1) | (side & 1);
                        tmp[0][i] = (int)((mid + side) >> 1);
                        tmp[1][i] = (int)((mid - side) >> 1);
                    }
                r.AlignToByte();
                int crcEnd = r.BytePos;
                uint crc16 = r.Read(16);
                if (crc16 != Crc.Crc16(data, frameStart, crcEnd - frameStart)) throw new InvalidDataException("Frame CRC mismatch.");
                int take = Math.Min(n, total - pos);
                for (int c = 0; c < ch; c++) Array.Copy(tmp[c], 0, pcm[c], pos, take);
                pos += take;
            }
            if (pos < total) throw new InvalidDataException($"Truncated FLAC: {pos} of {total} frames.");

            bool md5Set = false;
            foreach (var b in info.Md5) if (b != 0) { md5Set = true; break; }
            if (md5Set)
            {
                var actual = FlacEncoder.Md5Of(pcm, total, info.BitsPerSample);
                info.Md5Matches = true;
                for (int i = 0; i < 16; i++) if (actual[i] != info.Md5[i]) { info.Md5Matches = false; break; }
            }
            else info.Md5Matches = true;

            var buf = new AudioBuffer(Math.Min(ch, 8), total, info.SampleRate);
            double scale = 1.0 / (1L << (info.BitsPerSample - 1));
            for (int c = 0; c < buf.Channels; c++)
            {
                var dst = buf.GetChannel(c); var src = pcm[c];
                for (int i = 0; i < total; i++) dst[i] = (float)(src[i] * scale);
            }
            return buf;
        }

        private static void ReadUtf8(BitReader r)
        {
            uint b = r.Read(8);
            int extra = 0;
            while ((b & (0x80u >> extra)) != 0 && extra < 7) extra++;
            for (int i = 1; i < extra; i++) r.Read(8);
        }

        private static void DecodeSubframe(BitReader r, int[] x, int n, int bps)
        {
            r.Read(1);
            uint type = r.Read(6);
            int wasted = 0;
            if (r.Read(1) == 1) wasted = (int)r.ReadUnary() + 1;
            bps -= wasted;
            if (type == 0)
            {
                int v = r.ReadSigned(bps);
                for (int i = 0; i < n; i++) x[i] = v;
            }
            else if (type == 1)
            {
                for (int i = 0; i < n; i++) x[i] = r.ReadSigned(bps);
            }
            else if (type >= 8 && type <= 12)
            {
                int order = (int)type - 8;
                for (int i = 0; i < order; i++) x[i] = r.ReadSigned(bps);
                ReadResidual(r, x, n, order);
                for (int i = order; i < n; i++)
                {
                    long p;
                    switch (order)
                    {
                        case 0: p = 0; break;
                        case 1: p = x[i - 1]; break;
                        case 2: p = 2L * x[i - 1] - x[i - 2]; break;
                        case 3: p = 3L * x[i - 1] - 3L * x[i - 2] + x[i - 3]; break;
                        default: p = 4L * x[i - 1] - 6L * x[i - 2] + 4L * x[i - 3] - x[i - 4]; break;
                    }
                    x[i] = (int)(p + x[i]);
                }
            }
            else if (type >= 32)
            {
                int order = (int)type - 31;
                for (int i = 0; i < order; i++) x[i] = r.ReadSigned(bps);
                int precision = (int)r.Read(4) + 1;
                if (precision == 16) throw new InvalidDataException("Invalid LPC precision.");
                int shift = r.ReadSigned(5);
                var coef = new int[order];
                for (int i = 0; i < order; i++) coef[i] = r.ReadSigned(precision);
                ReadResidual(r, x, n, order);
                for (int i = order; i < n; i++)
                {
                    long sum = 0;
                    for (int j = 0; j < order; j++) sum += (long)coef[j] * x[i - 1 - j];
                    x[i] = (int)((sum >> shift) + x[i]);
                }
            }
            else throw new InvalidDataException($"Reserved subframe type {type}.");
            if (wasted > 0) for (int i = 0; i < n; i++) x[i] <<= wasted;
        }

        private static void ReadResidual(BitReader r, int[] x, int n, int order)
        {
            uint method = r.Read(2);
            if (method > 1) throw new InvalidDataException("Reserved residual coding method.");
            int paramBits = method == 0 ? 4 : 5;
            uint escape = method == 0 ? 15u : 31u;
            int po = (int)r.Read(4);
            int parts = 1 << po, psize = n >> po;
            int idx = order;
            for (int p = 0; p < parts; p++)
            {
                int count = p == 0 ? psize - order : psize;
                uint k = r.Read(paramBits);
                if (k == escape)
                {
                    int raw = (int)r.Read(5);
                    for (int i = 0; i < count; i++) x[idx++] = raw == 0 ? 0 : r.ReadSigned(raw);
                }
                else
                {
                    for (int i = 0; i < count; i++)
                    {
                        ulong q = r.ReadUnary();
                        ulong u = (q << (int)k) | (k > 0 ? r.Read((int)k) : 0u);
                        x[idx++] = (int)((long)(u >> 1) ^ -(long)(u & 1));
                    }
                }
            }
        }
    }

    internal sealed class BitWriter
    {
        public byte[] Buffer;
        public int Length;
        private ulong _acc;
        private int _bits;

        public BitWriter(int capacity) { Buffer = new byte[Math.Max(64, capacity)]; }

        private void Put(byte b)
        {
            if (Length == Buffer.Length) Array.Resize(ref Buffer, Buffer.Length * 2);
            Buffer[Length++] = b;
        }

        public void Write(uint value, int bits)
        {
            if (bits == 0) return;
            ulong mask = bits == 32 ? 0xFFFFFFFFUL : (1UL << bits) - 1;
            _acc = (_acc << bits) | (value & mask);
            _bits += bits;
            while (_bits >= 8) { _bits -= 8; Put((byte)(_acc >> _bits)); }
        }

        public void WriteSigned(int value, int bits) => Write(unchecked((uint)value), bits);

        public void WriteUnary(ulong zeros)
        {
            while (zeros >= 32) { Write(0, 32); zeros -= 32; }
            Write(1, (int)zeros + 1);
        }

        public void AlignToByte() { if (_bits > 0) Write(0, 8 - _bits); }
    }

    internal sealed class BitReader
    {
        private readonly byte[] _d;
        private long _pos; // in bits

        public BitReader(byte[] d) { _d = d; }
        public int BytePos => (int)(_pos >> 3);
        public void SeekByte(int b) => _pos = (long)b << 3;
        public void AlignToByte() => _pos = (_pos + 7) & ~7L;

        public uint Read(int bits)
        {
            if (bits == 0) return 0;
            if (((_pos + bits + 7) >> 3) > _d.Length) throw new InvalidDataException("Unexpected end of FLAC data.");
            ulong v = 0;
            for (int i = 0; i < bits; i++)
            {
                int bit = (_d[_pos >> 3] >> (7 - (int)(_pos & 7))) & 1;
                v = (v << 1) | (uint)bit;
                _pos++;
            }
            return (uint)v;
        }

        public int ReadSigned(int bits)
        {
            if (bits == 0) return 0;
            uint v = Read(bits);
            int shift = 32 - bits;
            return shift == 0 ? (int)v : ((int)(v << shift)) >> shift;
        }

        public ulong ReadUnary()
        {
            ulong n = 0;
            while (true)
            {
                if ((_pos >> 3) >= _d.Length) throw new InvalidDataException("Unexpected end of FLAC data.");
                // Fast path: skip whole zero bytes.
                if ((_pos & 7) == 0 && _d[_pos >> 3] == 0) { n += 8; _pos += 8; continue; }
                int bit = (_d[_pos >> 3] >> (7 - (int)(_pos & 7))) & 1;
                _pos++;
                if (bit == 1) return n;
                n++;
            }
        }
    }

    internal static class Crc
    {
        private static readonly byte[] T8 = new byte[256];
        private static readonly ushort[] T16 = new ushort[256];

        static Crc()
        {
            for (int i = 0; i < 256; i++)
            {
                int c = i;
                for (int b = 0; b < 8; b++) c = (c & 0x80) != 0 ? ((c << 1) ^ 0x07) : (c << 1);
                T8[i] = (byte)c;
                int d = i << 8;
                for (int b = 0; b < 8; b++) d = (d & 0x8000) != 0 ? ((d << 1) ^ 0x8005) : (d << 1);
                T16[i] = (ushort)d;
            }
        }

        public static uint Crc8(byte[] d, int off, int len)
        {
            byte c = 0;
            for (int i = 0; i < len; i++) c = T8[c ^ d[off + i]];
            return c;
        }

        public static uint Crc16(byte[] d, int off, int len)
        {
            ushort c = 0;
            for (int i = 0; i < len; i++) c = (ushort)((c << 8) ^ T16[(c >> 8) ^ d[off + i]]);
            return c;
        }
    }
}
