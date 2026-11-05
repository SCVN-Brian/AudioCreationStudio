using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using AudioCreationStudio.DSP;
using AudioCreationStudio.Export;
using AudioCreationStudio.Models;
using UnityEditor;
using UnityEngine.Networking;

namespace AudioCreationStudio.EditorUI
{
    public sealed class SfxRequest
    {
        public string Text;
        /// <summary>0.5–30 s, or null to let ElevenLabs pick the duration from the prompt.</summary>
        public float? DurationSeconds;
        /// <summary>0–1: how closely the generation follows the prompt (ElevenLabs default 0.3).</summary>
        public float PromptInfluence = 0.3f;
        public bool Loop;
        public string ModelId = ElevenLabsClient.DefaultModel;
        public string OutputFormat = ElevenLabsClient.PreferredFormat;
    }

    public sealed class SfxResponse
    {
        public bool Ok;
        public long Status;
        public byte[] Audio;
        public string Format;
        public string Error;
        public double Seconds;
    }

    /// <summary>
    /// ElevenLabs text-to-sound-effects client (raw HTTPS via UnityWebRequest, main thread).
    /// POST https://api.elevenlabs.io/v1/sound-generation, header xi-api-key. The key is stored in EditorPrefs
    /// (per user and machine, never in the project) or read from ELEVENLABS_API_KEY.
    /// </summary>
    public static class ElevenLabsClient
    {
        public const string Endpoint = "https://api.elevenlabs.io/v1/sound-generation";
        public const string DefaultModel = "eleven_text_to_sound_v2";
        public static readonly string[] Models = { "eleven_text_to_sound_v2", "eleven_text_to_sound_v3" };
        /// <summary>Uncompressed 48 kHz WAV (matches the studio rate; 44.1 kHz PCM/WAV needs a Pro plan, 48 kHz is not tier-gated).</summary>
        public const string PreferredFormat = "wav_48000";
        public const string FallbackFormat = "mp3_44100_128";
        public const float MinDuration = 0.5f, MaxDuration = 30f;
        private const string KeyPref = "AudioCreationStudio.ElevenLabsApiKey";

        public static string ApiKey
        {
            get
            {
                var k = EditorPrefs.GetString(KeyPref, "");
                return !string.IsNullOrEmpty(k) ? k : Environment.GetEnvironmentVariable("ELEVENLABS_API_KEY") ?? "";
            }
        }

        public static bool HasKey => !string.IsNullOrEmpty(ApiKey);
        public static bool KeyFromEnvironment => string.IsNullOrEmpty(EditorPrefs.GetString(KeyPref, "")) && HasKey;
        public static void SaveKey(string key) => EditorPrefs.SetString(KeyPref, (key ?? "").Trim());
        public static void ClearKey() => EditorPrefs.DeleteKey(KeyPref);

        public static string MaskedKey
        {
            get
            {
                var k = ApiKey;
                if (k.Length < 10) return k.Length == 0 ? "" : "••••";
                return k.Substring(0, 4) + "…" + k.Substring(k.Length - 4);
            }
        }

        // ------------------------------------------------------------------ Request

        public static string BuildUrl(SfxRequest r) => $"{Endpoint}?output_format={Uri.EscapeDataString(r.OutputFormat ?? PreferredFormat)}";

        public static string BuildBody(SfxRequest r)
        {
            var sb = new StringBuilder("{");
            sb.Append("\"text\":").Append(Json.Str(r.Text ?? ""));
            sb.Append(",\"model_id\":").Append(Json.Str(string.IsNullOrEmpty(r.ModelId) ? DefaultModel : r.ModelId));
            if (r.DurationSeconds.HasValue)
                sb.Append(",\"duration_seconds\":").Append(Json.Num(Math.Max(MinDuration, Math.Min(MaxDuration, r.DurationSeconds.Value))));
            sb.Append(",\"prompt_influence\":").Append(Json.Num(Math.Max(0f, Math.Min(1f, r.PromptInfluence))));
            sb.Append(",\"loop\":").Append(r.Loop ? "true" : "false");
            return sb.Append('}').ToString();
        }

        // ------------------------------------------------------------------ Sending

        public sealed class Handle
        {
            internal UnityWebRequest Web;
            internal double Started;
            internal Action<SfxResponse> Done;
            internal SfxRequest Request;
            internal bool Finished, Retried;
            public double Elapsed => EditorApplication.timeSinceStartup - Started;
            public bool IsDone => Finished;

            public void Cancel()
            {
                if (Finished) return;
                Finished = true;
                EditorApplication.update -= Poll;
                Web?.Abort();
                Web?.Dispose();
                Done?.Invoke(new SfxResponse { Error = "Cancelled." });
            }

            internal void Poll()
            {
                if (Finished || Web == null || !Web.isDone) return;
                long status = Web.responseCode;
                byte[] data = Web.downloadHandler?.data;
                string netError = Web.error;
                var result = Web.result;
                Web.Dispose();
                Web = null;

                // The preferred lossless format may not be available on every plan: retry once as MP3.
                if (!Retried && status >= 400 && status < 500 && status != 401 && status != 429 && Request.OutputFormat != FallbackFormat &&
                    (status == 403 || (ErrorText(data) ?? "").IndexOf("format", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    Retried = true;
                    Request.OutputFormat = FallbackFormat;
                    Web = Create(Request);
                    Web.SendWebRequest();
                    return;
                }

                Finished = true;
                EditorApplication.update -= Poll;
                var resp = Parse(status, data, result, netError, Request.OutputFormat);
                resp.Seconds = Elapsed;
                Done?.Invoke(resp);
            }
        }

        private static UnityWebRequest Create(SfxRequest r)
        {
            var web = new UnityWebRequest(BuildUrl(r), UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(BuildBody(r))) { contentType = "application/json" },
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = 180
            };
            web.SetRequestHeader("content-type", "application/json");
            web.SetRequestHeader("xi-api-key", ApiKey);
            return web;
        }

        public static Handle Send(SfxRequest request, Action<SfxResponse> done)
        {
            var h = new Handle { Started = EditorApplication.timeSinceStartup, Done = done, Request = request };
            if (!HasKey)
            {
                h.Finished = true;
                done(new SfxResponse { Error = "No ElevenLabs API key. Click 'API Key' and paste a key from elevenlabs.io (Profile ▸ API Keys)." });
                return h;
            }
            if (string.IsNullOrWhiteSpace(request.Text))
            {
                h.Finished = true;
                done(new SfxResponse { Error = "Describe the sound first." });
                return h;
            }
            h.Web = Create(request);
            h.Web.SendWebRequest();
            EditorApplication.update += h.Poll;
            return h;
        }

        // ------------------------------------------------------------------ Response

        private static readonly Regex MessageRx = new Regex("\"(?:message|msg)\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.Compiled);

        /// <summary>Extracts a readable message from ElevenLabs error JSON ({"detail":{"message":…}} or 422 {"detail":[{"msg":…}]}).</summary>
        public static string ErrorText(byte[] data)
        {
            if (data == null || data.Length == 0) return null;
            string body;
            try { body = Encoding.UTF8.GetString(data); } catch { return null; }
            var m = MessageRx.Match(body);
            if (m.Success) return Regex.Unescape(m.Groups[1].Value);
            body = body.Trim();
            return body.StartsWith("{") || body.StartsWith("[") ? (body.Length > 300 ? body.Substring(0, 300) : body) : null;
        }

        public static SfxResponse Parse(long status, byte[] data, UnityWebRequest.Result result, string netError, string format)
        {
            var r = new SfxResponse { Status = status, Format = format };
            if (status == 200 && data != null && data.Length > 64)
            {
                r.Ok = true;
                r.Audio = data;
                return r;
            }
            string msg = ErrorText(data);
            switch (status)
            {
                case 0: r.Error = $"Network error: {netError ?? result.ToString()}. Check the internet connection."; break;
                case 200: r.Error = "ElevenLabs returned no audio."; break;
                case 401: r.Error = "Invalid ElevenLabs API key (401). Check it in 'API Key'."; break;
                case 402: r.Error = $"Not enough ElevenLabs credits / plan limit (402). {msg}"; break;
                case 403: r.Error = $"Not allowed for this plan or key (403). {msg}"; break;
                case 422: r.Error = $"Invalid request (422): {msg}"; break;
                case 429: r.Error = $"Too many requests or concurrency limit (429). Wait a moment. {msg}"; break;
                default: r.Error = status >= 500 ? $"ElevenLabs server error ({status}). Try again." : $"HTTP {status}: {msg ?? netError}"; break;
            }
            return r;
        }

        // ------------------------------------------------------------------ Saving

        public static bool IsRiffWave(byte[] d) =>
            d != null && d.Length >= 12 && d[0] == 'R' && d[1] == 'I' && d[2] == 'F' && d[3] == 'F' && d[8] == 'W' && d[9] == 'A' && d[10] == 'V' && d[11] == 'E';

        /// <summary>
        /// Writes the returned audio as an asset file. WAV is validated (and raw PCM is wrapped in a WAV header);
        /// MP3 is written as-is. Returns the project-relative path.
        /// </summary>
        public static string SaveAudio(byte[] audio, string format, string folder, string baseName)
        {
            Directory.CreateDirectory(folder);
            string stem = PresetStore.MakeSafe(string.IsNullOrWhiteSpace(baseName) ? "SFX" : baseName);
            if (stem.Length > 48) stem = stem.Substring(0, 48);
            stem += "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            bool mp3 = format != null && format.StartsWith("mp3");
            string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{stem}.{(mp3 ? "mp3" : "wav")}");
            if (mp3)
            {
                File.WriteAllBytes(path, audio);
                return path;
            }
            if (IsRiffWave(audio))
            {
                using (var ms = new MemoryStream(audio)) WavDecoder.ReadInfo(ms); // throws if malformed
                File.WriteAllBytes(path, audio);
                return path;
            }
            // Raw little-endian 16-bit mono PCM (pcm_* formats): wrap it.
            int rate = 48000;
            var m = Regex.Match(format ?? "", @"_(\d+)$");
            if (m.Success) rate = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            var buf = new AudioBuffer(1, audio.Length / 2, rate);
            var ch = buf.GetChannel(0);
            for (int i = 0; i < ch.Length; i++) ch[i] = (short)(audio[i * 2] | (audio[i * 2 + 1] << 8)) / 32768f;
            using (var fs = File.Create(path)) WavEncoder.Write(fs, buf, WavSampleFormat.Pcm16);
            return path;
        }
    }
}
