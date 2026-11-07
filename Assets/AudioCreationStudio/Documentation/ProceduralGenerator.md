# Procedural Generator

Signal flow: **oscillator** (Sine, Square with duty cycle, Triangle, Saw; band-limited with PolyBLEP) or **noise** (white, pink via Paul Kellet's filter) + **sub-oscillator** → **noise mix** → **tremolo** → **state-variable filter** (LP/HP/BP; cutoff modulated by the amplitude envelope and an LFO) → **drive** (tanh) → **ADSR** → volume.

Pitch = base frequency × 2^(semitones/12), where semitones combine the start→end sweep (with curve exponent), an instant **jump** (for coin-style arpeggios), **vibrato**, and smoothed **noise FM** (for engine wobble).

* **One-shot** envelope: attack → decay → release (no sustain hold). Otherwise sustain is held until `duration − release`. A 2 ms anti-click ramp always ends the sound.
* **Deterministic:** identical settings and seed give bit-identical output (tested for every preset). **Randomize** derives new settings from the current settings and seed, then advances the seed.
* **Visualizations** call the same functions the renderer uses (`OscillatorValue`, `EnvelopeAt`, `PitchSemitonesAt`, filter coefficients), so the plots show what you will hear.

Presets: Coin Pickup, UI Click, Pop, Notification, Whoosh, Impact, Upgrade, Cartoon Jump, Machine Start, Machine Running, Machine Stop.

Actions: Preview (Space), Generate, Randomize, Seed / new seed, Save Preset, **Insert Into Timeline** (creates a generator clip at the playhead; later edits update that clip and can be undone), Export WAV.

Generator clips are rendered at the recipe sample rate and cached by settings hash.

## AI Sound Effects (ElevenLabs)

The **AI Generate** tab generates **real audio** with the ElevenLabs text-to-sound-effects API. You then edit the result with the studio's tools: trim, fades, playback rate/pitch, EQ and effects, the Loop editor, and Variations.

0. *(Optional)* **AI sound analysis → parameters → prompt**. This is three steps in the form:
   - **1 · Describe the sound** in Vietnamese or English, then click **Analyze Sound** (or **Analyze & Generate**). Gemini analyses the description into nine structured parameters, all in English: **Sound Source, Action / Event, Sound Texture, Pitch, Rhythm / Timing** (pattern, repetitions, spacing), **Intensity, Style, Duration / Structure** (seconds, attack, sustain, release) and **Constraints** (unwanted elements such as music, ambience, dialogue, reverb). It also recommends Seamless Loop and Prompt Influence. The answer is a typed JSON object (`SoundSpec`), not a paragraph. Values are clamped to ElevenLabs' limits and checked with the prompt rules (English, no quotes, onomatopoeia or instructions). If a rule is broken, the model is asked once to fix it. A duration or loop you set beforehand, and a loaded reference, are passed as requirements. A short explanation and any warning appear in your language.
   - **2 · Sound parameters**: review and override every field. ▾ menus offer common values (texture and constraints are comma-separated lists that the menu toggles). Fields you change are marked in amber, with the AI's suggestion in the tooltip, and **Reset to AI** restores them. You can also fill the fields in yourself without AI.
   - **3 · ElevenLabs prompt**: built locally from the parameters, with no second AI call, and updated on every edit. Order: intensity + source + action, texture, pitch, rhythm, envelope, style, constraints (as "no music…"). Moderate intensity, envelope on loops and sustain that repeats the rhythm are left out. Over 300 characters, the least important parts are dropped first. You can still edit the prompt by hand; parameter changes then stop overwriting it, and **Rebuild from Parameters** brings it back. The take's `.txt` sidecar records your description, the analyzer, how many parameters you edited and the full parameter JSON.

   The default analyzer is **Google Gemini**, with a free API key: open [aistudio.google.com/apikey](https://aistudio.google.com/apikey), sign in with your Google account, click **Create API key** and paste it in Preferences ▸ Audio Creation Studio ▸ Prompt writer (or set `GEMINI_API_KEY`). The model defaults to the `gemini-flash-lite-latest` alias (newest Flash-Lite, about 2–3 s per prompt). If the chosen model gives no answer within 20 s, is overloaded (5xx), rate-limited (429) or missing (404), `gemini-3.5-flash-lite` and `gemini-3.1-flash-lite` are tried automatically, and the status line names the model that answered. The free tier has per-minute and per-day limits. You can switch the provider to **OpenAI** there, with an OpenAI API key and a model that supports Structured Outputs (default `gpt-4.1-mini`).
1. **API Key**: paste an ElevenLabs API key (elevenlabs.io ▸ Developers ▸ API Keys) in Preferences ▸ Audio Creation Studio (the ⚙ on the card opens it). It is stored in EditorPrefs on this computer only, never in the project. `ELEVENLABS_API_KEY` is used if no key is saved.
2. **Describe the sound**. English prompts work best.
3. Options:
   - **Auto Duration**, or a fixed duration from 0.5 to 30 s.
   - **Prompt Influence** (default 30%): higher follows the text more literally.
   - **Seamless Loop**: for ambiences and engines.
   - **Model**: `eleven_text_to_sound_v2` (default) or `_v3`.
   - **Takes**: 1–4 versions per click, each costing credits.
4. *(Optional)* **Reference**: ElevenLabs only accepts text. **Use** sets the duration to the reference's length and appends a timing/character hint from the local analysis to the prompt, for example "2 separate hits over 0.3 seconds, sharp attack, tonal, mid pitch". **A/B** plays the reference, then a take.
5. **Generate with ElevenLabs**. Each take is saved as an AudioClip into the folder selected in the Take Library (below `Assets/AudioCreationStudio/Generated/ElevenLabs/`), with a `.txt` sidecar recording the prompt and settings. The first take plays automatically.
6. Each take has these buttons: ▶ (play), **A/B**, **Edit in Custom** (opens it in the Custom Generate tab), **Insert** (adds it on a new track at the playhead), **Loop** (opens it in the Loop editor) and **⋯** (move, rename, show in Project, delete).

### Take Library: organise takes in folders
The right side of the AI Generate tab is a folder tree (left) and the takes of the selected folder (right). **All Takes** shows every take, with a chip naming its folder.
- **New Folder** creates a folder inside the selected one and starts renaming it. Double-click a folder to rename it, right-click for New Folder Inside / Rename / Show in Project / Delete.
- **Drag** one or more takes onto a folder to move them. Drag a folder onto another folder to nest it. Drag AudioClips from the Project window onto a folder to copy them in. Hovering a collapsed folder during a drag opens it.
- **Select several** with Cmd/Ctrl-click or Shift-click. The selection bar offers **Move to…**, **Insert** and **Delete**.
- **Keys**: ↑/↓ select, Space plays, F2/Enter renames, Delete deletes, Cmd/Ctrl+A selects all, Esc clears.
- **Search** filters by file name and prompt.
- New generations go into the selected folder (the green "New takes →" pill shows where).
- Folders are real asset folders. Moves go through the AssetDatabase, so clip GUIDs survive (clips already on the timeline keep working), and each take's `.txt` sidecar moves with it. Deleting a folder that still has takes asks whether to keep them (moved up one level) or delete everything.

### Sample source: make variants of a take
**Edit in Custom** opens the **Custom Generate** tab with the take loaded as **Oscillator ▸ Sample**. It starts from neutral settings, so it plays unchanged. Every generator control then processes the sample:
- Pitch start/end/sweep/jump/vibrato/noise FM transpose it in semitones (varispeed: higher pitch also plays faster).
- The ADSR shapes and shortens it.
- Filter (with envelope/LFO), noise mix, tremolo, drive and volume all apply.
- **Start Offset** skips into the sample, and **Fit Duration to Sample** matches its length.
- **Randomize** and **Seed** give quick variants.
- Everything is undoable, and **Insert Into Timeline** / **Save Preset** work as for any generator sound.

**Edit a clip that is already on the timeline:** right-click it ▸ **Edit in Custom Generate**, or use the Inspector's Source section. The clip keeps its position, length, gain, pan, fades and loop, and plays the same part of the file. Custom Generate opens bound to it, so each change updates the clip live. One Cmd/Ctrl+Z restores the original clip, and **Revert to Original Audio** brings back the plain file later. Stereo files are played mono, with the level compensated. **Import any sound to edit:** click **Import Sound** next to the sound's name. You can browse for a file anywhere on disk, use the AudioClip selected in the Project window, or pick a recent AI take. You can also drag a sound file (wav, aiff, mp3, ogg, flac) or an AudioClip onto the tab. It loads as the Sample source with neutral settings. Files from outside the project are copied to `Assets/AudioCreationStudio/Imported/`, and the original is never modified. Any AudioClip can be used the same way: choose **Sample** and assign the clip. The sample is played mono, and the base Frequency and Sub Osc don't apply to it.

How the request is made:
- Call: `POST https://api.elevenlabs.io/v1/sound-generation?output_format=wav_48000` with header `xi-api-key`.
- Format: `wav_48000` is lossless and matches the studio's sample rate (44.1 kHz WAV/PCM requires a Pro plan; 48 kHz is not tier-gated). If a 403 or format error comes back, the request is retried once as `mp3_44100_128`.
- Saving: WAV responses are validated before saving, and raw `pcm_*` responses are wrapped in a WAV header.
- Rights: the rights to use the generated audio follow your ElevenLabs plan's terms.
