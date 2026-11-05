# DSP Pipeline

```
for each audible track (mute/solo applied):
    clip render  → resample (windowed sinc) → clip gain × fade × clip automation → pan law → track bus
    track FX chain (block-based, 512 frames, parameter automation per block)
    track fader (gain dB + pan, per-sample automation)
sum → master FX chain → master gain → sanitize (NaN/Inf → 0) → tail trim (−96 dBFS) → output
```

The same `MixRenderer` produces preview audio and export audio.

## Timing

All positions are `long` sample indices at the recipe rate. For every output sample `t`, the source position is computed directly as `sourceOffset + (t − start) × step` with `step = playbackRate × sourceRate / recipeRate`. Nothing is accumulated, so there is no drift. When the step is exactly 1, samples are copied verbatim (bit-exact).

## Resampling

`SincInterpolator`: Blackman-windowed sinc with 8 zero crossings each side, a 512-phase kernel table, and normalised weights (DC is reproduced exactly). When reading faster than 1× (downsampling or pitch up), the kernel is stretched to low-pass at `0.95 / step` of Nyquist. Measured aliasing rejection is better than 40 dB (see tests).

## Pan law and channels

* Mono source → stereo: **constant power** (`cos/sin`), −3.01 dB per side at centre.
* Stereo source / track pan: **balance**, unity at centre, linear attenuation of the opposite side.
* Stereo → mono output: (L+R)/2. Mono → stereo export: duplicated.

## Fades

Linear, Equal Power (sin), Exponential (x²), S-Curve (raised cosine). Fade-in starts at exactly 0 and fade-out ends at exactly 0.

## Numerical guarantees

* No silent normalization; clipping is detected and reported (master bar, inspector, export summary).
* NaN/Inf are removed at the output and counted (`RenderResult.InvalidSamples`).
* Filter states use doubles with denormal flushing.
* Effects keep their state between blocks, and output does not depend on block size (tested at 64 vs 4096 frames, within 1e-5).

## Incremental rendering

Each track's post-fader output is cached under a 64-bit content hash (clip placement, gains, fades, effects, automation, source identity). Editing one track re-renders only that track. Preview renders are debounced (80 ms), cancellable and run on a worker thread.

Measured on an Apple Silicon MacBook Pro (Editor, Mono, Debug code):

| Operation | Time |
|---|---|
| Saw Machine demo, full render (4 tracks, 8 effects, 3.8 s) | 112 ms |
| Same, all tracks cached | 33 ms |
| Waveform peak pyramid, 60 s stereo | 26 ms |
| 2000 waveform column queries | 0.18 ms |
| Generator "Machine Running" (2 s) | 17 ms |
| Sinc resample 10 s stereo 48 → 44.1 kHz | 252 ms |
