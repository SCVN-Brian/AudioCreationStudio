using System;
using AudioCreationStudio.DSP;
using AudioCreationStudio.Editing;
using AudioCreationStudio.Models;
using NUnit.Framework;
using static AudioCreationStudio.Tests.TestUtil;

namespace AudioCreationStudio.Tests
{
    public class MixingTests
    {
        [Test]
        public void TrackSummation_AddsOverlappingClips()
        {
            var rig = new Rig(OutputChannelMode.Mono);
            rig.Clip(rig.Track(), Constant(0.25f, 1000));
            rig.Clip(rig.Track(), Constant(0.25f, 1000));
            var m = rig.Render().Master;
            Assert.AreEqual(1000, m.Frames);
            for (int i = 0; i < 1000; i++) Assert.AreEqual(0.5f, m[0, i], Exact);
        }

        [Test]
        public void ClipPlacement_IsSampleAccurate()
        {
            var rig = new Rig(OutputChannelMode.Mono);
            rig.Clip(rig.Track(), Impulse(100, 10), start: 12345);
            var m = rig.Render().Master;
            Assert.AreEqual(12345 + 100, m.Frames);
            Assert.AreEqual(1f, m[0, 12355], Exact);
            Assert.AreEqual(0f, m[0, 12354], Exact);
            Assert.AreEqual(0f, m[0, 12356], Exact);
        }

        [Test]
        public void Trimming_KeepsAudioAnchoredInTime()
        {
            var rig = new Rig(OutputChannelMode.Mono);
            var src = new AudioBuffer(1, 1000, SR);
            for (int i = 0; i < 1000; i++) src[0, i] = i / 1000f;
            var c = rig.Clip(rig.Track(), src, start: 500);
            TimelineEditing.TrimStart(c, 700, SR);
            TimelineEditing.TrimEnd(c, 1200, SR);
            Assert.AreEqual(200, c.sourceOffset);
            Assert.AreEqual(500, c.lengthSamples);
            var m = rig.Render().Master;
            Assert.AreEqual(0f, m[0, 699], Exact);
            Assert.AreEqual(200 / 1000f, m[0, 700], Exact);
            Assert.AreEqual(699 / 1000f, m[0, 1199], Exact);
            Assert.AreEqual(1200, m.Frames);
        }

        [Test]
        public void Split_ProducesIdenticalAudio()
        {
            var rig = new Rig();
            var t = rig.Track();
            var c = rig.Clip(t, Sine(440, 4800), start: 100);
            c.fadeIn.lengthSamples = 300;
            c.fadeOut.lengthSamples = 400;
            var before = rig.Render().Master;
            var right = TimelineEditing.Split(t, c, 2000, SR);
            Assert.NotNull(right);
            rig.Sources[right.uid] = rig.Sources[c.uid];
            var after = rig.Render().Master;
            Assert.AreEqual(before.Frames, after.Frames);
            for (int ch = 0; ch < 2; ch++) for (int i = 0; i < before.Frames; i++) Assert.AreEqual(before[ch, i], after[ch, i], Exact, $"ch{ch} frame {i}");
        }

        [Test]
        public void ClipGain_Minus6dB_HalvesAmplitude()
        {
            var rig = new Rig(OutputChannelMode.Mono);
            var c = rig.Clip(rig.Track(), Constant(0.8f, 100));
            c.gainDb = -6.0206f;
            Assert.AreEqual(0.4f, rig.Render().Master[0, 50], 1e-4f);
        }

        [Test]
        public void TrackAndMasterGain_Multiply()
        {
            var rig = new Rig(OutputChannelMode.Mono);
            var t = rig.Track();
            t.gainDb = -6.0206f;
            rig.Recipe.masterGainDb = -6.0206f;
            rig.Clip(t, Constant(0.8f, 100));
            Assert.AreEqual(0.2f, rig.Render().Master[0, 50], 1e-4f);
        }

        [Test]
        public void Pan_MonoSourceUsesConstantPowerLaw()
        {
            var rig = new Rig();
            var c = rig.Clip(rig.Track(), Constant(1f, 100));
            c.pan = -1f;
            var m = rig.Render().Master;
            Assert.AreEqual(1f, m[0, 10], 1e-6f, "hard left: L");
            Assert.AreEqual(0f, m[1, 10], 1e-6f, "hard left: R");
            c.pan = 0f;
            m = rig.Render().Master;
            Assert.AreEqual(Math.Sqrt(0.5), m[0, 10], 1e-6, "centre −3 dB");
            Assert.AreEqual(Math.Sqrt(0.5), m[1, 10], 1e-6);
        }

        [Test]
        public void Pan_StereoSourceUsesBalanceAndKeepsChannelsIsolated()
        {
            var rig = new Rig();
            var src = new AudioBuffer(2, 100, SR);
            for (int i = 0; i < 100; i++) { src[0, i] = 0.5f; src[1, i] = 0f; }
            var c = rig.Clip(rig.Track(), src);
            var m = rig.Render().Master;
            Assert.AreEqual(0.5f, m[0, 20], Exact);
            Assert.AreEqual(0f, m[1, 20], Exact, "Left-only content must not leak into right");
            c.pan = 0.5f;
            m = rig.Render().Master;
            Assert.AreEqual(0.25f, m[0, 20], Exact);
        }

        [Test]
        public void Fades_StartAndEndAtSilence()
        {
            var rig = new Rig(OutputChannelMode.Mono);
            var c = rig.Clip(rig.Track(), Constant(1f, 1000));
            c.fadeIn = new FadeData { lengthSamples = 100, curve = FadeCurve.Linear };
            c.fadeOut = new FadeData { lengthSamples = 200, curve = FadeCurve.Linear };
            var m = rig.Render().Master;
            Assert.AreEqual(0f, m[0, 0], Exact);
            Assert.AreEqual(0.5f, m[0, 50], Exact);
            Assert.AreEqual(1f, m[0, 500], Exact);
            Assert.AreEqual(0f, m[0, 999], Exact);
            Assert.AreEqual(0.5f, m[0, 899], Exact);
        }

        [Test]
        public void EqualPowerFade_MidpointIsMinus3dB()
        {
            Assert.AreEqual(Math.Sqrt(0.5), FadeCurves.Evaluate(FadeCurve.EqualPower, 0.5), 1e-6);
        }

        [Test]
        public void MuteAndSolo_SelectTracks()
        {
            var rig = new Rig(OutputChannelMode.Mono);
            var a = rig.Track(); rig.Clip(a, Constant(0.1f, 100));
            var b = rig.Track(); rig.Clip(b, Constant(0.2f, 100));
            a.mute = true;
            Assert.AreEqual(0.2f, rig.Render().Master[0, 5], Exact);
            a.mute = false;
            a.solo = true;
            Assert.AreEqual(0.1f, rig.Render().Master[0, 5], Exact);
            b.solo = true;
            Assert.AreEqual(0.3f, rig.Render().Master[0, 5], Exact);
        }

        [Test]
        public void ScopedRenders_IsolateTrackAndClip()
        {
            var rig = new Rig(OutputChannelMode.Mono);
            var a = rig.Track(); var ca = rig.Clip(a, Constant(0.1f, 100));
            rig.Clip(a, Constant(0.4f, 100), start: 200);
            var b = rig.Track(); rig.Clip(b, Constant(0.2f, 100));
            Assert.AreEqual(0.1f, rig.Render(RenderScope.Track, a.uid).Master[0, 5], Exact);
            var clip = rig.Render(RenderScope.Clip, null, ca.uid).Master;
            Assert.AreEqual(100, clip.Frames);
            Assert.AreEqual(0.1f, clip[0, 5], Exact);
        }

        [Test]
        public void PlaybackRate2_HalvesDurationAndSkipsSamples()
        {
            var rig = new Rig(OutputChannelMode.Mono);
            var src = new AudioBuffer(1, 1000, SR);
            for (int i = 0; i < 1000; i++) src[0, i] = 0.25f;
            var c = rig.Clip(rig.Track(), src);
            c.playbackRate = 2.0;
            TimelineEditing.FitLengthToSource(c, SR);
            Assert.AreEqual(500, c.lengthSamples);
            var m = rig.Render().Master;
            Assert.AreEqual(500, m.Frames);
            Assert.AreEqual(0.25f, m[0, 250], 1e-4f);
        }

        [Test]
        public void SourceAtDifferentSampleRate_IsResampledToTimelineRate()
        {
            var rig = new Rig(OutputChannelMode.Mono);
            var src = Sine(500, 24000, 0.5f, 24000); // 1 s at 24 kHz
            var c = rig.Clip(rig.Track(), src);
            Assert.AreEqual(48000, c.lengthSamples, "1 s at 24 kHz is 48000 timeline samples at 48 kHz");
            var m = rig.Render().Master;
            var expected = Sine(500, 48000, 0.5f);
            double err = 0;
            for (int i = 100; i < 47900; i++) err = Math.Max(err, Math.Abs(m[0, i] - expected[0, i]));
            Assert.Less(err, 3e-3);
        }

        [Test]
        public void InvalidSamples_AreSanitized_AndClippingIsReported()
        {
            var rig = new Rig(OutputChannelMode.Mono);
            var src = Constant(1.5f, 100);
            src[0, 10] = float.NaN;
            src[0, 11] = float.PositiveInfinity;
            rig.Clip(rig.Track(), src);
            var res = rig.Render();
            for (int i = 0; i < res.Master.Frames; i++) Assert.IsFalse(float.IsNaN(res.Master[0, i]) || float.IsInfinity(res.Master[0, i]));
            Assert.Greater(res.ClippedSamples, 0, "Samples above 0 dBFS must be reported, not silently normalized");
            Assert.AreEqual(1.5f, res.Master[0, 50], Exact, "The mix is not normalized");
        }

        [Test]
        public void TrackCache_ReusesUnchangedTracks_AndInvalidatesOnEdit()
        {
            var rig = new Rig();
            var a = rig.Track(); rig.Clip(a, Sine(200, 4800));
            var b = rig.Track(); var cb = rig.Clip(b, Sine(300, 4800));
            var cache = new TrackRenderCache();
            RenderResult R()
            {
                var req = new RenderRequest { Recipe = rig.Recipe.Clone(), Cache = cache };
                foreach (var kv in rig.Sources) req.Sources[kv.Key] = kv.Value;
                return new MixRenderer().Render(req);
            }
            var first = R();
            Assert.AreEqual(2, first.TracksRendered);
            var second = R();
            Assert.AreEqual(2, second.TracksFromCache);
            cb.gainDb = -3;
            var third = R();
            Assert.AreEqual(1, third.TracksFromCache);
            Assert.AreEqual(1, third.TracksRendered);
            var fresh = rig.Render().Master;
            for (int i = 0; i < fresh.Frames; i++) Assert.AreEqual(fresh[0, i], third.Master[0, i], Exact);
        }

        [Test]
        public void Automation_Interpolation_LinearSmoothStep()
        {
            var lane = new AutomationLane("gain");
            lane.points.Add(new AutomationPoint(0, 0f, AutomationInterpolation.Linear));
            lane.points.Add(new AutomationPoint(100, 10f, AutomationInterpolation.Smooth));
            lane.points.Add(new AutomationPoint(200, 0f, AutomationInterpolation.Step));
            lane.points.Add(new AutomationPoint(300, 5f));
            Assert.AreEqual(5f, lane.Evaluate(50, -1), 1e-5f);
            Assert.AreEqual(5f, lane.Evaluate(150, -1), 1e-5f, "smoothstep midpoint");
            Assert.AreEqual(10f - 10f * (0.25f * 0.25f * (3 - 0.5f)), lane.Evaluate(125, -1), 1e-4f);
            Assert.AreEqual(0f, lane.Evaluate(250, -1), 1e-5f, "step holds");
            Assert.AreEqual(5f, lane.Evaluate(1000, -1), 1e-5f, "after last point");
            lane.enabled = false;
            Assert.AreEqual(-1f, lane.Evaluate(50, -1));
        }

        [Test]
        public void ClipGainAutomation_IsAppliedInRender()
        {
            var rig = new Rig(OutputChannelMode.Mono);
            var c = rig.Clip(rig.Track(), Constant(1f, 1000));
            var lane = c.GetOrCreateLane(AutomationTargets.Gain);
            lane.points.Add(new AutomationPoint(0, 0f));
            lane.points.Add(new AutomationPoint(500, -6.0206f, AutomationInterpolation.Step));
            var m = rig.Render().Master;
            Assert.AreEqual(1f, m[0, 0], 1e-4f);
            Assert.AreEqual(0.5f, m[0, 700], 1e-4f);
        }

        [Test]
        public void ChangeSampleRate_PreservesTimeInSeconds()
        {
            var rig = new Rig();
            var c = rig.Clip(rig.Track(), Constant(0.1f, 4800), start: 48000);
            c.fadeIn.lengthSamples = 4800;
            TimelineEditing.ChangeSampleRate(rig.Recipe, 44100);
            Assert.AreEqual(44100, c.startSample);
            Assert.AreEqual(4410, c.fadeIn.lengthSamples);
            Assert.AreEqual(4410, c.lengthSamples);
        }
    }
}
