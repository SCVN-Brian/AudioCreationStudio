using System;
using System.Collections.Generic;
using UnityEngine;

namespace AudioCreationStudio.Samples
{
    /// <summary>
    /// Minimal in-game player for the sounds exported by the Audio Creation Studio demos.
    /// Enter Play Mode in the demo scene and use the on-screen buttons (or keys 1–9).
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class DemoSfxPlayer : MonoBehaviour
    {
        [Serializable]
        public class Entry
        {
            public string label;
            public AudioClip clip;
            public bool loop;
        }

        public List<Entry> sounds = new List<Entry>();
        [Range(0f, 1f)] public float volume = 0.9f;

        private AudioSource _oneShots;
        private AudioSource _loopSource;
        private GUIStyle _button, _title, _note;

        private void Awake()
        {
            _oneShots = GetComponent<AudioSource>();
            _oneShots.playOnAwake = false;
            _loopSource = gameObject.AddComponent<AudioSource>();
            _loopSource.playOnAwake = false;
            _loopSource.loop = true;
        }

        private void Update()
        {
            for (int i = 0; i < sounds.Count && i < 9; i++)
                if (Input.GetKeyDown(KeyCode.Alpha1 + i)) Play(sounds[i]);
        }

        public void Play(Entry e)
        {
            if (e?.clip == null) return;
            if (e.loop)
            {
                if (_loopSource.isPlaying && _loopSource.clip == e.clip) { _loopSource.Stop(); return; }
                _loopSource.clip = e.clip;
                _loopSource.volume = volume;
                _loopSource.Play();
            }
            else _oneShots.PlayOneShot(e.clip, volume);
        }

        private void OnGUI()
        {
            if (_button == null)
            {
                _button = new GUIStyle(GUI.skin.button) { fontSize = 16, fixedHeight = 44 };
                _title = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };
                _note = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true };
            }
            GUILayout.BeginArea(new Rect(24, 24, 420, Screen.height - 48));
            GUILayout.Label("Audio Creation Studio — Demo Sounds", _title);
            GUILayout.Label("Exported WAVs played through a regular AudioSource. Keys 1–9 also work. Looping sounds toggle on/off.", _note);
            GUILayout.Space(10);
            for (int i = 0; i < sounds.Count; i++)
            {
                var e = sounds[i];
                string state = e.loop && _loopSource != null && _loopSource.isPlaying && _loopSource.clip == e.clip ? "  ■ stop" : "";
                GUI.enabled = e.clip != null;
                if (GUILayout.Button($"{i + 1}.  {e.label}{(e.loop ? "  (loop)" : "")}{state}", _button)) Play(e);
                GUI.enabled = true;
            }
            GUILayout.EndArea();
        }
    }
}
