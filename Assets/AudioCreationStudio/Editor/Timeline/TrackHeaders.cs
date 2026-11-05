using System;
using System.Collections.Generic;
using AudioCreationStudio.Editing;
using AudioCreationStudio.Models;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AudioCreationStudio.EditorUI
{
    /// <summary>Left panel with one header per track, vertically synchronized with the timeline.</summary>
    public sealed class TrackHeaderPanel : VisualElement
    {
        private readonly StudioContext _ctx;
        private readonly VisualElement _content;
        private readonly List<TrackHeaderElement> _headers = new List<TrackHeaderElement>();
        private readonly VisualElement _insertLine;

        public TrackHeaderPanel(StudioContext ctx)
        {
            _ctx = ctx;
            AddToClassList("acs-trackpanel");
            var top = new VisualElement();
            top.AddToClassList("acs-trackpanel__top");
            top.style.height = TimelineViewState.RulerHeight;
            top.Add(UIUtil.Label("TRACKS", "acs-overline"));
            top.Add(UIUtil.Spacer());
            top.Add(new StudioButton(Icon.Plus, null, () => _ctx.Commands.AddTrack(), "Add track", "acs-btn--sm"));
            Add(top);
            var clip = new VisualElement();
            clip.AddToClassList("acs-trackpanel__viewport");
            Add(clip);
            _content = new VisualElement();
            _content.AddToClassList("acs-trackpanel__content");
            clip.Add(_content);
            _insertLine = new VisualElement();
            _insertLine.AddToClassList("acs-insert-line");
            _insertLine.style.display = DisplayStyle.None;
            clip.Add(_insertLine);
            clip.RegisterCallback<WheelEvent>(e =>
            {
                float maxY = Mathf.Max(0, TimelineViewState.ContentHeight(_ctx.Data) - clip.contentRect.height + 40);
                _ctx.Session.View.scrollY = Mathf.Clamp(_ctx.Session.View.scrollY + e.delta.y * 18f, 0, maxY);
                _ctx.Session.Notify(StudioChange.View);
                e.StopPropagation();
            });
            var addRow = new VisualElement();
            addRow.AddToClassList("acs-trackpanel__add");
            addRow.Add(new StudioButton(Icon.Plus, "Add Track", () => _ctx.Commands.AddTrack(), "Add a new empty track", "acs-btn--ghost"));
            _content.Add(addRow);
        }

        public void Rebuild()
        {
            foreach (var h in _headers) h.RemoveFromHierarchy();
            _headers.Clear();
            int i = 0;
            foreach (var t in _ctx.Data.tracks)
            {
                var h = new TrackHeaderElement(_ctx, t.uid, this);
                _content.Insert(i++, h);
                _headers.Add(h);
            }
            SyncScroll();
        }

        public void RefreshValues()
        {
            if (_headers.Count != _ctx.Data.tracks.Count) { Rebuild(); return; }
            for (int i = 0; i < _headers.Count; i++)
            {
                if (_headers[i].TrackUid != _ctx.Data.tracks[i].uid) { Rebuild(); return; }
                _headers[i].Refresh();
            }
        }

        public void SyncScroll() => _content.style.top = -_ctx.Session.View.scrollY;

        public void UpdateMeters(double time)
        {
            foreach (var h in _headers) h.UpdateMeter(time);
        }

        internal int IndexAtY(float panelY)
        {
            float y = panelY - TimelineViewState.RulerHeight + _ctx.Session.View.scrollY;
            float top = 0;
            var tracks = _ctx.Data.tracks;
            for (int i = 0; i < tracks.Count; i++)
            {
                float h = TimelineViewState.RowHeight(tracks[i]);
                if (y < top + h * 0.5f) return i;
                top += h;
            }
            return tracks.Count;
        }

        internal void ShowInsert(int index)
        {
            if (index < 0) { _insertLine.style.display = DisplayStyle.None; return; }
            float y = -_ctx.Session.View.scrollY;
            for (int i = 0; i < index && i < _ctx.Data.tracks.Count; i++) y += TimelineViewState.RowHeight(_ctx.Data.tracks[i]);
            _insertLine.style.display = DisplayStyle.Flex;
            _insertLine.style.top = y - 1;
        }
    }

    public sealed class TrackHeaderElement : VisualElement
    {
        private readonly StudioContext _ctx;
        private readonly TrackHeaderPanel _panel;
        public readonly string TrackUid;
        private readonly VisualElement _color;
        private readonly Label _name;
        private readonly TextField _rename;
        private readonly ToggleChip _mute, _solo;
        private readonly StudioButton _autoBtn;
        private readonly Label _fx;
        private readonly ValueSlider _gain;
        private readonly Label _gainLabel;
        private readonly Knob _pan;
        private readonly LevelMeterElement _meter;
        private readonly VisualElement _autoRow;
        private readonly Label _autoTarget;
        private readonly SwitchToggle _autoEnabled;
        private bool _reorderPending, _reordering;
        private Vector2 _downPos;

        private TrackData T => _ctx.Data.FindTrack(TrackUid);

        public TrackHeaderElement(StudioContext ctx, string uid, TrackHeaderPanel panel)
        {
            _ctx = ctx;
            _panel = panel;
            TrackUid = uid;
            AddToClassList("acs-track");

            _color = new VisualElement();
            _color.AddToClassList("acs-track__color");
            _color.tooltip = "Track color (click to change)";
            _color.AddManipulator(new Clickable(ShowColorMenu));
            Add(_color);

            var main = new VisualElement();
            main.AddToClassList("acs-track__main");
            Add(main);

            var row1 = new VisualElement();
            row1.AddToClassList("acs-track__row");
            _name = new Label();
            _name.AddToClassList("acs-track__name");
            _name.tooltip = "Double-click to rename · drag to reorder";
            _rename = new TextField { isDelayed = true };
            _rename.AddToClassList("acs-track__rename");
            _rename.style.display = DisplayStyle.None;
            _rename.RegisterValueChangedCallback(e => Rename(e.newValue));
            _rename.RegisterCallback<FocusOutEvent>(_ => EndRename());
            row1.Add(_name);
            row1.Add(_rename);
            row1.Add(UIUtil.Spacer());
            _fx = new Label();
            _fx.AddToClassList("acs-badge");
            _fx.tooltip = "Effects on this track (select the track to edit them in the Inspector)";
            row1.Add(_fx);
            _mute = new ToggleChip("M", "acs-chip--mute", "Mute");
            _mute.Changed += v => _ctx.Session.Modify(v ? "Mute Track" : "Unmute Track", d => { var t = d.FindTrack(TrackUid); if (t != null) t.mute = v; });
            _solo = new ToggleChip("S", "acs-chip--solo", "Solo");
            _solo.Changed += v => _ctx.Session.Modify(v ? "Solo Track" : "Unsolo Track", d => { var t = d.FindTrack(TrackUid); if (t != null) t.solo = v; });
            row1.Add(_mute);
            row1.Add(_solo);
            _autoBtn = new StudioButton(Icon.Curve, null, ToggleAutomation, "Show automation lane", "acs-btn--xs");
            row1.Add(_autoBtn);
            row1.Add(new StudioButton(Icon.More, null, ShowMenu, "Track options", "acs-btn--xs"));
            main.Add(row1);

            var row2 = new VisualElement();
            row2.AddToClassList("acs-track__row");
            _gain = new ValueSlider(new ValueRange(-60f, 12f, 0f, false, true), 0f);
            _gain.AddToClassList("acs-track__gain");
            _gain.tooltip = "Track gain (double-click to reset)";
            _gain.EditBegan += () => _ctx.Session.BeginContinuous("Track Gain");
            _gain.ValueChanged += v => { _ctx.Session.Continuous(d => { var t = d.FindTrack(TrackUid); if (t != null) t.gainDb = v; }, StudioChange.Content, this); _gainLabel.text = $"{v:0.0} dB"; };
            _gain.EditEnded += () => _ctx.Session.EndContinuous();
            row2.Add(_gain);
            _gainLabel = new Label();
            _gainLabel.AddToClassList("acs-track__gainlabel");
            row2.Add(_gainLabel);
            _pan = new Knob(new ValueRange(-1f, 1f, 0f, false, true), 0f, 22) { Formatter = v => "Pan " + AutomationTargetInfo.FormatPan(v) };
            _pan.EditBegan += () => _ctx.Session.BeginContinuous("Track Pan");
            _pan.ValueChanged += v => _ctx.Session.Continuous(d => { var t = d.FindTrack(TrackUid); if (t != null) t.pan = v; }, StudioChange.Content, this);
            _pan.EditEnded += () => _ctx.Session.EndContinuous();
            row2.Add(_pan);
            main.Add(row2);

            _meter = new LevelMeterElement(true);
            _meter.AddToClassList("acs-track__meter");
            main.Add(_meter);

            _autoRow = new VisualElement();
            _autoRow.AddToClassList("acs-track__auto");
            _autoTarget = new Label();
            _autoTarget.AddToClassList("acs-dropdown");
            _autoTarget.tooltip = "Choose the parameter shown in the automation lane";
            _autoTarget.AddManipulator(new Clickable(ShowTargetMenu));
            _autoRow.Add(_autoTarget);
            var r = new VisualElement();
            r.AddToClassList("acs-row");
            _autoEnabled = new SwitchToggle(true, "Enable / bypass this automation lane");
            _autoEnabled.Changed += v => _ctx.Session.Modify("Toggle Automation", d =>
            {
                var t = d.FindTrack(TrackUid);
                if (t != null) t.GetOrCreateLane(t.automationTarget).enabled = v;
            });
            r.Add(UIUtil.Label("Active", "acs-label-dim"));
            r.Add(_autoEnabled);
            r.Add(UIUtil.Spacer());
            r.Add(new StudioButton(Icon.Trash, null, () => _ctx.Session.Modify("Clear Automation", d =>
            {
                var t = d.FindTrack(TrackUid);
                t?.FindLane(t.automationTarget)?.points.Clear();
            }), "Clear all points in this lane", "acs-btn--xs"));
            _autoRow.Add(r);
            main.Add(_autoRow);

            RegisterCallback<PointerDownEvent>(OnDown);
            RegisterCallback<PointerMoveEvent>(OnMove);
            RegisterCallback<PointerUpEvent>(OnUp);
            Refresh();
        }

        public void Refresh()
        {
            var t = T;
            if (t == null) return;
            style.height = TimelineViewState.RowHeight(t);
            _color.style.backgroundColor = new Color(t.color.r, t.color.g, t.color.b, 1f);
            _name.text = t.name;
            _mute.Value = t.mute;
            _solo.Value = t.solo;
            _gain.Value = t.gainDb;
            _gain.FillColor = new Color(t.color.r, t.color.g, t.color.b, 1f);
            _gainLabel.text = $"{t.gainDb:0.0} dB";
            _pan.Value = t.pan;
            _pan.ArcColor = _gain.FillColor;
            int active = 0;
            foreach (var e in t.effects) if (e.enabled && !e.bypass) active++;
            _fx.text = t.effects.Count == 0 ? "FX" : $"FX {active}";
            _fx.EnableInClassList("acs-badge--on", active > 0);
            _autoBtn.Toggled = t.showAutomation;
            _autoRow.style.display = t.showAutomation ? DisplayStyle.Flex : DisplayStyle.None;
            var info = AutomationTargetInfo.Find(AutomationTargetInfo.ForTrack(t), t.automationTarget);
            _autoTarget.text = (info?.Name ?? "Gain") + "  ▾";
            var lane = t.FindLane(t.automationTarget);
            _autoEnabled.Value = lane == null || lane.enabled;
            bool anySolo = false;
            foreach (var x in _ctx.Data.tracks) anySolo |= x.solo;
            EnableInClassList("acs-track--selected", _ctx.Session.SelectedTrack == TrackUid && !_ctx.Session.InspectMaster);
            EnableInClassList("acs-track--muted", t.mute || (anySolo && !t.solo));
        }

        public void UpdateMeter(double time)
        {
            var reading = _ctx.Transport.TrackReading(TrackUid);
            if (reading.PeakL > 0 || _meter.IsActive) _meter.Push(reading, time);
        }

        private void ToggleAutomation() =>
            _ctx.Session.Modify("Toggle Automation Lane", d => { var t = d.FindTrack(TrackUid); if (t != null) t.showAutomation = !t.showAutomation; }, StudioChange.Structure);

        private void ShowTargetMenu()
        {
            var t = T;
            if (t == null) return;
            var menu = new GenericMenu();
            foreach (var info in AutomationTargetInfo.ForTrack(t))
            {
                var id = info.TargetId;
                var lane = t.FindLane(id);
                string suffix = lane != null && lane.points.Count > 0 ? $"  ({lane.points.Count})" : "";
                menu.AddItem(new GUIContent(info.Name.Replace(" › ", "/") + suffix), t.automationTarget == id,
                    () => _ctx.Session.Modify("Select Automation Target", d => { var tt = d.FindTrack(TrackUid); if (tt != null) tt.automationTarget = id; }));
            }
            menu.DropDown(_autoTarget.worldBound);
        }

        private void ShowColorMenu()
        {
            var menu = new GenericMenu();
            string[] names = { "Blue", "Teal", "Amber", "Violet", "Rose", "Cyan", "Orange", "Lime" };
            for (int i = 0; i < TimelineEditing.TrackPalette.Length; i++)
            {
                var c = TimelineEditing.TrackPalette[i];
                menu.AddItem(new GUIContent(names[i]), false, () => _ctx.Session.Modify("Track Color", d => { var t = d.FindTrack(TrackUid); if (t != null) t.color = c; }));
            }
            menu.DropDown(_color.worldBound);
        }

        private void ShowMenu()
        {
            var menu = new GenericMenu();
            int idx = _ctx.Data.tracks.FindIndex(t => t.uid == TrackUid);
            menu.AddItem(new GUIContent("Preview Track"), false, () => _ctx.Transport.PlayTrack(TrackUid));
            menu.AddItem(new GUIContent("Rename"), false, BeginRename);
            menu.AddItem(new GUIContent("Duplicate Track"), false, () => _ctx.Commands.DuplicateTrack(TrackUid));
            menu.AddSeparator("");
            if (idx > 0) menu.AddItem(new GUIContent("Move Up"), false, () => _ctx.Commands.MoveTrack(TrackUid, idx - 1)); else menu.AddDisabledItem(new GUIContent("Move Up"));
            if (idx < _ctx.Data.tracks.Count - 1) menu.AddItem(new GUIContent("Move Down"), false, () => _ctx.Commands.MoveTrack(TrackUid, idx + 1)); else menu.AddDisabledItem(new GUIContent("Move Down"));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Delete Track"), false, () => _ctx.Commands.DeleteTrack(TrackUid));
            menu.ShowAsContext();
        }

        private void BeginRename()
        {
            _name.style.display = DisplayStyle.None;
            _rename.style.display = DisplayStyle.Flex;
            _rename.SetValueWithoutNotify(T?.name ?? "");
            _rename.schedule.Execute(() => { _rename.Focus(); _rename.SelectAll(); });
        }

        private void Rename(string name)
        {
            if (!string.IsNullOrWhiteSpace(name)) _ctx.Session.Modify("Rename Track", d => { var t = d.FindTrack(TrackUid); if (t != null) t.name = name.Trim(); });
            EndRename();
        }

        private void EndRename()
        {
            _rename.style.display = DisplayStyle.None;
            _name.style.display = DisplayStyle.Flex;
        }

        private bool IsInteractiveTarget(IEventHandler target)
        {
            for (var ve = target as VisualElement; ve != null && ve != this; ve = ve.parent)
            {
                if (ve is TextField) return true;
                foreach (var cls in new[] { "acs-btn", "acs-chip", "acs-switch", "acs-dropdown", "acs-slider", "acs-knob", "acs-track__color", "acs-meter" })
                    if (ve.ClassListContains(cls)) return true;
            }
            return false;
        }

        private void OnDown(PointerDownEvent e)
        {
            if (e.button != 0 || IsInteractiveTarget(e.target)) return;
            if (e.clickCount == 2 && _name.worldBound.Contains(e.position)) { BeginRename(); e.StopPropagation(); return; }
            _ctx.Session.SelectTrack(TrackUid);
            _reorderPending = true;
            _downPos = e.position;
            this.CapturePointer(e.pointerId);
        }

        private void OnMove(PointerMoveEvent e)
        {
            if (!_reorderPending || !this.HasPointerCapture(e.pointerId)) return;
            if (!_reordering && Mathf.Abs(e.position.y - _downPos.y) > 6) { _reordering = true; AddToClassList("acs-track--dragging"); }
            if (_reordering) _panel.ShowInsert(_panel.IndexAtY(_panel.WorldToLocal(e.position).y));
        }

        private void OnUp(PointerUpEvent e)
        {
            if (!this.HasPointerCapture(e.pointerId)) return;
            this.ReleasePointer(e.pointerId);
            if (_reordering)
            {
                int target = _panel.IndexAtY(_panel.WorldToLocal(e.position).y);
                int current = _ctx.Data.tracks.FindIndex(t => t.uid == TrackUid);
                if (target > current) target--;
                _panel.ShowInsert(-1);
                RemoveFromClassList("acs-track--dragging");
                if (target != current) _ctx.Commands.MoveTrack(TrackUid, target);
            }
            _reorderPending = _reordering = false;
        }
    }
}
