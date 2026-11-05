using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
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
    public class EditorIntegrationTests
    {
        private const string TempFolder = "Assets/AudioCreationStudio/Tests/_Temp";

        [SetUp]
        public void SetUp() => Directory.CreateDirectory(TempFolder);

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(TempFolder);
            AssetDatabase.Refresh();
        }

        private static AudioClip MakeWavAsset(string name, AudioBuffer b)
        {
            string path = $"{TempFolder}/{name}.wav";
            using (var fs = File.Create(path)) WavEncoder.Write(fs, b, WavSampleFormat.Pcm16);
            AssetDatabase.ImportAsset(path);
            return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }

        private static RecipeData SampleRecipe(AudioClip clip)
        {
            var r = new RecipeData { name = "Serialize Me", sampleRate = 44100, channels = OutputChannelMode.Mono };
            var t = TimelineEditing.AddTrack(r, "A");
            var c = TimelineEditing.CreateClipFromSource(clip, 48000, 4800, 1, 123);
            c.lengthSamples = 4000; c.gainDb = -3; c.pan = 0.2f; c.playbackRate = 1.5; c.fadeIn.lengthSamples = 100; c.fadeOut.curve = FadeCurve.SCurve;
            c.GetOrCreateLane(AutomationTargets.Gain).points.Add(new AutomationPoint(10, -6, AutomationInterpolation.Smooth));
            t.clips.Add(c);
            t.clips.Add(TimelineEditing.CreateGeneratorClip(GeneratorPresets.CoinPickup(), r.sampleRate, 5000));
            var fx = EffectRegistry.CreateData(EffectTypes.Delay);
            fx.values[0] = 333;
            t.effects.Add(fx);
            t.GetOrCreateLane(AutomationTargets.Effect(fx.uid, "mix")).points.Add(new AutomationPoint(0, 0.7f));
            r.masterEffects.Add(EffectRegistry.CreateData(EffectTypes.Limiter));
            r.loopRegion = new TimelineRegion { enabled = true, start = 10, end = 900 };
            r.export.format = WavSampleFormat.Float32;
            r.variations.seed = 4242;
            return r;
        }

        [Test]
        public void RecipeAsset_SaveAndReload_PreservesEverything()
        {
            var clip = MakeWavAsset("src", Sine(440, 4800));
            var data = SampleRecipe(clip);
            string expected = EditorJsonUtility.ToJson(data);
            var asset = ScriptableObject.CreateInstance<AudioRecipe>();
            asset.data = data;
            string path = $"{TempFolder}/Recipe.asset";
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            Resources.UnloadAsset(asset);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var loaded = AssetDatabase.LoadAssetAtPath<AudioRecipe>(path);
            Assert.NotNull(loaded);
            Assert.AreEqual(expected, EditorJsonUtility.ToJson(loaded.data));
            Assert.AreEqual(clip, loaded.data.tracks[0].clips[0].sourceClip, "stable AudioClip reference");
            Assert.AreEqual(1.5, loaded.data.tracks[0].clips[0].playbackRate);
        }

        [Test]
        public void RecipeClone_IsDeepAndEqual()
        {
            var r = SampleRecipe(null);
            var c = r.Clone();
            Assert.AreEqual(JsonUtility.ToJson(r), JsonUtility.ToJson(c));
            c.tracks[0].clips[0].gainDb = 6;
            c.tracks[0].effects[0].values[0] = 1;
            c.tracks[0].clips[0].automation[0].points.Clear();
            c.tracks[0].clips[1].generator.frequency = 1;
            Assert.AreEqual(-3, r.tracks[0].clips[0].gainDb);
            Assert.AreEqual(333, r.tracks[0].effects[0].values[0]);
            Assert.AreEqual(1, r.tracks[0].clips[0].automation[0].points.Count);
            Assert.AreNotEqual(1, r.tracks[0].clips[1].generator.frequency);
        }

        [Test]
        public void SourceAsset_IsNeverModified()
        {
            var clip = MakeWavAsset("untouched", Sine(220, 9600, 0.7f));
            string path = AssetDatabase.GetAssetPath(clip);
            byte[] Hash() { using (var sha = SHA256.Create()) return sha.ComputeHash(File.ReadAllBytes(path)); }
            var before = Hash();
            var importerBefore = EditorJsonUtility.ToJson(AssetImporter.GetAtPath(path));
            var r = new RecipeData();
            var t = TimelineEditing.AddTrack(r);
            Assert.IsTrue(SourceLibrary.Shared.TryGetClip(clip, out var buf, out _, out _));
            var c = TimelineEditing.CreateClipFromSource(clip, buf.SampleRate, buf.Frames, buf.Channels, 0);
            TimelineEditing.FitLengthToSource(c, r.sampleRate);
            t.clips.Add(c);
            TimelineEditing.TrimStart(c, 1000, r.sampleRate);
            TimelineEditing.Split(t, c, 4000, r.sampleRate);
            var job = RenderRequestFactory.Create(r, SourceLibrary.Shared);
            var res = job.Execute(new MixRenderer(), default);
            Assert.Greater(res.Peak, 0.1f);
            CollectionAssert.AreEqual(before, Hash(), "source file bytes changed");
            Assert.AreEqual(importerBefore, EditorJsonUtility.ToJson(AssetImporter.GetAtPath(path)), "import settings changed");
        }

        [Test]
        public void SourceLibrary_DecodesWavSamplesExactly()
        {
            var b = Sine(1000, 4800, 0.5f);
            var clip = MakeWavAsset("decode", b);
            Assert.IsTrue(SourceLibrary.Shared.TryGetClip(clip, out var buf, out var key, out var err), err);
            Assert.AreEqual(4800, buf.Frames);
            Assert.AreEqual(SR, buf.SampleRate);
            StringAssert.StartsWith("clip:", key);
            for (int i = 0; i < 4800; i++) Assert.AreEqual(b[0, i], buf[0, i], 1f / 32768f);
        }

        [Test]
        public void Session_UndoRedo_RestoresRecipe()
        {
            var state = new StudioState();
            var session = new StudioSession(state, false);
            try
            {
                int tracks = session.Data.tracks.Count;
                session.Modify("Add Track", d => TimelineEditing.AddTrack(d), StudioChange.Structure);
                Assert.AreEqual(tracks + 1, session.Data.tracks.Count);
                session.BeginContinuous("Drag Gain");
                for (int i = 1; i <= 5; i++) session.Continuous(d => d.tracks[0].gainDb = -i);
                session.EndContinuous();
                Assert.AreEqual(-5, session.Data.tracks[0].gainDb);
                Undo.PerformUndo();
                Assert.AreEqual(0, session.Data.tracks[0].gainDb, "continuous drag collapses into one undo step");
                Undo.PerformUndo();
                Assert.AreEqual(tracks, session.Data.tracks.Count);
                Undo.PerformRedo();
                Assert.AreEqual(tracks + 1, session.Data.tracks.Count);
            }
            finally
            {
                session.Dispose();
                Undo.ClearUndo(state.working);
                UnityEngine.Object.DestroyImmediate(state.working);
            }
        }

        [Test]
        public void Commands_SplitDuplicateDeleteCopyPaste()
        {
            var state = new StudioState();
            var session = new StudioSession(state, false);
            try
            {
                var ctx = new StudioContext { Session = session, Library = SourceLibrary.Shared, Waveforms = WaveformCache.Shared };
                ctx.Commands = new StudioCommands(ctx);
                var clip = ctx.Commands.InsertGenerator(GeneratorPresets.Whoosh(), null, 0);
                Assert.AreEqual(1, session.SelectedClips.Count);
                session.Playhead = clip.lengthSamples / 2;
                ctx.Commands.SplitAtPlayhead();
                session.Data.FindClip(clip.uid, out var owner);
                Assert.AreEqual(2, owner.clips.Count);
                ctx.Commands.DuplicateSelection();
                Assert.AreEqual(3, owner.clips.Count);
                ctx.Commands.CopySelection();
                ctx.Commands.Paste(100000, owner.uid);
                Assert.AreEqual(4, owner.clips.Count);
                Assert.AreEqual(100000, session.PrimaryClip.startSample);
                ctx.Commands.DeleteSelection();
                Assert.AreEqual(3, owner.clips.Count);
            }
            finally
            {
                session.Dispose();
                UnityEngine.Object.DestroyImmediate(state.working);
            }
        }

        [Test]
        public void PreviewPlayer_ReleasesResources()
        {
            int Count() => Resources.FindObjectsOfTypeAll<AudioClip>().Count(c => c.name == "ACS Preview");
            int before = Count();
            var p = PreviewPlayerFactory.Create();
            p.Play(Sine(440, 4800), 0, false);
            Assert.AreEqual(before + 1, Count());
            p.Play(Sine(220, 4800), 0, true);
            Assert.AreEqual(before + 1, Count(), "restarting playback must not leak clips");
            p.Stop();
            Assert.AreEqual(before, Count());
            p.Play(Sine(220, 4800), 0, true);
            p.Dispose();
            Assert.AreEqual(before, Count());
            Assert.IsFalse(p.IsPlaying);
        }

        [Test]
        public void AudioSourcePlayer_PlaysAndCleansUp()
        {
            var p = PreviewPlayerFactory.Create(PlaybackBackend.SceneAudioSource);
            p.Play(Sine(440, 48000), 100, true);
            Assert.IsTrue(p.IsPlaying);
            Assert.AreEqual(1, Resources.FindObjectsOfTypeAll<GameObject>().Count(g => g.name == "ACS Preview Source"));
            p.Pause();
            Assert.IsTrue(p.IsPlaying, "paused counts as active");
            p.Dispose();
            Assert.IsFalse(p.IsPlaying);
            Assert.AreEqual(0, Resources.FindObjectsOfTypeAll<GameObject>().Count(g => g.name == "ACS Preview Source" || g.name == "ACS Preview Listener"));
            Assert.AreEqual(0, Resources.FindObjectsOfTypeAll<AudioClip>().Count(c => c.name == "ACS Preview"));
        }

        [Test]
        public void PreviewPlayer_DetectsExternalStop_AndTransportSelfHeals()
        {
            var p = PreviewPlayerFactory.Create(PlaybackBackend.EditorPreview);
            if (!(p is AudioUtilPreviewPlayer)) { p.Dispose(); Assert.Ignore("Editor preview backend unavailable"); }
            p.Play(Sine(440, 48000), 0, true);
            Assert.IsTrue(p.IsPlaying);
            // Something else (e.g. the AudioClip inspector) stops the shared preview channel.
            var util = typeof(Editor).Assembly.GetType("UnityEditor.AudioUtil");
            util.GetMethod("StopAllPreviewClips", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic).Invoke(null, null);
            Assert.IsFalse(p.IsPlaying, "player must notice it no longer owns the preview");
            Assert.AreEqual(0, Resources.FindObjectsOfTypeAll<AudioClip>().Count(c => c.name == "ACS Preview"), "orphaned preview clip released");
            p.Play(Sine(440, 4800), 0, false);
            Assert.IsTrue(p.IsPlaying, "playing again works after an external stop");
            p.Dispose();
        }

        [Test]
        public void PresetStore_SaveLoadRenameDuplicateDelete()
        {
            bool folderExisted = AssetDatabase.IsValidFolder(PresetStore.UserFolder);
            var g = GeneratorPresets.Impact();
            g.seed = 31337;
            var p = PresetStore.Save(PresetKind.Generator, "ACS Test Preset", "Tests", g);
            try
            {
                var loaded = PresetStore.LoadGenerator(p);
                Assert.AreEqual(JsonUtility.ToJson(g), JsonUtility.ToJson(loaded));
                PresetStore.Rename(p, "ACS Renamed");
                Assert.AreEqual("ACS Renamed", p.displayName);
                var dup = PresetStore.Duplicate(p);
                Assert.NotNull(dup);
                Assert.IsTrue(PresetStore.OfKind(PresetKind.Generator).Contains(dup));
                PresetStore.Delete(dup);
                var chain = new EffectChainPayload();
                chain.effects.Add(EffectRegistry.CreateData(EffectTypes.Reverb));
                var cp = PresetStore.Save(PresetKind.EffectChain, "ACS Chain", "Tests", chain);
                var lc = PresetStore.LoadChain(cp);
                Assert.AreEqual(1, lc.Count);
                Assert.AreNotEqual(chain.effects[0].uid, lc[0].uid, "loaded chains get fresh uids");
                PresetStore.Delete(cp);
            }
            finally
            {
                PresetStore.Delete(p);
                if (!folderExisted) AssetDatabase.DeleteAsset(PresetStore.UserFolder);
            }
        }

        [Test]
        public void RenderService_ProducesSameAudioAsDirectRender()
        {
            var state = new StudioState();
            var session = new StudioSession(state, false);
            try
            {
                session.Modify("setup", d =>
                {
                    d.tracks[0].clips.Add(TimelineEditing.CreateGeneratorClip(GeneratorPresets.Pop(), d.sampleRate, 100));
                });
                var svc = new PreviewRenderService(session, SourceLibrary.Shared);
                var viaService = svc.RenderNow(RenderScope.FullMix).Master;
                var job = RenderRequestFactory.Create(session.Data, SourceLibrary.Shared);
                var direct = job.Execute(new MixRenderer(), default).Master;
                Assert.AreEqual(direct.Frames, viaService.Frames);
                for (int i = 0; i < direct.Frames; i++) Assert.AreEqual(direct[0, i], viaService[0, i], 0f);
                svc.Dispose();
            }
            finally
            {
                session.Dispose();
                UnityEngine.Object.DestroyImmediate(state.working);
            }
        }
    }
}
