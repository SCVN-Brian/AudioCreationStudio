using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace AudioCreationStudio.EditorUI
{
    /// <summary>Painter2D helpers for rounded shapes.</summary>
    public static class PaintUtil
    {
        public static void RoundedRectPath(Painter2D p, Rect r, float radius)
        {
            radius = Mathf.Min(radius, r.width * 0.5f, r.height * 0.5f);
            p.BeginPath();
            if (radius < 0.5f)
            {
                p.MoveTo(new Vector2(r.xMin, r.yMin));
                p.LineTo(new Vector2(r.xMax, r.yMin));
                p.LineTo(new Vector2(r.xMax, r.yMax));
                p.LineTo(new Vector2(r.xMin, r.yMax));
                p.ClosePath();
                return;
            }
            p.MoveTo(new Vector2(r.xMin + radius, r.yMin));
            p.LineTo(new Vector2(r.xMax - radius, r.yMin));
            p.ArcTo(new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMin + radius), radius);
            p.LineTo(new Vector2(r.xMax, r.yMax - radius));
            p.ArcTo(new Vector2(r.xMax, r.yMax), new Vector2(r.xMax - radius, r.yMax), radius);
            p.LineTo(new Vector2(r.xMin + radius, r.yMax));
            p.ArcTo(new Vector2(r.xMin, r.yMax), new Vector2(r.xMin, r.yMax - radius), radius);
            p.LineTo(new Vector2(r.xMin, r.yMin + radius));
            p.ArcTo(new Vector2(r.xMin, r.yMin), new Vector2(r.xMin + radius, r.yMin), radius);
            p.ClosePath();
        }

        public static void FillRounded(Painter2D p, Rect r, float radius, Color c)
        {
            if (r.width <= 0 || r.height <= 0) return;
            p.fillColor = c;
            RoundedRectPath(p, r, radius);
            p.Fill();
        }

        public static void StrokeRounded(Painter2D p, Rect r, float radius, Color c, float width)
        {
            if (r.width <= 0 || r.height <= 0) return;
            p.strokeColor = c;
            p.lineWidth = width;
            RoundedRectPath(p, r, radius);
            p.Stroke();
        }

        public static void Circle(Painter2D p, Vector2 center, float radius, Color fill)
        {
            p.fillColor = fill;
            p.BeginPath();
            p.Arc(center, radius, Angle.Degrees(0), Angle.Degrees(360), ArcDirection.Clockwise);
            p.ClosePath();
            p.Fill();
        }

        public static void Line(Painter2D p, Vector2 a, Vector2 b, Color c, float width)
        {
            p.strokeColor = c;
            p.lineWidth = width;
            p.BeginPath();
            p.MoveTo(a);
            p.LineTo(b);
            p.Stroke();
        }

        public static void Polyline(Painter2D p, List<Vector2> pts, Color c, float width)
        {
            if (pts.Count < 2) return;
            p.strokeColor = c;
            p.lineWidth = width;
            p.lineJoin = LineJoin.Round;
            p.BeginPath();
            p.MoveTo(pts[0]);
            for (int i = 1; i < pts.Count; i++) p.LineTo(pts[i]);
            p.Stroke();
        }
    }

    /// <summary>
    /// Batches axis-aligned colored quads into raw UI Toolkit meshes. Used for waveforms, grids and meters,
    /// which would be far too expensive as individual Painter2D shapes.
    /// </summary>
    public sealed class QuadBatch
    {
        private const int MaxQuadsPerMesh = 16000;
        private readonly List<Vector4> _rects = new List<Vector4>(4096);
        private readonly List<Color32> _colors = new List<Color32>(4096);

        public int Count => _rects.Count;

        public void Clear() { _rects.Clear(); _colors.Clear(); }

        public void Rect(float x, float y, float w, float h, Color c)
        {
            if (w <= 0 || h <= 0) return;
            _rects.Add(new Vector4(x, y, w, h));
            _colors.Add(c);
        }

        public void Rect(Rect r, Color c) => Rect(r.x, r.y, r.width, r.height, c);

        public void Flush(MeshGenerationContext ctx)
        {
            int total = _rects.Count;
            for (int start = 0; start < total; start += MaxQuadsPerMesh)
            {
                int n = Mathf.Min(MaxQuadsPerMesh, total - start);
                var mesh = ctx.Allocate(n * 4, n * 6);
                for (int i = 0; i < n; i++)
                {
                    var r = _rects[start + i];
                    var c = _colors[start + i];
                    float x0 = r.x, y0 = r.y, x1 = r.x + r.z, y1 = r.y + r.w;
                    mesh.SetNextVertex(new Vertex { position = new Vector3(x0, y0, Vertex.nearZ), tint = c });
                    mesh.SetNextVertex(new Vertex { position = new Vector3(x1, y0, Vertex.nearZ), tint = c });
                    mesh.SetNextVertex(new Vertex { position = new Vector3(x1, y1, Vertex.nearZ), tint = c });
                    mesh.SetNextVertex(new Vertex { position = new Vector3(x0, y1, Vertex.nearZ), tint = c });
                    ushort b = (ushort)(i * 4);
                    mesh.SetNextIndex(b); mesh.SetNextIndex((ushort)(b + 1)); mesh.SetNextIndex((ushort)(b + 2));
                    mesh.SetNextIndex((ushort)(b + 2)); mesh.SetNextIndex((ushort)(b + 3)); mesh.SetNextIndex(b);
                }
            }
            Clear();
        }
    }
}
