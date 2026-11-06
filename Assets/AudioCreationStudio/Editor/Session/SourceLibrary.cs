using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using AudioCreationStudio.DSP;
using AudioCreationStudio.Export;
using AudioCreationStudio.Models;
using UnityEditor;
using UnityEngine;

namespace AudioCreationStudio.EditorUI
{
    /// <summary>
    /// Decodes source audio into <see cref="AudioBuffer"/>s and caches them. Never changes import settings:
    /// WAV sources are decoded directly from the original file (bit-exact, independent of compression
    /// settings); other formats are read through AudioClip.GetData when the clip is Decompress-On-Load.
    /// AudioClip access happens on the main thread only; generated buffers are thread-safe.
    /// </summary>
    public sealed class SourceLibrary
    {
        private sealed class Entry
        {
            public AudioBuffer Buffer;
            public string Key;
            public string Error;
            public Hash128 Dependency;
        }

        private readonly Dictionary<int, Entry> _clips = new Dictionary<int, Entry>();
        private readonly ConcurrentDictionary<string, AudioBuffer> _generated = new ConcurrentDictionary<string, AudioBuffer>();
        private readonly ConcurrentDictionary<string, KeyValuePair<AudioBuffer, AudioBuffer>> _samples = new ConcurrentDictionary<string, KeyValuePair<AudioBuffer, AudioBuffer>>();

        public static readonly SourceLibrary Shared = new SourceLibrary();

        public bool TryGetClip(AudioClip clip, out AudioBuffer buffer, out string key, out string error)
        {
            buffer = null; key = null; error = null;
            if (clip == null) { error = "Missing source AudioClip."; return false; }
            string path = AssetDatabase.GetAssetPath(clip);
            Hash128 dep = string.IsNullOrEmpty(path) ? default : AssetDatabase.GetAssetDependencyHash(path);
            int id = clip.GetInstanceID();
            if (_clips.TryGetValue(id, out var e) && e.Dependency == dep)
            {
                buffer = e.Buffer; key = e.Key; error = e.Error;
                return buffer != null;
            }
            e = new Entry { Dependency = dep };
            try
            {
                e.Buffer = Decode(clip, path, out e.Error);
            }
            catch (Exception ex)
            {
                e.Error = $"Could not read '{clip.name}': {ex.Message}";
            }
            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(clip, out string guid, out long local))
                e.Key = $"clip:{guid}:{local}:{dep}";
            else e.Key = $"clip:mem:{id}";
            _clips[id] = e;
            buffer = e.Buffer; key = e.Key; error = e.Error;
            return buffer != null;
        }

        private static AudioBuffer Decode(AudioClip clip, string assetPath, out string error)
        {
            error = null;
            if (!string.IsNullOrEmpty(assetPath) && assetPath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
            {
                // Packages/... asset paths of git/registry packages live in Library/PackageCache: resolve the real file.
                string full = Path.GetFullPath(FileUtil.GetPhysicalPath(assetPath));
                try { return WavDecoder.Decode(full, out _); }
                catch (Exception ex) { error = $"WAV decode failed ({ex.Message}); trying AudioClip data."; }
            }
            if (clip.loadType != AudioClipLoadType.DecompressOnLoad && !string.IsNullOrEmpty(assetPath))
            {
                error = $"'{clip.name}' uses Load Type '{clip.loadType}'. Set it to 'Decompress On Load' (or use a WAV file) so samples can be read. " +
                        "Import settings are never changed automatically.";
                return null;
            }
            if (clip.loadState != AudioDataLoadState.Loaded) clip.LoadAudioData();
            if (clip.samples <= 0 || clip.channels <= 0) { error = $"'{clip.name}' has no sample data."; return null; }
            var data = new float[clip.samples * clip.channels];
            if (!clip.GetData(data, 0)) { error = $"Unity could not provide sample data for '{clip.name}'."; return null; }
            error = null;
            return AudioBuffer.FromInterleaved(data, clip.channels, clip.frequency);
        }

        public static string GeneratorKey(GeneratorSettings s, int sampleRate) => $"gen:{s.ComputeHash():X16}:{sampleRate}";

        /// <summary>
        /// Decodes the sample a Sample-source generator uses (main thread only) and caches a mono mix-down so
        /// worker-thread renders can use it. Returns false (with <paramref name="error"/>) if it can't be read.
        /// </summary>
        public bool PrepareSample(GeneratorSettings s, out string error)
        {
            error = null;
            if (s == null || s.waveform != OscillatorWaveform.Sample) return true;
            if (string.IsNullOrEmpty(s.sampleGuid)) { error = "No sample assigned."; return false; }
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(s.sampleGuid));
            if (clip == null) { error = $"Sample '{s.sampleName}' is missing."; return false; }
            if (!TryGetClip(clip, out var buf, out _, out error)) return false;
            if (_samples.TryGetValue(s.sampleGuid, out var kv) && ReferenceEquals(kv.Key, buf)) return true;
            var mono = buf.Channels == 1 ? buf : ChannelConverter.Convert(buf, 1);
            _samples[s.sampleGuid] = new KeyValuePair<AudioBuffer, AudioBuffer>(buf, mono);
            return true;
        }

        public AudioBuffer GetPreparedSample(GeneratorSettings s) =>
            s != null && s.waveform == OscillatorWaveform.Sample && !string.IsNullOrEmpty(s.sampleGuid) && _samples.TryGetValue(s.sampleGuid, out var kv) ? kv.Value : null;

        /// <summary>Returns (rendering if needed) the generator output. Thread-safe; call <see cref="PrepareSample"/> first for Sample sources.</summary>
        public AudioBuffer GetGenerated(GeneratorSettings s, int sampleRate)
        {
            string key = GeneratorKey(s, sampleRate);
            if (_generated.TryGetValue(key, out var b)) return b;
            var sample = GetPreparedSample(s);
            b = ProceduralSynth.Render(s, sampleRate, default, sample);
            if (s.waveform == OscillatorWaveform.Sample && sample == null) return b; // don't cache a render without its sample
            if (_generated.Count > 256) _generated.Clear();
            _generated[key] = b;
            return b;
        }

        public bool TryGetGeneratedCached(GeneratorSettings s, int sampleRate, out AudioBuffer b) =>
            _generated.TryGetValue(GeneratorKey(s, sampleRate), out b);

        public string SourceKey(ClipData c, int recipeRate)
        {
            if (c.sourceKind == ClipSourceKind.Generator) return c.generator != null ? GeneratorKey(c.generator, recipeRate) : null;
            return TryGetClip(c.sourceClip, out _, out var key, out _) ? key : null;
        }

        public void Invalidate(AudioClip clip = null)
        {
            if (clip == null) { _clips.Clear(); return; }
            _clips.Remove(clip.GetInstanceID());
        }
    }
}
