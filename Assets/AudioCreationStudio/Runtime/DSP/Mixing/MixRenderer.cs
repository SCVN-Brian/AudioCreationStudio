using System;
using System.Collections.Generic;
using System.Threading;
using AudioCreationStudio.Models;

namespace AudioCreationStudio.DSP
{
    /// <summary>
    /// Offline, block-based floating-point mix engine. The single source of truth for both preview and
    /// export: Clip render → Track FX chain (with automation) → Track fader/pan → Sum → Master FX → Master gain.
    /// Pure C#; safe to run on a worker thread when given a <see cref="RenderRequest"/> snapshot.
    /// </summary>
    public sealed class MixRenderer
    {
        /// <summary>Trailing effect tails below this level are trimmed (dBFS).</summary>
        public const float TailSilenceDb = -96f;

        private readonly ISampleInterpolator _interpolator;

        public MixRenderer(ISampleInterpolator interpolator = null)
        {
            _interpolator = interpolator ?? SincInterpolator.Shared;
        }

        public RenderResult Render(RenderRequest request, CancellationToken ct = default, IProgress<float> progress = null)
        {
            if (request?.Recipe == null) throw new ArgumentNullException(nameof(request));
            var recipe = request.Recipe;
            int sr = recipe.sampleRate;
            int outCh = recipe.ChannelCount;
            int block = Math.Max(32, request.BlockSize);
            var result = new RenderResult();

            var tracks = SelectTracks(request, out var clipFilter);
            long contentEnd = 0;

            var rendered = new List<AudioBuffer>();
            for (int ti = 0; ti < tracks.Count; ti++)
            {
                ct.ThrowIfCancellationRequested();
                var track = tracks[ti];
                foreach (var c in track.clips)
                {
                    if (!c.enabled || (clipFilter != null && c.uid != clipFilter)) continue;
                    contentEnd = Math.Max(contentEnd, c.EndSample);
                    if (!request.Sources.ContainsKey(c.uid) || request.Sources[c.uid] == null)
                        result.MissingSourceClips.Add(c.uid);
                }

                ulong key = ComputeTrackKey(track, request, clipFilter, sr, outCh);
                AudioBuffer buf = null;
                if (request.Cache != null && request.Cache.TryGet(key, out var cached)) { buf = cached; result.TracksFromCache++; }
                else
                {
                    buf = RenderTrack(track, request, clipFilter, sr, outCh, block, ct);
                    request.Cache?.Store(key, buf);
                    result.TracksRendered++;
                }
                rendered.Add(buf);
                if (request.KeepTrackBuffers) result.TrackBuffers[track.uid] = buf;
                progress?.Report((ti + 1f) / (tracks.Count + 1f) * 0.9f);
            }

            // Sum into the master bus.
            long busLen = contentEnd;
            foreach (var b in rendered) busLen = Math.Max(busLen, b.Frames);
            var masterChain = new EffectChainProcessor(recipe.masterEffects, sr, outCh, block);
            long masterTail = masterChain.GetTailFrames();
            long total = busLen + (masterChain.Count > 0 ? masterTail : 0);
            total = Math.Min(total, (long)sr * 600); // hard safety cap: 10 minutes
            var master = new AudioBuffer(outCh, (int)total, sr);
            foreach (var b in rendered) master.AddFrom(b, 0, 0, b.Frames);

            // Master chain + gain.
            for (int pos = 0; pos < master.Frames; pos += block)
            {
                if ((pos & 0xFFFF) == 0) ct.ThrowIfCancellationRequested();
                int n = Math.Min(block, master.Frames - pos);
                masterChain.Process(master, pos, n);
            }
            float mg = DspMath.DbToGain(recipe.masterGainDb);
            if (mg != 1f) master.Scale(mg);

            result.InvalidSamples = master.Sanitize();

            // Length policy: content end, plus the audible part of effect tails if requested.
            long finalLen = contentEnd;
            if (request.IncludeTail) finalLen = Math.Max(contentEnd, FindAudibleEnd(master, contentEnd));
            master.SetFrames((int)Math.Min(master.Frames, Math.Max(0, finalLen)));

            result.Master = master;
            result.ContentFrames = contentEnd;
            result.Peak = master.GetPeak();
            result.ClippedSamples = CountClipped(master);
            progress?.Report(1f);
            return result;
        }

        private static List<TrackData> SelectTracks(RenderRequest request, out string clipFilter)
        {
            var recipe = request.Recipe;
            var list = new List<TrackData>();
            clipFilter = null;
            switch (request.Scope)
            {
                case RenderScope.Track:
                {
                    var t = recipe.FindTrack(request.TargetTrackUid);
                    if (t != null) list.Add(t);
                    break;
                }
                case RenderScope.Clip:
                {
                    var c = recipe.FindClip(request.TargetClipUid, out var owner);
                    if (c != null) { list.Add(owner); clipFilter = c.uid; }
                    break;
                }
                default:
                {
                    bool anySolo = false;
                    foreach (var t in recipe.tracks) anySolo |= t.solo;
                    foreach (var t in recipe.tracks)
                        if (anySolo ? t.solo : !t.mute) list.Add(t);
                    break;
                }
            }
            return list;
        }

        private AudioBuffer RenderTrack(TrackData track, RenderRequest req, string clipFilter, int sr, int outCh, int block,
            CancellationToken ct)
        {
            long end = 0;
            foreach (var c in track.clips)
                if (c.enabled && (clipFilter == null || c.uid == clipFilter)) end = Math.Max(end, c.EndSample);
            if (end <= 0) return new AudioBuffer(outCh, 0, sr);

            var chain = new EffectChainProcessor(track.effects, sr, outCh, block, track.automation);
            long len = Math.Min(end + chain.GetTailFrames(), (long)sr * 600);
            var bus = new AudioBuffer(outCh, (int)len, sr);

            foreach (var c in track.clips)
            {
                ct.ThrowIfCancellationRequested();
                if (!c.enabled || (clipFilter != null && c.uid != clipFilter)) continue;
                req.Sources.TryGetValue(c.uid, out var src);
                ClipRenderer.Render(c, src, bus, sr, _interpolator);
            }

            if (chain.Count > 0)
            {
                for (int pos = 0; pos < bus.Frames; pos += block)
                {
                    if ((pos & 0x3FFF) == 0) ct.ThrowIfCancellationRequested();
                    int n = Math.Min(block, bus.Frames - pos);
                    chain.ApplyAutomation(pos);
                    chain.Process(bus, pos, n);
                }
            }

            ApplyFader(track, bus);
            return bus;
        }

        /// <summary>Post-FX track gain (dB) and pan (balance), with per-sample automation.</summary>
        public static void ApplyFader(TrackData track, AudioBuffer bus)
        {
            var gainLane = track.FindLane(AutomationTargets.Gain);
            var panLane = track.FindLane(AutomationTargets.Pan);
            bool ga = gainLane != null && gainLane.IsActive;
            bool pa = panLane != null && panLane.IsActive;
            float g = DspMath.DbToGain(track.gainDb);
            PanLaw.Balance(track.pan, out float pl, out float pr);
            bool stereo = bus.Channels > 1;
            if (!ga && !pa && g == 1f && (!stereo || (pl == 1f && pr == 1f))) return;

            var l = bus.GetChannel(0);
            var r = stereo ? bus.GetChannel(1) : null;
            for (int i = 0; i < bus.Frames; i++)
            {
                if (ga) g = DspMath.DbToGain(gainLane.Evaluate(i, track.gainDb));
                if (pa) PanLaw.Balance(panLane.Evaluate(i, track.pan), out pl, out pr);
                if (stereo)
                {
                    l[i] *= g * pl;
                    r[i] *= g * pr;
                }
                else l[i] *= g;
            }
        }

        public static long FindAudibleEnd(AudioBuffer buf, long minimum)
        {
            float thr = DspMath.DbToGain(TailSilenceDb);
            for (long i = buf.Frames - 1; i >= minimum; i--)
            {
                for (int c = 0; c < buf.Channels; c++)
                {
                    float v = buf.GetChannel(c)[i];
                    if (v > thr || v < -thr) return i + 1;
                }
            }
            return minimum;
        }

        public static int CountClipped(AudioBuffer buf, float threshold = 1f)
        {
            int n = 0;
            for (int c = 0; c < buf.Channels; c++)
            {
                var d = buf.GetChannel(c);
                for (int i = 0; i < buf.Frames; i++) if (d[i] > threshold || d[i] < -threshold) n++;
            }
            return n;
        }

        /// <summary>Content hash of everything that affects a track's post-fader output.</summary>
        public static ulong ComputeTrackKey(TrackData t, RenderRequest req, string clipFilter, int sr, int outCh)
        {
            var h = new StableHash();
            h.Add(sr); h.Add(outCh); h.Add(clipFilter ?? "");
            h.Add(t.uid); h.Add(t.gainDb); h.Add(t.pan);
            foreach (var e in t.effects) { h.Add(e.uid); h.Add(e.typeId); h.Add(e.enabled); h.Add(e.bypass); h.Add(e.values); }
            HashLanes(ref h, t.automation);
            foreach (var c in t.clips)
            {
                h.Add(c.uid); h.Add(c.enabled); h.Add(c.startSample); h.Add(c.sourceOffset); h.Add(c.lengthSamples);
                h.Add(c.gainDb); h.Add(c.pan); h.Add(c.playbackRate); h.Add(c.loop);
                h.Add(c.fadeIn?.lengthSamples ?? 0); h.Add((int)(c.fadeIn?.curve ?? 0));
                h.Add(c.fadeOut?.lengthSamples ?? 0); h.Add((int)(c.fadeOut?.curve ?? 0));
                HashLanes(ref h, c.automation);
                h.Add(req.SourceKeys != null && req.SourceKeys.TryGetValue(c.uid, out var k) ? k : "none");
                h.Add(req.Sources.TryGetValue(c.uid, out var s) && s != null ? s.Frames : -1);
            }
            return h.Value;
        }

        private static void HashLanes(ref StableHash h, List<AutomationLane> lanes)
        {
            if (lanes == null) return;
            foreach (var l in lanes)
            {
                h.Add(l.targetId); h.Add(l.enabled);
                foreach (var p in l.points) { h.Add(p.time); h.Add(p.value); h.Add((int)p.interpolation); }
            }
        }
    }

    /// <summary>Thread-safe LRU-ish track cache bounded by total sample count.</summary>
    public sealed class TrackRenderCache : ITrackRenderCache
    {
        private readonly Dictionary<ulong, AudioBuffer> _map = new Dictionary<ulong, AudioBuffer>();
        private readonly LinkedList<ulong> _order = new LinkedList<ulong>();
        private readonly object _lock = new object();
        private readonly long _maxSamples;
        private long _samples;

        public TrackRenderCache(long maxSamples = 48000L * 2 * 60 * 4) { _maxSamples = maxSamples; }

        public bool TryGet(ulong key, out AudioBuffer buffer)
        {
            lock (_lock)
            {
                if (_map.TryGetValue(key, out buffer))
                {
                    _order.Remove(key);
                    _order.AddLast(key);
                    return true;
                }
                return false;
            }
        }

        public void Store(ulong key, AudioBuffer buffer)
        {
            lock (_lock)
            {
                if (_map.ContainsKey(key)) return;
                _map[key] = buffer;
                _order.AddLast(key);
                _samples += (long)buffer.Frames * buffer.Channels;
                while (_samples > _maxSamples && _order.Count > 1)
                {
                    var oldest = _order.First.Value;
                    _order.RemoveFirst();
                    if (_map.TryGetValue(oldest, out var b)) _samples -= (long)b.Frames * b.Channels;
                    _map.Remove(oldest);
                }
            }
        }

        public void Clear()
        {
            lock (_lock) { _map.Clear(); _order.Clear(); _samples = 0; }
        }
    }
}
