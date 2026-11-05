# Audio Recipe

An `AudioRecipe` asset (`Create ▸ Audio Creation Studio ▸ Audio Recipe`, or **Save** in the studio) stores a non-destructive description of a sound:

| Field | Meaning |
|---|---|
| `name`, `sampleRate`, `channels` | Output format. Timeline positions are integer samples at `sampleRate`. |
| `masterGainDb`, `masterEffects` | Master bus |
| `tracks[]` | `name, color, gainDb, pan, mute, solo, effects[], automation[], clips[]` |
| `clips[]` | `sourceKind` (AudioClip / Generator), `sourceClip` (asset reference), `generator` (settings), `startSample`, `sourceOffset` (source frames), `lengthSamples`, `gainDb`, `pan`, `playbackRate`, `fadeIn/fadeOut` (length + curve), `loop`, `enabled`, clip automation |
| `loopRegion` | Timeline loop region used for loop playback and *Loop Region* export |
| `loop` | Loop editor settings (source, points, crossfade) |
| `export` | Export settings (format, rate, channels, normalize, trim, destination) |
| `variations` | Variation ranges and seed |

* **Source references** are normal Unity object references (GUID + fileID), so moving or renaming clips keeps recipes intact.
* **Missing sources** render as silence, are shown as red "Missing source" clips, and are listed in the master inspector.
* **Original assets are never modified.** WAV sources are read straight from the original file. Other formats are read with `AudioClip.GetData`, which requires *Decompress On Load*. Import settings are never changed automatically.
* **File operations:** New, Open (toolbar menu or double-click), Save, Save As, Duplicate, Revert (save menu ▾).
* `formatVersion` allows future migrations.
