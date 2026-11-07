using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AudioCreationStudio.DSP;
using AudioCreationStudio.Export;
using AudioCreationStudio.Models;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AudioCreationStudio.EditorUI
{
    /// <summary>
    /// Procedural SFX workspace: presets, live DSP visualizations (oscillator, ADSR, pitch, filter),
    /// generated waveform, and Generate / Randomize / Seed / Preview / Save / Insert / Export actions.
    /// Can be bound to a generator clip on the timeline, in which case edits update that clip (undoable).
    /// </summary>
    public sealed class GeneratorView : VisualElement
    {
        private readonly StudioContext _ctx;
        private readonly StudioState _state;
        private GeneratorDocument _doc;
        private GeneratorSettings _s => _doc.settings;
        private ulong _lastHash;
        private int _dragGroup = -1;
        private string _dragName;
        private bool _dragging;
        private readonly VisualElement _presetList;
        private readonly TextField _search;
        private readonly ScrollView _params;
        private readonly CurvePlotView _osc, _env, _pitch;
        private readonly FrequencyResponseView _filter;
        private readonly BufferWaveformView _wave;
        private readonly Label _info, _bound, _nameLabel;
        private readonly IntegerField _seed;
        private AudioBuffer _buffer;
        private CancellationTokenSource _cts;
        private bool _renderPending;
        private double _changeTime;
        private readonly System.Collections.Concurrent.ConcurrentQueue<Action> _main = new System.Collections.Concurrent.ConcurrentQueue<Action>();

        public GeneratorView(StudioContext ctx, StudioState state)
        {
            _ctx = ctx;
            _state = state;
            _doc = state.generatorDoc;
            if (_doc == null)
            {
                _doc = ScriptableObject.CreateInstance<GeneratorDocument>();
                _doc.hideFlags = HideFlags.HideAndDontSave;
                _doc.name = "ACS Generator Settings";
                _doc.settings = (state.generator ?? GeneratorPresets.CoinPickup()).Clone();
                state.generatorDoc = _doc;
            }
            state.generator = null;
            _lastHash = _s.ComputeHash();
            Undo.undoRedoPerformed += OnUndoRedo;
            RegisterCallback<DetachFromPanelEvent>(_ => Undo.undoRedoPerformed -= OnUndoRedo);
            RegisterCallback<AttachToPanelEvent>(_ => { Undo.undoRedoPerformed -= OnUndoRedo; Undo.undoRedoPerformed += OnUndoRedo; });
            AddToClassList("acs-gen");

            // Left: presets
            var left = new VisualElement();
            left.AddToClassList("acs-gen__left");
            left.Add(UIUtil.Label("PRESETS", "acs-overline"));
            _search = new TextField { value = "" };
            _search.AddToClassList("acs-search");
            _search.RegisterValueChangedCallback(_ => BuildPresetList());
            left.Add(_search);
            var ps = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            ps.AddToClassList("acs-gen__presets");
            _presetList = new VisualElement();
            ps.Add(_presetList);
            left.Add(ps);
            Add(left);

            // Center: visualizers + waveform + actions
            var center = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            center.AddToClassList("acs-gen__center");
            var head = new VisualElement();
            head.AddToClassList("acs-gen__head");
            _nameLabel = UIUtil.Label("", "acs-gen__name");
            head.Add(_nameLabel);
            _bound = UIUtil.Label("", "acs-pill");
            head.Add(_bound);
            StudioButton importBtn = null;
            importBtn = new StudioButton(Icon.Import, "Import Sound", () => ShowImportMenu(importBtn),
                "Edit an existing sound: browse for a file, use the clip selected in the Project window, or pick a recent AI take. You can also drag a file or AudioClip onto this tab.", "acs-btn--sm", "acs-btn--soft");
            importBtn.AddToClassList("acs-gen__import");
            head.Add(importBtn);
            head.Add(UIUtil.Spacer());
            _info = UIUtil.Label("", "acs-label-dim");
            head.Add(_info);
            center.Add(head);

            var grid = new VisualElement();
            grid.AddToClassList("acs-gen__grid");
            _osc = new CurvePlotView { Baseline = 0, Fill = false };
            _env = new CurvePlotView();
            _pitch = new CurvePlotView { CurveColor = StudioTheme.Warning };
            _filter = new FrequencyResponseView(76) { CurveColor = StudioTheme.Success };
            grid.Add(Viz("Oscillator", _osc));
            grid.Add(Viz("Amplitude Envelope", _env));
            grid.Add(Viz("Pitch", _pitch));
            grid.Add(Viz("Filter Response", _filter));
            center.Add(grid);

            var waveCard = new VisualElement();
            waveCard.AddToClassList("acs-gen__wavecard");
            waveCard.Add(UIUtil.Label("GENERATED WAVEFORM", "acs-overline"));
            _wave = new BufferWaveformView(150);
            waveCard.Add(_wave);
            center.Add(waveCard);

            var actions = new VisualElement();
            actions.AddToClassList("acs-gen__actions");
            actions.Add(new StudioButton(Icon.Play, "Preview", Preview, "Play the generated sound (Space)", "acs-btn--accent"));
            actions.Add(new StudioButton(Icon.Waveform, "Generate", () => Regenerate(true), "Re-render with the current settings"));
            actions.Add(new StudioButton(Icon.Dice, "Randomize", Randomize, "Deterministically randomize the settings around their current values"));
            var seedBox = new VisualElement();
            seedBox.AddToClassList("acs-row");
            seedBox.Add(UIUtil.Label("Seed", "acs-label-dim"));
            _seed = new IntegerField { value = _s.seed, isDelayed = true };
            _seed.AddToClassList("acs-field");
            _seed.AddToClassList("acs-seed");
            _seed.RegisterValueChangedCallback(e => Edit("Generator Seed", s => s.seed = e.newValue));
            seedBox.Add(_seed);
            seedBox.Add(new StudioButton(Icon.Dice, null, () => Edit("New Seed", s => s.seed = new System.Random().Next(1, 999999)), "New random seed (noise & randomization)", "acs-btn--sm"));
            actions.Add(seedBox);
            actions.Add(UIUtil.Spacer());
            actions.Add(new StudioButton(Icon.Save, "Save Preset", SavePreset, "Save these settings to the preset library"));
            actions.Add(new StudioButton(Icon.Plus, "Insert Into Timeline", Insert, "Add as a generator clip at the playhead", "acs-btn--soft"));
            actions.Add(new StudioButton(Icon.Export, "Export", ExportWav, "Export the generated sound (format from Export settings: WAV/AIFF/FLAC/MP3/OGG)"));
            center.Add(actions);
            Add(center);

            // Right: parameters
            _params = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            _params.AddToClassList("acs-gen__params");
            Add(_params);

            BuildDropOverlay();
            BuildPresetList();
            RebuildParams();
            Regenerate(true);
            schedule.Execute(Tick).Every(30);
        }

        private static VisualElement Viz(string title, VisualElement content)
        {
            var c = new VisualElement();
            c.AddToClassList("acs-viz");
            c.Add(UIUtil.Label(title.ToUpperInvariant(), "acs-overline"));
            c.Add(content);
            return c;
        }

        /// <summary>Copy of the settings currently in the editor (including the user's slider edits).</summary>
        public GeneratorSettings CurrentSettings => _s.Clone();

        /// <summary>Replaces the editor's settings (used by the AI designer). Undoable when bound to a timeline clip.</summary>
        public void ApplyExternal(GeneratorSettings g, string undoName)
        {
            var copy = g.Clone();
            Edit(undoName, s => CopyInto(copy, s));
            RebuildParams();
            Regenerate(true);
        }

        /// <param name="recordUndo">False when the caller's own undo step already covers the change (e.g. opening a
        /// timeline clip that was just converted), so one Ctrl+Z undoes the whole action.</param>
        public void Load(GeneratorSettings s, string clipUid, bool recordUndo = true)
        {
            if (recordUndo) Undo.RecordObject(_doc, "Load Generator Settings");
            _doc.settings = s.Clone();
            if (recordUndo) CommitUndo("Load Generator Settings");
            else _lastHash = _s.ComputeHash();
            _state.generatorClipUid = clipUid;
            RebuildParams();
            Regenerate(true);
        }

        /// <summary>The timeline clip being edited; null when standalone or when the clip is no longer a generator clip (e.g. after undo).</summary>
        private ClipData BoundClip
        {
            get
            {
                if (string.IsNullOrEmpty(_state.generatorClipUid)) return null;
                var c = _ctx.Data.FindClip(_state.generatorClipUid);
                return c != null && c.sourceKind == ClipSourceKind.Generator ? c : null;
            }
        }

        public void OnSessionChanged()
        {
            var c = BoundClip;
            if (c != null && c.generator != null && c.generator.ComputeHash() != _s.ComputeHash())
            {
                // Undo/redo or inspector edits changed the bound clip.
                _doc.settings = c.generator.Clone();
                _lastHash = _s.ComputeHash();
                RebuildParams();
                Regenerate(true);
            }
            UpdateHeader();
        }

        private void UpdateHeader()
        {
            _nameLabel.text = _s.presetName;
            var c = BoundClip;
            if (string.IsNullOrEmpty(_state.generatorClipUid) || c == null) { _bound.text = "Standalone"; _bound.RemoveFromClassList("acs-pill--accent"); }
            else { _bound.text = $"Editing clip · {c.DisplayName}"; _bound.AddToClassList("acs-pill--accent"); }
            _seed.SetValueWithoutNotify(_s.seed);
        }

        // ---------------------------------------------------------- Editing

        private void CommitUndo(string name)
        {
            Undo.FlushUndoRecordObjects();
            Undo.SetCurrentGroupName(name);
            Undo.IncrementCurrentGroup();
            _lastHash = _s.ComputeHash();
        }

        /// <summary>Starts a slider drag: every change until <see cref="EndDrag"/> collapses into one undo step.</summary>
        private void BeginDrag(string name)
        {
            _dragName = name;
            _dragging = true;
            if (BoundClip != null) _ctx.Session.BeginContinuous(name);
            else
            {
                Undo.IncrementCurrentGroup();
                _dragGroup = Undo.GetCurrentGroup();
            }
        }

        private void EndDrag()
        {
            if (!_dragging) return;
            _dragging = false;
            if (BoundClip != null || _dragGroup < 0) { _ctx.Session.EndContinuous(); _lastHash = _s.ComputeHash(); return; }
            Undo.FlushUndoRecordObjects();
            Undo.CollapseUndoOperations(_dragGroup);
            Undo.SetCurrentGroupName(_dragName);
            Undo.IncrementCurrentGroup();
            _dragGroup = -1;
            _lastHash = _s.ComputeHash();
        }

        private void OnUndoRedo()
        {
            if (_doc == null) return;
            ulong h = _s.ComputeHash();
            if (h == _lastHash) return;
            _lastHash = h;
            RebuildParams();
            Regenerate(true);
            UpdateHeader();
        }

        private void Edit(string undo, Action<GeneratorSettings> change, bool continuous = false)
        {
            continuous |= _dragging;
            Undo.RecordObject(_doc, continuous ? _dragName ?? undo : undo);
            change(_s);
            EditorUtility.SetDirty(_doc);
            var c = BoundClip;
            if (c != null)
            {
                var copy = _s.Clone();
                string uid = c.uid;
                Action<RecipeData> apply = d =>
                {
                    var cc = d.FindClip(uid);
                    if (cc == null) return;
                    cc.generator = copy.Clone();
                    long frames = (long)Math.Round(DspMath.Clamp(copy.durationSeconds, 0.005f, ProceduralSynth.MaxDurationSeconds) * d.sampleRate);
                    bool wasFull = cc.lengthSamples >= cc.sourceFrames - 1;
                    cc.sourceFrames = Math.Max(1, frames);
                    // Looping clips repeat the source, so their timeline length is independent of the source length.
                    if (!cc.loop && (wasFull || cc.lengthSamples > frames)) cc.lengthSamples = Math.Max(1, frames);
                    cc.name = copy.presetName;
                };
                if (continuous) _ctx.Session.Continuous(apply, StudioChange.Content, this);
                else _ctx.Session.Modify(undo, apply, StudioChange.Content, this);
            }
            // A bound edit is already one undo step (Session.Modify recorded the generator document and the recipe
            // together); committing again would push an empty step that makes the first Ctrl+Z look like it did nothing.
            if (!continuous)
            {
                if (c == null) CommitUndo(undo);
                else _lastHash = _s.ComputeHash();
            }
            Regenerate(false);
            UpdateVisuals();
            UpdateHeader();
        }

        private void Randomize()
        {
            // Deterministic: the result depends only on the current settings and seed (the seed advances each time).
            var r = GeneratorRandomizer.Randomize(_s, _s.seed);
            Edit("Randomize Generator", s => CopyInto(r, s));
            RebuildParams();
        }

        private static void CopyInto(GeneratorSettings src, GeneratorSettings dst)
        {
            foreach (var f in typeof(GeneratorSettings).GetFields()) f.SetValue(dst, f.GetValue(src));
        }

        private void Regenerate(bool immediate)
        {
            _renderPending = true;
            _changeTime = immediate ? 0 : EditorApplication.timeSinceStartup;
        }

        private void Tick()
        {
            while (_main.TryDequeue(out var a)) a();
            if (!_renderPending || EditorApplication.timeSinceStartup - _changeTime < 0.06) return;
            _renderPending = false;
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            var settings = _s.Clone();
            int sr = _ctx.SampleRate;
            AudioBuffer sample = null;
            if (settings.waveform == OscillatorWaveform.Sample)
            {
                if (_ctx.Library.PrepareSample(settings, out var sampleError)) sample = _ctx.Library.GetPreparedSample(settings);
                else _info.text = sampleError;
            }
            Task.Run(() =>
            {
                try
                {
                    var buf = ProceduralSynth.Render(settings, sr, token, sample);
                    var peaks = WaveformPeaks.Build(buf, token);
                    _main.Enqueue(() =>
                    {
                        if (token.IsCancellationRequested) return;
                        _buffer = buf;
                        _wave.SetPeaks(peaks);
                        float pk = buf.GetPeak();
                        _info.text = $"{buf.DurationSeconds:0.000} s · {sr} Hz · peak {DspMath.GainToDb(pk):0.0} dBFS" + (pk >= 1f ? " · CLIPPING" : "");
                    });
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { _main.Enqueue(() => Debug.LogException(ex)); }
            }, token);
            UpdateVisuals();
        }

        private void UpdateVisuals()
        {
            var s = _s;
            int sr = _ctx.SampleRate;
            // Oscillator: two cycles of the actual oscillator function.
            var osc = new float[256];
            if (s.waveform == OscillatorWaveform.Sample)
            {
                // Overview of the sample (signed peak per bin).
                var smp = _ctx.Library.GetPreparedSample(s);
                if (smp != null)
                {
                    var sd = smp.GetChannel(0);
                    int start = Mathf.Clamp((int)(s.sampleStart * smp.SampleRate), 0, Math.Max(0, smp.Frames - 1));
                    int span = Math.Max(1, (smp.Frames - start) / osc.Length);
                    for (int i = 0; i < osc.Length; i++)
                    {
                        float mx = 0;
                        for (int k = start + i * span; k < Math.Min(smp.Frames, start + (i + 1) * span); k++) if (Math.Abs(sd[k]) > Math.Abs(mx)) mx = sd[k];
                        osc[i] = mx;
                    }
                }
            }
            else if (s.waveform == OscillatorWaveform.WhiteNoise || s.waveform == OscillatorWaveform.PinkNoise)
            {
                var rng = new DeterministicRandom(s.seed);
                double b0 = 0;
                for (int i = 0; i < osc.Length; i++)
                {
                    double w = rng.NextSigned();
                    b0 = s.waveform == OscillatorWaveform.PinkNoise ? 0.9 * b0 + 0.1 * w * 3 : w;
                    osc[i] = (float)DspMath.Clamp(b0, -1, 1);
                }
            }
            else
                for (int i = 0; i < osc.Length; i++)
                {
                    double ph = DspMath.Mod(i / 128.0, 1.0);
                    osc[i] = (float)ProceduralSynth.OscillatorValue(s.waveform, ph, 1.0 / 128.0, s.dutyCycle);
                }
            _osc.Set(osc, -1.3f, 1.3f);

            double dur = DspMath.Clamp(s.durationSeconds, 0.005, ProceduralSynth.MaxDurationSeconds);
            var env = new float[200];
            var pitch = new float[200];
            float pmin = float.MaxValue, pmax = float.MinValue;
            for (int i = 0; i < env.Length; i++)
            {
                double t = i / (double)(env.Length - 1) * dur;
                env[i] = (float)ProceduralSynth.EnvelopeAt(s, t, dur);
                // Samples are transposed in semitones; oscillators are shown on a log-frequency axis.
                pitch[i] = s.waveform == OscillatorWaveform.Sample
                    ? (float)ProceduralSynth.PitchSemitonesAt(s, t) / 12f
                    : (float)Math.Log(Math.Max(1, s.frequency * Math.Pow(2, ProceduralSynth.PitchSemitonesAt(s, t) / 12.0)), 2);
                pmin = Mathf.Min(pmin, pitch[i]);
                pmax = Mathf.Max(pmax, pitch[i]);
            }
            float a = (float)(Math.Max(0.0005, s.attack) / dur), d = (float)((Math.Max(0.0005, s.attack) + s.decay) / dur);
            _env.Set(env, 0, 1.05f, Mathf.Clamp01(a), Mathf.Clamp01(d));
            float pad = Mathf.Max(0.5f, (pmax - pmin) * 0.15f);
            _pitch.Set(pitch, pmin - pad, pmax + pad);

            if (s.filterType == GeneratorFilterType.None) _filter.SetResponse(_ => 1.0);
            else
            {
                double fc = ProceduralSynth.FilterCutoffAt(s, 0, s.sustain);
                var k = s.filterType == GeneratorFilterType.LowPass ? BiquadCoefficients.LowPass(sr, fc, s.filterResonance)
                    : s.filterType == GeneratorFilterType.HighPass ? BiquadCoefficients.HighPass(sr, fc, s.filterResonance)
                    : BandPassUnity(sr, fc, s.filterResonance);
                _filter.SetResponse(f => k.MagnitudeAt(f, sr));
            }
        }

        /// <summary>
        /// The SVF band-pass output peaks at gain Q (analog prototype s/(s²+s/Q+1)), whereas the RBJ band-pass is
        /// normalised to 0 dB peak, so the RBJ numerator is scaled by Q to plot the generator's real response.
        /// </summary>
        private static BiquadCoefficients BandPassUnity(int sr, double fc, double q)
        {
            var k = BiquadCoefficients.BandPass(sr, fc, q);
            k.b0 *= q; k.b1 *= q; k.b2 *= q;
            return k;
        }

        // ---------------------------------------------------------- Parameters UI

        private void RebuildParams()
        {
            float scroll = _params.scrollOffset.y;
            _params.Clear();
            var s = _s;
            ParamRow P(string label, float min, float max, float val, bool log, Func<float, string> fmt, Action<GeneratorSettings, float> set, string tip = null, float? def = null)
            {
                var row = new ParamRow(label, new ValueRange(min, max, def ?? val, log, min < 0 && max > 0), val, fmt, tip);
                row.EditBegan += () => BeginDrag($"Generator {label}");
                row.ValueChanged += v => Edit($"Generator {label}", g => set(g, v));
                row.EditEnded += EndDrag;
                return row;
            }
            string Ms(float v) => v < 1f ? $"{v * 1000:0} ms" : $"{v:0.00} s";
            string Hz(float v) => v >= 1000 ? $"{v / 1000:0.00} kHz" : $"{v:0} Hz";
            string St(float v) => $"{v:+0.0;-0.0;0.0} st";
            string Pct(float v) => $"{v * 100:0}%";

            var name = new SectionCard("Sound");
            var nameField = new TextField { value = s.presetName, isDelayed = true };
            nameField.AddToClassList("acs-field");
            nameField.RegisterValueChangedCallback(e => Edit("Rename Generator", g => g.presetName = e.newValue));
            name.Body.Add(nameField);
            name.Body.Add(P("Duration", 0.02f, 30f, s.durationSeconds, true, Ms, (g, v) => g.durationSeconds = v));
            name.Body.Add(P("Volume", -48f, 0f, s.volumeDb, false, v => $"{v:0.0} dB", (g, v) => g.volumeDb = v, null, -6f));
            _params.Add(name);

            var osc = new SectionCard("Oscillator", false, true, StudioTheme.Accent);
            var wave = new SegmentedControl(new[] { "Sine", "Sqr", "Tri", "Saw", "White", "Pink", "Sample" }, (int)s.waveform);
            wave.AddToClassList("acs-seg--wrap");
            wave.Changed += i => { Edit("Waveform", g => g.waveform = (OscillatorWaveform)i); RebuildParams(); };
            osc.Body.Add(wave);
            if (s.waveform == OscillatorWaveform.Sample) BuildSampleControls(osc.Body, s, P, Ms);
            else
            {
                osc.Body.Add(P("Frequency", 20f, 12000f, s.frequency, true, Hz, (g, v) => g.frequency = v, null, 440f));
                if (s.waveform == OscillatorWaveform.Square) osc.Body.Add(P("Duty Cycle", 0.05f, 0.95f, s.dutyCycle, false, Pct, (g, v) => g.dutyCycle = v, null, 0.5f));
                osc.Body.Add(P("Sub Osc", 0f, 1f, s.subOscMix, false, Pct, (g, v) => g.subOscMix = v, "Sine one octave below", 0f));
            }
            _params.Add(osc);

            var env = new SectionCard("Envelope", false, true, StudioTheme.Accent);
            env.Body.Add(Fields.Switch("One-Shot", s.oneShot, v => { Edit("One-Shot", g => g.oneShot = v); }, "One-shot: A→D then release immediately. Off: hold sustain until Duration − Release."));
            env.Body.Add(P("Attack", 0.0005f, 2f, Mathf.Max(0.0005f, s.attack), true, Ms, (g, v) => g.attack = v, null, 0.005f));
            env.Body.Add(P("Decay", 0.001f, 2f, Mathf.Max(0.001f, s.decay), true, Ms, (g, v) => g.decay = v, null, 0.1f));
            env.Body.Add(P("Sustain", 0f, 1f, s.sustain, false, Pct, (g, v) => g.sustain = v, null, 0.5f));
            env.Body.Add(P("Release", 0.001f, 3f, Mathf.Max(0.001f, s.release), true, Ms, (g, v) => g.release = v, null, 0.2f));
            _params.Add(env);

            var pitch = new SectionCard("Pitch", false, true, StudioTheme.Warning);
            pitch.Body.Add(P("Start", -36f, 36f, s.pitchStart, false, St, (g, v) => g.pitchStart = v, null, 0f));
            pitch.Body.Add(P("End", -36f, 36f, s.pitchEnd, false, St, (g, v) => g.pitchEnd = v, null, 0f));
            pitch.Body.Add(P("Sweep Time", 0.005f, 5f, Mathf.Max(0.005f, s.pitchSweepTime), true, Ms, (g, v) => g.pitchSweepTime = v, null, 0.2f));
            pitch.Body.Add(P("Sweep Curve", 0.2f, 5f, Mathf.Max(0.2f, s.pitchCurve), true, v => $"{v:0.00}", (g, v) => g.pitchCurve = v, "<1 fast start, >1 slow start", 1f));
            pitch.Body.Add(P("Jump", -24f, 24f, s.pitchJump, false, St, (g, v) => g.pitchJump = Mathf.Round(v), "Instant pitch step (coin-style arpeggio)", 0f));
            pitch.Body.Add(P("Jump Time", 0f, 2f, s.pitchJumpTime, false, Ms, (g, v) => g.pitchJumpTime = v, null, 0.1f));
            pitch.Body.Add(P("Vibrato Rate", 0f, 30f, s.vibratoRate, false, v => $"{v:0.0} Hz", (g, v) => g.vibratoRate = v, null, 0f));
            pitch.Body.Add(P("Vibrato Depth", 0f, 2f, s.vibratoDepth, false, St, (g, v) => g.vibratoDepth = v, null, 0f));
            _params.Add(pitch);

            var mod = new SectionCard("Noise & Modulation", false, true, StudioTheme.Success);
            mod.Body.Add(P("Noise Mix", 0f, 1f, s.noiseMix, false, Pct, (g, v) => g.noiseMix = v, null, 0f));
            var nc = new SegmentedControl(new[] { "White", "Pink" }, (int)s.noiseColor);
            nc.Changed += i => Edit("Noise Color", g => g.noiseColor = (NoiseColor)i);
            var ncRow = UIUtil.Row(UIUtil.Label("Noise Color", "acs-param__label"), nc);
            ncRow.AddToClassList("acs-param");
            mod.Body.Add(ncRow);
            mod.Body.Add(P("Noise FM", 0f, 12f, s.noiseFm, false, St, (g, v) => g.noiseFm = v, "Random pitch wobble (machines, engines)", 0f));
            mod.Body.Add(P("Tremolo Rate", 0f, 40f, s.tremoloRate, false, v => $"{v:0.0} Hz", (g, v) => g.tremoloRate = v, null, 0f));
            mod.Body.Add(P("Tremolo Depth", 0f, 1f, s.tremoloDepth, false, Pct, (g, v) => g.tremoloDepth = v, null, 0f));
            _params.Add(mod);

            var filt = new SectionCard("Filter", false, true, StudioTheme.Success);
            var ft = new SegmentedControl(new[] { "Off", "LP", "HP", "BP" }, (int)s.filterType);
            ft.Changed += i => { Edit("Filter Type", g => g.filterType = (GeneratorFilterType)i); RebuildParams(); };
            filt.Body.Add(ft);
            if (s.filterType != GeneratorFilterType.None)
            {
                filt.Body.Add(P("Cutoff", 20f, 20000f, s.filterCutoff, true, Hz, (g, v) => g.filterCutoff = v, null, 4000f));
                filt.Body.Add(P("Resonance", 0.3f, 12f, Mathf.Max(0.3f, s.filterResonance), true, v => $"Q {v:0.00}", (g, v) => g.filterResonance = v, null, 0.707f));
                filt.Body.Add(P("Env Amount", -4f, 6f, s.filterEnvOctaves, false, v => $"{v:+0.0;-0.0;0} oct", (g, v) => g.filterEnvOctaves = v, "Cutoff follows the amplitude envelope", 0f));
                filt.Body.Add(P("LFO Rate", 0f, 20f, s.filterLfoRate, false, v => $"{v:0.0} Hz", (g, v) => g.filterLfoRate = v, null, 0f));
                filt.Body.Add(P("LFO Depth", 0f, 4f, s.filterLfoOctaves, false, v => $"{v:0.0} oct", (g, v) => g.filterLfoOctaves = v, null, 0f));
            }
            _params.Add(filt);

            var outc = new SectionCard("Output");
            outc.Body.Add(P("Drive", 0f, 1f, s.drive, false, Pct, (g, v) => g.drive = v, "tanh saturation", 0f));
            _params.Add(outc);

            UpdateVisuals();
            UpdateHeader();
            _params.schedule.Execute(() => _params.scrollOffset = new Vector2(0, scroll));
        }

        private delegate ParamRow ParamFactory(string label, float min, float max, float val, bool log, Func<float, string> fmt,
            Action<GeneratorSettings, float> set, string tip = null, float? def = null);

        private void BuildSampleControls(VisualElement body, GeneratorSettings s, ParamFactory P, Func<float, string> ms)
        {
            var path = string.IsNullOrEmpty(s.sampleGuid) ? null : AssetDatabase.GUIDToAssetPath(s.sampleGuid);
            var clip = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            var field = new ObjectField("Sample") { objectType = typeof(AudioClip), allowSceneObjects = false, value = clip };
            field.AddToClassList("acs-objectfield");
            field.tooltip = "The audio the generator plays instead of an oscillator. Pitch transposes it; envelope, filter, noise, tremolo and drive process it.";
            field.RegisterValueChangedCallback(e => { SetSample(e.newValue as AudioClip, true); });
            body.Add(field);
            if (clip == null)
            {
                body.Add(Fields.Warning(string.IsNullOrEmpty(s.sampleGuid) ? "Choose an AudioClip, click Import Sound at the top, or drag a sound file onto this tab." : $"Sample '{s.sampleName}' is missing.", clip == null && !string.IsNullOrEmpty(s.sampleGuid)));
                return;
            }
            float len = 0;
            if (_ctx.Library.TryGetClip(clip, out var buf, out _, out var err)) len = (float)buf.DurationSeconds;
            else body.Add(Fields.Warning(err, true));
            body.Add(P("Start Offset", 0f, Mathf.Max(0.01f, len), s.sampleStart, false, ms, (g, v) => g.sampleStart = v, "Where playback starts inside the sample", 0f));
            body.Add(new StudioButton(Icon.None, "Fit Duration to Sample", () => Edit("Fit Duration to Sample", g => g.durationSeconds = Mathf.Clamp(len - g.sampleStart, 0.02f, ProceduralSynth.MaxDurationSeconds)), "Set Duration to the remaining sample length", "acs-btn--soft", "acs-btn--sm"));
            body.Add(UIUtil.Label($"{len:0.00} s source · played mono. Pitch ±semitones transposes (and speeds up / slows down) the sample.", "acs-hint"));
        }

        // ------------------------------------------------------------------ Import a sound to edit

        private const string LastImportDirPref = "ACS.Generator.LastImportDir";
        private static readonly string[] AudioExtensions = { ".wav", ".aif", ".aiff", ".mp3", ".ogg", ".flac" };
        private VisualElement _dropOverlay;

        private void ShowImportMenu(VisualElement anchor)
        {
            var m = new GenericMenu();
            m.AddItem(new GUIContent("Browse for a File…"), false, BrowseForSound);
            AudioClip selected = Selection.activeObject as AudioClip;
            if (selected != null) m.AddItem(new GUIContent($"Use Selected in Project ({selected.name})"), false, () => ImportClip(selected));
            else m.AddDisabledItem(new GUIContent("Use Selected in Project (select an AudioClip first)"));
            var recent = RecentTakes(12);
            if (recent.Count == 0) m.AddDisabledItem(new GUIContent("Recent AI Takes/(none yet)"));
            foreach (var path in recent)
            {
                string p = path;
                m.AddItem(new GUIContent("Recent AI Takes/" + System.IO.Path.GetFileNameWithoutExtension(p).Replace('/', '-')), false,
                    () => ImportClip(AssetDatabase.LoadAssetAtPath<AudioClip>(p)));
            }
            m.AddSeparator("");
            m.AddDisabledItem(new GUIContent("Tip: drag a sound file or AudioClip onto this tab"));
            m.DropDown(anchor.worldBound);
        }

        private static List<string> RecentTakes(int max)
        {
            var list = TakeStore.Takes(ElevenLabsPanel.OutputFolder, true);
            if (list.Count > max) list.RemoveRange(max, list.Count - max);
            return list;
        }

        private void BrowseForSound()
        {
            string dir = EditorPrefs.GetString(LastImportDirPref, "");
            if (string.IsNullOrEmpty(dir) || !System.IO.Directory.Exists(dir)) dir = Application.dataPath;
            string path = EditorUtility.OpenFilePanelWithFilters("Import a sound to edit", dir, new[] { "Audio", "wav,aif,aiff,mp3,ogg,flac", "All files", "*" });
            if (string.IsNullOrEmpty(path)) return;
            EditorPrefs.SetString(LastImportDirPref, System.IO.Path.GetDirectoryName(path));
            ImportFile(path);
        }

        /// <summary>Copies a file from outside the project into Assets/AudioCreationStudio/Imported (originals untouched) and loads it.</summary>
        public void ImportFile(string fullPath)
        {
            if (!IsAudioFile(fullPath)) { _ctx.Toast?.Invoke($"Not a supported audio file: {System.IO.Path.GetFileName(fullPath)} (wav, aiff, mp3, ogg, flac)", true); return; }
            AudioClip clip;
            try { clip = StudioCommands.ImportExternalFile(fullPath); }
            catch (Exception ex) { _ctx.Toast?.Invoke("Could not import: " + ex.Message, true); return; }
            if (clip == null) { _ctx.Toast?.Invoke($"Unity could not read {System.IO.Path.GetFileName(fullPath)} as audio", true); return; }
            ImportClip(clip);
        }

        /// <summary>Loads a clip as the generator's Sample source with neutral settings, so it plays unchanged until edited.</summary>
        public void ImportClip(AudioClip clip)
        {
            if (clip == null) return;
            SetSample(clip, false);
            if (_s.waveform == OscillatorWaveform.Sample && _s.sampleName == clip.name)
                _ctx.Toast?.Invoke($"Loaded '{clip.name}'. It plays unchanged: tweak Pitch, Envelope, Filter, Noise, Drive… or Randomize for variants, then Insert or Export.", false);
        }

        private static bool IsAudioFile(string path) =>
            Array.IndexOf(AudioExtensions, System.IO.Path.GetExtension(path ?? "").ToLowerInvariant()) >= 0;

        /// <summary>Drag a sound file (Finder / Explorer) or an AudioClip (Project window) onto the tab to edit it.</summary>
        private void BuildDropOverlay()
        {
            _dropOverlay = new VisualElement { pickingMode = PickingMode.Ignore };
            _dropOverlay.AddToClassList("acs-gen__drop");
            var box = new VisualElement { pickingMode = PickingMode.Ignore };
            box.AddToClassList("acs-gen__dropbox");
            box.Add(new IconElement(Icon.Import, 28) { Color = StudioTheme.Accent });
            var l = UIUtil.Label("Drop to edit this sound", "acs-gen__droptitle");
            l.pickingMode = PickingMode.Ignore;
            box.Add(l);
            var h = UIUtil.Label("Loaded as the Sample source; it plays unchanged until you tweak it", "acs-hint");
            h.pickingMode = PickingMode.Ignore;
            box.Add(h);
            _dropOverlay.Add(box);
            _dropOverlay.style.display = DisplayStyle.None;
            Add(_dropOverlay);

            RegisterCallback<DragUpdatedEvent>(e =>
            {
                bool ok = DraggedSound(out _, out _);
                DragAndDrop.visualMode = ok ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
                _dropOverlay.style.display = ok ? DisplayStyle.Flex : DisplayStyle.None;
            });
            RegisterCallback<DragPerformEvent>(e =>
            {
                _dropOverlay.style.display = DisplayStyle.None;
                if (!DraggedSound(out var clip, out var file)) return;
                DragAndDrop.AcceptDrag();
                e.StopPropagation();
                // Defer: importing assets inside the drag callback upsets the drag state.
                schedule.Execute(() => { if (clip != null) ImportClip(clip); else ImportFile(file); });
            });
            RegisterCallback<DragLeaveEvent>(_ => _dropOverlay.style.display = DisplayStyle.None);
            RegisterCallback<DragExitedEvent>(_ => _dropOverlay.style.display = DisplayStyle.None);
        }

        /// <summary>The first AudioClip, or the first audio file path from outside Unity, in the current drag.</summary>
        private static bool DraggedSound(out AudioClip clip, out string file)
        {
            clip = null;
            file = null;
            foreach (var o in DragAndDrop.objectReferences)
                if (o is AudioClip ac) { clip = ac; return true; }
            foreach (var p in DragAndDrop.paths)
                if (IsAudioFile(p))
                {
                    if (p.StartsWith("Assets/") || p.StartsWith("Packages/"))
                    {
                        clip = AssetDatabase.LoadAssetAtPath<AudioClip>(p);
                        if (clip != null) return true;
                    }
                    else if (System.IO.File.Exists(p)) { file = p; return true; }
                }
            return false;
        }

        /// <summary>Switches the generator to play an AudioClip (undoable). Neutral settings when <paramref name="keepProcessing"/> is false.</summary>
        public void SetSample(AudioClip clip, bool keepProcessing)
        {
            if (clip == null) return;
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(clip, out string guid, out long _)) { _ctx.Toast?.Invoke("The sample must be an asset in the project", true); return; }
            float len = _ctx.Library.TryGetClip(clip, out var buf, out _, out var err) ? (float)buf.DurationSeconds : 1f;
            if (err != null && buf == null) { _ctx.Toast?.Invoke(err, true); return; }
            var g = keepProcessing ? _s.Clone() : NeutralSampleSettings();
            g.waveform = OscillatorWaveform.Sample;
            g.sampleGuid = guid;
            g.sampleName = clip.name;
            g.sampleStart = 0f;
            g.durationSeconds = Mathf.Clamp(len, 0.02f, ProceduralSynth.MaxDurationSeconds);
            if (!keepProcessing) g.presetName = clip.name;
            ApplyExternal(g, "Use Sample in Generator");
        }

        /// <summary>Settings that play the sample unchanged (unity gain, no pitch/filter/noise/modulation).</summary>
        public static GeneratorSettings NeutralSampleSettings() => new GeneratorSettings
        {
            waveform = OscillatorWaveform.Sample, volumeDb = 0f, oneShot = false,
            attack = 0.0005f, decay = 0.001f, sustain = 1f, release = 0.005f,
            pitchStart = 0f, pitchEnd = 0f, pitchSweepTime = 0.2f, pitchCurve = 1f, pitchJump = 0f, pitchJumpTime = 0.1f,
            vibratoRate = 0f, vibratoDepth = 0f, noiseMix = 0f, noiseFm = 0f, tremoloRate = 0f, tremoloDepth = 0f,
            filterType = GeneratorFilterType.None, filterCutoff = 4000f, filterResonance = 0.707f, drive = 0f, subOscMix = 0f, seed = 1
        };

        private void BuildPresetList()
        {
            _presetList.Clear();
            string q = (_search.value ?? "").Trim().ToLowerInvariant();
            string lastCat = null;
            foreach (var e in GeneratorPresets.All)
            {
                if (q.Length > 0 && !e.Name.ToLowerInvariant().Contains(q) && !e.Category.ToLowerInvariant().Contains(q)) continue;
                if (e.Category != lastCat) { _presetList.Add(UIUtil.Label(e.Category, "acs-gen__cat")); lastCat = e.Category; }
                var entry = e;
                _presetList.Add(PresetItem(e.Name, false, () => LoadPreset(entry.Create())));
            }
            var user = PresetStore.OfKind(PresetKind.Generator);
            bool header = false;
            foreach (var p in user)
            {
                if (q.Length > 0 && !p.displayName.ToLowerInvariant().Contains(q)) continue;
                if (!header) { _presetList.Add(UIUtil.Label("Your Presets", "acs-gen__cat")); header = true; }
                var preset = p;
                _presetList.Add(PresetItem(p.displayName, true, () => { var g = PresetStore.LoadGenerator(preset); if (g != null) LoadPreset(g); }));
            }
        }

        private VisualElement PresetItem(string name, bool user, Action click)
        {
            var row = new VisualElement();
            row.AddToClassList("acs-listitem");
            row.Add(new IconElement(user ? Icon.Save : Icon.Sine, 14));
            row.Add(UIUtil.Label(name, "acs-listitem__label"));
            row.AddManipulator(new Clickable(() =>
            {
                click();
                foreach (var c in _presetList.Children()) c.RemoveFromClassList("acs-listitem--on");
                row.AddToClassList("acs-listitem--on");
            }));
            return row;
        }

        private void LoadPreset(GeneratorSettings g)
        {
            Edit("Load Generator Preset", s => CopyInto(g, s));
            RebuildParams();
            Preview();
        }

        // ---------------------------------------------------------- Actions

        private void Preview()
        {
            if (_buffer == null) { Regenerate(true); return; }
            _ctx.Transport.PlayBuffer(_buffer, false, $"Generator · {_s.presetName}");
        }

        public bool HandlesSpace => true;

        public void TogglePreview()
        {
            if (_ctx.Transport.IsPlaying && _ctx.Transport.Target == PreviewTarget.Buffer) _ctx.Transport.Stop();
            else Preview();
        }

        private void SavePreset()
        {
            var p = PresetStore.Save(PresetKind.Generator, _s.presetName, "Generators", _s.Clone());
            _ctx.Toast?.Invoke($"Saved generator preset '{p.displayName}'", false);
            BuildPresetList();
        }

        private void Insert()
        {
            var clip = _ctx.Commands.InsertGenerator(_s.Clone());
            _state.generatorClipUid = clip.uid;
            UpdateHeader();
            _ctx.Toast?.Invoke($"Inserted '{_s.presetName}' at the playhead — further edits update that clip", false);
        }

        private void ExportWav()
        {
            if (_buffer == null) return;
            var settings = _ctx.Data.export;
            var opts = ExportOptions.From(settings);
            opts.SampleRate = 0;
            var r = ExportService.ExportBufferInteractive(_buffer, opts, _s.presetName);
            if (r != null) _ctx.Toast?.Invoke($"Exported {System.IO.Path.GetFileName(r.Path)} ({r.DurationSeconds:0.000} s)", false);
        }
    }
}
