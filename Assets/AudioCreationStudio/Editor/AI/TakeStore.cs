using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;

namespace AudioCreationStudio.EditorUI
{
    /// <summary>
    /// Folder operations for the ElevenLabs take library. Folders are real asset folders under a root
    /// (Generated/ElevenLabs by default) and every move goes through AssetDatabase, so AudioClip GUIDs
    /// (and therefore timeline/loop references) survive. Each take's ".txt" prompt sidecar travels with it.
    /// </summary>
    public static class TakeStore
    {
        private static readonly string[] AudioExtensions = { ".wav", ".mp3", ".ogg", ".aif", ".aiff", ".flac" };

        public static bool IsAudioFile(string path)
        {
            string ext = Path.GetExtension(path ?? "").ToLowerInvariant();
            return Array.IndexOf(AudioExtensions, ext) >= 0;
        }

        public static string Normalize(string path) => (path ?? "").Replace('\\', '/').TrimEnd('/');

        /// <summary>True when <paramref name="path"/> is <paramref name="folder"/> or anything below it.</summary>
        public static bool IsInside(string path, string folder)
        {
            path = Normalize(path);
            folder = Normalize(folder);
            return path == folder || path.StartsWith(folder + "/", StringComparison.Ordinal);
        }

        public static string Parent(string path)
        {
            path = Normalize(path);
            int i = path.LastIndexOf('/');
            return i > 0 ? path.Substring(0, i) : path;
        }

        public static string Name(string path) => Path.GetFileName(Normalize(path));

        /// <summary>"Dogs › Puppies" for a folder below the root, or <paramref name="rootLabel"/> for the root itself.</summary>
        public static string DisplayPath(string folder, string root, string rootLabel)
        {
            folder = Normalize(folder);
            root = Normalize(root);
            if (folder == root || !IsInside(folder, root)) return rootLabel;
            return folder.Substring(root.Length + 1).Replace("/", "  ›  ");
        }

        public static string SidecarPath(string audioPath) => Path.ChangeExtension(audioPath, ".txt").Replace('\\', '/');

        /// <summary>Creates the folder (and any missing parents) as asset folders.</summary>
        public static void EnsureFolder(string folder)
        {
            folder = Normalize(folder);
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = Parent(folder);
            if (parent != folder) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Name(folder));
        }

        public static List<string> SubFolders(string folder)
        {
            var list = new List<string>();
            if (!AssetDatabase.IsValidFolder(folder)) return list;
            foreach (var f in AssetDatabase.GetSubFolders(folder)) list.Add(Normalize(f));
            list.Sort((a, b) => string.Compare(Name(a), Name(b), StringComparison.OrdinalIgnoreCase));
            return list;
        }

        /// <summary>Audio files directly inside (or, with <paramref name="recursive"/>, anywhere below) a folder, newest first.</summary>
        public static List<string> Takes(string folder, bool recursive)
        {
            var list = new List<string>();
            if (!Directory.Exists(folder)) return list;
            foreach (var f in Directory.GetFiles(folder, "*", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly))
                if (IsAudioFile(f)) list.Add(Normalize(f));
            list.Sort((x, y) => File.GetLastWriteTimeUtc(y).CompareTo(File.GetLastWriteTimeUtc(x)));
            return list;
        }

        public static int CountTakes(string folder, bool recursive)
        {
            if (!Directory.Exists(folder)) return 0;
            int n = 0;
            foreach (var f in Directory.GetFiles(folder, "*", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly))
                if (IsAudioFile(f)) n++;
            return n;
        }

        /// <summary>Reads "prompt: …" from a take's sidecar, or null.</summary>
        public static string ReadPrompt(string audioPath)
        {
            string txt = SidecarPath(audioPath);
            if (!File.Exists(txt)) return null;
            foreach (var line in File.ReadAllLines(txt))
                if (line.StartsWith("prompt: ", StringComparison.Ordinal)) return line.Substring(8);
            return null;
        }

        // ------------------------------------------------------------------ Folders

        /// <summary>Creates a uniquely named sub-folder and returns its path.</summary>
        public static string CreateFolder(string parent, string name = "New Folder")
        {
            EnsureFolder(parent);
            string path = AssetDatabase.GenerateUniqueAssetPath($"{Normalize(parent)}/{Sanitize(name, "New Folder")}");
            AssetDatabase.CreateFolder(Parent(path), Name(path));
            return path;
        }

        /// <summary>Renames a folder or take (keeping the sidecar in step). Returns the new path; throws with Unity's message on failure.</summary>
        public static string Rename(string path, string newName)
        {
            path = Normalize(path);
            bool folder = AssetDatabase.IsValidFolder(path);
            string clean = Sanitize(newName, null);
            if (clean == null) throw new InvalidOperationException("The name cannot be empty.");
            string current = folder ? Name(path) : Path.GetFileNameWithoutExtension(path);
            if (clean == current) return path;
            string err = AssetDatabase.RenameAsset(path, clean);
            if (!string.IsNullOrEmpty(err)) throw new InvalidOperationException(err);
            string newPath = folder ? $"{Parent(path)}/{clean}" : $"{Parent(path)}/{clean}{Path.GetExtension(path)}";
            if (!folder && File.Exists(SidecarPath(path))) AssetDatabase.RenameAsset(SidecarPath(path), clean);
            return newPath;
        }

        /// <summary>
        /// Moves a folder into another folder. Returns the new path, or throws when the move is impossible
        /// (into itself / a descendant) or Unity refuses it. A name clash gets a unique suffix.
        /// </summary>
        public static string MoveFolder(string folder, string destParent)
        {
            folder = Normalize(folder);
            destParent = Normalize(destParent);
            if (IsInside(destParent, folder)) throw new InvalidOperationException("A folder cannot be moved into itself.");
            if (Parent(folder) == destParent) return folder;
            string dest = AssetDatabase.GenerateUniqueAssetPath($"{destParent}/{Name(folder)}");
            string err = AssetDatabase.MoveAsset(folder, dest);
            if (!string.IsNullOrEmpty(err)) throw new InvalidOperationException(err);
            return dest;
        }

        /// <summary>Moves a take (and its sidecar) into a folder. Returns the new path; a name clash gets a unique suffix.</summary>
        public static string MoveTake(string audioPath, string destFolder)
        {
            audioPath = Normalize(audioPath);
            destFolder = Normalize(destFolder);
            if (Parent(audioPath) == destFolder) return audioPath;
            EnsureFolder(destFolder);
            string dest = AssetDatabase.GenerateUniqueAssetPath($"{destFolder}/{Path.GetFileName(audioPath)}");
            string err = AssetDatabase.MoveAsset(audioPath, dest);
            if (!string.IsNullOrEmpty(err)) throw new InvalidOperationException(err);
            string side = SidecarPath(audioPath);
            if (File.Exists(side))
            {
                string sideDest = SidecarPath(dest);
                if (!File.Exists(sideDest)) AssetDatabase.MoveAsset(side, sideDest);
            }
            return dest;
        }

        /// <summary>Copies an audio asset from elsewhere in the project into a folder (sidecar included). Returns the new path.</summary>
        public static string CopyTake(string audioPath, string destFolder)
        {
            EnsureFolder(destFolder);
            string dest = AssetDatabase.GenerateUniqueAssetPath($"{Normalize(destFolder)}/{Path.GetFileName(audioPath)}");
            if (!AssetDatabase.CopyAsset(audioPath, dest)) throw new InvalidOperationException($"Could not copy '{audioPath}'.");
            string side = SidecarPath(audioPath);
            if (File.Exists(side) && !File.Exists(SidecarPath(dest))) AssetDatabase.CopyAsset(side, SidecarPath(dest));
            return dest;
        }

        /// <summary>
        /// Deletes a folder. With <paramref name="keepTakes"/> every take below it is first moved to the folder's parent.
        /// Returns the number of takes moved out.
        /// </summary>
        public static int DeleteFolder(string folder, bool keepTakes)
        {
            folder = Normalize(folder);
            int moved = 0;
            if (keepTakes)
            {
                string parent = Parent(folder);
                foreach (var t in Takes(folder, true)) { MoveTake(t, parent); moved++; }
            }
            AssetDatabase.DeleteAsset(folder);
            return moved;
        }

        public static void DeleteTake(string audioPath)
        {
            AssetDatabase.DeleteAsset(audioPath);
            string side = SidecarPath(audioPath);
            if (File.Exists(side)) AssetDatabase.DeleteAsset(side);
        }

        /// <summary>Strips characters that are invalid in file names; returns <paramref name="fallback"/> when nothing is left.</summary>
        public static string Sanitize(string name, string fallback)
        {
            var s = (name ?? "").Trim();
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c.ToString(), "");
            s = s.Replace("/", "").Replace("\\", "").Replace(":", "").Trim().TrimEnd('.');
            return s.Length == 0 ? fallback : s;
        }
    }
}
