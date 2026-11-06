# Audio Export (WAV · AIFF · FLAC · MP3 · OGG)

| File type | Encoder | Bit depth / quality | Notes |
|---|---|---|---|
| **WAV** | pure C# | PCM 16 / 24, Float 32 | The only format with `smpl` loop metadata |
| **AIFF** | pure C# | PCM 16 / 24, Float 32 (written as AIFF-C `fl32`) | Big-endian, bit-identical to WAV after decoding |
| **FLAC** | pure C# | 16 / 24 (Float 32 is stored as 24-bit) | Lossless (FIXED predictors + Rice coding, stereo decorrelation), STREAMINFO MD5. Unity does not import `.flac` as an AudioClip |
| **MP3** | ffmpeg (`libmp3lame`) | 64–320 kbps | Max 48 kHz (higher rates are resampled), max stereo. Encoder padding means it is not gapless, so avoid it for loops |
| **OGG** | ffmpeg (`libvorbis`, or the built-in `vorbis` encoder) | 64–320 kbps | ffmpeg's built-in Vorbis is stereo-only, so mono is upmixed unless your ffmpeg has libvorbis |

**ffmpeg** is needed only for MP3/OGG. It is found automatically (PATH, Homebrew `/opt/homebrew/bin`, `/usr/local/bin`, WinGet/Scoop/Chocolatey) or set
per machine in *Export ▸ Format ▸ ffmpeg* (stored in EditorPrefs, not in the recipe). The mix is handed to ffmpeg as a 32-bit float WAV, so
ffmpeg is the only lossy step. Each encoded file is decoded once by ffmpeg before being moved into place.

Lossless exports are validated by re-reading the header (WAV/AIFF) or decoding the whole file and checking frame CRCs and MD5 (FLAC).

## WAV details

* **Formats:** PCM 16-bit, PCM 24-bit, IEEE Float 32-bit (with `fact` chunk), little-endian and interleaved.
* **PCM scaling:** `clamp(round(x · 2^(n−1)), −2^(n−1), 2^(n−1)−1)`, so +1.0 maps to full scale minus 1 LSB. Optional TPDF dither (±1 LSB, deterministic) for 16-bit.
* **Scope:** Full Mix, Selected Track (soloed through master), Selected Clip (its span plus tail), Loop Region.
* **Options:** sample rate (sinc SRC), mono/stereo conversion, include tails, **normalize** to a target peak (off by default; the mix is never normalized silently), **trim silence** with a threshold.
* **Safety:** each file is written to `<name>.wav.partial~` (Unity ignores `~` files), re-read to validate the RIFF/WAVE header, format, channel count and frame count, then moved into place. Cancellation or errors delete the temp file and leave any existing file untouched (tested).
* **Progress and cancel:** the export sheet shows progress and a Cancel button. Rendering and encoding run on a worker thread.
* **AssetDatabase:** files written under `Assets/` are imported automatically.
* **Batch export:** Variations ▸ *Batch Export* (uses the selected file type). `DemoContent.Build` exports every demo recipe.
* **Loop exports** add a `smpl` chunk (see LoopEditor.md).
