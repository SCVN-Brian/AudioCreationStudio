using System;
using System.Collections.Generic;
using AudioCreationStudio.DSP;
using AudioCreationStudio.Editing;
using AudioCreationStudio.Models;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AudioCreationStudio.EditorUI
{
    /// <summary>Contextual inspector: selected clip → clip properties; selected track → track + effects; otherwise the recipe/master.</summary>
    public sealed class InspectorPanel : VisualElement
    {
        private readonly StudioContext _ctx;
        private readonly ScrollView _scroll;
        private readonly Label _title;
        private readonly Label _subtitle;
        private readonly VisualElement _titleDot;
        private string _builtFor;
        private string _clipAutoTarget = AutomationTargets.Gain;

        public InspectorPanel(StudioContext ctx)
        {
            _ctx = ctx;
            AddToClassList("acs-inspector");
            var head = new VisualElement();
            head.AddToClassList("acs-inspector__head");
            _titleDot = new VisualElement();
            _titleDot.AddToClassList("acs-inspector__dot");
            head.Add(_titleDot);
            var tcol = new VisualElement();
            _title = UIUtil.Label("Inspector", "acs-inspector__title");
            _subtitle = UIUtil.Label("", "acs-inspector__subtitle");
            tcol.Add(_title);
            tcol.Add(_subtitle);
            head.Add(tcol);
            head.Add(UIUtil.Spacer());
            head.Add(new StudioButton(Icon.Mixer, null, () => _ctx.Session.SelectMaster(), "Inspect recipe & master bus", "acs-btn--sm"));
            Add(head);
            _scroll = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            _scroll.AddToClassList("acs-inspector__scroll");
            Add(_scroll);
        }

        /// <summary>Rebuilds unless the change originated from this inspector (keeps sliders responsive while dragging).</summary>
        public void OnChanged(StudioChange change, object origin)
        {
            string key = SelectionKey();
            bool selectionChanged = key != _builtFor;
            bool structural = (change & (StudioChange.Structure | StudioChange.Recipe)) != 0;
            bool external = (change & StudioChange.Content) != 0 && origin == null && !_ctx.Session.InContinuousEdit;
            bool ownEdit = origin != null && IsOwnOrigin(origin);
            if (selectionChanged || structural || (external && !ownEdit) || (change == StudioChange.All)) Rebuild();
        }

        private bool IsOwnOrigin(object origin) => origin is VisualElement ve && (ve == this || ve.FindCommonAncestor(this) == this);

        private string SelectionKey()
        {
            var s = _ctx.Session;
            if (s.PrimaryClip != null) return "clip:" + s.PrimaryClip.uid;
            if (!s.InspectMaster && s.PrimaryTrack != null) return "track:" + s.PrimaryTrack.uid;
            return "master";
        }

        public void Rebuild()
        {
            _builtFor = SelectionKey();
            float scroll = _scroll.scrollOffset.y;
            _scroll.Clear();
            var s = _ctx.Session;
            var clip = s.PrimaryClip;
            if (clip != null)
            {
                s.Data.FindClip(clip.uid, out var owner);
                BuildClip(clip, owner);
            }
            else if (!s.InspectMaster && s.PrimaryTrack != null) BuildTrack(s.PrimaryTrack);
            else BuildMaster();
            _scroll.schedule.Execute(() => _scroll.scrollOffset = new Vector2(0, scroll));
        }

        private void SetHeader(string title, string subtitle, Color dot)
        {
            _title.text = title;
            _subtitle.text = subtitle;
            _titleDot.style.backgroundColor = dot;
        }

        private static Color C(TrackData t) => t == null ? StudioTheme.Accent : new Color(t.color.r, t.color.g, t.color.b, 1f);

        // ------------------------------------------------------------- Clip

        private void BuildClip(ClipData c, TrackData owner)
        {
            string uid = c.uid;
            int sr = _ctx.SampleRate;
            int count = _ctx.Session.SelectedClips.Count;
            SetHeader(c.DisplayName, $"Clip on {owner?.name}" + (count > 1 ? $"  ·  {count} selected" : ""), C(owner));
            ClipData Get(RecipeData d) => d.FindClip(uid);

            // Source
            var src = new SectionCard("Source");
            src.Actions.Add(new StudioButton(Icon.Play, "Preview", () => _ctx.Transport.PlayClip(uid), "Play this clip alone (through its track effects)", "acs-btn--sm", "acs-btn--soft"));
            if (c.sourceKind == ClipSourceKind.Generator)
            {
                src.Body.Add(Fields.Info("Type", $"Procedural · {c.generator?.presetName}"));
                var edit = new StudioButton(Icon.Sine, "Edit in Custom Generate", () => _ctx.EditClipInGenerator?.Invoke(uid), "Open this clip in Custom Generate; edits update the clip", "acs-btn--soft");
                src.Body.Add(edit);
                if (StudioCommands.CanRevertToAudio(c))
                    src.Body.Add(new StudioButton(Icon.Undo, "Revert to Original Audio", () => _ctx.Commands.RevertToOriginalAudio(uid),
                        $"Play '{c.sourceClip.name}' again as plain audio (drops the Custom Generate edits)", "acs-btn--sm"));
            }
            else
            {
                var of = new ObjectField("AudioClip") { objectType = typeof(AudioClip), allowSceneObjects = false, value = c.sourceClip };
                of.AddToClassList("acs-objectfield");
                of.RegisterValueChangedCallback(e =>
                {
                    var ac = e.newValue as AudioClip;
                    if (ac != null && !_ctx.Library.TryGetClip(ac, out _, out _, out var err)) { _ctx.Toast?.Invoke(err, true); }
                    _ctx.Session.Modify("Change Clip Source", d =>
                    {
                        var cc = Get(d);
                        cc.sourceClip = ac;
                        if (ac != null && _ctx.Library.TryGetClip(ac, out var b, out _, out _))
                        {
                            cc.sourceSampleRate = b.SampleRate; cc.sourceFrames = b.Frames; cc.sourceChannels = b.Channels;
                            cc.sourceOffset = Math.Min(cc.sourceOffset, Math.Max(0, b.Frames - 1));
                        }
                    }, StudioChange.Structure);
                });
                src.Body.Add(of);
                if (c.sourceClip != null)
                    src.Body.Add(new StudioButton(Icon.Sine, "Edit in Custom Generate", () => _ctx.EditClipInGenerator?.Invoke(uid),
                        "Edit this clip's sound with Custom Generate (pitch, envelope, filter, noise, drive…). The clip is updated in place; Cmd/Ctrl+Z or Revert to Original Audio restores it.", "acs-btn--soft"));
                if (c.sourceClip == null) src.Body.Add(Fields.Warning("Source AudioClip is missing. Assign a clip; the timeline placement is kept.", true));
                else if (!_ctx.Library.TryGetClip(c.sourceClip, out _, out _, out var err)) src.Body.Add(Fields.Warning(err, true));
            }
            src.Body.Add(Fields.Info("Format", $"{c.sourceSampleRate} Hz · {(c.sourceChannels == 1 ? "Mono" : c.sourceChannels == 2 ? "Stereo" : c.sourceChannels + " ch")} · {c.sourceFrames / (double)Math.Max(1, c.sourceSampleRate):0.000} s"));
            _scroll.Add(src);

            // Timing
            var timing = new SectionCard("Timing");
            timing.Body.Add(Fields.Text("Name", c.name ?? "", v => _ctx.Session.Modify("Rename Clip", d => Get(d).name = v)));
            timing.Body.Add(Fields.Time(_ctx, "Start", c.startSample, "Move Clip", (d, v) => TimelineEditing.MoveClip(Get(d), v), this, "Timeline position"));
            timing.Body.Add(Fields.Time(_ctx, "Source Offset", SampleTime.Rescale(c.sourceOffset, c.sourceSampleRate, sr), "Change Source Offset", (d, v) =>
            {
                var cc = Get(d);
                cc.sourceOffset = Math.Max(0, Math.Min(SampleTime.Rescale(v, sr, cc.sourceSampleRate), Math.Max(0, cc.sourceFrames - 1)));
                cc.lengthSamples = Math.Max(TimelineEditing.MinClipLength, Math.Min(cc.lengthSamples, cc.MaxLengthFromOffset(sr)));
            }, this, "Where playback starts inside the source"));
            timing.Body.Add(Fields.Time(_ctx, "Duration", c.lengthSamples, "Change Duration", (d, v) => TimelineEditing.TrimEnd(Get(d), Get(d).startSample + v, sr), this));
            timing.Body.Add(Fields.Param(_ctx, "Playback Rate", new ValueRange(0.25f, 4f, 1f, true), (float)c.playbackRate,
                v => $"×{v:0.000} ({DspMath.RatioToSemitones(v):+0.0;-0.0;0.0} st)", "Playback Rate", (d, v) =>
                {
                    var cc = Get(d);
                    double old = cc.playbackRate;
                    cc.playbackRate = v;
                    // Keep the same source region: duration scales inversely with rate.
                    cc.lengthSamples = Math.Max(TimelineEditing.MinClipLength, (long)Math.Round(cc.lengthSamples * old / v));
                    if (!cc.loop) cc.lengthSamples = Math.Min(cc.lengthSamples, cc.MaxLengthFromOffset(sr));
                    TimelineEditing.ClampFades(cc);
                }, this, "Varispeed: changes pitch and duration together"));
            timing.Body.Add(Fields.Switch("Loop Source", c.loop, v => _ctx.Session.Modify("Toggle Clip Loop", d => Get(d).loop = v), "Repeat the source to fill the clip duration"));
            timing.Body.Add(Fields.Switch("Enabled", c.enabled, v => _ctx.Session.Modify("Toggle Clip", d => Get(d).enabled = v)));
            var fit = new StudioButton(Icon.None, "Fit Duration to Source", () => _ctx.Session.Modify("Fit to Source", d => TimelineEditing.FitLengthToSource(Get(d), sr)), null, "acs-btn--soft", "acs-btn--sm");
            timing.Body.Add(fit);
            _scroll.Add(timing);

            // Level
            var level = new SectionCard("Level");
            level.Body.Add(Fields.Db(_ctx, "Gain", c.gainDb, "Clip Gain", (d, v) => Get(d).gainDb = v, this));
            level.Body.Add(Fields.Pan(_ctx, "Pan", c.pan, "Clip Pan", (d, v) => Get(d).pan = v, this));
            _scroll.Add(level);

            // Fades
            var fades = new SectionCard("Fades");
            float maxMs = (float)(c.lengthSamples * 1000.0 / sr);
            fades.Body.Add(Fields.Param(_ctx, "Fade In", new ValueRange(0f, Mathf.Max(1f, maxMs), 0f), (float)(c.fadeIn.lengthSamples * 1000.0 / sr), v => $"{v:0} ms",
                "Fade In", (d, v) => TimelineEditing.SetFadeIn(Get(d), SampleTime.SecondsToSamples(v / 1000.0, sr)), this));
            fades.Body.Add(Fields.Enum("In Curve", c.fadeIn.curve, v => _ctx.Session.Modify("Fade Curve", d => Get(d).fadeIn.curve = v)));
            fades.Body.Add(Fields.Param(_ctx, "Fade Out", new ValueRange(0f, Mathf.Max(1f, maxMs), 0f), (float)(c.fadeOut.lengthSamples * 1000.0 / sr), v => $"{v:0} ms",
                "Fade Out", (d, v) => TimelineEditing.SetFadeOut(Get(d), SampleTime.SecondsToSamples(v / 1000.0, sr)), this));
            fades.Body.Add(Fields.Enum("Out Curve", c.fadeOut.curve, v => _ctx.Session.Modify("Fade Curve", d => Get(d).fadeOut.curve = v)));
            _scroll.Add(fades);

            // Automation
            var auto = new SectionCard("Clip Automation", true, c.automation.Exists(l => l.IsActive));
            var seg = new SegmentedControl(new[] { "Gain", "Pan" }, _clipAutoTarget == AutomationTargets.Pan ? 1 : 0);
            auto.Actions.Add(seg);
            var curve = new AutomationCurveElement(_ctx, uid, _clipAutoTarget);
            seg.Changed += i => { _clipAutoTarget = i == 1 ? AutomationTargets.Pan : AutomationTargets.Gain; curve.Target = _clipAutoTarget; };
            auto.Body.Add(curve);
            var autoRow = UIUtil.Row(UIUtil.Label("Values override the static Gain/Pan while a lane has points.", "acs-hint"), UIUtil.Spacer(),
                new StudioButton(Icon.Trash, "Clear", () => _ctx.Session.Modify("Clear Clip Automation", d => Get(d).FindLane(_clipAutoTarget)?.points.Clear(), StudioChange.Content), "Clear this lane", "acs-btn--sm"));
            auto.Body.Add(autoRow);
            _scroll.Add(auto);
        }

        // ------------------------------------------------------------- Track

        private void BuildTrack(TrackData t)
        {
            string uid = t.uid;
            SetHeader(t.name, $"Track · {t.clips.Count} clip(s) · {t.effects.Count} effect(s)", C(t));
            TrackData Get(RecipeData d) => d.FindTrack(uid);

            var card = new SectionCard("Track");
            card.Actions.Add(new StudioButton(Icon.Play, "Preview", () => _ctx.Transport.PlayTrack(uid), "Play this track alone", "acs-btn--sm", "acs-btn--soft"));
            card.Body.Add(Fields.Text("Name", t.name, v => _ctx.Session.Modify("Rename Track", d => Get(d).name = v)));
            var swatches = new VisualElement();
            swatches.AddToClassList("acs-swatches");
            foreach (var col in TimelineEditing.TrackPalette)
            {
                var c = col;
                var sw = new VisualElement();
                sw.AddToClassList("acs-swatch");
                sw.style.backgroundColor = c;
                if (Mathf.Abs(t.color.r - c.r) + Mathf.Abs(t.color.g - c.g) + Mathf.Abs(t.color.b - c.b) < 0.02f) sw.AddToClassList("acs-swatch--on");
                sw.AddManipulator(new Clickable(() => _ctx.Session.Modify("Track Color", d => Get(d).color = c, StudioChange.Structure)));
                swatches.Add(sw);
            }
            var colorRow = new VisualElement();
            colorRow.AddToClassList("acs-param");
            colorRow.Add(UIUtil.Label("Color", "acs-param__label"));
            colorRow.Add(swatches);
            card.Body.Add(colorRow);
            card.Body.Add(Fields.Db(_ctx, "Gain", t.gainDb, "Track Gain", (d, v) => Get(d).gainDb = v, this, -60f, 12f));
            card.Body.Add(Fields.Pan(_ctx, "Pan", t.pan, "Track Pan", (d, v) => Get(d).pan = v, this));
            card.Body.Add(Fields.Switch("Mute", t.mute, v => _ctx.Session.Modify("Mute Track", d => Get(d).mute = v)));
            card.Body.Add(Fields.Switch("Solo", t.solo, v => _ctx.Session.Modify("Solo Track", d => Get(d).solo = v)));
            _scroll.Add(card);

            var rackCard = new SectionCard("Effects Rack");
            rackCard.Body.Add(new EffectsRack(_ctx, t.name, d => d.FindTrack(uid)?.effects));
            _scroll.Add(rackCard);

            var auto = new SectionCard("Automation");
            auto.Body.Add(Fields.Switch("Show Lane", t.showAutomation, v => _ctx.Session.Modify("Toggle Automation Lane", d => Get(d).showAutomation = v, StudioChange.Structure),
                "Show the automation lane under this track in the timeline"));
            int lanes = 0;
            foreach (var info in AutomationTargetInfo.ForTrack(t))
            {
                var lane = t.FindLane(info.TargetId);
                if (lane == null || lane.points.Count == 0) continue;
                lanes++;
                var id = info.TargetId;
                var row = UIUtil.Row(UIUtil.Label(info.Name, "acs-param__label", "acs-grow"), UIUtil.Label($"{lane.points.Count} pts", "acs-param__hint"),
                    new StudioButton(Icon.Curve, null, () => _ctx.Session.Modify("Show Automation", d => { Get(d).showAutomation = true; Get(d).automationTarget = id; }, StudioChange.Structure), "Show in timeline", "acs-btn--xs"),
                    new StudioButton(Icon.Trash, null, () => _ctx.Session.Modify("Delete Automation Lane", d => Get(d).automation.RemoveAll(l => l.targetId == id), StudioChange.Structure), "Delete lane", "acs-btn--xs"));
                auto.Body.Add(row);
            }
            if (lanes == 0) auto.Body.Add(UIUtil.Label("No automation yet. Show the lane and click in it to add points (gain, pan or any effect parameter).", "acs-hint"));
            _scroll.Add(auto);
        }

        // ------------------------------------------------------------- Master / Recipe

        private static readonly int[] Rates = { 22050, 32000, 44100, 48000, 96000 };

        private void BuildMaster()
        {
            var d0 = _ctx.Data;
            SetHeader(d0.name, "Recipe & Master Bus", StudioTheme.Accent);
            var card = new SectionCard("Recipe");
            card.Body.Add(Fields.Text("Name", d0.name, v => _ctx.Session.Modify("Rename Recipe", d => d.name = v)));
            int ri = Array.IndexOf(Rates, d0.sampleRate);
            var rateNames = Array.ConvertAll(Rates, r => $"{r} Hz");
            if (ri < 0) { ri = 3; }
            card.Body.Add(Fields.Popup("Sample Rate", rateNames, ri, i => _ctx.Session.Modify("Change Sample Rate", d => TimelineEditing.ChangeSampleRate(d, Rates[i]), StudioChange.Structure)));
            card.Body.Add(Fields.Enum("Channels", d0.channels, v => _ctx.Session.Modify("Change Channels", d => d.channels = v, StudioChange.Structure)));
            card.Body.Add(Fields.Db(_ctx, "Master Gain", d0.masterGainDb, "Master Gain", (d, v) => d.masterGainDb = v, this, -60f, 12f));
            card.Body.Add(Fields.Info("Length", $"{d0.ContentEnd / (double)d0.sampleRate:0.000} s  ·  {d0.tracks.Count} tracks"));
            var res = _ctx.Render.Current;
            if (res != null)
            {
                card.Body.Add(Fields.Info("Mix Peak", $"{DspMath.GainToDb(res.Peak):0.0} dBFS" + (res.ClippedSamples > 0 ? $"  ·  {res.ClippedSamples} clipped samples" : "")));
                if (res.ClippedSamples > 0) card.Body.Add(Fields.Warning("The mix exceeds 0 dBFS. Lower gains, add a Limiter, or enable normalization on export."));
            }
            _scroll.Add(card);

            var rack = new SectionCard("Master Effects");
            rack.Body.Add(new EffectsRack(_ctx, "Master", d => d.masterEffects));
            _scroll.Add(rack);

            var loop = new SectionCard("Loop Region");
            loop.Body.Add(Fields.Switch("Enabled", d0.loopRegion.enabled, v => _ctx.Session.Modify("Toggle Loop Region", d => d.loopRegion.enabled = v)));
            loop.Body.Add(Fields.Time(_ctx, "Start", d0.loopRegion.start, "Loop Start", (d, v) => d.loopRegion.start = Math.Min(v, d.loopRegion.end), this));
            loop.Body.Add(Fields.Time(_ctx, "End", d0.loopRegion.end, "Loop End", (d, v) => d.loopRegion.end = Math.Max(v, d.loopRegion.start), this));
            loop.Body.Add(UIUtil.Label("Shift-drag in the ruler to draw a loop region. Enable Loop in the transport to repeat it.", "acs-hint"));
            _scroll.Add(loop);

            var missing = _ctx.Session.FindMissingSources(_ctx.Library);
            var srcCard = new SectionCard("Sources");
            if (missing.Count == 0) srcCard.Body.Add(UIUtil.Label("All sources are available. Original assets are never modified.", "acs-hint"));
            foreach (var m in missing)
            {
                string why = m.sourceClip == null ? "missing" : "unreadable";
                srcCard.Body.Add(Fields.Warning($"{m.DisplayName}: source {why}", true));
            }
            _scroll.Add(srcCard);
        }
    }
}
