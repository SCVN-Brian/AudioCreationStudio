using System;
using System.Collections.Generic;
using AudioCreationStudio.Editing;
using AudioCreationStudio.Models;
using UnityEditor;
using UnityEngine;

namespace AudioCreationStudio.EditorUI
{
    [Flags]
    public enum StudioChange
    {
        None = 0,
        Content = 1,      // parameter values (gain, fades, effect params…)
        Structure = 2,    // tracks/clips/effects added, removed, reordered
        Selection = 4,
        View = 8,         // zoom/scroll
        Playhead = 16,
        Recipe = 32,      // a different recipe was loaded
        All = ~0
    }

    /// <summary>Serializable window state that survives domain reloads.</summary>
    [Serializable]
    public class StudioState
    {
        public AudioRecipe working;
        public AudioRecipe asset;
        public bool dirty;
        public long playhead;
        public bool loopPlayback;
        public bool snap = true;
        public string activeTab = "Arrange";
        public TimelineViewState view = new TimelineViewState();
        public List<string> selectedClips = new List<string>();
        public string selectedTrack;
        public string selectedEffect;
        public bool inspectorMaster;
        public GeneratorSettings generator;
        public GeneratorDocument generatorDoc;
        public string generatorClipUid;
        public float leftPanelWidth = 248;
        public bool followPlayhead = true;
    }

    /// <summary>
    /// Owns the editable working copy of a recipe and every mutation of it. All edits go through
    /// <see cref="Modify"/> or the continuous-edit API so they are recorded with Unity's Undo system and
    /// broadcast to views. The working copy is a hidden in-memory asset; the on-disk recipe is only
    /// written on Save, which keeps Revert trivial and source assets untouched.
    /// </summary>
    public sealed class StudioSession : IDisposable
    {
        private readonly StudioState _state;
        private int _continuousGroup = -1;
        private string _continuousName;

        public event Action<StudioChange, object> Changed;
        public const string LastRecipePref = "AudioCreationStudio.LastRecipe";

        private static void RememberRecipe(AudioRecipe asset)
        {
            if (asset != null) EditorPrefs.SetString(LastRecipePref, AssetDatabase.GetAssetPath(asset));
        }

        public StudioSession(StudioState state, bool restoreLastRecipe = true)
        {
            _state = state;
            if (_state.working == null)
            {
                // The hidden working copy can be lost (e.g. editor restarts). Recover from the saved asset
                // rather than silently showing an empty recipe.
                if (_state.asset == null && restoreLastRecipe)
                {
                    // A freshly opened window reopens the last recipe instead of an empty one.
                    string last = EditorPrefs.GetString(LastRecipePref, "");
                    if (!string.IsNullOrEmpty(last)) _state.asset = AssetDatabase.LoadAssetAtPath<AudioRecipe>(last);
                }
                if (_state.asset != null)
                {
                    CreateWorking(_state.asset.data.Clone());
                    _state.dirty = false;
                }
                else CreateWorking(null);
            }
            _contentHash = ContentHash();
            Data.EnsureValid();
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        public void Dispose()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
        }

        public StudioState State => _state;
        public AudioRecipe Working => _state.working;
        public AudioRecipe Asset => _state.asset;
        public RecipeData Data => _state.working.data;
        public bool IsDirty => _state.dirty;
        public TimelineViewState View => _state.view;
        public int Version { get; private set; }

        public long Playhead
        {
            get => _state.playhead;
            set
            {
                value = Math.Max(0, value);
                if (value == _state.playhead) return;
                _state.playhead = value;
                Changed?.Invoke(StudioChange.Playhead, null);
            }
        }

        public bool Snap { get => _state.snap; set { _state.snap = value; Changed?.Invoke(StudioChange.View, null); } }
        public bool LoopPlayback { get => _state.loopPlayback; set { _state.loopPlayback = value; Changed?.Invoke(StudioChange.View, null); } }

        public string DisplayName => (Asset != null ? Asset.name : Data.name) + (IsDirty ? " •" : "");

        // ---------- Selection (not part of undo) ----------

        public IReadOnlyList<string> SelectedClips => _state.selectedClips;
        public string SelectedTrack => _state.selectedTrack;
        public string SelectedEffect => _state.selectedEffect;
        public bool InspectMaster => _state.inspectorMaster;

        public ClipData PrimaryClip => _state.selectedClips.Count > 0 ? Data.FindClip(_state.selectedClips[_state.selectedClips.Count - 1]) : null;
        public TrackData PrimaryTrack => Data.FindTrack(_state.selectedTrack);

        public bool IsClipSelected(string uid) => _state.selectedClips.Contains(uid);

        public void SelectClip(string uid, bool additive)
        {
            if (!additive) _state.selectedClips.Clear();
            if (uid != null)
            {
                if (additive && _state.selectedClips.Contains(uid)) _state.selectedClips.Remove(uid);
                else _state.selectedClips.Add(uid);
                Data.FindClip(uid, out var owner);
                if (owner != null) _state.selectedTrack = owner.uid;
            }
            _state.inspectorMaster = false;
            Changed?.Invoke(StudioChange.Selection, null);
        }

        public void SelectTrack(string uid, bool clearClips = true)
        {
            _state.selectedTrack = uid;
            if (clearClips) _state.selectedClips.Clear();
            _state.inspectorMaster = false;
            Changed?.Invoke(StudioChange.Selection, null);
        }

        public void SelectMaster()
        {
            _state.selectedClips.Clear();
            _state.selectedTrack = null;
            _state.inspectorMaster = true;
            Changed?.Invoke(StudioChange.Selection, null);
        }

        public void SelectEffect(string uid)
        {
            _state.selectedEffect = uid;
        }

        public void ClearSelection()
        {
            _state.selectedClips.Clear();
            _state.inspectorMaster = false;
            Changed?.Invoke(StudioChange.Selection, null);
        }

        private void PruneSelection()
        {
            _state.selectedClips.RemoveAll(u => Data.FindClip(u) == null);
            if (_state.selectedTrack != null && Data.FindTrack(_state.selectedTrack) == null) _state.selectedTrack = null;
        }

        // ---------- Mutation ----------

        public void Modify(string undoName, Action<RecipeData> action, StudioChange kind = StudioChange.Content, object origin = null)
        {
            Undo.RecordObject(Working, undoName);
            action(Data);
            Undo.FlushUndoRecordObjects();
            Undo.SetCurrentGroupName(undoName);
            Undo.IncrementCurrentGroup();
            AfterChange(kind, origin);
        }

        /// <summary>Starts a drag-style edit that collapses into one undo step.</summary>
        public void BeginContinuous(string undoName)
        {
            if (_continuousGroup >= 0) EndContinuous();
            Undo.IncrementCurrentGroup();
            _continuousGroup = Undo.GetCurrentGroup();
            _continuousName = undoName;
            Undo.RecordObject(Working, undoName);
        }

        public void Continuous(Action<RecipeData> action, StudioChange kind = StudioChange.Content, object origin = null)
        {
            if (_continuousGroup < 0) { Modify(_continuousName ?? "Edit", action, kind, origin); return; }
            Undo.RecordObject(Working, _continuousName);
            action(Data);
            AfterChange(kind, origin);
        }

        public void EndContinuous()
        {
            if (_continuousGroup < 0) return;
            Undo.FlushUndoRecordObjects();
            Undo.CollapseUndoOperations(_continuousGroup);
            Undo.SetCurrentGroupName(_continuousName);
            Undo.IncrementCurrentGroup();
            _continuousGroup = -1;
            Changed?.Invoke(StudioChange.Content, null);
        }

        public bool InContinuousEdit => _continuousGroup >= 0;

        private void AfterChange(StudioChange kind, object origin)
        {
            _state.dirty = true;
            Version++;
            _contentHash = ContentHash();
            if ((kind & StudioChange.Structure) != 0) PruneSelection();
            EditorUtility.SetDirty(Working);
            Changed?.Invoke(kind, origin);
        }

        /// <summary>Announces a change that did not mutate recipe data (e.g. source re-import).</summary>
        public void Notify(StudioChange kind, object origin = null)
        {
            if ((kind & (StudioChange.Content | StudioChange.Structure)) != 0) Version++;
            Changed?.Invoke(kind, origin);
        }

        private ulong _contentHash;

        private ulong ContentHash()
        {
            var h = new DSP.StableHash();
            h.Add(UnityEngine.JsonUtility.ToJson(Data));
            return h.Value;
        }

        private void OnUndoRedo()
        {
            if (Working == null) return;
            // Unity's undo stack is global: ignore undo/redo that did not touch this recipe.
            ulong hash = ContentHash();
            if (hash == _contentHash) return;
            _contentHash = hash;
            Data.EnsureValid();
            Version++;
            _state.dirty = true;
            PruneSelection();
            Changed?.Invoke(StudioChange.All & ~StudioChange.Recipe, null);
        }

        // ---------- File operations ----------

        private void CreateWorking(RecipeData data)
        {
            var w = ScriptableObject.CreateInstance<AudioRecipe>();
            w.hideFlags = HideFlags.HideAndDontSave;
            w.name = "ACS Working Recipe";
            w.data = data ?? CreateDefaultRecipe();
            w.data.EnsureValid();
            _state.working = w;
        }

        public static RecipeData CreateDefaultRecipe()
        {
            var r = new RecipeData { name = "New Sound" };
            TimelineEditing.AddTrack(r, "Layer 1");
            TimelineEditing.AddTrack(r, "Layer 2");
            r.masterEffects.Add(DSP.EffectRegistry.CreateData(DSP.EffectTypes.Limiter));
            return r;
        }

        private void Load(RecipeData data, AudioRecipe asset)
        {
            if (Working != null) { Undo.ClearUndo(Working); UnityEngine.Object.DestroyImmediate(Working); }
            CreateWorking(data);
            _state.asset = asset;
            RememberRecipe(asset);
            _state.dirty = false;
            _state.selectedClips.Clear();
            _state.selectedTrack = Data.tracks.Count > 0 ? Data.tracks[0].uid : null;
            _state.playhead = 0;
            Version++;
            _contentHash = ContentHash();
            Changed?.Invoke(StudioChange.All, null);
        }

        public bool ConfirmDiscard()
        {
            if (!IsDirty) return true;
            int r = EditorUtility.DisplayDialogComplex("Unsaved changes",
                $"'{DisplayName.TrimEnd(' ', '•')}' has unsaved changes.", "Save", "Cancel", "Discard");
            if (r == 1) return false;
            if (r == 0) return Save();
            return true;
        }

        public void New()
        {
            if (!ConfirmDiscard()) return;
            Load(CreateDefaultRecipe(), null);
        }

        public void Open(AudioRecipe asset)
        {
            if (asset == null || !ConfirmDiscard()) return;
            var copy = asset.data.Clone();
            copy.EnsureValid();
            Load(copy, asset);
        }

        public void OpenFromData(RecipeData data)
        {
            if (!ConfirmDiscard()) return;
            Load(data.Clone(), null);
            _state.dirty = true;
        }

        public bool Save()
        {
            if (Asset == null) return SaveAs();
            Asset.data = Data.Clone();
            Asset.formatVersion = AudioRecipe.CurrentFormatVersion;
            EditorUtility.SetDirty(Asset);
            AssetDatabase.SaveAssetIfDirty(Asset);
            _state.dirty = false;
            Changed?.Invoke(StudioChange.View, null);
            return true;
        }

        public bool SaveAs()
        {
            string dir = Asset != null ? System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(Asset)) : "Assets";
            string path = EditorUtility.SaveFilePanelInProject("Save Audio Recipe", string.IsNullOrEmpty(Data.name) ? "New Sound" : Data.name, "asset",
                "Choose where to save the recipe", dir);
            if (string.IsNullOrEmpty(path)) return false;
            SaveTo(path);
            return true;
        }

        public AudioRecipe SaveTo(string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<AudioRecipe>(path);
            var name = System.IO.Path.GetFileNameWithoutExtension(path);
            if (existing != null)
            {
                existing.data = Data.Clone();
                EditorUtility.SetDirty(existing);
                AssetDatabase.SaveAssetIfDirty(existing);
                _state.asset = existing;
            }
            else
            {
                var a = ScriptableObject.CreateInstance<AudioRecipe>();
                a.data = Data.Clone();
                AssetDatabase.CreateAsset(a, path);
                AssetDatabase.SaveAssets();
                _state.asset = a;
            }
            if (Data.name == "New Sound" || string.IsNullOrEmpty(Data.name)) Data.name = name;
            RememberRecipe(_state.asset);
            _state.dirty = false;
            Changed?.Invoke(StudioChange.View, null);
            return _state.asset;
        }

        public void Revert()
        {
            if (Asset == null) return;
            if (IsDirty && !EditorUtility.DisplayDialog("Revert recipe", "Discard all changes since the last save?", "Revert", "Cancel")) return;
            var copy = Asset.data.Clone();
            copy.EnsureValid();
            Load(copy, Asset);
        }

        public void DuplicateAsset()
        {
            string dir = Asset != null ? System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(Asset)) : "Assets";
            string path = AssetDatabase.GenerateUniqueAssetPath($"{dir}/{(Asset != null ? Asset.name : Data.name)} Copy.asset");
            var a = ScriptableObject.CreateInstance<AudioRecipe>();
            a.data = Data.Clone();
            a.data.name = System.IO.Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(a, path);
            AssetDatabase.SaveAssets();
            Open(a);
        }

        /// <summary>Clips whose source asset is missing or unreadable.</summary>
        public List<ClipData> FindMissingSources(SourceLibrary lib)
        {
            var list = new List<ClipData>();
            foreach (var c in Data.AllClips())
            {
                if (c.sourceKind == ClipSourceKind.Generator) continue;
                if (c.sourceClip == null || !lib.TryGetClip(c.sourceClip, out _, out _, out _)) list.Add(c);
            }
            return list;
        }
    }
}
