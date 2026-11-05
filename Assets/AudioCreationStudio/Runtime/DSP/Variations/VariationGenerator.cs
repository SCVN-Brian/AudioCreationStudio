using System;
using System.Collections.Generic;
using AudioCreationStudio.Models;

namespace AudioCreationStudio.DSP
{
    public sealed class VariationInfo
    {
        public int Index;
        public int Seed;
        public float PitchSemitones;
        public float VolumeDb;
        public List<string> DroppedTracks = new List<string>();
        public string Summary;
    }

    /// <summary>
    /// Produces deterministic variants of a recipe. Variant <c>i</c> of seed <c>s</c> is always identical.
    /// Pitch is applied as a playback-rate change (clip lengths are compensated so the same source
    /// region plays); per-clip timing jitter and source-offset changes are bounded; effect parameters
    /// move by a fraction of their range.
    /// </summary>
    public static class VariationGenerator
    {
        public static RecipeData CreateVariant(RecipeData baseRecipe, VariationSettings v, int index, out VariationInfo info)
        {
            int seed = DeterministicRandom.DeriveSeed(v.seed, index);
            var rng = new DeterministicRandom(seed);
            var r = baseRecipe.Clone();
            int sr = r.sampleRate;
            info = new VariationInfo { Index = index, Seed = seed };

            float pitch = rng.Range(-v.pitchSemitones, v.pitchSemitones);
            float vol = rng.Range(-v.volumeDb, v.volumeDb);
            info.PitchSemitones = pitch;
            info.VolumeDb = vol;
            double ratio = DspMath.SemitonesToRatio(pitch);
            r.masterGainDb += vol;

            // Layer selection: drop tracks with probability, but always keep at least one audible track.
            var candidates = new List<TrackData>();
            foreach (var t in r.tracks) if (!t.mute && t.clips.Count > 0) candidates.Add(t);
            var dropped = new List<TrackData>();
            foreach (var t in candidates) if (v.layerDropProbability > 0 && rng.Chance(v.layerDropProbability)) dropped.Add(t);
            if (dropped.Count >= candidates.Count && dropped.Count > 0) dropped.RemoveAt(rng.Range(0, dropped.Count));
            foreach (var t in dropped) { t.mute = true; t.solo = false; info.DroppedTracks.Add(t.name); }

            long jitterMax = SampleTime.SecondsToSamples(v.timingJitterMs / 1000.0, sr);
            foreach (var t in r.tracks)
            {
                foreach (var c in t.clips)
                {
                    long originalStart = c.startSample;
                    c.playbackRate *= ratio;
                    c.lengthSamples = Math.Max(1, (long)Math.Round(c.lengthSamples / ratio));
                    c.fadeIn.lengthSamples = (long)Math.Round(c.fadeIn.lengthSamples / ratio);
                    c.fadeOut.lengthSamples = (long)Math.Round(c.fadeOut.lengthSamples / ratio);
                    // Clips starting at 0 stay anchored so the attack is not delayed.
                    long scaledStart = (long)Math.Round(originalStart / ratio);
                    long jitter = jitterMax > 0 && originalStart > 0 ? (long)Math.Round(rng.Range(-1.0, 1.0) * jitterMax) : 0;
                    c.startSample = Math.Max(0, scaledStart + jitter);
                    if (v.startOffsetMs > 0 && c.sourceKind == ClipSourceKind.AudioClip)
                    {
                        long offMax = SampleTime.SecondsToSamples(v.startOffsetMs / 1000.0, c.sourceSampleRate);
                        long delta = (long)Math.Round(rng.NextDouble() * offMax);
                        long maxOffset = Math.Max(0, c.sourceFrames - 1);
                        c.sourceOffset = Math.Min(maxOffset, c.sourceOffset + delta);
                        c.lengthSamples = Math.Max(1, Math.Min(c.lengthSamples, c.MaxLengthFromOffset(sr)));
                    }
                    foreach (var lane in c.automation)
                        for (int i = 0; i < lane.points.Count; i++)
                        {
                            var p = lane.points[i];
                            p.time = (long)Math.Round(p.time / ratio);
                            lane.points[i] = p;
                        }
                }
                VaryEffects(t.effects, rng, v.effectVariation);
            }
            VaryEffects(r.masterEffects, rng, v.effectVariation);

            info.Summary = $"{(pitch >= 0 ? "+" : "")}{pitch:0.00} st · {(vol >= 0 ? "+" : "")}{vol:0.0} dB" +
                           (info.DroppedTracks.Count > 0 ? $" · −{string.Join(", ", info.DroppedTracks)}" : "");
            r.name = $"{baseRecipe.name}_Var{index + 1:00}";
            return r;
        }

        private static void VaryEffects(List<EffectData> effects, DeterministicRandom rng, float amount)
        {
            if (amount <= 0) return;
            foreach (var e in effects)
            {
                var def = EffectRegistry.Get(e.typeId);
                if (def == null) continue;
                e.values = def.Normalize(e.values);
                for (int i = 0; i < def.Params.Length; i++)
                {
                    var p = def.Params[i];
                    if (p.IsChoice) continue;
                    float n = p.ToNormalized(e.values[i]) + rng.Range(-amount, amount) * 0.5f;
                    e.values[i] = p.FromNormalized(n);
                }
            }
        }
    }
}
