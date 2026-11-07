using System;
using System.Collections.Generic;
using AudioCreationStudio.DSP;
using AudioCreationStudio.Editing;
using AudioCreationStudio.Models;
using UnityEditor;
using UnityEngine;

namespace AudioCreationStudio.EditorUI
{
    /// <summary>Services shared by every view of one studio window (explicit dependency injection, no globals).</summary>
    public sealed class StudioContext
    {
        public StudioSession Session;
        public SourceLibrary Library;
        public WaveformCache Waveforms;
        public PreviewRenderService Render;
        public TransportController Transport;
        public StudioCommands Commands;
        public Action<string> ShowTab;
        public Action<string> OpenExport;
        public Action<string, bool> Toast;
        public Action<GeneratorSettings, string> EditGenerator;
        public Action<string> OpenLoopEditorForClip;
        /// <summary>Opens the Custom Generate tab with this clip as its Sample source.</summary>
        public Action<AudioClip> EditSampleInGenerator;
        /// <summary>Opens a timeline clip in Custom Generate, bound so edits update the clip (audio clips are converted first).</summary>
        public Action<string> EditClipInGenerator;

        public RecipeData Data => Session.Data;
        public int SampleRate => Session.Data.sampleRate;

        /// <summary>Returns waveform peaks for a clip's source (async build; null while analysing).</summary>
        public WaveformPeaks PeaksFor(ClipData c, out bool missing)
        {
            missing = false;
            if (c.sourceKind == ClipSourceKind.Generator)
            {
                if (c.generator == null) { missing = true; return null; }
                if (!Library.PrepareSample(c.generator, out _)) { missing = true; return null; }
                int sr = SampleRate;
                string key = SourceLibrary.GeneratorKey(c.generator, sr);
                if (Library.TryGetGeneratedCached(c.generator, sr, out var gb)) return Waveforms.Get(key, gb);
                var settings = c.generator.Clone();
                var lib = Library;
                return Waveforms.GetAsync(key, () => lib.GetGenerated(settings, sr));
            }
            if (!Library.TryGetClip(c.sourceClip, out var buf, out var k, out _)) { missing = true; return null; }
            return Waveforms.Get(k, buf);
        }
    }

    /// <summary>User-level editing commands, each a single undo step.</summary>
    public sealed class StudioCommands
    {
        private readonly StudioContext _ctx;
        private readonly List<ClipData> _clipboard = new List<ClipData>();

        public StudioCommands(StudioContext ctx) { _ctx = ctx; }

        private StudioSession S => _ctx.Session;
        private int SR => S.Data.sampleRate;

        public bool HasClipSelection => S.SelectedClips.Count > 0;
        public bool CanPaste => _clipboard.Count > 0;

        public void SplitAtPlayhead()
        {
            long at = S.Playhead;
            var targets = new List<string>(S.SelectedClips);
            if (targets.Count == 0)
            {
                // No selection: split every clip under the playhead on the selected track (or all tracks).
                foreach (var t in S.Data.tracks)
                    if (S.SelectedTrack == null || t.uid == S.SelectedTrack)
                        foreach (var c in t.clips) if (at > c.startSample && at < c.EndSample) targets.Add(c.uid);
            }
            if (targets.Count == 0) { _ctx.Toast?.Invoke("Place the playhead over a clip to split", false); return; }
            var created = new List<string>();
            S.Modify("Split Clip", r =>
            {
                foreach (var uid in targets)
                {
                    var c = r.FindClip(uid, out var owner);
                    if (c == null) continue;
                    var right = TimelineEditing.Split(owner, c, at, r.sampleRate);
                    if (right != null) created.Add(right.uid);
                }
            }, StudioChange.Structure);
            if (created.Count > 0) { S.SelectClip(created[0], false); for (int i = 1; i < created.Count; i++) S.SelectClip(created[i], true); }
        }

        public void DeleteSelection()
        {
            if (S.SelectedClips.Count == 0) return;
            var uids = new List<string>(S.SelectedClips);
            S.Modify(uids.Count > 1 ? "Delete Clips" : "Delete Clip", r => { foreach (var u in uids) TimelineEditing.DeleteClip(r, u); }, StudioChange.Structure);
            S.ClearSelection();
        }

        public void DuplicateSelection()
        {
            if (S.SelectedClips.Count == 0) return;
            var uids = new List<string>(S.SelectedClips);
            var created = new List<string>();
            S.Modify("Duplicate Clip", r =>
            {
                // Duplicate as a block: keep relative spacing, place right after the selection's end.
                long minStart = long.MaxValue, maxEnd = 0;
                foreach (var u in uids) { var c = r.FindClip(u); if (c == null) continue; minStart = Math.Min(minStart, c.startSample); maxEnd = Math.Max(maxEnd, c.EndSample); }
                long shift = maxEnd - minStart;
                foreach (var u in uids)
                {
                    var c = r.FindClip(u, out var owner);
                    if (c == null) continue;
                    var copy = c.Clone(true);
                    copy.startSample = c.startSample + shift;
                    owner.clips.Add(copy);
                    created.Add(copy.uid);
                }
            }, StudioChange.Structure);
            S.SelectClip(null, false);
            foreach (var u in created) S.SelectClip(u, true);
        }

        public void CopySelection()
        {
            _clipboard.Clear();
            long min = long.MaxValue;
            foreach (var u in S.SelectedClips) { var c = S.Data.FindClip(u); if (c != null) min = Math.Min(min, c.startSample); }
            foreach (var u in S.SelectedClips)
            {
                var c = S.Data.FindClip(u);
                if (c == null) continue;
                var copy = c.Clone(false);
                copy.startSample -= min; // store relative to the earliest clip
                _clipboard.Add(copy);
            }
            if (_clipboard.Count > 0) _ctx.Toast?.Invoke($"Copied {_clipboard.Count} clip(s)", false);
        }

        public void Paste(long at, string trackUid = null)
        {
            if (_clipboard.Count == 0) return;
            var created = new List<string>();
            S.Modify("Paste Clips", r =>
            {
                var track = r.FindTrack(trackUid ?? S.SelectedTrack) ?? (r.tracks.Count > 0 ? r.tracks[0] : TimelineEditing.AddTrack(r));
                foreach (var tmpl in _clipboard)
                {
                    var c = TimelineEditing.Paste(track, tmpl, at + tmpl.startSample);
                    created.Add(c.uid);
                }
            }, StudioChange.Structure);
            S.SelectClip(null, false);
            foreach (var u in created) S.SelectClip(u, true);
        }

        public void PasteAtPlayhead() => Paste(S.Playhead);

        public TrackData AddTrack()
        {
            TrackData created = null;
            S.Modify("Add Track", r => created = TimelineEditing.AddTrack(r), StudioChange.Structure);
            S.SelectTrack(created.uid);
            return created;
        }

        public void DeleteTrack(string uid)
        {
            S.Modify("Delete Track", r => { var t = r.FindTrack(uid); if (t != null) TimelineEditing.DeleteTrack(r, t); }, StudioChange.Structure);
        }

        public void DuplicateTrack(string uid)
        {
            TrackData created = null;
            S.Modify("Duplicate Track", r => { var t = r.FindTrack(uid); if (t != null) created = TimelineEditing.DuplicateTrack(r, t); }, StudioChange.Structure);
            if (created != null) S.SelectTrack(created.uid);
        }

        public void MoveTrack(string uid, int newIndex)
        {
            S.Modify("Reorder Track", r => { var t = r.FindTrack(uid); if (t != null) TimelineEditing.MoveTrack(r, t, newIndex); }, StudioChange.Structure);
        }

        /// <summary>Adds AudioClips to the timeline. Clips that can't be read are reported, not imported silently.</summary>
        public int ImportClips(IList<AudioClip> clips, long at, int trackIndex, bool stackOnNewTracks = false)
        {
            var readable = new List<(AudioClip clip, AudioBuffer buf)>();
            foreach (var clip in clips)
            {
                if (clip == null) continue;
                if (_ctx.Library.TryGetClip(clip, out var buf, out _, out var err)) readable.Add((clip, buf));
                else _ctx.Toast?.Invoke(err ?? $"Cannot read {clip.name}", true);
            }
            if (readable.Count == 0) return 0;
            var created = new List<string>();
            S.Modify(readable.Count > 1 ? "Import Audio Clips" : "Import Audio Clip", r =>
            {
                long pos = Math.Max(0, at);
                for (int i = 0; i < readable.Count; i++)
                {
                    var (clip, buf) = readable[i];
                    TrackData track;
                    int ti = trackIndex + (stackOnNewTracks ? i : 0);
                    if (ti >= 0 && ti < r.tracks.Count) track = r.tracks[ti];
                    else track = TimelineEditing.AddTrack(r, clip.name);
                    if (track.clips.Count == 0 && (track.name.StartsWith("Track ") || track.name.StartsWith("Layer "))) track.name = clip.name;
                    var c = TimelineEditing.CreateClipFromSource(clip, buf.SampleRate, buf.Frames, buf.Channels, stackOnNewTracks ? at : pos);
                    TimelineEditing.FitLengthToSource(c, r.sampleRate);
                    track.clips.Add(c);
                    created.Add(c.uid);
                    if (!stackOnNewTracks) pos = c.EndSample;
                }
            }, StudioChange.Structure);
            S.SelectClip(null, false);
            foreach (var u in created) S.SelectClip(u, true);
            return created.Count;
        }

        public ClipData InsertGenerator(GeneratorSettings settings, string trackUid = null, long? at = null)
        {
            ClipData created = null;
            S.Modify("Insert Generated Sound", r =>
            {
                var track = r.FindTrack(trackUid ?? S.SelectedTrack);
                if (track == null || track.clips.Count > 0 && trackUid == null)
                {
                    // Prefer an empty track, otherwise create one.
                    track = null;
                    foreach (var t in r.tracks) if (t.clips.Count == 0) { track = t; break; }
                    track ??= TimelineEditing.AddTrack(r, settings.presetName);
                }
                if (track.clips.Count == 0) track.name = settings.presetName;
                created = TimelineEditing.CreateGeneratorClip(settings, r.sampleRate, at ?? S.Playhead);
                track.clips.Add(created);
            }, StudioChange.Structure);
            S.SelectClip(created.uid, false);
            return created;
        }

        // ------------------------------------------------------------------ Edit a timeline clip in Custom Generate

        /// <summary>
        /// Turns an audio clip on the timeline into a Sample-generator clip that plays the same audio (same source
        /// offset, length, rate, gain, pan, fades and automation), so Custom Generate can edit it in place. The original
        /// AudioClip reference is kept for <see cref="RevertToOriginalAudio"/>; the change is one undo step.
        /// Returns the clip's generator settings, or null (with a toast) when it cannot be converted.
        /// </summary>
        public GeneratorSettings ConvertToSampleGenerator(string uid)
        {
            var c = S.Data.FindClip(uid);
            if (c == null) return null;
            if (c.sourceKind == ClipSourceKind.Generator) return c.generator;
            if (c.sourceClip == null) { _ctx.Toast?.Invoke("This clip has no source audio to edit", true); return null; }
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(c.sourceClip, out string guid, out long _))
            {
                _ctx.Toast?.Invoke("The clip's audio must be an asset in the project", true);
                return null;
            }
            int sr = SR;
            double srcRate = Math.Max(1, c.sourceSampleRate);
            // Source seconds the clip plays: timeline length × source step (covers playback rate); loops use the rest of the file.
            double seconds = c.loop ? Math.Max(0, c.sourceFrames - c.sourceOffset) / srcRate : c.lengthSamples * c.SourceStep(sr) / srcRate;
            var g = GeneratorView.NeutralSampleSettings();
            g.sampleGuid = guid;
            g.sampleName = c.sourceClip.name;
            g.presetName = c.DisplayName;
            g.sampleStart = (float)(c.sourceOffset / srcRate);
            g.durationSeconds = Mathf.Clamp((float)seconds, 0.02f, ProceduralSynth.MaxDurationSeconds);
            // The sample plays mono and a mono clip is panned centre at -3 dB; a stereo source played at unity per side,
            // so lift it by the pan law to keep the same loudness.
            if (c.sourceChannels > 1) g.volumeDb = 3.0103f;
            bool trimmed = seconds > ProceduralSynth.MaxDurationSeconds;
            bool stereo = c.sourceChannels > 1;
            S.Modify("Edit Clip in Custom Generate", d =>
            {
                var cc = d.FindClip(uid);
                if (cc == null) return;
                long frames = Math.Max(1, (long)Math.Round(g.durationSeconds * d.sampleRate));
                cc.sourceKind = ClipSourceKind.Generator;
                cc.generator = g.Clone();
                cc.name = g.presetName;
                cc.sourceSampleRate = d.sampleRate;
                cc.sourceFrames = frames;
                cc.sourceChannels = 1;
                cc.sourceOffset = 0;
                if (!cc.loop) cc.lengthSamples = Math.Min(cc.lengthSamples, Math.Max(1, (long)Math.Floor(frames / cc.SourceStep(d.sampleRate))));
            }, StudioChange.Structure | StudioChange.Content);
            _ctx.Toast?.Invoke("Clip is now editable in Custom Generate" + (stereo ? " (played mono)" : "") + (trimmed ? $" · limited to {ProceduralSynth.MaxDurationSeconds:0} s" : "") +
                               ". Cmd/Ctrl+Z or right-click ▸ Revert to Original Audio restores it.", false);
            return S.Data.FindClip(uid)?.generator;
        }

        /// <summary>True for a clip made by <see cref="ConvertToSampleGenerator"/> that still knows its original audio.</summary>
        public static bool CanRevertToAudio(ClipData c) =>
            c != null && c.sourceKind == ClipSourceKind.Generator && c.sourceClip != null && c.generator != null && c.generator.waveform == OscillatorWaveform.Sample;

        /// <summary>Turns a converted clip back into a plain audio clip playing the original file (generator edits are dropped).</summary>
        public void RevertToOriginalAudio(string uid)
        {
            var c = S.Data.FindClip(uid);
            if (!CanRevertToAudio(c)) return;
            if (!_ctx.Library.TryGetClip(c.sourceClip, out var buf, out _, out var err)) { _ctx.Toast?.Invoke(err, true); return; }
            float start = c.generator.sampleStart;
            S.Modify("Revert to Original Audio", d =>
            {
                var cc = d.FindClip(uid);
                if (cc == null) return;
                cc.sourceKind = ClipSourceKind.AudioClip;
                cc.name = null;
                cc.sourceSampleRate = buf.SampleRate;
                cc.sourceFrames = buf.Frames;
                cc.sourceChannels = buf.Channels;
                cc.sourceOffset = Math.Max(0, Math.Min(buf.Frames - 1, (long)Math.Round(start * buf.SampleRate)));
                if (!cc.loop) cc.lengthSamples = Math.Max(1, Math.Min(cc.lengthSamples, cc.MaxLengthFromOffset(d.sampleRate)));
                cc.generator = null;
            }, StudioChange.Structure | StudioChange.Content);
            _ctx.Toast?.Invoke("Clip restored to its original audio", false);
        }

        public void ImportFromProjectSelection()
        {
            var clips = new List<AudioClip>();
            foreach (var o in Selection.objects) if (o is AudioClip ac) clips.Add(ac);
            if (clips.Count == 0)
            {
                string path = EditorUtility.OpenFilePanelWithFilters("Import audio", "Assets", new[] { "Audio", "wav,aif,aiff,mp3,ogg,flac", "All", "*" });
                if (string.IsNullOrEmpty(path)) return;
                var clip = ImportExternalFile(path);
                if (clip != null) clips.Add(clip);
            }
            if (clips.Count == 0) return;
            int ti = S.PrimaryTrack != null ? S.Data.tracks.IndexOf(S.PrimaryTrack) : 0;
            ImportClips(clips, S.Playhead, ti);
        }

        /// <summary>Copies a file from outside the project into Assets (originals are never moved or modified).</summary>
        public static AudioClip ImportExternalFile(string fullPath)
        {
            string dataPath = Application.dataPath.Replace('\\', '/');
            fullPath = fullPath.Replace('\\', '/');
            string assetPath;
            if (fullPath.StartsWith(dataPath)) assetPath = "Assets" + fullPath.Substring(dataPath.Length);
            else
            {
                const string folder = "Assets/AudioCreationStudio/Imported";
                System.IO.Directory.CreateDirectory(folder);
                assetPath = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{System.IO.Path.GetFileName(fullPath)}");
                System.IO.File.Copy(fullPath, assetPath);
                AssetDatabase.ImportAsset(assetPath);
            }
            return AssetDatabase.LoadAssetAtPath<AudioClip>(assetPath);
        }
    }
}
