using System;
using System.Reflection;
using AudioCreationStudio.DSP;
using UnityEditor;
using UnityEngine;

namespace AudioCreationStudio.EditorUI
{
    /// <summary>Plays rendered buffers in the Editor. Implementations must be used from the main thread only.</summary>
    public interface IPreviewPlayer : IDisposable
    {
        string BackendName { get; }
        bool IsPlaying { get; }
        void Play(AudioBuffer buffer, int startFrame, bool loop);
        void Pause();
        void Resume();
        void Stop();
        /// <summary>Current position in frames within the buffer being played.</summary>
        int PositionFrames { get; }
    }

    public enum PlaybackBackend { SceneAudioSource = 0, EditorPreview = 1 }

    public static class PreviewPlayerFactory
    {
        public const string BackendPref = "AudioCreationStudio.PlaybackBackend";

        /// <summary>
        /// Default is a hidden AudioSource: it goes through Unity's normal mixer, which is the most reliable
        /// path (the internal Editor preview channel can be silent on some setups). The preview channel remains selectable.
        /// </summary>
        public static PlaybackBackend Preferred
        {
            get => (PlaybackBackend)EditorPrefs.GetInt(BackendPref, (int)PlaybackBackend.SceneAudioSource);
            set => EditorPrefs.SetInt(BackendPref, (int)value);
        }

        public static IPreviewPlayer Create() => Create(Preferred);

        /// <summary>Removes hidden preview objects orphaned by a domain reload.</summary>
        public static void CleanupOrphans()
        {
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
                if ((go.name == "ACS Preview Source" || go.name == "ACS Preview Listener") && (go.hideFlags & HideFlags.DontSave) != 0)
                    UnityEngine.Object.DestroyImmediate(go);
        }

        public static IPreviewPlayer Create(PlaybackBackend backend)
        {
            CleanupOrphans();
            if (backend == PlaybackBackend.EditorPreview)
            {
                var p = new AudioUtilPreviewPlayer();
                if (p.Available) return p;
                p.Dispose();
            }
            return new AudioSourcePreviewPlayer();
        }

        public static AudioClip CreateClip(AudioBuffer buffer, string name)
        {
            int frames = Math.Max(1, buffer.Frames);
            var clip = AudioClip.Create(name, frames, buffer.Channels, buffer.SampleRate, false);
            clip.hideFlags = HideFlags.HideAndDontSave;
            if (buffer.Frames > 0) clip.SetData(buffer.ToInterleaved(), 0);
            return clip;
        }
    }

    /// <summary>
    /// Uses the Editor's internal preview audio path (the one the AudioClip inspector uses): works without
    /// an AudioListener or play mode. Accessed through reflection; falls back if unavailable.
    /// </summary>
    public sealed class AudioUtilPreviewPlayer : IPreviewPlayer
    {
        private static readonly Type Util = typeof(Editor).Assembly.GetType("UnityEditor.AudioUtil");
        private static readonly MethodInfo PlayM = Find("PlayPreviewClip", 3);
        private static readonly MethodInfo StopAllM = Find("StopAllPreviewClips", 0);
        private static readonly MethodInfo PauseM = Find("PausePreviewClip", 0);
        private static readonly MethodInfo ResumeM = Find("ResumePreviewClip", 0);
        private static readonly MethodInfo IsPlayingM = Find("IsPreviewClipPlaying", 0);
        private static readonly MethodInfo PosM = Find("GetPreviewClipSamplePosition", 0);

        private AudioClip _clip;
        private bool _paused;
        private bool _loop;
        private int _frames, _rate, _startFrame;
        private readonly System.Diagnostics.Stopwatch _clock = new System.Diagnostics.Stopwatch();

        private static MethodInfo Find(string name, int args)
        {
            if (Util == null) return null;
            foreach (var m in Util.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                if (m.Name == name && m.GetParameters().Length == args) return m;
            return null;
        }

        public bool Available => PlayM != null && StopAllM != null && IsPlayingM != null && PosM != null;
        public string BackendName => "Editor Preview";

        /// <summary>
        /// The Editor preview channel is shared (the AudioClip inspector uses it too). If anything else starts a
        /// preview, Unity silently replaces ours. We detect that by comparing Unity's reported position with the
        /// position predicted from our own clock, and then report "not playing" so the transport resets cleanly.
        /// </summary>
        public bool IsPlaying
        {
            get
            {
                if (_clip == null) return false;
                if (_paused) return true;
                if (!(bool)IsPlayingM.Invoke(null, null) || IsHijacked()) { Release(); return false; }
                return true;
            }
        }

        private long Expected()
        {
            long pos = _startFrame + (long)(_clock.Elapsed.TotalSeconds * _rate);
            return _loop && _frames > 0 ? pos % _frames : pos;
        }

        private bool IsHijacked()
        {
            if (_clock.Elapsed.TotalSeconds < 0.5) return false; // let playback settle
            long actual = (int)PosM.Invoke(null, null);
            long expected = Expected();
            long diff = Math.Abs(actual - expected);
            if (_loop && _frames > 0) diff = Math.Min(diff, _frames - diff);
            if (!_loop && expected >= _frames) return false; // finishing: Unity will report not-playing
            return diff > _rate * 0.75;
        }

        public int PositionFrames => _clip == null ? 0 : (int)PosM.Invoke(null, null);

        public void Play(AudioBuffer buffer, int startFrame, bool loop)
        {
            Stop();
            _clip = PreviewPlayerFactory.CreateClip(buffer, "ACS Preview");
            _paused = false;
            _loop = loop;
            _frames = Math.Max(1, buffer.Frames);
            _rate = buffer.SampleRate;
            startFrame = Mathf.Clamp(startFrame, 0, Mathf.Max(0, buffer.Frames - 1));
            _startFrame = startFrame;
            PlayM.Invoke(null, new object[] { _clip, startFrame, loop });
            _clock.Restart();
        }

        public void Pause()
        {
            if (_clip == null) return;
            PauseM?.Invoke(null, null);
            _paused = true;
            _clock.Stop();
        }

        public void Resume()
        {
            if (_clip == null) return;
            ResumeM?.Invoke(null, null);
            _paused = false;
            _clock.Start();
        }

        public void Stop()
        {
            if (_clip != null) StopAllM?.Invoke(null, null);
            Release();
        }

        private void Release()
        {
            _paused = false;
            _clock.Reset();
            if (_clip != null) { UnityEngine.Object.DestroyImmediate(_clip); _clip = null; }
        }

        public void Dispose() => Stop();
    }

    /// <summary>
    /// Hidden AudioSource player (default). Plays through Unity's regular mixer in Edit Mode. If no enabled
    /// AudioListener exists in the loaded scenes, a hidden listener is created for the duration of playback.
    /// </summary>
    public sealed class AudioSourcePreviewPlayer : IPreviewPlayer
    {
        private GameObject _go;
        private AudioSource _src;
        private AudioListener _ownListener;
        private AudioClip _clip;
        private bool _paused;

        public string BackendName => _ownListener != null ? "AudioSource (own listener)" : "AudioSource";

        private void Ensure()
        {
            if (_src == null)
            {
                _go = EditorUtility.CreateGameObjectWithHideFlags("ACS Preview Source", HideFlags.HideAndDontSave, typeof(AudioSource));
                _src = _go.GetComponent<AudioSource>();
                _src.playOnAwake = false;
                _src.spatialBlend = 0f;
                _src.bypassListenerEffects = true;
                _src.bypassReverbZones = true;
                _src.priority = 0;
            }
            bool listener = false;
            foreach (var l in UnityEngine.Object.FindObjectsOfType<AudioListener>())
                if (l != _ownListener && l.isActiveAndEnabled) { listener = true; break; }
            if (!listener && _ownListener == null)
            {
                var lg = EditorUtility.CreateGameObjectWithHideFlags("ACS Preview Listener", HideFlags.HideAndDontSave, typeof(AudioListener));
                _ownListener = lg.GetComponent<AudioListener>();
            }
            else if (listener && _ownListener != null) DestroyListener();
        }

        private void DestroyListener()
        {
            if (_ownListener != null) UnityEngine.Object.DestroyImmediate(_ownListener.gameObject);
            _ownListener = null;
        }

        public bool IsPlaying => _src != null && _clip != null && (_paused || _src.isPlaying);
        public int PositionFrames => _src != null && _clip != null ? _src.timeSamples : 0;

        public void Play(AudioBuffer buffer, int startFrame, bool loop)
        {
            Stop();
            Ensure();
            _clip = PreviewPlayerFactory.CreateClip(buffer, "ACS Preview");
            _src.clip = _clip;
            _src.loop = loop;
            _src.volume = 1f;
            _src.timeSamples = Mathf.Clamp(startFrame, 0, Mathf.Max(0, buffer.Frames - 1));
            _src.Play();
            _paused = false;
        }

        public void Pause() { if (_src != null) { _src.Pause(); _paused = true; } }
        public void Resume() { if (_src != null) { _src.UnPause(); _paused = false; } }

        public void Stop()
        {
            if (_src != null) { _src.Stop(); _src.clip = null; }
            _paused = false;
            if (_clip != null) { UnityEngine.Object.DestroyImmediate(_clip); _clip = null; }
        }

        public void Dispose()
        {
            Stop();
            DestroyListener();
            if (_go != null) UnityEngine.Object.DestroyImmediate(_go);
            _go = null;
            _src = null;
        }
    }
}
