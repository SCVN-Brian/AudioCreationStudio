using System;
using System.Collections.Generic;

namespace AudioCreationStudio.Models
{
    [Serializable]
    public struct AutomationPoint
    {
        /// <summary>Time in samples. Clip lanes are relative to clip start; track lanes are timeline-absolute.</summary>
        public long time;
        public float value;
        /// <summary>Interpolation used from this point to the next.</summary>
        public AutomationInterpolation interpolation;

        public AutomationPoint(long time, float value, AutomationInterpolation interpolation = AutomationInterpolation.Linear)
        {
            this.time = time;
            this.value = value;
            this.interpolation = interpolation;
        }
    }

    /// <summary>Well-known automation target ids.</summary>
    public static class AutomationTargets
    {
        /// <summary>Gain lane; values are in dB and override the static gain while the lane has points.</summary>
        public const string Gain = "gain";
        /// <summary>Pan lane; values in [-1, 1] override the static pan while the lane has points.</summary>
        public const string Pan = "pan";

        public static string Effect(string effectUid, string paramId) => $"fx/{effectUid}/{paramId}";

        public static bool TryParseEffect(string targetId, out string effectUid, out string paramId)
        {
            effectUid = paramId = null;
            if (string.IsNullOrEmpty(targetId) || !targetId.StartsWith("fx/")) return false;
            var parts = targetId.Split('/');
            if (parts.Length != 3) return false;
            effectUid = parts[1];
            paramId = parts[2];
            return true;
        }
    }

    [Serializable]
    public class AutomationLane
    {
        public string targetId;
        public bool enabled = true;
        public List<AutomationPoint> points = new List<AutomationPoint>();

        public bool IsActive => enabled && points != null && points.Count > 0;

        public AutomationLane() { }
        public AutomationLane(string targetId) { this.targetId = targetId; }

        public void Sort() => points.Sort((a, b) => a.time.CompareTo(b.time));

        /// <summary>Evaluates the lane at <paramref name="time"/>; returns <paramref name="fallback"/> when inactive.</summary>
        public float Evaluate(long time, float fallback)
        {
            if (!IsActive) return fallback;
            var pts = points;
            int n = pts.Count;
            if (time <= pts[0].time) return pts[0].value;
            if (time >= pts[n - 1].time) return pts[n - 1].value;

            int lo = 0, hi = n - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) >> 1;
                if (pts[mid].time <= time) lo = mid; else hi = mid;
            }
            var a = pts[lo];
            var b = pts[hi];
            long span = b.time - a.time;
            if (span <= 0) return b.value;
            double x = (double)(time - a.time) / span;
            switch (a.interpolation)
            {
                case AutomationInterpolation.Step: return a.value;
                case AutomationInterpolation.Smooth: x = x * x * (3.0 - 2.0 * x); break;
            }
            return (float)(a.value + (b.value - a.value) * x);
        }

        public AutomationLane Clone()
        {
            return new AutomationLane
            {
                targetId = targetId,
                enabled = enabled,
                points = new List<AutomationPoint>(points ?? new List<AutomationPoint>())
            };
        }
    }
}
