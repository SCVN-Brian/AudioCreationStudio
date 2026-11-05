using System.Collections.Generic;
using System.Threading;
using AudioCreationStudio.DSP;
using AudioCreationStudio.Models;

namespace AudioCreationStudio.EditorUI
{
    /// <summary>
    /// Builds thread-safe render jobs. Must be called on the main thread (AudioClips are decoded here);
    /// generator sources are synthesized later on the worker thread inside <see cref="RenderJob.Execute"/>.
    /// </summary>
    public sealed class RenderJob
    {
        public RenderRequest Request;
        public readonly List<KeyValuePair<string, GeneratorSettings>> PendingGenerators = new List<KeyValuePair<string, GeneratorSettings>>();
        public SourceLibrary Library;

        public RenderResult Execute(MixRenderer renderer, CancellationToken ct)
        {
            int sr = Request.Recipe.sampleRate;
            foreach (var kv in PendingGenerators)
            {
                ct.ThrowIfCancellationRequested();
                Request.Sources[kv.Key] = Library.GetGenerated(kv.Value, sr);
                Request.SourceKeys[kv.Key] = SourceLibrary.GeneratorKey(kv.Value, sr);
            }
            return renderer.Render(Request, ct);
        }
    }

    public static class RenderRequestFactory
    {
        public static RenderJob Create(RecipeData live, SourceLibrary lib, RenderScope scope = RenderScope.FullMix,
            string trackUid = null, string clipUid = null, bool includeTail = true)
        {
            SyncSourceInfo(live, lib);
            var recipe = live.Clone();
            var job = new RenderJob { Library = lib };
            var req = new RenderRequest
            {
                Recipe = recipe, Scope = scope, TargetTrackUid = trackUid, TargetClipUid = clipUid, IncludeTail = includeTail
            };
            foreach (var c in recipe.AllClips())
            {
                if (c.sourceKind == ClipSourceKind.Generator)
                {
                    if (c.generator == null) continue;
                    lib.PrepareSample(c.generator, out _);
                    if (lib.TryGetGeneratedCached(c.generator, recipe.sampleRate, out var gb))
                    {
                        req.Sources[c.uid] = gb;
                        req.SourceKeys[c.uid] = SourceLibrary.GeneratorKey(c.generator, recipe.sampleRate);
                    }
                    else job.PendingGenerators.Add(new KeyValuePair<string, GeneratorSettings>(c.uid, c.generator.Clone()));
                }
                else if (lib.TryGetClip(c.sourceClip, out var buf, out var key, out _))
                {
                    req.Sources[c.uid] = buf;
                    req.SourceKeys[c.uid] = key;
                }
            }
            job.Request = req;
            return job;
        }

        /// <summary>Keeps cached source format info in clips in sync with the decoded audio (e.g. after re-import).</summary>
        public static void SyncSourceInfo(RecipeData data, SourceLibrary lib)
        {
            foreach (var c in data.AllClips())
            {
                if (c.sourceKind == ClipSourceKind.Generator)
                {
                    if (c.generator == null) continue;
                    long frames = (long)System.Math.Round(DspMath.Clamp(c.generator.durationSeconds, 0.005f, ProceduralSynth.MaxDurationSeconds) * data.sampleRate);
                    c.sourceSampleRate = data.sampleRate;
                    c.sourceFrames = System.Math.Max(1, frames);
                    c.sourceChannels = 1;
                    continue;
                }
                if (!lib.TryGetClip(c.sourceClip, out var buf, out _, out _)) continue;
                c.sourceSampleRate = buf.SampleRate;
                c.sourceFrames = buf.Frames;
                c.sourceChannels = buf.Channels;
            }
        }
    }
}
