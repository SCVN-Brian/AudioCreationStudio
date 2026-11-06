using System;
using System.Collections.Generic;
using AudioCreationStudio.DSP;
using AudioCreationStudio.Models;
using UnityEngine;

namespace AudioCreationStudio.Editing
{
    /// <summary>
    /// Pure, non-destructive timeline operations on <see cref="RecipeData"/>. No Unity editor dependency, so
    /// every operation is unit-testable. Callers are responsible for undo recording.
    /// </summary>
    public static class TimelineEditing
    {
        public const long MinClipLength = 64;

        public static readonly Color[] TrackPalette =
        {
            new Color32(0x5B, 0x9D, 0xFF, 0xFF), new Color32(0x42, 0xC9, 0xA0, 0xFF), new Color32(0xF0, 0xB3, 0x5A, 0xFF),
            new Color32(0xC0, 0x84, 0xFC, 0xFF), new Color32(0xF0, 0x6C, 0x78, 0xFF), new Color32(0x4F, 0xD1, 0xE8, 0xFF),
            new Color32(0xE8, 0x8A, 0x4F, 0xFF), new Color32(0x9A, 0xD8, 0x5B, 0xFF)
        };

        public static TrackData AddTrack(RecipeData r, string name = null, int index = -1)
        {
            var t = new TrackData
            {
                uid = Uid.New(),
                name = name ?? $"Track {r.tracks.Count + 1}",
                color = TrackPalette[r.tracks.Count % TrackPalette.Length]
            };
            if (index < 0 || index > r.tracks.Count) r.tracks.Add(t); else r.tracks.Insert(index, t);
            return t;
        }

        public static TrackData DuplicateTrack(RecipeData r, TrackData t)
        {
            var copy = t.Clone(true);
            copy.name = t.name + " Copy";
            copy.solo = false;
            r.tracks.Insert(r.tracks.IndexOf(t) + 1, copy);
            return copy;
        }

        public static void DeleteTrack(RecipeData r, TrackData t) => r.tracks.Remove(t);

        public static void MoveTrack(RecipeData r, TrackData t, int newIndex)
        {
            int old = r.tracks.IndexOf(t);
            if (old < 0) return;
            newIndex = Mathf.Clamp(newIndex, 0, r.tracks.Count - 1);
            if (old == newIndex) return;
            r.tracks.RemoveAt(old);
            r.tracks.Insert(newIndex, t);
        }

        public static ClipData CreateClipFromSource(AudioClip clip, int sourceRate, long sourceFrames, int sourceChannels, long start)
        {
            return new ClipData
            {
                uid = Uid.New(),
                sourceKind = ClipSourceKind.AudioClip,
                sourceClip = clip,
                sourceSampleRate = sourceRate,
                sourceFrames = sourceFrames,
                sourceChannels = sourceChannels,
                startSample = Math.Max(0, start),
                fadeIn = new FadeData(),
                fadeOut = new FadeData()
            };
        }

        public static ClipData CreateGeneratorClip(GeneratorSettings settings, int recipeRate, long start)
        {
            long frames = Math.Max(1, (long)Math.Round(DspMath.Clamp(settings.durationSeconds, 0.005f, ProceduralSynth.MaxDurationSeconds) * recipeRate));
            return new ClipData
            {
                uid = Uid.New(),
                name = settings.presetName,
                sourceKind = ClipSourceKind.Generator,
                generator = settings.Clone(),
                sourceSampleRate = recipeRate,
                sourceFrames = frames,
                sourceChannels = 1,
                startSample = Math.Max(0, start),
                lengthSamples = frames,
                fadeIn = new FadeData(),
                fadeOut = new FadeData()
            };
        }

        /// <summary>Sets the clip length to the full source duration at the current playback rate.</summary>
        public static void FitLengthToSource(ClipData c, int recipeRate)
        {
            c.lengthSamples = Math.Max(MinClipLength, (long)Math.Floor(Math.Max(0, c.sourceFrames - c.sourceOffset) / c.SourceStep(recipeRate)));
        }

        public static void MoveClip(ClipData c, long newStart) => c.startSample = Math.Max(0, newStart);

        /// <summary>Moves the left edge to <paramref name="newStart"/> while keeping the audio anchored in time.</summary>
        public static void TrimStart(ClipData c, long newStart, int recipeRate)
        {
            long end = c.EndSample;
            newStart = Math.Max(0, Math.Min(newStart, end - MinClipLength));
            long delta = newStart - c.startSample;
            double step = c.SourceStep(recipeRate);
            long newOffset = c.sourceOffset + (long)Math.Round(delta * step);
            if (!c.loop && newOffset < 0)
            {
                // Can't reveal audio before the source start.
                long maxBack = (long)Math.Floor(c.sourceOffset / step);
                newStart = c.startSample - maxBack;
                delta = newStart - c.startSample;
                newOffset = c.sourceOffset + (long)Math.Round(delta * step);
                newOffset = Math.Max(0, newOffset);
            }
            if (c.loop && c.sourceFrames > 0) newOffset = DspMath.Mod(newOffset, c.sourceFrames);
            ShiftAutomation(c.automation, -delta);
            c.sourceOffset = newOffset;
            c.startSample = newStart;
            c.lengthSamples = end - newStart;
            ClampFades(c);
        }

        public static void TrimEnd(ClipData c, long newEnd, int recipeRate)
        {
            long len = Math.Max(MinClipLength, newEnd - c.startSample);
            len = Math.Min(len, c.MaxLengthFromOffset(recipeRate));
            c.lengthSamples = Math.Max(MinClipLength, len);
            ClampFades(c);
        }

        public static void SetFadeIn(ClipData c, long length)
        {
            c.fadeIn.lengthSamples = DspMath.Clamp(length, 0, c.lengthSamples - c.fadeOut.lengthSamples);
        }

        public static void SetFadeOut(ClipData c, long length)
        {
            c.fadeOut.lengthSamples = DspMath.Clamp(length, 0, c.lengthSamples - c.fadeIn.lengthSamples);
        }

        public static void ClampFades(ClipData c)
        {
            c.fadeIn.lengthSamples = DspMath.Clamp(c.fadeIn.lengthSamples, 0, c.lengthSamples);
            c.fadeOut.lengthSamples = DspMath.Clamp(c.fadeOut.lengthSamples, 0, c.lengthSamples - c.fadeIn.lengthSamples);
        }

        /// <summary>Splits a clip at timeline sample <paramref name="at"/>. Returns the new right-hand clip, or null.</summary>
        public static ClipData Split(TrackData track, ClipData c, long at, int recipeRate)
        {
            if (at <= c.startSample + MinClipLength / 2 || at >= c.EndSample - MinClipLength / 2) return null;
            var right = c.Clone(true);
            long leftLen = at - c.startSample;
            double step = c.SourceStep(recipeRate);
            right.startSample = at;
            right.sourceOffset = c.sourceOffset + (long)Math.Round(leftLen * step);
            if (c.loop && c.sourceFrames > 0) right.sourceOffset = DspMath.Mod(right.sourceOffset, c.sourceFrames);
            right.lengthSamples = c.EndSample - at;
            right.fadeIn = new FadeData { curve = c.fadeIn.curve };
            c.lengthSamples = leftLen;
            c.fadeOut = new FadeData { curve = c.fadeOut.curve };
            ClampFades(c);
            ClampFades(right);

            // Split automation: left keeps points before the cut, right gets the rest shifted.
            for (int i = 0; i < c.automation.Count; i++)
            {
                var lane = c.automation[i];
                var rlane = right.automation[i];
                float cutValue = lane.Evaluate(leftLen, 0f);
                bool active = lane.IsActive;
                lane.points.RemoveAll(p => p.time > leftLen);
                rlane.points.RemoveAll(p => p.time < leftLen);
                for (int k = 0; k < rlane.points.Count; k++)
                {
                    var p = rlane.points[k]; p.time -= leftLen; rlane.points[k] = p;
                }
                if (active)
                {
                    if (lane.points.Count == 0 || lane.points[lane.points.Count - 1].time != leftLen) lane.points.Add(new AutomationPoint(leftLen, cutValue));
                    if (rlane.points.Count == 0 || rlane.points[0].time != 0) rlane.points.Insert(0, new AutomationPoint(0, cutValue));
                }
            }
            track.clips.Insert(track.clips.IndexOf(c) + 1, right);
            return right;
        }

        public static ClipData Duplicate(TrackData track, ClipData c)
        {
            var copy = c.Clone(true);
            copy.startSample = c.EndSample;
            track.clips.Add(copy);
            return copy;
        }

        public static ClipData Paste(TrackData track, ClipData template, long at)
        {
            var copy = template.Clone(true);
            copy.startSample = Math.Max(0, at);
            track.clips.Add(copy);
            return copy;
        }

        public static bool DeleteClip(RecipeData r, string clipUid)
        {
            var c = r.FindClip(clipUid, out var owner);
            return c != null && owner.clips.Remove(c);
        }

        public static void MoveClipToTrack(RecipeData r, ClipData c, TrackData target)
        {
            r.FindClip(c.uid, out var owner);
            if (owner == null || owner == target) return;
            owner.clips.Remove(c);
            target.clips.Add(c);
        }

        /// <summary>Changes the recipe sample rate, rescaling every timeline position so timing is preserved.</summary>
        public static void ChangeSampleRate(RecipeData r, int newRate)
        {
            int old = r.sampleRate;
            if (old == newRate || newRate < 8000) return;
            long S(long v) => SampleTime.Rescale(v, old, newRate);
            foreach (var t in r.tracks)
            {
                foreach (var c in t.clips)
                {
                    c.startSample = S(c.startSample);
                    c.lengthSamples = Math.Max(1, S(c.lengthSamples));
                    c.fadeIn.lengthSamples = S(c.fadeIn.lengthSamples);
                    c.fadeOut.lengthSamples = S(c.fadeOut.lengthSamples);
                    if (c.sourceKind == ClipSourceKind.Generator)
                    {
                        c.sourceOffset = SampleTime.Rescale(c.sourceOffset, c.sourceSampleRate, newRate);
                        c.sourceFrames = SampleTime.Rescale(c.sourceFrames, c.sourceSampleRate, newRate);
                        c.sourceSampleRate = newRate;
                    }
                    RescaleLanes(c.automation, old, newRate);
                }
                RescaleLanes(t.automation, old, newRate);
            }
            r.loopRegion.start = S(r.loopRegion.start);
            r.loopRegion.end = S(r.loopRegion.end);
            r.loop.crossfadeSamples = S(r.loop.crossfadeSamples);
            r.sampleRate = newRate;
        }

        private static void RescaleLanes(List<AutomationLane> lanes, int from, int to)
        {
            foreach (var l in lanes)
                for (int i = 0; i < l.points.Count; i++)
                {
                    var p = l.points[i]; p.time = SampleTime.Rescale(p.time, from, to); l.points[i] = p;
                }
        }

        // ================================================================== Trim tools (slip / slide / roll / ripple)

        /// <summary>Clips closer than this (in samples) count as touching for roll and slide.</summary>
        public const long AdjacencyTolerance = 2;

        /// <summary>Copies the editable placement of <paramref name="snapshot"/> back onto <paramref name="dst"/> (drags re-apply from their origin).</summary>
        public static void RestorePlacement(ClipData dst, ClipData snapshot)
        {
            dst.startSample = snapshot.startSample;
            dst.sourceOffset = snapshot.sourceOffset;
            dst.lengthSamples = snapshot.lengthSamples;
            dst.fadeIn = (snapshot.fadeIn ?? new FadeData()).Clone();
            dst.fadeOut = (snapshot.fadeOut ?? new FadeData()).Clone();
            dst.automation = new List<AutomationLane>();
            if (snapshot.automation != null) foreach (var l in snapshot.automation) dst.automation.Add(l.Clone());
        }

        /// <summary>The clip on the same track whose end touches <paramref name="c"/>'s start, or null.</summary>
        public static ClipData LeftNeighbor(TrackData t, ClipData c)
        {
            ClipData best = null;
            foreach (var o in t.clips)
                if (o != c && Math.Abs(o.EndSample - c.startSample) <= AdjacencyTolerance && o.startSample < c.startSample && (best == null || o.EndSample > best.EndSample))
                    best = o;
            return best;
        }

        /// <summary>The clip on the same track whose start touches <paramref name="c"/>'s end, or null.</summary>
        public static ClipData RightNeighbor(TrackData t, ClipData c)
        {
            ClipData best = null;
            foreach (var o in t.clips)
                if (o != c && Math.Abs(o.startSample - c.EndSample) <= AdjacencyTolerance && o.EndSample > c.EndSample && (best == null || o.startSample < best.startSample))
                    best = o;
            return best;
        }

        /// <summary>Valid <see cref="ClipData.sourceOffset"/> range for slipping (min, max). Looping clips can slip freely.</summary>
        public static void SlipRange(ClipData c, int recipeRate, out long min, out long max)
        {
            if (c.loop) { min = long.MinValue / 4; max = long.MaxValue / 4; return; }
            long needed = (long)Math.Ceiling(c.lengthSamples * c.SourceStep(recipeRate));
            min = 0;
            max = Math.Max(0, c.sourceFrames - needed);
        }

        /// <summary>
        /// Slip: changes which part of the source the clip shows without moving the clip, its length, fades or automation.
        /// The offset is clamped so the clip never runs past either end of the source.
        /// </summary>
        public static void Slip(ClipData c, long newSourceOffset, int recipeRate)
        {
            if (c.loop && c.sourceFrames > 0) { c.sourceOffset = DspMath.Mod(newSourceOffset, c.sourceFrames); return; }
            SlipRange(c, recipeRate, out long min, out long max);
            c.sourceOffset = DspMath.Clamp(newSourceOffset, min, max);
        }

        /// <summary>
        /// Slide: moves <paramref name="c"/> while the touching neighbours absorb the change (the left one's end and the
        /// right one's start follow), so the edit stays gap-free. Returns the applied delta.
        /// </summary>
        public static long Slide(TrackData t, ClipData c, long newStart, int recipeRate)
        {
            var left = LeftNeighbor(t, c);
            var right = RightNeighbor(t, c);
            long delta = newStart - c.startSample;
            long lo = -c.startSample, hi = long.MaxValue / 4;
            if (left != null)
            {
                lo = Math.Max(lo, MinClipLength - left.lengthSamples);
                if (!left.loop) hi = Math.Min(hi, left.MaxLengthFromOffset(recipeRate) - left.lengthSamples);
            }
            if (right != null)
            {
                hi = Math.Min(hi, right.lengthSamples - MinClipLength);
                if (!right.loop) lo = Math.Max(lo, -(long)Math.Floor(right.sourceOffset / right.SourceStep(recipeRate)));
            }
            if (lo > hi) return 0;
            delta = DspMath.Clamp(delta, lo, hi);
            if (delta == 0) return 0;
            if (left != null) TrimEnd(left, left.EndSample + delta, recipeRate);
            if (right != null) TrimStart(right, right.startSample + delta, recipeRate);
            c.startSample += delta;
            return delta;
        }

        /// <summary>
        /// Roll: moves the cut between two touching clips — <paramref name="left"/> gets longer while
        /// <paramref name="right"/> gets shorter (or vice versa); nothing else on the timeline moves. Returns the new cut.
        /// </summary>
        public static long Roll(ClipData left, ClipData right, long newCut, int recipeRate)
        {
            long lo = left.startSample + MinClipLength;
            long hi = right.EndSample - MinClipLength;
            if (!left.loop) hi = Math.Min(hi, left.startSample + left.MaxLengthFromOffset(recipeRate));
            if (!right.loop) lo = Math.Max(lo, right.startSample - (long)Math.Floor(right.sourceOffset / right.SourceStep(recipeRate)));
            if (lo > hi) return left.EndSample;
            newCut = DspMath.Clamp(newCut, lo, hi);
            TrimEnd(left, newCut, recipeRate);
            TrimStart(right, newCut, recipeRate);
            return newCut;
        }

        /// <summary>
        /// Ripple trim of the clip's end: the clip gets shorter/longer and every later clip on the track moves by the
        /// same amount, so no gap opens. Returns the applied delta.
        /// </summary>
        public static long RippleTrimEnd(TrackData t, ClipData c, long newEnd, int recipeRate)
        {
            long oldEnd = c.EndSample;
            TrimEnd(c, newEnd, recipeRate);
            long delta = c.EndSample - oldEnd;
            ShiftFollowing(t, c, oldEnd, delta);
            return delta;
        }

        /// <summary>
        /// Ripple trim of the clip's head: the clip stays at the same timeline position, its in-point moves
        /// (<paramref name="newStart"/> − start samples are removed, or revealed when negative), and every later clip
        /// moves to close/open the gap. Returns the number of samples removed from the head.
        /// </summary>
        public static long RippleTrimStart(TrackData t, ClipData c, long newStart, int recipeRate)
        {
            long oldEnd = c.EndSample;
            long delta = newStart - c.startSample;
            double step = c.SourceStep(recipeRate);
            delta = Math.Min(delta, c.lengthSamples - MinClipLength);
            if (!c.loop) delta = Math.Max(delta, -(long)Math.Floor(c.sourceOffset / step));
            if (delta == 0) return 0;
            long newOffset = c.sourceOffset + (long)Math.Round(delta * step);
            newOffset = c.loop && c.sourceFrames > 0 ? DspMath.Mod(newOffset, c.sourceFrames) : Math.Max(0, newOffset);
            ShiftAutomation(c.automation, -delta);
            c.sourceOffset = newOffset;
            c.lengthSamples -= delta;
            ClampFades(c);
            ShiftFollowing(t, c, oldEnd, -delta);
            return delta;
        }

        /// <summary>Moves every clip on <paramref name="t"/> (except <paramref name="except"/>) that starts at or after <paramref name="from"/>.</summary>
        public static void ShiftFollowing(TrackData t, ClipData except, long from, long delta)
        {
            if (delta == 0) return;
            foreach (var o in t.clips)
                if (o != except && o.startSample >= from - AdjacencyTolerance) o.startSample = Math.Max(0, o.startSample + delta);
        }

        /// <summary>Blade: splits every clip in <paramref name="tracks"/> that spans <paramref name="at"/>. Returns the new right-hand clips.</summary>
        public static List<ClipData> SplitAll(IEnumerable<TrackData> tracks, long at, int recipeRate)
        {
            var created = new List<ClipData>();
            foreach (var t in tracks)
            {
                var hits = new List<ClipData>();
                foreach (var c in t.clips) if (at > c.startSample && at < c.EndSample) hits.Add(c);
                foreach (var c in hits)
                {
                    var r = Split(t, c, at, recipeRate);
                    if (r != null) created.Add(r);
                }
            }
            return created;
        }

        private static void ShiftAutomation(List<AutomationLane> lanes, long delta)
        {
            foreach (var l in lanes)
                for (int i = 0; i < l.points.Count; i++)
                {
                    var p = l.points[i]; p.time += delta; l.points[i] = p;
                }
        }
    }
}
