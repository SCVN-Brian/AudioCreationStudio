using System;
using System.Collections.Generic;
using AudioCreationStudio.Models;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AudioCreationStudio.EditorUI
{
    /// <summary>
    /// Curve editor for a clip automation lane (time relative to the clip). Click to add, drag to move,
    /// Alt-click / double-click to delete, right-click for interpolation (Linear / Smooth / Step).
    /// </summary>
    public sealed class AutomationCurveElement : VisualElement
    {
        private readonly StudioContext _ctx;
        private readonly string _clipUid;
        private string _target;
        private readonly List<Vector2> _pts = new List<Vector2>(128);
        private readonly QuadBatch _quads = new QuadBatch();
        private int _drag = -1, _hover = -1;

        public AutomationCurveElement(StudioContext ctx, string clipUid, string target)
        {
            _ctx = ctx;
            _clipUid = clipUid;
            _target = target;
            AddToClassList("acs-autocurve");
            generateVisualContent += Draw;
            RegisterCallback<PointerDownEvent>(OnDown);
            RegisterCallback<PointerMoveEvent>(OnMove);
            RegisterCallback<PointerUpEvent>(OnUp);
            tooltip = "Click to add a point · drag to move · Alt-click to delete · right-click for curve type";
        }

        public string Target { get => _target; set { _target = value; MarkDirtyRepaint(); } }

        private ClipData Clip => _ctx.Data.FindClip(_clipUid);
        private Rect Plot => new Rect(6, 6, contentRect.width - 12, contentRect.height - 12);

        private AutomationTargetInfo Info(ClipData c) => AutomationTargetInfo.Find(AutomationTargetInfo.ForClip(c), _target);

        private Vector2 ToPos(ClipData c, AutomationTargetInfo info, AutomationPoint p)
        {
            var r = Plot;
            float x = r.x + (float)(p.time / (double)Math.Max(1, c.lengthSamples)) * r.width;
            return new Vector2(x, r.yMax - info.Range.ToNorm(p.value) * r.height);
        }

        private void Draw(MeshGenerationContext mgc)
        {
            var c = Clip;
            var r = Plot;
            var p = mgc.painter2D;
            PaintUtil.FillRounded(p, contentRect, 6, StudioTheme.Inset);
            if (c == null || r.width < 4) return;
            var info = Info(c);
            for (int i = 1; i < 4; i++) _quads.Rect(r.x, r.y + r.height * i / 4f, r.width, 1, StudioTheme.GridMinor);
            float y0 = r.yMax - info.Range.ToNorm(info.Range.Origin) * r.height;
            _quads.Rect(r.x, y0, r.width, 1, StudioTheme.GridMajor);
            _quads.Flush(mgc);
            var lane = c.FindLane(_target);
            var color = StudioTheme.Accent;
            if (lane == null || lane.points.Count == 0)
            {
                float y = r.yMax - info.Range.ToNorm(info.StaticValue) * r.height;
                for (float x = r.x; x < r.xMax; x += 10) _quads.Rect(x, y, 5, 1, color.WithAlpha(0.6f));
                _quads.Flush(mgc);
                return;
            }
            _pts.Clear();
            var pts = lane.points;
            _pts.Add(new Vector2(r.x, ToPos(c, info, pts[0]).y));
            for (int i = 0; i < pts.Count; i++)
            {
                _pts.Add(ToPos(c, info, pts[i]));
                if (i + 1 >= pts.Count) continue;
                if (pts[i].interpolation == AutomationInterpolation.Step) _pts.Add(new Vector2(ToPos(c, info, pts[i + 1]).x, ToPos(c, info, pts[i]).y));
                else if (pts[i].interpolation == AutomationInterpolation.Smooth)
                    for (int k = 1; k < 16; k++)
                    {
                        long t = pts[i].time + (pts[i + 1].time - pts[i].time) * k / 16;
                        _pts.Add(ToPos(c, info, new AutomationPoint(t, lane.Evaluate(t, info.StaticValue))));
                    }
            }
            _pts.Add(new Vector2(r.xMax, ToPos(c, info, pts[pts.Count - 1]).y));
            PaintUtil.Polyline(p, _pts, lane.enabled ? color : StudioTheme.TextFaint, 1.8f);
            for (int i = 0; i < pts.Count; i++)
            {
                var pos = ToPos(c, info, pts[i]);
                bool hot = i == _hover || i == _drag;
                PaintUtil.Circle(p, pos, hot ? 5.5f : 4.2f, hot ? StudioTheme.Text : color);
                PaintUtil.Circle(p, pos, 2f, StudioTheme.Background);
            }
        }

        private int HitPoint(Vector2 pos)
        {
            var c = Clip;
            var lane = c?.FindLane(_target);
            if (lane == null) return -1;
            var info = Info(c);
            for (int i = 0; i < lane.points.Count; i++) if (Vector2.Distance(ToPos(c, info, lane.points[i]), pos) < 8) return i;
            return -1;
        }

        private void PointFromPos(ClipData c, Vector2 pos, out long time, out float value)
        {
            var r = Plot;
            var info = Info(c);
            time = (long)Math.Round(Mathf.Clamp01((pos.x - r.x) / r.width) * c.lengthSamples);
            value = info.Range.FromNorm((r.yMax - pos.y) / r.height);
        }

        private void OnDown(PointerDownEvent e)
        {
            var c = Clip;
            if (c == null) return;
            int hit = HitPoint(e.localPosition);
            if (e.button == 1 && hit >= 0) { ShowMenu(hit); e.StopPropagation(); return; }
            if (e.button != 0) return;
            if (hit >= 0 && (e.altKey || e.clickCount == 2))
            {
                _ctx.Session.Modify("Delete Automation Point", d => d.FindClip(_clipUid)?.FindLane(_target)?.points.RemoveAt(hit), StudioChange.Content, this);
                MarkDirtyRepaint();
                e.StopPropagation();
                return;
            }
            _ctx.Session.BeginContinuous(hit >= 0 ? "Move Automation Point" : "Add Automation Point");
            if (hit < 0)
            {
                PointFromPos(c, e.localPosition, out long t, out float v);
                _ctx.Session.Continuous(d =>
                {
                    var lane = d.FindClip(_clipUid).GetOrCreateLane(_target);
                    lane.points.Add(new AutomationPoint(t, v));
                    lane.Sort();
                    hit = lane.points.FindIndex(pp => pp.time == t);
                }, StudioChange.Content, this);
            }
            _drag = hit;
            this.CapturePointer(e.pointerId);
            MarkDirtyRepaint();
            e.StopPropagation();
        }

        private void OnMove(PointerMoveEvent e)
        {
            if (_drag < 0)
            {
                int h = HitPoint(e.localPosition);
                if (h != _hover) { _hover = h; MarkDirtyRepaint(); }
                return;
            }
            var c = Clip;
            if (c == null) return;
            PointFromPos(c, e.localPosition, out long t, out float v);
            int idx = _drag;
            _ctx.Session.Continuous(d =>
            {
                var lane = d.FindClip(_clipUid)?.FindLane(_target);
                if (lane == null || idx >= lane.points.Count) return;
                long lo = idx > 0 ? lane.points[idx - 1].time + 1 : 0;
                long hi = idx + 1 < lane.points.Count ? lane.points[idx + 1].time - 1 : c.lengthSamples;
                var p = lane.points[idx];
                p.time = Math.Max(lo, Math.Min(hi, t));
                p.value = v;
                lane.points[idx] = p;
            }, StudioChange.Content, this);
            MarkDirtyRepaint();
        }

        private void OnUp(PointerUpEvent e)
        {
            if (_drag < 0) return;
            _drag = -1;
            this.ReleasePointer(e.pointerId);
            _ctx.Session.EndContinuous();
            MarkDirtyRepaint();
        }

        private void ShowMenu(int index)
        {
            var menu = new GenericMenu();
            foreach (AutomationInterpolation mode in Enum.GetValues(typeof(AutomationInterpolation)))
            {
                var m = mode;
                var cur = Clip.FindLane(_target).points[index].interpolation;
                menu.AddItem(new GUIContent(m.ToString()), cur == m, () => _ctx.Session.Modify("Change Interpolation", d =>
                {
                    var lane = d.FindClip(_clipUid).FindLane(_target);
                    var p = lane.points[index]; p.interpolation = m; lane.points[index] = p;
                }, StudioChange.Content, this));
            }
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Delete Point"), false, () => _ctx.Session.Modify("Delete Automation Point", d => d.FindClip(_clipUid).FindLane(_target).points.RemoveAt(index), StudioChange.Content, this));
            menu.ShowAsContext();
        }
    }
}
