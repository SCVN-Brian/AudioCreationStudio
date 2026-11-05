# Effects

Every effect supports **Enable** (the power switch; disabled effects are removed from processing), **Bypass** (BYP; audio passes through untouched and the slot is kept), **Reset**, **Presets** (⋯ ▸ Save/Load Preset), **Reorder** (▲/▼; the order is the processing order) and serialization. Chains can be saved and loaded as presets (rack ⋯ menu). All continuous parameters can be automated.

| Effect | Parameters | Notes |
|---|---|---|
| Gain | Gain dB | 10 ms smoothing |
| Low-Pass / High-Pass | Cutoff (log), Resonance Q | RBJ biquad, log-domain cutoff smoothing per 32 samples; response curve drawn from the real coefficients |
| Parametric EQ | Low shelf (freq, gain), Mid 1 & 2 (freq, gain, Q), High shelf (freq, gain) | Four cascaded biquads, combined magnitude plotted |
| Compressor | Threshold, Ratio, Attack, Release, Knee, Makeup, Mix | Stereo-linked peak detector, soft-knee gain computer in dB, transfer curve shown |
| Limiter | Ceiling, Input Gain, Release | Zero-latency brickwall: output ≤ ceiling is guaranteed (tested); no latency, so timeline alignment is kept |
| Distortion | Mode (Soft tanh / Hard / Fold), Drive, Tone, Mix, Output | One-pole tone filter after the shaper |
| Delay | Time, Feedback, Mix, Damping, Ping-Pong | Fractional delay with smoothed time; damping filter in the feedback path; tail length estimated from feedback |
| Reverb | Room Size, Damping, Width, Pre-Delay, Mix | Freeverb topology (8 combs + 4 all-passes per channel), sample-rate scaled |

Tails: the renderer extends each track by its chain's tail estimate (capped at 10 s) and trims the master below −96 dBFS. Export can include or exclude tails.

**Adding a custom effect:** implement `IEffectProcessor` (or derive from `EffectProcessorBase`) and call `EffectRegistry.Register(new EffectDefinition(...))` from an `[InitializeOnLoad]` class. The UI, presets, serialization and automation work without further changes.
