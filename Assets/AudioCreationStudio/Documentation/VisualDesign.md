# Visual Design

## Tokens

Defined once as USS custom properties (`Editor/Styling/StudioStyles.uss`, `.acs-root`) and mirrored in `StudioTheme.cs` for code-drawn content (Painter2D and meshes).

| Token | Value | Use |
|---|---|---|
| `--bg` | `#15181E` | Main background, timeline |
| `--surface` | `#1D222B` | Panels, toolbar, master bar |
| `--elevated` | `#282E39` | Hover, selected, effect cards |
| `--border` | `#363D49` | Separators |
| `--text` / `--text-dim` / `--text-faint` | `#E8ECF2` / `#9CA6B5` / `#6B7484` | Typography hierarchy |
| `--accent` | `#5B9DFF` | Primary actions, focus, selection |
| `--success` / `--warning` / `--error` | `#42C9A0` / `#F0B35A` / `#F06C78` | Solo / mute & meter yellow / clipping |
| `--inset` | `#11141A` | Wells: fields, meters, plots |

Track colors come from an 8-color palette (`TimelineEditing.TrackPalette`). Each track's color is reused for its header strip, clips, waveform, automation lane, mixer strip and controls.

## Rendering

* **Waveforms:** batched quads (`QuadBatch`) with one column per *physical* pixel (`EditorGUIUtility.pixelsPerPoint`), so they stay crisp on high-DPI screens. Each column shows the min/max envelope plus a brighter RMS core, read from a multi-resolution peak pyramid built on a worker thread. Samples are never drawn one by one, and repaints never re-analyse audio.
* **Shapes:** Painter2D rounded rects, curves and arcs for clips, fades, automation, knobs, faders, response curves and the vector icon set (`Icons.cs`; no texture assets).
* **Text:** pooled `Label` elements.

## States

Hover (lighter surface), active, selected (white clip outline, elevated header, accent strip), disabled (35% opacity), keyboard focus (accent ring), toggled (accent-soft background). Transitions are kept short (0.12 to 0.35 s) and only animate color, opacity and position.

## Layout

Toolbar (top) · track headers and timeline in a resizable split (center) · contextual inspector in a resizable split (right; hidden for full-width workspaces) · master bar (bottom). Breakpoints set from code: `< 1380 px` (`.acs-compact`) hides toolbar labels, and `< 1000 px` (`.acs-narrow`) tightens the clock, meters and inspector labels. Minimum window size is 900 × 520.
