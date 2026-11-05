using System;
using AudioCreationStudio.DSP;
using AudioCreationStudio.Editing;
using AudioCreationStudio.Models;

namespace AudioCreationStudio.Tests
{
    /// <summary>Known test signals and a tiny recipe builder that feeds buffers straight into the renderer.</summary>
    public static class TestUtil
    {
        public const int SR = 48000;
        /// <summary>Tolerance for float pipelines that should be exact up to rounding.</summary>
        public const float Exact = 1e-6f;
        /// <summary>Tolerance for interpolated / filtered results.</summary>
        public const float Loose = 1e-3f;

        public static AudioBuffer Sine(double freq, int frames, float amp = 0.5f, int sr = SR, int channels = 1)
        {
            var b = new AudioBuffer(channels, frames, sr);
            for (int c = 0; c < channels; c++)
            {
                var d = b.GetChannel(c);
                for (int i = 0; i < frames; i++) d[i] = (float)(amp * Math.Sin(2 * Math.PI * freq * i / sr));
            }
            return b;
        }

        public static AudioBuffer Constant(float v, int frames, int sr = SR, int channels = 1)
        {
            var b = new AudioBuffer(channels, frames, sr);
            for (int c = 0; c < channels; c++) for (int i = 0; i < frames; i++) b.GetChannel(c)[i] = v;
            return b;
        }

        public static AudioBuffer Impulse(int frames, int at = 0, float v = 1f, int sr = SR)
        {
            var b = new AudioBuffer(1, frames, sr);
            b.GetChannel(0)[at] = v;
            return b;
        }

        public static double Rms(float[] d, int start, int count) => SignalAnalysis.Rms(d, start, count);

        public sealed class Rig
        {
            public RecipeData Recipe = new RecipeData { sampleRate = SR, channels = OutputChannelMode.Stereo };
            public readonly System.Collections.Generic.Dictionary<string, AudioBuffer> Sources = new System.Collections.Generic.Dictionary<string, AudioBuffer>();

            public Rig(OutputChannelMode ch = OutputChannelMode.Stereo)
            {
                Recipe.channels = ch;
                Recipe.masterEffects.Clear();
            }

            public TrackData Track(string name = null) => TimelineEditing.AddTrack(Recipe, name);

            public ClipData Clip(TrackData t, AudioBuffer src, long start = 0, long length = -1)
            {
                var c = TimelineEditing.CreateClipFromSource(null, src.SampleRate, src.Frames, src.Channels, start);
                TimelineEditing.FitLengthToSource(c, Recipe.sampleRate);
                if (length > 0) c.lengthSamples = length;
                t.clips.Add(c);
                Sources[c.uid] = src;
                return c;
            }

            public RenderResult Render(RenderScope scope = RenderScope.FullMix, string track = null, string clip = null, bool tail = true)
            {
                var req = new RenderRequest { Recipe = Recipe.Clone(), Scope = scope, TargetTrackUid = track, TargetClipUid = clip, IncludeTail = tail };
                foreach (var kv in Sources) req.Sources[kv.Key] = kv.Value;
                return new MixRenderer().Render(req);
            }
        }
    }
}
