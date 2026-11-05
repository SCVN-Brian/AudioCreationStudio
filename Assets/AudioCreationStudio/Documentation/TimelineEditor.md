# Timeline Editor

## Mouse

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
