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
        private string _keyDraft = "";
        private bool _showKey;

        private AudioStudioSettingsProvider() : base(AudioStudioSettings.PreferencesPath, SettingsScope.User,
            new HashSet<string> { "ElevenLabs", "API", "Key", "ffmpeg", "MP3", "OGG", "Audio", "Export" }) { }

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
