using UnityEngine;
using UnityEngine.UIElements;

namespace AudioCreationStudio.EditorUI
{
    /// <summary>
    /// Design tokens. USS uses the same values as CSS custom properties (StudioStyles.uss); code that
    /// paints with Painter2D/meshes reads them from here so no colors are scattered through the UI code.
    /// </summary>
    public static class StudioTheme
    {
        public static readonly Color Background = Hex(0x15181E);
        public static readonly Color Surface = Hex(0x1D222B);
        public static readonly Color Elevated = Hex(0x282E39);
        public static readonly Color Border = Hex(0x363D49);
        public static readonly Color Text = Hex(0xE8ECF2);
        public static readonly Color TextDim = Hex(0x9CA6B5);
        public static readonly Color TextFaint = Hex(0x6B7484);
        public static readonly Color Accent = Hex(0x5B9DFF);
        public static readonly Color Success = Hex(0x42C9A0);
        public static readonly Color Warning = Hex(0xF0B35A);
        public static readonly Color Error = Hex(0xF06C78);
        public static readonly Color Inset = Hex(0x11141A);
        public static readonly Color LaneA = Hex(0x171B22);
        public static readonly Color LaneB = Hex(0x1A1E26);
        public static readonly Color GridMajor = new Color(1f, 1f, 1f, 0.07f);
        public static readonly Color GridMinor = new Color(1f, 1f, 1f, 0.03f);
        public static readonly Color Playhead = Hex(0xFFFFFF);
        public static readonly Color Selection = Hex(0xFFFFFF);

        public static Color Hex(int rgb, float a = 1f) =>
            new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, a);

        public static Color WithAlpha(this Color c, float a) => new Color(c.r, c.g, c.b, a);

        public static Color Mix(Color a, Color b, float t) => Color.Lerp(a, b, t);

        public static Color Lighten(Color c, float t) => Color.Lerp(c, Color.white, t);

        public static Color Darken(Color c, float t) => Color.Lerp(c, Color.black, t);

        /// <summary>Meter color for a linear level.</summary>
        public static Color MeterColor(float db)
        {
            if (db > -3f) return Error;
            if (db > -12f) return Warning;
            return Success;
        }

        /// <summary>GUID of Editor/Styling/StudioStyles.uss (from its .meta).</summary>
        private const string StyleSheetGuid = "e961269b9213a4890af0d8251ecfa7fb";
        private static StyleSheet _sheet;

        public static StyleSheet StyleSheet
        {
            get
            {
                if (_sheet != null) return _sheet;
                // By GUID first: works the same in Assets/ and as a UPM package (Packages/com.supercent.audiocreationstudio/...).
                var byGuid = UnityEditor.AssetDatabase.GUIDToAssetPath(StyleSheetGuid);
                if (!string.IsNullOrEmpty(byGuid)) _sheet = UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>(byGuid);
                if (_sheet != null) return _sheet;
                foreach (var g in UnityEditor.AssetDatabase.FindAssets("StudioStyles t:StyleSheet"))
                {
                    var path = UnityEditor.AssetDatabase.GUIDToAssetPath(g);
                    // Package folders are lower-case (com.supercent.audiocreationstudio), so compare case-insensitively.
                    if (path.IndexOf("audiocreationstudio", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                    _sheet = UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
                    if (_sheet != null) break;
                }
                if (_sheet == null) UnityEngine.Debug.LogError("[Audio Creation Studio] StudioStyles.uss not found: the window will be unstyled. Reinstall the package.");
                return _sheet;
            }
        }

        public static void Apply(VisualElement root)
        {
            var sheet = StyleSheet;
            if (sheet != null && !root.styleSheets.Contains(sheet)) root.styleSheets.Add(sheet);
            root.AddToClassList("acs-root");
        }
    }
}
