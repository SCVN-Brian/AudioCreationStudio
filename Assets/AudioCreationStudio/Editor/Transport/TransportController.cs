using System;
using AudioCreationStudio.DSP;
using AudioCreationStudio.Models;
using UnityEditor;

namespace AudioCreationStudio.EditorUI
{
    public enum PreviewTarget { None, Mix, Track, Clip, Buffer }

    /// <summary>
    /// Transport state machine (play / pause / stop / seek / loop) over an <see cref="IPreviewPlayer"/>.
    /// Mix playback follows edits live: when a newer render arrives while playing, playback continues
    /// from the same position with the new audio.
    /// </summary>
    public sealed class TransportController : IDisposable
    {
        private readonly StudioSession _session;
        private readonly PreviewRenderService _render;
        private IPreviewPlayer _player;
        private AudioBuffer _buffer;
        private long _timelineOffset;
        private bool _driveTimeline;
        private bool _pendingPlay;
        private long _playStart;
        private int _playingVersion = -1;

        public bool IsPlaying { get; private set; }
        public bool IsPaused { get; private set; }
        public PreviewTarget Target { get; private set; }
        public string TargetLabel { get; private set; } = "";
        public AudioBuffer PlayingBuffer => _buffer;
        public string Backend => _player.BackendName;
        public event Action StateChanged;
        /// <summary>Raised with a user-facing reason when a play request cannot produce audio.</summary>
        public event Action<string> PlaybackProblem;

        public TransportController(StudioSession session, PreviewRenderService render)
        {
            _session = session;
            _render = render;
            _player = PreviewPlayerFactory.Create();
            _render.Rendered += OnRendered;
        }

        /// <summary>Switches the playback backend (stops playback).</summary>
        public void SetBackend(PlaybackBackend backend)
        {
            Stop();
            _player.Dispose();
            PreviewPlayerFactory.Preferred = backend;
            _player = PreviewPlayerFactory.Create(backend);
            StateChanged?.Invoke();
        }

        public void Dispose()
        {
            _render.Rendered -= OnRendered;
            _player.Dispose();
        }

        public int BufferPosition => IsPlaying ? _player.PositionFrames : 0;

        public void TogglePlay()
        {
            // Self-heal if the audio under us was stopped or taken over (e.g. an Inspector AudioClip preview).
            if (IsPlaying && !IsPaused && !_player.IsPlaying) Stop();
            if (IsPlaying && !IsPaused) Pause();
            else if (IsPaused) Resume();
            else PlayMix();
        }

        public void PlayMix()
        {
            if (!_render.IsUpToDate)
            {
                _pendingPlay = true;
                _render.RenderSoon();
                TargetLabel = "Rendering…";
                StateChanged?.Invoke();
                return;
            }
            StartMix(_session.Playhead);
        }

        private void StartMix(long from)
        {
            var res = _render.Current;
            if (res?.Master == null || res.Master.Frames == 0)
            {
                Stop();
                PlaybackProblem?.Invoke(_session.Data.ContentEnd == 0
                    ? "Nothing to play: the timeline is empty. Drag AudioClips in, insert a generated sound, or open a demo."
                    : res != null && res.MissingSourceClips.Count > 0 ? "Nothing audible: the clips' source audio is missing or unreadable." : "Nothing to play.");
                return;
            }
            if (res.Peak <= 0f) PlaybackProblem?.Invoke(res.MissingSourceClips.Count > 0
                ? $"{res.MissingSourceClips.Count} clip(s) have missing/unreadable sources and play as silence."
                : "The mix is silent (check mute/solo, gains and fades).");
            var data = _session.Data;
            var full = res.Master;
            bool loop = _session.LoopPlayback;
            var region = data.loopRegion;
            if (loop && region.enabled && region.Length > 64 && region.start < full.Frames)
            {
                long end = Math.Min(region.end, full.Frames);
                _buffer = full.Slice((int)region.start, (int)(end - region.start));
                _timelineOffset = region.start;
            }
            else
            {
                _buffer = full;
                _timelineOffset = 0;
            }
            long startFrame = from - _timelineOffset;
            if (startFrame < 0 || startFrame >= _buffer.Frames) startFrame = 0;
            _playStart = _timelineOffset + startFrame;
            _player.Play(_buffer, (int)startFrame, loop);
            _driveTimeline = true;
            _playingVersion = _render.CurrentVersion;
            Begin(PreviewTarget.Mix, "Full Mix");
        }

        public void PlayTrack(string trackUid)
        {
            var t = _session.Data.FindTrack(trackUid);
            if (t == null) return;
            var res = _render.RenderNow(RenderScope.Track, trackUid);
            StartScoped(res, Math.Min(_session.Playhead, Math.Max(0, res.ContentFrames - 1)), PreviewTarget.Track, $"Track · {t.name}");
        }

        public void PlayClip(string clipUid)
        {
            var c = _session.Data.FindClip(clipUid);
            if (c == null) return;
            var res = _render.RenderNow(RenderScope.Clip, null, clipUid);
            StartScoped(res, c.startSample, PreviewTarget.Clip, $"Clip · {c.DisplayName}");
        }

        private void StartScoped(RenderResult res, long from, PreviewTarget target, string label)
        {
            if (res?.Master == null || res.Master.Frames == 0) { PlaybackProblem?.Invoke("Nothing to play for this selection."); return; }
            _buffer = res.Master;
            _timelineOffset = 0;
            _playStart = from;
            _player.Play(_buffer, (int)Math.Max(0, Math.Min(from, _buffer.Frames - 1)), false);
            _driveTimeline = true;
            Begin(target, label);
        }

        /// <summary>Plays an arbitrary buffer (generator, loop, variation previews) without moving the timeline playhead.</summary>
        public void PlayBuffer(AudioBuffer buffer, bool loop, string label)
        {
            if (buffer == null || buffer.Frames == 0) return;
            _buffer = buffer;
            _timelineOffset = 0;
            _player.Play(buffer, 0, loop);
            _driveTimeline = false;
            Begin(PreviewTarget.Buffer, label);
        }

        private void Begin(PreviewTarget target, string label)
        {
            _pendingPlay = false;
            IsPlaying = true;
            IsPaused = false;
            Target = target;
            TargetLabel = label;
            StateChanged?.Invoke();
        }

        public void Pause()
        {
            if (!IsPlaying) return;
            _player.Pause();
            IsPaused = true;
            StateChanged?.Invoke();
        }

        public void Resume()
        {
            if (!IsPaused) return;
            _player.Resume();
            IsPaused = false;
            StateChanged?.Invoke();
        }

        public void Stop()
        {
            bool wasDriving = _driveTimeline && IsPlaying;
            _player.Stop();
            _pendingPlay = false;
            IsPlaying = false;
            IsPaused = false;
            Target = PreviewTarget.None;
            TargetLabel = "";
            if (wasDriving) _session.Playhead = _playStart;
            _driveTimeline = false;
            StateChanged?.Invoke();
        }

        public void Seek(long sample)
        {
            _session.Playhead = Math.Max(0, sample);
            if (IsPlaying && Target == PreviewTarget.Mix && !IsPaused) StartMix(_session.Playhead);
            else if (IsPlaying && Target == PreviewTarget.Mix) { StartMix(_session.Playhead); Pause(); }
        }

        public void Tick()
        {
            if (_pendingPlay && _render.IsUpToDate) StartMix(_session.Playhead);
            if (!IsPlaying || IsPaused) return;
            if (!_player.IsPlaying)
            {
                bool drive = _driveTimeline;
                long end = _timelineOffset + (_buffer?.Frames ?? 0);
                Stop();
                if (drive) _session.Playhead = _playStart; // return to start like a DAW
                return;
            }
            if (_driveTimeline) _session.Playhead = _timelineOffset + _player.PositionFrames;
        }

        private void OnRendered()
        {
            // Live update: keep playing from the same spot with the new mix.
            if (IsPlaying && !IsPaused && Target == PreviewTarget.Mix && _render.CurrentVersion != _playingVersion)
            {
                long keepStart = _playStart;
                StartMix(_session.Playhead);
                _playStart = keepStart;
            }
        }

        /// <summary>Real level of the audio currently playing (window ending at the play position).</summary>
        public LevelReading MasterReading()
        {
            if (!IsPlaying || IsPaused || _buffer == null) return LevelReading.Silent;
            return SignalAnalysis.Measure(_buffer, _player.PositionFrames, Math.Max(256, _buffer.SampleRate / 30));
        }

        public LevelReading TrackReading(string trackUid)
        {
            if (!IsPlaying || IsPaused || Target != PreviewTarget.Mix) return LevelReading.Silent;
            var res = _render.Current;
            if (res == null || !res.TrackBuffers.TryGetValue(trackUid, out var b)) return LevelReading.Silent;
            return SignalAnalysis.Measure(b, _session.Playhead, Math.Max(256, b.SampleRate / 30));
        }
    }
}
