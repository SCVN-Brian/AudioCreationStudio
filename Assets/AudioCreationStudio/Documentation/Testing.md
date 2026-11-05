# Testing

Run with **Window ▸ General ▸ Test Runner ▸ EditMode ▸ AudioCreationStudio.Tests**.

## Automated (98 EditMode tests)

| Area | Tests (file) |
|---|---|
| Buffers / resampling / peaks | interleave round trip, channel conversion, DC preserved after 44.1→48 k (±1e-4), sine fidelity (±2e-3), aliasing < −40 dB on downsample, sample-time rescale, peak pyramid vs. raw min/max (`DSP/BufferAndResamplingTests`) |
| Mixing / editing | summation, sample-accurate placement, trim anchoring, split = identical audio (exact), −6.02 dB = ½, track × master gain, constant-power and balance pan laws with channel isolation, fade endpoints, equal-power −3 dB, mute/solo, scoped renders, playback rate, source SRC, NaN/Inf sanitizing + clipping report (no normalization), track cache reuse/invalidation, automation interpolation, clip gain automation, sample-rate change (`DSP/MixingTests`) |
| Effects | finite output for all effects, bypass/disable are exact pass-through, gain, LP/HP attenuation, biquad magnitude vs. measured EQ (±0.15 dB), block-size independence (±1e-5), effect order changes output, delay echo position, reverb tail kept/excluded, limiter ceiling, compressor static curve (±0.2 dB), effect-parameter automation, log param mapping (`DSP/EffectTests`) |
| Generator / loop / variations | determinism of every preset (bit-exact), seed sensitivity, presets audible/bounded/exact length/end silent, sine frequency accuracy, randomize determinism, pre-roll and overlap loop continuity, zero-crossing search, click detector, variation determinism, ≥ 1 layer kept (`DSP/GeneratorLoopVariationTests`) |
| WAV / export | RIFF header fields, PCM16 scaling/byte order/clamping, PCM24 round trip (1 LSB), float32 bit-exact + `fact`, stereo interleaving, `smpl` round trip, validated file and duration, SRC + channel conversion, normalization only when requested, silence trim, pre-cancel and mid-write cancel leave no file and keep the existing file, **preview ≡ export** (float32 bit-exact; PCM16 within ½ LSB), deterministic dither (`Export/WavExportTests`) |
| ElevenLabs + analyzer (offline) | documented endpoint/body/format (clamping, auto duration, escaping), both error shapes + status messages, audio passthrough, WAV validation, raw PCM wrapping, MP3 passthrough, reference hints, analyzer pitch/noise/falling pitch, FFT round trip (`Editor/ElevenLabsTests`) |
| Sample source | neutral settings reproduce the sample (±1e-5), +12 st doubles frequency, start offset/end, volume processing, missing sample = silence, hash includes sample, timeline generator clip renders with the main-thread-prepared sample (−12 st = half frequency), SetSample is undoable (`Editor/SampleSourceTests`) |
| Generator undo | standalone undo/redo, slider drag collapses into one step, generator undo leaves the recipe untouched (`Editor/GeneratorUndoTests`) |
| Editor integration | recipe asset save/reload equality and stable clip refs, deep clone, **source file bytes and import settings unchanged**, WAV source decoding, undo/redo incl. collapsed drags, commands (split/duplicate/copy/paste/delete), preview player resource cleanup, recovery when another preview takes over the shared Editor preview channel, preset store CRUD, render service ≡ direct render (`Editor/EditorIntegrationTests`) |

Tolerances: `Exact = 1e-6` for paths that should be exact up to float rounding, and documented per test otherwise.

## Manual listening checklist

1. Demo A: press Space and listen for the blade spin-up (automation), the crack with a short room, and no clicks at fade edges.
2. Mute and solo tracks during playback: the mix updates within about 0.1 s.
3. Demo C: Loop tab ▸ ×10 should play with no audible seam. Set the crossfade to 0 and the raw cut should click.
4. Generator: step through every preset with Space, then Randomize several times.
5. Export 16/24/32-bit, re-import, and compare in the AudioClip inspector.
6. Drag a 22.05 kHz or 44.1 kHz clip into a 48 kHz recipe and confirm the pitch is unchanged.
