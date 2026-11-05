using System;
using System.IO;
using System.Threading;
using AudioCreationStudio.DSP;
using AudioCreationStudio.Models;

namespace AudioCreationStudio.Export
{
    public sealed class ExportOptions
    {
        public WavSampleFormat Format = WavSampleFormat.Pcm16;
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
            Format = s.format, SampleRate = s.sampleRate, Channels = s.channels,
            Normalize = s.normalize, NormalizeTargetDb = s.normalizeTargetDb,
            TrimSilence = s.trimSilence && !loop, TrimThresholdDb = s.trimThresholdDb,
            Dither = s.dither && s.format == WavSampleFormat.Pcm16, WriteLoopMetadata = loop
        };
    }

    public sealed class ExportResult
    {
        public string Path;
        public long Frames;
        public int SampleRate;
        public int Channels;
        public WavSampleFormat Format;
        public float PeakDb;
        public float AppliedGainDb;
        public int ClippedSamples;
        public bool Validated;
        public long FileBytes;
        public double DurationSeconds => SampleRate > 0 ? (double)Frames / SampleRate : 0;
    }

    /// <summary>
    /// Export: channel conversion → sample-rate conversion → silence trim → normalization → WAV encode.
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
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    WavEncoder.Write(fs, buf, o.Format, o.Dither, o.DitherSeed, loop, ct, progress == null ? null : new SyncProgress(progress));
                }
                ct.ThrowIfCancellationRequested();

                WavInfo info;
                using (var fs = File.OpenRead(tmp)) info = WavDecoder.ReadInfo(fs);
                bool valid = info.Frames == buf.Frames && info.Channels == buf.Channels && info.SampleRate == buf.SampleRate &&
                             info.BitsPerSample == WavEncoder.BytesPerSample(o.Format) * 8;
                if (!valid) throw new IOException("Exported WAV failed validation (header/sample count mismatch).");

                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
                progress?.Report(1f);
                return new ExportResult
                {
                    Path = path, Frames = buf.Frames, SampleRate = buf.SampleRate, Channels = buf.Channels, Format = o.Format,
                    PeakDb = DspMath.GainToDb(buf.GetPeak()), AppliedGainDb = DspMath.GainToDb(gain),
                    ClippedSamples = o.Format == WavSampleFormat.Float32 ? 0 : MixRenderer.CountClipped(buf),
                    Validated = true, FileBytes = new FileInfo(path).Length
                };
            }
            finally
            {
                if (File.Exists(tmp)) { try { File.Delete(tmp); } catch { /* best effort */ } }
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
