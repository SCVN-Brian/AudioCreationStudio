# Seamless Loop Editor

1. Choose a source: **Mix** (the rendered recipe), **Clip** (a timeline clip's source; right-click a clip ▸ *Open in Loop Editor*), or **Asset** (any AudioClip).
2. Drag the green **START** and red **END** markers. With *Zero Snap* on, each marker jumps to the nearest rising zero crossing (within the search window) when you release it.
3. Set the **crossfade** length and curve (Linear or Equal Power).
   * **Pre-roll mode** (enough audio before START): the loop tail is blended with the audio before the start, so wrapping END → START is continuous and the loop length is preserved.
   * **Overlap mode** (no room before START): the head is blended with the tail, and the loop becomes shorter by the crossfade length.
4. Compare the **raw cut** and **crossfaded** boundary views, which zoom in on ±20 ms around the wrap point.
5. Read the **Boundary Analysis**:
   * *Jump*: the wrap discontinuity relative to the signal's typical sample-to-sample step (under 3× is classed as seamless).
   * *Level match*: RMS difference between the last and first 10 ms.
   * *Clicks*: isolated second-difference spikes detected on a 2× repeat.
6. Listen with **Loop ∞**, **×1**, **×5** or **×10**. Always confirm by ear: zero crossings and metrics do not guarantee that a loop sounds seamless.
7. **Export Loop WAV** writes the loop with a RIFF `smpl` chunk (one forward loop covering the whole file). **Use as Timeline Loop Region** copies the points to the recipe's loop region.

Loop metadata: Unity's AudioImporter ignores `smpl`, so set *Loop* on the AudioSource. Samplers and middleware (Wwise, FMOD, most DAWs) read the chunk. Because the exported loop is the whole file, looping the whole clip in Unity is seamless.
