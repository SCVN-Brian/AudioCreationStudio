using System;
using AudioCreationStudio.DSP;
using AudioCreationStudio.Export;
using AudioCreationStudio.Models;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AudioCreationStudio.EditorUI
{
    /// <summary>
    /// Seamless loop editor: loop markers with zero-crossing assistance, adjustable linear/equal-power
    /// crossfade, boundary visualization (raw cut vs. crossfaded), click analysis, repeated preview
    /// (×1/×5/×10/∞) and loop-ready WAV export with a 'smpl' loop chunk.
    /// </summary>
    public sealed class LoopEditorView : VisualElement
    {
        private readonly StudioContext _ctx;
        private readonly BufferWaveformView _main;
        private readonly BufferWaveformView _rawJoin, _loopJoin;
        private readonly VisualElement _settings, _report;
        private readonly Label _srcInfo;
        private AudioBuffer _source;
        private AudioBuffer _loop;
        private string _sourceKey;

        private LoopSettings L => _ctx.Data.loop;

        public LoopEditorView(StudioContext ctx)
        {
            _ctx = ctx;
            AddToClassList("acs-loop");

            var left = new VisualElement();
            left.AddToClassList("acs-loop__main");
            var head = new VisualElement();
            head.AddToClassList("acs-gen__head");
            head.Add(UIUtil.Label("Seamless Loop", "acs-gen__name"));
            _srcInfo = UIUtil.Label("", "acs-label-dim");
            head.Add(UIUtil.Spacer());
            head.Add(_srcInfo);
            left.Add(head);

            var srcCard = new VisualElement();
            srcCard.AddToClassList("acs-gen__wavecard");
            srcCard.Add(UIUtil.Label("SOURCE · drag the green/red markers to set the loop", "acs-overline"));
            _main = new BufferWaveformView(170);
            _main.MarkerDragBegan += _ => _ctx.Session.BeginContinuous("Move Loop Marker");
            _main.MarkerMoved += (i, f) =>
            {
                _ctx.Session.Continuous(d =>
                {
                    if (i == 0) d.loop.loopStart = Math.Min(f, d.loop.loopEnd - 16);
                    else d.loop.loopEnd = Math.Max(f, d.loop.loopStart + 16);
                }, StudioChange.Content, this);
                Process();
            };
            _main.MarkerDragEnded += i =>
            {
                if (L.snapToZeroCrossing && _source != null) SnapMarkers(i == 0, i == 1);
                _ctx.Session.EndContinuous();
                Process();
            };
            srcCard.Add(_main);
            left.Add(srcCard);

            var joins = new VisualElement();
            joins.AddToClassList("acs-loop__joins");
            _rawJoin = new BufferWaveformView(110) { WaveColor = StudioTheme.TextDim };
            _loopJoin = new BufferWaveformView(110) { WaveColor = StudioTheme.Success };
            joins.Add(Card("BOUNDARY · RAW CUT (end → start)", _rawJoin));
            joins.Add(Card("BOUNDARY · CROSSFADED LOOP", _loopJoin));
            left.Add(joins);

            var actions = new VisualElement();
            actions.AddToClassList("acs-gen__actions");
            actions.Add(new StudioButton(Icon.Loop, "Loop ∞", () => PlayLoop(0), "Play the loop continuously", "acs-btn--accent"));
            actions.Add(new StudioButton(Icon.Play, "×1", () => PlayLoop(1), "Play once"));
            actions.Add(new StudioButton(Icon.Play, "×5", () => PlayLoop(5), "Play 5 repetitions back to back"));
            actions.Add(new StudioButton(Icon.Play, "×10", () => PlayLoop(10), "Play 10 repetitions back to back"));
            actions.Add(new StudioButton(Icon.Stop, null, () => _ctx.Transport.Stop(), "Stop"));
            actions.Add(UIUtil.Spacer());
            actions.Add(new StudioButton(Icon.Magnet, "Snap to Zero Crossings", () => { _ctx.Session.Modify("Snap Loop Markers", _ => { }, StudioChange.Content); SnapMarkers(true, true); Process(); }, "Move both markers to the nearest rising zero crossings"));
            actions.Add(new StudioButton(Icon.Export, "Export Loop WAV", ExportLoop, "Export loop-ready WAV with smpl loop metadata", "acs-btn--soft"));
            left.Add(actions);
            Add(left);

            var right = new ScrollView();
            right.AddToClassList("acs-gen__params");
            _settings = new VisualElement();
            right.Add(_settings);
            _report = new VisualElement();
            right.Add(_report);
            Add(right);
        }

        private static VisualElement Card(string title, VisualElement content)
        {
            var c = new VisualElement();
            c.AddToClassList("acs-gen__wavecard");
            c.AddToClassList("acs-grow");
            c.Add(UIUtil.Label(title, "acs-overline"));
            c.Add(content);
            return c;
        }

        public void OpenForClip(string clipUid)
        {
            _ctx.Session.Modify("Loop Source", d => { d.loop.source = LoopSourceKind.Clip; d.loop.clipUid = clipUid; d.loop.loopEnd = 0; });
            Refresh();
        }

        public void Refresh()
        {
            ResolveSource();
            BuildSettings();
            Process();
        }

        private void ResolveSource()
        {
            var l = L;
            _source = null;
            string info = "";
            switch (l.source)
            {
                case LoopSourceKind.Clip:
                {
                    var c = _ctx.Data.FindClip(l.clipUid);
                    if (c == null) { info = "Select a clip on the timeline (right-click → Open in Loop Editor)"; break; }
                    if (c.sourceKind == ClipSourceKind.Generator) { _ctx.Library.PrepareSample(c.generator, out _); _source = _ctx.Library.GetGenerated(c.generator, _ctx.SampleRate); }
                    else _ctx.Library.TryGetClip(c.sourceClip, out _source, out _, out info);
                    info = $"Clip source · {c.DisplayName}";
                    break;
                }
                case LoopSourceKind.AudioClipAsset:
                    if (l.assetClip != null && !_ctx.Library.TryGetClip(l.assetClip, out _source, out _, out var err)) info = err;
                    else info = l.assetClip != null ? $"Asset · {l.assetClip.name}" : "Choose an AudioClip";
                    break;
                default:
                {
                    var res = _ctx.Render.IsUpToDate ? _ctx.Render.Current : _ctx.Render.RenderNow(RenderScope.FullMix);
                    _source = res?.Master;
                    info = "Rendered mix of the current recipe";
                    break;
                }
            }
            if (_source != null)
            {
                info += $"  ·  {_source.DurationSeconds:0.000} s · {_source.SampleRate} Hz";
                string key = $"{l.source}:{l.clipUid}:{_source.Frames}:{_source.GetHashCode()}";
                if (key != _sourceKey) { _main.SetBuffer(_source); _sourceKey = key; }
                if (l.loopEnd <= l.loopStart || l.loopEnd > _source.Frames)
                {
                    // Sensible defaults: leave room for a pre-roll crossfade.
                    long start = Math.Min(_source.Frames / 10, _source.Frames / 2);
                    long end = Math.Max(start + 16, _source.Frames - _source.Frames / 10);
                    // View defaults only: not an edit, so the recipe is not marked dirty.
                    l.loopStart = start;
                    l.loopEnd = end;
                }
            }
            else _main.SetBuffer(null);
            _srcInfo.text = info;
        }

        private void BuildSettings()
        {
            _settings.Clear();
            var l = L;
            int sr = _source?.SampleRate ?? _ctx.SampleRate;
            var src = new SectionCard("Source");
            var seg = new SegmentedControl(new[] { "Mix", "Clip", "Asset" }, (int)l.source);
            seg.Changed += i => { _ctx.Session.Modify("Loop Source", d => { d.loop.source = (LoopSourceKind)i; d.loop.loopEnd = 0; if (i == 1 && _ctx.Session.PrimaryClip != null) d.loop.clipUid = _ctx.Session.PrimaryClip.uid; }); Refresh(); };
            src.Body.Add(seg);
            if (l.source == LoopSourceKind.AudioClipAsset)
            {
                var of = new ObjectField("AudioClip") { objectType = typeof(AudioClip), value = l.assetClip, allowSceneObjects = false };
                of.AddToClassList("acs-objectfield");
                of.RegisterValueChangedCallback(e => { _ctx.Session.Modify("Loop Asset", d => { d.loop.assetClip = e.newValue as AudioClip; d.loop.loopEnd = 0; }); Refresh(); });
                src.Body.Add(of);
            }
            if (l.source == LoopSourceKind.Clip)
                src.Body.Add(new StudioButton(Icon.Waveform, "Use Selected Clip", () => { var c = _ctx.Session.PrimaryClip; if (c != null) OpenForClip(c.uid); }, null, "acs-btn--soft", "acs-btn--sm"));
            _settings.Add(src);

            var markers = new SectionCard("Loop Points");
            markers.Body.Add(Fields.Time(_ctx, "Start", l.loopStart, "Loop Start", (d, v) => d.loop.loopStart = Math.Min(v, d.loop.loopEnd - 16), this, null, sr));
            markers.Body.Add(Fields.Time(_ctx, "End", l.loopEnd, "Loop End", (d, v) => d.loop.loopEnd = Math.Max(v, d.loop.loopStart + 16), this, null, sr));
            markers.Body.Add(Fields.Info("Length", $"{(l.loopEnd - l.loopStart) / (double)sr:0.000} s"));
            markers.Body.Add(Fields.Switch("Zero Snap", l.snapToZeroCrossing, v => _ctx.Session.Modify("Zero Crossing Snap", d => d.loop.snapToZeroCrossing = v),
                "Snap markers to rising zero crossings after dragging. Zero crossings alone do not guarantee a seamless loop; the crossfade does the real work."));
            markers.Body.Add(Fields.Param(_ctx, "Search Window", new ValueRange(0.5f, 50f, 5f, true), l.zeroSearchMs, v => $"{v:0.0} ms", "Zero Search Window", (d, v) => d.loop.zeroSearchMs = v, this));
            _settings.Add(markers);

            var xf = new SectionCard("Crossfade");
            float maxMs = Mathf.Max(1f, (l.loopEnd - l.loopStart) * 500f / sr);
            var xfRow = Fields.Param(_ctx, "Length", new ValueRange(0f, Mathf.Min(1000f, maxMs), 50f), (float)(l.crossfadeSamples * 1000.0 / sr), v => $"{v:0} ms", "Crossfade Length",
                (d, v) => d.loop.crossfadeSamples = (long)Math.Round(v / 1000.0 * sr), this);
            xfRow.ValueChanged += _ => Process();
            xf.Body.Add(xfRow);
            var curve = new SegmentedControl(new[] { "Linear", "Equal Power" }, l.crossfadeCurve == FadeCurve.EqualPower ? 1 : 0);
            curve.Changed += i => { _ctx.Session.Modify("Crossfade Curve", d => d.loop.crossfadeCurve = i == 1 ? FadeCurve.EqualPower : FadeCurve.Linear); Process(); };
            var cr = UIUtil.Row(UIUtil.Label("Curve", "acs-param__label"), curve);
            cr.AddToClassList("acs-param");
            xf.Body.Add(cr);
            xf.Body.Add(UIUtil.Label(l.loopStart >= l.crossfadeSamples
                ? "Pre-roll mode: the loop tail is blended with the audio before the loop start. Loop length is preserved."
                : "Overlap mode (no room before the start): head and tail are blended; the loop gets shorter by the crossfade length.", "acs-hint"));
            _settings.Add(xf);

            if (l.source == LoopSourceKind.RenderedMix)
            {
                var apply = new StudioButton(Icon.Loop, "Use as Timeline Loop Region", () =>
                    _ctx.Session.Modify("Set Loop Region", d => { d.loopRegion.enabled = true; d.loopRegion.start = d.loop.loopStart; d.loopRegion.end = d.loop.loopEnd; }), null, "acs-btn--soft", "acs-btn--sm");
                _settings.Add(apply);
            }
        }

        private void SnapMarkers(bool start, bool end)
        {
            if (_source == null) return;
            var l = L;
            int radius = (int)(l.zeroSearchMs / 1000f * _source.SampleRate);
            long s = start ? ZeroCrossing.FindNearest(_source, l.loopStart, radius) : l.loopStart;
            long e = end ? ZeroCrossing.FindNearest(_source, l.loopEnd, radius) : l.loopEnd;
            _ctx.Session.Continuous(d => { d.loop.loopStart = s; d.loop.loopEnd = Math.Max(e, s + 16); }, StudioChange.Content, this);
        }

        private void Process()
        {
            var l = L;
            _main.Markers.Clear();
            _main.Regions.Clear();
            _report.Clear();
            if (_source == null) { _main.Refresh(); return; }
            long s = DspMath.Clamp(l.loopStart, 0, _source.Frames), e = DspMath.Clamp(l.loopEnd, s, _source.Frames);
            int xf = (int)Math.Min(l.crossfadeSamples, (e - s) / 2);
            _main.Regions.Add(new BufferWaveformView.Region { Start = s, End = e, Color = StudioTheme.Accent.WithAlpha(0.08f) });
            if (xf > 0)
            {
                if (s >= xf) _main.Regions.Add(new BufferWaveformView.Region { Start = s - xf, End = s, Color = StudioTheme.Warning.WithAlpha(0.14f) });
                _main.Regions.Add(new BufferWaveformView.Region { Start = e - xf, End = e, Color = StudioTheme.Warning.WithAlpha(0.14f) });
            }
            _main.Markers.Add(new BufferWaveformView.Marker { Frame = s, Color = StudioTheme.Success, Label = "START", Draggable = true });
            _main.Markers.Add(new BufferWaveformView.Marker { Frame = e, Color = StudioTheme.Error, Label = "END", Draggable = true });
            _main.Refresh();

            var raw = _source.Slice((int)s, (int)(e - s));
            _loop = SeamlessLoop.Create(_source, s, e, xf, l.crossfadeCurve);
            ShowJoin(_rawJoin, raw);
            ShowJoin(_loopJoin, _loop);

            var rr = SeamlessLoop.AnalyzeBoundary(raw);
            var lr = SeamlessLoop.AnalyzeBoundary(_loop);
            var card = new SectionCard("Boundary Analysis");
            card.Body.Add(VerdictRow("Raw cut", rr));
            card.Body.Add(VerdictRow("Crossfaded", lr));
            card.Body.Add(Fields.Info("Jump", $"{rr.JumpRatio:0.0}× → {lr.JumpRatio:0.0}× typical step"));
            card.Body.Add(Fields.Info("Level match", $"{rr.LevelMismatchDb:0.0} dB → {lr.LevelMismatchDb:0.0} dB"));
            card.Body.Add(Fields.Info("Clicks found", $"{lr.InternalClicks} (2× repeat)"));
            card.Body.Add(Fields.Info("Loop length", $"{_loop.DurationSeconds:0.000} s · {_loop.Frames:N0} smp"));
            card.Body.Add(UIUtil.Label("Jump compares the wrap-around step with the signal's typical sample-to-sample change; < 3× is inaudible for most material. Always confirm by listening (×10).", "acs-hint"));
            _report.Add(card);
        }

        private static VisualElement VerdictRow(string label, LoopBoundaryReport r)
        {
            var row = new VisualElement();
            row.AddToClassList("acs-param");
            row.Add(UIUtil.Label(label, "acs-param__label"));
            var pill = UIUtil.Label(r.Verdict == LoopVerdict.Seamless ? "Seamless" : r.Verdict == LoopVerdict.Minor ? "Minor bump" : "Click", "acs-pill");
            pill.AddToClassList(r.Verdict == LoopVerdict.Seamless ? "acs-pill--success" : r.Verdict == LoopVerdict.Minor ? "acs-pill--warning" : "acs-pill--error");
            row.Add(pill);
            return row;
        }

        private static void ShowJoin(BufferWaveformView view, AudioBuffer loop)
        {
            int w = Math.Min(loop.Frames / 2, Math.Max(64, loop.SampleRate / 50));
            var join = new AudioBuffer(loop.Channels, w * 2, loop.SampleRate);
            for (int c = 0; c < loop.Channels; c++)
            {
                Array.Copy(loop.GetChannel(c), loop.Frames - w, join.GetChannel(c), 0, w);
                Array.Copy(loop.GetChannel(c), 0, join.GetChannel(c), w, w);
            }
            view.SetBuffer(join);
            view.Markers.Clear();
            view.Markers.Add(new BufferWaveformView.Marker { Frame = w, Color = StudioTheme.Warning, Label = "wrap" });
            view.Refresh();
        }

        private void PlayLoop(int times)
        {
            if (_loop == null || _loop.Frames == 0) return;
            if (times <= 0) _ctx.Transport.PlayBuffer(_loop, true, "Loop ∞");
            else _ctx.Transport.PlayBuffer(SeamlessLoop.Repeat(_loop, times), false, $"Loop ×{times}");
        }

        private void ExportLoop()
        {
            if (_loop == null) return;
            var opts = ExportOptions.From(_ctx.Data.export, true);
            var r = ExportService.ExportBufferInteractive(_loop, opts, $"{_ctx.Data.name}_Loop");
            if (r != null) _ctx.Toast?.Invoke($"Exported loop {System.IO.Path.GetFileName(r.Path)} (smpl loop 0–{r.Frames})", false);
        }
    }
}
