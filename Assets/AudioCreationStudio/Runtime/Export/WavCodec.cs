using System;
using System.IO;
using System.Text;
using System.Threading;
using AudioCreationStudio.DSP;
using AudioCreationStudio.Models;

namespace AudioCreationStudio.Export
{
    public struct WavLoopPoints
    {
        public uint Start;
        /// <summary>Exclusive end frame. Written to the 'smpl' chunk as the inclusive last frame (End - 1).</summary>
        public uint End;
    }

    public sealed class WavInfo
    {
        public int FormatTag;
        public int Channels;
        public int SampleRate;
        public int BitsPerSample;
        public long Frames;
        public long DataOffset;
        public long DataBytes;
        public WavLoopPoints? Loop;
    }

    /// <summary>
    /// RIFF/WAVE encoder: PCM 16-bit, PCM 24-bit, IEEE float 32-bit, little-endian, interleaved.
    /// PCM scaling: sample = clamp(round(x · 2^(bits-1)), −2^(bits-1), 2^(bits-1) − 1), i.e. +1.0 maps to full scale −1 LSB.
    /// </summary>
    public static class WavEncoder
    {
        public static int BytesPerSample(WavSampleFormat f) => f == WavSampleFormat.Pcm16 ? 2 : f == WavSampleFormat.Pcm24 ? 3 : 4;

        public static void Write(Stream stream, AudioBuffer buffer, WavSampleFormat format, bool dither = false, int ditherSeed = 1,
            WavLoopPoints? loop = null, CancellationToken ct = default, IProgress<float> progress = null)
        {
            int ch = buffer.Channels;
            int bps = BytesPerSample(format);
            int bits = bps * 8;
            long frames = buffer.Frames;
            long dataBytes = frames * ch * bps;
            if (dataBytes > uint.MaxValue - 1024) throw new InvalidOperationException("Audio too long for a RIFF/WAVE file.");
            bool isFloat = format == WavSampleFormat.Float32;
            int fmtSize = isFloat ? 18 : 16;
            int smplSize = loop.HasValue ? 36 + 24 : 0;
            int factSize = isFloat ? 4 : 0;

            using (var w = new BinaryWriter(stream, Encoding.ASCII, true))
            {
                long riffSize = 4 + (8 + fmtSize) + (isFloat ? 8 + factSize : 0) + (loop.HasValue ? 8 + smplSize : 0) + 8 + dataBytes + (dataBytes & 1);
                w.Write(Encoding.ASCII.GetBytes("RIFF"));
                w.Write((uint)riffSize);
                w.Write(Encoding.ASCII.GetBytes("WAVE"));

                w.Write(Encoding.ASCII.GetBytes("fmt "));
                w.Write(fmtSize);
                w.Write((ushort)(isFloat ? 3 : 1));
                w.Write((ushort)ch);
                w.Write(buffer.SampleRate);
                w.Write(buffer.SampleRate * ch * bps);
                w.Write((ushort)(ch * bps));
                w.Write((ushort)bits);
                if (isFloat) w.Write((ushort)0);

                if (isFloat)
                {
                    w.Write(Encoding.ASCII.GetBytes("fact"));
                    w.Write(4);
                    w.Write((uint)frames);
                }

                if (loop.HasValue)
                {
                    var lp = loop.Value;
                    w.Write(Encoding.ASCII.GetBytes("smpl"));
                    w.Write(smplSize);
                    w.Write(0); // manufacturer
                    w.Write(0); // product
                    w.Write((uint)(1000000000.0 / buffer.SampleRate)); // sample period (ns)
                    w.Write(60); // MIDI unity note
                    w.Write(0); // pitch fraction
                    w.Write(0); // SMPTE format
                    w.Write(0); // SMPTE offset
                    w.Write(1); // loop count
                    w.Write(0); // sampler data
                    w.Write(0); // cue id
                    w.Write(0); // type: forward
                    w.Write(lp.Start);
                    w.Write(lp.End > 0 ? lp.End - 1 : 0);
                    w.Write(0); // fraction
                    w.Write(0); // play count: infinite
                }

                w.Write(Encoding.ASCII.GetBytes("data"));
                w.Write((uint)dataBytes);

                var rng = dither ? new DeterministicRandom(ditherSeed) : null;
                double scale = format == WavSampleFormat.Pcm16 ? 32768.0 : 8388608.0;
                long maxV = format == WavSampleFormat.Pcm16 ? 32767 : 8388607;
                long minV = -maxV - 1;
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
                            float x = DspMath.Sanitize(buffer.GetChannel(c)[f0 + i]);
                            if (isFloat)
                            {
                                int bitsF = BitConverter.SingleToInt32Bits(x);
                                bytes[bi++] = (byte)bitsF; bytes[bi++] = (byte)(bitsF >> 8);
                                bytes[bi++] = (byte)(bitsF >> 16); bytes[bi++] = (byte)(bitsF >> 24);
                                continue;
                            }
                            double v = x * scale;
                            if (rng != null) v += rng.NextDouble() - rng.NextDouble(); // TPDF ±1 LSB
                            long s = (long)Math.Round(v, MidpointRounding.AwayFromZero);
                            if (s > maxV) s = maxV; else if (s < minV) s = minV;
                            bytes[bi++] = (byte)s;
                            bytes[bi++] = (byte)(s >> 8);
                            if (bps == 3) bytes[bi++] = (byte)(s >> 16);
                        }
                    }
                    w.Write(bytes, 0, bi);
                    progress?.Report((float)(f0 + n) / Math.Max(1, frames));
                }
                if ((dataBytes & 1) == 1) w.Write((byte)0);
            }
        }

        public static byte[] Encode(AudioBuffer buffer, WavSampleFormat format, bool dither = false, WavLoopPoints? loop = null)
        {
            using (var ms = new MemoryStream())
            {
                Write(ms, buffer, format, dither, 1, loop);
                return ms.ToArray();
            }
        }
    }

    /// <summary>RIFF/WAVE decoder: PCM 8/16/24/32, IEEE float 32/64, WAVE_FORMAT_EXTENSIBLE; reads 'smpl' loops.</summary>
    public static class WavDecoder
    {
        public static WavInfo ReadInfo(Stream s)
        {
            using (var r = new BinaryReader(s, Encoding.ASCII, true))
            {
                if (s.Length < 12) throw new InvalidDataException("File too small for RIFF/WAVE.");
                if (new string(r.ReadChars(4)) != "RIFF") throw new InvalidDataException("Missing RIFF header.");
                r.ReadUInt32();
                if (new string(r.ReadChars(4)) != "WAVE") throw new InvalidDataException("Missing WAVE id.");
                var info = new WavInfo();
                bool haveFmt = false, haveData = false;
                while (s.Position + 8 <= s.Length)
                {
                    string id = new string(r.ReadChars(4));
                    uint size = r.ReadUInt32();
                    long next = s.Position + size + (size & 1);
                    if (id == "fmt ")
                    {
                        info.FormatTag = r.ReadUInt16();
                        info.Channels = r.ReadUInt16();
                        info.SampleRate = r.ReadInt32();
                        r.ReadInt32(); r.ReadUInt16();
                        info.BitsPerSample = r.ReadUInt16();
                        if (info.FormatTag == 0xFFFE && size >= 40)
                        {
                            r.ReadUInt16(); r.ReadUInt16(); r.ReadUInt32();
                            info.FormatTag = r.ReadUInt16(); // first two bytes of the sub-format GUID
                        }
                        haveFmt = true;
                    }
                    else if (id == "data")
                    {
                        info.DataOffset = s.Position;
                        info.DataBytes = Math.Min(size, s.Length - s.Position);
                        haveData = true;
                    }
                    else if (id == "smpl" && size >= 36 + 24)
                    {
                        r.ReadBytes(28);
                        uint loops = r.ReadUInt32();
                        r.ReadUInt32();
                        if (loops > 0)
                        {
                            r.ReadUInt32(); r.ReadUInt32();
                            uint start = r.ReadUInt32(), end = r.ReadUInt32();
                            info.Loop = new WavLoopPoints { Start = start, End = end + 1 };
                        }
                    }
                    if (next > s.Length) break;
                    s.Position = next;
                }
                if (!haveFmt || !haveData) throw new InvalidDataException("Missing fmt or data chunk.");
                int bytesPer = info.BitsPerSample / 8;
                if (info.Channels <= 0 || bytesPer <= 0) throw new InvalidDataException("Invalid format.");
                info.Frames = info.DataBytes / (bytesPer * info.Channels);
                return info;
            }
        }

        public static AudioBuffer Decode(byte[] bytes) { using (var ms = new MemoryStream(bytes)) return Decode(ms, out _); }

        public static AudioBuffer Decode(string path, out WavInfo info)
        {
            using (var fs = File.OpenRead(path)) return Decode(fs, out info);
        }

        public static AudioBuffer Decode(Stream s, out WavInfo info)
        {
            info = ReadInfo(s);
            int ch = info.Channels;
            int bytesPer = info.BitsPerSample / 8;
            if (info.Frames > int.MaxValue) throw new InvalidDataException("File too long.");
            var buf = new AudioBuffer(Math.Min(ch, 8), (int)info.Frames, info.SampleRate);
            s.Position = info.DataOffset;
            var data = new byte[info.Frames * ch * bytesPer];
            int read = 0;
            while (read < data.Length)
            {
                int n = s.Read(data, read, data.Length - read);
                if (n <= 0) break;
                read += n;
            }
            bool isFloat = info.FormatTag == 3;
            if (!isFloat && info.FormatTag != 1) throw new InvalidDataException($"Unsupported WAV format tag {info.FormatTag}.");
            int idx = 0;
            for (int f = 0; f < info.Frames; f++)
            {
                for (int c = 0; c < ch; c++)
                {
                    float v;
                    if (isFloat && bytesPer == 4) v = BitConverter.ToSingle(data, idx);
                    else if (isFloat && bytesPer == 8) v = (float)BitConverter.ToDouble(data, idx);
                    else switch (bytesPer)
                    {
                        case 1: v = (data[idx] - 128) / 128f; break;
                        case 2: v = (short)(data[idx] | (data[idx + 1] << 8)) / 32768f; break;
                        case 3:
                        {
                            int x = data[idx] | (data[idx + 1] << 8) | (data[idx + 2] << 16);
                            if ((x & 0x800000) != 0) x |= unchecked((int)0xFF000000);
                            v = x / 8388608f;
                            break;
                        }
                        case 4: v = BitConverter.ToInt32(data, idx) / 2147483648f; break;
                        default: throw new InvalidDataException("Unsupported bit depth.");
                    }
                    if (c < buf.Channels) buf.GetChannel(c)[f] = v;
                    idx += bytesPer;
                }
            }
            return buf;
        }
    }
}
