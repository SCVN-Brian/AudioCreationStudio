using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using AudioCreationStudio.DSP;
using AudioCreationStudio.Editing;
using AudioCreationStudio.Export;
using AudioCreationStudio.Models;
using AudioCreationStudio.Samples;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AudioCreationStudio.EditorUI
{
    /// <summary>
    /// Builds the demo content: original source WAVs synthesized with the procedural engine, demo recipes
    /// that layer/trim/fade/process them, the final exported WAVs and a scene that plays them.
    /// All audio is generated here — no third-party assets.
    /// </summary>
    public static class DemoContent
    {
        public static string Root
        {
            get
            {
                foreach (var g in AssetDatabase.FindAssets("AudioCreationStudio.Runtime t:AssemblyDefinitionAsset"))
                {
                    var p = AssetDatabase.GUIDToAssetPath(g);
                    if (p.EndsWith("Runtime/AudioCreationStudio.Runtime.asmdef")) return p.Substring(0, p.Length - "/Runtime/AudioCreationStudio.Runtime.asmdef".Length);
                }
                return "Assets/AudioCreationStudio";
            }
        }

        public static string AudioFolder => Root + "/Samples/Audio";
        public static string RecipeFolder => Root + "/Samples/Recipes";
        public static string ExportFolder => Root + "/Samples/Demo/Exports";
        public static string ScenePath => Root + "/Samples/Demo/AudioStudioDemo.unity";

        private const int SR = 48000;

        [MenuItem("Tools/Audio Creation Studio/Open Demo", priority = 20)]
        public static void OpenDemo()
        {
            EnsureBuilt();
            OpenRecipe("Demo_SawMachine");
        }

        [MenuItem("Tools/Audio Creation Studio/Demos/Saw Machine", priority = 21)] public static void DemoSaw() { EnsureBuilt(); OpenRecipe("Demo_SawMachine"); }
        [MenuItem("Tools/Audio Creation Studio/Demos/Coin Pickup", priority = 22)] public static void DemoCoin() { EnsureBuilt(); OpenRecipe("Demo_CoinPickup"); }
        [MenuItem("Tools/Audio Creation Studio/Demos/Seamless Machine Loop", priority = 23)] public static void DemoLoop() { EnsureBuilt(); OpenRecipe("Demo_MachineLoop"); var w = EditorWindow.GetWindow<AudioStudioWindow>(); w.Context?.ShowTab?.Invoke("Loop"); }
        [MenuItem("Tools/Audio Creation Studio/Demos/UI SFX Pack", priority = 24)] public static void DemoUi() { EnsureBuilt(); OpenRecipe("Demo_UI_Confirm"); }
        [MenuItem("Tools/Audio Creation Studio/Demos/Open Demo Scene", priority = 40)]
        public static void OpenScene()
        {
            EnsureBuilt();
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(ScenePath);
        }

        [MenuItem("Tools/Audio Creation Studio/Demos/Rebuild Demo Assets", priority = 60)]
        public static void RebuildMenu()
        {
            Build(true);
            EditorUtility.DisplayDialog("Audio Creation Studio", $"Demo assets rebuilt.\n\nRecipes: {RecipeFolder}\nExports: {ExportFolder}\nScene: {ScenePath}", "OK");
        }

        [MenuItem("Tools/Audio Creation Studio/Documentation", priority = 100)]
        public static void OpenDocumentation()
        {
            var readme = AssetDatabase.LoadAssetAtPath<TextAsset>(Root + "/Documentation/README.md");
            if (readme != null) { Selection.activeObject = readme; EditorGUIUtility.PingObject(readme); AssetDatabase.OpenAsset(readme); }
        }

        public static void OpenDemoMenu()
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("A · Saw Machine (layering, trim, fades, effects)"), false, DemoSaw);
            menu.AddItem(new GUIContent("B · Coin Pickup (procedural)"), false, DemoCoin);
            menu.AddItem(new GUIContent("C · Seamless Machine Loop"), false, DemoLoop);
            menu.AddItem(new GUIContent("D · UI SFX Pack/UI Click"), false, () => { EnsureBuilt(); OpenRecipe("Demo_UI_Click"); });
            menu.AddItem(new GUIContent("D · UI SFX Pack/UI Confirm"), false, () => { EnsureBuilt(); OpenRecipe("Demo_UI_Confirm"); });
            menu.AddItem(new GUIContent("D · UI SFX Pack/UI Error"), false, () => { EnsureBuilt(); OpenRecipe("Demo_UI_Error"); });
            menu.AddItem(new GUIContent("D · UI SFX Pack/Upgrade"), false, () => { EnsureBuilt(); OpenRecipe("Demo_Upgrade"); });
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Open Demo Scene"), false, OpenScene);
            menu.AddItem(new GUIContent("Rebuild Demo Assets"), false, RebuildMenu);
            menu.ShowAsContext();
        }

        private static void OpenRecipe(string name)
        {
            var r = AssetDatabase.LoadAssetAtPath<AudioRecipe>($"{RecipeFolder}/{name}.asset");
            if (r != null) AudioStudioWindow.OpenRecipe(r);
            else Debug.LogWarning($"[Audio Creation Studio] Demo recipe {name} not found. Use Tools ▸ Audio Creation Studio ▸ Demos ▸ Rebuild Demo Assets.");
        }

        public static void EnsureBuilt()
        {
            if (AssetDatabase.LoadAssetAtPath<AudioRecipe>($"{RecipeFolder}/Demo_SawMachine.asset") == null) Build(false);
        }

        // =================================================================== Build

        public static List<ExportResult> Build(bool overwrite)
        {
            Directory.CreateDirectory(AudioFolder);
            Directory.CreateDirectory(RecipeFolder);
            Directory.CreateDirectory(ExportFolder);
            try
            {
                EditorUtility.DisplayProgressBar("Audio Creation Studio", "Synthesizing demo source audio…", 0.1f);
                WriteSource("SawMotor_Loop", MotorLoop(), overwrite);
                WriteSource("SawBlade_Rotation", BladeRotation(), overwrite);
                WriteSource("Wood_Cutting", WoodCutting(), overwrite);
                WriteSource("Wood_Crack", WoodCrack(), overwrite);
                WriteSource("Machine_Raw", MachineRaw(), overwrite);
                AssetDatabase.Refresh();

                EditorUtility.DisplayProgressBar("Audio Creation Studio", "Creating demo recipes…", 0.4f);
                var recipes = new List<(string name, RecipeData data, bool loop)>
                {
                    ("Demo_SawMachine", SawMachineRecipe(), false),
                    ("Demo_CoinPickup", CoinRecipe(), false),
                    ("Demo_MachineLoop", MachineLoopRecipe(), true),
                    ("Demo_UI_Click", SingleGenerator("UI_Click", UiClick()), false),
                    ("Demo_UI_Confirm", ConfirmRecipe(), false),
                    ("Demo_UI_Error", SingleGenerator("UI_Error", UiError()), false),
                    ("Demo_Upgrade", SingleGenerator("Upgrade", GeneratorPresets.Upgrade()), false),
                };
                foreach (var (name, data, _) in recipes) SaveRecipe(name, data);
                AssetDatabase.SaveAssets();

                EditorUtility.DisplayProgressBar("Audio Creation Studio", "Rendering and exporting final WAVs…", 0.6f);
                var results = new List<ExportResult>();
                foreach (var (name, data, loop) in recipes) results.Add(ExportRecipe(data, loop));
                AssetDatabase.Refresh();

                EditorUtility.DisplayProgressBar("Audio Creation Studio", "Creating demo scene…", 0.9f);
                BuildScene(results);
                return results;
            }
            finally { EditorUtility.ClearProgressBar(); }
        }

        private static void WriteSource(string name, AudioBuffer buffer, bool overwrite)
        {
            string path = $"{AudioFolder}/{name}.wav";
            if (!overwrite && File.Exists(path)) return;
            using (var fs = File.Create(path)) WavEncoder.Write(fs, buffer, WavSampleFormat.Pcm16);
        }

        private static AudioClip Clip(string name)
        {
            string path = $"{AudioFolder}/{name}.wav";
            AssetDatabase.ImportAsset(path);
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip == null) throw new InvalidOperationException($"Demo source {path} failed to import.");
            return clip;
        }

        private static void SaveRecipe(string name, RecipeData data)
        {
            string path = $"{RecipeFolder}/{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<AudioRecipe>(path);
            if (existing != null) { existing.data = data; EditorUtility.SetDirty(existing); return; }
            var a = ScriptableObject.CreateInstance<AudioRecipe>();
            a.data = data;
            AssetDatabase.CreateAsset(a, path);
        }

        public static ExportResult ExportRecipe(RecipeData data, bool loop)
        {
            var job = RenderRequestFactory.Create(data, SourceLibrary.Shared);
            var mix = job.Execute(new MixRenderer(), CancellationToken.None).Master;
            var opts = ExportOptions.From(data.export, loop);
            if (loop)
            {
                var l = data.loop;
                mix = SeamlessLoop.Create(mix, l.loopStart, l.loopEnd, (int)l.crossfadeSamples, l.crossfadeCurve);
            }
            string path = Path.GetFullPath(ExportService.MakePath(data.export.outputFolder, data.export.fileName));
            var r = ExportPipeline.ExportToFile(mix, opts, path);
            ExportService.ImportIfInProject(path);
            return r;
        }

        // =================================================================== Source synthesis (original audio)

        private static AudioBuffer Stereoize(AudioBuffer mono, float widthMs, int seed)
        {
            // Subtle Haas-style width so stereo sources are genuinely stereo.
            int d = (int)(widthMs / 1000f * mono.SampleRate);
            var st = new AudioBuffer(2, mono.Frames, mono.SampleRate);
            var m = mono.GetChannel(0);
            var l = st.GetChannel(0);
            var r = st.GetChannel(1);
            for (int i = 0; i < mono.Frames; i++)
            {
                l[i] = m[i];
                r[i] = i >= d ? m[i - d] * 0.92f + m[i] * 0.08f : m[i];
            }
            return st;
        }

        private static AudioBuffer Mix(params (AudioBuffer b, float gain)[] parts)
        {
            int frames = 0;
            foreach (var p in parts) frames = Math.Max(frames, p.b.Frames);
            var dst = new AudioBuffer(1, frames, SR);
            foreach (var (b, g) in parts) dst.AddFrom(b, 0, 0, b.Frames, g);
            return dst;
        }

        public static AudioBuffer MotorLoop()
        {
            // 2.0 s: 50 Hz saw body + tremolo at 25 Hz; integer cycles keep it naturally loopable.
            var body = ProceduralSynth.Render(new GeneratorSettings
            {
                waveform = OscillatorWaveform.Sawtooth, frequency = 50f, durationSeconds = 2f, volumeDb = -8f, oneShot = false,
                attack = 0.0005f, decay = 0f, sustain = 1f, release = 0.0005f, tremoloRate = 25f, tremoloDepth = 0.45f,
                noiseMix = 0.18f, noiseColor = NoiseColor.Pink, filterType = GeneratorFilterType.LowPass, filterCutoff = 1200f, drive = 0.35f, seed = 11
            }, SR);
            var hum = ProceduralSynth.Render(new GeneratorSettings
            {
                waveform = OscillatorWaveform.Square, dutyCycle = 0.3f, frequency = 100f, durationSeconds = 2f, volumeDb = -20f, oneShot = false,
                attack = 0.0005f, decay = 0f, sustain = 1f, release = 0.0005f, filterType = GeneratorFilterType.LowPass, filterCutoff = 700f, seed = 12
            }, SR);
            return Stereoize(Mix((body, 1f), (hum, 1f)), 0.6f, 1);
        }

        public static AudioBuffer BladeRotation()
        {
            var whine = ProceduralSynth.Render(new GeneratorSettings
            {
                waveform = OscillatorWaveform.Sawtooth, frequency = 780f, durationSeconds = 2f, volumeDb = -14f, oneShot = false,
                attack = 0.0005f, decay = 0f, sustain = 1f, release = 0.0005f, vibratoRate = 25f, vibratoDepth = 0.15f,
                filterType = GeneratorFilterType.BandPass, filterCutoff = 2600f, filterResonance = 1.2f, seed = 21
            }, SR);
            var air = ProceduralSynth.Render(new GeneratorSettings
            {
                waveform = OscillatorWaveform.PinkNoise, durationSeconds = 2f, volumeDb = -6f, oneShot = false,
                attack = 0.0005f, decay = 0f, sustain = 1f, release = 0.0005f, tremoloRate = 25f, tremoloDepth = 0.6f,
                filterType = GeneratorFilterType.BandPass, filterCutoff = 4200f, filterResonance = 0.9f, seed = 22
            }, SR);
            return Stereoize(Mix((whine, 1f), (air, 1f)), 1.1f, 2);
        }

        public static AudioBuffer WoodCutting()
        {
            var rasp = ProceduralSynth.Render(new GeneratorSettings
            {
                waveform = OscillatorWaveform.PinkNoise, durationSeconds = 1.5f, volumeDb = 2f, oneShot = false,
                attack = 0.06f, decay = 0.2f, sustain = 0.75f, release = 0.3f, tremoloRate = 25f, tremoloDepth = 0.85f,
                filterType = GeneratorFilterType.BandPass, filterCutoff = 1500f, filterResonance = 1.1f, filterLfoRate = 3.5f, filterLfoOctaves = 0.6f,
                drive = 0.4f, seed = 31
            }, SR);
            var grind = ProceduralSynth.Render(new GeneratorSettings
            {
                waveform = OscillatorWaveform.Sawtooth, frequency = 160f, durationSeconds = 1.5f, volumeDb = -16f, oneShot = false,
                attack = 0.08f, decay = 0.2f, sustain = 0.7f, release = 0.3f, noiseFm = 4f, filterType = GeneratorFilterType.LowPass, filterCutoff = 900f, seed = 32
            }, SR);
            return Stereoize(Mix((rasp, 1f), (grind, 1f)), 0.8f, 3);
        }

        public static AudioBuffer WoodCrack()
        {
            var snap = ProceduralSynth.Render(new GeneratorSettings
            {
                waveform = OscillatorWaveform.WhiteNoise, durationSeconds = 0.6f, volumeDb = -2f, oneShot = true,
                attack = 0.0005f, decay = 0.07f, sustain = 0.08f, release = 0.25f,
                filterType = GeneratorFilterType.BandPass, filterCutoff = 2200f, filterResonance = 0.8f, filterEnvOctaves = 1.5f, drive = 0.5f, seed = 41
            }, SR);
            var thump = ProceduralSynth.Render(new GeneratorSettings
            {
                waveform = OscillatorWaveform.Sine, frequency = 110f, durationSeconds = 0.6f, volumeDb = -4f, oneShot = true,
                attack = 0.0005f, decay = 0.12f, sustain = 0f, release = 0.12f, pitchStart = 12f, pitchEnd = 0f, pitchSweepTime = 0.05f, pitchCurve = 0.5f, seed = 42
            }, SR);
            return Mix((snap, 1f), (thump, 1f));
        }

        public static AudioBuffer MachineRaw()
        {
            // 4 s of machine noise with random speed wobble: not periodic, so it needs a crossfaded loop.
            var s = GeneratorPresets.MachineRunning();
            s.durationSeconds = 4f;
            s.frequency = 47f;
            s.noiseFm = 0.8f;
            s.attack = 0.0005f;
            s.release = 0.0005f;
            s.seed = 51;
            var body = ProceduralSynth.Render(s, SR);
            var clank = ProceduralSynth.Render(new GeneratorSettings
            {
                waveform = OscillatorWaveform.PinkNoise, durationSeconds = 4f, volumeDb = -12f, oneShot = false, attack = 0.0005f, decay = 0f, sustain = 1f, release = 0.0005f,
                tremoloRate = 4.7f, tremoloDepth = 0.9f, filterType = GeneratorFilterType.HighPass, filterCutoff = 2500f, seed = 52
            }, SR);
            return Stereoize(Mix((body, 1f), (clank, 1f)), 0.7f, 5);
        }

        // =================================================================== Recipes

        private static RecipeData NewRecipe(string name, string exportName, bool normalize = false)
        {
            var r = new RecipeData { name = name, sampleRate = SR, channels = OutputChannelMode.Stereo };
            r.export.fileName = exportName;
            r.export.outputFolder = ExportFolder;
            r.export.format = WavSampleFormat.Pcm16;
            // Explicit, visible export option (the mix itself is never normalized).
            r.export.normalize = normalize;
            r.export.normalizeTargetDb = -1f;
            return r;
        }

        private static long S(double seconds) => SampleTime.SecondsToSamples(seconds, SR);

        private static ClipData AddClip(TrackData t, AudioClip clip, double start, double length, double offset = 0, bool loop = false)
        {
            SourceLibrary.Shared.TryGetClip(clip, out var buf, out _, out var err);
            if (buf == null) throw new InvalidOperationException(err);
            var c = TimelineEditing.CreateClipFromSource(clip, buf.SampleRate, buf.Frames, buf.Channels, S(start));
            c.sourceOffset = SampleTime.SecondsToSamples(offset, buf.SampleRate);
            c.loop = loop;
            c.lengthSamples = S(length);
            if (!loop) c.lengthSamples = Math.Min(c.lengthSamples, c.MaxLengthFromOffset(SR));
            t.clips.Add(c);
            return c;
        }

        private static EffectData Fx(string type, params (string id, float v)[] values)
        {
            var e = EffectRegistry.CreateData(type);
            var def = EffectRegistry.Get(type);
            foreach (var (id, v) in values) e.values[def.IndexOf(id)] = v;
            return e;
        }

        public static RecipeData SawMachineRecipe()
        {
            var r = NewRecipe("Saw Machine", "SawMachine_Final");
            var motor = TimelineEditing.AddTrack(r, "Motor Loop");
            var c1 = AddClip(motor, Clip("SawMotor_Loop"), 0, 3.3, 0, true);
            c1.gainDb = -3f; c1.fadeIn.lengthSamples = S(0.25); c1.fadeOut.lengthSamples = S(0.45);
            motor.effects.Add(Fx(EffectTypes.LowPass, ("cutoff", 2400f), ("q", 0.8f)));

            var blade = TimelineEditing.AddTrack(r, "Blade Rotation");
            var c2 = AddClip(blade, Clip("SawBlade_Rotation"), 0.12, 3.1, 0.3, true);
            c2.gainDb = -8f; c2.pan = 0.25f; c2.fadeIn.lengthSamples = S(0.5); c2.fadeIn.curve = FadeCurve.SCurve; c2.fadeOut.lengthSamples = S(0.6);
            blade.effects.Add(Fx(EffectTypes.HighPass, ("cutoff", 350f)));
            blade.effects.Add(Fx(EffectTypes.Equalizer, ("mid2Freq", 3200f), ("mid2Gain", 3.5f), ("highGain", -4f)));
            var lane = blade.GetOrCreateLane(AutomationTargets.Gain);
            lane.points.Add(new AutomationPoint(S(0.0), -24f, AutomationInterpolation.Smooth));
            lane.points.Add(new AutomationPoint(S(0.7), -2f, AutomationInterpolation.Linear));
            lane.points.Add(new AutomationPoint(S(2.8), -2f, AutomationInterpolation.Smooth));
            lane.points.Add(new AutomationPoint(S(3.2), -30f, AutomationInterpolation.Linear));
            blade.showAutomation = true;

            var cut = TimelineEditing.AddTrack(r, "Wood Cutting");
            var c3 = AddClip(cut, Clip("Wood_Cutting"), 0.85, 1.25, 0.1);
            c3.gainDb = -3f; c3.pan = -0.15f; c3.fadeIn.lengthSamples = S(0.06); c3.fadeOut.lengthSamples = S(0.25);
            cut.effects.Add(Fx(EffectTypes.Distortion, ("drive", 8f), ("mix", 0.35f), ("output", -2f)));
            cut.effects.Add(Fx(EffectTypes.Compressor, ("threshold", -20f), ("ratio", 3f), ("attack", 5f), ("release", 90f), ("makeup", 3f)));

            var crack = TimelineEditing.AddTrack(r, "Wood Crack");
            var c4 = AddClip(crack, Clip("Wood_Crack"), 2.08, 0.6);
            c4.gainDb = -1f; c4.fadeOut.lengthSamples = S(0.08);
            crack.effects.Add(Fx(EffectTypes.Reverb, ("size", 0.45f), ("mix", 0.18f), ("predelay", 8f)));

            r.masterEffects.Add(Fx(EffectTypes.Compressor, ("threshold", -14f), ("ratio", 2.5f), ("attack", 8f), ("release", 150f)));
            r.masterEffects.Add(Fx(EffectTypes.Limiter, ("ceiling", -1f)));
            r.EnsureValid();
            return r;
        }

        public static RecipeData CoinRecipe()
        {
            var r = NewRecipe("Coin Pickup", "CoinPickup_Final", true);
            var coin = TimelineEditing.AddTrack(r, "Coin");
            var g = GeneratorPresets.CoinPickup();
            g.seed = 7;
            coin.clips.Add(TimelineEditing.CreateGeneratorClip(g, SR, 0));
            var sparkle = TimelineEditing.AddTrack(r, "Sparkle");
            var sp = new GeneratorSettings
            {
                presetName = "Sparkle", waveform = OscillatorWaveform.Sine, frequency = 2637f, durationSeconds = 0.3f, volumeDb = -16f, oneShot = true,
                attack = 0.001f, decay = 0.08f, sustain = 0.2f, release = 0.2f, pitchJump = 5f, pitchJumpTime = 0.06f, vibratoRate = 18f, vibratoDepth = 0.3f, seed = 8
            };
            var sc = TimelineEditing.CreateGeneratorClip(sp, SR, S(0.07));
            sc.pan = 0.3f;
            sparkle.clips.Add(sc);
            r.masterEffects.Add(Fx(EffectTypes.Delay, ("time", 95f), ("feedback", 0.28f), ("mix", 0.16f), ("pingpong", 1f)));
            r.masterEffects.Add(Fx(EffectTypes.Limiter, ("ceiling", -1f)));
            r.EnsureValid();
            return r;
        }

        public static RecipeData MachineLoopRecipe()
        {
            var r = NewRecipe("Machine Loop", "MachineLoop_Final");
            var t = TimelineEditing.AddTrack(r, "Machine");
            var src = Clip("Machine_Raw");
            AddClip(t, src, 0, 4.0);
            t.effects.Add(Fx(EffectTypes.LowPass, ("cutoff", 5000f)));
            SourceLibrary.Shared.TryGetClip(src, out var buf, out _, out _);
            long start = ZeroCrossing.FindNearest(buf, S(0.6), (int)S(0.005));
            long end = ZeroCrossing.FindNearest(buf, S(3.6), (int)S(0.005));
            r.loop = new LoopSettings
            {
                source = LoopSourceKind.RenderedMix, loopStart = start, loopEnd = end, crossfadeSamples = S(0.15),
                crossfadeCurve = FadeCurve.EqualPower, snapToZeroCrossing = true, zeroSearchMs = 5f
            };
            r.loopRegion = new TimelineRegion { enabled = true, start = start, end = end };
            r.EnsureValid();
            return r;
        }

        private static RecipeData SingleGenerator(string exportName, GeneratorSettings g)
        {
            var r = NewRecipe(exportName.Replace('_', ' '), exportName, true);
            var t = TimelineEditing.AddTrack(r, g.presetName);
            t.clips.Add(TimelineEditing.CreateGeneratorClip(g, SR, 0));
            r.masterEffects.Add(Fx(EffectTypes.Limiter, ("ceiling", -1f)));
            r.EnsureValid();
            return r;
        }

        private static GeneratorSettings UiClick()
        {
            var g = GeneratorPresets.UiClick();
            g.presetName = "UI Click";
            return g;
        }

        private static GeneratorSettings UiError()
        {
            return new GeneratorSettings
            {
                presetName = "UI Error", waveform = OscillatorWaveform.Square, dutyCycle = 0.5f, frequency = 233f, durationSeconds = 0.34f, volumeDb = -14f,
                oneShot = false, attack = 0.002f, decay = 0.05f, sustain = 0.8f, release = 0.08f, pitchJump = -4f, pitchJumpTime = 0.16f,
                tremoloRate = 12f, tremoloDepth = 1f, filterType = GeneratorFilterType.LowPass, filterCutoff = 1800f, seed = 3
            };
        }

        private static RecipeData ConfirmRecipe()
        {
            var r = NewRecipe("UI Confirm", "UI_Confirm", true);
            var a = TimelineEditing.AddTrack(r, "Tone 1");
            var ga = new GeneratorSettings
            {
                presetName = "Confirm Low", waveform = OscillatorWaveform.Triangle, frequency = 1046.5f, durationSeconds = 0.16f, volumeDb = -6f, oneShot = true,
                attack = 0.002f, decay = 0.06f, sustain = 0.4f, release = 0.09f, seed = 4
            };
            a.clips.Add(TimelineEditing.CreateGeneratorClip(ga, SR, 0));
            var b = TimelineEditing.AddTrack(r, "Tone 2");
            var gb = ga.Clone();
            gb.presetName = "Confirm High";
            gb.frequency = 1568f;
            gb.durationSeconds = 0.28f;
            gb.release = 0.2f;
            b.clips.Add(TimelineEditing.CreateGeneratorClip(gb, SR, S(0.075)));
            r.masterEffects.Add(Fx(EffectTypes.Reverb, ("size", 0.15f), ("mix", 0.1f)));
            r.masterEffects.Add(Fx(EffectTypes.Limiter, ("ceiling", -1f)));
            r.EnsureValid();
            return r;
        }

        // =================================================================== Scene

        private static void BuildScene(List<ExportResult> results)
        {
            var current = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                var cam = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
                cam.tag = "MainCamera";
                var c = cam.GetComponent<Camera>();
                c.clearFlags = CameraClearFlags.SolidColor;
                c.backgroundColor = new Color(0.08f, 0.09f, 0.12f);
                SceneManager.MoveGameObjectToScene(cam, scene);
                var go = new GameObject("Demo SFX Player", typeof(AudioSource), typeof(DemoSfxPlayer));
                SceneManager.MoveGameObjectToScene(go, scene);
                var player = go.GetComponent<DemoSfxPlayer>();
                player.sounds.Clear();
                foreach (var r in results)
                {
                    string data = Application.dataPath.Replace('\\', '/');
                    string rel = "Assets" + r.Path.Replace('\\', '/').Substring(data.Length);
                    var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(rel);
                    string file = Path.GetFileNameWithoutExtension(r.Path);
                    player.sounds.Add(new DemoSfxPlayer.Entry { label = file.Replace("_Final", "").Replace('_', ' '), clip = clip, loop = file.Contains("Loop") });
                }
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            finally
            {
                if (scene != current) EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
