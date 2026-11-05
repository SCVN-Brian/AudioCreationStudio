using System.Collections.Generic;
using AudioCreationStudio.DSP;
using AudioCreationStudio.Models;
using UnityEngine;
using UnityEngine.UIElements;

namespace AudioCreationStudio.EditorUI
{
    /// <summary>Console-style mixer: one channel strip per track plus the master strip. Meters show real rendered levels.</summary>
    public sealed class MixerView : VisualElement
    {
        private readonly StudioContext _ctx;
        private readonly ScrollView _strips;
        private readonly List<ChannelStrip> _channels = new List<ChannelStrip>();
        private ChannelStrip _master;

        public MixerView(StudioContext ctx)
        {
            _ctx = ctx;
            AddToClassList("acs-mixer");
            _strips = new ScrollView(ScrollViewMode.Horizontal);
            _strips.AddToClassList("acs-mixer__scroll");
            Add(_strips);
        }

        public void Rebuild()
        {
            _strips.Clear();
            _channels.Clear();
            foreach (var t in _ctx.Data.tracks)
            {
                var s = new ChannelStrip(_ctx, t.uid);
                _channels.Add(s);
                _strips.Add(s);
            }
            var add = new VisualElement();
            add.AddToClassList("acs-strip--add");
            add.Add(new StudioButton(Icon.Plus, null, () => _ctx.Commands.AddTrack(), "Add track", "acs-btn--soft"));
            _strips.Add(add);
            _master = new ChannelStrip(_ctx, null);
            _strips.Add(_master);
        }

        public void RefreshValues()
        {
            if (_channels.Count != _ctx.Data.tracks.Count) { Rebuild(); return; }
            for (int i = 0; i < _channels.Count; i++)
                if (_channels[i].TrackUid != _ctx.Data.tracks[i].uid) { Rebuild(); return; }
            foreach (var c in _channels) c.Refresh();
            _master?.Refresh();
        }

        public void UpdateMeters(double time)
        {
            foreach (var c in _channels) c.UpdateMeter(time);
            _master?.UpdateMeter(time);
        }
    }

    public sealed class ChannelStrip : VisualElement
    {
        private readonly StudioContext _ctx;
        public readonly string TrackUid;
        private readonly Label _name, _db;
        private readonly VisualElement _colorBar;
        private readonly VerticalFader _fader;
        private readonly LevelMeterElement _meter;
        private readonly Knob _pan;
        private readonly ToggleChip _mute, _solo;
        private readonly VisualElement _fxList;

        private bool IsMaster => TrackUid == null;
        private TrackData T => IsMaster ? null : _ctx.Data.FindTrack(TrackUid);

        public ChannelStrip(StudioContext ctx, string trackUid)
        {
            _ctx = ctx;
            TrackUid = trackUid;
            AddToClassList("acs-strip");
            if (IsMaster) AddToClassList("acs-strip--master");

            _colorBar = new VisualElement();
            _colorBar.AddToClassList("acs-strip__color");
            Add(_colorBar);
            _name = UIUtil.Label("", "acs-strip__name");
            Add(_name);

            _fxList = new VisualElement();
            _fxList.AddToClassList("acs-strip__fx");
            Add(_fxList);

            var panRow = new VisualElement();
            panRow.AddToClassList("acs-strip__pan");
            _pan = new Knob(new ValueRange(-1f, 1f, 0f, false, true), 0f, 30) { Formatter = v => "Pan " + AutomationTargetInfo.FormatPan(v) };
            _pan.EditBegan += () => _ctx.Session.BeginContinuous("Pan");
            _pan.ValueChanged += v => _ctx.Session.Continuous(d => { var t = d.FindTrack(TrackUid); if (t != null) t.pan = v; }, StudioChange.Content, this);
            _pan.EditEnded += () => _ctx.Session.EndContinuous();
            panRow.Add(_pan);
            if (IsMaster) _pan.style.visibility = Visibility.Hidden;
            Add(panRow);

            var fm = new VisualElement();
            fm.AddToClassList("acs-strip__faderrow");
            _fader = new VerticalFader(0f);
            _fader.EditBegan += () => _ctx.Session.BeginContinuous(IsMaster ? "Master Gain" : "Track Gain");
            _fader.ValueChanged += v =>
            {
                _ctx.Session.Continuous(d =>
                {
                    float db = Mathf.Max(-80f, v);
                    if (IsMaster) d.masterGainDb = db; else { var t = d.FindTrack(TrackUid); if (t != null) t.gainDb = Mathf.Min(12f, db); }
                }, StudioChange.Content, this);
                _db.text = FormatDb(v);
            };
            _fader.EditEnded += () => _ctx.Session.EndContinuous();
            fm.Add(_fader);
            _meter = new LevelMeterElement(false);
            _meter.AddToClassList("acs-strip__meter");
            fm.Add(_meter);
            Add(fm);

            _db = UIUtil.Label("", "acs-strip__db");
            Add(_db);

            var chips = new VisualElement();
            chips.AddToClassList("acs-strip__chips");
            _mute = new ToggleChip("M", "acs-chip--mute", "Mute");
            _mute.Changed += v => _ctx.Session.Modify("Mute Track", d => { var t = d.FindTrack(TrackUid); if (t != null) t.mute = v; });
            _solo = new ToggleChip("S", "acs-chip--solo", "Solo");
            _solo.Changed += v => _ctx.Session.Modify("Solo Track", d => { var t = d.FindTrack(TrackUid); if (t != null) t.solo = v; });
            chips.Add(_mute);
            chips.Add(_solo);
            if (IsMaster) chips.style.visibility = Visibility.Hidden;
            Add(chips);

            this.AddManipulator(new Clickable(() =>
            {
                if (IsMaster) _ctx.Session.SelectMaster(); else _ctx.Session.SelectTrack(TrackUid);
            }));
            Refresh();
        }

        private static string FormatDb(float db) => db <= -79f ? "-∞" : $"{db:+0.0;-0.0;0.0}";

        public void Refresh()
        {
            var data = _ctx.Data;
            List<EffectData> fx;
            if (IsMaster)
            {
                _name.text = "MASTER";
                _colorBar.style.backgroundColor = StudioTheme.Text;
                _fader.ValueDb = data.masterGainDb;
                _db.text = FormatDb(data.masterGainDb);
                fx = data.masterEffects;
                EnableInClassList("acs-strip--selected", _ctx.Session.InspectMaster);
            }
            else
            {
                var t = T;
                if (t == null) return;
                var c = new Color(t.color.r, t.color.g, t.color.b, 1f);
                _name.text = t.name;
                _colorBar.style.backgroundColor = c;
                _fader.ValueDb = t.gainDb;
                _fader.CapColor = c;
                _db.text = FormatDb(t.gainDb);
                _pan.Value = t.pan;
                _pan.ArcColor = c;
                _mute.Value = t.mute;
                _solo.Value = t.solo;
                fx = t.effects;
                EnableInClassList("acs-strip--selected", _ctx.Session.SelectedTrack == TrackUid && !_ctx.Session.InspectMaster);
                bool anySolo = data.tracks.Exists(x => x.solo);
                EnableInClassList("acs-strip--muted", t.mute || (anySolo && !t.solo));
            }
            _fxList.Clear();
            if (fx.Count == 0) _fxList.Add(UIUtil.Label("No FX", "acs-strip__fxempty"));
            foreach (var e in fx)
            {
                var def = EffectRegistry.Get(e.typeId);
                var l = UIUtil.Label(def?.DisplayName ?? e.typeId, "acs-strip__fxitem");
                l.EnableInClassList("acs-strip__fxitem--off", !e.enabled || e.bypass);
                l.tooltip = e.bypass ? "Bypassed" : e.enabled ? "Active" : "Disabled";
                _fxList.Add(l);
            }
        }

        public void UpdateMeter(double time)
        {
            var reading = IsMaster ? _ctx.Transport.MasterReading() : _ctx.Transport.TrackReading(TrackUid);
            if (reading.PeakL > 0 || _meter.IsActive) _meter.Push(reading, time);
        }
    }
}
