using System;
using System.Collections.Generic;
using AudioCreationStudio.DSP;
using AudioCreationStudio.Models;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AudioCreationStudio.EditorUI
{
    /// <summary>Preset library: search, category filter, load/apply, duplicate, rename, delete.</summary>
    public sealed class LibraryView : VisualElement
    {
        private readonly StudioContext _ctx;
        private readonly VisualElement _cats, _grid;
        private readonly TextField _search;
        private string _category = "All";
        private string _renaming;

        private static readonly (string name, PresetKind? kind)[] Categories =
        {
            ("All", null), ("Generators", PresetKind.Generator), ("Effects", PresetKind.Effect),
            ("Effect Chains", PresetKind.EffectChain), ("Recipes", PresetKind.Recipe), ("Export Settings", PresetKind.ExportSettings)
        };

        public LibraryView(StudioContext ctx)
        {
            _ctx = ctx;
            AddToClassList("acs-lib");
            var left = new VisualElement();
            left.AddToClassList("acs-gen__left");
            left.Add(UIUtil.Label("LIBRARY", "acs-overline"));
            _cats = new VisualElement();
            left.Add(_cats);
            left.Add(UIUtil.Spacer());
            left.Add(UIUtil.Label("SAVE CURRENT", "acs-overline"));
            left.Add(new StudioButton(Icon.Save, "Recipe as Template", () =>
            {
                var p = PresetStore.Save(PresetKind.Recipe, _ctx.Data.name, "Recipes", _ctx.Data.Clone(), $"{_ctx.Data.tracks.Count} tracks");
                _ctx.Toast?.Invoke($"Saved recipe template '{p.displayName}'", false);
                Refresh();
            }, "Save the current recipe as a reusable template", "acs-btn--ghost"));
            left.Add(new StudioButton(Icon.Save, "Export Settings", () =>
            {
                var e = _ctx.Data.export;
                var p = PresetStore.Save(PresetKind.ExportSettings, $"{e.format} {(e.sampleRate == 0 ? _ctx.Data.sampleRate : e.sampleRate)} Hz", "Export", e.Clone());
                _ctx.Toast?.Invoke($"Saved export preset '{p.displayName}'", false);
                Refresh();
            }, "Save the current export settings", "acs-btn--ghost"));
            Add(left);

            var main = new VisualElement();
            main.AddToClassList("acs-loop__main");
            var head = new VisualElement();
            head.AddToClassList("acs-gen__head");
            head.Add(UIUtil.Label("Preset Library", "acs-gen__name"));
            head.Add(UIUtil.Spacer());
            _search = new TextField { value = "" };
            _search.AddToClassList("acs-search");
            _search.AddToClassList("acs-search--wide");
            _search.RegisterValueChangedCallback(_ => BuildGrid());
            head.Add(new IconElement(Icon.Search, 14));
            head.Add(_search);
            main.Add(head);
            var scroll = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            scroll.AddToClassList("acs-lib__scroll");
            _grid = new VisualElement();
            _grid.AddToClassList("acs-lib__grid");
            scroll.Add(_grid);
            main.Add(scroll);
            Add(main);
            Refresh();
        }

        public void Refresh()
        {
            var all = PresetStore.All();
            _cats.Clear();
            foreach (var (name, kind) in Categories)
            {
                int n = kind == null ? all.Count + GeneratorPresets.All.Count : all.FindAll(p => p.kind == kind).Count + (kind == PresetKind.Generator ? GeneratorPresets.All.Count : 0);
                var row = new VisualElement();
                row.AddToClassList("acs-listitem");
                if (name == _category) row.AddToClassList("acs-listitem--on");
                row.Add(UIUtil.Label(name, "acs-listitem__label"));
                row.Add(UIUtil.Label(n.ToString(), "acs-badge"));
                string cat = name;
                row.AddManipulator(new Clickable(() => { _category = cat; Refresh(); }));
                _cats.Add(row);
            }
            BuildGrid();
        }

        private void BuildGrid()
        {
            _grid.Clear();
            string q = (_search.value ?? "").Trim().ToLowerInvariant();
            PresetKind? kind = null;
            foreach (var (n, k) in Categories) if (n == _category) kind = k;
            bool Match(string a, string b) => q.Length == 0 || (a ?? "").ToLowerInvariant().Contains(q) || (b ?? "").ToLowerInvariant().Contains(q);

            int count = 0;
            if (kind == null || kind == PresetKind.Generator)
            {
                foreach (var f in GeneratorPresets.All)
                {
                    if (!Match(f.Name, f.Category)) continue;
                    var entry = f;
                    _grid.Add(Card(f.Name, "Generator", $"Factory · {f.Category}", null, () => _ctx.EditGenerator?.Invoke(entry.Create(), null), null));
                    count++;
                }
            }
            foreach (var p in PresetStore.All())
            {
                if (kind != null && p.kind != kind) continue;
                if (!Match(p.displayName, p.category)) continue;
                var preset = p;
                _grid.Add(Card(p.displayName, p.kind.ToString(), $"{p.category}{(string.IsNullOrEmpty(p.description) ? "" : " · " + p.description)}", preset, () => Apply(preset), preset));
                count++;
            }
            if (count == 0) _grid.Add(UIUtil.EmptyState(Icon.Grid, "Nothing here yet", "Save presets from the generator, effect cards (⋯ menu), effect chains or the buttons on the left."));
        }

        private VisualElement Card(string title, string kind, string subtitle, AudioPreset preset, Action apply, AudioPreset editable)
        {
            var c = new VisualElement();
            c.AddToClassList("acs-preset");
            var top = new VisualElement();
            top.AddToClassList("acs-row");
            var pill = UIUtil.Label(kind, "acs-pill");
            top.Add(pill);
            top.Add(UIUtil.Spacer());
            if (editable != null)
            {
                top.Add(new StudioButton(Icon.Duplicate, null, () => { PresetStore.Duplicate(editable); Refresh(); }, "Duplicate", "acs-btn--xs"));
                top.Add(new StudioButton(Icon.New, null, () => { _renaming = AssetDatabase.GetAssetPath(editable); BuildGrid(); }, "Rename", "acs-btn--xs"));
                top.Add(new StudioButton(Icon.Trash, null, () =>
                {
                    if (EditorUtility.DisplayDialog("Delete preset", $"Delete '{editable.displayName}'?", "Delete", "Cancel")) { PresetStore.Delete(editable); Refresh(); }
                }, "Delete", "acs-btn--xs"));
            }
            c.Add(top);
            if (editable != null && _renaming == AssetDatabase.GetAssetPath(editable))
            {
                var tf = new TextField { value = title, isDelayed = true };
                tf.AddToClassList("acs-field");
                tf.RegisterValueChangedCallback(e => { PresetStore.Rename(editable, e.newValue); _renaming = null; Refresh(); });
                c.Add(tf);
                tf.schedule.Execute(() => { tf.Focus(); tf.SelectAll(); });
            }
            else c.Add(UIUtil.Label(title, "acs-preset__title"));
            c.Add(UIUtil.Label(subtitle, "acs-preset__sub"));
            var applyBtn = new StudioButton(Icon.Check, kind == "Recipe" ? "Open as New" : "Load", apply, "Load / apply this preset", "acs-btn--soft", "acs-btn--sm");
            c.Add(applyBtn);
            return c;
        }

        private void Apply(AudioPreset p)
        {
            var s = _ctx.Session;
            switch (p.kind)
            {
                case PresetKind.Generator:
                {
                    var g = PresetStore.LoadGenerator(p);
                    if (g != null) _ctx.EditGenerator?.Invoke(g, null);
                    break;
                }
                case PresetKind.Effect:
                {
                    var e = PresetStore.LoadEffect(p);
                    if (e == null) return;
                    var track = s.PrimaryTrack;
                    if (track == null && !s.InspectMaster) { _ctx.Toast?.Invoke("Select a track (or the master) to add this effect", true); return; }
                    string uid = track?.uid;
                    s.Modify($"Add {p.displayName}", d => (uid == null ? d.masterEffects : d.FindTrack(uid).effects).Add(e.Clone(true)), StudioChange.Structure);
                    _ctx.Toast?.Invoke($"Added '{p.displayName}' to {(track?.name ?? "Master")}", false);
                    break;
                }
                case PresetKind.EffectChain:
                {
                    var chain = PresetStore.LoadChain(p);
                    var track = s.PrimaryTrack;
                    if (track == null && !s.InspectMaster) { _ctx.Toast?.Invoke("Select a track (or the master) to load this chain", true); return; }
                    string uid = track?.uid;
                    s.Modify("Load Effect Chain", d => { var l = uid == null ? d.masterEffects : d.FindTrack(uid).effects; l.Clear(); l.AddRange(chain); }, StudioChange.Structure);
                    _ctx.Toast?.Invoke($"Loaded chain onto {(track?.name ?? "Master")}", false);
                    break;
                }
                case PresetKind.Recipe:
                {
                    var r = PresetStore.Load<RecipeData>(p);
                    if (r == null) return;
                    r.EnsureValid();
                    s.OpenFromData(r);
                    _ctx.ShowTab?.Invoke("Arrange");
                    break;
                }
                case PresetKind.ExportSettings:
                {
                    var e = PresetStore.Load<ExportSettings>(p);
                    if (e != null) s.Modify("Apply Export Preset", d => d.export = e.Clone());
                    _ctx.Toast?.Invoke("Export settings applied", false);
                    break;
                }
            }
        }
    }
}
