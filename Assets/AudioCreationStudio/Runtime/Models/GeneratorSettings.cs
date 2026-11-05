using System;

namespace AudioCreationStudio.Models
{
    /// <summary>
    /// Complete parameter set of the procedural SFX synthesizer. Every field is a value type so the
    /// settings can be cloned with MemberwiseClone and hashed deterministically.
    /// </summary>
    [Serializable]
    public class GeneratorSettings
    {
        public string presetName = "Custom";
        public int seed = 1;

        // Oscillator
        public OscillatorWaveform waveform = OscillatorWaveform.Sine;
        public float frequency = 440f;
        public float dutyCycle = 0.5f;
        public float subOscMix;
        public float durationSeconds = 0.5f;
        public float volumeDb = -6f;

        // Sample source (waveform == Sample): an AudioClip asset referenced by GUID, read from sampleStart seconds.
        // Pitch settings transpose it; envelope, filter, noise, tremolo and drive process it like the oscillator.
        public string sampleGuid = "";
        public string sampleName = "";
        public float sampleStart;

        // Amplitude envelope (ADSR, seconds / level)
        public bool oneShot = true;
        public float attack = 0.005f;
        public float decay = 0.12f;
        public float sustain = 0.5f;
        public float release = 0.2f;

        // Pitch envelope (semitones relative to frequency)
        public float pitchStart;
        public float pitchEnd;
        public float pitchSweepTime = 0.2f;
        public float pitchCurve = 1f;
        public float pitchJump;
        public float pitchJumpTime = 0.1f;
        public float vibratoRate;
        public float vibratoDepth;

        // Noise
        public float noiseMix;
        public NoiseColor noiseColor = NoiseColor.White;
        public float noiseFm;

        // Amplitude modulation
        public float tremoloRate;
        public float tremoloDepth;

        // Filter
        public GeneratorFilterType filterType = GeneratorFilterType.None;
        public float filterCutoff = 4000f;
        public float filterResonance = 0.707f;
        public float filterEnvOctaves;
        public float filterLfoRate;
        public float filterLfoOctaves;

        // Output
        public float drive;

        public GeneratorSettings Clone() => (GeneratorSettings)MemberwiseClone();

        public ulong ComputeHash()
        {
            var h = new DSP.StableHash();
            h.Add(seed); h.Add((int)waveform); h.Add(frequency); h.Add(dutyCycle); h.Add(subOscMix);
            h.Add(durationSeconds); h.Add(volumeDb); h.Add(oneShot); h.Add(attack); h.Add(decay);
            h.Add(sustain); h.Add(release); h.Add(pitchStart); h.Add(pitchEnd); h.Add(pitchSweepTime);
            h.Add(pitchCurve); h.Add(pitchJump); h.Add(pitchJumpTime); h.Add(vibratoRate); h.Add(vibratoDepth);
            h.Add(noiseMix); h.Add((int)noiseColor); h.Add(noiseFm); h.Add(tremoloRate); h.Add(tremoloDepth);
            h.Add((int)filterType); h.Add(filterCutoff); h.Add(filterResonance); h.Add(filterEnvOctaves);
            h.Add(filterLfoRate); h.Add(filterLfoOctaves); h.Add(drive);
            if (waveform == OscillatorWaveform.Sample) { h.Add(sampleGuid); h.Add(sampleStart); }
            return h.Value;
        }
    }
}
