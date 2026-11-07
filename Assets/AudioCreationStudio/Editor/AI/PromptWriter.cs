using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace AudioCreationStudio.EditorUI
{
    /// <summary>What the user wants, in their own words (any language), plus optional context.</summary>
    public sealed class PromptIdea
    {
        public string Description;
        /// <summary>Optional reference analysis: its length in seconds and a plain-language timing hint.</summary>
        public float? ReferenceSeconds;
        public string ReferenceHint;
        /// <summary>Optional: the duration the user already fixed in the UI (null = let the writer decide).</summary>
        public float? FixedDuration;
        public bool WantLoop;
    }

    public sealed class PromptResult
    {
        public bool Ok;
        /// <summary>"Gemini gemini-flash-latest", "OpenAI gpt-4.1-mini"…</summary>
        public string Source;
        /// <summary>The nine analysed sound parameters plus recommended ElevenLabs settings (validated, clamped).</summary>
        public SoundSpec Spec;
        public string Error;
        public double Seconds;
        /// <summary>True when the first answer broke a rule and the model was asked to fix it.</summary>
        public bool Revised;
        /// <summary>Models that failed before one answered (null when the first one did).</summary>
        public string Skipped;
    }

    /// <summary>Which LLM writes the ElevenLabs prompt.</summary>
    public enum PromptWriterProvider { Gemini = 0, OpenAI = 1 }

    /// <summary>
    /// AI Sound Analyzer: turns a free-form description (Vietnamese, English or any language) into a typed
    /// <see cref="SoundSpec"/> — sound source, action, texture, pitch, rhythm, intensity, style, duration/structure and
    /// constraints, all in English — plus recommended ElevenLabs settings. Uses Google Gemini (free key from Google AI
    /// Studio, JSON response schema) or OpenAI (strict JSON schema). The answer is clamped to ElevenLabs' limits and
    /// checked (English, no quotes/onomatopoeia/instructions, the user's fixed choices); a violation is sent back once
    /// for a fix. The ElevenLabs prompt itself is built locally from the spec by <see cref="PromptBuilder"/>.
    /// </summary>
    public static class PromptWriter
    {
        public const string Endpoint = "https://api.openai.com/v1/chat/completions";
        public const string GeminiEndpoint = "https://generativelanguage.googleapis.com/v1beta/models/";
        public const int GeminiTimeoutSeconds = 20;
        /// <summary>Tried in order when the chosen Gemini model hangs, is overloaded (5xx), rate-limited (429) or missing (404).</summary>
        public static readonly string[] GeminiFallbacks = { "gemini-flash-lite-latest", "gemini-3.5-flash-lite", "gemini-3.1-flash-lite" };

        public static bool IsRetryable(long status) => status == 0 || status == 404 || status == 429 || status >= 500;

        /// <summary>The configured model first, then the fallbacks (no duplicates).</summary>
        public static List<string> GeminiModelChain(string configured)
        {
            var list = new List<string>();
            void Add(string m)
            {
                m = (m ?? "").Trim();
                if (m.StartsWith("models/", StringComparison.Ordinal)) m = m.Substring(7);
                if (m.Length > 0 && !list.Contains(m)) list.Add(m);
            }
            Add(configured);
            foreach (var f in GeminiFallbacks) Add(f);
            return list;
        }
        /// <summary>ElevenLabs reads short descriptions best; longer text is trimmed at a word boundary.</summary>
        public const int MaxPromptChars = 300;

        public const string SystemPrompt =
@"You are a game sound designer. The user describes a sound effect they need, in Vietnamese, English or another language. Analyse it into structured parameters that will be turned into an ElevenLabs text-to-sound-effects prompt.

FIELDS (every value in ENGLISH, short descriptive phrases, translate the meaning, never copy non-English words)
- soundSource: what produces the sound (object, character, machine, creature, environment). E.g. ""Human voice"", ""Small wooden door"", ""Puppy"".
- action: the physical action or event that makes the sound. E.g. ""Underwater gurgling"", ""Slamming shut"", ""Barking"".
- texture: 1 to 5 acoustic qualities, e.g. Wet, Bubbly, Metallic, Crunchy, Muffled, Airy, Hollow, Gritty.
- pitch: Low, Medium, High, Variable, or a short profile such as ""Rising from low to high"".
- rhythm.pattern: what happens over time, e.g. ""Three short gurgles"", ""Single hit"", ""Steady continuous hum"". rhythm.repetitions: how many distinct events (0 if continuous or unclear). rhythm.spacing: e.g. ""Short pauses"", ""Even spacing"", ""Rapid succession"" (empty if single or continuous).
- intensity: Soft, Moderate, Strong, Aggressive, Explosive, or Dynamic (with a short note if it changes).
- style: e.g. Realistic, Cartoon, Casual game, Arcade, Retro 8-bit, Cinematic, Fantasy, Sci-fi. Combine when useful (""Cartoon casual game"").
- duration.seconds: total length, 0.5 to 30. If the user states a length, use it. Otherwise estimate: events x typical event length + natural tail (UI click 0.5, coin/pickup 0.5-1, footstep 0.5, gunshot 1-2, door slam 1.5-2, short vocal or creature call 1-2, explosion 3-5, whoosh 1-1.5, ambience/loop 8-15). Use 0 only if it truly cannot be judged.
- duration.attack / duration.sustain / duration.release: the envelope, e.g. ""Immediate"", ""Three distinct vocal bursts"", ""Short natural decay"".
- constraints: unwanted elements, phrased ""No music"", ""No background ambience"", ""No dialogue"", ""No reverb"", ""No distortion"". Default to [""No music"", ""No background ambience""] for an isolated game sound effect unless the user wants an environment; add what the user explicitly excludes.
- loop: true only for continuous sounds meant to repeat seamlessly (ambience, engine hum, rain, drones); then the rhythm is steady and continuous and seconds is at least 5.
- promptInfluence: 0.3 normal; 0.5-0.7 when the user gives precise requirements (counts, exact character); 0.15-0.25 for vague or creative requests.
- explanation: one or two short sentences IN THE USER'S LANGUAGE summarising the analysis.
- warning: IN THE USER'S LANGUAGE, only if something cannot be met (longer than 30 s, intelligible speech or lyrics, music, several unrelated sounds); otherwise an empty string.

RULES
- No quotation marks, no onomatopoeia (woof, bang, boom, pew) and no spoken words in any field: ElevenLabs reads them aloud as speech. Describe vocal sounds instead (""wordless panicked shout"").
- No instructions or requests (""make"", ""create"", ""generate"", ""please"").
- Infer sensible values for anything the user did not say; never leave soundSource or action empty.";

        // ------------------------------------------------------------------ Request

        public static string BuildUserMessage(PromptIdea idea)
        {
            var sb = new StringBuilder();
            sb.Append("Sound needed: ").Append((idea.Description ?? "").Trim());
            if (idea.FixedDuration.HasValue)
                sb.Append($"\nThe user fixed the duration to {idea.FixedDuration.Value:0.0} seconds: use exactly that.");
            if (idea.WantLoop)
                sb.Append("\nThe user wants a seamless loop.");
            if (idea.ReferenceSeconds.HasValue)
                sb.Append($"\nReference recording: {idea.ReferenceSeconds.Value:0.00} s long; analysis: {idea.ReferenceHint}. Match its length and timing.");
            return sb.ToString();
        }

        private static readonly string[] SpecFields =
            { "soundSource", "action", "texture", "pitch", "rhythm", "intensity", "style", "duration", "constraints", "loop", "promptInfluence", "explanation", "warning" };

        /// <summary>JSON schema of <see cref="SoundSpec"/>. <paramref name="gemini"/>: Gemini's OpenAPI subset (UPPERCASE types,
        /// propertyOrdering); otherwise OpenAI strict mode (additionalProperties false everywhere).</summary>
        public static string SpecSchema(bool gemini)
        {
            string T(string t) => gemini ? t.ToUpperInvariant() : t;
            string Str(string desc = null) => desc == null ? $"{{\"type\":\"{T("string")}\"}}" : $"{{\"type\":\"{T("string")}\",\"description\":{Json.Str(desc)}}}";
            string StrArray(string desc) => $"{{\"type\":\"{T("array")}\",\"description\":{Json.Str(desc)},\"items\":{{\"type\":\"{T("string")}\"}}}}";
            string Obj(string[] names, string[] props)
            {
                var sb = new StringBuilder($"{{\"type\":\"{T("object")}\",\"properties\":{{");
                for (int i = 0; i < names.Length; i++) sb.Append(i > 0 ? "," : "").Append(Json.Str(names[i])).Append(':').Append(props[i]);
                sb.Append("},\"required\":[");
                for (int i = 0; i < names.Length; i++) sb.Append(i > 0 ? "," : "").Append(Json.Str(names[i]));
                sb.Append(']');
                if (gemini)
                {
                    sb.Append(",\"propertyOrdering\":[");
                    for (int i = 0; i < names.Length; i++) sb.Append(i > 0 ? "," : "").Append(Json.Str(names[i]));
                    sb.Append(']');
                }
                else sb.Append(",\"additionalProperties\":false");
                return sb.Append('}').ToString();
            }
            string rhythm = Obj(new[] { "pattern", "repetitions", "spacing" },
                new[] { Str("What happens over time"), $"{{\"type\":\"{T("integer")}\"}}", Str("Spacing between events") });
            string duration = Obj(new[] { "seconds", "attack", "sustain", "release" },
                new[] { $"{{\"type\":\"{T("number")}\",\"description\":\"0.5 to 30, or 0 if unknown\"}}", Str(), Str(), Str() });
            return Obj(SpecFields, new[]
            {
                Str("Object, character, machine, creature or environment producing the sound"),
                Str("Physical action or event"),
                StrArray("Acoustic qualities"),
                Str("Low, Medium, High, Variable or a pitch profile"),
                rhythm,
                Str("Soft, Moderate, Strong, Aggressive, Explosive or Dynamic"),
                Str("Cartoon, Casual game, Realistic, Cinematic, Arcade, Fantasy…"),
                duration,
                StrArray("Unwanted elements, e.g. No music"),
                $"{{\"type\":\"{T("boolean")}\"}}",
                $"{{\"type\":\"{T("number")}\",\"description\":\"0 to 1\"}}",
                Str("In the user's language"),
                Str("In the user's language, or empty")
            });
        }

        /// <param name="history">Extra (role, content) messages appended after the user message (used for the fix-up round).</param>
        public static string BuildOpenAIBody(PromptIdea idea, string model, IList<KeyValuePair<string, string>> history = null)
        {
            var sb = new StringBuilder("{");
            sb.Append("\"model\":").Append(Json.Str(string.IsNullOrWhiteSpace(model) ? AudioStudioSettings.DefaultOpenAIModel : model.Trim()));
            sb.Append(",\"messages\":[");
            sb.Append("{\"role\":\"system\",\"content\":").Append(Json.Str(SystemPrompt)).Append('}');
            sb.Append(",{\"role\":\"user\",\"content\":").Append(Json.Str(BuildUserMessage(idea))).Append('}');
            if (history != null)
                foreach (var m in history)
                    sb.Append(",{\"role\":").Append(Json.Str(m.Key)).Append(",\"content\":").Append(Json.Str(m.Value)).Append('}');
            sb.Append("],\"response_format\":{\"type\":\"json_schema\",\"json_schema\":{\"name\":\"sound_spec\",\"strict\":true,\"schema\":")
              .Append(SpecSchema(false)).Append("}}");
            return sb.Append('}').ToString();
        }

        // ------------------------------------------------------------------ Response

        [Serializable] private sealed class ChatResponse { public Choice[] choices; public ErrorBody error; }
        [Serializable] private sealed class Choice { public Message message; public string finish_reason; }
        [Serializable] private sealed class Message { public string content; public string refusal; }
        [Serializable] private sealed class ErrorBody { public string message; public string type; public string code; }

        /// <summary>Extracts the OpenAI model's JSON answer, or an error message. Returns the raw content via <paramref name="content"/>.</summary>
        public static PromptResult ParseOpenAI(long status, string body, string netError, out string content)
        {
            content = null;
            ChatResponse r = null;
            try { if (!string.IsNullOrEmpty(body)) r = JsonUtility.FromJson<ChatResponse>(body); } catch { /* not JSON */ }
            string apiMsg = r?.error?.message;
            if (status != 200)
            {
                string err;
                switch (status)
                {
                    case 0: err = $"Network error: {netError}. Check the internet connection."; break;
                    case 401: err = "Invalid OpenAI API key (401). Check it in Preferences ▸ Audio Creation Studio."; break;
                    case 403: err = $"OpenAI refused the request (403). {apiMsg}"; break;
                    case 404: err = $"Model not found (404). Check the model name in Preferences ▸ Audio Creation Studio. {apiMsg}"; break;
                    case 429: err = $"OpenAI rate limit or no credits left (429). {apiMsg}"; break;
                    case 400: err = $"OpenAI rejected the request (400): {apiMsg}"; break;
                    default: err = status >= 500 ? $"OpenAI server error ({status}). Try again." : $"HTTP {status}: {apiMsg ?? netError}"; break;
                }
                return new PromptResult { Error = err };
            }
            var choice = r?.choices != null && r.choices.Length > 0 ? r.choices[0] : null;
            if (choice?.message == null) return new PromptResult { Error = "OpenAI returned no answer." };
            if (!string.IsNullOrEmpty(choice.message.refusal)) return new PromptResult { Error = "The model refused: " + choice.message.refusal };
            if (choice.finish_reason == "length") return new PromptResult { Error = "The answer was cut off (token limit). Try a shorter description." };
            content = choice.message.content;
            return ReadSpec(content);
        }

        private static PromptResult ReadSpec(string content)
        {
            SoundSpec spec = null;
            try { spec = JsonUtility.FromJson<SoundSpec>(content ?? ""); } catch { /* handled below */ }
            if (spec == null) return new PromptResult { Error = "The model's answer could not be read." };
            spec.Normalize();
            if (spec.soundSource.Length == 0 && spec.action.Length == 0) return new PromptResult { Error = "The model's answer could not be read (no sound source or action)." };
            return new PromptResult { Ok = true, Spec = spec };
        }

        // ------------------------------------------------------------------ Gemini

        /// <summary>"models/gemini-x" or "gemini-x" → the generateContent URL.</summary>
        public static string GeminiUrl(string model)
        {
            model = (string.IsNullOrWhiteSpace(model) ? AudioStudioSettings.DefaultGeminiModel : model.Trim());
            if (model.StartsWith("models/", StringComparison.Ordinal)) model = model.Substring(7);
            return GeminiEndpoint + Uri.EscapeDataString(model) + ":generateContent";
        }

        /// <param name="history">Extra (role, content) turns; "assistant" is sent as Gemini's "model" role.</param>
        public static string BuildGeminiBody(PromptIdea idea, IList<KeyValuePair<string, string>> history = null)
        {
            var sb = new StringBuilder("{");
            sb.Append("\"system_instruction\":{\"parts\":[{\"text\":").Append(Json.Str(SystemPrompt)).Append("}]}");
            sb.Append(",\"contents\":[");
            sb.Append("{\"role\":\"user\",\"parts\":[{\"text\":").Append(Json.Str(BuildUserMessage(idea))).Append("}]}");
            if (history != null)
                foreach (var m in history)
                    sb.Append(",{\"role\":").Append(Json.Str(m.Key == "assistant" ? "model" : "user"))
                      .Append(",\"parts\":[{\"text\":").Append(Json.Str(m.Value)).Append("}]}");
            sb.Append("],\"generationConfig\":{\"responseMimeType\":\"application/json\",\"responseSchema\":").Append(SpecSchema(true)).Append('}');
            return sb.Append('}').ToString();
        }

        [Serializable] private sealed class GResponse { public GCandidate[] candidates; public GFeedback promptFeedback; public GError error; }
        [Serializable] private sealed class GCandidate { public GContent content; public string finishReason; }
        [Serializable] private sealed class GContent { public GPart[] parts; }
        [Serializable] private sealed class GPart { public string text; public bool thought; }
        [Serializable] private sealed class GFeedback { public string blockReason; }
        [Serializable] private sealed class GError { public int code; public string message; public string status; }

        /// <summary>Extracts Gemini's JSON answer, or an error message. Returns the raw text via <paramref name="content"/>.</summary>
        public static PromptResult ParseGemini(long status, string body, string netError, out string content)
        {
            content = null;
            GResponse r = null;
            try { if (!string.IsNullOrEmpty(body)) r = JsonUtility.FromJson<GResponse>(body); } catch { /* not JSON */ }
            string apiMsg = r?.error?.message;
            if (status != 200)
            {
                string err;
                bool badKey = (apiMsg ?? "").IndexOf("API key", StringComparison.OrdinalIgnoreCase) >= 0;
                switch (status)
                {
                    case 0: err = $"Network error: {netError}. Check the internet connection."; break;
                    case 400 when badKey:
                    case 401:
                    case 403 when badKey:
                        err = "Invalid Gemini API key. Get a free one at aistudio.google.com ▸ Get API key, then save it in Preferences ▸ Audio Creation Studio."; break;
                    case 403: err = $"Google refused the request (403). {apiMsg}"; break;
                    case 404: err = $"Gemini model not found (404). Check the model name in Preferences ▸ Audio Creation Studio. {apiMsg}"; break;
                    case 429: err = $"Gemini free-tier limit reached (429). Wait a minute and try again. {apiMsg}"; break;
                    case 400: err = $"Gemini rejected the request (400): {apiMsg}"; break;
                    default: err = status >= 500 ? $"Gemini server error ({status}). Try again." : $"HTTP {status}: {apiMsg ?? netError}"; break;
                }
                return new PromptResult { Error = err };
            }
            if (!string.IsNullOrEmpty(r?.promptFeedback?.blockReason))
                return new PromptResult { Error = $"Gemini blocked the description ({r.promptFeedback.blockReason}). Rephrase it." };
            var cand = r?.candidates != null && r.candidates.Length > 0 ? r.candidates[0] : null;
            if (cand == null) return new PromptResult { Error = "Gemini returned no answer." };
            if (cand.finishReason == "MAX_TOKENS") return new PromptResult { Error = "The answer was cut off (token limit). Try a shorter description." };
            if (cand.finishReason == "SAFETY" || cand.finishReason == "PROHIBITED_CONTENT")
                return new PromptResult { Error = "Gemini blocked the answer for safety reasons. Rephrase the description." };
            var text = new StringBuilder();
            if (cand.content?.parts != null)
                foreach (var part in cand.content.parts)
                    if (!part.thought && part.text != null) text.Append(part.text);
            content = text.ToString();
            return ReadSpec(content);
        }

        /// <summary>Rule problems the model must fix (null = fine): the studio's prompt checks on the built prompt and on
        /// every field, plus the user's fixed choices.</summary>
        public static string Problems(SoundSpec s, PromptIdea idea)
        {
            var issues = new List<string>();
            if (s.soundSource.Length == 0) issues.Add("soundSource is empty.");
            if (s.action.Length == 0) issues.Add("action is empty.");
            string all = string.Join(" ", s.soundSource, s.action, SoundSpec.JoinList(s.texture), s.pitch, s.rhythm.pattern, s.rhythm.spacing,
                s.intensity, s.style, s.duration.attack, s.duration.sustain, s.duration.release, SoundSpec.JoinList(s.constraints));
            var check = ElevenLabsPanel.CheckPrompt(all);
            if (check != null) issues.Add("All field values must follow the rules: " + check);
            if (idea.FixedDuration.HasValue && Mathf.Abs(s.duration.seconds - idea.FixedDuration.Value) > 0.05f)
                issues.Add($"duration.seconds must be {idea.FixedDuration.Value:0.0}.");
            if (idea.WantLoop && !s.loop) issues.Add("loop must be true.");
            return issues.Count == 0 ? null : string.Join(" ", issues);
        }

        // ------------------------------------------------------------------ Sending

        public sealed class Handle
        {
            internal UnityWebRequest Web;
            internal double Started;
            internal Action<PromptResult> Done;
            internal PromptIdea Idea;
            internal string Model;
            internal PromptWriterProvider Provider;
            internal bool Finished, Revised;
            internal Queue<string> Fallbacks = new Queue<string>();
            internal List<string> Skipped = new List<string>();
            public double Elapsed => EditorApplication.timeSinceStartup - Started;
            /// <summary>The model currently being asked (changes when a fallback model is tried).</summary>
            public string CurrentModel => Model;

            public void Cancel()
            {
                if (Finished) return;
                Finished = true;
                EditorApplication.update -= Poll;
                Web?.Abort();
                Web?.Dispose();
                Done?.Invoke(new PromptResult { Error = "Cancelled." });
            }

            internal void Poll()
            {
                if (Finished || Web == null || !Web.isDone) return;
                long status = Web.responseCode;
                string body = Web.downloadHandler?.text;
                string netError = Web.error;
                Web.Dispose();
                Web = null;
                string content;
                var res = Provider == PromptWriterProvider.Gemini
                    ? ParseGemini(status, body, netError, out content)
                    : ParseOpenAI(status, body, netError, out content);
                // Busy, hanging, rate-limited or missing model: try the next fallback model instead of failing.
                if (!res.Ok && IsRetryable(status) && Fallbacks.Count > 0)
                {
                    Skipped.Add($"{Model} ({(status == 0 ? "no answer" : status.ToString())})");
                    Model = Fallbacks.Dequeue();
                    Web = Create(Provider, Model, Idea);
                    Web.SendWebRequest();
                    return;
                }
                if (res.Ok && !Revised)
                {
                    string problems = Problems(res.Spec, Idea);
                    if (problems != null)
                    {
                        // One fix-up round: show the model its answer and what is wrong with it.
                        Revised = true;
                        var history = new List<KeyValuePair<string, string>>
                        {
                            new KeyValuePair<string, string>("assistant", content),
                            new KeyValuePair<string, string>("user", "Fix these problems and answer again with the full JSON: " + problems)
                        };
                        Web = Create(Provider, Model, Idea, history);
                        Web.SendWebRequest();
                        return;
                    }
                }
                Finished = true;
                EditorApplication.update -= Poll;
                res.Seconds = Elapsed;
                res.Revised = Revised;
                res.Source = $"{Provider} {Model}";
                res.Skipped = Skipped.Count > 0 ? string.Join(", ", Skipped) : null;
                if (!res.Ok && Skipped.Count > 0) res.Error += $" (also tried: {res.Skipped})";
                Done?.Invoke(res);
            }
        }

        private static UnityWebRequest Create(PromptWriterProvider provider, string model, PromptIdea idea, IList<KeyValuePair<string, string>> history = null)
        {
            bool gemini = provider == PromptWriterProvider.Gemini;
            string body = gemini ? BuildGeminiBody(idea, history) : BuildOpenAIBody(idea, model, history);
            var web = new UnityWebRequest(gemini ? GeminiUrl(model) : Endpoint, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)) { contentType = "application/json" },
                downloadHandler = new DownloadHandlerBuffer(),
                // Flash-Lite answers in ~2 s; a model that is silent for this long is overloaded, so move on to a fallback.
                timeout = gemini ? GeminiTimeoutSeconds : 90
            };
            web.SetRequestHeader("Content-Type", "application/json");
            if (gemini) web.SetRequestHeader("x-goog-api-key", AudioStudioSettings.GeminiKey);
            else web.SetRequestHeader("Authorization", "Bearer " + AudioStudioSettings.OpenAIKey);
            return web;
        }

        /// <summary>
        /// Verifies a Gemini key by listing one model (no generation, no quota cost worth mentioning).
        /// Calls <paramref name="done"/>(ok, message) on the main thread.
        /// </summary>
        public static void CheckGeminiKey(string key, Action<bool, string> done)
        {
            var web = UnityWebRequest.Get(GeminiEndpoint.TrimEnd('/') + "?pageSize=1");
            web.timeout = 20;
            web.SetRequestHeader("x-goog-api-key", key ?? "");
            web.SendWebRequest();
            void Poll()
            {
                if (!web.isDone) return;
                EditorApplication.update -= Poll;
                long status = web.responseCode;
                string body = web.downloadHandler?.text;
                string netError = web.error;
                web.Dispose();
                if (status == 200) done(true, null);
                else done(false, ParseGemini(status, body, netError, out _).Error);
            }
            EditorApplication.update += Poll;
        }

        public static Handle Send(PromptIdea idea, Action<PromptResult> done)
        {
            var provider = AudioStudioSettings.PromptWriter;
            string model = AudioStudioSettings.PromptWriterModel;
            var h = new Handle { Started = EditorApplication.timeSinceStartup, Done = done, Idea = idea, Model = model, Provider = provider };
            if (provider == PromptWriterProvider.Gemini)
            {
                var chain = GeminiModelChain(model);
                h.Model = chain[0];
                for (int i = 1; i < chain.Count; i++) h.Fallbacks.Enqueue(chain[i]);
            }
            if (!AudioStudioSettings.RequirePromptWriterKey())
            {
                h.Finished = true;
                done(new PromptResult { Error = provider == PromptWriterProvider.Gemini
                    ? "No Gemini API key. Get a free one at aistudio.google.com and save it in Preferences ▸ Audio Creation Studio."
                    : "No OpenAI API key. Set it in Preferences ▸ Audio Creation Studio." });
                return h;
            }
            if (string.IsNullOrWhiteSpace(idea.Description))
            {
                h.Finished = true;
                done(new PromptResult { Error = "Describe the sound you need first." });
                return h;
            }
            h.Web = Create(provider, h.Model, idea);
            h.Web.SendWebRequest();
            EditorApplication.update += h.Poll;
            return h;
        }
    }
}
