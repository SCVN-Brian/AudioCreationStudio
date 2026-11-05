using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace AudioCreationStudio.EditorUI
{
    /// <summary>Time ruler: adaptive ticks, loop region bar and playhead head. Click/drag to scrub, Shift-drag to set a loop region.</summary>
    public sealed class TimelineRuler : VisualElement
    {
        private enum Drag { None, Scrub, LoopNew, LoopStart, LoopEnd, LoopMove }
        private readonly StudioContext _ctx;
        private readonly QuadBatch _quads = new QuadBatch();
        private readonly List<Label> _labels = new List<Label>();
        private Drag _drag;
        private long _anchor, _origStart, _origEnd;

        private TimelineViewState V => _ctx.Session.View;
        private int SR => _ctx.Data.sampleRate;

        public TimelineRuler(StudioContext ctx)
        {
            _ctx = ctx;
            AddToClassList("acs-ruler");
            style.height = TimelineViewState.RulerHeight;
            generateVisualContent += Draw;
            RegisterCallback<PointerDownEvent>(OnDown);
            RegisterCallback<PointerMoveEvent>(OnMove);
            RegisterCallback<PointerUpEvent>(OnUp);
            RegisterCallback<GeometryChangedEvent>(_ => Refresh());
            tooltip = "Click or drag to move the playhead · Shift-drag to set a loop region · Double-click the loop bar to toggle it";
        }

        public void Refresh()
        {
            MarkDirtyRepaint();
            LayoutLabels();
        }

        private void LayoutLabels()
        {
            var r = contentRect;
            V.GridSteps(out double major, out _);
            double t0 = Math.Max(0, Math.Floor(V.SecondsAtX(0) / major) * major), t1 = V.SecondsAtX(r.width);
            int used = 0;
            for (double s = t0; s <= t1; s += major)
            {
                float x = (float)((s - V.scrollSeconds) * V.pixelsPerSecond);
                while (_labels.Count <= used)
                {
                    var l = new Label { pickingMode = PickingMode.Ignore };
                    l.AddToClassList("acs-ruler__label");
                    Add(l);
                    _labels.Add(l);
                }
                var lab = _labels[used++];
                lab.style.display = DisplayStyle.Flex;
                lab.text = TimelineViewState.FormatTime(s, major);
                lab.style.left = x + 4;
            }
            for (int i = used; i < _labels.Count; i++) _labels[i].style.display = DisplayStyle.None;
        }

        private Rect LoopBar => new Rect(0, contentRect.height - 7, contentRect.width, 7);

        private void Draw(MeshGenerationContext mgc)
        {
            var r = contentRect;
            var data = _ctx.Data;
            float ppp = UnityEditor.EditorGUIUtility.pixelsPerPoint;
            _quads.Rect(0, 0, r.width, r.height, StudioTheme.Surface);
            _quads.Rect(0, r.height - 1, r.width, 1, StudioTheme.Border);
            V.GridSteps(out double major, out double minor);
            double t0 = Math.Max(0, Math.Floor(V.SecondsAtX(0) / minor) * minor), t1 = V.SecondsAtX(r.width);
            for (double s = t0; s <= t1; s += minor)
            {
                float x = Mathf.Round((float)((s - V.scrollSeconds) * V.pixelsPerSecond) * ppp) / ppp;
                bool isMajor = Math.Abs(s / major - Math.Round(s / major)) < 1e-6;
                float h = isMajor ? 12 : 5;
                _quads.Rect(x, r.height - h - 1, 1f / ppp, h, isMajor ? StudioTheme.TextDim.WithAlpha(0.7f) : StudioTheme.TextFaint.WithAlpha(0.5f));
            }
            // Content end marker
            float endX = V.X(data.ContentEnd, SR);
            if (endX > 0 && endX < r.width) _quads.Rect(endX, 4, 1, r.height - 8, StudioTheme.TextFaint.WithAlpha(0.5f));

            var lr = data.loopRegion;
            if (lr.Length > 0)
            {
                float a = V.X(lr.start, SR), b = V.X(lr.end, SR);
                var c = lr.enabled ? StudioTheme.Accent : StudioTheme.TextFaint;
                _quads.Rect(a, LoopBar.y, b - a, LoopBar.height - 1, c.WithAlpha(lr.enabled ? 0.55f : 0.3f));
                _quads.Rect(a, LoopBar.y - 4, 2, LoopBar.height + 3, c);
                _quads.Rect(b - 2, LoopBar.y - 4, 2, LoopBar.height + 3, c);
            }
            _quads.Flush(mgc);

            float px = V.X(_ctx.Session.Playhead, SR);
            if (px > -8 && px < r.width + 8)
            {
                var p = mgc.painter2D;
                p.fillColor = StudioTheme.Playhead;
                p.BeginPath();
                p.MoveTo(new Vector2(px - 6, r.height - 14));
                p.LineTo(new Vector2(px + 6, r.height - 14));
                p.LineTo(new Vector2(px, r.height - 3));
                p.ClosePath();
                p.Fill();
                PaintUtil.Line(p, new Vector2(px, r.height - 4), new Vector2(px, r.height), StudioTheme.Playhead, 1.5f);
            }
        }

        private void OnDown(PointerDownEvent e)
        {
            if (e.button != 0) return;
            var lr = _ctx.Data.loopRegion;
            float x = e.localPosition.x;
            long s = Math.Max(0, V.SampleAtX(x, SR));
            bool inBar = e.localPosition.y >= LoopBar.y - 6;
            float a = V.X(lr.start, SR), b = V.X(lr.end, SR);
            if (inBar && lr.Length > 0 && e.clickCount == 2 && x >= a && x <= b)
            {
                _ctx.Session.Modify("Toggle Loop Region", d => d.loopRegion.enabled = !d.loopRegion.enabled);
                e.StopPropagation();
                return;
            }
            if (lr.Length > 0 && Mathf.Abs(x - a) < 6) _drag = Drag.LoopStart;
            else if (lr.Length > 0 && Mathf.Abs(x - b) < 6) _drag = Drag.LoopEnd;
            else if (inBar && lr.Length > 0 && x > a && x < b) { _drag = Drag.LoopMove; _anchor = s; _origStart = lr.start; _origEnd = lr.end; }
            else if (e.shiftKey) { _drag = Drag.LoopNew; _anchor = s; }
            else { _drag = Drag.Scrub; _ctx.Transport.Seek(s); }
            if (_drag != Drag.Scrub) _ctx.Session.BeginContinuous("Edit Loop Region");
            this.CapturePointer(e.pointerId);
            e.StopPropagation();
            Refresh();
        }

        private void OnMove(PointerMoveEvent e)
        {
            if (_drag == Drag.None || !this.HasPointerCapture(e.pointerId)) return;
            long s = Math.Max(0, V.SampleAtX(e.localPosition.x, SR));
            switch (_drag)
            {
                case Drag.Scrub: _ctx.Transport.Seek(s); break;
                case Drag.LoopNew:
                    _ctx.Session.Continuous(d => { d.loopRegion.enabled = true; d.loopRegion.start = Math.Min(_anchor, s); d.loopRegion.end = Math.Max(_anchor, s); });
                    break;
                case Drag.LoopStart:
                    _ctx.Session.Continuous(d => { d.loopRegion.start = Math.Min(s, d.loopRegion.end - 1); d.loopRegion.enabled = true; });
                    break;
                case Drag.LoopEnd:
                    _ctx.Session.Continuous(d => { d.loopRegion.end = Math.Max(s, d.loopRegion.start + 1); d.loopRegion.enabled = true; });
                    break;
                case Drag.LoopMove:
                {
                    long delta = s - _anchor;
                    if (_origStart + delta < 0) delta = -_origStart;
                    _ctx.Session.Continuous(d => { d.loopRegion.start = _origStart + delta; d.loopRegion.end = _origEnd + delta; });
                    break;
                }
            }
            Refresh();
        }

        private void OnUp(PointerUpEvent e)
        {
            if (_drag == Drag.None) return;
            if (_drag != Drag.Scrub) _ctx.Session.EndContinuous();
            _drag = Drag.None;
            this.ReleasePointer(e.pointerId);
        }
    }
}
