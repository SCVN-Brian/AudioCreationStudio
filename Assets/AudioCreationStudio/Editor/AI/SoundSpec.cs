using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;

namespace AudioCreationStudio.EditorUI
{
    [Serializable]
    public sealed class RhythmSpec
    {
        public string pattern = "";
        public int repetitions;
        public string spacing = "";
    }

    [Serializable]
    public sealed class DurationSpec
    {
        /// <summary>Total length in seconds (0.5–30), or 0 to let ElevenLabs decide.</summary>
        public float seconds;
        public string attack = "";
        public string sustain = "";
        public string release = "";
    }

    /// <summary>
    /// The nine sound parameters Gemini extracts from a description (all values in English), plus the ElevenLabs
    /// settings it recommends. Users review and override every field before the prompt is built from it.
    /// Field names match the JSON the model returns.
    /// </summary>
    [Serializable]
    public sealed class SoundSpec
    {
        public string soundSource = "";
        public string action = "";
        public string[] texture = new string[0];
        public string pitch = "";
        public RhythmSpec rhythm = new RhythmSpec();
        public string intensity = "";
        public string style = "";
        public DurationSpec duration = new DurationSpec();
        public string[] constraints = new string[0];

        // ElevenLabs settings and messages for the user
        public bool loop;
        public float promptInfluence = 0.3f;
        /// <summary>Short explanation in the user's language.</summary>
        public string explanation = "";
        /// <summary>Anything in the request that cannot be met, in the user's language (empty when fine).</summary>
        public string warning = "";

        public float? Duration => duration != null && duration.seconds > 0 ? duration.seconds : (float?)null;

        public SoundSpec Clone() => JsonUtility.FromJson<SoundSpec>(JsonUtility.ToJson(this));

        /// <summary>Fills nulls, tidies text and clamps numbers to what ElevenLabs accepts.</summary>
        public SoundSpec Normalize()
        {
            soundSource = Clean(soundSource);
            action = Clean(action);
            texture = CleanList(texture);
            pitch = Clean(pitch);
            rhythm = rhythm ?? new RhythmSpec();
            rhythm.pattern = Clean(rhythm.pattern);
            rhythm.spacing = Clean(rhythm.spacing);
            rhythm.repetitions = Mathf.Clamp(rhythm.repetitions, 0, 50);
            intensity = Clean(intensity);
            style = Clean(style);
            duration = duration ?? new DurationSpec();
            duration.attack = Clean(duration.attack);
            duration.sustain = Clean(duration.sustain);
            duration.release = Clean(duration.release);
            duration.seconds = float.IsNaN(duration.seconds) || duration.seconds <= 0 ? 0f
                : Mathf.Clamp(duration.seconds, ElevenLabsClient.MinDuration, ElevenLabsClient.MaxDuration);
            constraints = CleanList(constraints);
            promptInfluence = float.IsNaN(promptInfluence) ? 0.3f : Mathf.Clamp01(promptInfluence);
            explanation = (explanation ?? "").Trim();
            warning = (warning ?? "").Trim();
            return this;
        }

        internal static string Clean(string s)
        {
            s = (s ?? "").Replace("\r", " ").Replace("\n", " ").Replace("\"", "").Replace("“", "").Replace("”", "");
            return Regex.Replace(s, @"\s+", " ").Trim().Trim(',', ';').TrimEnd('.').Trim();
        }

        internal static string[] CleanList(IEnumerable<string> items)
        {
            var list = new List<string>();
            if (items != null)
                foreach (var raw in items)
                {
                    var c = Clean(raw);
                    if (c.Length > 0 && !list.Exists(x => string.Equals(x, c, StringComparison.OrdinalIgnoreCase))) list.Add(c);
                }
            return list.ToArray();
        }

        /// <summary>"wet, bubbly; muffled" → ["wet", "bubbly", "muffled"].</summary>
        public static string[] SplitList(string text) => CleanList((text ?? "").Split(',', ';'));

        public static string JoinList(IEnumerable<string> items) => items == null ? "" : string.Join(", ", items);
    }

    /// <summary>
    /// Builds the English ElevenLabs prompt from a <see cref="SoundSpec"/> without another AI call, so every
    /// override the user makes is reflected exactly. Order: intensity + source + action, texture, pitch, rhythm,
    /// envelope, style, constraints. When the text is too long, the least important parts are dropped first.
    /// </summary>
    public static class PromptBuilder
    {
        private sealed class Part
        {
            public string Text;
            /// <summary>Higher = dropped earlier when the prompt is too long.</summary>
            public int Drop;
        }

        public static string Build(SoundSpec spec, int maxChars = PromptWriter.MaxPromptChars)
        {
            if (spec == null) return "";
            var s = spec.Clone().Normalize();
            var parts = new List<Part>();
            void Add(string text, int drop)
            {
                text = SoundSpec.Clean(text);
                if (text.Length == 0) return;
                foreach (var p in parts)
                    if (Contains(p.Text, text)) return; // already said
                parts.Add(new Part { Text = text, Drop = drop });
            }

            // Core: "Puppy barking" (verb-ing action) or "Raindrops on a tin roof, steady rainfall" (noun phrase).
            // Intensity leads only when it says something ("Aggressive puppy barking"); "moderate" is the default.
            string core = s.soundSource;
            if (s.action.Length > 0)
            {
                if (core.Length == 0 || Contains(s.action, s.soundSource)) core = s.action;
                else core = StartsWithIng(s.action) ? $"{core} {LowerFirst(s.action)}" : $"{core}, {LowerFirst(s.action)}";
            }
            if (s.intensity.Length > 0 && core.Length > 0 && !IsDefaultIntensity(s.intensity) && !Contains(core, s.intensity))
                core = $"{s.intensity} {LowerFirst(core)}";
            Add(UpperFirst(core), 0);

            for (int i = 0; i < s.texture.Length; i++) Add(LowerFirst(s.texture[i]), i < 3 ? 2 : 6);
            if (s.pitch.Length > 0) Add(Contains(s.pitch, "pitch") ? LowerFirst(s.pitch) : $"{LowerFirst(s.pitch)} pitch", 4);

            // Rhythm: "three short gurgles with short pauses"
            string rhythm = LowerFirst(s.rhythm.pattern);
            if (s.rhythm.repetitions > 1 && !HasCount(rhythm))
                rhythm = rhythm.Length > 0 ? $"{CountWord(s.rhythm.repetitions)} times, {rhythm}" : $"{CountWord(s.rhythm.repetitions)} times";
            if (s.rhythm.spacing.Length > 0 && !Contains(rhythm, s.rhythm.spacing))
                rhythm = rhythm.Length > 0 ? $"{rhythm} with {LowerFirst(s.rhythm.spacing)}" : LowerFirst(s.rhythm.spacing);
            Add(rhythm, 1);

            // Envelope (skipped for loops, which have no start or end; sustain skipped when it repeats the rhythm)
            if (s.loop) { s.duration.attack = ""; s.duration.release = ""; }
            if (Overlaps(s.duration.sustain, s.rhythm.pattern)) s.duration.sustain = "";
            if (s.duration.attack.Length > 0) Add(Contains(s.duration.attack, "attack") || Contains(s.duration.attack, "fade") ? LowerFirst(s.duration.attack) : $"{LowerFirst(s.duration.attack)} attack", 5);
            if (s.duration.sustain.Length > 0) Add(LowerFirst(s.duration.sustain), 8);
            if (s.duration.release.Length > 0) Add(Regex.IsMatch(s.duration.release, "decay|release|fade|tail|stop|end|ring|cut|loop|transition|silence", RegexOptions.IgnoreCase) ? LowerFirst(s.duration.release) : $"{LowerFirst(s.duration.release)} decay", 5);

            if (s.style.Length > 0) Add(Contains(s.style, "style") || Contains(s.style, "sound") ? LowerFirst(s.style) : $"{LowerFirst(s.style)} style", 3);

            for (int i = 0; i < s.constraints.Length; i++) Add(Negate(s.constraints[i]), i < 2 ? 7 : 9);

            string text = Join(parts);
            while (text.Length > maxChars && parts.Count > 1)
            {
                int worst = 1;
                for (int i = 1; i < parts.Count; i++)
                    if (parts[i].Drop >= parts[worst].Drop) worst = i;
                parts.RemoveAt(worst);
                text = Join(parts);
            }
            if (text.Length > maxChars)
            {
                int cut = text.LastIndexOf(' ', maxChars);
                text = text.Substring(0, cut > maxChars / 2 ? cut : maxChars).TrimEnd(',', ' ');
            }
            return text;
        }

        /// <summary>"music" / "No music" / "without reverb" → "no music" / "no music" / "without reverb".</summary>
        public static string Negate(string c)
        {
            c = SoundSpec.Clean(c);
            if (c.Length == 0) return c;
            string lower = c.ToLowerInvariant();
            if (lower.StartsWith("no ") || lower.StartsWith("without ") || lower.StartsWith("not ") || lower == "isolated" || lower.StartsWith("isolated") || lower.StartsWith("dry"))
                return LowerFirst(c);
            return "no " + LowerFirst(c);
        }

        private static string Join(List<Part> parts)
        {
            var list = new List<string>();
            foreach (var p in parts) list.Add(p.Text);
            return string.Join(", ", list);
        }

        private static bool StartsWithIng(string s)
        {
            var m = Regex.Match(s ?? "", @"^\s*([A-Za-z-]+)");
            return m.Success && m.Groups[1].Value.EndsWith("ing", StringComparison.OrdinalIgnoreCase) && m.Groups[1].Value.Length > 4;
        }

        private static bool IsDefaultIntensity(string s) =>
            Regex.IsMatch(s ?? "", @"^\s*(moderate|medium|normal|average)\s*$", RegexOptions.IgnoreCase);

        private static readonly HashSet<string> StopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "a", "an", "the", "of", "and", "with", "in", "on", "to", "each", "short", "long", "distinct", "sound", "sounds" };

        /// <summary>True when most content words of <paramref name="detail"/> already appear in <paramref name="main"/>.</summary>
        private static bool Overlaps(string detail, string main)
        {
            if (string.IsNullOrWhiteSpace(detail) || string.IsNullOrWhiteSpace(main)) return false;
            var mainWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in Regex.Matches(main, @"[A-Za-z0-9-]+")) mainWords.Add(Stem(m.Value));
            int total = 0, shared = 0;
            foreach (Match m in Regex.Matches(detail, @"[A-Za-z0-9-]+"))
            {
                if (StopWords.Contains(m.Value)) continue;
                total++;
                if (mainWords.Contains(Stem(m.Value))) shared++;
            }
            return total > 0 && shared * 2 >= total;
        }

        private static string Stem(string w)
        {
            w = w.ToLowerInvariant();
            if (w.EndsWith("ing") && w.Length > 5) w = w.Substring(0, w.Length - 3);
            else if (w.EndsWith("es") && w.Length > 4) w = w.Substring(0, w.Length - 2);
            else if (w.EndsWith("s") && w.Length > 3) w = w.Substring(0, w.Length - 1);
            return w;
        }

        private static bool Contains(string haystack, string needle) =>
            !string.IsNullOrEmpty(haystack) && !string.IsNullOrEmpty(needle) &&
            haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

        private static readonly Regex CountRx = new Regex(@"\b(\d+|one|two|three|four|five|six|seven|eight|nine|ten|once|twice|single|double|triple|several|multiple|repeated|rapid)\b", RegexOptions.IgnoreCase);
        private static bool HasCount(string s) => CountRx.IsMatch(s ?? "");

        private static string CountWord(int n)
        {
            string[] words = { "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten" };
            return n >= 0 && n < words.Length ? words[n] : n.ToString(CultureInfo.InvariantCulture);
        }

        private static string LowerFirst(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            // Keep acronyms and proper nouns ("UI", "8-bit", "NPC") as written.
            if (s.Length > 1 && char.IsUpper(s[0]) && char.IsUpper(s[1])) return s;
            return char.ToLowerInvariant(s[0]) + s.Substring(1);
        }

        private static string UpperFirst(string s) => string.IsNullOrEmpty(s) ? "" : char.ToUpperInvariant(s[0]) + s.Substring(1);
    }
}
