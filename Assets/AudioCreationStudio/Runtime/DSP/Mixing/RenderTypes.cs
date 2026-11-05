using System.Collections.Generic;
using AudioCreationStudio.Models;

namespace AudioCreationStudio.DSP
{
    public enum RenderScope { FullMix, Track, Clip }

    /// <summary>Optional cache for rendered (post-fader) track buffers, keyed by a content hash.</summary>
    public interface ITrackRenderCache
    {
        bool TryGet(ulong key, out AudioBuffer buffer);
        void Store(ulong key, AudioBuffer buffer);
    }

    /// <summary>
    /// Immutable-by-convention input of a render. <see cref="Recipe"/> must be a private clone and
    /// <see cref="Sources"/> must contain already-decoded audio, so rendering can run on any thread
    /// without touching Unity APIs.
    /// </summary>
    public sealed class RenderRequest
    {
        public RecipeData Recipe;
        /// <summary>Decoded source audio keyed by clip uid. Missing entries render as silence.</summary>
        public Dictionary<string, AudioBuffer> Sources = new Dictionary<string, AudioBuffer>();
        /// <summary>Source identity strings keyed by clip uid (used for cache keys).</summary>
        public Dictionary<string, string> SourceKeys = new Dictionary<string, string>();
        public RenderScope Scope = RenderScope.FullMix;
        public string TargetTrackUid;
        public string TargetClipUid;
        public bool IncludeTail = true;
        public int BlockSize = 512;
        public ITrackRenderCache Cache;
        /// <summary>Keep individual post-fader track buffers in the result (for meters).</summary>
        public bool KeepTrackBuffers;
    }

    public sealed class RenderResult
    {
        public AudioBuffer Master;
        public Dictionary<string, AudioBuffer> TrackBuffers = new Dictionary<string, AudioBuffer>();
        public long ContentFrames;
        public float Peak;
        public int ClippedSamples;
        public int InvalidSamples;
        public List<string> MissingSourceClips = new List<string>();
        public int TracksFromCache;
        public int TracksRendered;
    }
}
