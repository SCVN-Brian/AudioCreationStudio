using AudioCreationStudio.DSP;
using AudioCreationStudio.Editing;
using AudioCreationStudio.Models;
using NUnit.Framework;
using static AudioCreationStudio.Tests.TestUtil;

namespace AudioCreationStudio.Tests
{
    /// <summary>Slip / slide / roll / ripple / blade (the Trim, Blade and Dynamic Trim edit tools).</summary>
    public class TimelineToolsTests
    {
        /// <summary>Mono ramp source: sample i has value i / 10000, so rendered values reveal which source frame played.</summary>
        private static AudioBuffer Ramp(int n = 10000)
        {
            var b = new AudioBuffer(1, n, SR);
            for (int i = 0; i < n; i++) b[0, i] = i / 10000f;
            return b;
        }

        // ------------------------------------------------------------------ Slip

        [Test]
        public void Slip_ChangesVisibleAudio_NotPosition()
        {
            var rig = new Rig(OutputChannelMode.Mono);
            var c = rig.Clip(rig.Track(), Ramp(), start: 1000);
            TimelineEditing.TrimStart(c, 2000, SR); // in-point 1000
            TimelineEditing.TrimEnd(c, 4000, SR);   // 2000 samples long
            TimelineEditing.Slip(c, 3000, SR);
            Assert.AreEqual(2000, c.startSample);
            Assert.AreEqual(2000, c.lengthSamples);
            Assert.AreEqual(3000, c.sourceOffset);
            var m = rig.Render().Master;
            Assert.AreEqual(3000 / 10000f, m[0, 2000], Exact);
            Assert.AreEqual(4999 / 10000f, m[0, 3999], Exact);
        }

        [Test]
        public void Slip_IsClampedToTheSource()
        {
            var rig = new Rig();
            var c = rig.Clip(rig.Track(), Ramp(), start: 0);
            TimelineEditing.TrimEnd(c, 4000, SR);
            TimelineEditing.Slip(c, -500, SR);
            Assert.AreEqual(0, c.sourceOffset);
            TimelineEditing.Slip(c, 99999, SR);
            Assert.AreEqual(6000, c.sourceOffset, "10000-frame source, 4000 shown → max in-point 6000");
            Assert.LessOrEqual(c.lengthSamples, c.MaxLengthFromOffset(SR));
        }

        [Test]
        public void Slip_OnLoopingClipWraps()
        {
            var rig = new Rig();
            var c = rig.Clip(rig.Track(), Ramp(1000), start: 0);
            c.loop = true;
            c.lengthSamples = 5000;
            TimelineEditing.Slip(c, 1250, SR);
            Assert.AreEqual(250, c.sourceOffset);
            TimelineEditing.Slip(c, -100, SR);
            Assert.AreEqual(900, c.sourceOffset);
        }

        [Test]
        public void Slip_HonoursPlaybackRate()
        {
            var rig = new Rig();
            var c = rig.Clip(rig.Track(), Ramp(), start: 0);
            c.playbackRate = 2.0;
            c.lengthSamples = 2000; // uses 4000 source frames
            TimelineEditing.Slip(c, 9000, SR);
            Assert.AreEqual(6000, c.sourceOffset);
        }

        // ------------------------------------------------------------------ Roll

        [Test]
        public void Roll_MovesTheCut_KeepsOuterEdges()
        {
            var rig = new Rig(OutputChannelMode.Mono);
            var t = rig.Track();
            var c = rig.Clip(t, Ramp(), start: 0);
            var right = TimelineEditing.Split(t, c, 5000, SR);
            rig.Sources[right.uid] = rig.Sources[c.uid];
            var before = rig.Render().Master;

            long cut = TimelineEditing.Roll(c, right, 6000, SR);
            Assert.AreEqual(6000, cut);
            Assert.AreEqual(0, c.startSample);
            Assert.AreEqual(6000, c.EndSample);
            Assert.AreEqual(6000, right.startSample);
            Assert.AreEqual(10000, right.EndSample);
            // Same source on both sides: rolling the cut must not change the audio at all.
            var after = rig.Render().Master;
            for (int i = 0; i < before.Frames; i++) Assert.AreEqual(before[0, i], after[0, i], Exact, $"frame {i}");
        }

        [Test]
        public void Roll_IsLimitedByAvailableMedia()
        {
            var rig = new Rig();
            var t = rig.Track();
            var a = rig.Clip(t, Ramp(3000), start: 0);   // full source used: cannot extend right
            var b = rig.Clip(t, Ramp(), start: 3000);
            b.sourceOffset = 1000;                       // 1000 frames of hidden head
            b.lengthSamples = 4000;
            Assert.AreEqual(3000, TimelineEditing.Roll(a, b, 5000, SR), "left clip has no more media");
            Assert.AreEqual(2000, TimelineEditing.Roll(a, b, 500, SR), "right clip has only 1000 frames before its in-point");
            Assert.AreEqual(0, b.sourceOffset);
            Assert.AreEqual(2500, TimelineEditing.Roll(a, b, 2500, SR));
        }

        // ------------------------------------------------------------------ Ripple

        [Test]
        public void RippleTrimEnd_ShiftsLaterClips()
        {
            var rig = new Rig();
            var t = rig.Track();
            var a = rig.Clip(t, Ramp(), start: 0);
            a.lengthSamples = 4000;
            var b = rig.Clip(t, Ramp(), start: 4000);
            var far = rig.Clip(t, Ramp(), start: 20000);
            long d = TimelineEditing.RippleTrimEnd(t, a, 3000, SR);
            Assert.AreEqual(-1000, d);
            Assert.AreEqual(3000, a.EndSample);
            Assert.AreEqual(3000, b.startSample, "touching clip follows");
            Assert.AreEqual(19000, far.startSample, "every later clip follows");
        }

        [Test]
        public void RippleTrimStart_KeepsClipStart_RemovesHead()
        {
            var rig = new Rig(OutputChannelMode.Mono);
            var t = rig.Track();
            var a = rig.Clip(t, Ramp(), start: 1000);
            a.lengthSamples = 5000;
            var b = rig.Clip(t, Ramp(), start: 6000);
            long removed = TimelineEditing.RippleTrimStart(t, a, 1500, SR);
            Assert.AreEqual(500, removed);
            Assert.AreEqual(1000, a.startSample);
            Assert.AreEqual(500, a.sourceOffset);
            Assert.AreEqual(4500, a.lengthSamples);
            Assert.AreEqual(5500, b.startSample);
            rig.Sources.Remove(b.uid);
            t.clips.Remove(b);
            Assert.AreEqual(500 / 10000f, rig.Render().Master[0, 1000], Exact);
        }

        [Test]
        public void RippleTrimStart_CanRevealHead_UpToSourceStart()
        {
            var rig = new Rig();
            var t = rig.Track();
            var a = rig.Clip(t, Ramp(), start: 0);
            a.sourceOffset = 300;
            a.lengthSamples = 2000;
            var b = rig.Clip(t, Ramp(), start: 2000);
            long removed = TimelineEditing.RippleTrimStart(t, a, -1000, SR);
            Assert.AreEqual(-300, removed);
            Assert.AreEqual(0, a.sourceOffset);
            Assert.AreEqual(2300, a.lengthSamples);
            Assert.AreEqual(2300, b.startSample);
        }

        // ------------------------------------------------------------------ Slide

        [Test]
        public void Slide_NeighboursAbsorbTheMove()
        {
            var rig = new Rig();
            var t = rig.Track();
            var a = rig.Clip(t, Ramp(), start: 0);
            var mid = TimelineEditing.Split(t, a, 3000, SR);
            var c = TimelineEditing.Split(t, mid, 6000, SR);
            long d = TimelineEditing.Slide(t, mid, 3500, SR);
            Assert.AreEqual(500, d);
            Assert.AreEqual(3500, a.EndSample);
            Assert.AreEqual(3500, mid.startSample);
            Assert.AreEqual(6500, mid.EndSample);
            Assert.AreEqual(6500, c.startSample);
            Assert.AreEqual(10000, c.EndSample, "outer edges unchanged");
            Assert.AreEqual(3000, mid.sourceOffset, "slide does not change the slid clip's content");
        }

        [Test]
        public void Slide_StopsWhenANeighbourWouldVanish()
        {
            var rig = new Rig();
            var t = rig.Track();
            var a = rig.Clip(t, Ramp(), start: 0);
            var mid = TimelineEditing.Split(t, a, 3000, SR);
            TimelineEditing.Split(t, mid, 6000, SR);
            TimelineEditing.Slide(t, mid, -5000, SR);
            Assert.AreEqual(TimelineEditing.MinClipLength, a.lengthSamples);
        }

        // ------------------------------------------------------------------ Blade / restore

        [Test]
        public void SplitAll_CutsEveryTrackUnderThePoint()
        {
            var rig = new Rig();
            var t1 = rig.Track();
            var t2 = rig.Track();
            rig.Clip(t1, Ramp(), start: 0);
            rig.Clip(t2, Ramp(), start: 0);
            rig.Clip(t2, Ramp(), start: 20000);
            var made = TimelineEditing.SplitAll(new[] { t1, t2 }, 4000, SR);
            Assert.AreEqual(2, made.Count);
            Assert.AreEqual(2, t1.clips.Count);
            Assert.AreEqual(3, t2.clips.Count);
        }

        [Test]
        public void RestorePlacement_UndoesATrimToolDrag()
        {
            var rig = new Rig();
            var t = rig.Track();
            var c = rig.Clip(t, Ramp(), start: 100);
            c.fadeIn.lengthSamples = 50;
            var snap = c.Clone(false);
            TimelineEditing.RippleTrimStart(t, c, 900, SR);
            TimelineEditing.Slip(c, 42, SR);
            TimelineEditing.RestorePlacement(c, snap);
            Assert.AreEqual(snap.startSample, c.startSample);
            Assert.AreEqual(snap.sourceOffset, c.sourceOffset);
            Assert.AreEqual(snap.lengthSamples, c.lengthSamples);
            Assert.AreEqual(50, c.fadeIn.lengthSamples);
        }
    }
}
