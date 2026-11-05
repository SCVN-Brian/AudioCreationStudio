using UnityEngine;

namespace AudioCreationStudio
{
    public enum PresetKind { Generator = 0, Effect = 1, EffectChain = 2, Recipe = 3, ExportSettings = 4 }

    /// <summary>
    /// A reusable preset stored as an asset. The payload is JSON produced by JsonUtility from one of the
    /// plain model classes, which keeps presets diff-friendly and independent of their consumers.
    /// </summary>
    public class AudioPreset : ScriptableObject
    {
        public PresetKind kind;
        public string displayName = "Preset";
        public string category = "User";
        [TextArea] public string description = "";
        [TextArea(4, 20)] public string json = "{}";
    }
}
