using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using AudioCreationStudio.DSP;
using UnityEngine;

namespace AudioCreationStudio.EditorUI
{
    /// <summary>
    /// Caches <see cref="WaveformPeaks"/> per source key. Peaks are built on worker threads so the Editor never
    /// blocks during analysis; completed results are published on the main thread via <see cref="Pump"/>.
    /// </summary>
    public sealed class WaveformCache
    {
        private readonly Dictionary<string, WaveformPeaks> _ready = new Dictionary<string, WaveformPeaks>();
        private readonly HashSet<string> _pending = new HashSet<string>();
        private readonly ConcurrentQueue<KeyValuePair<string, WaveformPeaks>> _done = new ConcurrentQueue<KeyValuePair<string, WaveformPeaks>>();

        public static readonly WaveformCache Shared = new WaveformCache();

        public event Action Updated;

        public WaveformPeaks Get(string key, AudioBuffer buffer)
        {
            if (key == null || buffer == null) return null;
            if (_ready.TryGetValue(key, out var p)) return p;
            if (_pending.Add(key))
            {
                Task.Run(() =>
                {
                    try { _done.Enqueue(new KeyValuePair<string, WaveformPeaks>(key, WaveformPeaks.Build(buffer))); }
                    catch (Exception ex) { Debug.LogException(ex); _done.Enqueue(new KeyValuePair<string, WaveformPeaks>(key, null)); }
                });
            }
            return null;
        }

        /// <summary>Builds peaks from a lazily produced buffer (the producer runs on a worker thread).</summary>
        public WaveformPeaks GetAsync(string key, Func<AudioBuffer> producer)
        {
            if (key == null) return null;
            if (_ready.TryGetValue(key, out var p)) return p;
            if (_pending.Add(key))
            {
                Task.Run(() =>
                {
                    try { _done.Enqueue(new KeyValuePair<string, WaveformPeaks>(key, WaveformPeaks.Build(producer()))); }
                    catch (Exception ex) { Debug.LogException(ex); _done.Enqueue(new KeyValuePair<string, WaveformPeaks>(key, null)); }
                });
            }
            return null;
        }

        /// <summary>Synchronous build for small buffers where an immediate result is preferable.</summary>
        public WaveformPeaks GetImmediate(string key, AudioBuffer buffer)
        {
            if (_ready.TryGetValue(key, out var p)) return p;
            p = WaveformPeaks.Build(buffer);
            _ready[key] = p;
            return p;
        }

        public void Pump()
        {
            bool any = false;
            while (_done.TryDequeue(out var kv))
            {
                _pending.Remove(kv.Key);
                if (kv.Value != null) _ready[kv.Key] = kv.Value;
                any = true;
            }
            if (_ready.Count > 512) _ready.Clear();
            if (any) Updated?.Invoke();
        }
    }
}
