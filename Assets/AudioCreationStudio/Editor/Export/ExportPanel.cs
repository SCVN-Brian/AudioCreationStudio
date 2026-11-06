using System;
using System.Collections.Generic;
using AudioCreationStudio.Export;
using AudioCreationStudio.Models;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AudioCreationStudio.EditorUI
{
    /// <summary>Modal export sheet: scope, file type (WAV/AIFF/FLAC/MP3/OGG), bit depth or bitrate, rate, channels, normalization, trimming, destination, progress and cancel.</summary>
    public sealed class ExportPanel : VisualElement
    {
        private static readonly int[] Rates = { 0, 22050, 32000, 44100, 48000, 96000 };
        private readonly StudioContext _ctx;
        private readonly ExportRunner _runner;
        private readonly VisualElement _body;
        private readonly VisualElement _progressFill;
        private readonly Label _status;
        private readonly StudioButton _exportBtn, _cancelBtn;
        private readonly VisualElement _results;

        private ExportSettings E => _ctx.Data.export;

        public ExportPanel(StudioContext ctx, ExportRunner runner)
        {
            _ctx = ctx;
            _runner = runner;
            AddToClassList("acs-sheet-backdrop");
            this.AddManipulator(new Clickable(() => { if (!_runner.IsRunning) Hide(); }));
            var sheet = new VisualElement();
            sheet.AddToClassList("acs-sheet");
            sheet.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            sheet.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
            Add(sheet);

            var head = new VisualElement();
            head.AddToClassList("acs-sheet__head");
            head.Add(new IconElement(Icon.Export, 18) { Color = StudioTheme.Accent });
            head.Add(UIUtil.Label("Export Audio", "acs-sheet__title"));
            head.Add(UIUtil.Spacer());
            head.Add(new StudioButton(Icon.Close, null, () => { if (!_runner.IsRunning) Hide(); }, "Close", "acs-btn--sm"));
            sheet.Add(head);

            _body = new ScrollView();
            _body.AddToClassList("acs-sheet__body");
            sheet.Add(_body);

            var foot = new VisualElement();
            foot.AddToClassList("acs-sheet__foot");
            var bar = new VisualElement();
            bar.AddToClassList("acs-progress");
            _progressFill = new VisualElement();
            _progressFill.AddToClassList("acs-progress__fill");
            bar.Add(_progressFill);
            foot.Add(bar);
            _status = UIUtil.Label("", "acs-sheet__status");
            foot.Add(_status);
            _results = new VisualElement();
            foot.Add(_results);
            var buttons = new VisualElement();
            buttons.AddToClassList("acs-row");
            buttons.Add(UIUtil.Spacer());
            _cancelBtn = new StudioButton(Icon.Stop, "Cancel", () => _runner.Cancel(), "Cancel the running export");
            buttons.Add(_cancelBtn);
            _exportBtn = new StudioButton(Icon.Export, "Export", Export, "Render and write the audio file", "acs-btn--accent");
            buttons.Add(_exportBtn);
            foot.Add(buttons);
            sheet.Add(foot);

            _runner.Completed += (results, ex, cancelled) => ShowResults(results, ex, cancelled);
            Action refresh = () => { if (IsOpen) Build(); };
            RegisterCallback<AttachToPanelEvent>(_ => AudioStudioSettings.Changed += refresh);
            RegisterCallback<DetachFromPanelEvent>(_ => AudioStudioSettings.Changed -= refresh);
            style.display = DisplayStyle.None;
            schedule.Execute(UpdateProgress).Every(50);
        }

        public bool IsOpen => style.display == DisplayStyle.Flex;

        public void Show()
        {
            Build();
            style.display = DisplayStyle.Flex;
            UpdateProgress();
        }

        public void Hide() => style.display = DisplayStyle.None;

        public void Build()
        {
            _body.Clear();
            var e = E;
            var s = _ctx.Session;
            void Set(string undo, Action<ExportSettings> a) => s.Modify(undo, d => a(d.export), StudioChange.Content, this);

            var scope = new SectionCard("What");
            var seg = new SegmentedControl(new[] { "Full Mix", "Selected Track", "Selected Clip", "Loop Region" }, (int)e.scope);
            seg.Changed += i => { Set("Export Scope", x => x.scope = (ExportScope)i); Build(); };
            scope.Body.Add(seg);
            string scopeInfo = e.scope switch
            {
                ExportScope.SelectedTrack => s.PrimaryTrack != null ? $"Track '{s.PrimaryTrack.name}' (soloed, through master)" : "⚠ No track selected",
                ExportScope.SelectedClip => s.PrimaryClip != null ? $"Clip '{s.PrimaryClip.DisplayName}' (through its track and master)" : "⚠ No clip selected",
                ExportScope.LoopRegion => _ctx.Data.loopRegion.enabled ? $"{_ctx.Data.loopRegion.Length / (double)_ctx.SampleRate:0.000} s loop region" : "⚠ No loop region enabled",
                _ => $"{_ctx.Data.tracks.Count} tracks · {_ctx.Data.ContentEnd / (double)_ctx.SampleRate:0.000} s"
            };
            scope.Body.Add(UIUtil.Label(scopeInfo, "acs-hint"));
            scope.Body.Add(Fields.Switch("Include Tails", e.includeTail, v => Set("Export Tails", x => x.includeTail = v), "Keep delay/reverb tails after the last clip (trimmed below -96 dBFS)"));
            _body.Add(scope);

            var fmt = new SectionCard("Format");
            var types = new SegmentedControl(new[] { "WAV", "AIFF", "FLAC", "MP3", "OGG" }, (int)e.fileType);
            types.Changed += i => { Set("Export File Type", x => x.fileType = (AudioFileType)i); Build(); };
            fmt.Body.Add(types);
            bool lossy = AudioFormats.IsLossy(e.fileType);
            if (!lossy)
            {
                bool flac = e.fileType == AudioFileType.Flac;
                var depthNames = flac ? new[] { "16-bit", "24-bit" } : new[] { "PCM 16-bit", "PCM 24-bit", "Float 32-bit" };
                int depthIndex = flac && e.format == WavSampleFormat.Float32 ? 1 : (int)e.format;
                var f = new SegmentedControl(depthNames, depthIndex);
                f.Changed += i => { Set("Export Format", x => x.format = (WavSampleFormat)i); Build(); };
                fmt.Body.Add(f);
                if (flac) fmt.Body.Add(UIUtil.Label("Lossless, ~40–60% of WAV size. Unity does not import .flac as an AudioClip, so use it for delivery/archival.", "acs-hint"));
                if (e.fileType == AudioFileType.Aiff && e.format == WavSampleFormat.Float32) fmt.Body.Add(UIUtil.Label("Float is written as AIFF-C (fl32).", "acs-hint"));
            }
            else
            {
                int bi = Math.Max(0, Array.IndexOf(FfmpegEncoder.Bitrates, e.lossyBitrateKbps));
                if (Array.IndexOf(FfmpegEncoder.Bitrates, e.lossyBitrateKbps) < 0) bi = Array.IndexOf(FfmpegEncoder.Bitrates, 192);
                fmt.Body.Add(Fields.Popup("Bitrate", Array.ConvertAll(FfmpegEncoder.Bitrates, b => $"{b} kbps"), bi,
                    i => Set("Export Bitrate", x => x.lossyBitrateKbps = FfmpegEncoder.Bitrates[i])));
                fmt.Body.Add(FfmpegRow());
                if (e.fileType == AudioFileType.Mp3)
                    fmt.Body.Add(UIUtil.Label("MP3 adds encoder padding, so it is not sample-accurate for seamless loops. Max 48 kHz, stereo.", "acs-hint"));
            }
            if (e.scope == ExportScope.LoopRegion && !AudioFormats.SupportsLoopMetadata(e.fileType))
                fmt.Body.Add(UIUtil.Label("Loop points ('smpl' chunk) are only written to WAV.", "acs-hint"));
            var rateNames = Array.ConvertAll(Rates, r => r == 0 ? $"Recipe ({_ctx.SampleRate} Hz)" : $"{r} Hz");
            fmt.Body.Add(Fields.Popup("Sample Rate", rateNames, Math.Max(0, Array.IndexOf(Rates, e.sampleRate)), i => Set("Export Rate", x => x.sampleRate = Rates[i])));
            fmt.Body.Add(Fields.Popup("Channels", new[] { $"Recipe ({_ctx.Data.channels})", "Mono", "Stereo" }, Mathf.Clamp(e.channels, 0, 2), i => Set("Export Channels", x => x.channels = i)));
            if (!lossy && e.format == WavSampleFormat.Pcm16) fmt.Body.Add(Fields.Switch("TPDF Dither", e.dither, v => Set("Export Dither", x => x.dither = v), "Adds ±1 LSB triangular dither before 16-bit quantization"));
            _body.Add(fmt);

            var proc = new SectionCard("Processing");
            proc.Body.Add(Fields.Switch("Normalize", e.normalize, v => { Set("Export Normalize", x => x.normalize = v); Build(); }, "Scale so the peak hits the target (never applied silently)"));
            if (e.normalize) proc.Body.Add(Fields.Param(_ctx, "Target Peak", new ValueRange(-24f, 0f, -1f), e.normalizeTargetDb, v => $"{v:0.0} dBFS", "Normalize Target", (d, v) => d.export.normalizeTargetDb = v, this));
            proc.Body.Add(Fields.Switch("Trim Silence", e.trimSilence, v => { Set("Export Trim", x => x.trimSilence = v); Build(); }, "Remove leading/trailing audio below the threshold"));
            if (e.trimSilence) proc.Body.Add(Fields.Param(_ctx, "Threshold", new ValueRange(-96f, -20f, -60f), e.trimThresholdDb, v => $"{v:0} dBFS", "Trim Threshold", (d, v) => d.export.trimThresholdDb = v, this));
            _body.Add(proc);

            var dest = new SectionCard("Destination");
            string defName = string.IsNullOrEmpty(e.fileName) ? DefaultName() : e.fileName;
            dest.Body.Add(Fields.Text("File Name", defName, v => Set("Export File Name", x => x.fileName = v)));
            var folderRow = Fields.Text("Folder", e.outputFolder, v => Set("Export Folder", x => x.outputFolder = v));
            folderRow.Add(new StudioButton(Icon.Folder, null, () =>
            {
                string abs = EditorUtility.SaveFolderPanel("Export folder", string.IsNullOrEmpty(e.outputFolder) ? "Assets" : e.outputFolder, "");
                if (string.IsNullOrEmpty(abs)) return;
                string data = Application.dataPath.Replace('\\', '/');
                abs = abs.Replace('\\', '/');
                string rel = abs.StartsWith(data) ? "Assets" + abs.Substring(data.Length) : abs;
                Set("Export Folder", x => x.outputFolder = rel);
                Build();
            }, "Browse", "acs-btn--sm"));
            dest.Body.Add(folderRow);
            dest.Body.Add(UIUtil.Label($"→ {ExportService.MakePath(e.outputFolder, defName, e.fileType)}", "acs-hint"));
            _body.Add(dest);

            _exportBtn.Text = $"Export {AudioFormats.Label(e.fileType)}";
            _exportBtn.tooltip = $"Render and write the {AudioFormats.Label(e.fileType)} file";
        }

        /// <summary>Shows which ffmpeg MP3/OGG export will use. The path itself is set in Preferences ▸ Audio Creation Studio.</summary>
        private VisualElement FfmpegRow()
        {
            string found = AudioStudioSettings.ResolveFfmpeg();
            var row = new VisualElement();
            row.AddToClassList("acs-row");
            row.Add(UIUtil.Label(found != null ? $"✓ ffmpeg: {found}" : "⚠ ffmpeg not found (needed for MP3/OGG)", "acs-hint"));
            row.Add(UIUtil.Spacer());
            row.Add(new StudioButton(Icon.Gear, "Settings", AudioStudioSettings.Open, "Set the ffmpeg path in Preferences ▸ Audio Creation Studio", "acs-btn--sm"));
            return row;
        }

        private string DefaultName()
        {
            var s = _ctx.Session;
            string n = _ctx.Data.name;
            switch (E.scope)
            {
                case ExportScope.SelectedTrack: return $"{n}_{s.PrimaryTrack?.name ?? "Track"}";
                case ExportScope.SelectedClip: return $"{n}_{s.PrimaryClip?.DisplayName ?? "Clip"}";
                case ExportScope.LoopRegion: return $"{n}_Loop";
                default: return n;
            }
        }

        public void Export()
        {
            if (_runner.IsRunning) return;
            var e = E;
            try
            {
                string name = string.IsNullOrEmpty(e.fileName) ? DefaultName() : e.fileName;
                if (AudioFormats.IsLossy(e.fileType) && AudioStudioSettings.RequireFfmpeg(AudioFormats.Label(e.fileType)) == null)
                    throw new InvalidOperationException($"{AudioFormats.Label(e.fileType)} export needs ffmpeg, which was not found. Set it in Preferences ▸ Audio Creation Studio.");
                string path = ExportService.MakePath(e.outputFolder, name, e.fileType);
                var opts = ExportOptions.From(e, e.scope == ExportScope.LoopRegion);
                var item = ExportService.CreateItem(_ctx, e.scope, opts, path);
                _results.Clear();
                _runner.Run(new List<ExportItem> { item });
            }
            catch (Exception ex)
            {
                _status.text = ex.Message;
                _status.AddToClassList("acs-sheet__status--error");
            }
        }

        private void UpdateProgress()
        {
            _progressFill.style.width = Length.Percent(_runner.Progress * 100f);
            if (_runner.IsRunning || !string.IsNullOrEmpty(_runner.Status)) _status.text = _runner.Status;
            _status.EnableInClassList("acs-sheet__status--error", _runner.Status.StartsWith("Export failed"));
            _cancelBtn.SetEnabled(_runner.IsRunning);
            _exportBtn.SetEnabled(!_runner.IsRunning);
        }

        private static string FormatBytes(long b) => b >= 1 << 20 ? $"{b / 1048576.0:0.0} MB" : b >= 1024 ? $"{b / 1024.0:0} KB" : $"{b} B";

        private void ShowResults(List<ExportResult> results, Exception ex, bool cancelled)
        {
            _results.Clear();
            foreach (var r in results)
            {
                var row = new VisualElement();
                row.AddToClassList("acs-result");
                row.Add(new IconElement(r.Validated ? Icon.Check : Icon.Warning, 14) { Color = r.Validated ? StudioTheme.Success : StudioTheme.Warning });
                row.Add(UIUtil.Label(System.IO.Path.GetFileName(r.Path), "acs-result__name"));
                row.Add(UIUtil.Label($"{r.DurationSeconds:0.000}s · {r.SampleRate} Hz · {r.Channels}ch · {r.FormatLabel} · {FormatBytes(r.FileBytes)} · peak {r.PeakDb:0.0} dB" +
                    (r.ClippedSamples > 0 ? $" · {r.ClippedSamples} clipped" : "") + (string.IsNullOrEmpty(r.Note) ? "" : $" · {r.Note}"), "acs-label-dim"));
                string path = r.Path;
                row.Add(new StudioButton(Icon.Folder, null, () =>
                {
                    string data = Application.dataPath.Replace('\\', '/');
                    string p = path.Replace('\\', '/');
                    var asset = p.StartsWith(data) ? AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets" + p.Substring(data.Length)) : null;
                    if (asset != null) EditorGUIUtility.PingObject(asset);
                    else EditorUtility.RevealInFinder(p);
                }, "Show file", "acs-btn--xs"));
                _results.Add(row);
            }
        }
    }
}
