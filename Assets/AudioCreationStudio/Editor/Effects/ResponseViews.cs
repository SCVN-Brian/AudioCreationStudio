using System;
using System.Collections.Generic;
using AudioCreationStudio.DSP;
using UnityEngine;
using UnityEngine.UIElements;

namespace AudioCreationStudio.EditorUI
{
    /// <summary>Log-frequency magnitude response plot computed from real filter coefficients.</summary>
    public class FrequencyResponseView : VisualElement
    {
        private readonly QuadBatch _quads = new QuadBatch();
        private readonly List<Vector2> _pts = new List<Vector2>(200);
        private Func<double, double> _magnitude;
        public float RangeDb = 24f;
        public Color CurveColor = StudioTheme.Accent;

        public FrequencyResponseView(float height = 70)
        {
            AddToClassList("acs-response");
            style.height = height;
            generateVisualContent += Draw;
        }

        /// <summary>Sets the response function: frequency (Hz) → linear magnitude.</summary>
        public void SetResponse(Func<double, double> magnitude)
        {
            _magnitude = magnitude;
            MarkDirtyRepaint();
        }

        private void Draw(MeshGenerationContext ctx)
        {
            var r = contentRect;
            if (r.width < 4) return;
            var p = ctx.painter2D;
            PaintUtil.FillRounded(p, r, 6, StudioTheme.Inset);
            float X(double f) => (float)(Math.Log(f / 20.0) / Math.Log(1000.0)) * r.width;
            float Y(double db) => r.center.y - (float)(db / RangeDb) * (r.height * 0.5f - 4);
            foreach (var f in new double[] { 50, 100, 200, 500, 1000, 2000, 5000, 10000 })
                _quads.Rect(X(f), 0, 1, r.height, StudioTheme.GridMajor);
            foreach (var db in new double[] { -12, 0, 12 })
                _quads.Rect(0, Y(db), r.width, 1, db == 0 ? StudioTheme.GridMajor.WithAlpha(0.14f) : StudioTheme.GridMinor);
            _quads.Flush(ctx);
            if (_magnitude == null) return;
            _pts.Clear();
            int n = Mathf.Clamp((int)r.width / 2, 32, 240);
            for (int i = 0; i <= n; i++)
            {
                double f = 20.0 * Math.Pow(1000.0, i / (double)n);
                double db = 20 * Math.Log10(Math.Max(1e-6, _magnitude(f)));
                _pts.Add(new Vector2(i / (float)n * r.width, Mathf.Clamp(Y(db), 1, r.height - 1)));
            }
            p.fillColor = CurveColor.WithAlpha(0.14f);
            p.BeginPath();
            p.MoveTo(new Vector2(0, r.center.y));
            foreach (var pt in _pts) p.LineTo(pt);
            p.LineTo(new Vector2(r.width, r.center.y));
            p.ClosePath();
            p.Fill();
            PaintUtil.Polyline(p, _pts, CurveColor, 1.8f);
        }
    }

    /// <summary>Compressor static transfer curve (input dB → output dB) from the real gain computer.</summary>
    public class TransferCurveView : VisualElement
    {
        private readonly List<Vector2> _pts = new List<Vector2>(128);
        private readonly QuadBatch _quads = new QuadBatch();
        private float _thr = -18, _ratio = 4, _knee = 6, _makeup;

        public TransferCurveView(float size = 90)
        {
            AddToClassList("acs-response");
            style.height = size;
            generateVisualContent += Draw;
        }

        public void Set(float thr, float ratio, float knee, float makeup)
        {
            _thr = thr; _ratio = ratio; _knee = knee; _makeup = makeup;
            MarkDirtyRepaint();
        }

        private void Draw(MeshGenerationContext ctx)
        {
            var r = contentRect;
            if (r.width < 4) return;
            var p = ctx.painter2D;
            PaintUtil.FillRounded(p, r, 6, StudioTheme.Inset);
            const float lo = -60f;
            float X(float db) => (db - lo) / -lo * r.width;
            float Y(float db) => r.height - (db - lo) / -lo * r.height;
            for (float g = -48; g < 0; g += 12) { _quads.Rect(X(g), 0, 1, r.height, StudioTheme.GridMinor); _quads.Rect(0, Y(g), r.width, 1, StudioTheme.GridMinor); }
            _quads.Rect(X(_thr), 0, 1, r.height, StudioTheme.Warning.WithAlpha(0.4f));
            _quads.Flush(ctx);
            PaintUtil.Line(p, new Vector2(0, r.height), new Vector2(r.width, 0), StudioTheme.GridMajor, 1f);
            _pts.Clear();
            for (int i = 0; i <= 96; i++)
            {
                float inDb = lo + i / 96f * -lo;
                float outDb = inDb + (float)CompressorEffect.ComputeGainReduction(inDb, _thr, _ratio, _knee) + _makeup;
                _pts.Add(new Vector2(X(inDb), Mathf.Clamp(Y(outDb), 0, r.height)));
            }
            PaintUtil.Polyline(p, _pts, StudioTheme.Accent, 1.8f);
        }
    }
}
