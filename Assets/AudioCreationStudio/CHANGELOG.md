# Changelog

All notable changes to this package are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
