using System;
using System.Collections.Generic;
using AudioCreationStudio.DSP;
using AudioCreationStudio.Models;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AudioCreationStudio.EditorUI
{
    /// <summary>
    /// Effect chain editor (track or master). Order of cards is processing order. Every control drives
    /// real DSP parameters; filter/EQ/compressor cards show curves computed from the same DSP code.
    /// </summary>
    public sealed class EffectsRack : VisualElement
    {
        private readonly StudioContext _ctx;
        private readonly Func<RecipeData, List<EffectData>> _list;
        private readonly string _scopeName;
        private readonly VisualElement _cards;

        public EffectsRack(StudioContext ctx, string scopeName, Func<RecipeData, List<EffectData>> list)
        {
            _ctx = ctx;
            _list = list;
            _scopeName = scopeName;
            AddToClassList("acs-rack");
            var head = new VisualElement();
            head.AddToClassList("acs-rack__head");
            head.Add(UIUtil.Label("EFFECTS", "acs-overline"));
            head.Add(UIUtil.Spacer());
            var addBtn = new StudioButton(Icon.Plus, "Add Effect", null, "Add an effect to the end of the chain", "acs-btn--sm", "acs-btn--soft");
            addBtn.SetAction(() => ShowAddMenu(addBtn));
            head.Add(addBtn);
            var chainBtn = new StudioButton(Icon.More, null, null, "Effect chain presets", "acs-btn--sm");
            chainBtn.SetAction(() => ShowChainMenu(chainBtn));
            head.Add(chainBtn);
            Add(head);
            _cards = new VisualElement();
            Add(_cards);
            Rebuild();
        }

        private List<EffectData> List => _list(_ctx.Data);

        public void Rebuild()
        {
            _cards.Clear();
            var list = List;
            if (list == null) return;
            if (list.Count == 0)
            {
                var e = UIUtil.Label("No effects. Signal passes through unchanged.", "acs-rack__empty");
                _cards.Add(e);
                return;
            }
            for (int i = 0; i < list.Count; i++) _cards.Add(BuildCard(list[i], i, list.Count));
        }

        private void ShowAddMenu(VisualElement anchor)
        {
            var menu = new GenericMenu();
            foreach (var def in EffectRegistry.All)
            {
                var d = def;
                menu.AddItem(new GUIContent($"{d.Category}/{d.DisplayName}"), false, () =>
                {
                    _ctx.Session.Modify($"Add {d.DisplayName}", r => _list(r).Add(EffectRegistry.CreateData(d.TypeId)), StudioChange.Structure);
                });
            }
            menu.DropDown(anchor.worldBound);
        }

        private void ShowChainMenu(VisualElement anchor)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Save Chain as Preset…"), false, () =>
            {
                var name = $"{_scopeName} Chain";
                var payload = new EffectChainPayload();
                foreach (var e in List) payload.effects.Add(e.Clone());
                var p = PresetStore.Save(PresetKind.EffectChain, name, "Effect Chains", payload, $"{payload.effects.Count} effects");
                _ctx.Toast?.Invoke($"Saved chain preset '{p.displayName}'", false);
            });
            var chains = PresetStore.OfKind(PresetKind.EffectChain);
            if (chains.Count == 0) menu.AddDisabledItem(new GUIContent("Load Chain Preset/(none saved)"));
            foreach (var p in chains)
            {
                var preset = p;
                menu.AddItem(new GUIContent($"Load Chain Preset/{preset.displayName}"), false, () =>
                    _ctx.Session.Modify("Load Effect Chain", r => { var l = _list(r); l.Clear(); l.AddRange(PresetStore.LoadChain(preset)); }, StudioChange.Structure));
            }
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Bypass All"), false, () => _ctx.Session.Modify("Bypass All Effects", r => { foreach (var e in _list(r)) e.bypass = true; }));
            menu.AddItem(new GUIContent("Enable All"), false, () => _ctx.Session.Modify("Enable All Effects", r => { foreach (var e in _list(r)) { e.bypass = false; e.enabled = true; } }));
            menu.AddItem(new GUIContent("Remove All"), false, () => _ctx.Session.Modify("Clear Effects", r => _list(r).Clear(), StudioChange.Structure));
            menu.DropDown(anchor.worldBound);
        }

        private EffectData Find(RecipeData r, string uid)
        {
            foreach (var e in _list(r)) if (e.uid == uid) return e;
            return null;
        }

        private VisualElement BuildCard(EffectData fx, int index, int count)
        {
            var def = EffectRegistry.Get(fx.typeId);
            string uid = fx.uid;
            var card = new VisualElement();
            card.AddToClassList("acs-fx");
            card.EnableInClassList("acs-fx--off", !fx.enabled || fx.bypass);
            if (_ctx.Session.SelectedEffect == uid) card.AddToClassList("acs-fx--focus");

            var head = new VisualElement();
            head.AddToClassList("acs-fx__head");
            var power = new SwitchToggle(fx.enabled, "Enable/disable (disabled effects are removed from processing)");
            power.Changed += v => _ctx.Session.Modify(v ? "Enable Effect" : "Disable Effect", r => { var e = Find(r, uid); if (e != null) e.enabled = v; });
            head.Add(power);
            var title = UIUtil.Label(def?.DisplayName ?? $"Unknown ({fx.typeId})", "acs-fx__title");
            title.tooltip = def?.Description;
            head.Add(title);
            head.Add(UIUtil.Spacer());
            var bypass = new ToggleChip("BYP", "acs-chip--bypass", "Bypass (audio passes through unchanged)") { Value = fx.bypass };
            bypass.Changed += v => _ctx.Session.Modify(v ? "Bypass Effect" : "Unbypass Effect", r => { var e = Find(r, uid); if (e != null) e.bypass = v; });
            head.Add(bypass);
            var up = new StudioButton(Icon.Up, null, () => Move(uid, -1), "Move earlier in the chain", "acs-btn--xs");
            up.SetEnabled(index > 0);
            head.Add(up);
            var down = new StudioButton(Icon.Down, null, () => Move(uid, 1), "Move later in the chain", "acs-btn--xs");
            down.SetEnabled(index < count - 1);
            head.Add(down);
            var more = new StudioButton(Icon.More, null, null, "Effect options", "acs-btn--xs");
            more.SetAction(() => ShowEffectMenu(more, uid));
            head.Add(more);
            var chevron = new StudioButton(fx.expanded ? Icon.ChevronDown : Icon.ChevronRight, null,
                () => _ctx.Session.Modify("Toggle Effect Panel", r => { var e = Find(r, uid); if (e != null) e.expanded = !e.expanded; }, StudioChange.Structure),
                "Expand / collapse", "acs-btn--xs");
            head.Insert(0, chevron);
            card.Add(head);

            if (def == null || !fx.expanded) return card;

            var body = new VisualElement();
            body.AddToClassList("acs-fx__body");
            var values = def.Normalize(fx.values);

            FrequencyResponseView response = null;
            TransferCurveView transfer = null;
            Action<float[]> updateViz = null;
            if (def.TypeId == EffectTypes.LowPass || def.TypeId == EffectTypes.HighPass || def.TypeId == EffectTypes.Equalizer)
            {
                response = new FrequencyResponseView();
                body.Add(response);
                int sr = _ctx.SampleRate;
                bool hp = def.TypeId == EffectTypes.HighPass;
                updateViz = v =>
                {
                    if (def.TypeId == EffectTypes.Equalizer)
                    {
                        var k = EqualizerEffect.Design(v, sr);
                        response.SetResponse(f => k[0].MagnitudeAt(f, sr) * k[1].MagnitudeAt(f, sr) * k[2].MagnitudeAt(f, sr) * k[3].MagnitudeAt(f, sr));
                    }
                    else
                    {
                        var k = hp ? BiquadCoefficients.HighPass(sr, v[0], v[1]) : BiquadCoefficients.LowPass(sr, v[0], v[1]);
                        response.SetResponse(f => k.MagnitudeAt(f, sr));
                    }
                };
            }
            else if (def.TypeId == EffectTypes.Compressor)
            {
                transfer = new TransferCurveView();
                body.Add(transfer);
                updateViz = v => transfer.Set(v[0], v[1], v[4], v[5]);
            }
            updateViz?.Invoke(values);

            for (int i = 0; i < def.Params.Length; i++)
            {
                var p = def.Params[i];
                int pi = i;
                if (p.IsChoice)
                {
                    var seg = new SegmentedControl(p.Choices, (int)values[i]);
                    seg.Changed += k => _ctx.Session.Modify($"{p.Name}", r => { var e = Find(r, uid); if (e != null) { e.values = def.Normalize(e.values); e.values[pi] = k; } }, StudioChange.Content, this);
                    var row = new VisualElement();
                    row.AddToClassList("acs-param");
                    row.Add(UIUtil.Label(p.Name, "acs-param__label"));
                    row.Add(seg);
                    body.Add(row);
                    continue;
                }
                var pr = Fields.Param(_ctx, p.Name, ValueRange.FromParam(p), values[i], p.Format, $"{def.DisplayName} {p.Name}", (r, v) =>
                {
                    var e = Find(r, uid);
                    if (e == null) return;
                    e.values = def.Normalize(e.values);
                    e.values[pi] = p.Clamp(v);
                    values[pi] = e.values[pi];
                }, this);
                if (updateViz != null) pr.ValueChanged += _ => updateViz(values);
                if (p.Automatable && _ctx.Data.tracks.Exists(t => t.effects.Exists(e => e.uid == uid)))
                {
                    var t = _ctx.Data.tracks.Find(tt => tt.effects.Exists(e => e.uid == uid));
                    var lane = t.FindLane(AutomationTargets.Effect(uid, p.Id));
                    if (lane != null && lane.IsActive) pr.AddToClassList("acs-param--automated");
                }
                body.Add(pr);
            }
            card.Add(body);
            card.RegisterCallback<PointerDownEvent>(_ => _ctx.Session.SelectEffect(uid), TrickleDown.TrickleDown);
            return card;
        }

        private void Move(string uid, int dir)
        {
            _ctx.Session.Modify("Reorder Effect", r =>
            {
                var l = _list(r);
                int i = l.FindIndex(e => e.uid == uid);
                int j = i + dir;
                if (i < 0 || j < 0 || j >= l.Count) return;
                var tmp = l[i]; l[i] = l[j]; l[j] = tmp;
            }, StudioChange.Structure);
        }

        private void ShowEffectMenu(VisualElement anchor, string uid)
        {
            var fx = Find(_ctx.Data, uid);
            if (fx == null) return;
            var def = EffectRegistry.Get(fx.typeId);
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Reset to Defaults"), false, () =>
                _ctx.Session.Modify("Reset Effect", r => { var e = Find(r, uid); if (e != null && def != null) e.values = def.DefaultValues(); }, StudioChange.Structure));
            menu.AddItem(new GUIContent("Duplicate"), false, () => _ctx.Session.Modify("Duplicate Effect", r =>
            {
                var l = _list(r);
                int i = l.FindIndex(e => e.uid == uid);
                if (i >= 0) l.Insert(i + 1, l[i].Clone(true));
            }, StudioChange.Structure));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Save Preset…"), false, () =>
            {
                var p = PresetStore.Save(PresetKind.Effect, $"{def?.DisplayName ?? "Effect"} {DateTime.Now:HHmmss}", PresetStore.CategoryFor(fx), fx.Clone());
                _ctx.Toast?.Invoke($"Saved effect preset '{p.displayName}' (rename it in the Library)", false);
            });
            var presets = PresetStore.EffectPresets(fx.typeId);
            if (presets.Count == 0) menu.AddDisabledItem(new GUIContent("Load Preset/(none saved)"));
            foreach (var p in presets)
            {
                var preset = p;
                menu.AddItem(new GUIContent($"Load Preset/{preset.displayName}"), false, () =>
                {
                    var src = PresetStore.LoadEffect(preset);
                    if (src == null) return;
                    _ctx.Session.Modify("Load Effect Preset", r => { var e = Find(r, uid); if (e != null) e.values = (float[])src.values.Clone(); }, StudioChange.Structure);
                });
            }
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Remove"), false, () => _ctx.Session.Modify("Remove Effect", r => _list(r).RemoveAll(e => e.uid == uid), StudioChange.Structure));
            menu.DropDown(anchor.worldBound);
        }
    }
}
