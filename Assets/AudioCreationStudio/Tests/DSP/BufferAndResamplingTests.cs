using System;
using AudioCreationStudio.DSP;
using NUnit.Framework;
using static AudioCreationStudio.Tests.TestUtil;

namespace AudioCreationStudio.Tests
{
    public class BufferAndResamplingTests
    {
        [Test]
        public void Interleave_RoundTrip_IsExact()
        {
            var b = new AudioBuffer(2, 100, SR);
            for (int i = 0; i < 100; i++) { b[0, i] = i * 0.01f; b[1, i] = -i * 0.01f; }
            var inter = b.ToInterleaved();
            Assert.AreEqual(200, inter.Length);
            Assert.AreEqual(b[0, 7], inter[14]);
            Assert.AreEqual(b[1, 7], inter[15]);
            var back = AudioBuffer.FromInterleaved(inter, 2, SR);
            for (int i = 0; i < 100; i++) { Assert.AreEqual(b[0, i], back[0, i]); Assert.AreEqual(b[1, i], back[1, i]); }
        }

        [Test]
        public void ChannelConverter_StereoToMono_Averages_MonoToStereo_Duplicates()
        {
            var st = new AudioBuffer(2, 4, SR);
            st[0, 0] = 1f; st[1, 0] = 0f;
            var mono = ChannelConverter.Convert(st, 1);
            Assert.AreEqual(0.5f, mono[0, 0], Exact);
            var up = ChannelConverter.Convert(mono, 2);
            Assert.AreEqual(0.5f, up[0, 0], Exact);
            Assert.AreEqual(0.5f, up[1, 0], Exact);
        }

        [Test]
        public void Resample_PreservesDc_AndLength()
        {
            var dc = Constant(0.3f, 44100, 44100);
            var r = Resampler.Resample(dc, 48000);
            Assert.AreEqual(48000, r.Frames);
            Assert.AreEqual(48000, r.SampleRate);
            // Ignore filter edges (zero-padded) at the very ends.
            for (int i = 64; i < r.Frames - 64; i++) Assert.AreEqual(0.3f, r[0, i], 1e-4f, $"frame {i}");
        }

        [Test]
        public void Resample_PreservesSineFrequencyAndAmplitude()
        {
            var s = Sine(1000, 44100, 0.5f, 44100);
            var r = Resampler.Resample(s, 48000);
            var reference = Sine(1000, 48000, 0.5f, 48000);
            double err = 0;
            for (int i = 200; i < 47800; i++) err = Math.Max(err, Math.Abs(r[0, i] - reference[0, i]));
            Assert.Less(err, 2e-3, "Resampled sine deviates from analytic sine");
        }

        [Test]
        public void Downsample_AttenuatesContentAboveNewNyquist()
        {
            // 20 kHz tone at 48 kHz → 22.05 kHz would alias to 2.05 kHz without filtering.
            var s = Sine(20000, 48000, 0.5f, 48000);
            var r = Resampler.Resample(s, 22050);
            double rms = Rms(r.GetChannel(0), 200, r.Frames - 400);
            Assert.Less(20 * Math.Log10(rms / (0.5 / Math.Sqrt(2))), -40.0, "Aliased energy should be < -40 dB");
        }

        [Test]
        public void SampleTime_RescaleIsSymmetricAndRounds()
        {
            Assert.AreEqual(48000, SampleTime.Rescale(44100, 44100, 48000));
            Assert.AreEqual(1, SampleTime.Rescale(1, 48000, 44100));
            Assert.AreEqual(24000, SampleTime.SecondsToSamples(0.5, 48000));
        }

        [Test]
        public void WaveformPeaks_MatchSampleMinMax()
        {
            var s = Sine(50, 48000, 0.8f);
            var p = WaveformPeaks.Build(s);
            p.Query(0, 48000, out float mn, out float mx, out float rms);
            Assert.AreEqual(0.8f, mx, 1e-3f);
            Assert.AreEqual(-0.8f, mn, 1e-3f);
            Assert.AreEqual(0.8f / Math.Sqrt(2), rms, 1e-2f);
            p.Query(100, 110, out mn, out mx, out _);
            float expMax = float.MinValue;
            for (int i = 100; i < 110; i++) expMax = Math.Max(expMax, s[0, i]);
            Assert.AreEqual(expMax, mx, Exact);
        }
    }
}
