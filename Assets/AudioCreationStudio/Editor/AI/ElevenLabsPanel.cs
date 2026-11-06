using System;
using System.Collections.Generic;
using System.IO;
using AudioCreationStudio.DSP;
using AudioCreationStudio.Models;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AudioCreationStudio.EditorUI
{
    /// <summary>
    /// "AI Sound Effects · ElevenLabs" card: text prompt (+ optional reference used for duration and hints)
    /// → ElevenLabs generates real audio → saved as AudioClip assets → preview, insert into the timeline,
    /// loop, and edit with the studio's tools.
    /// </summary>
    public sealed class ElevenLabsPanel : SectionCard
    {
        public const string OutputFolder = "Assets/AudioCreationStudio/Generated/ElevenLabs";
        private const float MaxReferenceSeconds = 30f;

        private sealed class Take
        {
            public string Path;
            public AudioClip Clip;
            public AudioBuffer Buffer;
            public string Prompt;
        }

        private readonly StudioContext _ctx;
        private readonly GeneratorView _gen;
        private readonly TextField _prompt;
        private readonly SwitchToggle _autoDuration, _loop;
        private readonly ParamRow _duration, _influence;
        private readonly PopupField<string> _model;
        private readonly SegmentedControl _takesCount;
        private readonly ObjectField _reference;
        private readonly Label _refInfo, _status;
        private readonly VisualElement _noKey, _takesList, _promptWarning;
        private readonly Label _promptWarningText;
        private readonly StudioButton _generateBtn, _cancelBtn;
        private readonly List<Take> _takes = new List<Take>();
        private readonly Queue<SfxRequest> _queue = new Queue<SfxRequest>();
        private ElevenLabsClient.Handle _pending;
        private AudioBuffer _referenceBuffer;
        private SoundAnalysis _referenceAnalysis;
        private int _batchTotal, _batchDone;

        public ElevenLabsPanel(StudioContext ctx, GeneratorView gen = null) : base("AI Sound Effects  ·  ElevenLabs", true, true, StudioTheme.Accent)
        {
            _ctx = ctx;
            _gen = gen;
            AddToClassList("acs-ai");
            Actions.Add(new StudioButton(Icon.Gear, "Settings", AudioStudioSettings.Open, "ElevenLabs API key and other settings (Preferences ▸ Audio Creation Studio)", "acs-btn--sm"));

            // Missing key notice (the key itself lives in Preferences ▸ Audio Creation Studio)
            _noKey = new VisualElement();
            _noKey.Add(Fields.Warning("No ElevenLabs API key. Add it in Preferences ▸ Audio Creation Studio. Each generation uses ElevenLabs credits from your plan."));
            _noKey.Add(new StudioButton(Icon.Gear, "Open Settings", AudioStudioSettings.Open, "Preferences ▸ Audio Creation Studio", "acs-btn--sm", "acs-btn--soft"));
            Body.Add(_noKey);
            AudioStudioSettings.Changed += UpdateKeyState;
            RegisterCallback<DetachFromPanelEvent>(_ => AudioStudioSettings.Changed -= UpdateKeyState);
            RegisterCallback<AttachToPanelEvent>(_ => { AudioStudioSettings.Changed -= UpdateKeyState; AudioStudioSettings.Changed += UpdateKeyState; UpdateKeyState(); });

            // Prompt
            Body.Add(UIUtil.Label("DESCRIBE THE SOUND (English works best)", "acs-overline"));
            _prompt = new TextField { multiline = true, value = "" };
            _prompt.AddToClassList("acs-field");
            _prompt.AddToClassList("acs-ai__desc");
            Body.Add(_prompt);
            _promptWarning = Fields.Warning("");
            _promptWarningText = _promptWarning.Q<Label>();
            _promptWarning.style.display = DisplayStyle.None;
            Body.Add(_promptWarning);
            _prompt.RegisterValueChangedCallback(e => UpdatePromptWarning(e.newValue));
            Body.Add(UIUtil.Label("e.g. \"medium dog barking twice, close mic, dry\" · \"retro laser shot, descending\" · \"heavy wooden door slam in a hallway\"", "acs-hint"));

            // Options
            var durRow = new VisualElement();
            durRow.AddToClassList("acs-param");
            durRow.Add(UIUtil.Label("Auto Duration", "acs-param__label"));
            _autoDuration = new SwitchToggle(true, "Let ElevenLabs choose the length from the prompt");
            durRow.Add(_autoDuration);
            durRow.Add(UIUtil.Spacer());
            Body.Add(durRow);
            _duration = new ParamRow("Duration", new ValueRange(ElevenLabsClient.MinDuration, ElevenLabsClient.MaxDuration, 2f, true), 2f, v => $"{v:0.0} s");
            _duration.SetEnabled(false);
            _autoDuration.Changed += v => _duration.SetEnabled(!v);
            Body.Add(_duration);
            _influence = new ParamRow("Prompt Influence", new ValueRange(0f, 1f, 0.3f), 0.3f, v => $"{v * 100:0}%",
                "Higher = follows the text more literally, lower = more creative variation (ElevenLabs default 30%)");
            Body.Add(_influence);
            var loopRow = new VisualElement();
            loopRow.AddToClassList("acs-param");
            loopRow.Add(UIUtil.Label("Seamless Loop", "acs-param__label"));
            _loop = new SwitchToggle(false, "Ask ElevenLabs for a sound that loops smoothly (ambiences, engines)");
            loopRow.Add(_loop);
            loopRow.Add(UIUtil.Spacer());
            Body.Add(loopRow);
            var modelRow = new VisualElement();
            modelRow.AddToClassList("acs-param");
            modelRow.Add(UIUtil.Label("Model", "acs-param__label"));
            _model = new PopupField<string>(new List<string>(ElevenLabsClient.Models), 0);
            _model.AddToClassList("acs-field");
            _model.style.flexGrow = 1;
            modelRow.Add(_model);
            Body.Add(modelRow);
            var takesRow = new VisualElement();
            takesRow.AddToClassList("acs-param");
            takesRow.Add(UIUtil.Label("Takes", "acs-param__label"));
            _takesCount = new SegmentedControl(new[] { "1", "2", "3", "4" }, 0);
            _takesCount.tooltip = "How many different versions to generate (each one uses credits)";
            takesRow.Add(_takesCount);
            Body.Add(takesRow);

            // Reference
            var refRow = new VisualElement();
            refRow.AddToClassList("acs-row");
            _reference = new ObjectField { objectType = typeof(AudioClip), allowSceneObjects = false };
            _reference.AddToClassList("acs-objectfield");
            _reference.AddToClassList("acs-grow");
            _reference.tooltip = "Optional reference. ElevenLabs only takes text, so the reference sets the duration and adds a short timing/character hint to the prompt.";
            _reference.RegisterValueChangedCallback(e => LoadReference(e.newValue as AudioClip));
            refRow.Add(UIUtil.Label("Reference", "acs-param__label"));
            refRow.Add(_reference);
            refRow.Add(new StudioButton(Icon.Play, null, PlayReference, "Play the reference", "acs-btn--sm"));
            refRow.Add(new StudioButton(Icon.Check, "Use", UseReference, "Set the duration from the reference and add a timing hint to the prompt", "acs-btn--sm"));
            Body.Add(refRow);
            _refInfo = UIUtil.Label("Optional: drag an AudioClip here to match its length and rhythm.", "acs-hint");
            Body.Add(_refInfo);

            // Generate
            var actions = new VisualElement();
            actions.AddToClassList("acs-row");
            actions.AddToClassList("acs-ai__actions");
            _generateBtn = new StudioButton(Icon.Waveform, "Generate with ElevenLabs", Generate, "Generate real audio from the description", "acs-btn--accent");
            actions.Add(_generateBtn);
            _cancelBtn = new StudioButton(Icon.Stop, "Cancel", Cancel, "Cancel", "acs-btn--sm");
            _cancelBtn.style.display = DisplayStyle.None;
            actions.Add(_cancelBtn);
            _status = UIUtil.Label("", "acs-label-dim");
            actions.Add(_status);
            actions.Add(UIUtil.Spacer());
            actions.Add(new StudioButton(Icon.Folder, null, () =>
            {
                var folder = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(OutputFolder);
                if (folder != null) EditorGUIUtility.PingObject(folder); else _ctx.Toast?.Invoke("Nothing generated yet", false);
            }, "Show the generated files in the Project window", "acs-btn--sm"));
            Body.Add(actions);

            _takesList = new VisualElement();
            Body.Add(_takesList);
            UpdateKeyState();
            LoadExistingTakes();
        }

        // ------------------------------------------------------------------ Prompt check

        /// <summary>
        /// The sound-effects model is trained on English descriptions; non-English text, instructions ("make…")
        /// and onomatopoeia tend to be read aloud as speech. Returns a warning, or null if the prompt looks fine.
        /// </summary>
        public static string CheckPrompt(string prompt)
        {
            var p = (prompt ?? "").Trim();
            if (p.Length == 0) return null;
            int nonAscii = 0, letters = 0;
            foreach (char c in p)
            {
                if (char.IsLetter(c)) letters++;
                if (c > 127 && char.IsLetter(c)) nonAscii++;
            }
            if (nonAscii > 0 && nonAscii * 20 >= letters)
                return "This prompt is not in English. The ElevenLabs sound-effects model often READS non-English text aloud as speech instead of making the sound. " +
                       "Describe the sound in English, e.g. \"a medium-sized dog barking twice, close up, outdoors\".";
            string lower = " " + p.ToLowerInvariant() + " ";
            foreach (var verb in new[] { " make ", " create ", " generate ", " give me ", " i want ", " please " })
                if (lower.StartsWith(verb) || lower.Contains(" please "))
                    return "Describe the sound itself rather than giving an instruction (e.g. \"dog barking twice\" instead of \"make a dog bark\"); instructions are sometimes spoken aloud.";
            if (p.Contains("\"") || System.Text.RegularExpressions.Regex.IsMatch(lower, @"\b(woof|meow|bang|boom|pew)\s+\1\b"))
                return "Quoted words and repeated onomatopoeia (\"woof woof\") may be voiced as speech. Describe the source and action instead (\"dog barking twice\").";
            return null;
        }

        private void UpdatePromptWarning(string prompt)
        {
            var w = CheckPrompt(prompt);
            _promptWarning.style.display = w == null ? DisplayStyle.None : DisplayStyle.Flex;
            if (_promptWarningText != null) _promptWarningText.text = w ?? "";
        }

        // ------------------------------------------------------------------ Key

        private void UpdateKeyState()
        {
            _noKey.style.display = ElevenLabsClient.HasKey ? DisplayStyle.None : DisplayStyle.Flex;
            // Generate stays clickable without a key: clicking it logs a warning that says where to set one.
            _generateBtn.SetEnabled(_pending == null);
            Title.text = ElevenLabsClient.HasKey
                ? $"AI Sound Effects  ·  ElevenLabs  ·  key {ElevenLabsClient.MaskedKey}{(ElevenLabsClient.KeyFromEnvironment ? " (env)" : "")}"
                : "AI Sound Effects  ·  ElevenLabs";
        }

        // ------------------------------------------------------------------ Reference

        private void LoadReference(AudioClip clip)
        {
            _referenceBuffer = null;
            _referenceAnalysis = null;
            if (clip == null) { _refInfo.text = "Optional: drag an AudioClip here to match its length and rhythm."; return; }
            if (!_ctx.Library.TryGetClip(clip, out var buf, out _, out var err)) { _refInfo.text = err; _ctx.Toast?.Invoke(err, true); return; }
            int max = (int)(MaxReferenceSeconds * buf.SampleRate);
            _referenceBuffer = buf.Frames > max ? buf.Slice(0, max) : buf;
            _referenceAnalysis = SoundAnalyzer.Analyze(_referenceBuffer);
            _refInfo.text = $"{_referenceAnalysis.DurationSeconds:0.00} s · {ReferenceHint(_referenceAnalysis)}";
        }

        /// <summary>Plain-language hint about timing and character for the text prompt.</summary>
        public static string ReferenceHint(SoundAnalysis a)
        {
            var parts = new List<string>();
            int hits = a.OnsetTimes.Count;
            if (hits == 1) parts.Add("a single hit");
            else if (hits > 1)
            {
                double span = a.OnsetTimes[hits - 1] - a.OnsetTimes[0];
                parts.Add(hits <= 6 ? $"{hits} separate hits over {span:0.0} seconds" : $"rapid repeated hits ({hits} in {span:0.0} s)");
            }
            parts.Add(a.AttackMs < 15 ? "sharp attack" : a.AttackMs > 200 ? "slow fade-in" : "quick attack");
            if (a.SustainRatio > 0.6f) parts.Add("sustained");
            else if (a.DecayMs < 150) parts.Add("very short and dry");
            bool pitched = a.VoicedRatio > 0.4f && a.AvgFlatness < 0.15f;
            parts.Add(pitched ? (a.MedianPitchHz < 200 ? "low-pitched tonal" : a.MedianPitchHz > 900 ? "high-pitched tonal" : "tonal, mid pitch") : a.AvgFlatness > 0.35f ? "noisy, airy" : "textured");
            parts.Add(a.AvgCentroidHz < 1000 ? "dark, muffled tone" : a.AvgCentroidHz > 4000 ? "bright, crisp tone" : "balanced tone");
            return string.Join(", ", parts);
        }

        private void UseReference()
        {
            if (_referenceAnalysis == null) { _ctx.Toast?.Invoke("Choose a reference AudioClip first", true); return; }
            float d = Mathf.Clamp((float)_referenceAnalysis.DurationSeconds, ElevenLabsClient.MinDuration, ElevenLabsClient.MaxDuration);
            _autoDuration.Value = false;
            _duration.SetEnabled(true);
            _duration.Value = d;
            string hint = ReferenceHint(_referenceAnalysis);
            string p = (_prompt.value ?? "").Trim();
            if (!p.Contains(hint)) _prompt.value = string.IsNullOrEmpty(p) ? hint : $"{p}; {hint}";
            _ctx.Toast?.Invoke($"Duration set to {d:0.0} s and timing hint added", false);
        }

        private void PlayReference()
        {
            if (_referenceBuffer == null) { _ctx.Toast?.Invoke("Choose a reference AudioClip first", true); return; }
            _ctx.Transport.PlayBuffer(_referenceBuffer, false, "Reference");
        }

        private AudioBuffer ToStudio(AudioBuffer b)
        {
            var x = b.SampleRate != _ctx.SampleRate ? Resampler.Resample(b, _ctx.SampleRate) : b;
            return x.Channels == 2 ? x : ChannelConverter.Convert(x, 2);
        }

        // ------------------------------------------------------------------ Generate

        private void Generate()
        {
            if (_pending != null) return;
            if (!AudioStudioSettings.RequireElevenLabsKey())
            {
                _ctx.Toast?.Invoke("No ElevenLabs API key: set it in Preferences ▸ Audio Creation Studio", true);
                return;
            }
            string text = (_prompt.value ?? "").Trim();
            if (text.Length == 0) { _ctx.Toast?.Invoke("Describe the sound first", true); return; }
            var warning = CheckPrompt(text);
            if (warning != null && !EditorUtility.DisplayDialog("Check the description", warning + "\n\nGenerate anyway? (uses credits)", "Generate anyway", "Edit description"))
                return;
            int n = _takesCount.Index + 1;
            _queue.Clear();
            for (int i = 0; i < n; i++)
                _queue.Enqueue(new SfxRequest
                {
                    Text = text,
                    DurationSeconds = _autoDuration.Value ? (float?)null : _duration.Value,
                    PromptInfluence = _influence.Value,
                    Loop = _loop.Value,
                    ModelId = _model.value
                });
            _batchTotal = n;
            _batchDone = 0;
            SendNext();
        }

        private void SendNext()
        {
            if (_queue.Count == 0)
            {
                SetBusy(false);
                return;
            }
            var req = _queue.Dequeue();
            SetBusy(true);
            _pending = ElevenLabsClient.Send(req, resp => OnResponse(req, resp));
            schedule.Execute(() =>
            {
                if (_pending != null) _status.text = $"Generating take {_batchDone + 1}/{_batchTotal}… {_pending.Elapsed:0} s";
            }).Every(500).Until(() => _pending == null);
        }

        private void Cancel()
        {
            _queue.Clear();
            _pending?.Cancel();
        }

        private void SetBusy(bool busy)
        {
            _cancelBtn.style.display = busy ? DisplayStyle.Flex : DisplayStyle.None;
            _generateBtn.SetEnabled(!busy);
        }

        private void OnResponse(SfxRequest req, SfxResponse resp)
        {
            _pending = null;
            if (!resp.Ok)
            {
                _queue.Clear();
                SetBusy(false);
                _status.text = resp.Error;
                if (resp.Error != "Cancelled.") _ctx.Toast?.Invoke(resp.Error, true);
                return;
            }
            try
            {
                string path = ElevenLabsClient.SaveAudio(resp.Audio, resp.Format, OutputFolder, req.Text);
                File.WriteAllText(Path.ChangeExtension(path, ".txt"),
                    $"ElevenLabs sound effect\nprompt: {req.Text}\nmodel: {req.ModelId}\nduration: {(req.DurationSeconds.HasValue ? req.DurationSeconds.Value.ToString("0.0") + " s" : "auto")}\n" +
                    $"prompt_influence: {req.PromptInfluence:0.00}\nloop: {req.Loop}\nformat: {resp.Format}\ngenerated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");
                AssetDatabase.ImportAsset(path);
                AssetDatabase.ImportAsset(Path.ChangeExtension(path, ".txt"));
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                AudioBuffer buf = null;
                if (clip != null) _ctx.Library.TryGetClip(clip, out buf, out _, out _);
                var take = new Take { Path = path, Clip = clip, Buffer = buf, Prompt = req.Text };
                _takes.Insert(0, take);
                _batchDone++;
                RebuildTakes();
                _status.text = $"Take {_batchDone}/{_batchTotal} ready · {resp.Seconds:0} s" + (resp.Format != ElevenLabsClient.PreferredFormat ? $" · {resp.Format}" : "");
                if (_batchDone == 1 && buf != null) _ctx.Transport.PlayBuffer(buf, false, $"ElevenLabs · {Path.GetFileNameWithoutExtension(path)}");
            }
            catch (Exception ex)
            {
                _status.text = "Could not save the audio: " + ex.Message;
                _ctx.Toast?.Invoke(_status.text, true);
            }
            SendNext();
        }

        // ------------------------------------------------------------------ Takes

        /// <summary>Lists takes generated earlier (newest first) so they survive script reloads and editor restarts.</summary>
        private void LoadExistingTakes()
        {
            if (!Directory.Exists(OutputFolder)) return;
            var files = new List<string>();
            foreach (var f in Directory.GetFiles(OutputFolder))
                if (f.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)) files.Add(f.Replace('\\', '/'));
            files.Sort((x, y) => File.GetLastWriteTimeUtc(y).CompareTo(File.GetLastWriteTimeUtc(x)));
            foreach (var f in files)
            {
                if (_takes.Count >= 20) break;
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(f);
                if (clip == null) continue;
                _ctx.Library.TryGetClip(clip, out var buf, out _, out _);
                string txt = Path.ChangeExtension(f, ".txt");
                string prompt = null;
                if (File.Exists(txt))
                    foreach (var line in File.ReadAllLines(txt)) if (line.StartsWith("prompt: ")) prompt = line.Substring(8);
                _takes.Add(new Take { Path = f, Clip = clip, Buffer = buf, Prompt = prompt });
            }
            RebuildTakes();
        }

        private void RebuildTakes()
        {
            _takesList.Clear();
            foreach (var t in _takes)
            {
                var take = t;
                var row = new VisualElement();
                row.AddToClassList("acs-varrow");
                var info = new VisualElement();
                info.AddToClassList("acs-varrow__info");
                info.Add(UIUtil.Label(Path.GetFileNameWithoutExtension(t.Path), "acs-varrow__name"));
                info.Add(UIUtil.Label(t.Buffer != null ? $"{t.Buffer.DurationSeconds:0.00} s · {t.Buffer.SampleRate} Hz · {(t.Buffer.Channels == 1 ? "mono" : "stereo")}" : "not readable", "acs-label-dim"));
                row.Add(info);
                var wave = new BufferWaveformView(44);
                wave.AddToClassList("acs-varrow__wave");
                if (t.Buffer != null) wave.SetBuffer(t.Buffer);
                row.Add(wave);
                row.Add(new StudioButton(Icon.Play, null, () => { if (take.Buffer != null) _ctx.Transport.PlayBuffer(take.Buffer, false, Path.GetFileNameWithoutExtension(take.Path)); }, "Play", "acs-btn--sm"));
                row.Add(new StudioButton(Icon.Layers, "A/B", () => PlayAB(take), "Play the reference, then this take", "acs-btn--sm"));
                if (_gen != null)
                    row.Add(new StudioButton(Icon.Down, "Edit in Generator", () => EditInGenerator(take),
                        "Load this take into the generator below as a Sample source: tweak pitch, envelope, filter, noise, tremolo, drive, or Randomize to make variants", "acs-btn--sm", "acs-btn--accent"));
                row.Add(new StudioButton(Icon.Plus, "Insert", () => Insert(take), "Add to the timeline on a new track at the playhead", "acs-btn--sm", "acs-btn--soft"));
                row.Add(new StudioButton(Icon.Loop, null, () => OpenInLoop(take), "Open in the Loop editor", "acs-btn--sm"));
                row.Add(new StudioButton(Icon.Folder, null, () => EditorGUIUtility.PingObject(take.Clip), "Show the asset", "acs-btn--sm"));
                row.Add(new StudioButton(Icon.Trash, null, () => Delete(take), "Delete this take (file and asset)", "acs-btn--sm"));
                _takesList.Add(row);
            }
        }

        private void EditInGenerator(Take t)
        {
            if (t.Clip == null) { _ctx.Toast?.Invoke("This take could not be loaded", true); return; }
            _gen.SetSample(t.Clip, false);
            _ctx.Toast?.Invoke("Loaded into the generator below (Oscillator ▸ Sample). Tweak the sliders or Randomize for variants; Cmd/Ctrl+Z undoes.", false);
        }

        private void PlayAB(Take t)
        {
            if (_referenceBuffer == null || t.Buffer == null) { _ctx.Toast?.Invoke("Choose a reference AudioClip first", true); return; }
            var a = ToStudio(_referenceBuffer);
            var b = ToStudio(t.Buffer);
            int gap = _ctx.SampleRate / 2;
            var ab = new AudioBuffer(2, a.Frames + gap + b.Frames, _ctx.SampleRate);
            ab.AddFrom(a, 0, 0, a.Frames);
            ab.AddFrom(b, 0, a.Frames + gap, b.Frames);
            _ctx.Transport.PlayBuffer(ab, false, "A/B · Reference → ElevenLabs");
        }

        private void Insert(Take t)
        {
            if (t.Clip == null) return;
            int n = _ctx.Commands.ImportClips(new[] { t.Clip }, _ctx.Session.Playhead, _ctx.Data.tracks.Count);
            if (n > 0) _ctx.Toast?.Invoke("Inserted on a new track. Edit it in the Arrange tab (trim, fades, rate, effects).", false);
        }

        private void OpenInLoop(Take t)
        {
            if (t.Clip == null) return;
            _ctx.Session.Modify("Loop Source", d => { d.loop.source = LoopSourceKind.AudioClipAsset; d.loop.assetClip = t.Clip; d.loop.loopEnd = 0; });
            _ctx.ShowTab?.Invoke("Loop");
        }

        private void Delete(Take t)
        {
            if (!EditorUtility.DisplayDialog("Delete take", $"Delete '{Path.GetFileName(t.Path)}' from the project?", "Delete", "Cancel")) return;
            AssetDatabase.DeleteAsset(t.Path);
            AssetDatabase.DeleteAsset(Path.ChangeExtension(t.Path, ".txt"));
            _takes.Remove(t);
            RebuildTakes();
        }
    }
}
