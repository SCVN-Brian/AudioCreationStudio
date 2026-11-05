using System;
using System.Collections.Generic;
using AudioCreationStudio.DSP;
using AudioCreationStudio.Models;
using UnityEngine;

namespace AudioCreationStudio.EditorUI
{
    [Serializable]
    public class TimelineViewState
    {
        public const float TrackHeight = 78f;
        public const float AutomationHeight = 66f;
        public const float RulerHeight = 30f;
        public const double MinPps = 4, MaxPps = 48000;

        public double pixelsPerSecond = 220;
        public double scrollSeconds;
        public float scrollY;

        public float X(long sample, int sr) => (float)((sample / (double)sr - scrollSeconds) * pixelsPerSecond);
        public double SecondsAtX(float x) => x / pixelsPerSecond + scrollSeconds;
        public long SampleAtX(float x, int sr) => (long)Math.Round(SecondsAtX(x) * sr);
        public double SamplesPerPixel(int sr) => sr / pixelsPerSecond;

        public void ZoomAt(float x, double factor)
        {
            double t = SecondsAtX(x);
            pixelsPerSecond = DspMath.Clamp(pixelsPerSecond * factor, MinPps, MaxPps);
            scrollSeconds = Math.Max(0, t - x / pixelsPerSecond);
        }

        public static float RowHeight(TrackData t) => TrackHeight + (t.showAutomation ? AutomationHeight : 0f);

        public static float ContentHeight(RecipeData r)
        {
            float h = 0;
            foreach (var t in r.tracks) h += RowHeight(t);
            return h;
        }

        public float RowTop(RecipeData r, int index)
        {
            float y = -scrollY;
            for (int i = 0; i < index && i < r.tracks.Count; i++) y += RowHeight(r.tracks[i]);
            return y;
        }

        /// <summary>Track index at canvas y, or -1 (above) / Count (below).</summary>
        public int TrackIndexAt(RecipeData r, float y, out bool inAutomation)
        {
            inAutomation = false;
            float top = -scrollY;
            if (y < top) return -1;
            for (int i = 0; i < r.tracks.Count; i++)
            {
                float h = RowHeight(r.tracks[i]);
                if (y < top + h)
                {
                    inAutomation = r.tracks[i].showAutomation && y >= top + TrackHeight;
                    return i;
                }
                top += h;
            }
            return r.tracks.Count;
        }

        /// <summary>Major/minor grid step in seconds for the current zoom.</summary>
        public void GridSteps(out double major, out double minor)
        {
            double[] steps = { 0.001, 0.002, 0.005, 0.01, 0.02, 0.05, 0.1, 0.2, 0.5, 1, 2, 5, 10, 15, 30, 60, 120 };
            major = steps[steps.Length - 1];
            foreach (var s in steps)
                if (s * pixelsPerSecond >= 90) { major = s; break; }
            string str = major.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            minor = str.Contains("2") ? major / 4 : major / 5;
        }

        public static string FormatTime(double seconds, double step)
        {
            if (seconds < 0) seconds = 0;
            if (step < 0.01) return $"{seconds:0.000}";
            if (step < 1) return $"{seconds:0.00}";
            if (seconds < 60) return $"{seconds:0}s";
            return $"{(int)(seconds / 60)}:{(int)(seconds % 60):00}";
        }
    }

    /// <summary>Describes an automation target for display and editing (name, range, formatting).</summary>
    public sealed class AutomationTargetInfo
    {
        public string TargetId;
        public string Name;
        public ValueRange Range;
        public float StaticValue;
        public Func<float, string> Format;

        public static List<AutomationTargetInfo> ForTrack(TrackData t)
        {
            var list = new List<AutomationTargetInfo>
            {
                new AutomationTargetInfo { TargetId = AutomationTargets.Gain, Name = "Track Gain", Range = new ValueRange(-60f, 12f, 0f, false, true), StaticValue = t.gainDb, Format = v => $"{v:0.0} dB" },
                new AutomationTargetInfo { TargetId = AutomationTargets.Pan, Name = "Track Pan", Range = new ValueRange(-1f, 1f, 0f, false, true), StaticValue = t.pan, Format = FormatPan }
            };
            foreach (var e in t.effects)
            {
                var def = EffectRegistry.Get(e.typeId);
                if (def == null) continue;
                var values = def.Normalize(e.values);
                for (int i = 0; i < def.Params.Length; i++)
                {
                    var p = def.Params[i];
                    if (!p.Automatable) continue;
                    list.Add(new AutomationTargetInfo
                    {
                        TargetId = AutomationTargets.Effect(e.uid, p.Id),
                        Name = $"{def.DisplayName} › {p.Name}",
                        Range = ValueRange.FromParam(p),
                        StaticValue = values[i],
                        Format = p.Format
                    });
                }
            }
            return list;
        }

        public static List<AutomationTargetInfo> ForClip(ClipData c) => new List<AutomationTargetInfo>
        {
            new AutomationTargetInfo { TargetId = AutomationTargets.Gain, Name = "Clip Gain", Range = new ValueRange(-60f, 12f, 0f, false, true), StaticValue = c.gainDb, Format = v => $"{v:0.0} dB" },
            new AutomationTargetInfo { TargetId = AutomationTargets.Pan, Name = "Clip Pan", Range = new ValueRange(-1f, 1f, 0f, false, true), StaticValue = c.pan, Format = FormatPan }
        };

        public static AutomationTargetInfo Find(List<AutomationTargetInfo> list, string id)
        {
            foreach (var i in list) if (i.TargetId == id) return i;
            return list.Count > 0 ? list[0] : null;
        }

        public static string FormatPan(float v)
        {
            if (Mathf.Abs(v) < 0.005f) return "C";
            return v < 0 ? $"L{Mathf.RoundToInt(-v * 100)}" : $"R{Mathf.RoundToInt(v * 100)}";
        }
    }

    /// <summary>Collects snap targets and resolves the nearest one within a pixel threshold.</summary>
    public sealed class SnapResolver
    {
        public const float ThresholdPx = 8f;
        private readonly List<long> _targets = new List<long>();
        private double _gridSamples;
        private int _sr;
        private TimelineViewState _view;
        public bool Enabled;

        public void Build(RecipeData r, TimelineViewState view, long playhead, ICollection<string> exclude, bool enabled)
        {
            Enabled = enabled;
            _view = view;
            _sr = r.sampleRate;
            _targets.Clear();
            _targets.Add(0);
            _targets.Add(playhead);
            if (r.loopRegion.enabled) { _targets.Add(r.loopRegion.start); _targets.Add(r.loopRegion.end); }
            foreach (var t in r.tracks)
                foreach (var c in t.clips)
                {
                    if (exclude != null && exclude.Contains(c.uid)) continue;
                    _targets.Add(c.startSample);
                    _targets.Add(c.EndSample);
                }
            view.GridSteps(out _, out double minor);
            _gridSamples = minor * _sr;
        }

        /// <summary>Returns the snapped position (or the input), with <paramref name="snapped"/> set when snapping occurred.</summary>
        public long Snap(long sample, out bool snapped)
        {
            snapped = false;
            if (!Enabled || _view == null) return sample;
            double thr = ThresholdPx * _view.SamplesPerPixel(_sr);
            long best = sample;
            double bestDist = thr;
            foreach (var t in _targets)
            {
                double d = Math.Abs(t - sample);
                if (d < bestDist) { bestDist = d; best = t; snapped = true; }
            }
            if (!snapped && _gridSamples > 0)
            {
                long g = (long)(Math.Round(sample / _gridSamples) * _gridSamples);
                if (Math.Abs(g - sample) < thr) { best = g; snapped = true; }
            }
            return best;
        }
    }
}
