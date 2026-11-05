using System;
using System.Collections.Generic;
using AudioCreationStudio.DSP;
using UnityEngine;
using UnityEngine.UIElements;

namespace AudioCreationStudio.EditorUI
{
    /// <summary>
    /// Stand-alone waveform display for a buffer (generator output, loops, variations) with optional
    /// draggable markers, shaded regions and a playhead.
    /// </summary>
    public class BufferWaveformView : VisualElement
    {
        public sealed class Marker
        {
            public long Frame;
            public Color Color;
            public string Label;
            public bool Draggable;
        }

        public sealed class Region
        {
            public long Start, End;
            public Color Color;
        }

        private readonly QuadBatch _quads = new QuadBatch();
        private WaveformPeaks _peaks;
        public readonly List<Marker> Markers = new List<Marker>();
        public readonly List<Region> Regions = new List<Region>();
        public long PlayheadFrame = -1;
        public Color WaveColor = StudioTheme.Accent;
        public double ViewStart, ViewEnd; // frames; ViewEnd <= ViewStart → whole buffer
        private int _dragMarker = -1;
        private readonly List<Label> _labels = new List<Label>();
        public event Action<int, long> MarkerMoved;
        public event Action<int> MarkerDragBegan;
        public event Action<int> MarkerDragEnded;
        public event Action<long> Clicked;

        public BufferWaveformView(float height = 120)
        {
            AddToClassList("acs-wave");
            style.height = height;
            generateVisualContent += Draw;
            RegisterCallback<PointerDownEvent>(OnDown);
            RegisterCallback<PointerMoveEvent>(OnMove);
            RegisterCallback<PointerUpEvent>(OnUp);
            RegisterCallback<GeometryChangedEvent>(_ => Refresh());
        }

        public WaveformPeaks Peaks => _peaks;
        public int Frames => _peaks?.Frames ?? 0;

        public void SetPeaks(WaveformPeaks peaks)
        {
            _peaks = peaks;
            Refresh();
        }

        public void SetBuffer(AudioBuffer buffer)
        {
            _peaks = buffer == null ? null : WaveformPeaks.Build(buffer);
            Refresh();
        }

        public void Refresh()
        {
            MarkDirtyRepaint();
            LayoutLabels();
        }

        private double VS => ViewEnd > ViewStart ? ViewStart : 0;
        private double VE => ViewEnd > ViewStart ? ViewEnd : Math.Max(1, Frames);

        public float XForFrame(double f) => (float)((f - VS) / (VE - VS) * contentRect.width);
        public long FrameForX(float x) => (long)Math.Round(VS + x / Math.Max(1, contentRect.width) * (VE - VS));

        private void Draw(MeshGenerationContext mgc)
        {
            var r = contentRect;
            var p = mgc.painter2D;
            PaintUtil.FillRounded(p, r, 8, StudioTheme.Inset);
            if (_peaks == null || r.width < 2) return;
            float mid = r.center.y, half = r.height * 0.5f - 6;
            foreach (var reg in Regions)
            {
                float a = XForFrame(reg.Start), b = XForFrame(reg.End);
                _quads.Rect(Mathf.Max(0, a), 0, Mathf.Min(r.width, b) - Mathf.Max(0, a), r.height, reg.Color);
            }
            _quads.Rect(0, mid, r.width, 1, WaveColor.WithAlpha(0.25f));
            float ppp = UnityEditor.EditorGUIUtility.pixelsPerPoint;
            float step = 1f / ppp;
            double fpp = (VE - VS) / r.width * step;
            for (float x = 0; x < r.width; x += step)
            {
                double f0 = VS + x / r.width * (VE - VS);
                if (f0 >= _peaks.Frames || f0 < 0) continue;
                _peaks.Query(f0, f0 + Math.Max(1, fpp), out float mn, out float mx, out float rms);
                float yt = mid - Mathf.Clamp(mx, -1, 1) * half, yb = mid - Mathf.Clamp(mn, -1, 1) * half;
                _quads.Rect(x, yt, step, Mathf.Max(step, yb - yt), WaveColor.WithAlpha(0.85f));
                float rh = Mathf.Min(rms, 1) * half;
                if (rh > 0.5f) _quads.Rect(x, mid - rh, step, rh * 2, StudioTheme.Lighten(WaveColor, 0.45f).WithAlpha(0.6f));
                if (mx >= 1f || mn <= -1f) _quads.Rect(x, 2, step, 3, StudioTheme.Error);
            }
            foreach (var m in Markers)
            {
                float x = XForFrame(m.Frame);
                if (x < -2 || x > r.width + 2) continue;
                _quads.Rect(x - 1, 0, 2, r.height, m.Color);
                if (m.Draggable) _quads.Rect(x - 5, 0, 10, 8, m.Color);
            }
            if (PlayheadFrame >= 0)
            {
                float x = XForFrame(PlayheadFrame);
                _quads.Rect(x - 0.75f, 0, 1.5f, r.height, StudioTheme.Playhead);
            }
            _quads.Flush(mgc);
        }

        private void LayoutLabels()
        {
            int used = 0;
            foreach (var m in Markers)
            {
                if (string.IsNullOrEmpty(m.Label)) continue;
                while (_labels.Count <= used)
                {
                    var l = new Label { pickingMode = PickingMode.Ignore };
                    l.AddToClassList("acs-wave__marker");
                    Add(l);
                    _labels.Add(l);
                }
                var lab = _labels[used++];
                lab.style.display = DisplayStyle.Flex;
                lab.text = m.Label;
                lab.style.left = XForFrame(m.Frame) + 6;
                lab.style.color = m.Color;
            }
            for (int i = used; i < _labels.Count; i++) _labels[i].style.display = DisplayStyle.None;
        }

        private void OnDown(PointerDownEvent e)
        {
            if (e.button != 0 || _peaks == null) return;
            for (int i = 0; i < Markers.Count; i++)
            {
                if (!Markers[i].Draggable) continue;
                if (Mathf.Abs(XForFrame(Markers[i].Frame) - e.localPosition.x) < 7)
                {
                    _dragMarker = i;
                    MarkerDragBegan?.Invoke(i);
                    this.CapturePointer(e.pointerId);
                    e.StopPropagation();
                    return;
                }
            }
            Clicked?.Invoke(Math.Max(0, Math.Min(Frames, FrameForX(e.localPosition.x))));
        }

        private void OnMove(PointerMoveEvent e)
        {
            if (_dragMarker < 0 || !this.HasPointerCapture(e.pointerId)) return;
            long f = Math.Max(0, Math.Min(Frames, FrameForX(e.localPosition.x)));
            Markers[_dragMarker].Frame = f;
            MarkerMoved?.Invoke(_dragMarker, f);
            Refresh();
        }

        private void OnUp(PointerUpEvent e)
        {
            if (_dragMarker < 0) return;
            int i = _dragMarker;
            _dragMarker = -1;
            this.ReleasePointer(e.pointerId);
            MarkerDragEnded?.Invoke(i);
        }
    }

    /// <summary>Simple function plot (polyline + soft fill) for generator visualizations.</summary>
    public class CurvePlotView : VisualElement
    {
        private readonly List<Vector2> _pts = new List<Vector2>(256);
        private readonly QuadBatch _quads = new QuadBatch();
        private float[] _values = Array.Empty<float>();
        private float _min, _max = 1;
        private readonly List<float> _vlines = new List<float>();
        public Color CurveColor = StudioTheme.Accent;
        public bool Fill = true;
        public float Baseline = float.NaN;

        public CurvePlotView(float height = 76)
        {
            AddToClassList("acs-plot");
            style.height = height;
            generateVisualContent += Draw;
        }

        public void Set(float[] values, float min, float max, params float[] verticalLines01)
        {
            _values = values ?? Array.Empty<float>();
            _min = min;
            _max = Math.Abs(max - min) < 1e-6f ? min + 1 : max;
            _vlines.Clear();
            if (verticalLines01 != null) _vlines.AddRange(verticalLines01);
            MarkDirtyRepaint();
        }

        private void Draw(MeshGenerationContext mgc)
        {
            var r = contentRect;
            var p = mgc.painter2D;
            PaintUtil.FillRounded(p, r, 6, StudioTheme.Inset);
            if (_values.Length < 2) return;
            var plot = new Rect(4, 6, r.width - 8, r.height - 12);
            float Y(float v) => plot.yMax - Mathf.Clamp01((v - _min) / (_max - _min)) * plot.height;
            for (int i = 1; i < 4; i++) _quads.Rect(plot.x, plot.y + plot.height * i / 4f, plot.width, 1, StudioTheme.GridMinor);
            foreach (var v in _vlines) _quads.Rect(plot.x + v * plot.width, plot.y, 1, plot.height, StudioTheme.GridMajor);
            if (!float.IsNaN(Baseline)) _quads.Rect(plot.x, Y(Baseline), plot.width, 1, StudioTheme.GridMajor.WithAlpha(0.15f));
            _quads.Flush(mgc);
            _pts.Clear();
            for (int i = 0; i < _values.Length; i++)
                _pts.Add(new Vector2(plot.x + i / (float)(_values.Length - 1) * plot.width, Y(_values[i])));
            if (Fill)
            {
                float by = float.IsNaN(Baseline) ? plot.yMax : Y(Baseline);
                p.fillColor = CurveColor.WithAlpha(0.13f);
                p.BeginPath();
                p.MoveTo(new Vector2(plot.x, by));
                foreach (var pt in _pts) p.LineTo(pt);
                p.LineTo(new Vector2(plot.xMax, by));
                p.ClosePath();
                p.Fill();
            }
            PaintUtil.Polyline(p, _pts, CurveColor, 1.8f);
        }
    }
}
