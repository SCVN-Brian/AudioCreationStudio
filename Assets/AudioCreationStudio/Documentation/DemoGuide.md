# Demo Guide

`Tools ▸ Audio Creation Studio ▸ Open Demo` opens Demo A. All demos are listed under `Tools ▸ Audio Creation Studio ▸ Demos` and in the toolbar's Open menu. Demo assets are created on first use, and `Demos ▸ Rebuild Demo Assets` regenerates them. All source audio is synthesized by the tool itself (no third-party assets).

| Demo | Recipe | Shows | Output |
|---|---|---|---|
| A · Saw Machine | `Samples/Recipes/Demo_SawMachine` | 4 layers from WAV sources (Motor Loop, Blade Rotation, Wood Cutting, Wood Crack): looping clips, trim (source offset), fades with different curves, pan, gain automation (smooth spin-up/down), LP/HP/EQ/Distortion/Compressor/Reverb, master compressor + limiter | `SawMachine_Final.wav` |
| B · Coin Pickup | `Demo_CoinPickup` | Square oscillator with pitch jump, ADSR, fixed seed, sparkle layer, ping-pong delay, normalize on export | `CoinPickup_Final.wav` |
| C · Seamless Machine Loop | `Demo_MachineLoop` | Non-periodic 4 s machine; loop points snapped to zero crossings, 150 ms equal-power crossfade, loop region; open the Loop tab and try ×10 | `MachineLoop_Final.wav` (with `smpl`) |
| D · UI SFX Pack | `Demo_UI_Click`, `Demo_UI_Confirm`, `Demo_UI_Error`, `Demo_Upgrade` | Generator presets, two-tone layering, tremolo gating | `UI_Click.wav`, `UI_Confirm.wav`, `UI_Error.wav`, `Upgrade.wav` |

* Exports: `Samples/Demo/Exports/`. Sources: `Samples/Audio/`.
* Scene: `Samples/Demo/AudioStudioDemo.unity` (`Demos ▸ Open Demo Scene`). Enter Play Mode and press the on-screen buttons or keys 1 to 7. The machine loop toggles on and off.
