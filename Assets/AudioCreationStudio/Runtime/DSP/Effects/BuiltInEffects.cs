namespace AudioCreationStudio.DSP
{
    public static class EffectTypes
    {
        public const string Gain = "gain";
        public const string LowPass = "lowpass";
        public const string HighPass = "highpass";
        public const string Equalizer = "eq";
        public const string Compressor = "compressor";
        public const string Limiter = "limiter";
        public const string Distortion = "distortion";
        public const string Delay = "delay";
        public const string Reverb = "reverb";
    }

    internal static class BuiltInEffects
    {
        public static void RegisterAll()
        {
            var log = ParamScale.Logarithmic;
            EffectRegistry.Register(new EffectDefinition(EffectTypes.Gain, "Gain", "Utility", "Smoothed level change.",
                new[] { new ParamDef("gain", "Gain", -48f, 24f, 0f, "dB") }, () => new GainEffect()));

            EffectRegistry.Register(new EffectDefinition(EffectTypes.LowPass, "Low-Pass Filter", "Filter", "12 dB/oct resonant low-pass.",
                new[]
                {
                    new ParamDef("cutoff", "Cutoff", 20f, 20000f, 4000f, "Hz", log),
                    new ParamDef("q", "Resonance", 0.3f, 12f, 0.707f, "Q", log)
                }, () => new FilterEffect(false)));

            EffectRegistry.Register(new EffectDefinition(EffectTypes.HighPass, "High-Pass Filter", "Filter", "12 dB/oct resonant high-pass.",
                new[]
                {
                    new ParamDef("cutoff", "Cutoff", 20f, 20000f, 120f, "Hz", log),
                    new ParamDef("q", "Resonance", 0.3f, 12f, 0.707f, "Q", log)
                }, () => new FilterEffect(true)));

            EffectRegistry.Register(new EffectDefinition(EffectTypes.Equalizer, "Parametric EQ", "Filter", "Low shelf, two peaking bands, high shelf.",
                new[]
                {
                    new ParamDef("lowFreq", "Low Freq", 20f, 1000f, 120f, "Hz", log),
                    new ParamDef("lowGain", "Low Gain", -18f, 18f, 0f, "dB"),
                    new ParamDef("mid1Freq", "Mid 1 Freq", 80f, 8000f, 500f, "Hz", log),
                    new ParamDef("mid1Gain", "Mid 1 Gain", -18f, 18f, 0f, "dB"),
                    new ParamDef("mid1Q", "Mid 1 Q", 0.2f, 10f, 1f, "Q", log),
                    new ParamDef("mid2Freq", "Mid 2 Freq", 200f, 16000f, 3000f, "Hz", log),
                    new ParamDef("mid2Gain", "Mid 2 Gain", -18f, 18f, 0f, "dB"),
                    new ParamDef("mid2Q", "Mid 2 Q", 0.2f, 10f, 1f, "Q", log),
                    new ParamDef("highFreq", "High Freq", 1000f, 20000f, 8000f, "Hz", log),
                    new ParamDef("highGain", "High Gain", -18f, 18f, 0f, "dB")
                }, () => new EqualizerEffect()));

            EffectRegistry.Register(new EffectDefinition(EffectTypes.Compressor, "Compressor", "Dynamics", "Stereo-linked feed-forward compressor with soft knee.",
                new[]
                {
                    new ParamDef("threshold", "Threshold", -60f, 0f, -18f, "dB"),
                    new ParamDef("ratio", "Ratio", 1f, 20f, 4f, ":1", log),
                    new ParamDef("attack", "Attack", 0.1f, 200f, 10f, "ms", log),
                    new ParamDef("release", "Release", 5f, 2000f, 120f, "ms", log),
                    new ParamDef("knee", "Knee", 0f, 24f, 6f, "dB"),
                    new ParamDef("makeup", "Makeup", 0f, 24f, 0f, "dB"),
                    new ParamDef("mix", "Mix", 0f, 1f, 1f)
                }, () => new CompressorEffect()));

            EffectRegistry.Register(new EffectDefinition(EffectTypes.Limiter, "Limiter", "Dynamics", "Zero-latency brickwall peak limiter.",
                new[]
                {
                    new ParamDef("ceiling", "Ceiling", -24f, 0f, -1f, "dB"),
                    new ParamDef("input", "Input Gain", 0f, 24f, 0f, "dB"),
                    new ParamDef("release", "Release", 1f, 1000f, 60f, "ms", log)
                }, () => new LimiterEffect()));

            EffectRegistry.Register(new EffectDefinition(EffectTypes.Distortion, "Distortion", "Color", "Waveshaping saturation with tone control.",
                new[]
                {
                    new ParamDef("mode", "Mode", 0f, 2f, 0f, "", ParamScale.Linear, new[] { "Soft", "Hard", "Fold" }),
                    new ParamDef("drive", "Drive", 0f, 48f, 12f, "dB"),
                    new ParamDef("tone", "Tone", 500f, 20000f, 9000f, "Hz", log),
                    new ParamDef("mix", "Mix", 0f, 1f, 1f),
                    new ParamDef("output", "Output", -24f, 12f, -6f, "dB")
                }, () => new DistortionEffect()));

            EffectRegistry.Register(new EffectDefinition(EffectTypes.Delay, "Delay", "Space", "Feedback delay with damping and ping-pong.",
                new[]
                {
                    new ParamDef("time", "Time", 1f, 2000f, 220f, "ms", log),
                    new ParamDef("feedback", "Feedback", 0f, 0.95f, 0.35f),
                    new ParamDef("mix", "Mix", 0f, 1f, 0.3f),
                    new ParamDef("damping", "Damping", 500f, 20000f, 6000f, "Hz", log),
                    new ParamDef("pingpong", "Ping-Pong", 0f, 1f, 0f, "", ParamScale.Linear, new[] { "Off", "On" })
                }, () => new DelayEffect()));

            EffectRegistry.Register(new EffectDefinition(EffectTypes.Reverb, "Reverb", "Space", "Stereo Schroeder/Freeverb-style room.",
                new[]
                {
                    new ParamDef("size", "Room Size", 0f, 1f, 0.6f),
                    new ParamDef("damping", "Damping", 0f, 1f, 0.45f),
                    new ParamDef("width", "Width", 0f, 1f, 1f),
                    new ParamDef("predelay", "Pre-Delay", 0f, 200f, 10f, "ms"),
                    new ParamDef("mix", "Mix", 0f, 1f, 0.25f)
                }, () => new ReverbEffect()));
        }
    }
}
