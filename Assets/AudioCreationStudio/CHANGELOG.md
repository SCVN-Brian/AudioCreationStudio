# Changelog

All notable changes to this package are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.3.0] - 2026-11-07

### Added
- **Edit a timeline clip in Custom Generate.** Right-click a clip ▸ **Edit in Custom Generate**, or use the button in the Inspector's Source section. An audio clip becomes a Sample-generator clip that plays the same audio: same file offset, length, playback rate, gain, pan, fades, loop and automation, with stereo sources lifted by the pan law so the loudness matches. Custom Generate then opens bound to the clip, so every pitch, envelope, filter, noise or drive change updates the clip on the timeline. One Cmd/Ctrl+Z restores the original clip, and **Revert to Original Audio** (right-click or Inspector) brings back the plain file at any time. The sample plays mono.
- **Import a sound into Custom Generate.** A new **Import Sound** button in the tab header offers three sources: Browse for a File… (any location; it remembers the last folder), Use Selected in Project, and Recent AI Takes. You can also drag a sound file from Finder/Explorer, or an AudioClip from the Project window, onto the tab; an overlay shows where to drop it. The sound becomes the Sample source with neutral settings, so it plays unchanged until you tweak pitch, envelope, filter, noise, drive or Randomize. Files from outside the project are copied to `Assets/AudioCreationStudio/Imported/` and the originals are never modified.
- **AI Sound Analyzer and ElevenLabs prompt builder in AI Generate.** The tab follows: description (Vietnamese or English) → AI analysis → nine structured sound parameters → review/override → prompt → generation. **Analyze Sound** asks the AI for a typed `SoundSpec` (JSON schema, not free text) with Sound Source, Action/Event, Texture, Pitch, Rhythm/Timing (pattern, repetitions, spacing), Intensity, Style, Duration/Structure (seconds, attack, sustain, release) and Constraints, all in English. It also recommends loop and prompt influence and returns an explanation and warnings in the user's language. Values are clamped to ElevenLabs' limits and checked against the prompt rules, with one fix-up round. Every parameter is editable, with ▾ suggestions, amber "edited" markers that show the AI value, and **Reset to AI**. The ElevenLabs prompt is built locally from the parameters (`PromptBuilder`), updates live, drops the least important parts beyond 300 characters, and can still be edited by hand (**Rebuild from Parameters**). The take's `.txt` stores the description and the parameter JSON. The default provider is **Google Gemini**, using a free API key from Google AI Studio (sign in with Google), model `gemini-flash-lite-latest` (about 2–3 s per prompt; if the model hangs, is busy, rate-limited or missing, `gemini-3.5-flash-lite` and `gemini-3.1-flash-lite` are tried automatically, 20 s per attempt), or `GEMINI_API_KEY`. OpenAI (API key, default `gpt-4.1-mini`) is available as an alternative. Both are set in Preferences ▸ Audio Creation Studio ▸ Prompt writer.

### Changed
- **AI Generate layout:** the prompt form and the Take Library are split by a draggable divider. The form starts at half the window, and the ratio you drag to is remembered and kept when the window is resized. Double-click the divider to go back to 50/50. The prompt boxes are twice as tall. When the library gets narrow, take rows wrap onto two lines and the folder tree slims down instead of overflowing.
- **AI Generate and Custom Generate are now separate tabs.** The ElevenLabs form and the Take Library have their own tab, with the library filling most of the width. The procedural synth is in **Custom Generate**. **Edit in Custom** on a take switches to that tab with the take loaded as the Sample source. A window saved on the old "Generator" tab reopens on Custom Generate.
- With seven tabs, the tab bar shows icons only below 1560 px window width (was 1380 px).

### Fixed
- **Custom Generate edits on a timeline clip** pushed an extra, empty undo step, so the first Cmd/Ctrl+Z appeared to do nothing. Each edit is now exactly one step.
- **Custom Generate shortened looping clips:** any edit cut a looping generator clip down to one pass of its source. Looping clips now keep their timeline length.
- **Layout overflow across the studio.** Every tab was checked at window widths from 950 to 1600 px. Long preset names in Custom Generate now end in "…" instead of forcing horizontal scrollbars, and the visualizer grid no longer pokes past its column. The BYP chip text is no longer clipped. The Loop tab's "Use as Timeline Loop Region" button and the Variations hint now have margins. Narrow-mode value labels ("±5% range") fit, and the toolbar switches to its narrow layout below 1150 px, so Export/Settings stay on screen with seven tabs. Vertical panels never show a horizontal scrollbar.
- The inspector stayed visible when the window opened on a tab that hides it.
- Icon-only tabs sometimes kept their labels after a script reload, pushing tabs off-screen.

### Added
- **Take Library for ElevenLabs takes.** Generated takes are now organised in folders: a folder tree on the left, the selected folder's takes on the right. Drag takes onto a folder to move them, drag a folder onto another to nest it, and drag AudioClips in from the Project window to copy them in. Hovering a collapsed folder during a drag opens it. Cmd/Ctrl- and Shift-click select several takes, and a selection bar offers Move to…, Insert and Delete. Right-click (or ⋯) opens Move to, Rename, Show in Project and Delete. The library also has search over names and prompts, inline rename (double-click a folder, F2 or Enter on a take) and keyboard shortcuts (↑/↓, Space to play, Delete, Cmd/Ctrl+A, Esc). New generations are saved into the selected folder. Folders are real asset folders under `Generated/ElevenLabs`, and moves go through the AssetDatabase, so clip GUIDs (and timeline references) survive and the prompt `.txt` moves with each take.

## [1.2.2] - 2026-11-06

### Fixed
- **The studio window had no styling when installed as a package** (everything stacked vertically). The stylesheet lookup matched `AudioCreationStudio` case-sensitively, but the package folder is `Packages/com.supercent.audiocreationstudio`. The stylesheet is now loaded by GUID. Tested with a git-URL install on Unity 2022.3.16 and 2023.2.20.
- WAV sources inside the package (the demo audio) are now read from their real location in `Library/PackageCache`, instead of failing and falling back to Unity's decoder.

## [1.2.1] - 2026-11-06

### Added
- Documentation: Vietnamese introduction (`GioiThieu_AudioCreationStudio.md`), an architecture diagram and screenshots.
- Sample ElevenLabs takes in `Generated/ElevenLabs/`.

### Changed
- Demo recipes Coin Pickup, UI Click and UI Error, and the exported `UI_Error.wav`.

## [1.2.0] - 2026-11-06

### Added
- `README.md`: installation guide (Vietnamese) covering install via `manifest.json` or Package Manager, updating, settings, export formats, edit tools and troubleshooting.
- **Preferences ▸ Audio Creation Studio** (also *Tools ▸ Audio Creation Studio ▸ Settings…*): one page for the ElevenLabs API key and the ffmpeg path. Both are stored per user in EditorPrefs and never written into the project.
- Using a feature whose setting is missing now logs a `Debug.LogWarning` that says where to set it. This applies to AI generation without an API key, and to MP3/OGG export without ffmpeg.

- **Timeline edit tools** (DaVinci Resolve style). Toolbar buttons and keys **A / T / B / W**:
  - **Selection**: works as before.
  - **Trim Edit**: *slip* (drag the body to move the audio inside the clip, with the full source shown behind it), *slide* (drag the name bar; the touching neighbours adjust), *ripple trim* (drag an edge; later clips follow) and *roll* (drag the cut between two touching clips).
  - **Blade**: click to cut; Shift-click cuts every track.
  - **Dynamic Trim**: the Trim Edit gestures, plus each edit is played back automatically.
  - `,` / `.` nudge the selected clips by 10 ms (Shift: 100 ms). In Selection mode they move the clips; in the trim modes they slip the audio.
  - While dragging, a readout shows the size of the edit.
  - New pure operations in `TimelineEditing`: `Slip`, `Slide`, `Roll`, `RippleTrimStart/End`, `SplitAll`, plus `TimelineToolsTests` (13 tests).

### Changed
- The ElevenLabs panel no longer has its own API key field. Its *Settings* button opens the Preferences page, and keys saved earlier still work.
- The Export panel only shows the ffmpeg status, with a button that opens Preferences.

## [1.1.0] - 2026-11-06

### Added
- **More export formats**: in addition to WAV, the Export panel now writes **AIFF**, **FLAC**, **MP3** and **OGG Vorbis**.
  - AIFF (PCM 16/24, AIFF-C float) and FLAC (lossless 16/24-bit, with MD5) are encoded in pure C#, with no dependencies.
  - MP3 and OGG are encoded through ffmpeg, auto-detected or set in the Export panel (64–320 kbps).
- Generator, Loop Editor and Variations exports (including batch) use the selected file type.
- The export results show the format, file size and any format adjustments (e.g. "resampled to 48000 Hz (MP3 limit)").
- `ExportFormatsTests`: 23 tests for AIFF/FLAC round-trips, corruption detection, the pipeline, and MP3/OGG (skipped without ffmpeg).

### Notes
- Loop metadata (`smpl`) is still written only to WAV.
- Existing recipes keep exporting WAV (`fileType` defaults to WAV).

## [1.0.0] - 2026-11-06

First release as a UPM package.

### Added
- **Arrange**: multitrack timeline with real waveforms, clip trims, fades and automation lanes.
- **Mixer**: channel strips with faders, pan, mute/solo, effect lists and live meters.
- **Effects rack**: nine pure-C# effects with savable effect chains.
- **Generator**: procedural SFX synth with live visualizations and 11 presets.
- **Loop editor**: seamless loops with crossfade, boundary analysis and x1/x5/x10 preview.
- **Variations**: deterministic variants, A/B comparison and batch export.
- **Library**: presets for generators, effects, effect chains, recipes and export settings.
- **Audio Recipe**: non-destructive recipe asset (double-click to open in the studio).
- **WAV export**: format options, validation and cancellation.
- **ElevenLabs** AI sound generation panel.
- Demo content (Saw Machine, Coin Pickup, Machine Loop, UI SFX Pack) and a demo scene.
- Editor tests (`AudioCreationStudio.Tests`).
- Documentation under `Documentation/`.
