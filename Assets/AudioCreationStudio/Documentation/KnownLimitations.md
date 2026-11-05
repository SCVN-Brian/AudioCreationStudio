# Known Limitations

* **Editor playback** uses Unity's internal `AudioUtil` preview API (through reflection; the same one the AudioClip inspector uses). If a future Unity version removes it, a hidden-AudioSource fallback is used, which needs an AudioListener in the open scene. Playback position comes from Unity's preview clip, and device output latency is not compensated. The preview channel is shared with the AudioClip Inspector: pressing ▶ on a clip in the Inspector stops the studio's playback (the studio detects this and resets; press Play again).
* **No sound at all?** Unity sometimes loses its output device (sleep, headphones/Bluetooth switching). Use ⚙ ▸ *Reset Audio Output*; if that doesn't help, restart Unity.
* **Preview renders the whole mix offline** (debounced, cached per track) rather than streaming. This suits SFX lengths (seconds to about a minute); renders are capped at 10 minutes.
* **Source formats:** WAV is decoded directly from the file. MP3/OGG/AIFF/FLAC need *Load Type = Decompress On Load*, and the tool reports when they cannot be read instead of changing settings.
* **No oversampling** in Distortion (aliasing possible at extreme drive) and **no lookahead** in the Limiter (zero latency was chosen to keep timeline alignment exact).
* **Pitch variations are varispeed** (pitch and duration change together); there is no time-stretching.
* Generator voices are **mono**. Width comes from pan, delay ping-pong and reverb.
* Menu: Unity cannot make `Tools ▸ Audio Creation Studio` both a command and a submenu, so the window is `Tools ▸ Audio Creation Studio ▸ Open Studio` (also `Window ▸ Audio ▸ Audio Creation Studio`).
* Timeline gestures (drag, trim, fade, reorder) were exercised by hand and through the command/undo tests. There are no automated pointer-event UI tests.
* Verified on Unity 2022.3.19f1 / macOS (Metal, Retina). Windows/Linux Editors and Unity 6 have not been tested.
* `smpl` loop metadata is ignored by Unity's importer (see LoopEditor.md).
* **AI Sound Effects (ElevenLabs):**
  * It needs an internet connection and your own ElevenLabs API key. Each take uses credits from your plan, and usage rights for the generated audio follow your ElevenLabs plan.
  * Write prompts in English and describe the sound itself ("dog barking twice, close up"). Non-English text, instructions ("make…") and onomatopoeia ("woof woof") are often read aloud as speech. The panel warns before generating such prompts.
  * ElevenLabs accepts text only. A reference sound influences the result only through its duration and a short text hint derived from it.
  * The live API path was verified offline only: the URL/body shape, error parsing (both error shapes), WAV validation, raw-PCM wrapping and MP3 passthrough are unit-tested against the documented API. A real request was not run during development, because no key was available.
  * MP3 takes (fallback format) are read through Unity's decoder, so they need the default *Decompress On Load* import setting.
  * An earlier Claude-based "AI Sound Designer" was replaced by this integration at the user's request.
