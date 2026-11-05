using System;
using System.Collections.Generic;
using AudioCreationStudio.DSP;
using AudioCreationStudio.Models;
using UnityEditor;
using UnityEngine;

namespace AudioCreationStudio.EditorUI
{
    [Serializable]
    public class EffectChainPayload
    {
        public List<EffectData> effects = new List<EffectData>();
    }

    /// <summary>
    /// Preset persistence. Presets are <see cref="AudioPreset"/> assets holding EditorJsonUtility payloads
    /// (object references are stored as stable GUID/fileID pairs). Factory generator presets live in code
    /// and are surfaced alongside user presets.
    /// </summary>
    public static class PresetStore
    {
        public const string UserFolder = "Assets/AudioCreationStudio/UserPresets";

        public static List<AudioPreset> All()
        {
            var list = new List<AudioPreset>();
            foreach (var g in AssetDatabase.FindAssets("t:AudioPreset"))
            {
                var p = AssetDatabase.LoadAssetAtPath<AudioPreset>(AssetDatabase.GUIDToAssetPath(g));
                if (p != null) list.Add(p);
            }
            list.Sort((a, b) => string.Compare(a.displayName, b.displayName, StringComparison.OrdinalIgnoreCase));
            return list;
        }

        public static List<AudioPreset> OfKind(PresetKind kind)
        {
            var r = new List<AudioPreset>();
            foreach (var p in All()) if (p.kind == kind) r.Add(p);
            return r;
        }

        public static List<AudioPreset> EffectPresets(string typeId)
        {
            var r = new List<AudioPreset>();
            foreach (var p in OfKind(PresetKind.Effect))
            {
                var e = LoadEffect(p);
                if (e != null && e.typeId == typeId) r.Add(p);
            }
            return r;
        }

        public static string ToJson(object o) => EditorJsonUtility.ToJson(o, true);

        public static AudioPreset Save(PresetKind kind, string name, string category, object payload, string description = "")
        {
            System.IO.Directory.CreateDirectory(UserFolder);
            string safe = MakeSafe(name);
            string path = AssetDatabase.GenerateUniqueAssetPath($"{UserFolder}/{kind}_{safe}.asset");
            var p = ScriptableObject.CreateInstance<AudioPreset>();
            p.kind = kind;
            p.displayName = name;
            p.category = string.IsNullOrEmpty(category) ? "User" : category;
            p.description = description;
            p.json = ToJson(payload);
            AssetDatabase.CreateAsset(p, path);
            AssetDatabase.SaveAssets();
            return p;
        }

        public static AudioPreset Duplicate(AudioPreset src)
        {
            var path = AssetDatabase.GetAssetPath(src);
            var dst = AssetDatabase.GenerateUniqueAssetPath(path);
            if (!AssetDatabase.CopyAsset(path, dst)) return null;
            var p = AssetDatabase.LoadAssetAtPath<AudioPreset>(dst);
            p.displayName = src.displayName + " Copy";
            EditorUtility.SetDirty(p);
            AssetDatabase.SaveAssetIfDirty(p);
            return p;
        }

        public static void Rename(AudioPreset p, string newName)
        {
            if (string.IsNullOrWhiteSpace(newName)) return;
            p.displayName = newName.Trim();
            EditorUtility.SetDirty(p);
            AssetDatabase.SaveAssetIfDirty(p);
            AssetDatabase.RenameAsset(AssetDatabase.GetAssetPath(p), $"{p.kind}_{MakeSafe(newName)}");
        }

        public static void Delete(AudioPreset p) => AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(p));

        public static T Load<T>(AudioPreset p) where T : new()
        {
            var o = new T();
            try { EditorJsonUtility.FromJsonOverwrite(p.json, o); }
            catch (Exception ex) { Debug.LogWarning($"[Audio Creation Studio] Preset '{p.displayName}' could not be read: {ex.Message}"); return default; }
            return o;
        }

        public static EffectData LoadEffect(AudioPreset p) => p.kind == PresetKind.Effect ? Load<EffectData>(p) : null;

        public static GeneratorSettings LoadGenerator(AudioPreset p) => Load<GeneratorSettings>(p);

        public static List<EffectData> LoadChain(AudioPreset p)
        {
            var payload = Load<EffectChainPayload>(p);
            var list = new List<EffectData>();
            if (payload?.effects == null) return list;
            foreach (var e in payload.effects) list.Add(e.Clone(true));
            return list;
        }

        public static string MakeSafe(string name)
        {
            foreach (var c in System.IO.Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name.Replace(' ', '_');
        }

        public static string CategoryFor(EffectData e) => EffectRegistry.Get(e.typeId)?.DisplayName ?? "Effect";
    }
}
