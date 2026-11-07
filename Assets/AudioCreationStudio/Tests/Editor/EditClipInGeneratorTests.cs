using System.IO;
using AudioCreationStudio.DSP;
using AudioCreationStudio.EditorUI;
using AudioCreationStudio.Export;
using AudioCreationStudio.Models;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AudioCreationStudio.Tests
{
    /// <summary>Timeline clip → "Edit in Custom Generate" conversion: same audio, same placement, undoable, revertible.</summary>
    public class EditClipInGeneratorTests
    {
        private const string Temp = "Assets/AudioCreationStudio/Tests/_TempEditClip";
        private StudioState _state;
        private StudioSession _session;
        private StudioContext _ctx;
        private AudioClip _clip;
        private AudioBuffer _src;

        [SetUp]
        public void SetUp()
        {
            TakeStore.EnsureFolder(Temp);
            // 2 s stereo source: different tones left/right so channel handling is visible.
            int sr = 48000;
            _src = new AudioBuffer(2, 2 * sr, sr);
            var l = _src.GetChannel(0); var r = _src.GetChannel(1);
            for (int i = 0; i < l.Length; i++)
            {
                l[i] = 0.4f * Mathf.Sin(2 * Mathf.PI * 220 * i / sr);
                r[i] = 0.3f * Mathf.Sin(2 * Mathf.PI * 330 * i / sr);
            }
            string path = $"{Temp}/stereo_src.wav";
            File.WriteAllBytes(path, WavEncoder.Encode(_src, WavSampleFormat.Float32));
            AssetDatabase.ImportAsset(path);
            _clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);

            _state = new StudioState { generator = GeneratorPresets.CoinPickup() };
            _session = new StudioSession(_state, false);
            _ctx = new StudioContext { Session = _session, Library = SourceLibrary.Shared, Waveforms = WaveformCache.Shared };
            _ctx.Commands = new StudioCommands(_ctx);
        }

        [TearDown]
        public void TearDown()
        {
            _session.Dispose();
            Undo.ClearUndo(_state.working);
            Object.DestroyImmediate(_state.working);
            if (AssetDatabase.IsValidFolder(Temp)) AssetDatabase.DeleteAsset(Temp);
        }

        private ClipData AddClip()
        {
            _ctx.Commands.ImportClips(new[] { _clip }, 4800, _ctx.Data.tracks.Count);
            ClipData c = null;
            foreach (var t in _ctx.Data.tracks) foreach (var cl in t.clips) if (cl.sourceClip == _clip) c = cl;
            Assert.IsNotNull(c, "clip added to the timeline");
            string uid = c.uid;
            _session.Modify("Trim", d =>
            {
                var cc = d.FindClip(uid);
                cc.sourceOffset = 24000;              // start 0.5 s into the file
                cc.lengthSamples = 48000;             // play 1 s
                cc.gainDb = -2f;
                cc.fadeIn.lengthSamples = 480;
            });
            return _ctx.Data.FindClip(uid);
        }

        private AudioBuffer Render(ClipData c)
        {
            int sr = _ctx.SampleRate;
            var bus = new AudioBuffer(2, (int)(c.EndSample + 16), sr);
            AudioBuffer source;
            if (c.sourceKind == ClipSourceKind.Generator) { _ctx.Library.PrepareSample(c.generator, out _); source = _ctx.Library.GetGenerated(c.generator, sr); }
            else _ctx.Library.TryGetClip(c.sourceClip, out source, out _, out _);
            ClipRenderer.Render(c, source, bus, sr, null);
            return bus;
        }

        [Test]
        public void Convert_KeepsPlacementAndSoundsTheSame()
        {
            var c = AddClip();
            string uid = c.uid;
            var before = Render(c);
            long start = c.startSample, len = c.lengthSamples;

            var g = _ctx.Commands.ConvertToSampleGenerator(uid);

            Assert.IsNotNull(g);
            var conv = _ctx.Data.FindClip(uid);
            Assert.AreEqual(ClipSourceKind.Generator, conv.sourceKind);
            Assert.AreEqual(OscillatorWaveform.Sample, conv.generator.waveform);
            Assert.AreEqual(0.5f, conv.generator.sampleStart, 1e-4f, "source offset becomes the sample start");
            Assert.AreEqual(1f, conv.generator.durationSeconds, 1e-3f);
            Assert.AreEqual(start, conv.startSample);
            Assert.AreEqual(len, conv.lengthSamples);
            Assert.AreEqual(-2f, conv.gainDb);
            Assert.AreEqual(480, conv.fadeIn.lengthSamples);
            Assert.IsTrue(StudioCommands.CanRevertToAudio(conv));

            // The sample plays mono: compare against the original's mid (L+R)/2 signal, level included.
            var after = Render(conv);
            var bl = before.GetChannel(0); var br = before.GetChannel(1); var a = after.GetChannel(0);
            double num = 0, e1 = 0, e2 = 0;
            for (long i = start + 960; i < start + len - 960; i++)
            {
                double mid = 0.5 * (bl[i] + br[i]);
                num += mid * a[i]; e1 += mid * mid; e2 += a[i] * (double)a[i];
            }
            Assert.Greater(num / System.Math.Sqrt(e1 * e2), 0.99, "same waveform from the same point in the file");
            Assert.AreEqual(1.0, System.Math.Sqrt(e2 / e1), 0.05, "same loudness as the original's mid signal (pan law compensated)");
        }

        [Test]
        public void Convert_IsOneUndoStep_AndRevertRestoresTheOriginal()
        {
            var c = AddClip();
            string uid = c.uid;
            var original = Render(c);
            long offset = c.sourceOffset, len = c.lengthSamples;

            _ctx.Commands.ConvertToSampleGenerator(uid);
            Undo.PerformUndo();
            Assert.AreEqual(ClipSourceKind.AudioClip, _ctx.Data.FindClip(uid).sourceKind, "Ctrl+Z restores the audio clip");
            Undo.PerformRedo();
            Assert.AreEqual(ClipSourceKind.Generator, _ctx.Data.FindClip(uid).sourceKind);

            _ctx.Commands.RevertToOriginalAudio(uid);
            var rev = _ctx.Data.FindClip(uid);
            Assert.AreEqual(ClipSourceKind.AudioClip, rev.sourceKind);
            Assert.AreEqual(2, rev.sourceChannels);
            Assert.AreEqual(offset, rev.sourceOffset);
            Assert.AreEqual(len, rev.lengthSamples);
            Assert.IsFalse(StudioCommands.CanRevertToAudio(rev));
            var back = Render(rev);
            for (int ch = 0; ch < 2; ch++)
            {
                var x = original.GetChannel(ch); var y = back.GetChannel(ch);
                for (int i = 0; i < x.Length; i++) Assert.AreEqual(x[i], y[i], 1e-5f, $"identical audio after revert (ch {ch}, sample {i})");
            }
        }

        [Test]
        public void BoundEdit_IsOneUndoStep_AndKeepsLoopLength()
        {
            var c = AddClip();
            string uid = c.uid;
            _session.Modify("Loop", d => { var cc = d.FindClip(uid); cc.loop = true; cc.lengthSamples = 3 * 48000; });
            var view = new GeneratorView(_ctx, _state);
            var g = _ctx.Commands.ConvertToSampleGenerator(uid);
            view.Load(g, uid, recordUndo: false);
            long loopLen = _ctx.Data.FindClip(uid).lengthSamples;

            var edited = view.CurrentSettings.Clone();
            edited.pitchStart = edited.pitchEnd = 7f;
            view.ApplyExternal(edited, "Generator Pitch");
            Assert.AreEqual(7f, _ctx.Data.FindClip(uid).generator.pitchStart, "the timeline clip follows the generator");
            Assert.AreEqual(loopLen, _ctx.Data.FindClip(uid).lengthSamples, "a looping clip keeps its timeline length");

            Undo.PerformUndo();
            Assert.AreEqual(0f, _ctx.Data.FindClip(uid).generator.pitchStart, "one Ctrl+Z undoes the edit");
            Assert.AreEqual(ClipSourceKind.Generator, _ctx.Data.FindClip(uid).sourceKind);
            Undo.PerformUndo();
            Assert.AreEqual(ClipSourceKind.AudioClip, _ctx.Data.FindClip(uid).sourceKind, "the next Ctrl+Z restores the original audio clip");
            if (_state.generatorDoc != null) { Undo.ClearUndo(_state.generatorDoc); Object.DestroyImmediate(_state.generatorDoc); }
        }

        [Test]
        public void Convert_GeneratorClipIsLeftAsIs()
        {
            var gc = _ctx.Commands.InsertGenerator(GeneratorPresets.CoinPickup(), null, 0);
            var g = _ctx.Commands.ConvertToSampleGenerator(gc.uid);
            Assert.AreSame(_ctx.Data.FindClip(gc.uid).generator, g);
            Assert.AreEqual(ClipSourceKind.Generator, _ctx.Data.FindClip(gc.uid).sourceKind);
            Assert.IsFalse(StudioCommands.CanRevertToAudio(_ctx.Data.FindClip(gc.uid)), "a plain generator clip has no original audio");
        }
    }
}
