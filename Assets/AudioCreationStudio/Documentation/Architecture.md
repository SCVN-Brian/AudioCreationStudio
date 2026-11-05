# Architecture

```
AudioCreationStudio/
├── Runtime/   AudioCreationStudio.Runtime   (pure C# DSP + models; no UnityEditor)
│   ├── Core/          DspMath, SampleTime, DeterministicRandom, StableHash
│   ├── Models/        RecipeData, TrackData, ClipData, EffectData, Automation, GeneratorSettings, settings
│   ├── DSP/Buffers    AudioBuffer (planar float), ChannelConverter
│   ├── DSP/Resampling SincInterpolator (ISampleInterpolator), Resampler
│   ├── DSP/Mixing     MixRenderer, ClipRenderer, PanLaw, FadeCurves, TrackRenderCache
│   ├── DSP/Effects    IEffectProcessor, EffectDefinition/Registry, EffectChainProcessor, 9 processors, Biquad/SVF
│   ├── DSP/Generators ProceduralSynth, GeneratorPresets, GeneratorRandomizer
│   ├── DSP/Analysis   WaveformPeaks, SignalAnalysis, ZeroCrossing
│   ├── DSP/Loop       SeamlessLoop
│   ├── DSP/Variations VariationGenerator
│   ├── Editing/       TimelineEditing (pure, testable edit operations)
│   ├── Export/        WavEncoder/Decoder, ExportPipeline
│   └── Recipes/       AudioRecipe, AudioPreset (ScriptableObjects)
├── Editor/    AudioCreationStudio.Editor    (UI Toolkit; Editor-only)
│   ├── Session/       StudioSession (state, undo, file ops), SourceLibrary, StudioContext, StudioCommands
│   ├── Preview/       PreviewRenderService, RenderRequestFactory, IPreviewPlayer implementations
│   ├── Transport/     TransportController
│   ├── Timeline/      ArrangeView, TimelineCanvas, TimelineRuler, TrackHeaders, view state, snapping
│   ├── Waveform/      WaveformCache, BufferWaveformView, CurvePlotView
│   ├── Inspectors/    InspectorPanel, Fields, AutomationCurveElement
│   ├── Effects/       EffectsRack, response views
│   ├── Mixer/ Generator/ LoopEditor/ Variations/ Presets/ Export/ Demo/ Windows/
│   ├── Controls/      Buttons, sliders, knob, fader, meter, switch, cards, vector icons
│   └── Styling/       StudioTheme (tokens), StudioStyles.uss, PaintUtil/QuadBatch
├── Samples/   AudioCreationStudio.Samples   (DemoSfxPlayer) + generated audio, recipes, demo scene
└── Tests/     AudioCreationStudio.Tests     (EditMode, Editor-only)
```

## Key boundaries

| Interface | Purpose | Implementations |
|---|---|---|
| `ISampleInterpolator` | Fractional reads (pitch / SRC) | `SincInterpolator` |
| `IEffectProcessor` | Stateful DSP | Gain, Filter, EQ, Compressor, Limiter, Distortion, Delay, Reverb |
| `EffectRegistry` | Open/closed effect catalogue | Built-ins + `EffectRegistry.Register(...)` for new effects |
| `ITrackRenderCache` | Incremental rendering | `TrackRenderCache` |
| `IPreviewPlayer` | Editor playback | `AudioUtilPreviewPlayer`, `AudioSourcePreviewPlayer` (fallback) |

## Threading and ownership

* **Main thread** builds a `RenderJob`. It clones `RecipeData` and decodes AudioClips through `SourceLibrary`, since Unity APIs may only be called here.
* **Worker threads** run `MixRenderer`, the synth, peak analysis and WAV encoding. They only see cloned data and decoded `AudioBuffer`s, never Unity objects.
* A rendered `AudioBuffer` is immutable once published: caches and meters only read it.
* Results come back to the main thread through queues pumped from `EditorApplication.update`.
* No audio-thread callbacks exist. Preview plays pre-rendered buffers, so nothing allocates on the audio thread.

## Editing and undo

All mutations go through `StudioSession.Modify` (one undo step) or `BeginContinuous/Continuous/EndContinuous` (a drag collapsed into one step). The session edits a hidden working copy of the recipe (`HideAndDontSave`), recorded with Unity's `Undo` system. **Save** copies it into the asset, and **Revert** reloads from the asset.
