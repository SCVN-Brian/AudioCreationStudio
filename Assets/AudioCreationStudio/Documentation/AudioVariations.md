# Audio Variations

Variations make repeated sounds less fatiguing. Each variant is the full recipe rendered through the full DSP pipeline with controlled random changes:

| Range | Effect |
|---|---|
| Pitch ± | Varispeed (duration scales; clip lengths compensated so the same source region plays) |
| Volume ± | Master gain offset |
| Start Offset | Extra offset into AudioClip sources |
| Timing ± | Shifts clip starts (clips at 0 stay anchored so the attack is not delayed) |
| Layer Drop | Probability of muting each layer; at least one layer is always kept |
| Effect Params | Moves continuous effect parameters by a fraction of their range |

Variant *i* with seed *s* is always identical (`DeterministicRandom.DeriveSeed(s, i)`). Actions: **Generate**, ▶ preview, **A/B** (original, then variant), **Play All in Sequence**, 💾 save the variant as a new recipe asset, ⬆ export one, or **Batch Export WAVs** (background, with progress and cancel).
