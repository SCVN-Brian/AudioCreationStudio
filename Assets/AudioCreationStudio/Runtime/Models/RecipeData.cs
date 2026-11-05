using System;
using System.Collections.Generic;

namespace AudioCreationStudio.Models
{
    /// <summary>
    /// The complete, non-destructive description of a sound. Plain serializable data: the renderer
    /// works on deep clones of this object so editing on the main thread never races with rendering.
    /// </summary>
    [Serializable]
    public class RecipeData
    {
        public string name = "New Sound";
        public int sampleRate = 48000;
        public OutputChannelMode channels = OutputChannelMode.Stereo;
        public float masterGainDb;
        public List<EffectData> masterEffects = new List<EffectData>();
        public List<TrackData> tracks = new List<TrackData>();
        public TimelineRegion loopRegion = new TimelineRegion();
        public LoopSettings loop = new LoopSettings();
        public ExportSettings export = new ExportSettings();
        public VariationSettings variations = new VariationSettings();

        public int ChannelCount => (int)channels;

        public long ContentEnd
        {
            get
            {
                long end = 0;
                foreach (var t in tracks) end = Math.Max(end, t.ContentEnd);
                return end;
            }
        }

        public TrackData FindTrack(string uid)
        {
            foreach (var t in tracks) if (t.uid == uid) return t;
            return null;
        }

        public ClipData FindClip(string uid, out TrackData owner)
        {
            foreach (var t in tracks)
                foreach (var c in t.clips)
                    if (c.uid == uid) { owner = t; return c; }
            owner = null;
            return null;
        }

        public ClipData FindClip(string uid) => FindClip(uid, out _);

        public IEnumerable<ClipData> AllClips()
        {
            foreach (var t in tracks)
                foreach (var c in t.clips) yield return c;
        }

        public void EnsureValid()
        {
            if (sampleRate < 8000) sampleRate = 48000;
            if (channels != OutputChannelMode.Mono && channels != OutputChannelMode.Stereo) channels = OutputChannelMode.Stereo;
            masterEffects ??= new List<EffectData>();
            tracks ??= new List<TrackData>();
            loopRegion ??= new TimelineRegion();
            loop ??= new LoopSettings();
            export ??= new ExportSettings();
            variations ??= new VariationSettings();
            foreach (var t in tracks)
            {
                if (string.IsNullOrEmpty(t.uid)) t.uid = Uid.New();
                t.clips ??= new List<ClipData>();
                t.effects ??= new List<EffectData>();
                t.automation ??= new List<AutomationLane>();
                foreach (var c in t.clips)
                {
                    if (string.IsNullOrEmpty(c.uid)) c.uid = Uid.New();
                    c.fadeIn ??= new FadeData();
                    c.fadeOut ??= new FadeData();
                    c.automation ??= new List<AutomationLane>();
                    if (c.playbackRate <= 0) c.playbackRate = 1.0;
                }
                foreach (var e in t.effects) if (string.IsNullOrEmpty(e.uid)) e.uid = Uid.New();
            }
            foreach (var e in masterEffects) if (string.IsNullOrEmpty(e.uid)) e.uid = Uid.New();
        }

        public RecipeData Clone()
        {
            var r = (RecipeData)MemberwiseClone();
            r.masterEffects = new List<EffectData>();
            foreach (var e in masterEffects) r.masterEffects.Add(e.Clone());
            r.tracks = new List<TrackData>();
            foreach (var t in tracks) r.tracks.Add(t.Clone(false));
            r.loopRegion = (loopRegion ?? new TimelineRegion()).Clone();
            r.loop = (loop ?? new LoopSettings()).Clone();
            r.export = (export ?? new ExportSettings()).Clone();
            r.variations = (variations ?? new VariationSettings()).Clone();
            return r;
        }
    }
}
