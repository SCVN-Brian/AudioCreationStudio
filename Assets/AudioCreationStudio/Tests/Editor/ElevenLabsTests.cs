using System;
using System.IO;
using System.Text;
using AudioCreationStudio.DSP;
using AudioCreationStudio.EditorUI;
using AudioCreationStudio.Export;
using AudioCreationStudio.Models;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.Networking;
using static AudioCreationStudio.Tests.TestUtil;

namespace AudioCreationStudio.Tests
{
    /// <summary>Offline tests for the ElevenLabs sound-effects integration and the reference analyzer (no network, no key).</summary>
    public class ElevenLabsTests
    {
        private const string Temp = "Assets/AudioCreationStudio/Tests/_TempEL";

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(Temp)) AssetDatabase.DeleteAsset(Temp);
            if (Directory.Exists(Temp)) Directory.Delete(Temp, true);
        }

        [Test]
        public void Request_UsesDocumentedEndpointBodyAndFormat()
        {
            var r = new SfxRequest { Text = "dog \"woof\"\nclose mic", DurationSeconds = 45f, PromptInfluence = 1.7f, Loop = true };
            Assert.AreEqual("https://api.elevenlabs.io/v1/sound-generation?output_format=wav_48000", ElevenLabsClient.BuildUrl(r));
            string body = ElevenLabsClient.BuildBody(r);
            StringAssert.Contains("\"text\":\"dog \\\"woof\\\"\\nclose mic\"", body);
            StringAssert.Contains("\"model_id\":\"eleven_text_to_sound_v2\"", body);
            StringAssert.Contains("\"duration_seconds\":30", body, "clamped to the 0.5-30 s range");
            StringAssert.Contains("\"prompt_influence\":1", body, "clamped to 0-1");
            StringAssert.Contains("\"loop\":true", body);
        }

        [Test]
        public void Request_AutoDurationOmitsField()
        {
            string body = ElevenLabsClient.BuildBody(new SfxRequest { Text = "pop", DurationSeconds = null });
            Assert.IsFalse(body.Contains("duration_seconds"), "null lets ElevenLabs choose the duration");
            string tiny = ElevenLabsClient.BuildBody(new SfxRequest { Text = "pop", DurationSeconds = 0.1f });
            StringAssert.Contains("\"duration_seconds\":0.5", tiny);
        }

        [Test]
        public void ErrorParsing_ReadsBothErrorShapes()
        {
            var detailObj = Encoding.UTF8.GetBytes("{\"detail\":{\"status\":\"invalid_api_key\",\"message\":\"Invalid API key\"}}");
            var r401 = ElevenLabsClient.Parse(401, detailObj, UnityWebRequest.Result.ProtocolError, null, "wav_48000");
            Assert.IsFalse(r401.Ok);
            StringAssert.Contains("Invalid ElevenLabs API key", r401.Error);
            var detailList = Encoding.UTF8.GetBytes("{\"detail\":[{\"loc\":[\"body\",\"text\"],\"msg\":\"field required\",\"type\":\"value_error.missing\"}]}");
            var r422 = ElevenLabsClient.Parse(422, detailList, UnityWebRequest.Result.ProtocolError, null, "wav_48000");
            StringAssert.Contains("field required", r422.Error);
            Assert.AreEqual("Invalid API key", ElevenLabsClient.ErrorText(detailObj));
            StringAssert.Contains("429", ElevenLabsClient.Parse(429, null, UnityWebRequest.Result.ProtocolError, null, "x").Error);
            StringAssert.Contains("Network", ElevenLabsClient.Parse(0, null, UnityWebRequest.Result.ConnectionError, "Cannot resolve", "x").Error);
        }

        [Test]
        public void Success_ReturnsAudioBytes()
        {
            var wav = WavEncoder.Encode(Sine(440, 4800), WavSampleFormat.Pcm16);
            var r = ElevenLabsClient.Parse(200, wav, UnityWebRequest.Result.Success, null, "wav_48000");
            Assert.IsTrue(r.Ok);
            Assert.AreEqual(wav.Length, r.Audio.Length);
            Assert.IsFalse(ElevenLabsClient.Parse(200, new byte[3], UnityWebRequest.Result.Success, null, "wav_48000").Ok);
        }

        [Test]
        public void SaveAudio_WritesValidWav_AndWrapsRawPcm()
        {
            var src = Sine(440, 4800, 0.5f);
            string p1 = ElevenLabsClient.SaveAudio(WavEncoder.Encode(src, WavSampleFormat.Pcm16), "wav_48000", Temp, "dog bark: twice?");
            StringAssert.EndsWith(".wav", p1);
            Assert.IsFalse(Path.GetFileName(p1).Contains(":") || Path.GetFileName(p1).Contains("?"), "file name sanitized");
            var back = WavDecoder.Decode(p1, out var info);
            Assert.AreEqual(4800, back.Frames);
            // Raw 16-bit PCM (pcm_24000) gets a WAV header at the right rate.
            var raw = new byte[2400 * 2];
            for (int i = 0; i < 2400; i++) { short s = (short)(Math.Sin(i * 0.1) * 10000); raw[i * 2] = (byte)s; raw[i * 2 + 1] = (byte)(s >> 8); }
            string p2 = ElevenLabsClient.SaveAudio(raw, "pcm_24000", Temp, "raw");
            var b2 = WavDecoder.Decode(p2, out var i2);
            Assert.AreEqual(24000, i2.SampleRate);
            Assert.AreEqual(2400, b2.Frames);
            Assert.AreEqual(Math.Sin(10 * 0.1) * 10000 / 32768.0, b2[0, 10], 1.0 / 32768);
            // MP3 is stored as-is.
            string p3 = ElevenLabsClient.SaveAudio(new byte[] { 0xFF, 0xFB, 0x90, 0x00 }, "mp3_44100_128", Temp, "mp3");
            StringAssert.EndsWith(".mp3", p3);
        }

        [Test]
        public void SaveAudio_RejectsCorruptWav()
        {
            var bad = Encoding.ASCII.GetBytes("RIFF\0\0\0\0WAVEjunkjunkjunk");
            Assert.Throws<InvalidDataException>(() => ElevenLabsClient.SaveAudio(bad, "wav_48000", Temp, "bad"));
        }

        [Test]
        public void ReferenceHint_DescribesTimingAndCharacter()
        {
            var b = new AudioBuffer(1, SR, SR);
            void Burst(int start) { for (int i = 0; i < 9600; i++) b[0, start + i] = (float)(0.6 * Math.Sin(2 * Math.PI * 400 * i / SR) * Math.Exp(-i / (0.04 * SR))); }
            Burst(2400);
            Burst(16800);
            string hint = ElevenLabsPanel.ReferenceHint(SoundAnalyzer.Analyze(b));
            StringAssert.Contains("2 separate hits", hint);
            StringAssert.Contains("tonal", hint);
        }

        [Test]
        public void PromptCheck_FlagsNonEnglishInstructionsAndOnomatopoeia()
        {
            StringAssert.Contains("not in English", ElevenLabsPanel.CheckPrompt("Tạo tiếng chó sủa gâu gâu"));
            StringAssert.Contains("not in English", ElevenLabsPanel.CheckPrompt("chó sủa"));
            StringAssert.Contains("instruction", ElevenLabsPanel.CheckPrompt("make a dog bark"));
            StringAssert.Contains("onomatopoeia", ElevenLabsPanel.CheckPrompt("dog going woof woof"));
            Assert.IsNull(ElevenLabsPanel.CheckPrompt("a medium-sized dog barking twice, close up, outdoors"));
            Assert.IsNull(ElevenLabsPanel.CheckPrompt("café ambience with espresso machine"), "a single accented word in English is fine");
            Assert.IsNull(ElevenLabsPanel.CheckPrompt(""));
        }

        // ---- Analyzer (used for reference hints and analysis)

        [Test]
        public void Analyzer_MeasuresDurationPitchAndTonality()
        {
            var a = SoundAnalyzer.Analyze(Sine(440, 24000, 0.5f));
            Assert.AreEqual(0.5, a.DurationSeconds, 1e-9);
            Assert.AreEqual(-6.02f, a.PeakDb, 0.05f);
            Assert.AreEqual(440f, a.MedianPitchHz, 4.4f);
            Assert.Greater(a.VoicedRatio, 0.8f);
            Assert.Less(a.AvgFlatness, 0.1f);
        }

        [Test]
        public void Analyzer_NoiseIsUnpitchedAndFlat()
        {
            var noise = new AudioBuffer(1, 24000, SR);
            var r = new DeterministicRandom(3);
            for (int i = 0; i < noise.Frames; i++) noise[0, i] = r.NextSigned() * 0.5f;
            var a = SoundAnalyzer.Analyze(noise);
            Assert.Less(a.VoicedRatio, 0.2f);
            Assert.Greater(a.AvgFlatness, 0.4f);
            Assert.Greater(a.AvgCentroidHz, 5000f);
        }

        [Test]
        public void Analyzer_TracksFallingPitch()
        {
            var b = new AudioBuffer(1, SR / 2, SR);
            double ph = 0;
            for (int i = 0; i < b.Frames; i++) { ph += (600 - 300 * i / (double)b.Frames) / SR; b[0, i] = (float)(0.5 * Math.Sin(2 * Math.PI * ph)); }
            var a = SoundAnalyzer.Analyze(b);
            float first = 0, last = 0;
            foreach (var p in a.PitchHz) if (p > 0) { first = p; break; }
            for (int i = a.PitchHz.Length - 1; i >= 0; i--) if (a.PitchHz[i] > 0) { last = a.PitchHz[i]; break; }
            Assert.Greater(first, 520f);
            Assert.Less(last, 360f);
        }

        [Test]
        public void Fft_RoundTrips()
        {
            var re = new double[64]; var im = new double[64];
            for (int i = 0; i < 64; i++) re[i] = Math.Sin(i * 0.3) + 0.1 * i;
            var orig = (double[])re.Clone();
            SoundAnalyzer.FftInPlace(re, im, 64, false);
            SoundAnalyzer.FftInPlace(re, im, 64, true);
            for (int i = 0; i < 64; i++) Assert.AreEqual(orig[i], re[i], 1e-9);
        }
    }
}
