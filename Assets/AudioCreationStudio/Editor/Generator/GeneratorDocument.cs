using AudioCreationStudio.Models;
using UnityEngine;

namespace AudioCreationStudio.EditorUI
{
    /// <summary>
    /// Hidden, undoable container for the Generator's current settings. Standalone generator edits are
    /// recorded on this object with Unity's Undo system, so Ctrl/Cmd+Z works in the Generator tab too.
    /// </summary>
    public sealed class GeneratorDocument : ScriptableObject
    {
        public GeneratorSettings settings = new GeneratorSettings();
    }
}
