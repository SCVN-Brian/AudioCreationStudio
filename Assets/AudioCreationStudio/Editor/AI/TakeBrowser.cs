using System;
using System.Collections.Generic;
using System.IO;
using AudioCreationStudio.DSP;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AudioCreationStudio.EditorUI
{
    /// <summary>
    /// Folder-organised library for generated takes: a folder tree on the left, the takes of the selected folder on
    /// the right. Takes and folders are dragged onto folders to move them (drag from the Project window copies a clip
    /// in), right-click opens every action, and new generations are saved into the selected folder.
    /// </summary>
    public sealed class TakeBrowser : VisualElement
    {
        public sealed class Take
        {
            public string Path;
            public AudioClip Clip;
            public AudioBuffer Buffer;
            public string Prompt;
            public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);
        }

        private const string AllView = "*all*";
        private const string DragTakesKey = "ACS.TakePaths";
        private const string DragFolderKey = "ACS.FolderPath";
        private const string PrefFolder = "ACS.TakeBrowser.Folder";
        private const string PrefExpanded = "ACS.TakeBrowser.Expanded";
        private const int PageSize = 50;
        private const float SpringOpenSeconds = 0.6f;

        private readonly StudioContext _ctx;
        private readonly string _root;
        private readonly string _rootLabel;
        private readonly ScrollView _tree, _list;
        private readonly VisualElement _selBar, _listHead;
        private readonly Label _selCount, _crumb, _crumbCount, _savePill, _searchHint;
        private readonly TextField _search;
        private readonly HashSet<string> _expanded = new HashSet<string>();
        private readonly HashSet<string> _selected = new HashSet<string>();
        private readonly Dictionary<string, Take> _cache = new Dictionary<string, Take>();
        private readonly List<string> _visible = new List<string>();
        private string _folder;
        private string _anchor;
        private int _shown = PageSize;
        private bool _renaming;
        private VisualElement _dropRow;
        private string _hoverFolder;
        private double _hoverSince;
        private IVisualElementScheduledItem _pendingRefresh;

        /// <summary>Extra per-take buttons (play, insert…) supplied by the host panel.</summary>
        public Func<Take, IEnumerable<VisualElement>> BuildActions;
        /// <summary>Double-click / Space on a take.</summary>
        public Action<Take> Play;
        /// <summary>"Insert" in the selection bar and context menu.</summary>
        public Action<List<Take>> InsertMany;

        /// <summary>Folder new generations are saved into (the root while "All Takes" is shown).</summary>
        public string SaveFolder => _folder == AllView || !AssetDatabase.IsValidFolder(_folder) ? _root : _folder;

        public TakeBrowser(StudioContext ctx, string root, string rootLabel)
        {
            _ctx = ctx;
            _root = TakeStore.Normalize(root);
            _rootLabel = rootLabel;
            AddToClassList("acs-takes");

            _folder = EditorPrefs.GetString(PrefFolder, AllView);
            foreach (var e in EditorPrefs.GetString(PrefExpanded, _root).Split('|'))
                if (!string.IsNullOrEmpty(e)) _expanded.Add(e);
            _expanded.Add(_root);

            // Toolbar: title, search, new folder
            var bar = new VisualElement();
            bar.AddToClassList("acs-row");
            bar.AddToClassList("acs-takes__bar");
            bar.Add(UIUtil.Label("TAKE LIBRARY", "acs-overline", "acs-takes__title"));
            bar.Add(UIUtil.Spacer());
            var searchBox = new VisualElement();
            searchBox.AddToClassList("acs-takes__search");
            searchBox.Add(new IconElement(Icon.Search, 13));
            _search = new TextField();
            _search.AddToClassList("acs-field");
            _search.RegisterValueChangedCallback(e => { _searchHint.style.display = string.IsNullOrEmpty(e.newValue) ? DisplayStyle.Flex : DisplayStyle.None; _shown = PageSize; RebuildList(); });
            _search.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Escape) { _search.value = ""; e.StopPropagation(); } });
            searchBox.Add(_search);
            _searchHint = UIUtil.Label("Search takes", "acs-takes__placeholder");
            _searchHint.pickingMode = PickingMode.Ignore;
            searchBox.Add(_searchHint);
            bar.Add(searchBox);
            bar.Add(new StudioButton(Icon.FolderPlus, "New Folder", () => NewFolder(SaveFolder), "Create a folder inside the selected folder", "acs-btn--sm", "acs-btn--soft"));
            Add(bar);

            // Selection bar (shown while takes are selected)
            _selBar = new VisualElement();
            _selBar.AddToClassList("acs-row");
            _selBar.AddToClassList("acs-takes__selbar");
            _selCount = UIUtil.Label("", "acs-takes__selcount");
            _selBar.Add(_selCount);
            StudioButton moveBtn = null;
            moveBtn = new StudioButton(Icon.Folder, "Move to…", () => ShowMoveMenu(SelectedPaths(), moveBtn.worldBound), "Move the selected takes into another folder", "acs-btn--sm");
            _selBar.Add(moveBtn);
            _selBar.Add(new StudioButton(Icon.Plus, "Insert", () => InsertMany?.Invoke(SelectedTakes()), "Add the selected takes to the timeline at the playhead", "acs-btn--sm"));
            _selBar.Add(new StudioButton(Icon.Trash, "Delete", () => DeleteTakes(SelectedPaths()), "Delete the selected takes", "acs-btn--sm"));
            _selBar.Add(UIUtil.Spacer());
            _selBar.Add(new StudioButton(Icon.Close, null, ClearSelection, "Clear the selection (Esc)", "acs-btn--sm"));
            Add(_selBar);

            // Split: tree | list
            var split = new VisualElement();
            split.AddToClassList("acs-takes__split");
            _tree = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            _tree.AddToClassList("acs-takes__tree");
            split.Add(_tree);

            var right = new VisualElement();
            right.AddToClassList("acs-takes__right");
            _listHead = new VisualElement();
            _listHead.AddToClassList("acs-row");
            _listHead.AddToClassList("acs-takes__head");
            _crumb = UIUtil.Label("", "acs-takes__crumb");
            _listHead.Add(_crumb);
            _crumbCount = UIUtil.Label("", "acs-label-dim");
            _listHead.Add(_crumbCount);
            _listHead.Add(UIUtil.Spacer());
            _savePill = UIUtil.Label("", "acs-takes__savepill");
            _savePill.tooltip = "New ElevenLabs generations are saved into the selected folder";
            _listHead.Add(_savePill);
            right.Add(_listHead);
            _list = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            _list.AddToClassList("acs-takes__list");
            _list.focusable = true;
            _list.RegisterCallback<KeyDownEvent>(OnListKey);
            _list.RegisterCallback<MouseDownEvent>(e => { if (e.target == _list || e.target == _list.contentContainer) { ClearSelection(); _list.Focus(); } });
            RegisterDropTarget(right, () => _folder == AllView ? null : _folder, null);
            right.Add(_list);
            // Narrow list: action buttons drop their text (tooltips keep it) so the waveform keeps its room.
            // Very narrow list (e.g. half-width window): each row wraps onto two lines instead of overflowing.
            right.RegisterCallback<GeometryChangedEvent>(e =>
            {
                EnableInClassList("acs-takes--narrow", e.newRect.width < 900);
                EnableInClassList("acs-takes--tight", e.newRect.width < 620);
            });
            RegisterCallback<GeometryChangedEvent>(e => EnableInClassList("acs-takes--compact", e.newRect.width < 820));
            split.Add(right);
            Add(split);

            Add(UIUtil.Label("Drag takes onto a folder to move them · drag a folder onto another to nest it · drag clips in from the Project window to copy them here · Cmd/Ctrl- or Shift-click to select several · right-click for more · double-click to play",
                "acs-hint"));

            RegisterCallback<AttachToPanelEvent>(_ => { EditorApplication.projectChanged -= OnProjectChanged; EditorApplication.projectChanged += OnProjectChanged; });
            RegisterCallback<DetachFromPanelEvent>(_ => EditorApplication.projectChanged -= OnProjectChanged);
            RegisterCallback<DragExitedEvent>(_ => ClearDrop());
            RegisterCallback<DragUpdatedEvent>(_ => { DragAndDrop.visualMode = DragAndDropVisualMode.Rejected; ClearDrop(); });
            Refresh();
        }

        // ------------------------------------------------------------------ Public

        public void Refresh()
        {
            if (_folder != AllView && !AssetDatabase.IsValidFolder(_folder)) _folder = AssetDatabase.IsValidFolder(_root) ? _root : AllView;
            _selected.RemoveWhere(p => !File.Exists(p));
            RebuildTree();
            RebuildList();
        }

        /// <summary>Refreshes, selects and scrolls to a take (used right after a generation).</summary>
        public void Reveal(string path)
        {
            path = TakeStore.Normalize(path);
            _cache.Remove(path);
            _selected.Clear();
            _selected.Add(path);
            _anchor = path;
            Refresh();
            schedule.Execute(() =>
            {
                var row = _list.Q(className: "acs-take--sel");
                if (row != null) _list.ScrollTo(row);
            });
        }

        // ------------------------------------------------------------------ Tree

        private void RebuildTree()
        {
            _tree.Clear();
            var all = TreeRow(AllView, Icon.Layers, "All Takes", 0, TakeStore.CountTakes(_root, true), false);
            all.tooltip = "Every take in every folder";
            _tree.Add(all);
            if (!AssetDatabase.IsValidFolder(_root))
            {
                _tree.Add(UIUtil.Label("No folders yet. Generate a take or click New Folder.", "acs-hint", "acs-takes__treehint"));
                return;
            }
            AddFolderRows(_root, 0);
        }

        private void AddFolderRows(string folder, int depth)
        {
            var subs = TakeStore.SubFolders(folder);
            bool open = _expanded.Contains(folder);
            string label = folder == _root ? _rootLabel : TakeStore.Name(folder);
            var row = TreeRow(folder, open && subs.Count > 0 || folder == _folder ? Icon.FolderOpen : Icon.Folder, label, depth,
                TakeStore.CountTakes(folder, true), subs.Count > 0);
            _tree.Add(row);
            if (open)
                foreach (var s in subs) AddFolderRows(s, depth + 1);
        }

        private VisualElement TreeRow(string folder, Icon icon, string label, int depth, int count, bool hasChildren)
        {
            bool isAll = folder == AllView;
            bool isRoot = folder == _root;
            var row = new VisualElement();
            row.AddToClassList("acs-tree__row");
            row.EnableInClassList("acs-tree__row--sel", folder == _folder);
            row.style.paddingLeft = 6 + depth * 14;
            row.userData = folder;

            var chevron = new IconElement(_expanded.Contains(folder) ? Icon.ChevronDown : Icon.ChevronRight, 12);
            chevron.AddToClassList("acs-tree__chevron");
            chevron.style.visibility = hasChildren ? Visibility.Visible : Visibility.Hidden;
            if (hasChildren)
            {
                chevron.pickingMode = PickingMode.Position;
                chevron.RegisterCallback<MouseDownEvent>(e => { ToggleExpanded(folder); e.StopPropagation(); });
            }
            if (!isAll) row.Add(chevron);
            var ic = new IconElement(icon, 15);
            ic.AddToClassList("acs-tree__icon");
            row.Add(ic);
            var name = UIUtil.Label(label, "acs-tree__name");
            name.pickingMode = PickingMode.Ignore;
            row.Add(name);
            var badge = UIUtil.Label(count > 0 ? count.ToString() : "", "acs-tree__count");
            badge.pickingMode = PickingMode.Ignore;
            row.Add(badge);

            MakeDraggable(row,
                click: c =>
                {
                    if (c.Count == 2 && !isAll && !isRoot) { BeginRename(name, folder); return; }
                    SelectFolder(folder);
                },
                startDrag: isAll || isRoot ? (Action)null : () => StartFolderDrag(folder));

            if (!isAll)
            {
                RegisterDropTarget(row, () => folder, row);
                OnRightClick(row, () => FolderMenu(folder, name));
            }
            return row;
        }

        private void FolderMenu(string folder, Label name)
        {
            bool isRoot = folder == _root;
            var m = new GenericMenu();
            m.AddItem(new GUIContent("New Folder Inside"), false, () => NewFolder(folder));
            if (isRoot) m.AddDisabledItem(new GUIContent("Rename")); else m.AddItem(new GUIContent("Rename"), false, () => BeginRename(name, folder));
            m.AddSeparator("");
            m.AddItem(new GUIContent("Show in Project"), false, () => Ping(folder));
            m.AddSeparator("");
            if (isRoot) m.AddDisabledItem(new GUIContent("Delete Folder…")); else m.AddItem(new GUIContent("Delete Folder…"), false, () => DeleteFolder(folder));
            m.ShowAsContext();
        }

        private void SelectFolder(string folder)
        {
            if (_folder == folder) return;
            _folder = folder;
            EditorPrefs.SetString(PrefFolder, folder);
            _selected.Clear();
            _shown = PageSize;
            if (folder != AllView)
                for (var p = TakeStore.Parent(folder); TakeStore.IsInside(p, _root) && p != folder; p = TakeStore.Parent(p))
                {
                    _expanded.Add(p);
                    if (p == _root) break;
                }
            SaveExpanded();
            Refresh();
        }

        private void ToggleExpanded(string folder)
        {
            if (!_expanded.Remove(folder)) _expanded.Add(folder);
            SaveExpanded();
            RebuildTree();
        }

        private void SaveExpanded() => EditorPrefs.SetString(PrefExpanded, string.Join("|", _expanded));

        // ------------------------------------------------------------------ List

        private void RebuildList()
        {
            _list.Clear();
            _visible.Clear();
            bool all = _folder == AllView;
            string folder = all ? _root : _folder;
            string q = (_search.value ?? "").Trim();
            bool searching = q.Length > 0;

            _crumb.text = all ? "All Takes" : TakeStore.DisplayPath(folder, _root, _rootLabel);
            _savePill.text = "New takes  →  " + TakeStore.DisplayPath(SaveFolder, _root, _rootLabel);
            _savePill.EnableInClassList("acs-takes__savepill--root", SaveFolder == _root);

            var paths = TakeStore.Takes(folder, all || searching);
            if (searching)
                paths.RemoveAll(p => p.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0 &&
                                     (TakeStore.ReadPrompt(p) ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0);
            _visible.AddRange(paths);
            _crumbCount.text = searching ? $"  ·  {paths.Count} match{(paths.Count == 1 ? "" : "es")} for \"{q}\"" : $"  ·  {paths.Count} take{(paths.Count == 1 ? "" : "s")}";

            if (paths.Count == 0)
            {
                _list.Add(UIUtil.EmptyState(searching ? Icon.Search : Icon.FolderOpen,
                    searching ? "No matching takes" : "This folder is empty",
                    searching ? "Try another word, or clear the search (Esc)." :
                        all ? "Generate a sound above: it lands in the selected folder." :
                        "Drag takes here from another folder, or generate a sound above: new takes are saved into this folder."));
            }
            for (int i = 0; i < paths.Count && i < _shown; i++)
                _list.Add(TakeRow(paths[i], all || searching));
            if (paths.Count > _shown)
                _list.Add(new StudioButton(Icon.ChevronDown, $"Show {Math.Min(PageSize, paths.Count - _shown)} more ({paths.Count - _shown} hidden)", () => { _shown += PageSize; RebuildList(); }, null, "acs-btn--sm", "acs-takes__more"));
            UpdateSelectionBar();
        }

        private Take GetTake(string path)
        {
            if (_cache.TryGetValue(path, out var t) && t.Clip != null) return t;
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            AudioBuffer buf = null;
            if (clip != null) _ctx.Library.TryGetClip(clip, out buf, out _, out _);
            t = new Take { Path = path, Clip = clip, Buffer = buf, Prompt = TakeStore.ReadPrompt(path) };
            _cache[path] = t;
            return t;
        }

        private VisualElement TakeRow(string path, bool showFolder)
        {
            var take = GetTake(path);
            var row = new VisualElement();
            row.AddToClassList("acs-varrow");
            row.AddToClassList("acs-take");
            row.EnableInClassList("acs-take--sel", _selected.Contains(path));
            row.userData = path;
            row.tooltip = string.IsNullOrEmpty(take.Prompt) ? null : "Prompt: " + take.Prompt;

            var grip = new IconElement(Icon.Grip, 14);
            grip.AddToClassList("acs-take__grip");
            row.Add(grip);
            var info = new VisualElement();
            info.AddToClassList("acs-varrow__info");
            var name = UIUtil.Label(take.Name, "acs-varrow__name");
            info.Add(name);
            var meta = new VisualElement();
            meta.AddToClassList("acs-row");
            if (showFolder)
            {
                string parent = TakeStore.Parent(path);
                var chip = new VisualElement();
                chip.AddToClassList("acs-take__folderchip");
                chip.Add(new IconElement(Icon.Folder, 11));
                chip.Add(UIUtil.Label(TakeStore.DisplayPath(parent, _root, _rootLabel), "acs-take__foldername"));
                chip.tooltip = "Open this folder";
                chip.RegisterCallback<MouseDownEvent>(e => { if (e.button == 0) { SelectFolder(parent); e.StopPropagation(); } });
                meta.Add(chip);
            }
            meta.Add(UIUtil.Label(take.Buffer != null ? $"{take.Buffer.DurationSeconds:0.00} s · {take.Buffer.SampleRate / 1000f:0.#} kHz · {(take.Buffer.Channels == 1 ? "mono" : "stereo")}" : "not readable", "acs-label-dim"));
            info.Add(meta);
            row.Add(info);
            var wave = new BufferWaveformView(44);
            wave.AddToClassList("acs-varrow__wave");
            if (take.Buffer != null) wave.SetBuffer(take.Buffer);
            row.Add(wave);
            if (BuildActions != null)
                foreach (var a in BuildActions(take)) row.Add(a);
            StudioButton more = null;
            more = new StudioButton(Icon.More, null, () =>
            {
                if (!_selected.Contains(path)) SelectOnly(path);
                var m = new GenericMenu();
                TakeMenu(m, path, name);
                m.DropDown(more.worldBound);
            }, "Move, rename, delete…", "acs-btn--sm");
            row.Add(more);

            MakeDraggable(row,
                click: c =>
                {
                    if (c.Count == 2) { Play?.Invoke(take); return; }
                    ClickSelect(path, c.Toggle, c.Range);
                    _list.Focus();
                },
                startDrag: () =>
                {
                    if (!_selected.Contains(path)) SelectOnly(path);
                    StartTakeDrag(SelectedPaths());
                });
            OnRightClick(row, () =>
            {
                if (!_selected.Contains(path)) SelectOnly(path);
                var m = new GenericMenu();
                TakeMenu(m, path, name);
                m.ShowAsContext();
            });
            return row;
        }

        private void TakeMenu(GenericMenu m, string path, Label name)
        {
            var sel = SelectedPaths();
            string n = sel.Count > 1 ? $" {sel.Count} Takes" : "";
            var take = GetTake(path);
            m.AddItem(new GUIContent("Play"), false, () => Play?.Invoke(take));
            m.AddItem(new GUIContent($"Insert{n} into Timeline"), false, () => InsertMany?.Invoke(SelectedTakes()));
            m.AddSeparator("");
            foreach (var (label, folder) in MoveTargets())
            {
                bool here = sel.TrueForAll(p => TakeStore.Parent(p) == folder);
                if (here) m.AddDisabledItem(new GUIContent($"Move{n} to/{label}"));
                else m.AddItem(new GUIContent($"Move{n} to/{label}"), false, () => MoveTakes(sel, folder));
            }
            m.AddItem(new GUIContent($"Move{n} to/New Folder…"), false, () => MoveToNewFolder(sel));
            m.AddSeparator("");
            if (sel.Count == 1) m.AddItem(new GUIContent("Rename"), false, () => BeginRename(name, path));
            else m.AddDisabledItem(new GUIContent("Rename"));
            m.AddItem(new GUIContent("Show in Project"), false, () => Ping(path));
            m.AddSeparator("");
            m.AddItem(new GUIContent($"Delete{n}…"), false, () => DeleteTakes(sel));
        }

        private List<(string label, string folder)> MoveTargets()
        {
            var list = new List<(string, string)>();
            if (!AssetDatabase.IsValidFolder(_root)) return list;
            void Walk(string f, int depth)
            {
                string label = f == _root ? _rootLabel + "  (top level)" : TakeStore.DisplayPath(f, _root, _rootLabel);
                list.Add((label, f));
                foreach (var s in TakeStore.SubFolders(f)) Walk(s, depth + 1);
            }
            Walk(_root, 0);
            return list;
        }

        private void ShowMoveMenu(List<string> paths, Rect at)
        {
            if (paths.Count == 0) return;
            var m = new GenericMenu();
            foreach (var (label, folder) in MoveTargets())
            {
                if (paths.TrueForAll(p => TakeStore.Parent(p) == folder)) m.AddDisabledItem(new GUIContent(label));
                else m.AddItem(new GUIContent(label), false, () => MoveTakes(paths, folder));
            }
            m.AddSeparator("");
            m.AddItem(new GUIContent("New Folder…"), false, () => MoveToNewFolder(paths));
            m.DropDown(at);
        }

        /// <summary>Creates a folder next to the current view, moves the takes into it and starts renaming it.</summary>
        private void MoveToNewFolder(List<string> paths)
        {
            string parent = SaveFolder;
            string f = TakeStore.CreateFolder(parent);
            _expanded.Add(parent);
            SaveExpanded();
            MoveTakes(paths, f);
            _folder = f;
            EditorPrefs.SetString(PrefFolder, f);
            Refresh();
            var row = FindTreeRow(f);
            if (row != null) BeginRename(row.Q<Label>(className: "acs-tree__name"), f);
        }

        // ------------------------------------------------------------------ Selection

        private List<string> SelectedPaths()
        {
            var list = new List<string>();
            foreach (var p in _visible) if (_selected.Contains(p)) list.Add(p);
            foreach (var p in _selected) if (!list.Contains(p)) list.Add(p);
            return list;
        }

        private List<Take> SelectedTakes()
        {
            var list = new List<Take>();
            foreach (var p in SelectedPaths()) list.Add(GetTake(p));
            return list;
        }

        private void SelectOnly(string path)
        {
            _selected.Clear();
            _selected.Add(path);
            _anchor = path;
            UpdateRowSelection();
        }

        private void ClickSelect(string path, bool toggle, bool range)
        {
            if (range && _anchor != null && _visible.Contains(_anchor))
            {
                int a = _visible.IndexOf(_anchor), b = _visible.IndexOf(path);
                if (!toggle) _selected.Clear();
                for (int i = Math.Min(a, b); i <= Math.Max(a, b); i++) _selected.Add(_visible[i]);
            }
            else if (toggle)
            {
                if (!_selected.Remove(path)) _selected.Add(path);
                _anchor = path;
            }
            else
            {
                _selected.Clear();
                _selected.Add(path);
                _anchor = path;
            }
            UpdateRowSelection();
        }

        private void ClearSelection()
        {
            if (_selected.Count == 0) return;
            _selected.Clear();
            UpdateRowSelection();
        }

        private void UpdateRowSelection()
        {
            _list.Query(className: "acs-take").ForEach(r => r.EnableInClassList("acs-take--sel", r.userData is string p && _selected.Contains(p)));
            UpdateSelectionBar();
        }

        private void UpdateSelectionBar()
        {
            int n = _selected.Count;
            _selBar.style.display = n > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _selCount.text = n == 1 ? "1 take selected" : $"{n} takes selected";
        }

        private void OnListKey(KeyDownEvent e)
        {
            if (_renaming) return;
            var sel = SelectedPaths();
            switch (e.keyCode)
            {
                case KeyCode.Escape: ClearSelection(); break;
                case KeyCode.Delete:
                case KeyCode.Backspace when e.actionKey:
                    DeleteTakes(sel); break;
                case KeyCode.A when e.actionKey:
                    _selected.Clear();
                    foreach (var p in _visible) _selected.Add(p);
                    UpdateRowSelection();
                    break;
                case KeyCode.Space when sel.Count > 0:
                    Play?.Invoke(GetTake(sel[0])); break;
                case KeyCode.F2 when sel.Count == 1:
                case KeyCode.Return when sel.Count == 1:
                    var row = FindListRow(sel[0]);
                    if (row != null) BeginRename(row.Q<Label>(className: "acs-varrow__name"), sel[0]);
                    break;
                case KeyCode.UpArrow:
                case KeyCode.DownArrow:
                    if (_visible.Count == 0) break;
                    int i = _anchor != null ? _visible.IndexOf(_anchor) : -1;
                    i = Mathf.Clamp(i + (e.keyCode == KeyCode.UpArrow ? -1 : 1), 0, Math.Min(_visible.Count, _shown) - 1);
                    ClickSelect(_visible[i], false, false);
                    var r = FindListRow(_visible[i]);
                    if (r != null) _list.ScrollTo(r);
                    break;
                default: return;
            }
            e.StopPropagation();
        }

        private VisualElement FindListRow(string path)
        {
            VisualElement found = null;
            _list.Query(className: "acs-take").ForEach(r => { if (r.userData as string == path) found = r; });
            return found;
        }

        private VisualElement FindTreeRow(string folder)
        {
            VisualElement found = null;
            _tree.Query(className: "acs-tree__row").ForEach(r => { if (r.userData as string == folder) found = r; });
            return found;
        }

        // ------------------------------------------------------------------ Drag & drop

        private struct Click
        {
            public int Count;
            public bool Toggle, Range;
        }

        /// <summary>Click vs. drag on one element: a press that moves more than a few pixels starts a drag.</summary>
        private static void MakeDraggable(VisualElement el, Action<Click> click, Action startDrag)
        {
            Vector2 start = default;
            bool armed = false, pendingClick = false;
            el.RegisterCallback<MouseDownEvent>(e =>
            {
                if (e.button != 0 || FromInteractive(e.target as VisualElement, el)) return;
                var c = new Click { Count = e.clickCount, Toggle = e.actionKey, Range = e.shiftKey };
                e.StopPropagation();
                if (c.Count >= 2) { armed = pendingClick = false; click(c); return; }
                start = e.mousePosition;
                armed = true;
                // Modifier clicks select immediately; plain clicks wait for mouse-up so dragging keeps a multi-selection.
                pendingClick = !(c.Toggle || c.Range);
                if (!pendingClick) click(c);
            });
            el.RegisterCallback<MouseMoveEvent>(e =>
            {
                if (!armed) return;
                if ((e.pressedButtons & 1) == 0) { armed = pendingClick = false; return; }
                if (startDrag == null || (e.mousePosition - start).sqrMagnitude < 36) return;
                armed = pendingClick = false;
                startDrag();
            });
            el.RegisterCallback<MouseUpEvent>(e =>
            {
                if (e.button != 0) return;
                bool fire = armed && pendingClick;
                armed = pendingClick = false;
                if (fire) click(new Click { Count = 1 });
            });
        }

        private static void OnRightClick(VisualElement el, Action show)
        {
            el.RegisterCallback<MouseDownEvent>(e =>
            {
                if (e.button != 1) return;
                show();
                e.StopPropagation();
            });
        }

        private static bool FromInteractive(VisualElement t, VisualElement stop)
        {
            for (; t != null && t != stop; t = t.parent)
                if (t is StudioButton || t is TextField || t.ClassListContains("acs-take__folderchip")) return true;
            return false;
        }

        private void StartTakeDrag(List<string> paths)
        {
            if (paths.Count == 0) return;
            var objs = new List<UnityEngine.Object>();
            foreach (var p in paths)
            {
                var c = AssetDatabase.LoadAssetAtPath<AudioClip>(p);
                if (c != null) objs.Add(c);
            }
            DragAndDrop.PrepareStartDrag();
            DragAndDrop.objectReferences = objs.ToArray();
            DragAndDrop.paths = paths.ToArray();
            DragAndDrop.SetGenericData(DragTakesKey, new List<string>(paths));
            DragAndDrop.StartDrag(paths.Count == 1 ? Path.GetFileNameWithoutExtension(paths[0]) : $"{paths.Count} takes");
        }

        private void StartFolderDrag(string folder)
        {
            DragAndDrop.PrepareStartDrag();
            var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(folder);
            DragAndDrop.objectReferences = obj != null ? new[] { obj } : new UnityEngine.Object[0];
            DragAndDrop.paths = new[] { folder };
            DragAndDrop.SetGenericData(DragFolderKey, folder);
            DragAndDrop.StartDrag(TakeStore.Name(folder));
        }

        /// <summary>Audio asset paths in the current drag (from this browser or the Project window).</summary>
        private static List<string> DraggedAudio()
        {
            if (DragAndDrop.GetGenericData(DragTakesKey) is List<string> own) return own;
            var list = new List<string>();
            foreach (var o in DragAndDrop.objectReferences)
                if (o is AudioClip)
                {
                    string p = AssetDatabase.GetAssetPath(o);
                    if (!string.IsNullOrEmpty(p)) list.Add(TakeStore.Normalize(p));
                }
            return list;
        }

        private static string DraggedFolder() => DragAndDrop.GetGenericData(DragFolderKey) as string;

        /// <summary>
        /// Makes <paramref name="el"/> accept dropped takes/folders into the folder <paramref name="target"/> returns
        /// (null = not a valid destination right now). <paramref name="highlight"/> gets the drop-hover style.
        /// </summary>
        private void RegisterDropTarget(VisualElement el, Func<string> target, VisualElement highlight)
        {
            el.RegisterCallback<DragUpdatedEvent>(e =>
            {
                string dest = target();
                var mode = dest == null ? DragAndDropVisualMode.Rejected : DropMode(dest);
                DragAndDrop.visualMode = mode;
                SetDrop(mode == DragAndDropVisualMode.Rejected ? null : highlight ?? _list);
                if (highlight != null && dest != null) SpringOpen(dest);
                e.StopPropagation();
            });
            el.RegisterCallback<DragLeaveEvent>(_ => { if (_dropRow == (highlight ?? _list)) SetDrop(null); });
            el.RegisterCallback<DragPerformEvent>(e =>
            {
                string dest = target();
                SetDrop(null);
                if (dest == null || DropMode(dest) == DragAndDropVisualMode.Rejected) return;
                DragAndDrop.AcceptDrag();
                e.StopPropagation();
                // Defer: moving assets inside the drag callback upsets the drag state.
                var folder = DraggedFolder();
                var audio = folder == null ? new List<string>(DraggedAudio()) : null;
                schedule.Execute(() =>
                {
                    if (folder != null) MoveFolder(folder, dest);
                    else MoveTakes(audio, dest);
                });
            });
        }

        private DragAndDropVisualMode DropMode(string dest)
        {
            var folder = DraggedFolder();
            if (folder != null)
                return TakeStore.IsInside(dest, folder) || TakeStore.Parent(folder) == dest ? DragAndDropVisualMode.Rejected : DragAndDropVisualMode.Move;
            var audio = DraggedAudio();
            if (audio.Count == 0) return DragAndDropVisualMode.Rejected;
            bool allHere = audio.TrueForAll(p => TakeStore.Parent(p) == dest);
            if (allHere) return DragAndDropVisualMode.Rejected;
            return audio.TrueForAll(p => TakeStore.IsInside(p, _root)) ? DragAndDropVisualMode.Move : DragAndDropVisualMode.Copy;
        }

        private void SetDrop(VisualElement row)
        {
            if (_dropRow == row) return;
            _dropRow?.RemoveFromClassList(_dropRow == _list ? "acs-takes__list--drop" : "acs-tree__row--drop");
            _dropRow = row;
            _dropRow?.AddToClassList(_dropRow == _list ? "acs-takes__list--drop" : "acs-tree__row--drop");
            if (row == null) _hoverFolder = null;
        }

        private void ClearDrop() => SetDrop(null);

        /// <summary>Hovering a collapsed folder during a drag opens it, so deep folders are reachable.</summary>
        private void SpringOpen(string folder)
        {
            if (_hoverFolder != folder) { _hoverFolder = folder; _hoverSince = EditorApplication.timeSinceStartup; return; }
            if (_expanded.Contains(folder) || EditorApplication.timeSinceStartup - _hoverSince < SpringOpenSeconds) return;
            if (TakeStore.SubFolders(folder).Count == 0) return;
            _expanded.Add(folder);
            SaveExpanded();
            _dropRow = null;
            RebuildTree();
            var row = FindTreeRow(folder);
            if (row != null) SetDrop(row);
            _hoverFolder = folder;
        }

        // ------------------------------------------------------------------ Operations

        private void MoveTakes(List<string> paths, string dest)
        {
            if (paths == null || paths.Count == 0) return;
            int moved = 0, copied = 0;
            var newSel = new List<string>();
            foreach (var p in paths)
            {
                try
                {
                    if (TakeStore.IsInside(p, _root))
                    {
                        string np = TakeStore.MoveTake(p, dest);
                        if (np != p) { moved++; _cache.Remove(p); }
                        newSel.Add(np);
                    }
                    else { newSel.Add(TakeStore.CopyTake(p, dest)); copied++; }
                }
                catch (Exception ex) { _ctx.Toast?.Invoke($"{Path.GetFileName(p)}: {ex.Message}", true); }
            }
            string where = TakeStore.DisplayPath(dest, _root, _rootLabel);
            if (moved + copied > 0)
                _ctx.Toast?.Invoke(copied > 0 ? $"Copied {copied} clip{(copied == 1 ? "" : "s")} into {where}" : $"Moved {moved} take{(moved == 1 ? "" : "s")} to {where}", false);
            _selected.Clear();
            // Keep the moved takes selected (visible when the view shows the destination).
            foreach (var p in newSel) _selected.Add(p);
            Refresh();
        }

        private void MoveFolder(string folder, string dest)
        {
            try
            {
                string np = TakeStore.MoveFolder(folder, dest);
                if (np == folder) return;
                _cache.Clear();
                RemapPrefs(folder, np);
                _expanded.Add(dest);
                SaveExpanded();
                _ctx.Toast?.Invoke($"Moved folder '{TakeStore.Name(folder)}' into {TakeStore.DisplayPath(dest, _root, _rootLabel)}", false);
            }
            catch (Exception ex) { _ctx.Toast?.Invoke(ex.Message, true); }
            Refresh();
        }

        private void RemapPrefs(string from, string to)
        {
            if (_folder != AllView && TakeStore.IsInside(_folder, from)) _folder = to + _folder.Substring(from.Length);
            EditorPrefs.SetString(PrefFolder, _folder);
            var ex = new List<string>(_expanded);
            _expanded.Clear();
            foreach (var e in ex) _expanded.Add(TakeStore.IsInside(e, from) ? to + e.Substring(from.Length) : e);
            SaveExpanded();
        }

        private void NewFolder(string parent)
        {
            string f = TakeStore.CreateFolder(parent);
            _expanded.Add(parent);
            SaveExpanded();
            _folder = f;
            EditorPrefs.SetString(PrefFolder, f);
            _selected.Clear();
            Refresh();
            var row = FindTreeRow(f);
            if (row != null) BeginRename(row.Q<Label>(className: "acs-tree__name"), f);
        }

        private void DeleteFolder(string folder)
        {
            int n = TakeStore.CountTakes(folder, true);
            string name = TakeStore.Name(folder);
            if (n == 0)
            {
                if (!EditorUtility.DisplayDialog("Delete folder", $"Delete the empty folder '{name}'?", "Delete", "Cancel")) return;
                TakeStore.DeleteFolder(folder, false);
            }
            else
            {
                string parent = TakeStore.DisplayPath(TakeStore.Parent(folder), _root, _rootLabel);
                int choice = EditorUtility.DisplayDialogComplex("Delete folder",
                    $"'{name}' contains {n} take{(n == 1 ? "" : "s")}.\n\nKeep them by moving them up to '{parent}', or delete everything?",
                    "Keep takes, delete folder", "Cancel", "Delete everything");
                if (choice == 1) return;
                TakeStore.DeleteFolder(folder, choice == 0);
            }
            _cache.Clear();
            if (_folder != AllView && TakeStore.IsInside(_folder, folder)) _folder = TakeStore.Parent(folder);
            EditorPrefs.SetString(PrefFolder, _folder);
            Refresh();
        }

        private void DeleteTakes(List<string> paths)
        {
            if (paths.Count == 0) return;
            string what = paths.Count == 1 ? $"'{Path.GetFileName(paths[0])}'" : $"{paths.Count} takes";
            if (!EditorUtility.DisplayDialog("Delete takes", $"Delete {what} from the project? This cannot be undone.", "Delete", "Cancel")) return;
            AssetDatabase.StartAssetEditing();
            try { foreach (var p in paths) { TakeStore.DeleteTake(p); _cache.Remove(p); } }
            finally { AssetDatabase.StopAssetEditing(); }
            _selected.Clear();
            Refresh();
        }

        private void BeginRename(Label label, string path)
        {
            if (label == null || _renaming) return;
            bool folder = AssetDatabase.IsValidFolder(path);
            var host = label.parent;
            int index = host.IndexOf(label);
            var field = new TextField { value = folder ? TakeStore.Name(path) : Path.GetFileNameWithoutExtension(path) };
            field.AddToClassList("acs-field");
            field.AddToClassList("acs-takes__rename");
            label.style.display = DisplayStyle.None;
            host.Insert(index + 1, field);
            _renaming = true;
            bool done = false;
            void Finish(bool commit)
            {
                if (done) return;
                done = true;
                _renaming = false;
                if (commit)
                {
                    try
                    {
                        string np = TakeStore.Rename(path, field.value);
                        if (np != path)
                        {
                            _cache.Remove(path);
                            if (folder) RemapPrefs(path, np);
                            else if (_selected.Remove(path)) { _selected.Add(np); _anchor = np; }
                        }
                    }
                    catch (Exception ex) { _ctx.Toast?.Invoke(ex.Message, true); }
                }
                schedule.Execute(Refresh);
            }
            field.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { Finish(true); e.StopPropagation(); }
                else if (e.keyCode == KeyCode.Escape) { Finish(false); e.StopPropagation(); }
            });
            field.RegisterCallback<FocusOutEvent>(_ => Finish(true));
            field.schedule.Execute(() => { field.Focus(); field.SelectAll(); });
        }

        private static void Ping(string path)
        {
            var o = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
            if (o == null) return;
            EditorGUIUtility.PingObject(o);
            Selection.activeObject = o;
        }

        private void OnProjectChanged()
        {
            if (_renaming) return;
            _pendingRefresh?.Pause();
            _pendingRefresh = schedule.Execute(() => { if (!_renaming) Refresh(); }).StartingIn(250);
        }
    }
}
