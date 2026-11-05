using System;
using System.Collections.Generic;
using AudioCreationStudio.DSP;
using AudioCreationStudio.Models;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;
using UnityEngine.UIElements;

namespace AudioCreationStudio.EditorUI
{
    /// <summary>
    /// The Audio Creation Studio window. Composes the views and services; contains no DSP or editing logic itself.
    /// Layout: toolbar (top) · track headers + timeline (center) · contextual inspector (right) · master bar (bottom).
    /// </summary>
    public sealed class AudioStudioWindow : EditorWindow
    {
        private static readonly string[] Tabs = { "Arrange", "Mixer", "Generator", "Loop", "Variations", "Library" };
        private static readonly Icon[] TabIcons = { Icon.Waveform, Icon.Mixer, Icon.Sine, Icon.Loop, Icon.Layers, Icon.Grid };

        [SerializeField] private StudioState _state = new StudioState();

        private StudioContext _ctx;
        private StudioSession _session;
        private PreviewRenderService _render;
        private TransportController _transport;
        private ExportRunner _exportRunner;

        // Views
        private VisualElement _workspace;
        private TwoPaneSplitView _split;
        private readonly Dictionary<string, VisualElement> _views = new Dictionary<string, VisualElement>();
        private readonly Dictionary<string, VisualElement> _tabButtons = new Dictionary<string, VisualElement>();
        private ArrangeView _arrange;
        private MixerView _mixer;
        private GeneratorView _generator;
        private LoopEditorView _loopEditor;
        private VariationsView _variations;
        private LibraryView _library;
        private InspectorPanel _inspector;
        private ExportPanel _exportPanel;

        // Toolbar / status widgets
        private Label _recipeName, _time, _total, _peak, _renderStatus, _formatPill, _exportStatus, _playingLabel;
        private StudioButton _playBtn, _loopBtn, _snapBtn, _undoBtn, _redoBtn;
        private LevelMeterElement _masterMeter;
        private ValueSlider _masterGain;
        private Label _masterGainLabel;
        private VisualElement _toastLayer;
        private double _lastMeterTime;

        [MenuItem("Tools/Audio Creation Studio/Open Studio %#&a", priority = 0)]
        [MenuItem("Window/Audio/Audio Creation Studio", priority = 2000)]
        public static AudioStudioWindow Open()
        {
            var w = GetWindow<AudioStudioWindow>();
            w.titleContent = new GUIContent("Audio Studio");
            w.minSize = new Vector2(900, 520);
            w.Show();
            return w;
        }

        public static void OpenRecipe(AudioRecipe recipe)
        {
            var w = Open();
            w._session?.Open(recipe);
        }

        [OnOpenAsset]
        private static bool OnOpenAsset(int instanceId, int line)
        {
            if (EditorUtility.InstanceIDToObject(instanceId) is AudioRecipe r) { OpenRecipe(r); return true; }
            return false;
        }

        private void OnEnable()
        {
            titleContent = new GUIContent("Audio Studio");
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            TearDown();
        }

        private void TearDown()
        {
            _transport?.Dispose();
            _render?.Dispose();
            if (_session != null) { _session.Changed -= OnSessionChanged; _session.Dispose(); }
            _transport = null;
            _render = null;
            _session = null;
        }

        private void OnDestroy()
        {
            if (_state.working != null)
            {
                if (_state.dirty && _state.asset != null &&
                    EditorUtility.DisplayDialog("Audio Creation Studio", $"Save changes to '{_state.asset.name}'?", "Save", "Discard"))
                {
                    _state.asset.data = _state.working.data.Clone();
                    EditorUtility.SetDirty(_state.asset);
                    AssetDatabase.SaveAssetIfDirty(_state.asset);
                }
                Undo.ClearUndo(_state.working);
                DestroyImmediate(_state.working);
            }
            if (_state.generatorDoc != null)
            {
                Undo.ClearUndo(_state.generatorDoc);
                DestroyImmediate(_state.generatorDoc);
            }
        }

        public void CreateGUI()
        {
            TearDown();
            _state ??= new StudioState();
            _session = new StudioSession(_state);
            _render = new PreviewRenderService(_session, SourceLibrary.Shared);
            _transport = new TransportController(_session, _render);
            _exportRunner = new ExportRunner();
            _ctx = new StudioContext
            {
                Session = _session, Library = SourceLibrary.Shared, Waveforms = WaveformCache.Shared, Render = _render, Transport = _transport,
                ShowTab = ShowTab, OpenExport = OpenExport, Toast = Toast, EditGenerator = EditGenerator, OpenLoopEditorForClip = OpenLoopForClip
            };
            _ctx.Commands = new StudioCommands(_ctx);

            var root = rootVisualElement;
            root.Clear();
            StudioTheme.Apply(root);
            root.focusable = true;
            root.AddToClassList("acs-window");

            root.Add(BuildToolbar());

            _split = new TwoPaneSplitView(1, 330, TwoPaneSplitViewOrientation.Horizontal);
            _split.AddToClassList("acs-main");
            _workspace = new VisualElement();
            _workspace.AddToClassList("acs-workspace");
            _split.Add(_workspace);
            _inspector = new InspectorPanel(_ctx);
            _split.Add(_inspector);
            root.Add(_split);

            root.Add(BuildMasterBar());

            _toastLayer = new VisualElement { pickingMode = PickingMode.Ignore };
            _toastLayer.AddToClassList("acs-toasts");
            root.Add(_toastLayer);

            _exportPanel = new ExportPanel(_ctx, _exportRunner);
            root.Add(_exportPanel);
            _exportRunner.Completed += (results, ex, cancelled) =>
            {
                if (ex != null) Toast(_exportRunner.Status, true);
                else if (!cancelled && results.Count > 0) Toast(_exportRunner.Status, false);
            };

            root.RegisterCallback<GeometryChangedEvent>(e =>
            {
                float w = e.newRect.width;
                root.EnableInClassList("acs-compact", w < 1380);
                root.EnableInClassList("acs-narrow", w < 1000);
            });
            root.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            root.RegisterCallback<ValidateCommandEvent>(OnValidateCommand);
            root.RegisterCallback<ExecuteCommandEvent>(OnExecuteCommand);

            _session.Changed += OnSessionChanged;
            _transport.StateChanged += UpdateTransportUI;
            _transport.PlaybackProblem += msg => Toast(msg, true);

            ShowTab(string.IsNullOrEmpty(_state.activeTab) ? "Arrange" : _state.activeTab);
            OnSessionChanged(StudioChange.All, null);
            root.schedule.Execute(() => { if (_arrange != null && _session.Data.ContentEnd > 0 && _state.view.pixelsPerSecond == 220) _arrange.ZoomToFit(); }).StartingIn(50);
        }

        // ------------------------------------------------------------ Toolbar

        private VisualElement BuildToolbar()
        {
            var bar = new VisualElement();
            bar.AddToClassList("acs-toolbar");

            var brand = new VisualElement();
            brand.AddToClassList("acs-brand");
            brand.Add(new IconElement(Icon.Logo, 22) { Color = StudioTheme.Accent });
            var names = new VisualElement();
            names.Add(UIUtil.Label("Audio Creation Studio", "acs-brand__title"));
            _recipeName = UIUtil.Label("", "acs-brand__recipe");
            names.Add(_recipeName);
            brand.Add(names);
            bar.Add(brand);

            var file = Group(bar);
            file.Add(new StudioButton(Icon.New, null, () => _session.New(), "New recipe"));
            var openBtn = new StudioButton(Icon.Open, null, null, "Open recipe");
            openBtn.SetAction(() => ShowOpenMenu(openBtn));
            file.Add(openBtn);
            var saveBtn = new StudioButton(Icon.Save, null, () => { if (_session.Save()) Toast("Recipe saved", false); }, "Save recipe (right-click for Save As / Duplicate / Revert)");
            saveBtn.RegisterCallback<ContextClickEvent>(_ => ShowSaveMenu());
            file.Add(saveBtn);
            var moreSave = new StudioButton(Icon.ChevronDown, null, ShowSaveMenu, "Save As, Duplicate, Revert", "acs-btn--narrow");
            file.Add(moreSave);
            var importBtn = new StudioButton(Icon.Import, "Import", () => _ctx.Commands.ImportFromProjectSelection(), "Import the AudioClips selected in the Project window (or browse for a file)");
            file.Add(importBtn);

            var edit = Group(bar);
            edit.AddToClassList("acs-group--edit");
            _undoBtn = new StudioButton(Icon.Undo, null, () => Undo.PerformUndo(), "Undo (⌘Z)");
            _redoBtn = new StudioButton(Icon.Redo, null, () => Undo.PerformRedo(), "Redo (⌘⇧Z)");
            edit.Add(_undoBtn);
            edit.Add(_redoBtn);
            edit.Add(new StudioButton(Icon.Scissors, null, () => _ctx.Commands.SplitAtPlayhead(), "Split at playhead (S)"));
            _snapBtn = new StudioButton(Icon.Magnet, null, () => { _session.Snap = !_session.Snap; _snapBtn.Toggled = _session.Snap; }, "Snap to grid, clip edges and playhead (hold Ctrl while dragging to bypass)");
            _snapBtn.Toggled = _state.snap;
            edit.Add(_snapBtn);

            bar.Add(UIUtil.Spacer());

            var transport = new VisualElement();
            transport.AddToClassList("acs-transport");
            transport.Add(new StudioButton(Icon.Stop, null, () => _transport.Stop(), "Stop (Esc)", "acs-btn--transport"));
            _playBtn = new StudioButton(Icon.Play, null, () => { if (_state.activeTab == "Generator") _generator?.TogglePreview(); else _transport.TogglePlay(); }, "Play / Pause (Space)", "acs-btn--play");
            transport.Add(_playBtn);
            _loopBtn = new StudioButton(Icon.Loop, null, () => { _session.LoopPlayback = !_session.LoopPlayback; _loopBtn.Toggled = _session.LoopPlayback; if (_transport.IsPlaying && _transport.Target == PreviewTarget.Mix) _transport.Seek(_session.Playhead); }, "Loop playback (L) — loops the loop region if one is enabled", "acs-btn--transport");
            _loopBtn.Toggled = _state.loopPlayback;
            transport.Add(_loopBtn);
            var clock = new VisualElement();
            clock.AddToClassList("acs-clock");
            _time = UIUtil.Label("00:00.000", "acs-clock__time");
            _total = UIUtil.Label("/ 00:00.000", "acs-clock__total");
            clock.Add(_time);
            clock.Add(_total);
            transport.Add(clock);
            bar.Add(transport);

            bar.Add(UIUtil.Spacer());

            var tabs = new VisualElement();
            tabs.AddToClassList("acs-tabs");
            for (int i = 0; i < Tabs.Length; i++)
            {
                string tab = Tabs[i];
                var t = new VisualElement();
                t.AddToClassList("acs-tab");
                t.Add(new IconElement(TabIcons[i], 14));
                t.Add(UIUtil.Label(tab, "acs-tab__label"));
                t.tooltip = tab;
                t.AddManipulator(new Clickable(() => ShowTab(tab)));
                _tabButtons[tab] = t;
                tabs.Add(t);
            }
            bar.Add(tabs);

            var right = Group(bar);
            right.Add(new StudioButton(Icon.Export, "Export", () => OpenExport(null), "Export WAV", "acs-btn--accent"));
            var gear = new StudioButton(Icon.Gear, null, null, "Settings");
            gear.SetAction(() => ShowSettingsMenu(gear));
            right.Add(gear);
            return bar;
        }

        private static VisualElement Group(VisualElement bar)
        {
            var g = new VisualElement();
            g.AddToClassList("acs-group");
            bar.Add(g);
            return g;
        }

        private void ShowOpenMenu(VisualElement anchor)
        {
            var menu = new GenericMenu();
            var guids = AssetDatabase.FindAssets("t:AudioRecipe");
            foreach (var g in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                var label = path.StartsWith("Assets/") ? path.Substring(7) : path;
                menu.AddItem(new GUIContent(label.Replace(".asset", "").Replace('/', '∕')), _session.Asset != null && AssetDatabase.GetAssetPath(_session.Asset) == path,
                    () => _session.Open(AssetDatabase.LoadAssetAtPath<AudioRecipe>(path)));
            }
            if (guids.Length == 0) menu.AddDisabledItem(new GUIContent("No recipes in the project yet"));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Open Demo…"), false, () => DemoContent.OpenDemoMenu());
            menu.DropDown(anchor.worldBound);
        }

        private void ShowSaveMenu()
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Save"), false, () => _session.Save());
            menu.AddItem(new GUIContent("Save As…"), false, () => _session.SaveAs());
            menu.AddItem(new GUIContent("Duplicate Recipe Asset"), false, () => _session.DuplicateAsset());
            if (_session.Asset != null) menu.AddItem(new GUIContent("Revert to Saved"), false, () => _session.Revert());
            else menu.AddDisabledItem(new GUIContent("Revert to Saved"));
            if (_session.Asset != null) menu.AddItem(new GUIContent("Ping Recipe Asset"), false, () => EditorGUIUtility.PingObject(_session.Asset));
            menu.ShowAsContext();
        }

        private void ShowSettingsMenu(VisualElement anchor)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Snap"), _state.snap, () => { _session.Snap = !_session.Snap; _snapBtn.Toggled = _session.Snap; });
            menu.AddItem(new GUIContent("Follow Playhead"), _state.followPlayhead, () => _state.followPlayhead = !_state.followPlayhead);
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Zoom to Fit"), false, () => _arrange?.ZoomToFit());
            var pref = PreviewPlayerFactory.Preferred;
            menu.AddItem(new GUIContent("Playback/Scene AudioSource (recommended)"), pref == PlaybackBackend.SceneAudioSource,
                () => { _transport.SetBackend(PlaybackBackend.SceneAudioSource); Toast("Playback: AudioSource", false); });
            menu.AddItem(new GUIContent("Playback/Editor Preview Channel"), pref == PlaybackBackend.EditorPreview,
                () => { _transport.SetBackend(PlaybackBackend.EditorPreview); Toast("Playback: Editor preview channel", false); });
            menu.AddItem(new GUIContent("Reset Audio Output (no sound?)"), false, () =>
            {
                _transport.Stop();
                bool ok = AudioSettings.Reset(AudioSettings.GetConfiguration());
                Toast(ok ? "Audio output re-initialized. Press Space to play." : "Unity could not reset the audio device; try restarting Unity.", !ok);
            });
            menu.AddItem(new GUIContent("Clear Audio Caches"), false, () => { SourceLibrary.Shared.Invalidate(); _render.ClearCache(); _render.Invalidate(); Toast("Caches cleared", false); });
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Open Demo…"), false, DemoContent.OpenDemoMenu);
            menu.AddItem(new GUIContent("Documentation"), false, DemoContent.OpenDocumentation);
            menu.AddDisabledItem(new GUIContent($"Playback: {_transport.Backend}"));
            menu.DropDown(anchor.worldBound);
        }

        // ------------------------------------------------------------ Master bar

        private VisualElement BuildMasterBar()
        {
            var bar = new VisualElement();
            bar.AddToClassList("acs-masterbar");
            bar.Add(UIUtil.Label("MASTER", "acs-overline"));
            _masterGain = new ValueSlider(new ValueRange(-60f, 12f, 0f, false, true), _session.Data.masterGainDb);
            _masterGain.AddToClassList("acs-masterbar__gain");
            _masterGain.tooltip = "Master gain (double-click to reset)";
            _masterGain.EditBegan += () => _session.BeginContinuous("Master Gain");
            _masterGain.ValueChanged += v => { _session.Continuous(d => d.masterGainDb = v, StudioChange.Content, _masterGain); _masterGainLabel.text = $"{v:+0.0;-0.0;0.0} dB"; };
            _masterGain.EditEnded += () => _session.EndContinuous();
            bar.Add(_masterGain);
            _masterGainLabel = UIUtil.Label("0.0 dB", "acs-masterbar__value");
            bar.Add(_masterGainLabel);
            _masterMeter = new LevelMeterElement(true);
            _masterMeter.AddToClassList("acs-masterbar__meter");
            bar.Add(_masterMeter);
            _peak = UIUtil.Label("", "acs-pill");
            _peak.tooltip = "Peak of the rendered mix. Red means samples above 0 dBFS (clipping).";
            bar.Add(_peak);
            _playingLabel = UIUtil.Label("", "acs-label-dim");
            bar.Add(_playingLabel);
            bar.Add(UIUtil.Spacer());
            _exportStatus = UIUtil.Label("", "acs-label-dim");
            bar.Add(_exportStatus);
            _renderStatus = UIUtil.Label("", "acs-pill");
            bar.Add(_renderStatus);
            _formatPill = UIUtil.Label("", "acs-pill");
            _formatPill.AddManipulator(new Clickable(() => _session.SelectMaster()));
            _formatPill.tooltip = "Recipe format (click to edit)";
            bar.Add(_formatPill);
            return bar;
        }

        // ------------------------------------------------------------ Tabs

        private void ShowTab(string tab)
        {
            if (Array.IndexOf(Tabs, tab) < 0) tab = "Arrange";
            _state.activeTab = tab;
            if (!_views.ContainsKey(tab))
            {
                VisualElement v;
                switch (tab)
                {
                    case "Mixer": v = _mixer = new MixerView(_ctx); _mixer.Rebuild(); break;
                    case "Generator": v = _generator = new GeneratorView(_ctx, _state); break;
                    case "Loop": v = _loopEditor = new LoopEditorView(_ctx); break;
                    case "Variations": v = _variations = new VariationsView(_ctx, _exportRunner); break;
                    case "Library": v = _library = new LibraryView(_ctx); break;
                    default: v = _arrange = new ArrangeView(_ctx); _arrange.RefreshAll(StudioChange.All); break;
                }
                v.AddToClassList("acs-view");
                _views[tab] = v;
                _workspace.Add(v);
            }
            foreach (var kv in _views) kv.Value.style.display = kv.Key == tab ? DisplayStyle.Flex : DisplayStyle.None;
            foreach (var kv in _tabButtons) kv.Value.EnableInClassList("acs-tab--on", kv.Key == tab);
            bool inspector = tab == "Arrange" || tab == "Mixer";
            if (inspector) _split.UnCollapse(); else _split.CollapseChild(1);
            switch (tab)
            {
                case "Arrange": _arrange.RefreshAll(StudioChange.All); break;
                case "Mixer": _mixer.Rebuild(); break;
                case "Loop": _loopEditor.Refresh(); break;
                case "Library": _library.Refresh(); break;
                case "Variations": _variations.Refresh(); break;
            }
        }

        private void EditGenerator(GeneratorSettings settings, string clipUid)
        {
            ShowTab("Generator");
            _generator.Load(settings ?? GeneratorPresets.CoinPickup(), clipUid);
        }

        private void OpenLoopForClip(string clipUid)
        {
            ShowTab("Loop");
            _loopEditor.OpenForClip(clipUid);
        }

        private void OpenExport(string mode)
        {
            _exportPanel.Show();
        }

        // ------------------------------------------------------------ Change dispatch

        private void OnSessionChanged(StudioChange change, object origin)
        {
            if (_ctx == null) return;
            if (change == StudioChange.Playhead)
            {
                _arrange?.RefreshPlayhead();
                if (_state.followPlayhead && _transport.IsPlaying && _transport.Target == PreviewTarget.Mix) _arrange?.FollowPlayhead();
                UpdateClock();
                return;
            }
            if ((change & (StudioChange.Content | StudioChange.Structure | StudioChange.Recipe)) != 0) _render.Invalidate();
            if (_state.activeTab == "Arrange" && _arrange != null) _arrange.RefreshAll(change);
            else if (_arrange != null && (change & (StudioChange.Structure | StudioChange.Recipe)) != 0) _arrange.Headers.Rebuild();
            if (_mixer != null && _state.activeTab == "Mixer")
            {
                if ((change & (StudioChange.Structure | StudioChange.Recipe)) != 0) _mixer.Rebuild(); else _mixer.RefreshValues();
            }
            if ((change & StudioChange.View) == 0 || change == StudioChange.All) _inspector.OnChanged(change, origin);
            if (_generator != null && (change & (StudioChange.Content | StudioChange.Structure)) != 0 && origin != _generator) _generator.OnSessionChanged();
            if (_loopEditor != null && _state.activeTab == "Loop" && (change & (StudioChange.Recipe | StudioChange.Structure)) != 0) _loopEditor.Refresh();
            if (_exportPanel.IsOpen && (change & (StudioChange.Recipe | StudioChange.Selection)) != 0) _exportPanel.Build();
            if (origin != _masterGain)
            {
                _masterGain.Value = _session.Data.masterGainDb;
                _masterGainLabel.text = $"{_session.Data.masterGainDb:+0.0;-0.0;0.0} dB";
            }
            _recipeName.text = _session.DisplayName + (_session.Asset == null ? "  (unsaved)" : "");
            _formatPill.text = $"{_session.Data.sampleRate / 1000f:0.#} kHz · {(_session.Data.channels == OutputChannelMode.Stereo ? "Stereo" : "Mono")}";
            titleContent = new GUIContent(_session.IsDirty ? "Audio Studio •" : "Audio Studio");
            UpdateClock();
        }

        private void UpdateClock()
        {
            int sr = _session.Data.sampleRate;
            _time.text = SampleTime.Format(_session.Playhead, sr);
            long total = _render.Current?.Master?.Frames ?? _session.Data.ContentEnd;
            _total.text = "/ " + SampleTime.Format(Math.Max(total, _session.Data.ContentEnd), sr);
        }

        private void UpdateTransportUI()
        {
            bool playing = _transport.IsPlaying && !_transport.IsPaused;
            _playBtn.IconView.Icon = playing ? Icon.Pause : Icon.Play;
            _playBtn.Toggled = playing;
            _playingLabel.text = _transport.IsPlaying ? (_transport.IsPaused ? "⏸ " : "▶ ") + _transport.TargetLabel : _transport.TargetLabel;
        }

        // ------------------------------------------------------------ Update loop

        private void OnEditorUpdate()
        {
            if (_ctx == null) return;
            WaveformCache.Shared.Pump();
            _render.Tick();
            _transport.Tick();
            _exportRunner.Tick();

            double now = EditorApplication.timeSinceStartup;
            if (now - _lastMeterTime > 1 / 30.0)
            {
                _lastMeterTime = now;
                var reading = _transport.MasterReading();
                if (reading.PeakL > 0 || _masterMeter.IsActive) _masterMeter.Push(reading, now);
                if (_state.activeTab == "Arrange") _arrange?.Headers.UpdateMeters(now);
                if (_state.activeTab == "Mixer") _mixer?.UpdateMeters(now);
                UpdateStatus();
            }
        }

        private int _lastStatusVersion = -2;

        private void UpdateStatus()
        {
            if (_render.IsRendering) { _renderStatus.text = "Rendering…"; _renderStatus.EnableInClassList("acs-pill--accent", true); }
            else if (_render.LastError != null) { _renderStatus.text = "Render error"; _renderStatus.tooltip = _render.LastError; }
            else if (_render.Current != null)
            {
                _renderStatus.EnableInClassList("acs-pill--accent", false);
                _renderStatus.text = $"Mix ready · {_render.LastRenderMs:0} ms";
                _renderStatus.tooltip = $"Last render {_render.LastRenderMs:0.0} ms · {_render.LastCacheHits} track(s) reused from cache";
            }
            if (_render.CurrentVersion != _lastStatusVersion && _render.Current != null)
            {
                _lastStatusVersion = _render.CurrentVersion;
                var res = _render.Current;
                float db = DspMath.GainToDb(res.Peak);
                _peak.text = res.Master.Frames == 0 ? "—" : res.ClippedSamples > 0 ? $"CLIP {db:+0.0} dB" : $"Peak {db:0.0} dB";
                _peak.EnableInClassList("acs-pill--error", res.ClippedSamples > 0);
                _peak.EnableInClassList("acs-pill--warning", res.ClippedSamples == 0 && db > -1f);
                UpdateClock();
                if (_state.activeTab == "Arrange" && _session.InspectMaster) _inspector.Rebuild();
            }
            _exportStatus.text = _exportRunner.IsRunning ? $"Exporting {_exportRunner.Progress * 100:0}%" : "";
            _undoBtn.SetEnabled(true);
        }

        // ------------------------------------------------------------ Toasts

        private void Toast(string text, bool error)
        {
            if (_toastLayer == null || string.IsNullOrEmpty(text)) return;
            var t = new VisualElement();
            t.AddToClassList("acs-toast");
            if (error) t.AddToClassList("acs-toast--error");
            t.Add(new IconElement(error ? Icon.Warning : Icon.Check, 14) { Color = error ? StudioTheme.Error : StudioTheme.Success });
            t.Add(UIUtil.Label(text, "acs-toast__text"));
            _toastLayer.Add(t);
            while (_toastLayer.childCount > 4) _toastLayer.RemoveAt(0);
            t.schedule.Execute(() => t.AddToClassList("acs-toast--out")).StartingIn(error ? 5200 : 3000);
            t.schedule.Execute(() => t.RemoveFromHierarchy()).StartingIn(error ? 5600 : 3400);
            if (error) Debug.LogWarning("[Audio Creation Studio] " + text);
        }

        // ------------------------------------------------------------ Keyboard

        private void OnKeyDown(KeyDownEvent e)
        {
            if (UIUtil.IsTextInputFocused(rootVisualElement)) return;
            bool mod = e.actionKey || e.ctrlKey;
            switch (e.keyCode)
            {
                case KeyCode.Space:
                    if (_state.activeTab == "Generator") _generator?.TogglePreview(); else _transport.TogglePlay();
                    break;
                case KeyCode.Escape:
                    if (_exportPanel.IsOpen && !_exportRunner.IsRunning) _exportPanel.Hide(); else _transport.Stop();
                    break;
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    _transport.Stop();
                    _transport.Seek(0);
                    break;
                case KeyCode.Home: _transport.Seek(0); break;
                case KeyCode.S when !mod: _ctx.Commands.SplitAtPlayhead(); break;
                case KeyCode.L when !mod: _session.LoopPlayback = !_session.LoopPlayback; _loopBtn.Toggled = _session.LoopPlayback; break;
                case KeyCode.Delete when !mod:
                case KeyCode.Backspace when !mod:
                    _ctx.Commands.DeleteSelection();
                    break;
                case KeyCode.Equals when mod: _session.View.ZoomAt(200, 1.5); _session.Notify(StudioChange.View); break;
                case KeyCode.Minus when mod: _session.View.ZoomAt(200, 1 / 1.5); _session.Notify(StudioChange.View); break;
                default: return;
            }
            e.StopPropagation();
        }

        private static readonly HashSet<string> Commands = new HashSet<string> { "Duplicate", "Delete", "SoftDelete", "Copy", "Paste", "Cut", "SelectAll" };

        private void OnValidateCommand(ValidateCommandEvent e)
        {
            if (UIUtil.IsTextInputFocused(rootVisualElement) || _state.activeTab != "Arrange") return;
            if (Commands.Contains(e.commandName)) e.StopPropagation();
        }

        private void OnExecuteCommand(ExecuteCommandEvent e)
        {
            if (UIUtil.IsTextInputFocused(rootVisualElement) || _state.activeTab != "Arrange") return;
            switch (e.commandName)
            {
                case "Duplicate": _ctx.Commands.DuplicateSelection(); break;
                case "Delete":
                case "SoftDelete": _ctx.Commands.DeleteSelection(); break;
                case "Copy": _ctx.Commands.CopySelection(); break;
                case "Cut": _ctx.Commands.CopySelection(); _ctx.Commands.DeleteSelection(); break;
                case "Paste": _ctx.Commands.PasteAtPlayhead(); break;
                case "SelectAll":
                    _session.SelectClip(null, false);
                    foreach (var c in _session.Data.AllClips()) _session.SelectClip(c.uid, true);
                    break;
                default: return;
            }
            e.StopPropagation();
        }

        // ------------------------------------------------------------ External API (demos, tests)

        public StudioContext Context => _ctx;
    }
}
