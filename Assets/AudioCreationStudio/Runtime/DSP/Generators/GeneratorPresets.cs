using System;
using System.Collections.Generic;
using AudioCreationStudio.Models;

namespace AudioCreationStudio.DSP
{
    /// <summary>Factory presets for the procedural generator. Each call returns a fresh editable copy.</summary>
    public static class GeneratorPresets
    {
        public sealed class Entry
        {
            public string Name;
            public string Category;
            public Func<GeneratorSettings> Create;
        }

        public static readonly IReadOnlyList<Entry> All = new List<Entry>
        {
            new Entry { Name = "Coin Pickup", Category = "Pickups", Create = CoinPickup },
            new Entry { Name = "UI Click", Category = "UI", Create = UiClick },
            new Entry { Name = "Pop", Category = "UI", Create = Pop },
            new Entry { Name = "Notification", Category = "UI", Create = Notification },
            new Entry { Name = "Whoosh", Category = "Movement", Create = Whoosh },
            new Entry { Name = "Impact", Category = "Impacts", Create = Impact },
            new Entry { Name = "Upgrade", Category = "Pickups", Create = Upgrade },
            new Entry { Name = "Cartoon Jump", Category = "Movement", Create = CartoonJump },
            new Entry { Name = "Machine Start", Category = "Machines", Create = MachineStart },
            new Entry { Name = "Machine Running", Category = "Machines", Create = MachineRunning },
            new Entry { Name = "Machine Stop", Category = "Machines", Create = MachineStop },
        };

        public static GeneratorSettings Create(string name)
        {
            foreach (var e in All) if (e.Name == name) return e.Create();
            return new GeneratorSettings();
        }

        public static GeneratorSettings CoinPickup() => new GeneratorSettings
        {
            presetName = "Coin Pickup", waveform = OscillatorWaveform.Square, dutyCycle = 0.5f, frequency = 987.77f,
            durationSeconds = 0.42f, volumeDb = -12f, oneShot = false,
            attack = 0.002f, decay = 0.06f, sustain = 0.6f, release = 0.28f,
            pitchJump = 5f, pitchJumpTime = 0.075f,
            filterType = GeneratorFilterType.LowPass, filterCutoff = 7000f, filterResonance = 0.7f
        };

        public static GeneratorSettings UiClick() => new GeneratorSettings
        {
            presetName = "UI Click", waveform = OscillatorWaveform.Triangle, frequency = 1800f,
            durationSeconds = 0.06f, volumeDb = -6f, oneShot = true,
            attack = 0.0005f, decay = 0.025f, sustain = 0f, release = 0.02f,
            pitchStart = 12f, pitchEnd = 0f, pitchSweepTime = 0.012f,
            noiseMix = 0.25f, filterType = GeneratorFilterType.HighPass, filterCutoff = 700f
        };

        public static GeneratorSettings Pop() => new GeneratorSettings
        {
            presetName = "Pop", waveform = OscillatorWaveform.Sine, frequency = 520f,
            durationSeconds = 0.14f, volumeDb = -4f, oneShot = true,
            attack = 0.001f, decay = 0.06f, sustain = 0f, release = 0.06f,
            pitchStart = -12f, pitchEnd = 7f, pitchSweepTime = 0.06f, pitchCurve = 0.6f
        };

        public static GeneratorSettings Notification() => new GeneratorSettings
        {
            presetName = "Notification", waveform = OscillatorWaveform.Sine, frequency = 880f,
            durationSeconds = 0.65f, volumeDb = -8f, oneShot = false,
            attack = 0.003f, decay = 0.12f, sustain = 0.35f, release = 0.4f,
            pitchJump = 7f, pitchJumpTime = 0.13f, subOscMix = 0.15f
        };

        public static GeneratorSettings Whoosh() => new GeneratorSettings
        {
            presetName = "Whoosh", waveform = OscillatorWaveform.PinkNoise, durationSeconds = 0.9f, volumeDb = 0f,
            oneShot = false, attack = 0.35f, decay = 0.2f, sustain = 0.5f, release = 0.35f,
            filterType = GeneratorFilterType.BandPass, filterCutoff = 450f, filterResonance = 1.4f, filterEnvOctaves = 3f
        };

        public static GeneratorSettings Impact() => new GeneratorSettings
        {
            presetName = "Impact", waveform = OscillatorWaveform.Sine, frequency = 65f, subOscMix = 0.6f,
            durationSeconds = 0.8f, volumeDb = -4f, oneShot = true,
            attack = 0.001f, decay = 0.25f, sustain = 0.15f, release = 0.45f,
            pitchStart = 14f, pitchEnd = 0f, pitchSweepTime = 0.09f, pitchCurve = 0.5f,
            noiseMix = 0.4f, noiseColor = NoiseColor.Pink,
            filterType = GeneratorFilterType.LowPass, filterCutoff = 1800f, filterEnvOctaves = 2f, drive = 0.35f
        };

        public static GeneratorSettings Upgrade() => new GeneratorSettings
        {
            presetName = "Upgrade", waveform = OscillatorWaveform.Square, dutyCycle = 0.25f, frequency = 523.25f,
            durationSeconds = 0.75f, volumeDb = -13f, oneShot = false,
            attack = 0.005f, decay = 0.1f, sustain = 0.65f, release = 0.3f,
            pitchStart = 0f, pitchEnd = 12f, pitchSweepTime = 0.5f, pitchCurve = 1f,
            vibratoRate = 7f, vibratoDepth = 0.25f,
            filterType = GeneratorFilterType.LowPass, filterCutoff = 5000f
        };

        public static GeneratorSettings CartoonJump() => new GeneratorSettings
        {
            presetName = "Cartoon Jump", waveform = OscillatorWaveform.Triangle, frequency = 300f,
            durationSeconds = 0.38f, volumeDb = -5f, oneShot = false,
            attack = 0.002f, decay = 0.1f, sustain = 0.5f, release = 0.2f,
            pitchStart = -5f, pitchEnd = 14f, pitchSweepTime = 0.26f, pitchCurve = 0.6f,
            vibratoRate = 14f, vibratoDepth = 0.6f
        };

        public static GeneratorSettings MachineStart() => new GeneratorSettings
        {
            presetName = "Machine Start", waveform = OscillatorWaveform.Sawtooth, frequency = 45f,
            durationSeconds = 2f, volumeDb = -7f, oneShot = false,
            attack = 0.6f, decay = 0.3f, sustain = 0.85f, release = 0.25f,
            pitchStart = -12f, pitchEnd = 0f, pitchSweepTime = 1.6f, pitchCurve = 0.7f,
            tremoloRate = 18f, tremoloDepth = 0.35f, noiseMix = 0.2f, noiseColor = NoiseColor.Pink,
            filterType = GeneratorFilterType.LowPass, filterCutoff = 900f, filterEnvOctaves = 1f, drive = 0.3f
        };

        public static GeneratorSettings MachineRunning() => new GeneratorSettings
        {
            presetName = "Machine Running", waveform = OscillatorWaveform.Sawtooth, frequency = 45f,
            durationSeconds = 2f, volumeDb = -7f, oneShot = false,
            attack = 0.01f, decay = 0f, sustain = 1f, release = 0.01f,
            tremoloRate = 18f, tremoloDepth = 0.35f, noiseMix = 0.2f, noiseColor = NoiseColor.Pink, noiseFm = 0.3f,
            filterType = GeneratorFilterType.LowPass, filterCutoff = 1100f, drive = 0.3f
        };

        public static GeneratorSettings MachineStop() => new GeneratorSettings
        {
            presetName = "Machine Stop", waveform = OscillatorWaveform.Sawtooth, frequency = 45f,
            durationSeconds = 1.8f, volumeDb = -7f, oneShot = false,
            attack = 0.005f, decay = 0.2f, sustain = 0.85f, release = 1.0f,
            pitchStart = 0f, pitchEnd = -14f, pitchSweepTime = 1.6f, pitchCurve = 1.4f,
            tremoloRate = 18f, tremoloDepth = 0.35f, noiseMix = 0.2f, noiseColor = NoiseColor.Pink,
            filterType = GeneratorFilterType.LowPass, filterCutoff = 1100f, filterEnvOctaves = 1f, drive = 0.3f
        };
    }

    /// <summary>Deterministic randomization of generator settings around their current values.</summary>
    public static class GeneratorRandomizer
    {
        public static GeneratorSettings Randomize(GeneratorSettings source, int seed, float amount = 0.35f)
        {
            var r = new DeterministicRandom(seed);
            var s = source.Clone();
            amount = DspMath.Clamp(amount, 0f, 1f);
            float Jitter(float v, float rel) => v * (1f + r.Range(-rel, rel) * amount);

            s.frequency = DspMath.Clamp(s.frequency * (float)Math.Pow(2.0, r.Range(-1f, 1f) * amount), 20f, 12000f);
            s.durationSeconds = DspMath.Clamp(Jitter(s.durationSeconds, 0.5f), 0.02f, ProceduralSynth.MaxDurationSeconds);
            s.attack = DspMath.Clamp(Jitter(s.attack, 0.8f), 0.0005f, 2f);
            s.decay = DspMath.Clamp(Jitter(s.decay, 0.8f), 0f, 2f);
            s.sustain = DspMath.Clamp(s.sustain + r.Range(-0.3f, 0.3f) * amount, 0f, 1f);
            s.release = DspMath.Clamp(Jitter(s.release, 0.8f), 0.001f, 3f);
            s.pitchStart += r.Range(-6f, 6f) * amount;
            s.pitchEnd += r.Range(-6f, 6f) * amount;
            s.pitchSweepTime = DspMath.Clamp(Jitter(s.pitchSweepTime, 0.6f), 0.005f, 5f);
            if (s.pitchJump != 0) s.pitchJump = (float)Math.Round(s.pitchJump + r.Range(-3f, 3f) * amount);
            s.dutyCycle = DspMath.Clamp(s.dutyCycle + r.Range(-0.2f, 0.2f) * amount, 0.05f, 0.95f);
            s.noiseMix = DspMath.Clamp(s.noiseMix + r.Range(-0.2f, 0.2f) * amount, 0f, 1f);
            if (s.filterType != GeneratorFilterType.None)
                s.filterCutoff = DspMath.Clamp(s.filterCutoff * (float)Math.Pow(2.0, r.Range(-1.5f, 1.5f) * amount), 40f, 18000f);
            s.seed = DeterministicRandom.DeriveSeed(seed, 7);
            s.presetName = (source.presetName ?? "Custom").Replace(" (Random)", "") + " (Random)";
            return s;
        }
    }
}
