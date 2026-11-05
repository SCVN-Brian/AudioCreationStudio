# WAV Export

* **Formats:** PCM 16-bit, PCM 24-bit, IEEE Float 32-bit (with `fact` chunk), little-endian and interleaved.
* **PCM scaling:** `clamp(round(x · 2^(n−1)), −2^(n−1), 2^(n−1)−1)`, so +1.0 maps to full scale minus 1 LSB. Optional TPDF dither (±1 LSB, deterministic) for 16-bit.
* **Scope:** Full Mix, Selected Track (soloed through master), Selected Clip (its span plus tail), Loop Region.
* **Options:** sample rate (sinc SRC), mono/stereo conversion, include tails, **normalize** to a target peak (off by default; the mix is never normalized silently), **trim silence** with a threshold.
* **Safety:** each file is written to `<name>.wav.partial~` (Unity ignores `~` files), re-read to validate the RIFF/WAVE header, format, channel count and frame count, then moved into place. Cancellation or errors delete the temp file and leave any existing file untouched (tested).
* **Progress and cancel:** the export sheet shows progress and a Cancel button. Rendering and encoding run on a worker thread.
* **AssetDatabase:** files written under `Assets/` are imported automatically.
* **Batch export:** Variations ▸ *Batch Export WAVs*. `DemoContent.Build` exports every demo recipe.
* **Loop exports** add a `smpl` chunk (see LoopEditor.md).
