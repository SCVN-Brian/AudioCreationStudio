using AudioCreationStudio.EditorUI;
using NUnit.Framework;

namespace AudioCreationStudio.Tests
{
    /// <summary>Offline tests for the AI sound analyzer (request bodies, typed SoundSpec parsing, ElevenLabs limits) and the prompt builder. No network.</summary>
    public class PromptWriterTests
    {
        /// <summary>The example from the feature spec (Vietnamese description → structured parameters).</summary>
        private const string ExampleSpec =
            "{\"soundSource\":\"Human voice\",\"action\":\"Underwater gurgling\",\"texture\":[\"Muffled\",\"Wet\",\"Bubbly\"],\"pitch\":\"Medium\"," +
            "\"rhythm\":{\"pattern\":\"Three short gurgles\",\"repetitions\":3,\"spacing\":\"Short pauses\"},\"intensity\":\"Moderate\",\"style\":\"Cartoon casual game\"," +
            "\"duration\":{\"seconds\":2.0,\"attack\":\"Immediate\",\"sustain\":\"Three distinct vocal bursts\",\"release\":\"Short natural decay\"}," +
            "\"constraints\":[\"No music\",\"No background ambience\"],\"loop\":false,\"promptInfluence\":0.5,\"explanation\":\"Ba tiếng ùng ục.\",\"warning\":\"\"}";

        private static string ChatBody(string contentJson) =>
            "{\"id\":\"x\",\"choices\":[{\"index\":0,\"finish_reason\":\"stop\",\"message\":{\"role\":\"assistant\",\"content\":" + Json.Str(contentJson) + "}}]}";

        [Test]
        public void Body_UsesStrictJsonSchemaAndCarriesRequirements()
        {
            var idea = new PromptIdea { Description = "tiếng chó sủa \"gâu\"", FixedDuration = 2.5f, WantLoop = true, ReferenceSeconds = 1.2f, ReferenceHint = "2 separate hits" };
            string body = PromptWriter.BuildOpenAIBody(idea, "my-model");
            StringAssert.Contains("\"model\":\"my-model\"", body);
            StringAssert.Contains("\"type\":\"json_schema\"", body);
            StringAssert.Contains("\"strict\":true", body);
            StringAssert.Contains("\"additionalProperties\":false", body);
            StringAssert.Contains("\"soundSource\"", body);
            StringAssert.Contains("\"rhythm\":{\"type\":\"object\"", body, "nested typed objects");
            StringAssert.Contains("\"constraints\":{\"type\":\"array\"", body);
            StringAssert.Contains("\\\"gâu\\\"", body, "user text is JSON-escaped");
            string user = PromptWriter.BuildUserMessage(idea);
            StringAssert.Contains("2.5 seconds", user);
            StringAssert.Contains("seamless loop", user);
            StringAssert.Contains("1.20 s", user);
        }

        [Test]
        public void Parse_ReadsTypedSpec()
        {
            var r = PromptWriter.ParseOpenAI(200, ChatBody(ExampleSpec), null, out _);
            Assert.IsTrue(r.Ok, r.Error);
            var s = r.Spec;
            Assert.AreEqual("Human voice", s.soundSource);
            Assert.AreEqual("Underwater gurgling", s.action);
            CollectionAssert.AreEqual(new[] { "Muffled", "Wet", "Bubbly" }, s.texture);
            Assert.AreEqual(3, s.rhythm.repetitions);
            Assert.AreEqual("Short pauses", s.rhythm.spacing);
            Assert.AreEqual(2f, s.Duration);
            Assert.AreEqual("Short natural decay", s.duration.release);
            CollectionAssert.AreEqual(new[] { "No music", "No background ambience" }, s.constraints);
            Assert.AreEqual(0.5f, s.promptInfluence);
            Assert.AreEqual("Ba tiếng ùng ục.", s.explanation);
        }

        [Test]
        public void Normalize_ClampsAndTidies()
        {
            var s = new SoundSpec { soundSource = " \"Dog\"\n", action = "barking.", texture = new[] { "wet", "Wet", " ", "airy" }, promptInfluence = 1.4f };
            s.duration.seconds = 45f;
            s.rhythm.repetitions = -2;
            s.Normalize();
            Assert.AreEqual("Dog", s.soundSource);
            Assert.AreEqual("barking", s.action);
            CollectionAssert.AreEqual(new[] { "wet", "airy" }, s.texture, "deduplicated, blanks removed");
            Assert.AreEqual(30f, s.duration.seconds, "clamped to ElevenLabs' 30 s maximum");
            Assert.AreEqual(0, s.rhythm.repetitions);
            Assert.AreEqual(1f, s.promptInfluence);
            var auto = new SoundSpec().Normalize();
            Assert.IsNull(auto.Duration, "0 s means auto");
            var tiny = new SoundSpec(); tiny.duration.seconds = 0.1f;
            Assert.AreEqual(0.5f, tiny.Normalize().duration.seconds, "clamped to the 0.5 s minimum");
        }

        [Test]
        public void Problems_FlagRuleBreaksAndIgnoredRequirements()
        {
            var idea = new PromptIdea { Description = "x", FixedDuration = 3f, WantLoop = true };
            var bad = new SoundSpec { soundSource = "Chó", action = "sủa to", loop = false }.Normalize();
            string problems = PromptWriter.Problems(bad, idea);
            StringAssert.Contains("not in English", problems);
            StringAssert.Contains("3.0", problems);
            StringAssert.Contains("loop must be true", problems);
            StringAssert.Contains("action is empty", PromptWriter.Problems(new SoundSpec { soundSource = "Dog" }.Normalize(), new PromptIdea()));
            var good = new SoundSpec { soundSource = "Diesel engine", action = "Steady idle", loop = true, texture = new[] { "Rumbling" } };
            good.duration.seconds = 3f;
            Assert.IsNull(PromptWriter.Problems(good.Normalize(), idea));
        }

        // ------------------------------------------------------------------ Prompt builder

        [Test]
        public void Builder_TurnsSpecIntoCleanEnglishPrompt()
        {
            var spec = PromptWriter.ParseOpenAI(200, ChatBody(ExampleSpec), null, out _).Spec;
            string p = PromptBuilder.Build(spec);
            StringAssert.StartsWith("Human voice, underwater gurgling", p, "noun-phrase action joined with a comma; moderate intensity adds nothing");
            StringAssert.Contains("muffled, wet, bubbly", p);
            StringAssert.Contains("medium pitch", p);
            StringAssert.Contains("three short gurgles with short pauses", p);
            StringAssert.Contains("immediate attack", p);
            StringAssert.Contains("short natural decay", p);
            StringAssert.Contains("three distinct vocal bursts", p, "sustain with new information is kept");
            spec.rhythm.pattern = "Three distinct gurgling vocal bursts";
            spec.duration.sustain = "Three gurgling bubble sounds";
            StringAssert.DoesNotContain("bubble sounds", PromptBuilder.Build(spec), "sustain that repeats the rhythm is left out");
            StringAssert.Contains("cartoon casual game style", p);
            StringAssert.Contains("no music, no background ambience", p);
            Assert.IsNull(ElevenLabsPanel.CheckPrompt(p), "passes the studio's prompt rules");
            Assert.LessOrEqual(p.Length, PromptWriter.MaxPromptChars);
        }

        [Test]
        public void Builder_ReflectsOverridesAndCounts()
        {
            var s = new SoundSpec { soundSource = "Puppy", action = "Barking", pitch = "High pitch", constraints = new[] { "music", "Without reverb" } };
            s.rhythm.repetitions = 2;
            s.rhythm.spacing = "Even spacing";
            string p = PromptBuilder.Build(s);
            StringAssert.Contains("Puppy barking", p);
            StringAssert.Contains("two times with even spacing", p, "repetitions become words when the pattern has no count");
            StringAssert.Contains("high pitch", p);
            Assert.IsFalse(p.Contains("pitch pitch"));
            StringAssert.Contains("no music", p, "constraints are phrased as negatives");
            StringAssert.Contains("without reverb", p);
            s.rhythm.pattern = "Two quick yaps";
            StringAssert.DoesNotContain("two times", PromptBuilder.Build(s), "no duplicate count when the pattern already has one");
            s.intensity = "Aggressive";
            StringAssert.StartsWith("Aggressive puppy barking", PromptBuilder.Build(s));
        }

        [Test]
        public void Builder_DropsLeastImportantPartsWhenTooLong()
        {
            var s = new SoundSpec { soundSource = "Giant ancient stone golem", action = "Stomping across a cavern", texture = new[] { "Rocky", "Crunchy", "Heavy", "Dusty", "Rumbling" },
                pitch = "Very low", intensity = "Explosive", style = "Cinematic fantasy", constraints = new[] { "No music", "No dialogue", "No reverb", "No distortion" } };
            s.rhythm.pattern = "Four slow thunderous footsteps, each one shaking the ground";
            s.rhythm.spacing = "Long pauses";
            s.duration.attack = "Sharp";
            s.duration.sustain = "Deep rumbling resonance between each impact with falling debris and pebbles";
            s.duration.release = "Long tail";
            string full = PromptBuilder.Build(s, 1000);
            string p = PromptBuilder.Build(s, 160);
            Assert.Greater(full.Length, 160);
            Assert.LessOrEqual(p.Length, 160);
            StringAssert.StartsWith("Explosive giant ancient stone golem stomping across a cavern", p, "core is always kept (verb-ing action joined directly)");
            StringAssert.Contains("four slow thunderous footsteps", p, "rhythm outranks the sustain detail");
            StringAssert.DoesNotContain("falling debris", p, "sustain is dropped first");
        }

        [Test]
        public void Builder_LoopsHaveNoAttackOrRelease()
        {
            var s = new SoundSpec { soundSource = "Rain on a tin roof", action = "Steady rainfall", loop = true, intensity = "Moderate" };
            s.duration.attack = "Immediate";
            s.duration.release = "Seamless loop transition";
            string p = PromptBuilder.Build(s);
            Assert.AreEqual("Rain on a tin roof, steady rainfall", p);
        }

        [Test]
        public void SpecLists_SplitAndJoin()
        {
            CollectionAssert.AreEqual(new[] { "wet", "bubbly", "muffled" }, SoundSpec.SplitList(" wet, bubbly; muffled ,, "));
            Assert.AreEqual("a, b", SoundSpec.JoinList(new[] { "a", "b" }));
        }

        [Test]
        public void Parse_ExplainsErrors()
        {
            StringAssert.Contains("Invalid OpenAI API key", PromptWriter.ParseOpenAI(401, "{\"error\":{\"message\":\"bad key\"}}", null, out _).Error);
            StringAssert.Contains("model name", PromptWriter.ParseOpenAI(404, "{\"error\":{\"message\":\"no such model\"}}", null, out _).Error);
            StringAssert.Contains("no such param", PromptWriter.ParseOpenAI(400, "{\"error\":{\"message\":\"no such param\"}}", null, out _).Error);
            StringAssert.Contains("Network error", PromptWriter.ParseOpenAI(0, null, "timeout", out _).Error);
            string refusal = "{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"content\":null,\"refusal\":\"no\"}}]}";
            StringAssert.Contains("refused", PromptWriter.ParseOpenAI(200, refusal, null, out _).Error);
            Assert.IsFalse(PromptWriter.ParseOpenAI(200, ChatBody("not json"), null, out _).Ok);
        }

        // ------------------------------------------------------------------ Gemini

        private static string GeminiBody(string text, string finish = "STOP") =>
            "{\"candidates\":[{\"content\":{\"role\":\"model\",\"parts\":[{\"text\":" + Json.Str(text) + "}]},\"finishReason\":\"" + finish + "\"}]}";

        [Test]
        public void Gemini_BodyUsesSystemInstructionSchemaAndModelRole()
        {
            var idea = new PromptIdea { Description = "mèo kêu đói" };
            var history = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, string>>
            {
                new System.Collections.Generic.KeyValuePair<string, string>("assistant", "{}"),
                new System.Collections.Generic.KeyValuePair<string, string>("user", "fix it")
            };
            string body = PromptWriter.BuildGeminiBody(idea, history);
            StringAssert.Contains("\"system_instruction\"", body);
            StringAssert.Contains("\"responseMimeType\":\"application/json\"", body);
            StringAssert.Contains("\"responseSchema\"", body);
            StringAssert.Contains("\"type\":\"OBJECT\"", body);
            StringAssert.Contains("\"propertyOrdering\"", body);
            StringAssert.Contains("\"role\":\"model\"", body, "assistant turns use Gemini's model role");
            StringAssert.Contains("mèo kêu đói", body);
            Assert.AreEqual("https://generativelanguage.googleapis.com/v1beta/models/gemini-x:generateContent", PromptWriter.GeminiUrl("models/gemini-x"));
            StringAssert.EndsWith(AudioStudioSettings.DefaultGeminiModel + ":generateContent", PromptWriter.GeminiUrl(" "));
        }

        [Test]
        public void Gemini_ParsesSpecAndSkipsThoughtParts()
        {
            string body = "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"thinking...\",\"thought\":true},{\"text\":" + Json.Str(ExampleSpec) + "}]},\"finishReason\":\"STOP\"}]}";
            var r = PromptWriter.ParseGemini(200, body, null, out _);
            Assert.IsTrue(r.Ok, r.Error);
            Assert.AreEqual("Human voice", r.Spec.soundSource);
            Assert.AreEqual(2f, r.Spec.Duration);
            Assert.IsTrue(PromptWriter.ParseGemini(200, GeminiBody(ExampleSpec), null, out _).Ok);
        }

        [Test]
        public void Gemini_ExplainsErrors()
        {
            StringAssert.Contains("Invalid Gemini API key", PromptWriter.ParseGemini(400, "{\"error\":{\"code\":400,\"message\":\"API key not valid. Please pass a valid API key.\",\"status\":\"INVALID_ARGUMENT\"}}", null, out _).Error);
            StringAssert.Contains("free-tier limit", PromptWriter.ParseGemini(429, "{\"error\":{\"message\":\"quota\"}}", null, out _).Error);
            StringAssert.Contains("model not found", PromptWriter.ParseGemini(404, "{\"error\":{\"message\":\"models/x is not found\"}}", null, out _).Error);
            StringAssert.Contains("blocked", PromptWriter.ParseGemini(200, "{\"promptFeedback\":{\"blockReason\":\"SAFETY\"}}", null, out _).Error);
            StringAssert.Contains("cut off", PromptWriter.ParseGemini(200, GeminiBody("{", "MAX_TOKENS"), null, out _).Error);
            Assert.IsFalse(PromptWriter.ParseGemini(200, GeminiBody("not json"), null, out _).Ok);
        }

        [Test]
        public void KeyCleaning_AcceptsAnyFormatButRejectsJunk()
        {
            Assert.AreEqual("AQ.Ab8RN6abc_def-123456789", AudioStudioSettings.CleanKey("  \"AQ.Ab8RN6abc_def-123456789\"\n"));
            Assert.IsTrue(AudioStudioSettings.LooksLikeKey("AQ.Ab8RN6abc_def-123456789"), "non-AIza formats are accepted");
            Assert.IsTrue(AudioStudioSettings.LooksLikeKey("AIzaSyD-abcdefghijklmnopqrstuvwxyz012"));
            Assert.IsFalse(AudioStudioSettings.LooksLikeKey("short"));
            Assert.IsFalse(AudioStudioSettings.LooksLikeKey("AIzaSyD abcdefghijklmnopqrstuvwxyz"));
        }

        [Test]
        public void Gemini_ModelChainStartsWithChoiceAndFallsBackToFlashLite()
        {
            var chain = PromptWriter.GeminiModelChain("models/gemini-3.8-flash");
            Assert.AreEqual("gemini-3.8-flash", chain[0]);
            CollectionAssert.IsSubsetOf(PromptWriter.GeminiFallbacks, chain);
            Assert.AreEqual(chain.Count, new System.Collections.Generic.HashSet<string>(chain).Count, "no duplicates");
            Assert.AreEqual(PromptWriter.GeminiFallbacks.Length, PromptWriter.GeminiModelChain(PromptWriter.GeminiFallbacks[0]).Count);
            Assert.IsTrue(PromptWriter.IsRetryable(0) && PromptWriter.IsRetryable(503) && PromptWriter.IsRetryable(429) && PromptWriter.IsRetryable(404));
            Assert.IsFalse(PromptWriter.IsRetryable(400) || PromptWriter.IsRetryable(401) || PromptWriter.IsRetryable(200));
        }
    }
}
