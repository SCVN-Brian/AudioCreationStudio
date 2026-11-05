# Quick Start: your first sound effect

1. **Open the studio.** `Tools ▸ Audio Creation Studio ▸ Open Studio`. A new recipe opens with two empty tracks and a limiter on the master bus.
2. **Import audio.** Drag one or more AudioClips from the Project window onto a track. Dropping several clips below the last track stacks them as layers on new tracks. The toolbar's **Import** button also adds the clips selected in the Project window, or lets you browse for a file (files from outside the project are *copied* into `Assets/AudioCreationStudio/Imported`).
   *No audio yet?* Right-click an empty lane and choose **Insert Generated ▸ Pickups ▸ Coin Pickup**.
3. **Arrange.** Drag clips to move them (also across tracks). Snapping to the grid, clip edges and the playhead is on by default (magnet button). Hold **Ctrl** while dragging to bypass snapping, or **Alt**-drag to duplicate.
4. **Trim.** Hover a clip edge until the cursor changes, then drag. Trimming is non-destructive: the audio stays anchored in time.
5. **Fade.** Drag the round handles at the top corners of a clip. Choose curves in the Inspector.
6. **Mix.** Use each track header's gain slider, pan knob and **M**/**S** chips, or open the **Mixer** tab.
7. **Process.** Select a track and click **Add Effect** in the Inspector (for example High-Pass, then Compressor). The order of the cards is the processing order.
8. **Preview.** Press **Space**. Edits during playback are rendered in the background and playback continues with the new mix. Double-click a clip to hear it alone.
9. **Export.** Click **Export** (top right), choose *Full Mix*, *PCM 16-bit*, a folder under `Assets`, and click **Export WAV**. The file is imported automatically.
10. **Save.** Click the save icon to store the recipe as an asset. You can reopen and change it at any time, and the source clips are never modified.
