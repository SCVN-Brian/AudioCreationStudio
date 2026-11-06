using System;

namespace AudioCreationStudio.Models
{
    [Serializable]
    public class TimelineRegion
    {
        public bool enabled;
        public long start;
        public long end;
        public long Length => Math.Max(0, end - start);
        public TimelineRegion Clone() => (TimelineRegion)MemberwiseClone();
    }

    [Serializable]
    public class ExportSettings
    {
        public AudioFileType fileType = AudioFileType.Wav;
        public WavSampleFormat format = WavSampleFormat.Pcm16;
        /// <summary>Bitrate for MP3/OGG in kbit/s.</summary>
        public int lossyBitrateKbps = 192;
        /// <summary>0 = use the recipe sample rate.</summary>
        public int sampleRate;
        /// <summary>0 = use the recipe channel layout.</summary>
        public int channels;
        public ExportScope scope = ExportScope.FullMix;
        public bool includeTail = true;
        public bool normalize;
        public float normalizeTargetDb = -1f;
        public bool trimSilence;
        public float trimThresholdDb = -60f;
        public bool dither;
        public string outputFolder = "Assets/AudioCreationStudio/Exports";
        public string fileName = "";

        public ExportSettings Clone() => (ExportSettings)MemberwiseClone();
    }

    [Serializable]
    public class LoopSettings
    {
        public LoopSourceKind source = LoopSourceKind.RenderedMix;
        public string clipUid;
        public UnityEngine.AudioClip assetClip;
        public long loopStart;
        public long loopEnd;
        public long crossfadeSamples = 2400;
        public FadeCurve crossfadeCurve = FadeCurve.EqualPower;
        public bool snapToZeroCrossing = true;
        public float zeroSearchMs = 5f;

        public LoopSettings Clone() => (LoopSettings)MemberwiseClone();
    }

    [Serializable]
    public class VariationSettings
    {
        public int count = 5;
        public int seed = 1234;
        public float pitchSemitones = 1.0f;
        public float volumeDb = 1.5f;
        public float startOffsetMs = 0f;
        public float timingJitterMs = 8f;
        public float layerDropProbability = 0f;
        public float effectVariation = 0.1f;

        public VariationSettings Clone() => (VariationSettings)MemberwiseClone();
    }
}
