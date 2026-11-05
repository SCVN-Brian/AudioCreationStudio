using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using AudioCreationStudio.DSP;
using UnityEditor;
using Debug = UnityEngine.Debug;

namespace AudioCreationStudio.EditorUI
{
    /// <summary>
    /// Keeps an up-to-date rendered mix for preview and meters. Renders are debounced, run on a worker
    /// thread, are cancellable, and reuse unchanged tracks through <see cref="TrackRenderCache"/>, so editing
    /// one track only re-renders that track. Uses exactly the same <see cref="MixRenderer"/> as export.
    /// </summary>
    public sealed class PreviewRenderService : IDisposable
    {
        private const double DebounceSeconds = 0.08;

        private readonly StudioSession _session;
        private readonly SourceLibrary _library;
        private readonly TrackRenderCache _cache = new TrackRenderCache();
        private readonly MixRenderer _renderer = new MixRenderer();
        private readonly ConcurrentQueue<Action> _main = new ConcurrentQueue<Action>();
        private CancellationTokenSource _cts;
        private bool _dirty = true;
        private double _dirtyTime;
        private int _renderingVersion = -1;

        public RenderResult Current { get; private set; }
        public int CurrentVersion { get; private set; } = -1;
        public bool IsRendering { get; private set; }
        public double LastRenderMs { get; private set; }
        public string LastError { get; private set; }
        public int LastCacheHits { get; private set; }
        public event Action Rendered;

        public PreviewRenderService(StudioSession session, SourceLibrary library)
        {
            _session = session;
            _library = library;
        }

        public bool IsUpToDate => !_dirty && !IsRendering && Current != null && CurrentVersion == _session.Version;

        public void Invalidate()
        {
            _dirty = true;
            _dirtyTime = EditorApplication.timeSinceStartup;
            if (IsRendering) _cts?.Cancel();
        }

        /// <summary>Requests an immediate (non-debounced) background render.</summary>
        public void RenderSoon()
        {
            _dirty = true;
            _dirtyTime = 0;
        }

        public void Tick()
        {
            while (_main.TryDequeue(out var a)) a();
            if (_session.Version != CurrentVersion && !IsRendering && !_dirty) { _dirty = true; _dirtyTime = EditorApplication.timeSinceStartup; }
            if (_dirty && !IsRendering && EditorApplication.timeSinceStartup - _dirtyTime >= DebounceSeconds) Start();
        }

        private void Start()
        {
            _dirty = false;
            RenderJob job;
            try
            {
                job = RenderRequestFactory.Create(_session.Data, _library);
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                Debug.LogException(ex);
                return;
            }
            job.Request.Cache = _cache;
            job.Request.KeepTrackBuffers = true;
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            int version = _session.Version;
            _renderingVersion = version;
            IsRendering = true;
            Task.Run(() =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    var result = job.Execute(_renderer, token);
                    sw.Stop();
                    _main.Enqueue(() =>
                    {
                        IsRendering = false;
                        Current = result;
                        CurrentVersion = version;
                        LastRenderMs = sw.Elapsed.TotalMilliseconds;
                        LastCacheHits = result.TracksFromCache;
                        LastError = null;
                        Rendered?.Invoke();
                    });
                }
                catch (OperationCanceledException)
                {
                    _main.Enqueue(() => { IsRendering = false; _dirty = true; });
                }
                catch (Exception ex)
                {
                    _main.Enqueue(() => { IsRendering = false; LastError = ex.Message; Debug.LogException(ex); });
                }
            }, token);
        }

        /// <summary>Synchronous render (used for clip/track preview). Same engine and settings as export.</summary>
        public RenderResult RenderNow(RenderScope scope, string trackUid = null, string clipUid = null, bool includeTail = true)
        {
            var job = RenderRequestFactory.Create(_session.Data, _library, scope, trackUid, clipUid, includeTail);
            job.Request.Cache = _cache;
            return job.Execute(_renderer, CancellationToken.None);
        }

        public void ClearCache() => _cache.Clear();

        public void Dispose()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cache.Clear();
        }
    }
}
