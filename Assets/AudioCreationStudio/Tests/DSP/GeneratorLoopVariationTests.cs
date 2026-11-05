using System;
using AudioCreationStudio.DSP;
using AudioCreationStudio.Editing;
using AudioCreationStudio.Models;
using NUnit.Framework;
using UnityEngine;
using static AudioCreationStudio.Tests.TestUtil;

namespace AudioCreationStudio.Tests
{
    public class GeneratorLoopVariationTests
    {
        [Test]
        public void Generator_IsDeterministicForSameSettingsAndSeed()
        {
            foreach (var p in GeneratorPresets.All)
            {
                var a = ProceduralSynth.Render(p.Create(), SR);
                var b = ProceduralSynth.Render(p.Create(), SR);
                Assert.AreEqual(a.Frames, b.Frames);
                for (int i = 0; i < a.Frames; i++) Assert.AreEqual(a[0, i], b[0, i], 0f, $"{p.Name} frame {i}");
            }
        }

        [Test]
        public void Generator_SeedChangesNoise()
        {
            var s = GeneratorPresets.Whoosh();
            var a = ProceduralSynth.Render(s, SR);
            s.seed = 99;
            var b = ProceduralSynth.Render(s, SR);
            double diff = 0;
            for (int i = 0; i < a.Frames; i++) diff += Math.Abs(a[0, i] - b[0, i]);
            Assert.Greater(diff, 1.0);
        }

        [Test]
        public void Generator_AllPresetsAreAudibleBoundedAndSized()
        {
            foreach (var p in GeneratorPresets.All)
            {
                var s = p.Create();
                var b = ProceduralSynth.Render(s, SR);
                Assert.AreEqual((int)Math.Round(s.durationSeconds * SR), b.Frames, p.Name);
                float peak = b.GetPeak();
                Assert.Greater(peak, 0.01f, $"{p.Name} is silent");
                Assert.Less(peak, 1.0f, $"{p.Name} clips");
                Assert.AreEqual(0f, b[0, b.Frames - 1], 1e-3f, $"{p.Name} must end at silence (no click)");
            }
        }

        [Test]
        public void Generator_SineFrequencyIsAccurate()
        {
            var s = new GeneratorSettings { waveform = OscillatorWaveform.Sine, frequency = 1000, durationSeconds = 1f, volumeDb = 0, oneShot = false, attack = 0.0005f, decay = 0, sustain = 1, release = 0.0005f };
            var b = ProceduralSynth.Render(s, SR);
            int crossings = 0;
            for (int i = 1; i < b.Frames; i++) if (b[0, i - 1] < 0 && b[0, i] >= 0) crossings++;
            Assert.AreEqual(1000, crossings, 2);
        }

        [Test]
        public void Randomize_IsDeterministic()
        {
            var a = GeneratorRandomizer.Randomize(GeneratorPresets.Impact(), 42);
            var b = GeneratorRandomizer.Randomize(GeneratorPresets.Impact(), 42);
            Assert.AreEqual(JsonUtility.ToJson(a), JsonUtility.ToJson(b));
            Assert.AreNotEqual(JsonUtility.ToJson(a), JsonUtility.ToJson(GeneratorRandomizer.Randomize(GeneratorPresets.Impact(), 43)));
        }

        [Test]
        public void SeamlessLoop_PreRoll_PreservesLengthAndRemovesJump()
        {
            // 441 Hz sine does not fit an integer number of cycles in the region → raw cut clicks.
            var src = Sine(441.3, 48000, 0.5f);
            long s = 10000, e = 30000;
            var raw = src.Slice((int)s, (int)(e - s));
            var loop = SeamlessLoop.Create(src, s, e, 2400, FadeCurve.EqualPower);
            Assert.AreEqual(e - s, loop.Frames, "pre-roll mode keeps the loop length");
            var rr = SeamlessLoop.AnalyzeBoundary(raw);
            var lr = SeamlessLoop.AnalyzeBoundary(loop);
            Assert.Greater(rr.JumpRatio, 3f, "raw cut should show a discontinuity");
            Assert.Less(lr.JumpRatio, 3f, "crossfaded loop should be continuous");
            Assert.AreEqual(LoopVerdict.Seamless, lr.Verdict);
        }

        [Test]
        public void SeamlessLoop_OverlapMode_ShortensByCrossfade()
        {
            var src = Sine(441.3, 20000, 0.5f);
            var loop = SeamlessLoop.Create(src, 0, 20000, 1000, FadeCurve.Linear);
            Assert.AreEqual(19000, loop.Frames);
            Assert.Less(SeamlessLoop.AnalyzeBoundary(loop).JumpRatio, 3f);
        }

        [Test]
        public void ZeroCrossing_FindsNearestRisingCrossing()
        {
            var src = Sine(100, 4800, 0.5f); // period 480 samples, rising crossings at multiples of 480
            Assert.AreEqual(960, ZeroCrossing.FindNearest(src, 950, 100), 1);
        }

        [Test]
        public void ClickDetector_FindsIsolatedClick_IgnoresCleanSine()
        {
            var src = Sine(300, 20000, 0.4f);
            Assert.AreEqual(0, SignalAnalysis.DetectClicks(src.GetChannel(0), src.Frames).Count);
            src[0, 10000] += 0.6f;
            Assert.GreaterOrEqual(SignalAnalysis.DetectClicks(src.GetChannel(0), src.Frames).Count, 1);
        }

        private static RecipeData VariationRecipe()
        {
            var r = new RecipeData();
            for (int i = 0; i < 3; i++)
            {
                var t = TimelineEditing.AddTrack(r);
                t.clips.Add(TimelineEditing.CreateGeneratorClip(GeneratorPresets.Pop(), r.sampleRate, i * 2400));
                t.effects.Add(EffectRegistry.CreateData(EffectTypes.LowPass));
            }
            return r;
        }

        [Test]
        public void Variations_AreDeterministicAndDistinct()
        {
            var r = VariationRecipe();
            var v = new VariationSettings { count = 4, seed = 77, pitchSemitones = 2, volumeDb = 3, timingJitterMs = 10, effectVariation = 0.2f };
            var a = VariationGenerator.CreateVariant(r, v, 1, out var ia);
            var b = VariationGenerator.CreateVariant(r, v, 1, out var ib);
            var c = VariationGenerator.CreateVariant(r, v, 2, out _);
            Assert.AreEqual(JsonUtility.ToJson(a), JsonUtility.ToJson(b));
            Assert.AreNotEqual(JsonUtility.ToJson(a), JsonUtility.ToJson(c));
            Assert.AreEqual(ia.PitchSemitones, ib.PitchSemitones);
            Assert.LessOrEqual(Math.Abs(ia.PitchSemitones), 2f);
            Assert.AreEqual(2400, r.tracks[1].clips[0].startSample, "base recipe must not be modified");
        }

        [Test]
        public void Variations_AlwaysKeepAtLeastOneLayer()
        {
            var r = VariationRecipe();
            var v = new VariationSettings { count = 20, seed = 5, layerDropProbability = 0.9f };
            for (int i = 0; i < 20; i++)
            {
                var x = VariationGenerator.CreateVariant(r, v, i, out _);
                Assert.IsTrue(x.tracks.Exists(t => !t.mute), $"variant {i} dropped every layer");
            }
        }
    }
}
