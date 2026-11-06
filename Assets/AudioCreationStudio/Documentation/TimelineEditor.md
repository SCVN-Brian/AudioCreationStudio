# Timeline Editor

## Edit tools

The four tool buttons on the toolbar (next to Split and Snap) work like DaVinci Resolve's edit modes:

| Tool | Key | What dragging does |
|---|---|---|
| **Selection** | A | Everything in the *Mouse* table below: select, move, trim edges (leaves a gap), fades |
| **Trim Edit** | T | **Body (waveform) → Slip**: moves the audio *inside* the clip, while the clip keeps its position and length. While you drag, the whole source is drawn faintly behind the clip so you can see which part is in the cut.<br>**Name bar → Slide**: moves the clip, and the clips touching it get longer or shorter so no gap opens.<br>**Edge → Ripple trim**: shortens or lengthens the clip, and every later clip on the track moves with it.<br>**Cut between two touching clips (±3 px) → Roll**: moves the cut, so one clip gets longer and the other shorter; nothing else moves. |
| **Blade** | B | Click to cut the clip at that point (snaps). **Shift-click** cuts every track at that point. A yellow line previews the cut. |
| **Dynamic Trim** | W | Same gestures as Trim Edit. After each edit, the area around it plays automatically (the whole clip for slip/slide, ±1 s around a cut for roll/ripple) |

While dragging, a readout shows how far you have moved (e.g. `Slip −100.0 ms · in 00:00.100`). Each drag is a single undo step.
Slip, roll and ripple never go past the ends of the source audio, and looping clips can slip freely.

`,` / `.` nudge the selected clips by 10 ms (Shift: 100 ms). In Selection mode they move the clips; in Trim and Dynamic Trim they slip the
audio inside the clips, and Dynamic Trim then plays the result.

## Mouse (Selection tool)

| Action | Gesture |
|---|---|
| Select clip | Click (Shift/⌘ to add or remove) |
| Move | Drag the clip body, vertically to change track |
| Duplicate by dragging | Alt-drag |
| Trim | Drag the left/right edge (cursor changes) |
| Fade | Drag the round handle at the top-left/top-right corner |
| Preview clip | Double-click the clip |
| Move playhead / scrub | Click or drag empty lane or ruler |
| Loop region | Shift-drag in the ruler; drag its edges or bar; double-click the bar to toggle |
| Context menu | Right-click a clip, lane or automation point |
| Zoom | ⌘/Ctrl + scroll, the −/+/Fit buttons, ⌘= / ⌘− |
| Scroll | Scroll (vertical), Shift+scroll or trackpad (horizontal) |
| Reorder tracks | Drag a track header vertically |
| Rename track | Double-click its name |
| Import | Drag AudioClips from the Project window |
| Bypass snapping | Hold Ctrl while dragging |

## Keyboard (only while the studio window has focus)

| Key | Action |
|---|---|
| Space | Play / Pause (in Generator: preview the sound) |
| Esc | Stop (or close the export sheet) |
| Enter | Stop and return to start |
| S | Split at playhead (selected clips, or clips under the playhead) |
| A / T / B / W | Selection / Trim Edit / Blade / Dynamic Trim tool |
| , / . | Nudge the selected clips by 10 ms (Shift: 100 ms). This moves them in Selection mode and slips their audio in Trim / Dynamic Trim |
| L | Toggle loop playback |
| Delete / Backspace | Delete selected clips |
| ⌘D / Ctrl+D | Duplicate (Unity's Duplicate command) |
| ⌘C / ⌘V / ⌘X | Copy / Paste at playhead / Cut |
| ⌘A | Select all clips |
| ⌘Z / ⌘⇧Z | Undo / Redo (Unity's global undo) |

## Automation

Click the curve icon on a track header to show its automation lane, then choose the target (Track Gain, Track Pan, or any effect parameter) from the dropdown. Click the lane to add points, drag to move them (Shift = value only), Alt-click or double-click to delete, and right-click for Linear / Smooth / Step. Clip gain and pan automation is edited in the clip inspector. Automation overrides the static value while a lane has points.

## Snapping

Targets: timeline start, playhead, loop region edges, other clip edges, and the minor grid. The threshold is 8 px. A dashed amber line shows the active snap.
