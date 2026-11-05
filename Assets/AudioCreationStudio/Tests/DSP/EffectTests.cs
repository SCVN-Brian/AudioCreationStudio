using System;
using AudioCreationStudio.DSP;
using AudioCreationStudio.Models;
using NUnit.Framework;
using static AudioCreationStudio.Tests.TestUtil;

namespace AudioCreationStudio.Tests
{
    public class EffectTests
    {
        private static EffectData Fx(string type, params (string id, float v)[] values)
        {
            var e = EffectRegistry.CreateData(type);
            var def = EffectRegistry.Get(type);
            foreach (var (id, v) in values) e.values[def.IndexOf(id)] = v;
            return e;
        }

        private static AudioBuffer Run(AudioBuffer input, int block, params EffectData[] chain)
        {
            var b = input.Clone();
            var p = new EffectChainProcessor(chain, b.SampleRate, b.Channels, block);
            for (int pos = 0; pos < b.Frames; pos += block) p.Process(b, pos, Math.Min(block, b.Frames - pos));
            return b;
        }

        [Test]
        public void AllRegisteredEffects_ProduceFiniteOutput()
        {
            var input = Sine(440, 9600, 0.9f, SR, 2);
            foreach (var def in EffectRegistry.All)
            {
                var o = Run(input, 512, EffectRegistry.CreateData(def.TypeId));
                for (int c = 0; c < 2; c++) for (int i = 0; i < o.Frames; i++)
                    Assert.IsFalse(float.IsNaN(o[c, i]) || float.IsInfinity(o[c, i]), $"{def.TypeId} produced invalid output");
            }
        }

        [Test]
        public void Bypass_And_Disable_PassAudioUnchanged()
        {
            var input = Sine(440, 4800, 0.5f, SR, 2);
            foreach (var def in EffectRegistry.All)
            {
                var e = EffectRegistry.CreateData(def.TypeId);
                e.bypass = true;
                var o = Run(input, 256, e);
                var e2 = EffectRegistry.CreateData(def.TypeId);
                e2.enabled = false;
                var o2 = Run(input, 256, e2);
                for (int i = 0; i < input.Frames; i++)
                {
                    Assert.AreEqual(input[0, i], o[0, i], 0f, $"{def.TypeId} bypass");
                    Assert.AreEqual(input[0, i], o2[0, i], 0f, $"{def.TypeId} disabled");
                }
            }
        }

        [Test]
        public void GainEffect_AppliesLevel()
        {
            var o = Run(Constant(0.5f, 4800), 512, Fx(EffectTypes.Gain, ("gain", -6.0206f)));
            Assert.AreEqual(0.25f, o[0, 4000], 1e-4f);
        }

        [Test]
        public void LowPass_PassesLowsAndAttenuatesHighs()
        {
            var lp = Fx(EffectTypes.LowPass, ("cutoff", 1000f), ("q", 0.707f));
            var low = Run(Sine(100, 24000), 512, lp);
            var high = Run(Sine(10000, 24000), 512, lp);
            double inRms = 0.5 / Math.Sqrt(2);
            double lowDb = 20 * Math.Log10(Rms(low.GetChannel(0), 4800, 19000) / inRms);
            double highDb = 20 * Math.Log10(Rms(high.GetChannel(0), 4800, 19000) / inRms);
            Assert.Greater(lowDb, -0.5, "100 Hz should pass");
            Assert.Less(highDb, -30, "10 kHz should be strongly attenuated");
        }

        [Test]
        public void HighPass_AttenuatesLows()
        {
            var hp = Fx(EffectTypes.HighPass, ("cutoff", 2000f));
            var low = Run(Sine(100, 24000), 512, hp);
            Assert.Less(20 * Math.Log10(Rms(low.GetChannel(0), 4800, 19000) / (0.5 / Math.Sqrt(2))), -40);
        }

        [Test]
        public void BiquadMagnitude_MatchesMeasuredResponse()
        {
            var k = BiquadCoefficients.Peaking(SR, 1000, 1, 6);
            Assert.AreEqual(Math.Pow(10, 6 / 20.0), k.MagnitudeAt(1000, SR), 1e-6);
            var eq = Fx(EffectTypes.Equalizer, ("mid1Freq", 1000f), ("mid1Gain", 6f), ("mid1Q", 1f));
            var o = Run(Sine(1000, 24000), 512, eq);
            double db = 20 * Math.Log10(Rms(o.GetChannel(0), 4800, 19000) / (0.5 / Math.Sqrt(2)));
            Assert.AreEqual(6.0, db, 0.15);
        }

        [Test]
        public void StatefulProcessing_IsBlockSizeIndependent()
        {
            var input = Sine(330, 9000, 0.7f, SR, 2);
            foreach (var type in new[] { EffectTypes.LowPass, EffectTypes.Equalizer, EffectTypes.Delay, EffectTypes.Reverb, EffectTypes.Compressor, EffectTypes.Distortion })
            {
                var a = Run(input, 64, EffectRegistry.CreateData(type));
                var b = Run(input, 4096, EffectRegistry.CreateData(type));
                for (int c = 0; c < 2; c++) for (int i = 0; i < input.Frames; i++) Assert.AreEqual(a[c, i], b[c, i], 1e-5f, $"{type} ch{c} frame {i}");
            }
        }

        [Test]
        public void EffectOrder_ChangesOutput()
        {
            var input = Sine(200, 4800, 0.9f);
            var dist = Fx(EffectTypes.Distortion, ("drive", 24f), ("mix", 1f), ("output", 0f), ("tone", 20000f));
            var gain = Fx(EffectTypes.Gain, ("gain", -20f));
            var a = Run(input, 512, dist, gain);
            var b = Run(input, 512, gain.Clone(true), dist.Clone(true));
            double diff = 0;
            for (int i = 1000; i < 4800; i++) diff = Math.Max(diff, Math.Abs(a[0, i] - b[0, i]));
            Assert.Greater(diff, 0.05, "Distortion→Gain must differ from Gain→Distortion");
        }

        [Test]
        public void Delay_ProducesEchoAtDelayTime()
        {
            var d = Fx(EffectTypes.Delay, ("time", 10f), ("feedback", 0f), ("mix", 0.5f), ("damping", 20000f));
            var o = Run(Impulse(2000), 256, d);
            Assert.AreEqual(0.5f, o[0, 0], 1e-4f, "dry");
            Assert.AreEqual(0.5f, o[0, 480], 2e-2f, "echo at 10 ms = 480 samples");
            Assert.AreEqual(0f, o[0, 300], 1e-4f);
        }

        [Test]
        public void Reverb_HasTail_AndRendererKeepsIt()
        {
            var rig = new Rig(OutputChannelMode.Stereo);
            var t = rig.Track();
            rig.Clip(t, Impulse(480));
            t.effects.Add(Fx(EffectTypes.Reverb, ("mix", 0.5f)));
            var withTail = rig.Render(tail: true).Master;
            var noTail = rig.Render(tail: false).Master;
            Assert.AreEqual(480, noTail.Frames);
            Assert.Greater(withTail.Frames, SR / 4, "reverb tail should extend well beyond the clip");
            Assert.Greater(Rms(withTail.GetChannel(0), 4800, 4800), 1e-4);
        }

        [Test]
        public void Limiter_NeverExceedsCeiling()
        {
            var o = Run(Sine(150, 24000, 2.5f, SR, 2), 512, Fx(EffectTypes.Limiter, ("ceiling", -1f), ("input", 6f)));
            float ceil = DspMath.DbToGain(-1f);
            Assert.LessOrEqual(o.GetPeak(), ceil + 1e-6f);
        }

        [Test]
        public void Compressor_ReducesLoudSignals_LeavesQuietAlone()
        {
            var comp = Fx(EffectTypes.Compressor, ("threshold", -20f), ("ratio", 4f), ("knee", 0f), ("attack", 1f), ("release", 50f), ("makeup", 0f));
            var loud = Run(Constant(0.5f, 24000), 512, comp);   // -6 dBFS: 14 dB over → output -16.5 dBFS
            Assert.AreEqual(-20 + 14 / 4.0, DspMath.GainToDb(loud[0, 20000]), 0.2);
            var quiet = Run(Constant(0.05f, 24000), 512, EffectRegistry.CreateData(EffectTypes.Compressor));
            Assert.AreEqual(0.05f, quiet[0, 20000], 1e-4f);
        }

        [Test]
        public void EffectAutomation_ChangesParameterOverTime()
        {
            var rig = new Rig(OutputChannelMode.Mono);
            var t = rig.Track();
            rig.Clip(t, Constant(1f, 48000));
            var g = Fx(EffectTypes.Gain, ("gain", 0f));
            t.effects.Add(g);
            var lane = t.GetOrCreateLane(AutomationTargets.Effect(g.uid, "gain"));
            lane.points.Add(new AutomationPoint(0, 0f, AutomationInterpolation.Step));
            lane.points.Add(new AutomationPoint(24000, -6.0206f, AutomationInterpolation.Step));
            var m = rig.Render().Master;
            Assert.AreEqual(1f, m[0, 12000], 1e-3f);
            Assert.AreEqual(0.5f, m[0, 40000], 1e-3f);
        }

        [Test]
        public void ParamDef_LogMappingRoundTrips()
        {
            var p = EffectRegistry.Get(EffectTypes.LowPass).Params[0];
            foreach (var v in new[] { 20f, 100f, 1000f, 20000f }) Assert.AreEqual(v, p.FromNormalized(p.ToNormalized(v)), v * 1e-4f);
        }
    }
}
