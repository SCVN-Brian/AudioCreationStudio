using System;
using System.Collections.Generic;
using UnityEngine;

namespace AudioCreationStudio.Models
{
    /// <summary>
    /// Non-destructive placement of a source on the timeline. The source audio is never modified;
    /// trimming, splitting and fading only change these numbers.
    /// </summary>
    [Serializable]
    public class ClipData
    {
        public string uid;
        public string name;

        public ClipSourceKind sourceKind = ClipSourceKind.AudioClip;
        public AudioClip sourceClip;
        public GeneratorSettings generator;

        // Cached source format (kept in the model so editing operations never need the AudioClip).
        public int sourceSampleRate = 48000;
        public long sourceFrames;
        public int sourceChannels = 1;

        /// <summary>Timeline position in samples (recipe rate).</summary>
        public long startSample;
        /// <summary>Start position inside the source, in source frames (source rate).</summary>
        public long sourceOffset;
        /// <summary>Duration on the timeline, in samples (recipe rate).</summary>
        public long lengthSamples;

        public float gainDb;
        public float pan;
        public double playbackRate = 1.0;
        public FadeData fadeIn = new FadeData();
        public FadeData fadeOut = new FadeData();
        public bool loop;
        public bool enabled = true;

        public List<AutomationLane> automation = new List<AutomationLane>();

        public long EndSample => startSample + lengthSamples;

        /// <summary>Source frames advanced per timeline sample.</summary>
        public double SourceStep(int timelineRate) =>
            timelineRate <= 0 || sourceSampleRate <= 0 ? 1.0 : playbackRate * sourceSampleRate / timelineRate;

        /// <summary>Timeline samples available from <see cref="sourceOffset"/> to the source end at the current rate.</summary>
        public long MaxLengthFromOffset(int timelineRate)
        {
            if (loop) return long.MaxValue / 4;
            double step = SourceStep(timelineRate);
            double remaining = Math.Max(0, sourceFrames - sourceOffset);
            return (long)Math.Floor(remaining / step);
        }

        public AutomationLane FindLane(string targetId)
        {
            if (automation == null) return null;
            foreach (var l in automation) if (l.targetId == targetId) return l;
            return null;
        }

        public AutomationLane GetOrCreateLane(string targetId)
        {
            var lane = FindLane(targetId);
            if (lane == null)
            {
                lane = new AutomationLane(targetId);
                automation.Add(lane);
            }
            return lane;
        }

        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrEmpty(name)) return name;
                if (sourceKind == ClipSourceKind.Generator) return generator != null ? generator.presetName : "Generator";
                return sourceClip != null ? sourceClip.name : "Missing Source";
            }
        }

        public ClipData Clone(bool newUid)
        {
            var c = (ClipData)MemberwiseClone();
            if (newUid) c.uid = Uid.New();
            c.generator = generator?.Clone();
            c.fadeIn = (fadeIn ?? new FadeData()).Clone();
            c.fadeOut = (fadeOut ?? new FadeData()).Clone();
            c.automation = new List<AutomationLane>();
            if (automation != null) foreach (var l in automation) c.automation.Add(l.Clone());
            return c;
        }
    }
}
