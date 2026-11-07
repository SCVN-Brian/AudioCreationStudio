using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AudioCreationStudio.DSP;
using AudioCreationStudio.Export;
using AudioCreationStudio.Models;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AudioCreationStudio.EditorUI
{
    /// <summary>
    /// Generates deterministic variants of the current recipe (pitch, volume, start offset, timing,
    /// layer selection, effect parameters), previews and compares them with the original, saves them as
    /// recipes and batch-exports them as baked WAV files.
    /// </summary>
    public sealed class VariationsView : VisualElement
    {
        private sealed class Variant
        {
            public RecipeData Recipe;
            public VariationInfo Info;
            public AudioBuffer Audio;
            public WaveformPeaks Peaks;
        }

        private readonly StudioContext _ctx;
        private readonly ExportRunner _runner;
        private readonly VisualElement _settings;
        private readonly ScrollView _list;
        private readonly Label _status;
        private readonly List<Variant> _variants = new List<Variant>();
        private AudioBuffer _original;
        private CancellationTokenSource _cts;
        private readonly System.Collections.Concurrent.ConcurrentQueue<Action> _main = new System.Collections.Concurrent.ConcurrentQueue<Action>();

        private VariationSettings V => _ctx.Data.variations;

        public VariationsView(StudioContext ctx, ExportRunner runner)
        {
            _ctx = ctx;
            _runner = runner;
            AddToClassList("acs-var");
            var left = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            left.AddToClassList("acs-gen__params");
            left.AddToClassList("acs-var__settings");
            _settings = new VisualElement();
            left.Add(_settings);
            Add(left);

            var main = new VisualElement();
            main.AddToClassList("acs-loop__main");
            var head = new VisualElement();
            head.AddToClassList("acs-gen__head");
            head.Add(UIUtil.Label("Variations", "acs-gen__name"));
            head.Add(UIUtil.Spacer());
            _status = UIUtil.Label("", "acs-label-dim");
            head.Add(_status);
            main.Add(head);
            var actions = new VisualElement();
            actions.AddToClassList("acs-gen__actions");
            actions.Add(new StudioButton(Icon.Layers, "Generate Variations", Generate, "Render all variations (deterministic for the same seed)", "acs-btn--accent"));
            actions.Add(new StudioButton(Icon.Play, "Play Original", () => { if (_original != null) _ctx.Transport.PlayBuffer(_original, false, "Original"); }, "Play the unvaried recipe for comparison"));
            actions.Add(new StudioButton(Icon.Skip, "Play All in Sequence", PlaySequence, "Original, then every variation with short gaps"));
            actions.Add(UIUtil.Spacer());
            actions.Add(new StudioButton(Icon.Export, "Batch Export", BatchExport, "Export every variation as a baked audio file in the format chosen in Export settings", "acs-btn--soft"));
            main.Add(actions);
            _list = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            _list.AddToClassList("acs-var__list");
            main.Add(_list);
            Add(main);
            schedule.Execute(() => { while (_main.TryDequeue(out var a)) a(); }).Every(30);
            BuildSettings();
            ShowEmpty();
        }

        private void ShowEmpty()
        {
            _list.Clear();
            _list.Add(UIUtil.EmptyState(Icon.Layers, "No variations yet", "Set the ranges on the left and press Generate. The same seed always produces the same variations."));
        }

        public void Refresh() => BuildSettings();

        private void BuildSettings()
        {
            _settings.Clear();
            var v = V;
            var card = new SectionCard("Variation Ranges");
            card.Body.Add(Fields.Param(_ctx, "Count", new ValueRange(1, 32, 5), v.count, x => $"{Mathf.RoundToInt(x)}", "Variation Count", (d, x) => d.variations.count = Mathf.RoundToInt(x), this));
            var seedRow = new VisualElement();
            seedRow.AddToClassList("acs-param");
            seedRow.Add(UIUtil.Label("Seed", "acs-param__label"));
            var seed = new IntegerField { value = v.seed, isDelayed = true };
            seed.AddToClassList("acs-field");
            seed.style.flexGrow = 1;
            seed.RegisterValueChangedCallback(e => _ctx.Session.Modify("Variation Seed", d => d.variations.seed = e.newValue, StudioChange.Content, this));
            seedRow.Add(seed);
            seedRow.Add(new StudioButton(Icon.Dice, null, () => { int s = new System.Random().Next(1, 999999); seed.SetValueWithoutNotify(s); _ctx.Session.Modify("Variation Seed", d => d.variations.seed = s, StudioChange.Content, this); }, "New seed", "acs-btn--sm"));
            card.Body.Add(seedRow);
            card.Body.Add(Fields.Param(_ctx, "Pitch ±", new ValueRange(0f, 12f, 1f), v.pitchSemitones, x => $"{x:0.0} st", "Variation Pitch", (d, x) => d.variations.pitchSemitones = x, this));
            card.Body.Add(Fields.Param(_ctx, "Volume ±", new ValueRange(0f, 12f, 1.5f), v.volumeDb, x => $"{x:0.0} dB", "Variation Volume", (d, x) => d.variations.volumeDb = x, this));
            card.Body.Add(Fields.Param(_ctx, "Start Offset", new ValueRange(0f, 200f, 0f), v.startOffsetMs, x => $"0–{x:0} ms", "Variation Offset", (d, x) => d.variations.startOffsetMs = x, this, "Random extra offset into each AudioClip source"));
            card.Body.Add(Fields.Param(_ctx, "Timing ±", new ValueRange(0f, 100f, 8f), v.timingJitterMs, x => $"{x:0} ms", "Variation Timing", (d, x) => d.variations.timingJitterMs = x, this, "Random shift of clip start times (clips at 0 stay anchored)"));
            card.Body.Add(Fields.Param(_ctx, "Layer Drop", new ValueRange(0f, 0.9f, 0f), v.layerDropProbability, x => $"{x * 100:0}%", "Variation Layers", (d, x) => d.variations.layerDropProbability = x, this, "Probability that each layer (track) is left out; at least one layer is always kept"));
            card.Body.Add(Fields.Param(_ctx, "Effect Params", new ValueRange(0f, 0.5f, 0.1f), v.effectVariation, x => $"±{x * 50:0}% range", "Variation Effects", (d, x) => d.variations.effectVariation = x, this));
            _settings.Add(card);
            _settings.Add(UIUtil.Label("Pitch is a varispeed change (duration scales with pitch). Variations are baked from the full DSP pipeline, so effects and automation are included.", "acs-hint"));
        }

        private void Generate()
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            var baseData = _ctx.Data;
            var settings = V.Clone();
            _variants.Clear();
            var jobs = new List<(Variant v, RenderJob job)>();
            var originalJob = RenderRequestFactory.Create(baseData, _ctx.Library);
            for (int i = 0; i < settings.count; i++)
            {
                var r = VariationGenerator.CreateVariant(baseData, settings, i, out var info);
                var job = RenderRequestFactory.Create(r, _ctx.Library);
                var variant = new Variant { Recipe = r, Info = info };
                _variants.Add(variant);
                jobs.Add((variant, job));
            }
            _status.text = $"Rendering {settings.count} variations…";
            RebuildList();
            Task.Run(() =>
            {
                try
                {
                    var renderer = new MixRenderer();
                    var orig = originalJob.Execute(renderer, token).Master;
                    _main.Enqueue(() => _original = orig);
                    int done = 0;
                    foreach (var (v, job) in jobs)
                    {
                        token.ThrowIfCancellationRequested();
                        var buf = job.Execute(renderer, token).Master;
                        var peaks = WaveformPeaks.Build(buf, token);
                        done++;
                        int d = done;
                        _main.Enqueue(() => { v.Audio = buf; v.Peaks = peaks; _status.text = $"{d}/{jobs.Count} rendered"; RebuildList(); });
                    }
                    _main.Enqueue(() => _status.text = $"{jobs.Count} variations · seed {settings.seed}");
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { _main.Enqueue(() => { _status.text = ex.Message; Debug.LogException(ex); }); }
            }, token);
        }

        private void RebuildList()
        {
            _list.Clear();
            if (_variants.Count == 0) { ShowEmpty(); return; }
            foreach (var v in _variants)
            {
                var variant = v;
                var row = new VisualElement();
                row.AddToClassList("acs-varrow");
                var idx = UIUtil.Label($"{v.Info.Index + 1:00}", "acs-varrow__index");
                row.Add(idx);
                var info = new VisualElement();
                info.AddToClassList("acs-varrow__info");
                info.Add(UIUtil.Label(v.Recipe.name, "acs-varrow__name"));
                info.Add(UIUtil.Label(v.Info.Summary + $" · seed {v.Info.Seed}", "acs-label-dim"));
                row.Add(info);
                var wave = new BufferWaveformView(44);
                wave.AddToClassList("acs-varrow__wave");
                if (v.Peaks != null) wave.SetPeaks(v.Peaks);
                row.Add(wave);
                var play = new StudioButton(Icon.Play, null, () => { if (variant.Audio != null) _ctx.Transport.PlayBuffer(variant.Audio, false, variant.Recipe.name); }, "Preview this variation", "acs-btn--sm");
                play.SetEnabled(v.Audio != null);
                row.Add(play);
                row.Add(new StudioButton(Icon.Layers, "A/B", () => Compare(variant), "Play the original, then this variation", "acs-btn--sm"));
                row.Add(new StudioButton(Icon.Save, null, () => SaveRecipe(variant), "Save this variation as a new recipe asset", "acs-btn--sm"));
                var exp = new StudioButton(Icon.Export, null, () => ExportOne(variant), "Export this variation as WAV", "acs-btn--sm");
                exp.SetEnabled(v.Audio != null);
                row.Add(exp);
                _list.Add(row);
            }
        }

        private static AudioBuffer Concat(IList<AudioBuffer> parts, int gapFrames)
        {
            int ch = 1, sr = 48000, total = 0;
            foreach (var p in parts) { ch = Math.Max(ch, p.Channels); sr = p.SampleRate; total += p.Frames + gapFrames; }
            var dst = new AudioBuffer(ch, total, sr);
            int pos = 0;
            foreach (var p in parts)
            {
                var src = p.Channels == ch ? p : ChannelConverter.Convert(p, ch);
                dst.AddFrom(src, 0, pos, src.Frames);
                pos += p.Frames + gapFrames;
            }
            return dst;
        }

        private void Compare(Variant v)
        {
            if (_original == null || v.Audio == null) return;
            _ctx.Transport.PlayBuffer(Concat(new[] { _original, v.Audio }, _original.SampleRate / 3), false, $"A/B · Original → {v.Recipe.name}");
        }

        private void PlaySequence()
        {
            var parts = new List<AudioBuffer>();
            if (_original != null) parts.Add(_original);
            foreach (var v in _variants) if (v.Audio != null) parts.Add(v.Audio);
            if (parts.Count == 0) return;
            _ctx.Transport.PlayBuffer(Concat(parts, parts[0].SampleRate / 4), false, "Original + Variations");
        }

        private void SaveRecipe(Variant v)
        {
            string dir = _ctx.Session.Asset != null ? System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(_ctx.Session.Asset)) : "Assets/AudioCreationStudio";
            System.IO.Directory.CreateDirectory(dir);
            string path = AssetDatabase.GenerateUniqueAssetPath($"{dir}/{PresetStore.MakeSafe(v.Recipe.name)}.asset");
            var a = ScriptableObject.CreateInstance<AudioRecipe>();
            a.data = v.Recipe.Clone();
            AssetDatabase.CreateAsset(a, path);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(a);
            _ctx.Toast?.Invoke($"Saved {System.IO.Path.GetFileName(path)}", false);
        }

        private void ExportOne(Variant v)
        {
            if (v.Audio == null) return;
            var r = ExportService.ExportBufferInteractive(v.Audio, ExportOptions.From(_ctx.Data.export), v.Recipe.name, _ctx.Data.export.outputFolder);
            if (r != null) _ctx.Toast?.Invoke($"Exported {System.IO.Path.GetFileName(r.Path)}", false);
        }

        private void BatchExport()
        {
            if (_variants.Count == 0) { _ctx.Toast?.Invoke("Generate variations first", true); return; }
            string folder = EditorUtility.SaveFolderPanel("Batch export variations", _ctx.Data.export.outputFolder, "");
            if (string.IsNullOrEmpty(folder)) return;
            var items = new List<ExportItem>();
            var opts = ExportOptions.From(_ctx.Data.export);
            foreach (var v in _variants)
            {
                var item = new ExportItem { Path = ExportService.MakePath(folder, v.Recipe.name, opts.FileType), Options = opts, Label = v.Recipe.name };
                if (v.Audio != null) item.Buffer = v.Audio; else item.Job = RenderRequestFactory.Create(v.Recipe, _ctx.Library);
                items.Add(item);
            }
            _runner.Run(items);
            _ctx.OpenExport?.Invoke("progress");
        }
    }
}
