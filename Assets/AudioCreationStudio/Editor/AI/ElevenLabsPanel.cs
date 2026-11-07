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

        private readonly StudioContext _ctx;
        private readonly TextField _prompt;
        private readonly SwitchToggle _autoDuration, _loop;
        private readonly ParamRow _duration, _influence;
        private readonly PopupField<string> _model;
        private readonly SegmentedControl _takesCount;
        private readonly ObjectField _reference;
        private readonly Label _refInfo, _status;
        private readonly VisualElement _noKey, _promptWarning;
        private readonly TakeBrowser _browser;

        /// <summary>The take library; the host view decides where it goes.</summary>
        public TakeBrowser Browser => _browser;
        private readonly Label _promptWarningText;
        private readonly StudioButton _generateBtn, _cancelBtn;
        private readonly Queue<SfxRequest> _queue = new Queue<SfxRequest>();
        private ElevenLabsClient.Handle _pending;
        private AudioBuffer _referenceBuffer;
        private SoundAnalysis _referenceAnalysis;
        private int _batchTotal, _batchDone;

        // Step 1: AI sound analyzer (Gemini / OpenAI) → step 2: parameters → step 3: ElevenLabs prompt
        private readonly TextField _idea;
        private readonly StudioButton _writeBtn, _writeGenBtn, _writeCancelBtn, _rebuildBtn;
        private readonly Label _writerStatus, _writerExplain, _writerWarn, _noWriterKeyText, _promptSource;
        private readonly VisualElement _writerResult, _noOpenAI;
        private readonly SoundSpecEditor _spec;
        private PromptWriter.Handle _writing;
        private string _gptIdea, _gptSource;
        /// <summary>The prompt last built from the parameters (the prompt field follows them until edited by hand).</summary>
        private string _promptFromSpec = "";

        public ElevenLabsPanel(StudioContext ctx) : base("AI Sound Effects  ·  ElevenLabs", false, true, StudioTheme.Accent)
        {
            _ctx = ctx;
            AddToClassList("acs-ai");
            Actions.Add(new StudioButton(Icon.Gear, null, AudioStudioSettings.Open, "ElevenLabs API key and other settings (Preferences ▸ Audio Creation Studio)", "acs-btn--sm"));

            // Missing key notice (the key itself lives in Preferences ▸ Audio Creation Studio)
            _noKey = new VisualElement();
            _noKey.Add(Fields.Warning("No ElevenLabs API key. Add it in Preferences ▸ Audio Creation Studio. Each generation uses ElevenLabs credits from your plan."));
            _noKey.Add(new StudioButton(Icon.Gear, "Open Settings", AudioStudioSettings.Open, "Preferences ▸ Audio Creation Studio", "acs-btn--sm", "acs-btn--soft"));
            Body.Add(_noKey);
            AudioStudioSettings.Changed += UpdateKeyState;
            RegisterCallback<DetachFromPanelEvent>(_ => AudioStudioSettings.Changed -= UpdateKeyState);
            RegisterCallback<AttachToPanelEvent>(_ => { AudioStudioSettings.Changed -= UpdateKeyState; AudioStudioSettings.Changed += UpdateKeyState; UpdateKeyState(); });

            // Step 1: describe in any language → AI analyses it into structured parameters
            Body.Add(UIUtil.Label("1 · DESCRIBE THE SOUND (Vietnamese or English)", "acs-overline"));
            _idea = new TextField { multiline = true, value = "" };
            _idea.AddToClassList("acs-field");
            _idea.AddToClassList("acs-ai__desc");
            _idea.tooltip = "Describe the sound in your own words, e.g. \"tiếng người ùng ục dưới nước 3 lần, kiểu cartoon\". AI analyses it into the parameters below.";
            Body.Add(_idea);
            Body.Add(UIUtil.Label("e.g. \"tiếng cửa gỗ đóng sầm trong hành lang\" · \"chó con sủa 2 lần, hơi xa, ngoài trời\" · \"tiếng mưa đều để loop làm nền\"", "acs-hint"));
            _noOpenAI = new VisualElement();
            _noOpenAI.AddToClassList("acs-row");
            _noWriterKeyText = UIUtil.Label("", "acs-hint", "acs-grow");
            _noOpenAI.Add(_noWriterKeyText);
            _noOpenAI.Add(new StudioButton(Icon.Gear, "Add Key", AudioStudioSettings.Open, "Add the key in Preferences ▸ Audio Creation Studio", "acs-btn--sm", "acs-btn--soft"));
            Body.Add(_noOpenAI);
            var writeRow = new VisualElement();
            writeRow.AddToClassList("acs-row");
            writeRow.AddToClassList("acs-ai__actions");
            _writeBtn = new StudioButton(Icon.Sparkle, "Analyze Sound", () => Analyze(false),
                "AI analyses the description into source, action, texture, pitch, rhythm, intensity, style, duration/structure and constraints. Review them below.", "acs-btn--soft");
            writeRow.Add(_writeBtn);
            _writeGenBtn = new StudioButton(Icon.Waveform, "Analyze & Generate", () => Analyze(true),
                "Analyse, build the prompt from the AI's parameters and generate with ElevenLabs right away (ElevenLabs credits)", "acs-btn--sm");
            writeRow.Add(_writeGenBtn);
            _writeCancelBtn = new StudioButton(Icon.Stop, null, () => _writing?.Cancel(), "Cancel", "acs-btn--sm");
            _writeCancelBtn.style.display = DisplayStyle.None;
            writeRow.Add(_writeCancelBtn);
            _writerStatus = UIUtil.Label("", "acs-label-dim");
            writeRow.Add(_writerStatus);
            Body.Add(writeRow);
            _writerResult = new VisualElement();
            _writerResult.AddToClassList("acs-ai__result");
            _writerExplain = UIUtil.Label("", "acs-ai__explanation");
            _writerResult.Add(_writerExplain);
            _writerWarn = UIUtil.Label("", "acs-ai__explanation", "acs-ai__writerwarn");
            _writerResult.Add(_writerWarn);
            _writerResult.style.display = DisplayStyle.None;
            Body.Add(_writerResult);

            // Step 2: the nine sound parameters — review and override
            Body.Add(UIUtil.Label("2 · SOUND PARAMETERS (review & edit)", "acs-overline", "acs-ai__step2"));
            _spec = new SoundSpecEditor();
            _spec.Changed += OnSpecChanged;
            Body.Add(_spec);
            var durRow = new VisualElement();
            durRow.AddToClassList("acs-param");
            durRow.Add(UIUtil.Label("Auto Duration", "acs-param__label"));
            _autoDuration = new SwitchToggle(true, "Let ElevenLabs choose the length from the prompt");
            durRow.Add(_autoDuration);
            durRow.Add(UIUtil.Spacer());
            _spec.DurationSlot.Add(durRow);
            _duration = new ParamRow("Duration", new ValueRange(ElevenLabsClient.MinDuration, ElevenLabsClient.MaxDuration, 2f, true), 2f, v => $"{v:0.0} s");
            _duration.SetEnabled(false);
            _autoDuration.Changed += v => _duration.SetEnabled(!v);
            _spec.DurationSlot.Add(_duration);

            // Step 3: the prompt ElevenLabs receives, built from the parameters (still editable)
            Body.Add(UIUtil.Label("3 · ELEVENLABS PROMPT (built from the parameters)", "acs-overline", "acs-ai__step2"));
            _prompt = new TextField { multiline = true, value = "" };
            _prompt.AddToClassList("acs-field");
            _prompt.AddToClassList("acs-ai__desc");
            _prompt.AddToClassList("acs-ai__prompt");
            Body.Add(_prompt);
            var srcRow = new VisualElement();
            srcRow.AddToClassList("acs-row");
            _promptSource = UIUtil.Label("", "acs-hint", "acs-grow");
            srcRow.Add(_promptSource);
            _rebuildBtn = new StudioButton(Icon.Undo, "Rebuild from Parameters", RebuildPrompt, "Discard manual edits and rebuild the prompt from the parameters above", "acs-btn--sm");
            srcRow.Add(_rebuildBtn);
            Body.Add(srcRow);
            _promptWarning = Fields.Warning("");
            _promptWarningText = _promptWarning.Q<Label>();
            _promptWarning.style.display = DisplayStyle.None;
            Body.Add(_promptWarning);
            _prompt.RegisterValueChangedCallback(e => { UpdatePromptWarning(e.newValue); UpdatePromptSource(); });
            UpdatePromptSource();

            // Options
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
                var folder = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(_browser.SaveFolder);
                if (folder != null) EditorGUIUtility.PingObject(folder); else _ctx.Toast?.Invoke("Nothing generated yet", false);
            }, "Show the selected take folder in the Project window", "acs-btn--sm"));
            Body.Add(actions);

            // Take library: folders + takes; new generations land in the selected folder.
            _browser = new TakeBrowser(ctx, OutputFolder, "ElevenLabs")
            {
                BuildActions = TakeActions,
                Play = take => { if (take.Buffer != null) _ctx.Transport.PlayBuffer(take.Buffer, false, take.Name); },
                InsertMany = InsertMany
            };
            _browser.Refresh(); // rebuild now that the row actions are wired
            UpdateKeyState();
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
            _generateBtn.SetEnabled(_pending == null && _writing == null);
            _noOpenAI.style.display = AudioStudioSettings.HasPromptWriterKey ? DisplayStyle.None : DisplayStyle.Flex;
            _noWriterKeyText.text = AudioStudioSettings.PromptWriter == PromptWriterProvider.Gemini
                ? "Needs a free Gemini key: sign in at aistudio.google.com with Google ▸ Get API key. Or skip this step and write the English prompt below yourself."
                : "Needs an OpenAI API key. Or skip this step and write the English prompt below yourself.";
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
            _spec.AppendToPattern(ReferenceHint(_referenceAnalysis));
            _ctx.Toast?.Invoke($"Duration set to {d:0.0} s and the reference's timing added to Rhythm ▸ Pattern", false);
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

        // ------------------------------------------------------------------ AI sound analyzer → parameters → prompt

        private void Analyze(bool thenGenerate)
        {
            if (_writing != null || _pending != null) return;
            string idea = (_idea.value ?? "").Trim();
            if (idea.Length == 0) { _ctx.Toast?.Invoke("Describe the sound in step 1 first", true); return; }
            if (!AudioStudioSettings.RequirePromptWriterKey())
            {
                _ctx.Toast?.Invoke($"No {AudioStudioSettings.PromptWriter} API key: set it in Preferences ▸ Audio Creation Studio", true);
                return;
            }
            var req = new PromptIdea
            {
                Description = idea,
                // Choices the user made deliberately are passed on as requirements.
                FixedDuration = _autoDuration.Value ? (float?)null : _duration.Value,
                WantLoop = _loop.Value,
                ReferenceSeconds = _referenceAnalysis != null ? (float?)_referenceAnalysis.DurationSeconds : null,
                ReferenceHint = _referenceAnalysis != null ? ReferenceHint(_referenceAnalysis) : null
            };
            SetWriting(true);
            _writing = PromptWriter.Send(req, res => OnAnalyzed(res, idea, thenGenerate));
            schedule.Execute(() =>
            {
                if (_writing != null) _writerStatus.text = $"Analysing with {_writing.CurrentModel}… {_writing.Elapsed:0} s";
            }).Every(500).Until(() => _writing == null);
        }

        private void SetWriting(bool busy)
        {
            _writeBtn.SetEnabled(!busy);
            _writeGenBtn.SetEnabled(!busy);
            _writeCancelBtn.style.display = busy ? DisplayStyle.Flex : DisplayStyle.None;
            _generateBtn.SetEnabled(!busy && _pending == null);
        }

        private void OnAnalyzed(PromptResult res, string idea, bool thenGenerate)
        {
            _writing = null;
            SetWriting(false);
            if (!res.Ok)
            {
                _writerStatus.text = res.Error;
                if (res.Error != "Cancelled.") _ctx.Toast?.Invoke(res.Error, true);
                return;
            }
            var s = res.Spec;
            _gptIdea = idea;
            _gptSource = res.Source;
            _autoDuration.Value = !s.Duration.HasValue;
            _duration.SetEnabled(s.Duration.HasValue);
            if (s.Duration.HasValue) _duration.Value = s.Duration.Value;
            _loop.Value = s.loop;
            _influence.Value = s.promptInfluence;
            _spec.LoadAi(s, $"Analysed by {res.Source}. Edit any field; the prompt below follows.");
            _prompt.value = ""; // follow the new parameters even if the old prompt was edited by hand
            RebuildPrompt();

            _writerExplain.text = string.IsNullOrEmpty(s.explanation) ? "Analysis ready." : s.explanation;
            _writerWarn.text = s.warning;
            _writerWarn.style.display = string.IsNullOrEmpty(s.warning) ? DisplayStyle.None : DisplayStyle.Flex;
            _writerResult.style.display = DisplayStyle.Flex;
            _writerStatus.text = $"Analysed · {res.Source} · {res.Seconds:0.0} s" + (res.Revised ? " · revised once to meet the rules" : "") +
                                 (res.Skipped != null ? $" · skipped {res.Skipped}" : "");
            if (thenGenerate) Generate();
        }

        private void OnSpecChanged()
        {
            string built = PromptBuilder.Build(_spec.Read());
            bool following = (_prompt.value ?? "").Trim() == _promptFromSpec || string.IsNullOrWhiteSpace(_prompt.value);
            _promptFromSpec = built;
            if (following) _prompt.value = built;
            UpdatePromptSource();
        }

        private void RebuildPrompt()
        {
            _promptFromSpec = PromptBuilder.Build(_spec.Read());
            _prompt.value = _promptFromSpec;
            UpdatePromptSource();
        }

        private void UpdatePromptSource()
        {
            if (_promptSource == null || _rebuildBtn == null) return;
            string p = (_prompt.value ?? "").Trim();
            bool fromSpec = p.Length > 0 && p == _promptFromSpec;
            bool edited = p.Length > 0 && !fromSpec && _promptFromSpec.Length > 0;
            _promptSource.text = p.Length == 0 ? "Fill in the parameters above (or let AI analyse the description), or type an English prompt here."
                : fromSpec ? $"Built from the parameters · {p.Length} chars" + (_spec.EditedCount > 0 ? $" · {_spec.EditedCount} parameter(s) edited by you" : "")
                : edited ? "Edited by hand: parameter changes no longer update it." : $"Typed by hand · {p.Length} chars";
            _rebuildBtn.style.display = edited ? DisplayStyle.Flex : DisplayStyle.None;
            _prompt.EnableInClassList("acs-ai__prompt--edited", edited);
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
            _generateBtn.SetEnabled(!busy && _writing == null);
            _writeBtn?.SetEnabled(!busy && _writing == null);
            _writeGenBtn?.SetEnabled(!busy && _writing == null);
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
                string folder = _browser.SaveFolder;
                TakeStore.EnsureFolder(folder);
                string path = ElevenLabsClient.SaveAudio(resp.Audio, resp.Format, folder, req.Text);
                File.WriteAllText(Path.ChangeExtension(path, ".txt"),
                    $"ElevenLabs sound effect\nprompt: {req.Text}\nmodel: {req.ModelId}\nduration: {(req.DurationSeconds.HasValue ? req.DurationSeconds.Value.ToString("0.0") + " s" : "auto")}\n" +
                    $"prompt_influence: {req.PromptInfluence:0.00}\nloop: {req.Loop}\nformat: {resp.Format}\ngenerated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" +
                    (req.Text == _promptFromSpec && !string.IsNullOrEmpty(_promptFromSpec)
                        ? (_spec.HasAiValues && !string.IsNullOrEmpty(_gptIdea) ? $"idea: {_gptIdea.Replace("\n", " ")}\nanalyzer: {_gptSource}\nedited_parameters: {_spec.EditedCount}\n" : "") +
                          $"spec: {JsonUtility.ToJson(_spec.Read())}\n"
                        : ""));
                AssetDatabase.ImportAsset(path);
                AssetDatabase.ImportAsset(Path.ChangeExtension(path, ".txt"));
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                AudioBuffer buf = null;
                if (clip != null) _ctx.Library.TryGetClip(clip, out buf, out _, out _);
                _batchDone++;
                _browser.Reveal(path);
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

        /// <summary>Per-take buttons shown in the take library.</summary>
        private IEnumerable<VisualElement> TakeActions(TakeBrowser.Take take)
        {
            yield return new StudioButton(Icon.Play, null, () => { if (take.Buffer != null) _ctx.Transport.PlayBuffer(take.Buffer, false, take.Name); }, "Play (or double-click the row)", "acs-btn--sm");
            yield return new StudioButton(Icon.Layers, "A/B", () => PlayAB(take), "Play the reference, then this take", "acs-btn--sm");
            yield return new StudioButton(Icon.Sine, "Edit in Custom", () => EditInGenerator(take),
                "Open this take in the Custom Generate tab as a Sample source: tweak pitch, envelope, filter, noise, tremolo, drive, or Randomize to make variants", "acs-btn--sm", "acs-btn--accent");
            yield return new StudioButton(Icon.Plus, "Insert", () => InsertMany(new List<TakeBrowser.Take> { take }), "Add to the timeline on a new track at the playhead", "acs-btn--sm", "acs-btn--soft");
            yield return new StudioButton(Icon.Loop, null, () => OpenInLoop(take), "Open in the Loop editor", "acs-btn--sm");
        }

        private void EditInGenerator(TakeBrowser.Take t)
        {
            if (t.Clip == null) { _ctx.Toast?.Invoke("This take could not be loaded", true); return; }
            _ctx.EditSampleInGenerator?.Invoke(t.Clip);
            _ctx.Toast?.Invoke("Loaded into Custom Generate (Oscillator ▸ Sample). Tweak the sliders or Randomize for variants; Cmd/Ctrl+Z undoes.", false);
        }

        private void PlayAB(TakeBrowser.Take t)
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

        private void InsertMany(List<TakeBrowser.Take> takes)
        {
            var clips = new List<AudioClip>();
            foreach (var t in takes) if (t.Clip != null) clips.Add(t.Clip);
            if (clips.Count == 0) return;
            int n = _ctx.Commands.ImportClips(clips, _ctx.Session.Playhead, _ctx.Data.tracks.Count, clips.Count > 1);
            if (n > 0) _ctx.Toast?.Invoke(clips.Count == 1 ? "Inserted on a new track. Edit it in the Arrange tab (trim, fades, rate, effects)."
                : $"Inserted {clips.Count} takes on new tracks at the playhead.", false);
        }

        private void OpenInLoop(TakeBrowser.Take t)
        {
            if (t.Clip == null) return;
            _ctx.Session.Modify("Loop Source", d => { d.loop.source = LoopSourceKind.AudioClipAsset; d.loop.assetClip = t.Clip; d.loop.loopEnd = 0; });
            _ctx.ShowTab?.Invoke("Loop");
        }
    }
}
