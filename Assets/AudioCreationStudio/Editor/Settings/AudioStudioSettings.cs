using System;
using System.Collections.Generic;
using AudioCreationStudio.Export;
using UnityEditor;
using UnityEngine;

namespace AudioCreationStudio.EditorUI
{
    /// <summary>
    /// Per-user, per-machine settings (EditorPrefs — never written into the project, so secrets stay out of git).
    /// Edited in Preferences ▸ Audio Creation Studio. Features read them through here and log a warning when a
    /// setting they need is missing.
    /// </summary>
    public static class AudioStudioSettings
    {
        public const string PreferencesPath = "Preferences/Audio Creation Studio";
        public const string ElevenLabsKeyEnv = "ELEVENLABS_API_KEY";
        private const string ElevenLabsKeyPref = "AudioCreationStudio.ElevenLabsApiKey";
        private const string FfmpegPref = "AudioCreationStudio.FfmpegPath";
        public const string OpenAIKeyEnv = "OPENAI_API_KEY";
        private const string OpenAIKeyPref = "AudioCreationStudio.OpenAIApiKey";
        private const string OpenAIModelPref = "AudioCreationStudio.OpenAIModel";
        /// <summary>Any Chat Completions model with Structured Outputs (json_schema) works; changeable in Preferences.</summary>
        public const string DefaultOpenAIModel = "gpt-4.1-mini";
        public const string GeminiKeyEnv = "GEMINI_API_KEY";
        private const string GeminiKeyPref = "AudioCreationStudio.GeminiApiKey";
        private const string GeminiModelPref = "AudioCreationStudio.GeminiModel";
        private const string PromptWriterPref = "AudioCreationStudio.PromptWriter";
        /// <summary>Google's alias for the newest Flash-Lite model (fast, free tier in Google AI Studio); changeable in Preferences.</summary>
        public const string DefaultGeminiModel = "gemini-flash-lite-latest";
        private const string LogPrefix = "[Audio Creation Studio] ";

        /// <summary>Raised after any setting changes, so open windows can refresh.</summary>
        public static event Action Changed;

        // ------------------------------------------------------------------ ElevenLabs

        /// <summary>The key saved in Preferences (empty if none).</summary>
        public static string StoredElevenLabsKey => EditorPrefs.GetString(ElevenLabsKeyPref, "");

        /// <summary>The key in effect: the saved one, otherwise the ELEVENLABS_API_KEY environment variable.</summary>
        public static string ElevenLabsKey
        {
            get
            {
                var k = StoredElevenLabsKey;
                return !string.IsNullOrEmpty(k) ? k : Environment.GetEnvironmentVariable(ElevenLabsKeyEnv) ?? "";
            }
        }

        public static bool HasElevenLabsKey => !string.IsNullOrEmpty(ElevenLabsKey);
        public static bool ElevenLabsKeyFromEnvironment => string.IsNullOrEmpty(StoredElevenLabsKey) && HasElevenLabsKey;

        public static void SetElevenLabsKey(string key)
        {
            key = (key ?? "").Trim();
            if (key.Length == 0) EditorPrefs.DeleteKey(ElevenLabsKeyPref);
            else EditorPrefs.SetString(ElevenLabsKeyPref, key);
            Changed?.Invoke();
        }

        /// <summary>Trims whitespace and surrounding quotes from a pasted key.</summary>
        public static string CleanKey(string k)
        {
            k = (k ?? "").Trim().Trim('"', '\'', '“', '”', '‘', '’').Trim();
            return k;
        }

        /// <summary>Loose sanity check only (formats change): long enough and no whitespace. The real check is an API call.</summary>
        public static bool LooksLikeKey(string k)
        {
            if (string.IsNullOrEmpty(k) || k.Length < 20) return false;
            foreach (char c in k) if (char.IsWhiteSpace(c)) return false;
            return true;
        }

        public static string Mask(string k)
        {
            if (string.IsNullOrEmpty(k)) return "";
            if (k.Length < 10) return "••••";
            return k.Substring(0, 4) + "…" + k.Substring(k.Length - 4);
        }

        /// <summary>Logs a warning when no ElevenLabs key is available. Returns true when a key is set.</summary>
        public static bool RequireElevenLabsKey()
        {
            if (HasElevenLabsKey) return true;
            Debug.LogWarning(LogPrefix + "No ElevenLabs API key set, so AI sound generation is unavailable. " +
                             $"Add a key in Preferences ▸ Audio Creation Studio (Tools ▸ Audio Creation Studio ▸ Settings) or set {ElevenLabsKeyEnv}.");
            return false;
        }

        // ------------------------------------------------------------------ Prompt writer (Gemini / OpenAI)

        /// <summary>The LLM that writes ElevenLabs prompts. Gemini by default (free key from Google AI Studio).</summary>
        public static PromptWriterProvider PromptWriter
        {
            get => (PromptWriterProvider)EditorPrefs.GetInt(PromptWriterPref, (int)PromptWriterProvider.Gemini);
            set { EditorPrefs.SetInt(PromptWriterPref, (int)value); Changed?.Invoke(); }
        }

        public static string PromptWriterModel => PromptWriter == PromptWriterProvider.Gemini ? GeminiModel : OpenAIModel;
        public static bool HasPromptWriterKey => PromptWriter == PromptWriterProvider.Gemini ? HasGeminiKey : HasOpenAIKey;
        public static bool RequirePromptWriterKey() => PromptWriter == PromptWriterProvider.Gemini ? RequireGeminiKey() : RequireOpenAIKey();
        public static string PromptWriterLabel => $"{PromptWriter} {PromptWriterModel}";

        // Gemini (Google AI Studio)

        public static string StoredGeminiKey => EditorPrefs.GetString(GeminiKeyPref, "");

        /// <summary>The key in effect: the saved one, otherwise GEMINI_API_KEY (or GOOGLE_API_KEY).</summary>
        public static string GeminiKey
        {
            get
            {
                var k = StoredGeminiKey;
                if (!string.IsNullOrEmpty(k)) return k;
                k = Environment.GetEnvironmentVariable(GeminiKeyEnv);
                return !string.IsNullOrEmpty(k) ? k : Environment.GetEnvironmentVariable("GOOGLE_API_KEY") ?? "";
            }
        }

        public static bool HasGeminiKey => !string.IsNullOrEmpty(GeminiKey);
        public static bool GeminiKeyFromEnvironment => string.IsNullOrEmpty(StoredGeminiKey) && HasGeminiKey;

        public static void SetGeminiKey(string key)
        {
            key = (key ?? "").Trim();
            if (key.Length == 0) EditorPrefs.DeleteKey(GeminiKeyPref);
            else EditorPrefs.SetString(GeminiKeyPref, key);
            Changed?.Invoke();
        }

        public static string GeminiModel
        {
            get
            {
                var m = EditorPrefs.GetString(GeminiModelPref, "").Trim();
                return m.Length > 0 ? m : DefaultGeminiModel;
            }
            set
            {
                value = (value ?? "").Trim();
                if (value.Length == 0 || value == DefaultGeminiModel) EditorPrefs.DeleteKey(GeminiModelPref);
                else EditorPrefs.SetString(GeminiModelPref, value);
                Changed?.Invoke();
            }
        }

        public static bool RequireGeminiKey()
        {
            if (HasGeminiKey) return true;
            Debug.LogWarning(LogPrefix + "No Gemini API key set, so the prompt writer is unavailable. Get a free key at https://aistudio.google.com/apikey (sign in with Google) " +
                             $"and save it in Preferences ▸ Audio Creation Studio, or set {GeminiKeyEnv}.");
            return false;
        }

        // OpenAI

        public static string StoredOpenAIKey => EditorPrefs.GetString(OpenAIKeyPref, "");

        /// <summary>The key in effect: the saved one, otherwise the OPENAI_API_KEY environment variable.</summary>
        public static string OpenAIKey
        {
            get
            {
                var k = StoredOpenAIKey;
                return !string.IsNullOrEmpty(k) ? k : Environment.GetEnvironmentVariable(OpenAIKeyEnv) ?? "";
            }
        }

        public static bool HasOpenAIKey => !string.IsNullOrEmpty(OpenAIKey);
        public static bool OpenAIKeyFromEnvironment => string.IsNullOrEmpty(StoredOpenAIKey) && HasOpenAIKey;

        public static void SetOpenAIKey(string key)
        {
            key = (key ?? "").Trim();
            if (key.Length == 0) EditorPrefs.DeleteKey(OpenAIKeyPref);
            else EditorPrefs.SetString(OpenAIKeyPref, key);
            Changed?.Invoke();
        }

        public static string OpenAIModel
        {
            get
            {
                var m = EditorPrefs.GetString(OpenAIModelPref, "").Trim();
                return m.Length > 0 ? m : DefaultOpenAIModel;
            }
            set
            {
                value = (value ?? "").Trim();
                if (value.Length == 0 || value == DefaultOpenAIModel) EditorPrefs.DeleteKey(OpenAIModelPref);
                else EditorPrefs.SetString(OpenAIModelPref, value);
                Changed?.Invoke();
            }
        }

        /// <summary>Logs a warning when no OpenAI key is available. Returns true when a key is set.</summary>
        public static bool RequireOpenAIKey()
        {
            if (HasOpenAIKey) return true;
            Debug.LogWarning(LogPrefix + "No OpenAI API key set, so the prompt writer is unavailable. " +
                             $"Add a key in Preferences ▸ Audio Creation Studio (Tools ▸ Audio Creation Studio ▸ Settings) or set {OpenAIKeyEnv}.");
            return false;
        }

        // ------------------------------------------------------------------ ffmpeg

        /// <summary>Explicit ffmpeg executable. Empty = auto-detect (PATH, Homebrew, WinGet, Scoop, Chocolatey).</summary>
        public static string FfmpegPath
        {
            get => EditorPrefs.GetString(FfmpegPref, "");
            set
            {
                EditorPrefs.SetString(FfmpegPref, (value ?? "").Trim());
                Changed?.Invoke();
            }
        }

        /// <summary>The ffmpeg MP3/OGG export will use, or null when none is found.</summary>
        public static string ResolveFfmpeg() => FfmpegEncoder.Locate(FfmpegPath);

        /// <summary>Logs a warning when MP3/OGG export cannot run because ffmpeg is missing. Returns the ffmpeg path or null.</summary>
        public static string RequireFfmpeg(string formatLabel)
        {
            var p = ResolveFfmpeg();
            if (p != null) return p;
            string configured = FfmpegPath;
            Debug.LogWarning(LogPrefix + $"{formatLabel} export needs ffmpeg, which was not found" +
                             (string.IsNullOrEmpty(configured) ? "" : $" (configured path '{configured}' does not exist)") +
                             ". Install it (macOS: brew install ffmpeg · Windows: winget install ffmpeg) or set its path in Preferences ▸ Audio Creation Studio. " +
                             "WAV, AIFF and FLAC export do not need it.");
            return null;
        }

        // ------------------------------------------------------------------ UI entry points

        [MenuItem("Tools/Audio Creation Studio/Settings…", priority = 90)]
        public static void Open() => SettingsService.OpenUserPreferences(PreferencesPath);
    }

    /// <summary>Preferences ▸ Audio Creation Studio.</summary>
    internal sealed class AudioStudioSettingsProvider : SettingsProvider
    {
        private string _keyDraft = "", _openAIDraft = "", _geminiDraft = "";
        private bool _showKey, _showOpenAI, _showGemini;

        private AudioStudioSettingsProvider() : base(AudioStudioSettings.PreferencesPath, SettingsScope.User,
            new HashSet<string> { "ElevenLabs", "OpenAI", "ChatGPT", "GPT", "Gemini", "Google", "API", "Key", "ffmpeg", "MP3", "OGG", "Audio", "Export" }) { }

        [SettingsProvider]
        public static SettingsProvider Create() => new AudioStudioSettingsProvider();

        public override void OnGUI(string searchContext)
        {
            EditorGUIUtility.labelWidth = 160;
            GUILayout.Space(8);
            using (new EditorGUILayout.VerticalScope(GUILayout.MaxWidth(640)))
            {
                ElevenLabsSection();
                GUILayout.Space(16);
                PromptWriterSection();
                GUILayout.Space(16);
                FfmpegSection();
                GUILayout.Space(16);
                EditorGUILayout.HelpBox("These settings are stored per user on this computer (EditorPrefs), never in the project or in git.", MessageType.None);
            }
        }

        private void ElevenLabsSection()
        {
            EditorGUILayout.LabelField("ElevenLabs (AI Sound Effects)", EditorStyles.boldLabel);
            string stored = AudioStudioSettings.StoredElevenLabsKey;
            string status = !string.IsNullOrEmpty(stored) ? $"Saved key {AudioStudioSettings.Mask(stored)}"
                : AudioStudioSettings.ElevenLabsKeyFromEnvironment ? $"Using {AudioStudioSettings.ElevenLabsKeyEnv} ({AudioStudioSettings.Mask(AudioStudioSettings.ElevenLabsKey)})"
                : "Not set";
            EditorGUILayout.LabelField("Status", status);

            using (new EditorGUILayout.HorizontalScope())
            {
                _keyDraft = _showKey ? EditorGUILayout.TextField("API Key", _keyDraft) : EditorGUILayout.PasswordField("API Key", _keyDraft);
                _showKey = GUILayout.Toggle(_showKey, "Show", EditorStyles.miniButton, GUILayout.Width(48));
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(EditorGUIUtility.labelWidth + 2);
                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_keyDraft)))
                {
                    if (GUILayout.Button("Save", GUILayout.Width(80)))
                    {
                        var k = _keyDraft.Trim();
                        if (k.Length < 20)
                            Debug.LogWarning("[Audio Creation Studio] That does not look like an ElevenLabs API key; it was not saved.");
                        else
                        {
                            AudioStudioSettings.SetElevenLabsKey(k);
                            _keyDraft = "";
                            GUI.FocusControl(null);
                        }
                    }
                }
                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(stored)))
                {
                    if (GUILayout.Button("Remove", GUILayout.Width(80))) AudioStudioSettings.SetElevenLabsKey("");
                }
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Get a key…", EditorStyles.linkLabel)) Application.OpenURL("https://elevenlabs.io/app/settings/api-keys");
            }
            EditorGUILayout.HelpBox($"Each generation uses ElevenLabs credits from your plan. If no key is saved, the {AudioStudioSettings.ElevenLabsKeyEnv} environment variable is used.", MessageType.Info);
        }

        private void PromptWriterSection()
        {
            EditorGUILayout.LabelField("Prompt writer (describe in any language → ElevenLabs prompt)", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            var provider = (PromptWriterProvider)EditorGUILayout.EnumPopup(new GUIContent("Provider", "Gemini: free key from Google AI Studio (sign in with Google). OpenAI: paid API key."), AudioStudioSettings.PromptWriter);
            if (EditorGUI.EndChangeCheck()) AudioStudioSettings.PromptWriter = provider;
            GUILayout.Space(4);
            if (provider == PromptWriterProvider.Gemini) GeminiSection(); else OpenAISection();
        }

        private void GeminiSection()
        {
            string stored = AudioStudioSettings.StoredGeminiKey;
            string status = !string.IsNullOrEmpty(stored) ? $"Saved key {AudioStudioSettings.Mask(stored)}"
                : AudioStudioSettings.GeminiKeyFromEnvironment ? $"Using environment variable ({AudioStudioSettings.Mask(AudioStudioSettings.GeminiKey)})"
                : "Not set";
            EditorGUILayout.LabelField("Status", status);
            using (new EditorGUILayout.HorizontalScope())
            {
                _geminiDraft = _showGemini ? EditorGUILayout.TextField("Gemini API Key", _geminiDraft) : EditorGUILayout.PasswordField("Gemini API Key", _geminiDraft);
                _showGemini = GUILayout.Toggle(_showGemini, "Show", EditorStyles.miniButton, GUILayout.Width(48));
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(EditorGUIUtility.labelWidth + 2);
                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_geminiDraft)))
                {
                    if (GUILayout.Button("Save", GUILayout.Width(80)))
                    {
                        var k = AudioStudioSettings.CleanKey(_geminiDraft);
                        if (!AudioStudioSettings.LooksLikeKey(k))
                        {
                            _geminiCheck = "That does not look like an API key (too short or contains spaces). Copy it again from aistudio.google.com/apikey.";
                            _geminiCheckType = MessageType.Error;
                        }
                        else
                        {
                            AudioStudioSettings.SetGeminiKey(k);
                            _geminiDraft = "";
                            GUI.FocusControl(null);
                            TestGeminiKey();
                        }
                    }
                }
                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(stored)))
                {
                    if (GUILayout.Button("Remove", GUILayout.Width(80))) { AudioStudioSettings.SetGeminiKey(""); _geminiCheck = null; }
                }
                using (new EditorGUI.DisabledScope(!AudioStudioSettings.HasGeminiKey || _geminiChecking))
                {
                    if (GUILayout.Button(new GUIContent(_geminiChecking ? "Testing…" : "Test", "Check the key with Google (free, no generation)"), GUILayout.Width(70))) TestGeminiKey();
                }
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Get a free key…", EditorStyles.linkLabel)) Application.OpenURL("https://aistudio.google.com/apikey");
            }
            if (!string.IsNullOrEmpty(_geminiCheck)) EditorGUILayout.HelpBox(_geminiCheck, _geminiCheckType);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                string m = EditorGUILayout.DelayedTextField(new GUIContent("Model", "Any Gemini model that supports JSON response schemas. The default alias always points to the newest Flash-Lite model (fast, ~2 s). If the model hangs or is busy, Flash-Lite fallbacks are tried automatically."), AudioStudioSettings.GeminiModel);
                if (EditorGUI.EndChangeCheck()) AudioStudioSettings.GeminiModel = m;
                using (new EditorGUI.DisabledScope(AudioStudioSettings.GeminiModel == AudioStudioSettings.DefaultGeminiModel))
                {
                    if (GUILayout.Button("Default", GUILayout.Width(64))) { AudioStudioSettings.GeminiModel = ""; GUI.FocusControl(null); }
                }
            }
            EditorGUILayout.HelpBox("Get a free key: open aistudio.google.com, sign in with your Google account, click Get API key ▸ Create API key, and paste it here. " +
                                    $"The free tier has per-minute and per-day limits. If no key is saved, the {AudioStudioSettings.GeminiKeyEnv} (or GOOGLE_API_KEY) environment variable is used.", MessageType.Info);
        }

        private string _geminiCheck;
        private MessageType _geminiCheckType;
        private bool _geminiChecking;

        private void TestGeminiKey()
        {
            _geminiChecking = true;
            _geminiCheck = "Checking the key with Google…";
            _geminiCheckType = MessageType.None;
            PromptWriter.CheckGeminiKey(AudioStudioSettings.GeminiKey, (ok, message) =>
            {
                _geminiChecking = false;
                _geminiCheck = ok ? "✓ Key saved and working." : "Key saved, but Google rejected it: " + message;
                _geminiCheckType = ok ? MessageType.Info : MessageType.Warning;
                Repaint();
            });
        }

        private void OpenAISection()
        {
            string stored = AudioStudioSettings.StoredOpenAIKey;
            string status = !string.IsNullOrEmpty(stored) ? $"Saved key {AudioStudioSettings.Mask(stored)}"
                : AudioStudioSettings.OpenAIKeyFromEnvironment ? $"Using {AudioStudioSettings.OpenAIKeyEnv} ({AudioStudioSettings.Mask(AudioStudioSettings.OpenAIKey)})"
                : "Not set";
            EditorGUILayout.LabelField("Status", status);
            using (new EditorGUILayout.HorizontalScope())
            {
                _openAIDraft = _showOpenAI ? EditorGUILayout.TextField("API Key", _openAIDraft) : EditorGUILayout.PasswordField("API Key", _openAIDraft);
                _showOpenAI = GUILayout.Toggle(_showOpenAI, "Show", EditorStyles.miniButton, GUILayout.Width(48));
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(EditorGUIUtility.labelWidth + 2);
                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_openAIDraft)))
                {
                    if (GUILayout.Button("Save", GUILayout.Width(80)))
                    {
                        var k = AudioStudioSettings.CleanKey(_openAIDraft);
                        if (!AudioStudioSettings.LooksLikeKey(k))
                            Debug.LogWarning("[Audio Creation Studio] That does not look like an API key (too short or contains spaces); it was not saved.");
                        else
                        {
                            AudioStudioSettings.SetOpenAIKey(k);
                            _openAIDraft = "";
                            GUI.FocusControl(null);
                        }
                    }
                }
                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(stored)))
                {
                    if (GUILayout.Button("Remove", GUILayout.Width(80))) AudioStudioSettings.SetOpenAIKey("");
                }
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Get a key…", EditorStyles.linkLabel)) Application.OpenURL("https://platform.openai.com/api-keys");
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                string m = EditorGUILayout.DelayedTextField(new GUIContent("Model", "Any Chat Completions model that supports Structured Outputs"), AudioStudioSettings.OpenAIModel);
                if (EditorGUI.EndChangeCheck()) AudioStudioSettings.OpenAIModel = m;
                using (new EditorGUI.DisabledScope(AudioStudioSettings.OpenAIModel == AudioStudioSettings.DefaultOpenAIModel))
                {
                    if (GUILayout.Button("Default", GUILayout.Width(64))) { AudioStudioSettings.OpenAIModel = ""; GUI.FocusControl(null); }
                }
            }
            EditorGUILayout.HelpBox("Turns your description (any language) into a precise English ElevenLabs prompt with duration, loop and prompt influence. " +
                                    $"Each request uses OpenAI credits. If no key is saved, the {AudioStudioSettings.OpenAIKeyEnv} environment variable is used.", MessageType.Info);
        }

        private void FfmpegSection()
        {
            EditorGUILayout.LabelField("ffmpeg (MP3 / OGG export)", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                string p = EditorGUILayout.DelayedTextField(new GUIContent("Executable", "Leave empty to auto-detect"), AudioStudioSettings.FfmpegPath);
                if (EditorGUI.EndChangeCheck()) AudioStudioSettings.FfmpegPath = p;
                if (GUILayout.Button("Browse…", GUILayout.Width(70)))
                {
                    string picked = EditorUtility.OpenFilePanel("Locate ffmpeg", "", "");
                    if (!string.IsNullOrEmpty(picked)) { AudioStudioSettings.FfmpegPath = picked; GUI.FocusControl(null); }
                }
                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(AudioStudioSettings.FfmpegPath)))
                {
                    if (GUILayout.Button("Auto", GUILayout.Width(48))) { AudioStudioSettings.FfmpegPath = ""; GUI.FocusControl(null); }
                }
            }
            string found = AudioStudioSettings.ResolveFfmpeg();
            if (found != null) EditorGUILayout.HelpBox($"Encoding with {found}", MessageType.Info);
            else EditorGUILayout.HelpBox("ffmpeg not found. Install it (macOS: brew install ffmpeg · Windows: winget install ffmpeg) or browse to it. WAV, AIFF and FLAC export do not need it.", MessageType.Warning);
        }
    }
}
