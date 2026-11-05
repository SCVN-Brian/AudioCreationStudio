using AudioCreationStudio.Models;
using UnityEngine;

namespace AudioCreationStudio
{
    /// <summary>
    /// Asset wrapper for a <see cref="RecipeData"/>. References source AudioClips through Unity's
    /// stable asset references (GUID + fileID), never copies or modifies them.
    /// </summary>
    [CreateAssetMenu(menuName = "Audio Creation Studio/Audio Recipe", fileName = "New Audio Recipe", order = 300)]
    public class AudioRecipe : ScriptableObject
    {
        public const int CurrentFormatVersion = 1;

        public int formatVersion = CurrentFormatVersion;
        public RecipeData data = new RecipeData();

        private void OnEnable()
        {
            data ??= new RecipeData();
            data.EnsureValid();
        }

        private void OnValidate()
        {
            data ??= new RecipeData();
            data.EnsureValid();
        }
    }
}
