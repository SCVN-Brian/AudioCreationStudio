using System;
using System.Collections.Generic;
using UnityEngine;

namespace AudioCreationStudio.Models
{
    [Serializable]
    public class TrackData
    {
        public string uid;
        public string name = "Track";
        public Color color = new Color(0.36f, 0.62f, 1f);
        public float gainDb;
        public float pan;
        public bool mute;
        public bool solo;
        public List<ClipData> clips = new List<ClipData>();
        public List<EffectData> effects = new List<EffectData>();
        public List<AutomationLane> automation = new List<AutomationLane>();

        // View state (serialized so the layout survives reloads)
        public bool showAutomation;
        public string automationTarget = AutomationTargets.Gain;

        public long ContentEnd
        {
            get
            {
                long end = 0;
                foreach (var c in clips) if (c.enabled && c.EndSample > end) end = c.EndSample;
                return end;
            }
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

        public TrackData Clone(bool newUids)
        {
            var t = (TrackData)MemberwiseClone();
            if (newUids) t.uid = Uid.New();
            t.clips = new List<ClipData>();
            foreach (var c in clips) t.clips.Add(c.Clone(newUids));
            t.effects = new List<EffectData>();
            var uidMap = new Dictionary<string, string>();
            foreach (var e in effects)
            {
                var ce = e.Clone(newUids);
                uidMap[e.uid ?? ""] = ce.uid;
                t.effects.Add(ce);
            }
            t.automation = new List<AutomationLane>();
            foreach (var l in automation)
            {
                var cl = l.Clone();
                if (newUids && AutomationTargets.TryParseEffect(cl.targetId, out var fxUid, out var pid) && uidMap.TryGetValue(fxUid, out var mapped))
                    cl.targetId = AutomationTargets.Effect(mapped, pid);
                t.automation.Add(cl);
            }
            if (newUids && AutomationTargets.TryParseEffect(t.automationTarget, out var tu, out var tp) && uidMap.TryGetValue(tu, out var tm))
                t.automationTarget = AutomationTargets.Effect(tm, tp);
            return t;
        }
    }
}
