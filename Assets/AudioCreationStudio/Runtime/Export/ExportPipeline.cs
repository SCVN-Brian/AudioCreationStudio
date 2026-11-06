using System;
using System.IO;
using System.Threading;
using AudioCreationStudio.DSP;
using AudioCreationStudio.Models;

namespace AudioCreationStudio.Export
{
    public static class AudioFormats
    {
        public static string Extension(AudioFileType t)
        {
            switch (t)
            {
                case AudioFileType.Aiff: return ".aiff";
                case AudioFileType.Flac: return ".flac";
                case AudioFileType.Mp3: return ".mp3";
                case AudioFileType.Ogg: return ".ogg";
                default: return ".wav";
            }
        }

        /// <summary>Lossy formats are encoded by ffmpeg and have no bit-depth/dither setting.</summary>
        public static bool IsLossy(AudioFileType t) => t == AudioFileType.Mp3 || t == AudioFileType.Ogg;

        /// <summary>Only WAV carries a 'smpl' loop chunk.</summary>
        public static bool SupportsLoopMetadata(AudioFileType t) => t == AudioFileType.Wav;

        public static string Label(AudioFileType t) => t == AudioFileType.Ogg ? "OGG Vorbis" : t.ToString().ToUpperInvariant();
    }

    public sealed class ExportOptions
    {
        public AudioFileType FileType = AudioFileType.Wav;
        public WavSampleFormat Format = WavSampleFormat.Pcm16;
        /// <summary>MP3/OGG bitrate in kbit/s.</summary>
        public int BitrateKbps = 192;
        /// <summary>Explicit ffmpeg executable; null searches PATH and common install folders.</summary>
        public string FfmpegPath;
        /// <summary>0 keeps the source rate.</summary>
        public int SampleRate;
        /// <summary>0 keeps the source channel count.</summary>
        public int Channels;
        public bool Normalize;
        public float NormalizeTargetDb = -1f;
        public bool TrimSilence;
        public float TrimThresholdDb = -60f;
        public bool Dither;
        public int DitherSeed = 1;
        /// <summary>Writes a 'smpl' loop chunk covering the whole file.</summary>
        public bool WriteLoopMetadata;

        public static ExportOptions From(ExportSettings s, bool loop = false) => new ExportOptions
        {
            FileType = s.fileType, Format = s.format, BitrateKbps = s.lossyBitrateKbps > 0 ? s.lossyBitrateKbps : 192,
            SampleRate = s.sampleRate, Channels = s.channels,
            Normalize = s.normalize, NormalizeTargetDb = s.normalizeTargetDb,
            TrimSilence = s.trimSilence && !loop, TrimThresholdDb = s.trimThresholdDb,
            Dither = s.dither && s.format == WavSampleFormat.Pcm16 && !AudioFormats.IsLossy(s.fileType),
            WriteLoopMetadata = loop && AudioFormats.SupportsLoopMetadata(s.fileType)
        };
    }

    public sealed class ExportResult
    {
        public string Path;
        public AudioFileType FileType;
        public long Frames;
        public int SampleRate;
        public int Channels;
        public WavSampleFormat Format;
        public float PeakDb;
        public float AppliedGainDb;
        public int ClippedSamples;
        public bool Validated;
        public long FileBytes;
        /// <summary>Format adjustments made during encoding (e.g. FLAC float → 24-bit, MP3 resampled to 48 kHz).</summary>
        public string Note = "";
        public string FormatLabel => AudioFormats.IsLossy(FileType) ? AudioFormats.Label(FileType) : $"{AudioFormats.Label(FileType)} {Format}";
        public double DurationSeconds => SampleRate > 0 ? (double)Frames / SampleRate : 0;
    }

    /// <summary>
    /// Export: channel conversion → sample-rate conversion → silence trim → normalization → encode (WAV/AIFF/FLAC in C#, MP3/OGG via ffmpeg).
    /// Files are written to a temporary sibling and atomically moved into place only after the header
    /// and sample count are validated, so cancellation or errors never leave a corrupted file behind.
    /// </summary>
    public static class ExportPipeline
    {
        public static AudioBuffer Prepare(AudioBuffer mix, ExportOptions o, out float appliedGain, CancellationToken ct = default)
        {
            appliedGain = 1f;
            var buf = o.Channels > 0 && o.Channels != mix.Channels ? ChannelConverter.Convert(mix, o.Channels) : mix.Clone();
            ct.ThrowIfCancellationRequested();
            if (o.SampleRate > 0 && o.SampleRate != buf.SampleRate) buf = Resampler.Resample(buf, o.SampleRate, null, ct);
            ct.ThrowIfCancellationRequested();
            if (o.TrimSilence && SignalAnalysis.FindSignalBounds(buf, o.TrimThresholdDb, out int first, out int last))
                buf = buf.Slice(first, last - first);
            if (o.Normalize)
            {
                appliedGain = SignalAnalysis.NormalizationGain(buf, o.NormalizeTargetDb);
                buf.Scale(appliedGain);
            }
            buf.Sanitize();
            return buf;
        }

        public static ExportResult ExportToFile(AudioBuffer mix, ExportOptions o, string path,
            CancellationToken ct = default, IProgress<float> progress = null)
        {
            if (mix == null) throw new ArgumentNullException(nameof(mix));
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("Output path required", nameof(path));
            var dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var buf = Prepare(mix, o, out float gain, ct);
            progress?.Report(0.2f);
            WavLoopPoints? loop = o.WriteLoopMetadata ? new WavLoopPoints { Start = 0, End = (uint)buf.Frames } : (WavLoopPoints?)null;

            // Ending in '~' makes Unity ignore the temp file if it is inside Assets.
            string tmp = path + ".partial~";
            try
            {
                var sub = progress == null ? null : new SyncProgress(progress);
                string note = Encode(buf, o, loop, tmp, ct, sub);
                ct.ThrowIfCancellationRequested();

                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
                progress?.Report(1f);
                return new ExportResult
                {
                    Path = path, FileType = o.FileType, Frames = buf.Frames, SampleRate = buf.SampleRate, Channels = buf.Channels, Format = o.Format,
                    PeakDb = DspMath.GainToDb(buf.GetPeak()), AppliedGainDb = DspMath.GainToDb(gain),
                    ClippedSamples = o.Format == WavSampleFormat.Float32 && (o.FileType == AudioFileType.Wav || o.FileType == AudioFileType.Aiff) ? 0 : MixRenderer.CountClipped(buf),
                    Validated = true, FileBytes = new FileInfo(path).Length, Note = note
                };
            }
            finally
            {
                if (File.Exists(tmp)) { try { File.Delete(tmp); } catch { /* best effort */ } }
            }
        }

        /// <summary>Writes <paramref name="buf"/> to <paramref name="tmp"/> in the requested container and validates it. Returns a note.</summary>
        private static string Encode(AudioBuffer buf, ExportOptions o, WavLoopPoints? loop, string tmp, CancellationToken ct, IProgress<float> progress)
        {
            switch (o.FileType)
            {
                case AudioFileType.Aiff:
                {
                    using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                        AiffEncoder.Write(fs, buf, o.Format, o.Dither, o.DitherSeed, ct, progress);
                    AiffInfo info;
                    using (var fs = File.OpenRead(tmp)) info = AiffDecoder.ReadInfo(fs);
                    int bits = o.Format == WavSampleFormat.Float32 ? 32 : WavEncoder.BytesPerSample(o.Format) * 8;
                    if (info.Frames != buf.Frames || info.Channels != buf.Channels || info.SampleRate != buf.SampleRate || info.BitsPerSample != bits)
                        throw new IOException("Exported AIFF failed validation (header/sample count mismatch).");
                    return "";
                }
                case AudioFileType.Flac:
                {
                    using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                        FlacEncoder.Write(fs, buf, o.Format, o.Dither, o.DitherSeed, ct, progress);
                    FlacDecoder.Decode(tmp, out var info); // verifies frame CRCs
                    if (info.Frames != buf.Frames || info.Channels != buf.Channels || info.SampleRate != buf.SampleRate ||
                        info.BitsPerSample != PcmQuantizer.BitsFor(o.Format) || !info.Md5Matches)
                        throw new IOException("Exported FLAC failed validation (header/MD5 mismatch).");
                    return o.Format == WavSampleFormat.Float32 ? "FLAC has no float mode: stored as 24-bit" : "";
                }
                case AudioFileType.Mp3:
                case AudioFileType.Ogg:
                    return FfmpegEncoder.Encode(buf, o.FileType, o.BitrateKbps, o.FfmpegPath, tmp, ct, progress);
                default:
                {
                    using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                        WavEncoder.Write(fs, buf, o.Format, o.Dither, o.DitherSeed, loop, ct, progress);
                    WavInfo info;
                    using (var fs = File.OpenRead(tmp)) info = WavDecoder.ReadInfo(fs);
                    if (info.Frames != buf.Frames || info.Channels != buf.Channels || info.SampleRate != buf.SampleRate ||
                        info.BitsPerSample != WavEncoder.BytesPerSample(o.Format) * 8)
                        throw new IOException("Exported WAV failed validation (header/sample count mismatch).");
                    return "";
                }
            }
        }

        /// <summary>Synchronous progress forwarding (Progress&lt;T&gt; posts asynchronously, which is not wanted here).</summary>
        private sealed class SyncProgress : IProgress<float>
        {
            private readonly IProgress<float> _inner;
            public SyncProgress(IProgress<float> inner) { _inner = inner; }
            public void Report(float value) => _inner.Report(0.2f + value * 0.75f);
        }
    }
}
