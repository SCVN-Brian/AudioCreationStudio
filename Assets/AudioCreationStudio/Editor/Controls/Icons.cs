using UnityEngine;
using UnityEngine.UIElements;

namespace AudioCreationStudio.EditorUI
{
    public enum Icon
    {
        None, Play, Pause, Stop, Loop, Plus, Trash, Undo, Redo, Save, Open, New, Import, Export, Gear, Magnet,
        Scissors, Duplicate, Dice, Waveform, Mixer, Sine, Layers, Grid, Power, ChevronDown, ChevronRight, Up, Down,
        Curve, Speaker, Close, Search, More, Check, Warning, Folder, Logo, Skip, Copy,
        Pointer, TrimTool, Blade, DynamicTrim, Grip, FolderOpen, FolderPlus, Pencil, Sparkle
    }

    /// <summary>Resolution-independent vector icons drawn with Painter2D (crisp on high-DPI displays).</summary>
    public class IconElement : VisualElement
    {
        private Icon _icon;
        private Color _color = StudioTheme.TextDim;
        private bool _colorOverride;

        public Icon Icon { get => _icon; set { _icon = value; MarkDirtyRepaint(); } }

        public Color Color
        {
            get => _color;
            set { _color = value; _colorOverride = true; MarkDirtyRepaint(); }
        }

        public IconElement(Icon icon, float size = 16)
        {
            _icon = icon;
            AddToClassList("acs-icon");
            style.width = size;
            style.height = size;
            style.flexShrink = 0;
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
            RegisterCallback<CustomStyleResolvedEvent>(_ => MarkDirtyRepaint());
        }

        private void Draw(MeshGenerationContext ctx)
        {
            var r = contentRect;
            if (r.width <= 0) return;
            var c = _colorOverride ? _color : resolvedStyle.color;
            IconPainter.Draw(ctx.painter2D, _icon, r, c);
        }
    }

    public static class IconPainter
    {
        public static void Draw(Painter2D p, Icon icon, Rect r, Color c)
        {
            float s = Mathf.Min(r.width, r.height);
            var o = new Vector2(r.x + (r.width - s) * 0.5f, r.y + (r.height - s) * 0.5f);
            Vector2 P(float x, float y) => o + new Vector2(x * s, y * s);
            float lw = Mathf.Max(1.2f, s * 0.09f);
            p.strokeColor = c;
            p.fillColor = c;
            p.lineWidth = lw;
            p.lineCap = LineCap.Round;
            p.lineJoin = LineJoin.Round;

            void Poly(bool fill, params Vector2[] pts)
            {
                p.BeginPath();
                p.MoveTo(pts[0]);
                for (int i = 1; i < pts.Length; i++) p.LineTo(pts[i]);
                if (fill) { p.ClosePath(); p.Fill(); } else p.Stroke();
            }
            void Seg(float x0, float y0, float x1, float y1) => Poly(false, P(x0, y0), P(x1, y1));
            void Ring(float cx, float cy, float rad, float a0, float a1)
            {
                p.BeginPath();
                p.Arc(P(cx, cy), rad * s, Angle.Degrees(a0), Angle.Degrees(a1), ArcDirection.Clockwise);
                p.Stroke();
            }
            void Dot(float cx, float cy, float rad) => PaintUtil.Circle(p, P(cx, cy), rad * s, c);
            void Box(float x, float y, float w, float h, float rad, bool fill)
            {
                var rr = new Rect(P(x, y), new Vector2(w * s, h * s));
                PaintUtil.RoundedRectPath(p, rr, rad * s);
                if (fill) p.Fill(); else p.Stroke();
            }

            switch (icon)
            {
                case Icon.Play: Poly(true, P(0.28f, 0.18f), P(0.82f, 0.5f), P(0.28f, 0.82f)); break;
                case Icon.Pause: Box(0.24f, 0.2f, 0.17f, 0.6f, 0.04f, true); Box(0.59f, 0.2f, 0.17f, 0.6f, 0.04f, true); break;
                case Icon.Stop: Box(0.24f, 0.24f, 0.52f, 0.52f, 0.08f, true); break;
                case Icon.Skip: Poly(true, P(0.62f, 0.2f), P(0.25f, 0.5f), P(0.62f, 0.8f)); Box(0.66f, 0.2f, 0.1f, 0.6f, 0.03f, true); break;
                case Icon.Loop:
                    Ring(0.5f, 0.5f, 0.3f, 200, 340);
                    Ring(0.5f, 0.5f, 0.3f, 20, 160);
                    Poly(true, P(0.86f, 0.36f), P(0.72f, 0.5f), P(0.66f, 0.3f));
                    Poly(true, P(0.14f, 0.64f), P(0.28f, 0.5f), P(0.34f, 0.7f));
                    break;
                case Icon.Plus: Seg(0.5f, 0.2f, 0.5f, 0.8f); Seg(0.2f, 0.5f, 0.8f, 0.5f); break;
                case Icon.Close: Seg(0.26f, 0.26f, 0.74f, 0.74f); Seg(0.74f, 0.26f, 0.26f, 0.74f); break;
                case Icon.Check: Poly(false, P(0.22f, 0.52f), P(0.42f, 0.72f), P(0.78f, 0.3f)); break;
                case Icon.Trash:
                    Seg(0.2f, 0.28f, 0.8f, 0.28f); Seg(0.4f, 0.18f, 0.6f, 0.18f);
                    Poly(false, P(0.28f, 0.3f), P(0.32f, 0.82f), P(0.68f, 0.82f), P(0.72f, 0.3f));
                    break;
                case Icon.Undo:
                    Ring(0.52f, 0.56f, 0.26f, 200, 450 - 30);
                    Poly(true, P(0.14f, 0.36f), P(0.38f, 0.3f), P(0.26f, 0.56f));
                    break;
                case Icon.Redo:
                    Ring(0.48f, 0.56f, 0.26f, 120, 340);
                    Poly(true, P(0.86f, 0.36f), P(0.62f, 0.3f), P(0.74f, 0.56f));
                    break;
                case Icon.Save:
                    Box(0.2f, 0.2f, 0.6f, 0.6f, 0.08f, false);
                    Box(0.34f, 0.2f, 0.32f, 0.18f, 0.02f, false);
                    Box(0.32f, 0.56f, 0.36f, 0.24f, 0.02f, true);
                    break;
                case Icon.Open:
                case Icon.Folder:
                    Poly(false, P(0.14f, 0.3f), P(0.4f, 0.3f), P(0.48f, 0.38f), P(0.86f, 0.38f), P(0.86f, 0.78f), P(0.14f, 0.78f), P(0.14f, 0.3f));
                    break;
                case Icon.New:
                    Poly(false, P(0.26f, 0.16f), P(0.58f, 0.16f), P(0.76f, 0.34f), P(0.76f, 0.84f), P(0.26f, 0.84f), P(0.26f, 0.16f));
                    Seg(0.51f, 0.48f, 0.51f, 0.7f); Seg(0.4f, 0.59f, 0.62f, 0.59f);
                    break;
                case Icon.Import:
                    Seg(0.5f, 0.14f, 0.5f, 0.6f); Poly(false, P(0.32f, 0.44f), P(0.5f, 0.62f), P(0.68f, 0.44f));
                    Poly(false, P(0.18f, 0.62f), P(0.18f, 0.82f), P(0.82f, 0.82f), P(0.82f, 0.62f));
                    break;
                case Icon.Export:
                    Seg(0.5f, 0.6f, 0.5f, 0.14f); Poly(false, P(0.32f, 0.32f), P(0.5f, 0.14f), P(0.68f, 0.32f));
                    Poly(false, P(0.18f, 0.62f), P(0.18f, 0.82f), P(0.82f, 0.82f), P(0.82f, 0.62f));
                    break;
                case Icon.Gear:
                    Ring(0.5f, 0.5f, 0.13f, 0, 360);
                    for (int i = 0; i < 8; i++)
                    {
                        float a = i * Mathf.PI / 4f;
                        var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                        Seg(0.5f + d.x * 0.24f, 0.5f + d.y * 0.24f, 0.5f + d.x * 0.36f, 0.5f + d.y * 0.36f);
                    }
                    Ring(0.5f, 0.5f, 0.25f, 0, 360);
                    break;
                case Icon.Magnet:
                    Ring(0.5f, 0.5f, 0.24f, 0, 180);
                    Seg(0.26f, 0.5f, 0.26f, 0.2f); Seg(0.74f, 0.5f, 0.74f, 0.2f);
                    Seg(0.2f, 0.28f, 0.32f, 0.28f); Seg(0.68f, 0.28f, 0.8f, 0.28f);
                    break;
                case Icon.Pointer:
                    Poly(true, P(0.3f, 0.14f), P(0.3f, 0.8f), P(0.45f, 0.66f), P(0.56f, 0.88f), P(0.65f, 0.84f), P(0.54f, 0.62f), P(0.74f, 0.62f));
                    break;
                case Icon.TrimTool:
                    Box(0.1f, 0.3f, 0.8f, 0.4f, 0.04f, false);
                    Box(0.45f, 0.2f, 0.1f, 0.6f, 0.03f, true);
                    Poly(false, P(0.33f, 0.42f), P(0.25f, 0.5f), P(0.33f, 0.58f));
                    Poly(false, P(0.67f, 0.42f), P(0.75f, 0.5f), P(0.67f, 0.58f));
                    break;
                case Icon.Blade:
                    Box(0.1f, 0.3f, 0.8f, 0.4f, 0.06f, false);
                    for (int i = 0; i < 6; i++) Seg(0.2f + i * 0.12f, 0.3f, 0.2f + i * 0.12f, 0.38f);
                    for (int i = 0; i < 6; i++) Seg(0.2f + i * 0.12f, 0.62f, 0.2f + i * 0.12f, 0.7f);
                    Box(0.3f, 0.46f, 0.4f, 0.08f, 0.04f, true);
                    break;
                case Icon.DynamicTrim:
                    Ring(0.5f, 0.5f, 0.38f, 140, 220);
                    Ring(0.5f, 0.5f, 0.38f, -40, 40);
                    Seg(0.5f, 0.3f, 0.5f, 0.78f);
                    Poly(true, P(0.43f, 0.22f), P(0.57f, 0.22f), P(0.5f, 0.32f));
                    Poly(false, P(0.36f, 0.42f), P(0.28f, 0.5f), P(0.36f, 0.58f));
                    Poly(false, P(0.64f, 0.42f), P(0.72f, 0.5f), P(0.64f, 0.58f));
                    break;
                case Icon.Scissors:
                    Ring(0.3f, 0.72f, 0.12f, 0, 360); Ring(0.7f, 0.72f, 0.12f, 0, 360);
                    Seg(0.38f, 0.62f, 0.7f, 0.16f); Seg(0.62f, 0.62f, 0.3f, 0.16f);
                    break;
                case Icon.Duplicate:
                case Icon.Copy:
                    Box(0.18f, 0.3f, 0.44f, 0.5f, 0.07f, false);
                    Poly(false, P(0.38f, 0.22f), P(0.38f, 0.16f), P(0.82f, 0.16f), P(0.82f, 0.62f), P(0.68f, 0.62f));
                    break;
                case Icon.Dice:
                    Box(0.18f, 0.18f, 0.64f, 0.64f, 0.12f, false);
                    Dot(0.36f, 0.36f, 0.055f); Dot(0.64f, 0.64f, 0.055f); Dot(0.5f, 0.5f, 0.055f);
                    Dot(0.64f, 0.36f, 0.055f); Dot(0.36f, 0.64f, 0.055f);
                    break;
                case Icon.Waveform:
                {
                    float[] h = { 0.12f, 0.3f, 0.18f, 0.36f, 0.24f, 0.1f };
                    for (int i = 0; i < h.Length; i++) { float x = 0.18f + i * 0.128f; Seg(x, 0.5f - h[i], x, 0.5f + h[i]); }
                    break;
                }
                case Icon.Mixer:
                    Seg(0.28f, 0.16f, 0.28f, 0.84f); Seg(0.5f, 0.16f, 0.5f, 0.84f); Seg(0.72f, 0.16f, 0.72f, 0.84f);
                    Box(0.2f, 0.56f, 0.16f, 0.1f, 0.03f, true); Box(0.42f, 0.28f, 0.16f, 0.1f, 0.03f, true); Box(0.64f, 0.46f, 0.16f, 0.1f, 0.03f, true);
                    break;
                case Icon.Sine:
                {
                    var pts = new Vector2[17];
                    for (int i = 0; i < pts.Length; i++)
                    {
                        float t = i / 16f;
                        pts[i] = P(0.14f + t * 0.72f, 0.5f - Mathf.Sin(t * Mathf.PI * 2) * 0.24f);
                    }
                    Poly(false, pts);
                    break;
                }
                case Icon.Layers:
                    Poly(false, P(0.5f, 0.18f), P(0.84f, 0.36f), P(0.5f, 0.54f), P(0.16f, 0.36f), P(0.5f, 0.18f));
                    Poly(false, P(0.16f, 0.52f), P(0.5f, 0.7f), P(0.84f, 0.52f));
                    Poly(false, P(0.16f, 0.66f), P(0.5f, 0.84f), P(0.84f, 0.66f));
                    break;
                case Icon.Grid:
                    Box(0.18f, 0.18f, 0.26f, 0.26f, 0.05f, false); Box(0.56f, 0.18f, 0.26f, 0.26f, 0.05f, false);
                    Box(0.18f, 0.56f, 0.26f, 0.26f, 0.05f, false); Box(0.56f, 0.56f, 0.26f, 0.26f, 0.05f, false);
                    break;
                case Icon.Power:
                    Ring(0.5f, 0.54f, 0.28f, -60 + 360 - 360, 240);
                    Seg(0.5f, 0.16f, 0.5f, 0.48f);
                    break;
                case Icon.ChevronDown: Poly(false, P(0.28f, 0.4f), P(0.5f, 0.62f), P(0.72f, 0.4f)); break;
                case Icon.ChevronRight: Poly(false, P(0.4f, 0.28f), P(0.62f, 0.5f), P(0.4f, 0.72f)); break;
                case Icon.Up: Poly(false, P(0.28f, 0.6f), P(0.5f, 0.38f), P(0.72f, 0.6f)); break;
                case Icon.Down: Poly(false, P(0.28f, 0.4f), P(0.5f, 0.62f), P(0.72f, 0.4f)); break;
                case Icon.Curve:
                    p.BeginPath(); p.MoveTo(P(0.14f, 0.78f)); p.BezierCurveTo(P(0.45f, 0.78f), P(0.5f, 0.22f), P(0.86f, 0.22f)); p.Stroke();
                    Dot(0.14f, 0.78f, 0.07f); Dot(0.86f, 0.22f, 0.07f);
                    break;
                case Icon.Speaker:
                    Poly(true, P(0.16f, 0.4f), P(0.32f, 0.4f), P(0.52f, 0.22f), P(0.52f, 0.78f), P(0.32f, 0.6f), P(0.16f, 0.6f));
                    Ring(0.52f, 0.5f, 0.16f, -50, 50); Ring(0.52f, 0.5f, 0.3f, -50, 50);
                    break;
                case Icon.Search:
                    Ring(0.44f, 0.44f, 0.22f, 0, 360); Seg(0.6f, 0.6f, 0.82f, 0.82f);
                    break;
                case Icon.Sparkle:
                    Poly(true, P(0.42f, 0.14f), P(0.5f, 0.42f), P(0.78f, 0.5f), P(0.5f, 0.58f), P(0.42f, 0.86f), P(0.34f, 0.58f), P(0.06f, 0.5f), P(0.34f, 0.42f));
                    Poly(true, P(0.78f, 0.1f), P(0.82f, 0.22f), P(0.94f, 0.26f), P(0.82f, 0.3f), P(0.78f, 0.42f), P(0.74f, 0.3f), P(0.62f, 0.26f), P(0.74f, 0.22f));
                    break;
                case Icon.Grip:
                    Dot(0.38f, 0.28f, 0.06f); Dot(0.62f, 0.28f, 0.06f); Dot(0.38f, 0.5f, 0.06f);
                    Dot(0.62f, 0.5f, 0.06f); Dot(0.38f, 0.72f, 0.06f); Dot(0.62f, 0.72f, 0.06f);
                    break;
                case Icon.FolderOpen:
                    Poly(false, P(0.14f, 0.74f), P(0.14f, 0.3f), P(0.4f, 0.3f), P(0.48f, 0.38f), P(0.78f, 0.38f), P(0.78f, 0.48f));
                    Poly(false, P(0.14f, 0.78f), P(0.28f, 0.48f), P(0.9f, 0.48f), P(0.76f, 0.78f), P(0.14f, 0.78f));
                    break;
                case Icon.FolderPlus:
                    Poly(false, P(0.56f, 0.78f), P(0.12f, 0.78f), P(0.12f, 0.3f), P(0.38f, 0.3f), P(0.46f, 0.38f), P(0.84f, 0.38f), P(0.84f, 0.52f));
                    Seg(0.76f, 0.58f, 0.76f, 0.9f); Seg(0.6f, 0.74f, 0.92f, 0.74f);
                    break;
                case Icon.Pencil:
                    Poly(false, P(0.2f, 0.8f), P(0.24f, 0.64f), P(0.66f, 0.22f), P(0.78f, 0.34f), P(0.36f, 0.76f), P(0.2f, 0.8f));
                    Seg(0.58f, 0.3f, 0.7f, 0.42f);
                    break;
                case Icon.More: Dot(0.24f, 0.5f, 0.07f); Dot(0.5f, 0.5f, 0.07f); Dot(0.76f, 0.5f, 0.07f); break;
                case Icon.Warning:
                    Poly(false, P(0.5f, 0.16f), P(0.86f, 0.82f), P(0.14f, 0.82f), P(0.5f, 0.16f));
                    Seg(0.5f, 0.4f, 0.5f, 0.6f); Dot(0.5f, 0.71f, 0.045f);
                    break;
                case Icon.Logo:
                {
                    // Stylized waveform inside a rounded tile.
                    Box(0.06f, 0.06f, 0.88f, 0.88f, 0.24f, true);
                    p.strokeColor = StudioTheme.Background;
                    float[] h = { 0.1f, 0.24f, 0.32f, 0.18f, 0.08f };
                    for (int i = 0; i < h.Length; i++) { float x = 0.26f + i * 0.12f; Seg(x, 0.5f - h[i], x, 0.5f + h[i]); }
                    break;
                }
            }
        }
    }
}
