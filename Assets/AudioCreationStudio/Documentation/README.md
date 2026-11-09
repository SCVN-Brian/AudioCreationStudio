# Audio Creation Studio

A Unity-native mini DAW for game sound effects: multitrack editing, mixing, an effects rack,
automation, procedural synthesis, seamless loops, variations, presets and WAV export, all inside the
Unity Editor.

- **Unity:** 2022.3 LTS (developed and tested on 2022.3.19f1, macOS, Mono, .NET Standard 2.1)
- **Dependencies:** none beyond Unity's built-in modules (UI Toolkit, Audio). All DSP is pure C#.
- **Open:** `Tools ▸ Audio Creation Studio ▸ Open Studio` (⌥⇧⌘A / Ctrl+Alt+Shift+A) or double-click an Audio Recipe asset.
- **Demos:** `Tools ▸ Audio Creation Studio ▸ Open Demo`.

## Workflow

**Import → Arrange → Trim → Mix → Process → Preview → Export**

| Workspace | What it does |
|---|---|
| Arrange | Multitrack timeline: clips with real waveforms, trims, fades, automation lanes |
| Mixer | Channel strips with faders, pan, mute/solo, effect lists and live meters |
| Generator | Procedural SFX synth with live visualizations and 11 presets |
| Loop | Seamless loop editor with crossfade, boundary analysis and ×1/×5/×10 preview |
| Variations | Deterministic variants, A/B compare, batch export |
| Library | Presets for generators, effects, effect chains, recipes and export settings |

## Documents

| File | Topic |
|---|---|
| [AudioCreationStudio_Guide.html](AudioCreationStudio_Guide.html) | Illustrated user guide (single HTML page, open in a browser) |
| [QuickStart.md](QuickStart.md) | Your first sound effect in 5 minutes |
| [Architecture.md](Architecture.md) | Assemblies, layers, SOLID boundaries |
| [AudioRecipe.md](AudioRecipe.md) | The non-destructive recipe format |
| [TimelineEditor.md](TimelineEditor.md) | Timeline interactions and shortcuts |
| [DSPPipeline.md](DSPPipeline.md) | Signal flow, pan law, resampling, numerical guarantees |
| [VisualDesign.md](VisualDesign.md) | Design tokens and UI system |
| [Effects.md](Effects.md) | The nine effects and their parameters |
| [ProceduralGenerator.md](ProceduralGenerator.md) | Synth architecture and presets |
| [LoopEditor.md](LoopEditor.md) | Seamless loops and loop metadata |
| [AudioVariations.md](AudioVariations.md) | Variation system |
| [WAVExport.md](WAVExport.md) | Formats, validation, cancellation |
| [DemoGuide.md](DemoGuide.md) | The four demos |
| [Testing.md](Testing.md) | Automated and manual tests |
| [KnownLimitations.md](KnownLimitations.md) | What it does not do (yet) |
