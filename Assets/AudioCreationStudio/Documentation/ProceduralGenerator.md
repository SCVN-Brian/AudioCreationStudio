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

The card at the top of the Generator tab generates **real audio** with the ElevenLabs text-to-sound-effects API. You then edit the result with the studio's tools: trim, fades, playback rate/pitch, EQ and effects, the Loop editor, and Variations.

1. **API Key**: paste an ElevenLabs API key (elevenlabs.io ▸ Developers ▸ API Keys). It is stored in EditorPrefs on this computer only, never in the project. `ELEVENLABS_API_KEY` is used if no key is saved.
2. **Describe the sound**. English prompts work best.
3. Options:
   - **Auto Duration**, or a fixed duration from 0.5 to 30 s.
   - **Prompt Influence** (default 30%): higher follows the text more literally.
   - **Seamless Loop**: for ambiences and engines.
   - **Model**: `eleven_text_to_sound_v2` (default) or `_v3`.
   - **Takes**: 1–4 versions per click, each costing credits.
4. *(Optional)* **Reference**: ElevenLabs only accepts text. **Use** sets the duration to the reference's length and appends a timing/character hint from the local analysis to the prompt, for example "2 separate hits over 0.3 seconds, sharp attack, tonal, mid pitch". **A/B** plays the reference, then a take.
5. **Generate with ElevenLabs**. Each take is saved to `Assets/AudioCreationStudio/Generated/ElevenLabs/` as an AudioClip, with a `.txt` sidecar recording the prompt and settings. The first take plays automatically.
6. Each take has these buttons: ▶ (play), **A/B**, **Edit in Generator ↓**, **Insert** (adds it on a new track at the playhead), **Loop** (opens it in the Loop editor), show the asset, and delete. Takes from earlier sessions are listed again when the panel opens.

### Sample source: make variants of a take
**Edit in Generator ↓** loads a take into the generator below as **Oscillator ▸ Sample**. It starts from neutral settings, so it plays unchanged. Every generator control then processes the sample:
- Pitch start/end/sweep/jump/vibrato/noise FM transpose it in semitones (varispeed: higher pitch also plays faster).
- The ADSR shapes and shortens it.
- Filter (with envelope/LFO), noise mix, tremolo, drive and volume all apply.
- **Start Offset** skips into the sample, and **Fit Duration to Sample** matches its length.
- **Randomize** and **Seed** give quick variants.
- Everything is undoable, and **Insert Into Timeline** / **Save Preset** work as for any generator sound.

Any AudioClip can be used the same way: choose **Sample** and assign the clip. The sample is played mono, and the base Frequency and Sub Osc don't apply to it.

How the request is made:
- Call: `POST https://api.elevenlabs.io/v1/sound-generation?output_format=wav_48000` with header `xi-api-key`.
- Format: `wav_48000` is lossless and matches the studio's sample rate (44.1 kHz WAV/PCM requires a Pro plan; 48 kHz is not tier-gated). If a 403 or format error comes back, the request is retried once as `mp3_44100_128`.
- Saving: WAV responses are validated before saving, and raw `pcm_*` responses are wrapped in a WAV header.
- Rights: the rights to use the generated audio follow your ElevenLabs plan's terms.
