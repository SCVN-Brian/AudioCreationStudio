using System;
using System.IO;
using AudioCreationStudio.DSP;
using AudioCreationStudio.EditorUI;
using AudioCreationStudio.Editing;
using AudioCreationStudio.Export;
using AudioCreationStudio.Models;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using static AudioCreationStudio.Tests.TestUtil;

namespace AudioCreationStudio.Tests
{
    public class SampleSourceTests
    {
        private const string Temp = "Assets/AudioCreationStudio/Tests/_TempSample";

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(Temp)) AssetDatabase.DeleteAsset(Temp);
        }

        private static GeneratorSettings Neutral(float dur) { var g = GeneratorView.NeutralSampleSettings(); g.durationSeconds = dur; g.sampleGuid = "x"; return g; }

        private static int RisingCrossings(AudioBuffer b, int from, int to)
        {
            int n = 0;
            for (int i = Math.Max(1, from); i < to; i++) if (b[0, i - 1] < 0 && b[0, i] >= 0) n++;
            return n;
        }

        [Test]
        public void NeutralSettings_ReproduceTheSample()
        {
            var src = Sine(300, SR / 2, 0.5f);
            var o = ProceduralSynth.Render(Neutral(0.5f), SR, default, src);
            Assert.AreEqual(src.Frames, o.Frames);
            for (int i = 1000; i < o.Frames - 1000; i++) Assert.AreEqual(src[0, i], o[0, i], 1e-5f, $"frame {i}");
        }

        [Test]
        public void PitchUpAnOctave_DoublesFrequency()
        {
            var src = Sine(300, SR, 0.5f);
            var g = Neutral(0.4f);
            g.pitchStart = g.pitchEnd = 12f;
            var o = ProceduralSynth.Render(g, SR, default, src);
            Assert.AreEqual(240, RisingCrossings(o, 0, o.Frames), 3, "600 Hz over 0.4 s");
        }

        [Test]
        public void SampleStart_AndEnd_AreRespected()
        {
            var src = new AudioBuffer(1, SR / 10, SR);
            for (int i = 0; i < src.Frames; i++) src[0, i] = i < src.Frames / 2 ? 0.25f : 0.75f;
            var g = Neutral(0.2f);
            g.sampleStart = 0.05f;
            var o = ProceduralSynth.Render(g, SR, default, src);
            Assert.AreEqual(0.75f, o[0, 1000], 1e-4f, "starts at the offset");
            Assert.AreEqual(0f, o[0, (int)(0.1 * SR)], 1e-6f, "silence after the sample ends");
        }

        [Test]
        public void ProcessingApplies_AndMissingSampleIsSilent()
        {
            var src = Sine(300, SR / 2, 0.5f);
            var g = Neutral(0.5f);
            g.volumeDb = -6.0206f;
            var o = ProceduralSynth.Render(g, SR, default, src);
            Assert.AreEqual(0.5f * src[0, 5000], o[0, 5000], 1e-4f);
            var silent = ProceduralSynth.Render(g, SR, default, null);
            Assert.AreEqual(0f, silent.GetPeak());
        }

        [Test]
        public void Hash_IncludesSampleIdentity()
        {
            var a = Neutral(0.5f);
            var b = a.Clone();
            Assert.AreEqual(a.ComputeHash(), b.ComputeHash());
            b.sampleGuid = "other";
            Assert.AreNotEqual(a.ComputeHash(), b.ComputeHash());
        }

        [Test]
        public void SampleGeneratorClip_RendersThroughTimelineAndLibrary()
        {
            Directory.CreateDirectory(Temp);
            string path = $"{Temp}/el_take.wav";
            using (var fs = File.Create(path)) WavEncoder.Write(fs, Sine(440, SR / 2, 0.5f, SR, 2), WavSampleFormat.Pcm16);
            AssetDatabase.ImportAsset(path);
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            var g = GeneratorView.NeutralSampleSettings();
            g.sampleGuid = AssetDatabase.AssetPathToGUID(path);
            g.durationSeconds = 0.5f;
            g.pitchStart = g.pitchEnd = -12f;
            var r = new RecipeData();
            var t = TimelineEditing.AddTrack(r);
            t.clips.Add(TimelineEditing.CreateGeneratorClip(g, r.sampleRate, 0));
            var res = RenderRequestFactory.Create(r, SourceLibrary.Shared).Execute(new MixRenderer(), default);
            Assert.Greater(res.Peak, 0.2f, "sample decoded on the main thread and used by the worker render");
            Assert.AreEqual(110, RisingCrossings(res.Master, 0, res.Master.Frames), 3, "an octave down: 220 Hz over 0.5 s");
            Assert.IsNotNull(clip);
        }

        [Test]
        public void GeneratorView_SetSample_IsUndoable()
        {
            Directory.CreateDirectory(Temp);
            string path = $"{Temp}/el_take2.wav";
            using (var fs = File.Create(path)) WavEncoder.Write(fs, Sine(440, SR, 0.5f), WavSampleFormat.Pcm16);
            AssetDatabase.ImportAsset(path);
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            var state = new StudioState { generator = GeneratorPresets.Pop() };
            var session = new StudioSession(state, false);
            try
            {
                var ctx = new StudioContext { Session = session, Library = SourceLibrary.Shared, Waveforms = WaveformCache.Shared };
                ctx.Commands = new StudioCommands(ctx);
                var view = new GeneratorView(ctx, state);
                view.SetSample(clip, false);
                var s = view.CurrentSettings;
                Assert.AreEqual(OscillatorWaveform.Sample, s.waveform);
                Assert.AreEqual(AssetDatabase.AssetPathToGUID(path), s.sampleGuid);
                Assert.AreEqual(1f, s.durationSeconds, 1e-3f, "duration fitted to the sample");
                Undo.PerformUndo();
                Assert.AreEqual("Pop", view.CurrentSettings.presetName);
            }
            finally
            {
                session.Dispose();
                if (state.generatorDoc != null) { Undo.ClearUndo(state.generatorDoc); UnityEngine.Object.DestroyImmediate(state.generatorDoc); }
                UnityEngine.Object.DestroyImmediate(state.working);
            }
        }
    }
}
