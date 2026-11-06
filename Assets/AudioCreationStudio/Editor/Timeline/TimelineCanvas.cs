using System;
using System.Collections.Generic;
using AudioCreationStudio.DSP;
using AudioCreationStudio.Editing;
using AudioCreationStudio.Models;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AudioCreationStudio.EditorUI
{
    /// <summary>
    /// The multitrack arrangement surface. Draws lanes, grid, clips (real waveforms from the peak cache),
    /// fades, automation and the playhead, and implements selection, move (incl. cross-track and
    /// Alt-duplicate), trim, fade, snapping, automation editing and drag-and-drop import.
    /// Edit tools (<see cref="EditTool"/>): Selection, Trim (slip / slide / ripple / roll), Blade and Dynamic Trim.
    /// </summary>
    public sealed class TimelineCanvas : VisualElement
    {
        private enum Hit { None, Empty, Lane, Body, TrimLeft, TrimRight, FadeIn, FadeOut, AutoLane, AutoPoint, Slip, Slide, Roll, RippleLeft, RippleRight, Blade }
        private enum Drag { None, Move, TrimLeft, TrimRight, FadeIn, FadeOut, AutoPoint, Scrub, Slip, Slide, Roll, RippleLeft, RippleRight }

        private const float ClipHeaderH = 17f, ClipPadTop = 4f, ClipPadBottom = 5f, EdgePx = 6f, FadeHandleR = 5f, RollPx = 3f;

        private readonly StudioContext _ctx;
        private readonly QuadBatch _quads = new QuadBatch();
        private readonly VisualElement _labelLayer;
        private readonly List<Label> _labels = new List<Label>();
        private readonly SnapResolver _snap = new SnapResolver();
        private readonly List<Vector2> _pts = new List<Vector2>(256);

        // Interaction state
        private Drag _drag;
        private string _hoverClip;
        private Hit _hoverHit;
        private string _dragClip;
        private Vector2 _dragStartPos;
        private long _dragStartSample;
        private int _dragStartTrack;
        private readonly Dictionary<string, long> _origStarts = new Dictionary<string, long>();
        private readonly Dictionary<string, int> _origTracks = new Dictionary<string, int>();
        private bool _dragMoved;
        private bool _altDuplicate;
        private long? _snapLine;
        private string _autoTrack;
        private int _autoPointIndex = -1;
        private int _hoverAutoTrack = -1, _hoverAutoPoint = -1;
        private Vector2 _dropPos;
        private bool _dropActive;
        private int _dropCount;

        // Trim tools
        private string _hitPartner, _hoverPartner;
        private string _dragPartner, _dragTrack;
        private readonly Dictionary<string, ClipData> _snapshot = new Dictionary<string, ClipData>();
        private long _bladeSample = -1;
        private bool _bladeAll;
        private readonly Label _info;

        private StudioSession S => _ctx.Session;
        private TimelineViewState V => _ctx.Session.View;
        private RecipeData R => _ctx.Session.Data;
        private int SR => R.sampleRate;

        public event Action ViewChanged;

        public TimelineCanvas(StudioContext ctx)
        {
            _ctx = ctx;
            AddToClassList("acs-canvas");
            focusable = true;
            generateVisualContent += Draw;
            _labelLayer = new VisualElement { pickingMode = PickingMode.Ignore };
            _labelLayer.AddToClassList("acs-canvas__labels");
            Add(_labelLayer);
            _info = new Label { pickingMode = PickingMode.Ignore };
            _info.AddToClassList("acs-draginfo");
            _info.style.display = DisplayStyle.None;
            Add(_info);

            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
            RegisterCallback<PointerCaptureOutEvent>(_ => FinishDrag());
            RegisterCallback<PointerLeaveEvent>(_ => { if (_hoverClip != null || _hoverAutoPoint >= 0) { _hoverClip = null; _hoverAutoPoint = -1; Refresh(); } });
            RegisterCallback<WheelEvent>(OnWheel);
            RegisterCallback<DragUpdatedEvent>(OnDragUpdated);
            RegisterCallback<DragPerformEvent>(OnDragPerform);
            RegisterCallback<DragLeaveEvent>(_ => { _dropActive = false; Refresh(); });
            RegisterCallback<DragExitedEvent>(_ => { _dropActive = false; Refresh(); });
            RegisterCallback<GeometryChangedEvent>(_ => Refresh());
            _ctx.Waveforms.Updated += Refresh;
            RegisterCallback<DetachFromPanelEvent>(_ => _ctx.Waveforms.Updated -= Refresh);
        }

        public void Refresh()
        {
            MarkDirtyRepaint();
            LayoutLabels();
        }

        // ------------------------------------------------------------------ Drawing

        private Color TrackColor(TrackData t) => t.color.a <= 0 ? StudioTheme.Accent : new Color(t.color.r, t.color.g, t.color.b, 1f);

        private Rect ClipRect(ClipData c, float rowTop)
        {
            float x0 = V.X(c.startSample, SR), x1 = V.X(c.EndSample, SR);
            return new Rect(x0, rowTop + ClipPadTop, Mathf.Max(2f, x1 - x0), TimelineViewState.TrackHeight - ClipPadTop - ClipPadBottom);
        }

        private void Draw(MeshGenerationContext mgc)
        {
            var r = contentRect;
            if (r.width < 1 || r.height < 1) return;
            var p = mgc.painter2D;
            var data = R;
            float ppp = UnityEditor.EditorGUIUtility.pixelsPerPoint;

            // Lanes and grid
            float y = -V.scrollY;
            for (int i = 0; i < data.tracks.Count; i++)
            {
                var t = data.tracks[i];
                float h = TimelineViewState.RowHeight(t);
                bool sel = t.uid == S.SelectedTrack;
                _quads.Rect(0, y, r.width, TimelineViewState.TrackHeight, sel ? StudioTheme.Mix(StudioTheme.LaneB, TrackColor(t), 0.05f) : (i % 2 == 0 ? StudioTheme.LaneA : StudioTheme.LaneB));
                if (t.showAutomation) _quads.Rect(0, y + TimelineViewState.TrackHeight, r.width, TimelineViewState.AutomationHeight, StudioTheme.Inset);
                _quads.Rect(0, y + h - 1, r.width, 1, StudioTheme.Border.WithAlpha(0.6f));
                y += h;
            }
            if (y < r.height) _quads.Rect(0, Mathf.Max(0, y), r.width, r.height - Mathf.Max(0, y), StudioTheme.Background);

            V.GridSteps(out double major, out double minor);
            double t0 = V.SecondsAtX(0), t1 = V.SecondsAtX(r.width);
            for (double s = Math.Floor(t0 / minor) * minor; s <= t1; s += minor)
            {
                if (s < 0) continue;
                float x = (float)((s - V.scrollSeconds) * V.pixelsPerSecond);
                bool isMajor = Math.Abs(s / major - Math.Round(s / major)) < 1e-6;
                _quads.Rect(Mathf.Round(x * ppp) / ppp, 0, 1f / ppp, Mathf.Min(r.height, Mathf.Max(y, 0)), isMajor ? StudioTheme.GridMajor : StudioTheme.GridMinor);
            }

            // Loop region
            if (data.loopRegion.enabled && data.loopRegion.Length > 0)
            {
                float lx0 = V.X(data.loopRegion.start, SR), lx1 = V.X(data.loopRegion.end, SR);
                _quads.Rect(lx0, 0, lx1 - lx0, r.height, StudioTheme.Accent.WithAlpha(0.06f));
                _quads.Rect(lx0, 0, 1, r.height, StudioTheme.Accent.WithAlpha(0.5f));
                _quads.Rect(lx1 - 1, 0, 1, r.height, StudioTheme.Accent.WithAlpha(0.5f));
            }
            _quads.Flush(mgc);

            // Clips
            y = -V.scrollY;
            for (int i = 0; i < data.tracks.Count; i++)
            {
                var t = data.tracks[i];
                float h = TimelineViewState.RowHeight(t);
                if (y + h >= 0 && y <= r.height)
                {
                    if (_drag == Drag.Slip && _dragTrack == t.uid) DrawSlipGhost(mgc, p, y, r, ppp);
                    foreach (var c in t.clips) DrawClip(mgc, p, t, c, y, r, ppp);
                    if (t.showAutomation) DrawAutomationLane(p, t, i, y + TimelineViewState.TrackHeight, r);
                }
                y += h;
            }

            // Drop preview
            if (_dropActive)
            {
                int ti = V.TrackIndexAt(data, _dropPos.y, out _);
                float rowTop = ti >= 0 && ti < data.tracks.Count ? V.RowTop(data, ti) : Mathf.Max(0, V.RowTop(data, data.tracks.Count));
                var dr = new Rect(_dropPos.x, rowTop + ClipPadTop, 120, TimelineViewState.TrackHeight - ClipPadTop - ClipPadBottom);
                PaintUtil.FillRounded(p, dr, 6, StudioTheme.Accent.WithAlpha(0.18f));
                PaintUtil.StrokeRounded(p, dr, 6, StudioTheme.Accent, 1.5f);
            }

            // Blade cut preview
            if (S.Tool == EditTool.Blade && _drag == Drag.None && _bladeSample >= 0 && _hoverClip != null)
            {
                float bx = V.X(_bladeSample, SR);
                var hc = R.FindClip(_hoverClip, out var owner);
                if (_bladeAll || hc == null) _quads.Rect(bx - 0.75f, 0, 1.5f, Mathf.Min(r.height, Mathf.Max(0, y)), StudioTheme.Warning);
                else
                {
                    var cr = ClipRect(hc, V.RowTop(data, data.tracks.IndexOf(owner)));
                    _quads.Rect(bx - 0.75f, cr.y - 2, 1.5f, cr.height + 4, StudioTheme.Warning);
                }
                _quads.Flush(mgc);
            }

            // Snap indicator
            if (_snapLine.HasValue)
            {
                float sx = V.X(_snapLine.Value, SR);
                for (float dy = 0; dy < r.height; dy += 8) _quads.Rect(sx - 0.5f, dy, 1f, 4f, StudioTheme.Warning);
                _quads.Flush(mgc);
            }

            // Playhead
            float px = V.X(S.Playhead, SR);
            if (px >= -2 && px <= r.width + 2)
            {
                _quads.Rect(Mathf.Round(px * ppp) / ppp - 0.5f, 0, 1.5f, r.height, StudioTheme.Playhead);
                _quads.Flush(mgc);
            }
        }

        private void DrawClip(MeshGenerationContext mgc, Painter2D p, TrackData t, ClipData c, float rowTop, Rect view, float ppp)
        {
            var cr = ClipRect(c, rowTop);
            if (cr.xMax < 0 || cr.x > view.width) return;
            var tc = TrackColor(t);
            bool selected = S.IsClipSelected(c.uid);
            bool hover = c.uid == _hoverClip;
            bool enabled = c.enabled;
            var peaks = _ctx.PeaksFor(c, out bool missing);
            Color bodyColor = missing ? StudioTheme.Error : tc;
            if (!enabled) bodyColor = StudioTheme.TextFaint;

            PaintUtil.FillRounded(p, cr, 6, StudioTheme.Mix(StudioTheme.Surface, bodyColor, selected ? 0.26f : 0.16f));
            var header = new Rect(cr.x, cr.y, cr.width, ClipHeaderH);
            p.fillColor = StudioTheme.Mix(StudioTheme.Surface, bodyColor, selected ? 0.75f : 0.55f);
            PaintUtil.RoundedRectPath(p, new Rect(header.x, header.y, header.width, header.height + 6), 6);
            p.Fill();
            p.fillColor = StudioTheme.Mix(StudioTheme.Surface, bodyColor, selected ? 0.26f : 0.16f);
            p.BeginPath();
            p.MoveTo(new Vector2(cr.x, header.yMax)); p.LineTo(new Vector2(cr.xMax, header.yMax));
            p.LineTo(new Vector2(cr.xMax, header.yMax + 6)); p.LineTo(new Vector2(cr.x, header.yMax + 6)); p.ClosePath();
            p.Fill();

            // Waveform
            var wr = new Rect(cr.x + 1, header.yMax + 2, cr.width - 2, cr.height - ClipHeaderH - 4);
            if (peaks != null && wr.width > 1)
                DrawClipWaveform(c, peaks, wr, view, ppp, enabled ? StudioTheme.Lighten(bodyColor, 0.15f) : StudioTheme.TextFaint, selected);
            else if (!missing)
                _quads.Rect(Mathf.Max(wr.x, 0), wr.center.y, Mathf.Min(wr.width, view.width), 1, bodyColor.WithAlpha(0.4f));
            _quads.Flush(mgc);

            // Fades
            DrawFade(p, c, cr, header.yMax, true, selected || hover);
            DrawFade(p, c, cr, header.yMax, false, selected || hover);

            // Border / selection
            if (selected) PaintUtil.StrokeRounded(p, cr, 6, StudioTheme.Selection.WithAlpha(0.92f), 1.6f);
            else PaintUtil.StrokeRounded(p, cr, 6, bodyColor.WithAlpha(hover ? 0.9f : 0.45f), 1f);

            // Trim handle hint
            if (hover && (_hoverHit == Hit.TrimLeft || _hoverHit == Hit.TrimRight))
            {
                float hx = _hoverHit == Hit.TrimLeft ? cr.x + 1 : cr.xMax - 4;
                PaintUtil.FillRounded(p, new Rect(hx, cr.y + 4, 3, cr.height - 8), 1.5f, StudioTheme.Text);
            }

            // Trim-tool edges: ripple (one side, warning colour) and roll (both sides of the cut, accent colour)
            bool active = _drag == Drag.None ? hover || c.uid == _hoverPartner : c.uid == _dragClip || c.uid == _dragPartner;
            Hit edge = _drag switch
            {
                Drag.RippleLeft => Hit.RippleLeft, Drag.RippleRight => Hit.RippleRight, Drag.Roll => Hit.Roll,
                Drag.None => _hoverHit, _ => Hit.None
            };
            string rollLeft = _drag == Drag.Roll ? _dragClip : _hoverClip;
            if (active && (edge == Hit.RippleLeft || edge == Hit.RippleRight))
                PaintUtil.FillRounded(p, new Rect(edge == Hit.RippleLeft ? cr.x + 1 : cr.xMax - 4, cr.y + 3, 3, cr.height - 6), 1.5f, StudioTheme.Warning);
            else if (active && edge == Hit.Roll)
                PaintUtil.FillRounded(p, new Rect(c.uid == rollLeft ? cr.xMax - 4 : cr.x + 1, cr.y + 3, 3, cr.height - 6), 1.5f, StudioTheme.Accent);
        }

        /// <summary>While slipping, shows the whole source behind the clip so you can see which part of the audio is in the cut.</summary>
        private void DrawSlipGhost(MeshGenerationContext mgc, Painter2D p, float rowTop, Rect view, float ppp)
        {
            var c = R.FindClip(_dragClip);
            if (c == null || c.loop || c.sourceFrames <= 0) return;
            double step = c.SourceStep(SR);
            var ghost = c.Clone(false);
            ghost.startSample = c.startSample - (long)Math.Round(c.sourceOffset / step);
            ghost.sourceOffset = 0;
            ghost.lengthSamples = Math.Max(1, (long)Math.Floor(c.sourceFrames / step));
            ghost.fadeIn = new FadeData();
            ghost.fadeOut = new FadeData();
            ghost.automation = new List<AutomationLane>();
            var gr = ClipRect(ghost, rowTop);
            PaintUtil.FillRounded(p, gr, 6, StudioTheme.Surface.WithAlpha(0.6f));
            var peaks = _ctx.PeaksFor(c, out _);
            if (peaks != null)
            {
                var wr = new Rect(gr.x + 1, gr.y + ClipHeaderH + 2, gr.width - 2, gr.height - ClipHeaderH - 4);
                DrawClipWaveform(ghost, peaks, wr, view, ppp, StudioTheme.TextFaint, false);
                _quads.Flush(mgc);
            }
            PaintUtil.StrokeRounded(p, gr, 6, StudioTheme.TextFaint.WithAlpha(0.8f), 1f);
        }

        private void DrawClipWaveform(ClipData c, WaveformPeaks peaks, Rect wr, Rect view, float ppp, Color color, bool selected)
        {
            float mid = wr.center.y, half = wr.height * 0.5f;
            float xa = Mathf.Max(wr.x, 0), xb = Mathf.Min(wr.xMax, view.width);
            float step = 1f / ppp;
            double srcStep = c.playbackRate * peaks.SampleRate / SR;
            double spp = V.SamplesPerPixel(SR) * step;
            float clipGain = DspMath.DbToGain(c.gainDb);
            var gainLane = c.FindLane(AutomationTargets.Gain);
            var rmsColor = StudioTheme.Lighten(color, 0.45f);
            var peakColor = color.WithAlpha(selected ? 0.95f : 0.8f);
            int srcLen = peaks.Frames;
            _quads.Rect(xa, mid, xb - xa, 1f / ppp, color.WithAlpha(0.25f));
            for (float x = xa; x < xb; x += step)
            {
                double tl = V.SecondsAtX(x) * SR - c.startSample;
                if (tl < 0) continue;
                double s0 = c.sourceOffset + tl * srcStep;
                double s1 = s0 + Math.Max(1.0, spp * srcStep);
                if (c.loop && srcLen > 0) { s0 = DspMath.Mod(s0, srcLen); s1 = s0 + Math.Max(1.0, spp * srcStep); }
                if (s0 >= srcLen) break;
                peaks.Query(s0, Math.Min(s1, srcLen), out float mn, out float mx, out float rms);
                long local = (long)tl;
                float g = (gainLane != null && gainLane.IsActive ? DspMath.DbToGain(gainLane.Evaluate(local, c.gainDb)) : clipGain) * FadeCurves.ClipFadeGain(c, local);
                float top = mx * g, bot = mn * g;
                bool clip = top >= 1f || bot <= -1f;
                float yt = mid - Mathf.Clamp(top, -1, 1) * half, yb = mid - Mathf.Clamp(bot, -1, 1) * half;
                _quads.Rect(x, yt, step, Mathf.Max(step, yb - yt), peakColor);
                float rh = Mathf.Min(rms * g, 1f) * half;
                if (rh > 0.5f) _quads.Rect(x, mid - rh, step, rh * 2, rmsColor.WithAlpha(0.55f));
                if (clip) { _quads.Rect(x, wr.y, step, 2, StudioTheme.Error); _quads.Rect(x, wr.yMax - 2, step, 2, StudioTheme.Error); }
            }
        }

        private void DrawFade(Painter2D p, ClipData c, Rect cr, float bodyTop, bool fadeIn, bool showHandle)
        {
            var fd = fadeIn ? c.fadeIn : c.fadeOut;
            float len = (float)(fd.lengthSamples / (double)SR * V.pixelsPerSecond);
            float edge = fadeIn ? cr.x : cr.xMax;
            float end = fadeIn ? edge + len : edge - len;
            float top = bodyTop, bottom = cr.yMax - 2;
            if (len >= 1f)
            {
                _pts.Clear();
                const int n = 24;
                for (int i = 0; i <= n; i++)
                {
                    float u = i / (float)n;
                    float g = FadeCurves.Evaluate(fd.curve, u);
                    float x = fadeIn ? edge + u * len : edge - u * len;
                    _pts.Add(new Vector2(x, bottom - g * (bottom - top)));
                }
                // Shade the attenuated area above the curve.
                p.fillColor = new Color(0, 0, 0, 0.32f);
                p.BeginPath();
                p.MoveTo(new Vector2(edge, top));
                foreach (var pt in _pts) p.LineTo(pt);
                p.LineTo(new Vector2(end, top));
                p.ClosePath();
                p.Fill();
                PaintUtil.Polyline(p, _pts, StudioTheme.Text.WithAlpha(0.85f), 1.3f);
            }
            if (showHandle)
            {
                float hx = Mathf.Clamp(end, cr.x + FadeHandleR, cr.xMax - FadeHandleR);
                bool hot = (fadeIn && _hoverHit == Hit.FadeIn || !fadeIn && _hoverHit == Hit.FadeOut) && _hoverClip == c.uid;
                PaintUtil.Circle(p, new Vector2(hx, top), hot ? FadeHandleR + 1.5f : FadeHandleR, StudioTheme.Text);
                PaintUtil.Circle(p, new Vector2(hx, top), FadeHandleR - 2.2f, StudioTheme.Background);
            }
        }

        private void DrawAutomationLane(Painter2D p, TrackData t, int trackIndex, float laneTop, Rect view)
        {
            var infos = AutomationTargetInfo.ForTrack(t);
            var info = AutomationTargetInfo.Find(infos, t.automationTarget);
            if (info == null) return;
            var lane = t.FindLane(info.TargetId);
            var lr = new Rect(0, laneTop + 8, view.width, TimelineViewState.AutomationHeight - 16);
            var color = TrackColor(t);
            float YFor(float v) => lr.yMax - info.Range.ToNorm(v) * lr.height;

            if (lane == null || lane.points.Count == 0)
            {
                float yy = YFor(info.StaticValue);
                for (float x = 0; x < view.width; x += 10) _quads.Rect(x, yy, 5, 1, color.WithAlpha(0.5f));
                return;
            }
            _pts.Clear();
            var pts = lane.points;
            float first = V.X(pts[0].time, SR);
            _pts.Add(new Vector2(Mathf.Min(0, first), YFor(pts[0].value)));
            for (int i = 0; i < pts.Count; i++)
            {
                var a = pts[i];
                float xa = V.X(a.time, SR);
                _pts.Add(new Vector2(xa, YFor(a.value)));
                if (i + 1 < pts.Count)
                {
                    var b = pts[i + 1];
                    float xb = V.X(b.time, SR);
                    if (xb < 0 || xa > view.width) { continue; }
                    if (a.interpolation == AutomationInterpolation.Step) _pts.Add(new Vector2(xb, YFor(a.value)));
                    else if (a.interpolation == AutomationInterpolation.Smooth)
                    {
                        for (int k = 1; k < 16; k++)
                        {
                            long tt = a.time + (b.time - a.time) * k / 16;
                            _pts.Add(new Vector2(V.X(tt, SR), YFor(lane.Evaluate(tt, info.StaticValue))));
                        }
                    }
                }
            }
            _pts.Add(new Vector2(Mathf.Max(view.width, V.X(pts[pts.Count - 1].time, SR)), YFor(pts[pts.Count - 1].value)));

            // Area under the curve
            p.fillColor = color.WithAlpha(0.12f);
            p.BeginPath();
            p.MoveTo(new Vector2(_pts[0].x, lr.yMax));
            foreach (var pt in _pts) p.LineTo(pt);
            p.LineTo(new Vector2(_pts[_pts.Count - 1].x, lr.yMax));
            p.ClosePath();
            p.Fill();
            PaintUtil.Polyline(p, _pts, lane.enabled ? color : StudioTheme.TextFaint, 1.6f);

            for (int i = 0; i < pts.Count; i++)
            {
                var c = new Vector2(V.X(pts[i].time, SR), YFor(pts[i].value));
                if (c.x < -6 || c.x > view.width + 6) continue;
                bool hot = (_hoverAutoTrack == trackIndex && _hoverAutoPoint == i) || (_drag == Drag.AutoPoint && _autoTrack == t.uid && _autoPointIndex == i);
                PaintUtil.Circle(p, c, hot ? 5.5f : 4f, hot ? StudioTheme.Text : color);
                PaintUtil.Circle(p, c, hot ? 2.5f : 1.8f, StudioTheme.Background);
            }
        }

        private void LayoutLabels()
        {
            var data = R;
            int used = 0;
            float y = -V.scrollY;
            float width = contentRect.width;
            for (int i = 0; i < data.tracks.Count; i++)
            {
                var t = data.tracks[i];
                float h = TimelineViewState.RowHeight(t);
                if (y + h >= 0 && y <= contentRect.height)
                {
                    foreach (var c in t.clips)
                    {
                        var cr = ClipRect(c, y);
                        if (cr.xMax < 0 || cr.x > width || cr.width < 24) continue;
                        float x0 = Mathf.Max(cr.x, 0) + 7;
                        float w = cr.xMax - x0 - 6;
                        if (w < 14) continue;
                        var l = GetLabel(used++);
                        bool missing = c.sourceKind == ClipSourceKind.AudioClip && c.sourceClip == null;
                        string extra = c.gainDb != 0 ? $"  {c.gainDb:+0.0;-0.0} dB" : "";
                        if (Math.Abs(c.playbackRate - 1.0) > 1e-4) extra += $"  ×{c.playbackRate:0.00}";
                        l.text = (missing ? "⚠ Missing source" : c.DisplayName) + extra;
                        l.style.left = x0;
                        l.style.top = cr.y + 1;
                        l.style.width = w;
                        l.EnableInClassList("acs-cliplabel--disabled", !c.enabled);
                    }
                    if (t.showAutomation)
                    {
                        var info = AutomationTargetInfo.Find(AutomationTargetInfo.ForTrack(t), t.automationTarget);
                        var l = GetLabel(used++);
                        var lane = info != null ? t.FindLane(info.TargetId) : null;
                        l.text = info == null ? "" : info.Name + (lane == null || lane.points.Count == 0 ? "   ·   click to add points" : "");
                        l.style.left = 8;
                        l.style.top = y + TimelineViewState.TrackHeight + 2;
                        l.style.width = 360;
                        l.EnableInClassList("acs-cliplabel--disabled", true);
                    }
                }
                y += h;
            }
            for (int i = used; i < _labels.Count; i++) _labels[i].style.display = DisplayStyle.None;
        }

        private Label GetLabel(int i)
        {
            while (_labels.Count <= i)
            {
                var l = new Label { pickingMode = PickingMode.Ignore };
                l.AddToClassList("acs-cliplabel");
                _labelLayer.Add(l);
                _labels.Add(l);
            }
            var lab = _labels[i];
            lab.style.display = DisplayStyle.Flex;
            return lab;
        }

        // ------------------------------------------------------------------ Hit testing

        private Hit HitTest(Vector2 pos, out ClipData clip, out TrackData track, out int pointIndex)
        {
            clip = null; track = null; pointIndex = -1;
            _hitPartner = null;
            var data = R;
            int ti = V.TrackIndexAt(data, pos.y, out bool inAuto);
            if (ti < 0 || ti >= data.tracks.Count) return Hit.Empty;
            track = data.tracks[ti];
            float rowTop = V.RowTop(data, ti);
            if (inAuto)
            {
                pointIndex = FindAutoPoint(track, rowTop + TimelineViewState.TrackHeight, pos);
                return pointIndex >= 0 ? Hit.AutoPoint : Hit.AutoLane;
            }
            // Topmost (last drawn) clip wins.
            for (int i = track.clips.Count - 1; i >= 0; i--)
            {
                var c = track.clips[i];
                var cr = ClipRect(c, rowTop);
                if (!cr.Contains(pos)) continue;
                clip = c;
                var tool = S.Tool;
                if (tool == EditTool.Blade) return Hit.Blade;
                if (tool == EditTool.Trim || tool == EditTool.DynamicTrim) return TrimToolHit(track, c, cr, pos, ref clip);
                float bodyTop = cr.y + ClipHeaderH;
                float fin = cr.x + (float)(c.fadeIn.lengthSamples / (double)SR * V.pixelsPerSecond);
                float fout = cr.xMax - (float)(c.fadeOut.lengthSamples / (double)SR * V.pixelsPerSecond);
                if (Vector2.Distance(pos, new Vector2(Mathf.Clamp(fin, cr.x + FadeHandleR, cr.xMax - FadeHandleR), bodyTop)) <= FadeHandleR + 3) return Hit.FadeIn;
                if (Vector2.Distance(pos, new Vector2(Mathf.Clamp(fout, cr.x + FadeHandleR, cr.xMax - FadeHandleR), bodyTop)) <= FadeHandleR + 3) return Hit.FadeOut;
                if (cr.width > EdgePx * 3)
                {
                    if (pos.x - cr.x <= EdgePx) return Hit.TrimLeft;
                    if (cr.xMax - pos.x <= EdgePx) return Hit.TrimRight;
                }
                return Hit.Body;
            }
            return Hit.Lane;
        }

        /// <summary>Trim / Dynamic Trim: edge → ripple, cut between touching clips → roll, name bar → slide, body → slip.</summary>
        private Hit TrimToolHit(TrackData track, ClipData c, Rect cr, Vector2 pos, ref ClipData clip)
        {
            if (cr.width > EdgePx * 3)
            {
                if (cr.xMax - pos.x <= EdgePx)
                {
                    var rn = TimelineEditing.RightNeighbor(track, c);
                    if (rn != null && cr.xMax - pos.x <= RollPx) { _hitPartner = rn.uid; return Hit.Roll; }
                    return Hit.RippleRight;
                }
                if (pos.x - cr.x <= EdgePx)
                {
                    var ln = TimelineEditing.LeftNeighbor(track, c);
                    if (ln != null && pos.x - cr.x <= RollPx) { clip = ln; _hitPartner = c.uid; return Hit.Roll; }
                    return Hit.RippleLeft;
                }
            }
            return pos.y < cr.y + ClipHeaderH ? Hit.Slide : Hit.Slip;
        }

        private int FindAutoPoint(TrackData t, float laneTop, Vector2 pos)
        {
            var info = AutomationTargetInfo.Find(AutomationTargetInfo.ForTrack(t), t.automationTarget);
            var lane = info != null ? t.FindLane(info.TargetId) : null;
            if (lane == null) return -1;
            var lr = new Rect(0, laneTop + 8, contentRect.width, TimelineViewState.AutomationHeight - 16);
            for (int i = 0; i < lane.points.Count; i++)
            {
                var c = new Vector2(V.X(lane.points[i].time, SR), lr.yMax - info.Range.ToNorm(lane.points[i].value) * lr.height);
                if (Vector2.Distance(c, pos) <= 7) return i;
            }
            return -1;
        }

        private float LaneValueAt(TrackData t, AutomationTargetInfo info, float laneTop, float y)
        {
            var lr = new Rect(0, laneTop + 8, contentRect.width, TimelineViewState.AutomationHeight - 16);
            return info.Range.FromNorm((lr.yMax - y) / lr.height);
        }

        // ------------------------------------------------------------------ Pointer handling

        private void OnPointerDown(PointerDownEvent e)
        {
            Focus();
            var pos = (Vector2)e.localPosition;
            var hit = HitTest(pos, out var clip, out var track, out int pointIndex);
            bool additive = e.shiftKey || e.actionKey;

            if (e.button == 1)
            {
                ShowContextMenu(hit, clip, track, pointIndex, pos);
                e.StopPropagation();
                return;
            }
            if (e.button != 0) return;

            _dragStartPos = pos;
            _dragStartSample = V.SampleAtX(pos.x, SR);
            _dragMoved = false;
            _snap.Build(R, V, S.Playhead, null, S.Snap && !e.ctrlKey);

            switch (hit)
            {
                case Hit.Body:
                    if (e.clickCount == 2) { _ctx.Transport.PlayClip(clip.uid); break; }
                    if (additive) S.SelectClip(clip.uid, true);
                    else if (!S.IsClipSelected(clip.uid)) S.SelectClip(clip.uid, false);
                    else S.SelectTrack(track.uid, false);
                    if (!S.IsClipSelected(clip.uid)) break;
                    BeginMove(clip, e.altKey);
                    break;
                case Hit.TrimLeft:
                case Hit.TrimRight:
                case Hit.FadeIn:
                case Hit.FadeOut:
                    if (!S.IsClipSelected(clip.uid)) S.SelectClip(clip.uid, false);
                    _dragClip = clip.uid;
                    _drag = hit == Hit.TrimLeft ? Drag.TrimLeft : hit == Hit.TrimRight ? Drag.TrimRight : hit == Hit.FadeIn ? Drag.FadeIn : Drag.FadeOut;
                    var exclude = new HashSet<string> { clip.uid };
                    _snap.Build(R, V, S.Playhead, exclude, S.Snap && !e.ctrlKey);
                    S.BeginContinuous(_drag == Drag.TrimLeft || _drag == Drag.TrimRight ? "Trim Clip" : "Adjust Fade");
                    break;
                case Hit.Blade:
                {
                    long cut = _snap.Snap(V.SampleAtX(pos.x, SR), out _);
                    bool all = e.shiftKey;
                    string tuid = track.uid;
                    int made = 0;
                    S.Modify(all ? "Blade All Tracks" : "Blade", r =>
                    {
                        IEnumerable<TrackData> tracks = all ? (IEnumerable<TrackData>)r.tracks : new[] { r.FindTrack(tuid) };
                        made = TimelineEditing.SplitAll(tracks, cut, r.sampleRate).Count;
                    }, StudioChange.Structure);
                    if (made == 0) _ctx.Toast?.Invoke("Too close to the clip edge to cut", false);
                    break;
                }
                case Hit.Slip:
                case Hit.Slide:
                case Hit.Roll:
                case Hit.RippleLeft:
                case Hit.RippleRight:
                    if (e.clickCount == 2) { _ctx.Transport.PlayClip(clip.uid); break; }
                    BeginTrimTool(hit, clip, track, e.ctrlKey);
                    break;
                case Hit.AutoPoint:
                    if (e.altKey || e.clickCount == 2) { DeleteAutoPoint(track, pointIndex); break; }
                    _autoTrack = track.uid;
                    _autoPointIndex = pointIndex;
                    _drag = Drag.AutoPoint;
                    S.BeginContinuous("Move Automation Point");
                    break;
                case Hit.AutoLane:
                    AddAutoPoint(track, pos);
                    break;
                default:
                    if (track != null) S.SelectTrack(track.uid);
                    else S.ClearSelection();
                    long at = _snap.Snap(V.SampleAtX(pos.x, SR), out _);
                    _ctx.Transport.Seek(at);
                    _drag = Drag.Scrub;
                    break;
            }
            if (_drag != Drag.None) this.CapturePointer(e.pointerId);
            e.StopPropagation();
            Refresh();
        }

        private void BeginMove(ClipData primary, bool altDuplicate)
        {
            _drag = Drag.Move;
            _dragClip = primary.uid;
            _altDuplicate = altDuplicate;
            _origStarts.Clear();
            _origTracks.Clear();
            S.BeginContinuous(altDuplicate ? "Duplicate Clips" : "Move Clips");
            if (altDuplicate)
            {
                var sel = new List<string>(S.SelectedClips);
                var copies = new List<string>();
                S.Continuous(r =>
                {
                    foreach (var u in sel)
                    {
                        var c = r.FindClip(u, out var owner);
                        if (c == null) continue;
                        var copy = c.Clone(true);
                        owner.clips.Add(copy);
                        copies.Add(copy.uid);
                        if (u == primary.uid) _dragClip = copy.uid;
                    }
                }, StudioChange.Structure);
                S.SelectClip(null, false);
                foreach (var u in copies) S.SelectClip(u, true);
            }
            foreach (var u in S.SelectedClips)
            {
                var c = R.FindClip(u, out var owner);
                if (c == null) continue;
                _origStarts[u] = c.startSample;
                _origTracks[u] = R.tracks.IndexOf(owner);
            }
            _dragStartTrack = _origTracks.TryGetValue(_dragClip, out var ti) ? ti : 0;
            _snap.Build(R, V, S.Playhead, new HashSet<string>(S.SelectedClips), _snap.Enabled);
        }

        private void BeginTrimTool(Hit hit, ClipData clip, TrackData track, bool noSnap)
        {
            if (!S.IsClipSelected(clip.uid)) S.SelectClip(clip.uid, false);
            _drag = hit == Hit.Slip ? Drag.Slip : hit == Hit.Slide ? Drag.Slide : hit == Hit.Roll ? Drag.Roll : hit == Hit.RippleLeft ? Drag.RippleLeft : Drag.RippleRight;
            _dragClip = clip.uid;
            _dragPartner = hit == Hit.Roll ? _hitPartner : null;
            _dragTrack = track.uid;
            _snapshot.Clear();
            var exclude = new HashSet<string>();
            foreach (var c in track.clips) { _snapshot[c.uid] = c.Clone(false); exclude.Add(c.uid); }
            _snap.Build(R, V, S.Playhead, exclude, S.Snap && !noSnap);
            S.BeginContinuous(_drag == Drag.Slip ? "Slip Clip" : _drag == Drag.Slide ? "Slide Clip" : _drag == Drag.Roll ? "Roll Edit" : "Ripple Trim");
        }

        private void OnPointerMove(PointerMoveEvent e)
        {
            var pos = (Vector2)e.localPosition;
            if (_drag == Drag.None)
            {
                var hit = HitTest(pos, out var clip, out var track, out int pi);
                string hc = clip?.uid;
                int hat = hit == Hit.AutoPoint ? R.tracks.IndexOf(track) : -1;
                long blade = -1;
                if (hit == Hit.Blade)
                {
                    _snap.Build(R, V, S.Playhead, null, S.Snap && !e.ctrlKey);
                    blade = _snap.Snap(V.SampleAtX(pos.x, SR), out _);
                }
                if (hc != _hoverClip || hit != _hoverHit || hat != _hoverAutoTrack || pi != _hoverAutoPoint || _hitPartner != _hoverPartner || blade != _bladeSample || e.shiftKey != _bladeAll)
                {
                    _hoverClip = hc; _hoverHit = hit; _hoverAutoTrack = hat; _hoverAutoPoint = pi; _hoverPartner = _hitPartner;
                    _bladeSample = blade; _bladeAll = e.shiftKey;
                    EnableInClassList("acs-cursor-resize", hit == Hit.TrimLeft || hit == Hit.TrimRight || hit == Hit.RippleLeft || hit == Hit.RippleRight);
                    EnableInClassList("acs-cursor-move", hit == Hit.AutoPoint || hit == Hit.FadeIn || hit == Hit.FadeOut || hit == Hit.Slide);
                    EnableInClassList("acs-cursor-slip", hit == Hit.Slip);
                    EnableInClassList("acs-cursor-roll", hit == Hit.Roll);
                    EnableInClassList("acs-cursor-blade", hit == Hit.Blade);
                    Refresh();
                }
                return;
            }
            if (!this.HasPointerCapture(e.pointerId)) return;
            if (!_dragMoved && Vector2.Distance(pos, _dragStartPos) < 2f) return;
            _dragMoved = true;
            long sample = V.SampleAtX(pos.x, SR);
            _snapLine = null;

            switch (_drag)
            {
                case Drag.Scrub:
                    _ctx.Transport.Seek(Math.Max(0, sample));
                    break;
                case Drag.Move: DoMove(pos, sample); break;
                case Drag.TrimLeft:
                case Drag.TrimRight:
                {
                    long snapped = _snap.Snap(sample, out bool did);
                    if (did) _snapLine = snapped;
                    var d = _drag;
                    S.Continuous(r =>
                    {
                        var c = r.FindClip(_dragClip);
                        if (c == null) return;
                        if (d == Drag.TrimLeft) TimelineEditing.TrimStart(c, snapped, r.sampleRate);
                        else TimelineEditing.TrimEnd(c, snapped, r.sampleRate);
                    }, StudioChange.Content, this);
                    break;
                }
                case Drag.FadeIn:
                case Drag.FadeOut:
                {
                    var d = _drag;
                    S.Continuous(r =>
                    {
                        var c = r.FindClip(_dragClip);
                        if (c == null) return;
                        if (d == Drag.FadeIn) TimelineEditing.SetFadeIn(c, sample - c.startSample);
                        else TimelineEditing.SetFadeOut(c, c.EndSample - sample);
                    }, StudioChange.Content, this);
                    break;
                }
                case Drag.AutoPoint: MoveAutoPoint(pos, sample, e.shiftKey); break;
                case Drag.Slip:
                case Drag.Slide:
                case Drag.Roll:
                case Drag.RippleLeft:
                case Drag.RippleRight:
                    DoTrimTool(sample, pos);
                    break;
            }
            Refresh();
            e.StopPropagation();
        }

        private void DoMove(Vector2 pos, long sample)
        {
            long delta = sample - _dragStartSample;
            if (_origStarts.TryGetValue(_dragClip, out long primaryStart))
            {
                var pc = R.FindClip(_dragClip);
                long len = pc?.lengthSamples ?? 0;
                long cand = primaryStart + delta;
                long s1 = _snap.Snap(cand, out bool a);
                long s2 = _snap.Snap(cand + len, out bool b);
                if (a && (!b || Math.Abs(s1 - cand) <= Math.Abs(s2 - cand - len))) { delta = s1 - primaryStart; _snapLine = s1; }
                else if (b) { delta = s2 - len - primaryStart; _snapLine = s2; }
            }
            // Never move any selected clip before zero.
            foreach (var kv in _origStarts) if (kv.Value + delta < 0) delta = -kv.Value;

            int ti = V.TrackIndexAt(R, pos.y, out _);
            int trackDelta = Mathf.Clamp(ti, 0, Math.Max(0, R.tracks.Count - 1)) - _dragStartTrack;
            int minT = int.MaxValue, maxT = int.MinValue;
            foreach (var kv in _origTracks) { minT = Math.Min(minT, kv.Value); maxT = Math.Max(maxT, kv.Value); }
            trackDelta = Mathf.Clamp(trackDelta, -minT, R.tracks.Count - 1 - maxT);

            S.Continuous(r =>
            {
                foreach (var kv in _origStarts)
                {
                    var c = r.FindClip(kv.Key, out var owner);
                    if (c == null) continue;
                    c.startSample = kv.Value + delta;
                    int target = _origTracks[kv.Key] + trackDelta;
                    if (target >= 0 && target < r.tracks.Count && r.tracks[target] != owner)
                        TimelineEditing.MoveClipToTrack(r, c, r.tracks[target]);
                }
            }, trackDelta != 0 ? StudioChange.Structure : StudioChange.Content, this);
        }

        private long SnapTo(long s)
        {
            long v = _snap.Snap(s, out bool did);
            if (did) _snapLine = v;
            return v;
        }

        /// <summary>Re-applies the active trim-tool drag from the snapshot taken at mouse-down, so it is exact and reversible.</summary>
        private void DoTrimTool(long sample, Vector2 pos)
        {
            if (!_snapshot.TryGetValue(_dragClip, out var o)) return;
            long delta = sample - _dragStartSample;
            var d = _drag;
            long target = 0;
            switch (d)
            {
                case Drag.Slide:
                {
                    long cand = o.startSample + delta;
                    long s1 = _snap.Snap(cand, out bool a), s2 = _snap.Snap(cand + o.lengthSamples, out bool b);
                    if (a && (!b || Math.Abs(s1 - cand) <= Math.Abs(s2 - cand - o.lengthSamples))) { target = s1; _snapLine = s1; }
                    else if (b) { target = s2 - o.lengthSamples; _snapLine = s2; }
                    else target = cand;
                    break;
                }
                case Drag.Roll:
                case Drag.RippleRight: target = SnapTo(o.EndSample + delta); break;
                case Drag.RippleLeft: target = SnapTo(o.startSample + delta); break;
            }
            string info = null;
            S.Continuous(r =>
            {
                var t = r.FindTrack(_dragTrack);
                if (t == null) return;
                foreach (var c in t.clips) if (_snapshot.TryGetValue(c.uid, out var snap)) TimelineEditing.RestorePlacement(c, snap);
                var clip = r.FindClip(_dragClip);
                if (clip == null) return;
                int rate = r.sampleRate;
                switch (d)
                {
                    case Drag.Slip:
                    {
                        double step = clip.SourceStep(rate);
                        TimelineEditing.Slip(clip, o.sourceOffset - (long)Math.Round(delta * step), rate);
                        long moved = (long)Math.Round((o.sourceOffset - clip.sourceOffset) / step);
                        info = $"Slip {FormatDelta(moved)}   ·   in {FormatTime(clip.sourceOffset / (double)Math.Max(1, clip.sourceSampleRate))}";
                        break;
                    }
                    case Drag.Slide:
                        info = $"Slide {FormatDelta(TimelineEditing.Slide(t, clip, target, rate))}";
                        break;
                    case Drag.Roll:
                    {
                        var right = r.FindClip(_dragPartner);
                        if (right == null) return;
                        info = $"Roll {FormatDelta(TimelineEditing.Roll(clip, right, target, rate) - o.EndSample)}";
                        break;
                    }
                    case Drag.RippleLeft:
                        info = $"Ripple {FormatDelta(-TimelineEditing.RippleTrimStart(t, clip, target, rate))}";
                        break;
                    case Drag.RippleRight:
                        info = $"Ripple {FormatDelta(TimelineEditing.RippleTrimEnd(t, clip, target, rate))}";
                        break;
                }
            }, StudioChange.Content, this);
            ShowInfo(info, pos);
        }

        private void ShowInfo(string text, Vector2 pos)
        {
            if (string.IsNullOrEmpty(text)) { _info.style.display = DisplayStyle.None; return; }
            _info.text = text;
            _info.style.left = Mathf.Clamp(pos.x + 14, 0, Mathf.Max(0, contentRect.width - 220));
            _info.style.top = Mathf.Max(0, pos.y - 28);
            _info.style.display = DisplayStyle.Flex;
        }

        private string FormatDelta(long samples)
        {
            double sec = Math.Abs(samples / (double)SR);
            string sign = samples > 0 ? "+" : samples < 0 ? "−" : "±";
            return sec < 1 ? $"{sign}{sec * 1000:0.0} ms" : $"{sign}{sec:0.000} s";
        }

        private static string FormatTime(double sec) => $"{(int)(sec / 60):00}:{sec % 60:00.000}";

        private static bool IsTrimToolDrag(Drag d) => d == Drag.Slip || d == Drag.Slide || d == Drag.Roll || d == Drag.RippleLeft || d == Drag.RippleRight;

        /// <summary>Dynamic Trim: plays the timeline around an edit (the whole clip for slip/slide, ±1 s for cuts).</summary>
        private void Audition(Drag d, string clipUid)
        {
            var c = R.FindClip(clipUid);
            if (c == null) return;
            long pad = SR / 2;
            switch (d)
            {
                case Drag.Roll:
                case Drag.RippleRight: _ctx.Transport.PlayRange(c.EndSample - SR, c.EndSample + SR, "Dynamic Trim · cut"); break;
                case Drag.RippleLeft: _ctx.Transport.PlayRange(c.startSample - pad, c.startSample + SR + pad, "Dynamic Trim · head"); break;
                default: _ctx.Transport.PlayRange(c.startSample - pad, c.EndSample + pad, "Dynamic Trim · clip"); break;
            }
        }

        /// <summary>
        /// Keyboard nudge for the selected clips (, and .): moves them in Selection mode, slips their content in
        /// Trim / Dynamic Trim mode (Dynamic Trim also auditions the result). Returns false when nothing is selected.
        /// </summary>
        public bool Nudge(int direction, bool big)
        {
            var sel = new List<string>(S.SelectedClips);
            if (sel.Count == 0) return false;
            long amount = (long)Math.Round(SR * (big ? 0.1 : 0.01)) * Math.Sign(direction);
            var tool = S.Tool;
            bool slip = tool == EditTool.Trim || tool == EditTool.DynamicTrim;
            if (tool == EditTool.Blade) return false;
            S.Modify(slip ? "Slip Clips" : "Nudge Clips", r =>
            {
                foreach (var u in sel)
                {
                    var c = r.FindClip(u);
                    if (c == null) continue;
                    if (slip) TimelineEditing.Slip(c, c.sourceOffset - (long)Math.Round(amount * c.SourceStep(r.sampleRate)), r.sampleRate);
                    else c.startSample = Math.Max(0, c.startSample + amount);
                }
            });
            if (tool == EditTool.DynamicTrim) Audition(Drag.Slip, sel[sel.Count - 1]);
            return true;
        }

        private void OnPointerUp(PointerUpEvent e)
        {
            if (_drag == Drag.None) return;
            this.ReleasePointer(e.pointerId);
            FinishDrag();
            e.StopPropagation();
        }

        private void FinishDrag()
        {
            if (_drag == Drag.None) return;
            var finished = _drag;
            bool moved = _dragMoved;
            string editedClip = _dragClip;
            if (_drag != Drag.Scrub) S.EndContinuous();
            if (_drag == Drag.Move && !_dragMoved && !_altDuplicate && _origStarts.Count > 1)
            {
                // Plain click on an already-selected clip within a multi-selection: select only it.
                S.SelectClip(_dragClip, false);
            }
            _drag = Drag.None;
            _snapLine = null;
            _autoPointIndex = -1;
            _altDuplicate = false;
            _snapshot.Clear();
            _dragPartner = null;
            _dragTrack = null;
            _info.style.display = DisplayStyle.None;
            S.Notify(StudioChange.Content, null);
            Refresh();
            if (moved && IsTrimToolDrag(finished) && S.Tool == EditTool.DynamicTrim) Audition(finished, editedClip);
        }

        private void OnWheel(WheelEvent e)
        {
            var d = e.delta;
            if (e.actionKey || e.ctrlKey)
            {
                V.ZoomAt(e.localMousePosition.x, d.y > 0 ? 1 / 1.15 : 1.15);
            }
            else if (e.shiftKey)
            {
                V.scrollSeconds = Math.Max(0, V.scrollSeconds + d.y * 30 / V.pixelsPerSecond);
            }
            else
            {
                if (Mathf.Abs(d.x) > 0.01f) V.scrollSeconds = Math.Max(0, V.scrollSeconds + d.x * 20 / V.pixelsPerSecond);
                float maxY = Mathf.Max(0, TimelineViewState.ContentHeight(R) - contentRect.height + 40);
                V.scrollY = Mathf.Clamp(V.scrollY + d.y * 18f, 0, maxY);
            }
            ViewChanged?.Invoke();
            e.StopPropagation();
        }

        // ------------------------------------------------------------------ Automation editing

        private void AddAutoPoint(TrackData t, Vector2 pos)
        {
            var info = AutomationTargetInfo.Find(AutomationTargetInfo.ForTrack(t), t.automationTarget);
            if (info == null) return;
            int ti = R.tracks.IndexOf(t);
            float laneTop = V.RowTop(R, ti) + TimelineViewState.TrackHeight;
            long time = Math.Max(0, _snap.Snap(V.SampleAtX(pos.x, SR), out _));
            float value = LaneValueAt(t, info, laneTop, pos.y);
            int index = -1;
            S.BeginContinuous("Add Automation Point");
            S.Continuous(r =>
            {
                var track = r.FindTrack(t.uid);
                var lane = track.GetOrCreateLane(info.TargetId);
                lane.enabled = true;
                lane.points.Add(new AutomationPoint(time, value, AutomationInterpolation.Linear));
                lane.Sort();
                index = lane.points.FindIndex(p => p.time == time && Mathf.Approximately(p.value, value));
            }, StudioChange.Content, this);
            _autoTrack = t.uid;
            _autoPointIndex = index;
            _drag = Drag.AutoPoint;
        }

        private void MoveAutoPoint(Vector2 pos, long sample, bool valueOnly)
        {
            var t = R.FindTrack(_autoTrack);
            if (t == null) return;
            var info = AutomationTargetInfo.Find(AutomationTargetInfo.ForTrack(t), t.automationTarget);
            var lane = t.FindLane(info.TargetId);
            if (lane == null || _autoPointIndex < 0 || _autoPointIndex >= lane.points.Count) return;
            float laneTop = V.RowTop(R, R.tracks.IndexOf(t)) + TimelineViewState.TrackHeight;
            float value = LaneValueAt(t, info, laneTop, pos.y);
            long time = _snap.Snap(Math.Max(0, sample), out _);
            int idx = _autoPointIndex;
            // Keep point order: clamp between neighbours.
            long lo = idx > 0 ? lane.points[idx - 1].time + 1 : 0;
            long hi = idx + 1 < lane.points.Count ? lane.points[idx + 1].time - 1 : long.MaxValue / 4;
            if (valueOnly) time = lane.points[idx].time;
            time = DspMath.Clamp(time, lo, hi);
            S.Continuous(r =>
            {
                var l = r.FindTrack(_autoTrack).FindLane(info.TargetId);
                var p = l.points[idx];
                p.time = time;
                p.value = value;
                l.points[idx] = p;
            }, StudioChange.Content, this);
        }

        private void DeleteAutoPoint(TrackData t, int index)
        {
            var info = AutomationTargetInfo.Find(AutomationTargetInfo.ForTrack(t), t.automationTarget);
            S.Modify("Delete Automation Point", r =>
            {
                var lane = r.FindTrack(t.uid)?.FindLane(info.TargetId);
                if (lane != null && index >= 0 && index < lane.points.Count) lane.points.RemoveAt(index);
            }, StudioChange.Content, this);
        }

        private void SetPointInterpolation(TrackData t, int index, AutomationInterpolation mode)
        {
            var info = AutomationTargetInfo.Find(AutomationTargetInfo.ForTrack(t), t.automationTarget);
            S.Modify("Change Interpolation", r =>
            {
                var lane = r.FindTrack(t.uid)?.FindLane(info.TargetId);
                if (lane == null || index < 0 || index >= lane.points.Count) return;
                var p = lane.points[index]; p.interpolation = mode; lane.points[index] = p;
            }, StudioChange.Content, this);
        }

        // ------------------------------------------------------------------ Context menu

        private void ShowContextMenu(Hit hit, ClipData clip, TrackData track, int pointIndex, Vector2 pos)
        {
            var menu = new GenericMenu();
            long at = V.SampleAtX(pos.x, SR);
            var cmd = _ctx.Commands;
            if (clip != null)
            {
                if (!S.IsClipSelected(clip.uid)) S.SelectClip(clip.uid, false);
                menu.AddItem(new GUIContent("Preview Clip"), false, () => _ctx.Transport.PlayClip(clip.uid));
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("Split at Playhead  (S)"), false, cmd.SplitAtPlayhead);
                menu.AddItem(new GUIContent("Split Here"), false, () =>
                {
                    S.Modify("Split Clip", r => { var c = r.FindClip(clip.uid, out var o); if (c != null) TimelineEditing.Split(o, c, at, r.sampleRate); }, StudioChange.Structure);
                });
                menu.AddItem(new GUIContent("Duplicate  (⌘D)"), false, cmd.DuplicateSelection);
                menu.AddItem(new GUIContent("Copy  (⌘C)"), false, cmd.CopySelection);
                menu.AddItem(new GUIContent("Delete  (⌫)"), false, cmd.DeleteSelection);
                menu.AddSeparator("");
                menu.AddItem(new GUIContent(clip.enabled ? "Disable Clip" : "Enable Clip"), false, () =>
                    S.Modify("Toggle Clip", r => { var c = r.FindClip(clip.uid); if (c != null) c.enabled = !c.enabled; }));
                menu.AddItem(new GUIContent("Loop Source"), clip.loop, () =>
                    S.Modify("Toggle Clip Loop", r => { var c = r.FindClip(clip.uid); if (c != null) c.loop = !c.loop; }));
                menu.AddItem(new GUIContent("Reset Gain & Pan"), false, () =>
                    S.Modify("Reset Clip Gain", r => { var c = r.FindClip(clip.uid); if (c != null) { c.gainDb = 0; c.pan = 0; } }));
                menu.AddItem(new GUIContent("Remove Fades"), false, () =>
                    S.Modify("Remove Fades", r => { var c = r.FindClip(clip.uid); if (c != null) { c.fadeIn.lengthSamples = 0; c.fadeOut.lengthSamples = 0; } }));
                menu.AddSeparator("");
                if (clip.sourceKind == ClipSourceKind.Generator)
                    menu.AddItem(new GUIContent("Edit in Generator"), false, () => _ctx.EditGenerator?.Invoke(clip.generator, clip.uid));
                menu.AddItem(new GUIContent("Open in Loop Editor"), false, () => _ctx.OpenLoopEditorForClip?.Invoke(clip.uid));
                if (clip.sourceClip != null)
                    menu.AddItem(new GUIContent("Reveal Source Asset"), false, () => EditorGUIUtility.PingObject(clip.sourceClip));
            }
            else if (hit == Hit.AutoPoint && track != null)
            {
                var lane = track.FindLane(track.automationTarget);
                var mode = lane != null && pointIndex < lane.points.Count ? lane.points[pointIndex].interpolation : AutomationInterpolation.Linear;
                menu.AddItem(new GUIContent("Linear"), mode == AutomationInterpolation.Linear, () => SetPointInterpolation(track, pointIndex, AutomationInterpolation.Linear));
                menu.AddItem(new GUIContent("Smooth"), mode == AutomationInterpolation.Smooth, () => SetPointInterpolation(track, pointIndex, AutomationInterpolation.Smooth));
                menu.AddItem(new GUIContent("Step"), mode == AutomationInterpolation.Step, () => SetPointInterpolation(track, pointIndex, AutomationInterpolation.Step));
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("Delete Point"), false, () => DeleteAutoPoint(track, pointIndex));
            }
            else
            {
                if (cmd.CanPaste) menu.AddItem(new GUIContent("Paste Here"), false, () => cmd.Paste(Math.Max(0, at), track?.uid));
                else menu.AddDisabledItem(new GUIContent("Paste Here"));
                menu.AddItem(new GUIContent("Set Playhead Here"), false, () => _ctx.Transport.Seek(Math.Max(0, at)));
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("Add Track"), false, () => cmd.AddTrack());
                foreach (var preset in GeneratorPresets.All)
                {
                    var pr = preset;
                    menu.AddItem(new GUIContent($"Insert Generated/{pr.Category}/{pr.Name}"), false, () => cmd.InsertGenerator(pr.Create(), track?.uid, Math.Max(0, at)));
                }
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("Set Loop Region/From Selection"), false, () => SetLoopFromSelection());
                menu.AddItem(new GUIContent("Set Loop Region/Whole Recipe"), false, () =>
                    S.Modify("Set Loop Region", r => { r.loopRegion.enabled = true; r.loopRegion.start = 0; r.loopRegion.end = r.ContentEnd; }));
                menu.AddItem(new GUIContent("Set Loop Region/Clear"), false, () => S.Modify("Clear Loop Region", r => r.loopRegion.enabled = false));
            }
            menu.ShowAsContext();
        }

        public void SetLoopFromSelection()
        {
            long a = long.MaxValue, b = 0;
            foreach (var u in S.SelectedClips) { var c = R.FindClip(u); if (c == null) continue; a = Math.Min(a, c.startSample); b = Math.Max(b, c.EndSample); }
            if (b <= 0) return;
            S.Modify("Set Loop Region", r => { r.loopRegion.enabled = true; r.loopRegion.start = a; r.loopRegion.end = b; });
        }

        // ------------------------------------------------------------------ Drag & drop from the Project window

        private static List<AudioClip> DraggedClips()
        {
            var list = new List<AudioClip>();
            foreach (var o in DragAndDrop.objectReferences) if (o is AudioClip a) list.Add(a);
            return list;
        }

        private void OnDragUpdated(DragUpdatedEvent e)
        {
            var clips = DraggedClips();
            if (clips.Count == 0) { DragAndDrop.visualMode = DragAndDropVisualMode.Rejected; return; }
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            _dropActive = true;
            _dropCount = clips.Count;
            long s = _snap.Snap(V.SampleAtX(e.localMousePosition.x, SR), out _);
            _dropPos = new Vector2(V.X(Math.Max(0, s), SR), e.localMousePosition.y);
            Refresh();
            e.StopPropagation();
        }

        private void OnDragPerform(DragPerformEvent e)
        {
            var clips = DraggedClips();
            _dropActive = false;
            if (clips.Count == 0) return;
            DragAndDrop.AcceptDrag();
            _snap.Build(R, V, S.Playhead, null, S.Snap);
            long at = Math.Max(0, _snap.Snap(V.SampleAtX(e.localMousePosition.x, SR), out _));
            int ti = V.TrackIndexAt(R, e.localMousePosition.y, out _);
            if (ti < 0) ti = 0;
            // Multiple clips dropped below the last track are stacked as layers on new tracks.
            bool stack = clips.Count > 1 && (ti >= R.tracks.Count || e.altKey);
            _ctx.Commands.ImportClips(clips, at, ti, stack);
            Refresh();
            e.StopPropagation();
        }
    }
}
