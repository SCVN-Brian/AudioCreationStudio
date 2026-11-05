namespace AudioCreationStudio.Models
{
    public enum ClipSourceKind { AudioClip = 0, Generator = 1 }

    public enum FadeCurve { Linear = 0, EqualPower = 1, Exponential = 2, SCurve = 3 }

    public enum OutputChannelMode { Mono = 1, Stereo = 2 }

    public enum AutomationInterpolation { Linear = 0, Smooth = 1, Step = 2 }

    public enum WavSampleFormat { Pcm16 = 0, Pcm24 = 1, Float32 = 2 }

    public enum ExportScope { FullMix = 0, SelectedTrack = 1, SelectedClip = 2, LoopRegion = 3 }

    public enum OscillatorWaveform { Sine = 0, Square = 1, Triangle = 2, Sawtooth = 3, WhiteNoise = 4, PinkNoise = 5, Sample = 6 }

    public enum NoiseColor { White = 0, Pink = 1 }

    public enum GeneratorFilterType { None = 0, LowPass = 1, HighPass = 2, BandPass = 3 }

    public enum LoopSourceKind { RenderedMix = 0, Clip = 1, AudioClipAsset = 2 }
}
