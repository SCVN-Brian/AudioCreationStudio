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
    public class WavExportTests
    {
        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "acs_tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        [Test]
        public void Pcm16_HeaderIsValidRiffWave()
        {
            var b = Sine(440, 1000, 0.5f, 44100, 2);
            var bytes = WavEncoder.Encode(b, WavSampleFormat.Pcm16);
            Assert.AreEqual("RIFF", Encoding.ASCII.GetString(bytes, 0, 4));
            Assert.AreEqual(bytes.Length - 8, BitConverter.ToInt32(bytes, 4), "RIFF size");
            Assert.AreEqual("WAVE", Encoding.ASCII.GetString(bytes, 8, 4));
            Assert.AreEqual("fmt ", Encoding.ASCII.GetString(bytes, 12, 4));
            Assert.AreEqual(16, BitConverter.ToInt32(bytes, 16));
            Assert.AreEqual(1, BitConverter.ToInt16(bytes, 20), "PCM");
            Assert.AreEqual(2, BitConverter.ToInt16(bytes, 22), "channels");
            Assert.AreEqual(44100, BitConverter.ToInt32(bytes, 24), "rate");
            Assert.AreEqual(44100 * 4, BitConverter.ToInt32(bytes, 28), "byte rate");
            Assert.AreEqual(4, BitConverter.ToInt16(bytes, 32), "block align");
            Assert.AreEqual(16, BitConverter.ToInt16(bytes, 34), "bits");
            Assert.AreEqual("data", Encoding.ASCII.GetString(bytes, 36, 4));
            Assert.AreEqual(1000 * 4, BitConverter.ToInt32(bytes, 40), "data size = frames × channels × 2");
            Assert.AreEqual(44 + 4000, bytes.Length);
        }

        [Test]
        public void Pcm16_ScalingByteOrderAndClamping()
        {
            var b = new AudioBuffer(1, 5, SR);
            b[0, 0] = 0.5f; b[0, 1] = -0.5f; b[0, 2] = 1f; b[0, 3] = -1f; b[0, 4] = 2f;
            var bytes = WavEncoder.Encode(b, WavSampleFormat.Pcm16);
            short S(int i) => BitConverter.ToInt16(bytes, 44 + i * 2);
            Assert.AreEqual(16384, S(0));
            Assert.AreEqual(0x00, bytes[44]); Assert.AreEqual(0x40, bytes[45]); // little-endian
            Assert.AreEqual(-16384, S(1));
            Assert.AreEqual(32767, S(2), "+1.0 clamps to full scale");
            Assert.AreEqual(-32768, S(3));
            Assert.AreEqual(32767, S(4), "over-range clamps");
        }

        [Test]
        public void Pcm24_RoundTripWithinOneLsb()
        {
            var b = Sine(997, 4800, 0.9f, SR, 2);
            var back = WavDecoder.Decode(WavEncoder.Encode(b, WavSampleFormat.Pcm24));
            Assert.AreEqual(2, back.Channels);
            Assert.AreEqual(4800, back.Frames);
            for (int c = 0; c < 2; c++) for (int i = 0; i < 4800; i++) Assert.AreEqual(b[c, i], back[c, i], 1.0f / 8388608f);
        }

        [Test]
        public void Float32_RoundTripIsBitExact_AndHasFactChunk()
        {
            var b = Sine(123, 1000, 1.3f);
            var bytes = WavEncoder.Encode(b, WavSampleFormat.Float32);
            Assert.AreEqual(3, BitConverter.ToInt16(bytes, 20), "IEEE float format tag");
            Assert.IsTrue(Encoding.ASCII.GetString(bytes).Contains("fact"));
            var back = WavDecoder.Decode(bytes);
            for (int i = 0; i < 1000; i++) Assert.AreEqual(b[0, i], back[0, i], 0f);
        }

        [Test]
        public void StereoInterleaving_KeepsChannelsSeparate()
        {
            var b = new AudioBuffer(2, 3, SR);
            b[0, 0] = 0.25f; b[1, 0] = -0.75f;
            var bytes = WavEncoder.Encode(b, WavSampleFormat.Pcm16);
            Assert.AreEqual(8192, BitConverter.ToInt16(bytes, 44), "frame 0 left");
            Assert.AreEqual(-24576, BitConverter.ToInt16(bytes, 46), "frame 0 right");
        }

        [Test]
        public void LoopMetadata_SmplChunkRoundTrips()
        {
            var b = Sine(440, 4800);
            var bytes = WavEncoder.Encode(b, WavSampleFormat.Pcm16, false, new WavLoopPoints { Start = 0, End = 4800 });
            using (var ms = new MemoryStream(bytes))
            {
                var info = WavDecoder.ReadInfo(ms);
                Assert.IsTrue(info.Loop.HasValue);
                Assert.AreEqual(0u, info.Loop.Value.Start);
                Assert.AreEqual(4800u, info.Loop.Value.End);
                Assert.AreEqual(4800, info.Frames);
            }
        }

        [Test]
        public void ExportPipeline_WritesValidatedFile_WithCorrectDuration()
        {
            var b = Sine(440, 24000, 0.5f, SR, 2);
            string path = Path.Combine(_dir, "out.wav");
            var r = ExportPipeline.ExportToFile(b, new ExportOptions { Format = WavSampleFormat.Pcm24 }, path);
            Assert.IsTrue(r.Validated);
            Assert.AreEqual(0.5, r.DurationSeconds, 1e-9);
            var back = WavDecoder.Decode(path, out var info);
            Assert.AreEqual(24, info.BitsPerSample);
            Assert.AreEqual(24000, back.Frames);
            Assert.IsFalse(File.Exists(path + ".partial~"), "temp file must be cleaned up");
        }

        [Test]
        public void ExportPipeline_ResamplesAndConvertsChannels()
        {
            var b = Sine(440, 48000, 0.5f, SR, 1);
            string path = Path.Combine(_dir, "conv.wav");
            var r = ExportPipeline.ExportToFile(b, new ExportOptions { SampleRate = 44100, Channels = 2 }, path);
            Assert.AreEqual(44100, r.SampleRate);
            Assert.AreEqual(2, r.Channels);
            Assert.AreEqual(44100, r.Frames);
        }

        [Test]
        public void ExportPipeline_NormalizesOnlyWhenRequested()
        {
            var b = Constant(0.25f, 1000);
            var plain = ExportPipeline.Prepare(b, new ExportOptions(), out float g0);
            Assert.AreEqual(1f, g0);
            Assert.AreEqual(0.25f, plain.GetPeak(), Exact);
            var norm = ExportPipeline.Prepare(b, new ExportOptions { Normalize = true, NormalizeTargetDb = -1f }, out _);
            Assert.AreEqual(DspMath.DbToGain(-1f), norm.GetPeak(), 1e-6f);
        }

        [Test]
        public void ExportPipeline_TrimsSilence()
        {
            var b = new AudioBuffer(1, 10000, SR);
            for (int i = 3000; i < 6000; i++) b[0, i] = 0.5f;
            var t = ExportPipeline.Prepare(b, new ExportOptions { TrimSilence = true, TrimThresholdDb = -60f }, out _);
            Assert.AreEqual(3000, t.Frames);
            Assert.AreEqual(0.5f, t[0, 0]);
        }

        [Test]
        public void ExportPipeline_Cancellation_LeavesNoFile()
        {
            var b = Sine(440, 480000, 0.5f, SR, 2);
            string path = Path.Combine(_dir, "cancel.wav");
            var cts = new CancellationTokenSource();
            cts.Cancel();
            Assert.Throws<OperationCanceledException>(() => ExportPipeline.ExportToFile(b, new ExportOptions(), path, cts.Token));
            Assert.IsFalse(File.Exists(path));
            Assert.IsFalse(File.Exists(path + ".partial~"));
        }

        [Test]
        public void ExportPipeline_CancelMidWrite_LeavesNoFileAndKeepsExisting()
        {
            string path = Path.Combine(_dir, "keep.wav");
            File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
            var cts = new CancellationTokenSource();
            var progress = new CancelAt(cts, 0.5f);
            Assert.Throws<OperationCanceledException>(() => ExportPipeline.ExportToFile(Sine(440, 480000, 0.5f, SR, 2), new ExportOptions(), path, cts.Token, progress));
            Assert.AreEqual(3, new FileInfo(path).Length, "a cancelled export must not touch the existing file");
            Assert.IsFalse(File.Exists(path + ".partial~"));
        }

        private sealed class CancelAt : IProgress<float>
        {
            private readonly CancellationTokenSource _c; private readonly float _at;
            public CancelAt(CancellationTokenSource c, float at) { _c = c; _at = at; }
            public void Report(float v) { if (v >= _at) _c.Cancel(); }
        }

        [Test]
        public void PreviewAndExport_AreEquivalent()
        {
            // Preview plays MixRenderer output directly; export writes the same buffer. Float32 export must be bit-exact.
            var rig = new Rig();
            var t = rig.Track();
            var c = rig.Clip(t, Sine(300, 24000, 0.6f), start: 1000);
            c.playbackRate = 1.25; c.fadeIn.lengthSamples = 2000; c.pan = 0.3f;
            t.effects.Add(EffectRegistry.CreateData(EffectTypes.Equalizer));
            t.effects.Add(EffectRegistry.CreateData(EffectTypes.Reverb));
            rig.Recipe.masterEffects.Add(EffectRegistry.CreateData(EffectTypes.Limiter));
            var preview = rig.Render().Master;
            var again = rig.Render().Master;
            string path = Path.Combine(_dir, "eq.wav");
            ExportPipeline.ExportToFile(again, new ExportOptions { Format = WavSampleFormat.Float32 }, path);
            var exported = WavDecoder.Decode(path, out _);
            Assert.AreEqual(preview.Frames, exported.Frames);
            for (int ch = 0; ch < 2; ch++) for (int i = 0; i < preview.Frames; i++) Assert.AreEqual(preview[ch, i], exported[ch, i], 0f);
            // 16-bit export differs only by quantization (≤ 0.5 LSB).
            string p16 = Path.Combine(_dir, "eq16.wav");
            ExportPipeline.ExportToFile(again, new ExportOptions { Format = WavSampleFormat.Pcm16 }, p16);
            var e16 = WavDecoder.Decode(p16, out _);
            for (int i = 0; i < preview.Frames; i++) Assert.AreEqual(Math.Max(-1f, Math.Min(32767f / 32768f, preview[0, i])), e16[0, i], 0.5f / 32768f + 1e-7f);
        }

        [Test]
        public void Dither_IsDeterministicAndSmall()
        {
            var b = Constant(0.1f, 2000);
            var a = WavEncoder.Encode(b, WavSampleFormat.Pcm16, true);
            var c = WavEncoder.Encode(b, WavSampleFormat.Pcm16, true);
            CollectionAssert.AreEqual(a, c);
            var d = WavDecoder.Decode(a);
            for (int i = 0; i < 2000; i++) Assert.AreEqual(0.1f, d[0, i], 2f / 32768f);
        }
    }
}
