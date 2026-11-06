using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AudioCreationStudio.DSP;
using AudioCreationStudio.Export;
using AudioCreationStudio.Models;
using UnityEditor;
using UnityEngine;

namespace AudioCreationStudio.EditorUI
{
    /// <summary>One file to write: either a ready buffer or a render job executed on the worker thread.</summary>
    public sealed class ExportItem
    {
        public string Path;
        public AudioBuffer Buffer;
        public RenderJob Job;
        public ExportOptions Options;
        /// <summary>Optional region (timeline samples) cut from the rendered mix.</summary>
        public long RegionStart = -1, RegionEnd = -1;
        public string Label;
    }

    /// <summary>
    /// Runs exports on a worker thread with progress and cancellation; imports results on the main thread.
    /// Rendering uses the same <see cref="MixRenderer"/> as preview, so exports match what you hear.
    /// </summary>
    public sealed class ExportRunner
    {
        private readonly ConcurrentQueue<Action> _main = new ConcurrentQueue<Action>();
        private CancellationTokenSource _cts;
        public bool IsRunning { get; private set; }
        public float Progress { get; private set; }
        public string Status { get; private set; } = "";
        public List<ExportResult> LastResults { get; private set; } = new List<ExportResult>();
        public event Action<List<ExportResult>, Exception, bool> Completed;

        public void Run(IList<ExportItem> items)
        {
            if (IsRunning || items == null || items.Count == 0) return;
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            IsRunning = true;
            Progress = 0;
            Status = "Starting…";
            var list = new List<ExportItem>(items);
            foreach (var it in list) ExportService.ApplyEditorPrefs(it.Options);
            var renderer = new MixRenderer();
            Task.Run(() =>
            {
                var results = new List<ExportResult>();
                try
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        token.ThrowIfCancellationRequested();
                        var it = list[i];
                        int idx = i;
                        _main.Enqueue(() => Status = $"Rendering {it.Label ?? System.IO.Path.GetFileName(it.Path)} ({idx + 1}/{list.Count})");
                        var buffer = it.Buffer;
                        if (buffer == null && it.Job != null) buffer = it.Job.Execute(renderer, token).Master;
                        if (buffer == null) throw new InvalidOperationException("Nothing to export.");
                        if (it.RegionStart >= 0 && it.RegionEnd > it.RegionStart)
                            buffer = buffer.Slice((int)Math.Min(it.RegionStart, buffer.Frames), (int)(it.RegionEnd - it.RegionStart));
                        if (buffer.Frames == 0) throw new InvalidOperationException("The selected scope contains no audio.");
                        var prog = new ProgressRelay(v => Progress = (idx + v) / list.Count, _main);
                        results.Add(ExportPipeline.ExportToFile(buffer, it.Options, System.IO.Path.GetFullPath(it.Path), token, prog));
                    }
                    _main.Enqueue(() => Finish(results, null, false));
                }
                catch (OperationCanceledException) { _main.Enqueue(() => Finish(results, null, true)); }
                catch (Exception ex) { _main.Enqueue(() => Finish(results, ex, false)); }
            }, token);
        }

        private sealed class ProgressRelay : IProgress<float>
        {
            private readonly Action<float> _set;
            private readonly ConcurrentQueue<Action> _q;
            public ProgressRelay(Action<float> set, ConcurrentQueue<Action> q) { _set = set; _q = q; }
            public void Report(float v) => _q.Enqueue(() => _set(v));
        }

        private void Finish(List<ExportResult> results, Exception ex, bool cancelled)
        {
            IsRunning = false;
            Progress = cancelled || ex != null ? 0 : 1;
            LastResults = results;
            foreach (var r in results) ExportService.ImportIfInProject(r.Path);
            Status = cancelled ? $"Export cancelled ({results.Count} file(s) written before cancel)"
                : ex != null ? $"Export failed: {ex.Message}"
                : results.Count == 1 ? $"Exported {System.IO.Path.GetFileName(results[0].Path)} · {results[0].DurationSeconds:0.000}s · peak {results[0].PeakDb:0.0} dBFS"
                : $"Exported {results.Count} files";
            if (ex != null) Debug.LogException(ex);
            Completed?.Invoke(results, ex, cancelled);
        }

        public void Cancel() => _cts?.Cancel();

        public void Tick()
        {
            while (_main.TryDequeue(out var a)) a();
        }
    }

    public static class ExportService
    {
        public const string DefaultFolder = "Assets/AudioCreationStudio/Exports";

        private static readonly string[] KnownExtensions = { ".wav", ".aif", ".aiff", ".flac", ".mp3", ".ogg" };
        /// <summary>
        /// Fills machine-specific options (ffmpeg location from <see cref="AudioStudioSettings"/>) that do not belong in the
        /// recipe asset. Logs a warning when MP3/OGG is requested but ffmpeg is missing.
        /// </summary>
        public static void ApplyEditorPrefs(ExportOptions o)
        {
            if (o == null || !string.IsNullOrEmpty(o.FfmpegPath)) return;
            o.FfmpegPath = AudioFormats.IsLossy(o.FileType) ? AudioStudioSettings.RequireFfmpeg(AudioFormats.Label(o.FileType)) : AudioStudioSettings.ResolveFfmpeg();
        }

        public static string MakePath(string folder, string fileName, AudioFileType type = AudioFileType.Wav)
        {
            if (string.IsNullOrWhiteSpace(folder)) folder = DefaultFolder;
            if (string.IsNullOrWhiteSpace(fileName)) fileName = "Sound";
            fileName = PresetStore.MakeSafe(fileName);
            foreach (var ext in KnownExtensions)
                if (fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) { fileName = fileName.Substring(0, fileName.Length - ext.Length); break; }
            fileName += AudioFormats.Extension(type);
            return System.IO.Path.Combine(folder, fileName).Replace('\\', '/');
        }

        public static void ImportIfInProject(string fullPath)
        {
            string data = Application.dataPath.Replace('\\', '/');
            string p = fullPath.Replace('\\', '/');
            if (!p.StartsWith(data)) return;
            string rel = "Assets" + p.Substring(data.Length);
            AssetDatabase.ImportAsset(rel, ImportAssetOptions.ForceUpdate);
        }

        /// <summary>Creates a render job for the requested scope (main thread).</summary>
        public static ExportItem CreateItem(StudioContext ctx, ExportScope scope, ExportOptions options, string path)
        {
            var s = ctx.Session;
            var data = s.Data;
            bool tail = data.export.includeTail;
            var item = new ExportItem { Path = path, Options = options };
            switch (scope)
            {
                case ExportScope.SelectedTrack:
                {
                    var t = s.PrimaryTrack ?? throw new InvalidOperationException("Select a track to export it.");
                    item.Job = RenderRequestFactory.Create(data, ctx.Library, RenderScope.Track, t.uid, null, tail);
                    item.Label = t.name;
                    break;
                }
                case ExportScope.SelectedClip:
                {
                    var c = s.PrimaryClip ?? throw new InvalidOperationException("Select a clip to export it.");
                    item.Job = RenderRequestFactory.Create(data, ctx.Library, RenderScope.Clip, null, c.uid, tail);
                    // Export only the clip's span (plus tail), not the silence before it.
                    item.RegionStart = c.startSample;
                    item.RegionEnd = long.MaxValue / 4;
                    item.Label = c.DisplayName;
                    break;
                }
                case ExportScope.LoopRegion:
                {
                    if (!data.loopRegion.enabled || data.loopRegion.Length <= 0) throw new InvalidOperationException("Set and enable a loop region first.");
                    item.Job = RenderRequestFactory.Create(data, ctx.Library, RenderScope.FullMix, null, null, true);
                    item.RegionStart = data.loopRegion.start;
                    item.RegionEnd = data.loopRegion.end;
                    item.Label = "Loop Region";
                    break;
                }
                default:
                    item.Job = RenderRequestFactory.Create(data, ctx.Library, RenderScope.FullMix, null, null, tail);
                    item.Label = data.name;
                    break;
            }
            return item;
        }

        /// <summary>Synchronous single-buffer export with a cancellable progress bar (used by generator/loop tools).</summary>
        public static ExportResult ExportBufferInteractive(AudioBuffer buffer, ExportOptions options, string defaultName, string folder = DefaultFolder)
        {
            Directory.CreateDirectory(folder);
            ApplyEditorPrefs(options);
            if (AudioFormats.IsLossy(options.FileType) && string.IsNullOrEmpty(options.FfmpegPath))
            {
                if (EditorUtility.DisplayDialog("Audio Creation Studio",
                        $"{AudioFormats.Label(options.FileType)} export needs ffmpeg, which was not found.\n\n" +
                        "Install it (macOS: brew install ffmpeg · Windows: winget install ffmpeg) or set its path in Preferences ▸ Audio Creation Studio.",
                        "Open Settings", "Cancel"))
                    AudioStudioSettings.Open();
                return null;
            }
            string ext = AudioFormats.Extension(options.FileType).TrimStart('.');
            string path = EditorUtility.SaveFilePanel($"Export {AudioFormats.Label(options.FileType)}", folder, PresetStore.MakeSafe(defaultName), ext);
            if (string.IsNullOrEmpty(path)) return null;
            var cts = new CancellationTokenSource();
            try
            {
                var progress = new BarProgress(cts, Path.GetFileName(path));
                var result = ExportPipeline.ExportToFile(buffer, options, path, cts.Token, progress);
                ImportIfInProject(path);
                return result;
            }
            catch (OperationCanceledException) { return null; }
            finally { EditorUtility.ClearProgressBar(); }
        }

        private sealed class BarProgress : IProgress<float>
        {
            private readonly CancellationTokenSource _cts;
            private readonly string _name;
            public BarProgress(CancellationTokenSource cts, string name) { _cts = cts; _name = name; }
            public void Report(float v)
            {
                if (EditorUtility.DisplayCancelableProgressBar("Audio Creation Studio", $"Exporting {_name}", v)) _cts.Cancel();
            }
        }
    }
}
