using System;
using System.IO;
using System.Text;
using System.Threading;
using AudioCreationStudio.DSP;
using AudioCreationStudio.Export;
using AudioCreationStudio.Models;
using NUnit.Framework;
using static AudioCreationStudio.Tests.TestUtil;

namespace AudioCreationStudio.Tests
{
    /// <summary>AIFF, FLAC (pure C#) and MP3/OGG (ffmpeg — skipped when ffmpeg is not installed).</summary>
    public class ExportFormatsTests
    {
        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "acs_fmt_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        private static AudioBuffer Noisy(int channels, int frames, int seed = 7)
        {
            var b = Sine(330, frames, 0.6f, SR, channels);
            var rng = new DeterministicRandom(seed);
            for (int c = 0; c < channels; c++)
            {
                var d = b.GetChannel(c);
                for (int i = 0; i < frames; i++) d[i] += (float)(0.05 * (rng.NextDouble() * 2 - 1)) * (c + 1);
            }
            return b;
        }

        private static void AssertSamplesEqual(AudioBuffer a, AudioBuffer b)
        {
            Assert.AreEqual(a.Channels, b.Channels, "channels");
            Assert.AreEqual(a.Frames, b.Frames, "frames");
            for (int c = 0; c < a.Channels; c++)
                for (int i = 0; i < a.Frames; i++)
                    if (a.GetChannel(c)[i] != b.GetChannel(c)[i]) Assert.Fail($"sample mismatch ch{c} @{i}: {a.GetChannel(c)[i]} vs {b.GetChannel(c)[i]}");
        }

        // ------------------------------------------------------------------ FLAC

        [TestCase(1, 1, WavSampleFormat.Pcm16)]
        [TestCase(1, 4096, WavSampleFormat.Pcm16)]
        [TestCase(2, 4097, WavSampleFormat.Pcm16)]
        [TestCase(2, 30000, WavSampleFormat.Pcm24)]
        [TestCase(3, 9000, WavSampleFormat.Pcm24)]
        public void Flac_IsBitExactWithWav(int channels, int frames, WavSampleFormat fmt)
        {
            var b = Noisy(channels, frames);
            var flac = FlacEncoder.Encode(b, fmt);
            var back = FlacDecoder.Decode(flac, out var info);
            var wav = WavDecoder.Decode(WavEncoder.Encode(b, fmt));
            AssertSamplesEqual(wav, back);
            Assert.IsTrue(info.Md5Matches, "STREAMINFO MD5");
            Assert.AreEqual(fmt == WavSampleFormat.Pcm16 ? 16 : 24, info.BitsPerSample);
            Assert.AreEqual("fLaC", Encoding.ASCII.GetString(flac, 0, 4));
        }

        [Test]
        public void Flac_CompressesTonalAudio_AndSilence()
        {
            var b = Sine(440, SR, 0.5f, SR, 2);
            var flac = FlacEncoder.Encode(b, WavSampleFormat.Pcm16);
            Assert.Less(flac.Length, SR * 2 * 2 * 0.7, "a pure tone should compress well below WAV size");
            var silent = FlacEncoder.Encode(new AudioBuffer(2, SR, SR), WavSampleFormat.Pcm24);
            Assert.Less(silent.Length, 1024, "silence is CONSTANT subframes");
        }

        [Test]
        public void Flac_DitherMatchesWavDither()
        {
            var b = Noisy(2, 10000);
            AssertSamplesEqual(WavDecoder.Decode(WavEncoder.Encode(b, WavSampleFormat.Pcm16, true)),
                               FlacDecoder.Decode(FlacEncoder.Encode(b, WavSampleFormat.Pcm16, true)));
        }

        [Test]
        public void Flac_CorruptionIsDetected()
        {
            var flac = FlacEncoder.Encode(Noisy(2, 10000), WavSampleFormat.Pcm16);
            flac[flac.Length / 2] ^= 0x10;
            Assert.Throws<InvalidDataException>(() => FlacDecoder.Decode(flac));
        }

        // ------------------------------------------------------------------ AIFF

        [TestCase(WavSampleFormat.Pcm16)]
        [TestCase(WavSampleFormat.Pcm24)]
        [TestCase(WavSampleFormat.Float32)]
        public void Aiff_RoundTripsLikeWav(WavSampleFormat fmt)
        {
            var b = Noisy(2, 5000);
            var bytes = AiffEncoder.Encode(b, fmt);
            Assert.AreEqual("FORM", Encoding.ASCII.GetString(bytes, 0, 4));
            Assert.AreEqual(fmt == WavSampleFormat.Float32 ? "AIFC" : "AIFF", Encoding.ASCII.GetString(bytes, 8, 4));
            AssertSamplesEqual(WavDecoder.Decode(WavEncoder.Encode(b, fmt)), AiffDecoder.Decode(bytes));
        }

        [Test]
        public void Aiff_HeaderReportsRateAndFrames()
        {
            var bytes = AiffEncoder.Encode(Sine(440, 12345, 0.5f, 44100, 1), WavSampleFormat.Pcm24);
            using (var ms = new MemoryStream(bytes))
            {
                var info = AiffDecoder.ReadInfo(ms);
                Assert.AreEqual(44100, info.SampleRate);
                Assert.AreEqual(12345, info.Frames);
                Assert.AreEqual(24, info.BitsPerSample);
                Assert.AreEqual(1, info.Channels);
            }
        }

        // ------------------------------------------------------------------ pipeline

        [TestCase(AudioFileType.Wav, ".wav")]
        [TestCase(AudioFileType.Aiff, ".aiff")]
        [TestCase(AudioFileType.Flac, ".flac")]
        public void Pipeline_WritesValidatedLosslessFile(AudioFileType type, string ext)
        {
            Assert.AreEqual(ext, AudioFormats.Extension(type));
            string path = Path.Combine(_dir, "out" + ext);
            var r = ExportPipeline.ExportToFile(Noisy(2, 24000), new ExportOptions { FileType = type, Format = WavSampleFormat.Pcm24 }, path);
            Assert.IsTrue(r.Validated);
            Assert.AreEqual(type, r.FileType);
            Assert.AreEqual(24000, r.Frames);
            Assert.IsTrue(File.Exists(path));
            Assert.IsFalse(File.Exists(path + ".partial~"));
        }

        [Test]
        public void Pipeline_FlacFloatIsStoredAs24Bit()
        {
            string path = Path.Combine(_dir, "f.flac");
            var r = ExportPipeline.ExportToFile(Noisy(1, 5000), new ExportOptions { FileType = AudioFileType.Flac, Format = WavSampleFormat.Float32 }, path);
            FlacDecoder.Decode(path, out var info);
            Assert.AreEqual(24, info.BitsPerSample);
            StringAssert.Contains("24-bit", r.Note);
        }

        [Test]
        public void Pipeline_CancelledFlacLeavesNoFile()
        {
            var cts = new CancellationTokenSource();
            cts.Cancel();
            string path = Path.Combine(_dir, "c.flac");
            Assert.Throws<OperationCanceledException>(() =>
                ExportPipeline.ExportToFile(Noisy(2, 50000), new ExportOptions { FileType = AudioFileType.Flac }, path, cts.Token));
            Assert.IsFalse(File.Exists(path));
            Assert.IsFalse(File.Exists(path + ".partial~"));
        }

        [Test]
        public void LoopMetadataOnlyForWav()
        {
            var s = new ExportSettings { fileType = AudioFileType.Ogg };
            Assert.IsFalse(ExportOptions.From(s, true).WriteLoopMetadata);
            s.fileType = AudioFileType.Wav;
            Assert.IsTrue(ExportOptions.From(s, true).WriteLoopMetadata);
        }

        [TestCase(AudioFileType.Mp3, 1, 44100)]
        [TestCase(AudioFileType.Mp3, 2, 96000)]
        [TestCase(AudioFileType.Ogg, 1, 48000)]
        [TestCase(AudioFileType.Ogg, 2, 44100)]
        public void Pipeline_LossyViaFfmpeg(AudioFileType type, int channels, int rate)
        {
            if (FfmpegEncoder.Locate() == null) Assert.Ignore("ffmpeg not installed");
            string path = Path.Combine(_dir, "l" + AudioFormats.Extension(type));
            var src = Sine(440, rate / 2, 0.5f, rate, channels);
            var r = ExportPipeline.ExportToFile(src, new ExportOptions { FileType = type, BitrateKbps = 128 }, path);
            Assert.IsTrue(r.Validated);
            Assert.IsTrue(FfmpegEncoder.HasMagic(path, type));
            Assert.Greater(r.FileBytes, 500);
            Assert.IsFalse(File.Exists(path + ".partial~"));
            Assert.IsFalse(File.Exists(path + ".partial~.src.wav~"));
            if (type == AudioFileType.Mp3 && rate > 48000) StringAssert.Contains("48000", r.Note);
        }

        [Test]
        public void Lossy_WithoutFfmpegFailsClearly()
        {
            var o = new ExportOptions { FileType = AudioFileType.Mp3, FfmpegPath = Path.Combine(_dir, "missing", "ffmpeg") };
            if (FfmpegEncoder.Locate(o.FfmpegPath) != null) Assert.Ignore("ffmpeg is installed; the fallback search finds it");
            Assert.Throws<FileNotFoundException>(() => ExportPipeline.ExportToFile(Sine(440, 1000), o, Path.Combine(_dir, "x.mp3")));
        }
    }
}
