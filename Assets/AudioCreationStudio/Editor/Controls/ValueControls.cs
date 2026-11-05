using System;
using AudioCreationStudio.DSP;
using UnityEngine;
using UnityEngine.UIElements;

namespace AudioCreationStudio.EditorUI
{
    /// <summary>Value mapping used by sliders/knobs (linear or logarithmic).</summary>
    public struct ValueRange
    {
        public float Min, Max, Default;
        public bool Log;
        public bool Bipolar;
        public float Origin;

        public ValueRange(float min, float max, float def, bool log = false, bool bipolar = false, float origin = 0f)
        {
            Min = min; Max = max; Default = def; Log = log && min > 0; Bipolar = bipolar; Origin = origin;
        }

        public static ValueRange FromParam(ParamDef p) =>
            new ValueRange(p.Min, p.Max, p.Default, p.Scale == ParamScale.Logarithmic, p.Min < 0 && p.Max > 0, 0f);

        public float ToNorm(float v)
        {
            v = Mathf.Clamp(v, Min, Max);
            if (Log) return Mathf.Log(v / Min) / Mathf.Log(Max / Min);
            return Max > Min ? (v - Min) / (Max - Min) : 0;
        }

        public float FromNorm(float n)
        {
            n = Mathf.Clamp01(n);
            return Log ? Min * Mathf.Pow(Max / Min, n) : Min + (Max - Min) * n;
        }
    }

    /// <summary>
    /// Custom-drawn horizontal slider. Drag for coarse, Shift-drag for fine, double-click to reset.
    /// Raises <see cref="EditBegan"/>/<see cref="EditEnded"/> so callers can group undo operations.
    /// </summary>
    public class ValueSlider : VisualElement
    {
        private ValueRange _range;
        private float _value;
        private bool _dragging;
        private float _dragStartNorm, _dragStartX;
        public Color FillColor = StudioTheme.Accent;
        public event Action<float> ValueChanged;
        public event Action EditBegan;
        public event Action EditEnded;

        public float Value
        {
            get => _value;
            set { _value = Mathf.Clamp(value, _range.Min, _range.Max); MarkDirtyRepaint(); }
        }

        public ValueRange Range { get => _range; set { _range = value; MarkDirtyRepaint(); } }

        public ValueSlider(ValueRange range, float value)
        {
            _range = range;
            _value = value;
            AddToClassList("acs-slider");
            focusable = true;
            generateVisualContent += Draw;
            RegisterCallback<PointerDownEvent>(OnDown);
            RegisterCallback<PointerMoveEvent>(OnMove);
            RegisterCallback<PointerUpEvent>(OnUp);
            RegisterCallback<PointerCaptureOutEvent>(_ => EndDrag());
            RegisterCallback<KeyDownEvent>(OnKey);
        }

        private Rect Track => new Rect(6, contentRect.height * 0.5f - 2, Mathf.Max(1, contentRect.width - 12), 4);

        private void Draw(MeshGenerationContext ctx)
        {
            var p = ctx.painter2D;
            var t = Track;
            bool enabled = enabledInHierarchy;
            PaintUtil.FillRounded(p, t, 2, StudioTheme.Inset);
            float n = _range.ToNorm(_value);
            float x = t.x + n * t.width;
            float ox = _range.Bipolar ? t.x + _range.ToNorm(_range.Origin) * t.width : t.x;
            var fill = enabled ? FillColor : StudioTheme.TextFaint;
            var fr = new Rect(Mathf.Min(ox, x), t.y, Mathf.Abs(x - ox), t.height);
            PaintUtil.FillRounded(p, fr, 2, fill);
            bool hot = _dragging || (focusController?.focusedElement == this);
            PaintUtil.Circle(p, new Vector2(x, t.center.y), hot ? 6.5f : 5.5f, enabled ? StudioTheme.Text : StudioTheme.TextDim);
            PaintUtil.Circle(p, new Vector2(x, t.center.y), 2.5f, fill);
        }

        private void OnDown(PointerDownEvent e)
        {
            if (!enabledInHierarchy || e.button != 0) return;
            if (e.clickCount == 2)
            {
                EditBegan?.Invoke();
                Set(_range.Default);
                EditEnded?.Invoke();
                e.StopPropagation();
                return;
            }
            var t = Track;
            float thumbX = t.x + _range.ToNorm(_value) * t.width;
            _dragging = true;
            EditBegan?.Invoke();
            if (Mathf.Abs(e.localPosition.x - thumbX) > 8)
                Set(_range.FromNorm((e.localPosition.x - t.x) / t.width));
            _dragStartNorm = _range.ToNorm(_value);
            _dragStartX = e.localPosition.x;
            this.CapturePointer(e.pointerId);
            Focus();
            e.StopPropagation();
            MarkDirtyRepaint();
        }

        private void OnMove(PointerMoveEvent e)
        {
            if (!_dragging || !this.HasPointerCapture(e.pointerId)) return;
            float scale = e.shiftKey ? 0.1f : 1f;
            float n = _dragStartNorm + (e.localPosition.x - _dragStartX) / Track.width * scale;
            Set(_range.FromNorm(n));
            e.StopPropagation();
        }

        private void OnUp(PointerUpEvent e)
        {
            if (!_dragging) return;
            this.ReleasePointer(e.pointerId);
            EndDrag();
            e.StopPropagation();
        }

        private void EndDrag()
        {
            if (!_dragging) return;
            _dragging = false;
            EditEnded?.Invoke();
            MarkDirtyRepaint();
        }

        private void OnKey(KeyDownEvent e)
        {
            float step = e.shiftKey ? 0.01f : 0.05f;
            if (e.keyCode == KeyCode.LeftArrow || e.keyCode == KeyCode.DownArrow) { Nudge(-step); e.StopPropagation(); }
            else if (e.keyCode == KeyCode.RightArrow || e.keyCode == KeyCode.UpArrow) { Nudge(step); e.StopPropagation(); }
        }

        private void Nudge(float dn)
        {
            EditBegan?.Invoke();
            Set(_range.FromNorm(_range.ToNorm(_value) + dn));
            EditEnded?.Invoke();
        }

        private void Set(float v)
        {
            v = Mathf.Clamp(v, _range.Min, _range.Max);
            if (Mathf.Approximately(v, _value)) return;
            _value = v;
            MarkDirtyRepaint();
            ValueChanged?.Invoke(v);
        }
    }

    /// <summary>Label + slider + editable numeric readout row.</summary>
    public class ParamRow : VisualElement
    {
        public readonly ValueSlider Slider;
        private readonly Label _valueLabel;
        private readonly TextField _edit;
        private readonly Func<float, string> _format;
        public event Action<float> ValueChanged;
        public event Action EditBegan;
        public event Action EditEnded;

        public float Value
        {
            get => Slider.Value;
            set { Slider.Value = value; _valueLabel.text = _format(Slider.Value); }
        }

        public ParamRow(string label, ValueRange range, float value, Func<float, string> format, string tooltipText = null)
        {
            AddToClassList("acs-param");
            _format = format ?? (v => v.ToString("0.00"));
            var l = new Label(label);
            l.AddToClassList("acs-param__label");
            l.tooltip = tooltipText ?? $"{label} — double-click the slider to reset";
            Add(l);
            Slider = new ValueSlider(range, value);
            Slider.AddToClassList("acs-param__slider");
            Add(Slider);
            _valueLabel = new Label(_format(value));
            _valueLabel.AddToClassList("acs-param__value");
            _valueLabel.tooltip = "Click to type a value";
            Add(_valueLabel);
            _edit = new TextField { isDelayed = true };
            _edit.AddToClassList("acs-param__edit");
            _edit.style.display = DisplayStyle.None;
            Add(_edit);

            Slider.ValueChanged += v => { _valueLabel.text = _format(v); ValueChanged?.Invoke(v); };
            Slider.EditBegan += () => EditBegan?.Invoke();
            Slider.EditEnded += () => EditEnded?.Invoke();

            _valueLabel.AddManipulator(new Clickable(() =>
            {
                _valueLabel.style.display = DisplayStyle.None;
                _edit.style.display = DisplayStyle.Flex;
                _edit.SetValueWithoutNotify(Slider.Value.ToString("0.###"));
                _edit.schedule.Execute(() => { _edit.Focus(); _edit.SelectAll(); });
            }));
            _edit.RegisterValueChangedCallback(e => Commit(e.newValue));
            _edit.RegisterCallback<FocusOutEvent>(_ => CloseEdit());
            _edit.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Escape) CloseEdit(); }, TrickleDown.TrickleDown);
        }

        private void Commit(string text)
        {
            var s = (text ?? "").Trim().ToLowerInvariant().Replace("db", "").Replace("hz", "").Replace("ms", "").Trim();
            float mult = 1f;
            if (s.EndsWith("k")) { mult = 1000f; s = s.Substring(0, s.Length - 1); }
            if (float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v))
            {
                EditBegan?.Invoke();
                Slider.Value = v * mult;
                _valueLabel.text = _format(Slider.Value);
                ValueChanged?.Invoke(Slider.Value);
                EditEnded?.Invoke();
            }
            CloseEdit();
        }

        private void CloseEdit()
        {
            _edit.style.display = DisplayStyle.None;
            _valueLabel.style.display = DisplayStyle.Flex;
        }
    }

    /// <summary>Rotary knob (used for pan). Vertical drag, Shift for fine, double-click resets.</summary>
    public class Knob : VisualElement
    {
        private ValueRange _range;
        private float _value;
        private bool _drag;
        private float _startY, _startN;
        public Color ArcColor = StudioTheme.Accent;
        public Func<float, string> Formatter;
        public event Action<float> ValueChanged;
        public event Action EditBegan;
        public event Action EditEnded;

        public float Value
        {
            get => _value;
            set { _value = Mathf.Clamp(value, _range.Min, _range.Max); UpdateTip(); MarkDirtyRepaint(); }
        }

        public Knob(ValueRange range, float value, float size = 26)
        {
            _range = range;
            _value = value;
            AddToClassList("acs-knob");
            style.width = size;
            style.height = size;
            generateVisualContent += Draw;
            RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                if (e.clickCount == 2) { EditBegan?.Invoke(); Set(_range.Default); EditEnded?.Invoke(); e.StopPropagation(); return; }
                _drag = true; _startY = e.position.y; _startN = _range.ToNorm(_value);
                EditBegan?.Invoke();
                this.CapturePointer(e.pointerId);
                e.StopPropagation();
            });
            RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!_drag) return;
                float scale = e.shiftKey ? 0.0015f : 0.008f;
                Set(_range.FromNorm(_startN + (_startY - e.position.y) * scale));
                e.StopPropagation();
            });
            RegisterCallback<PointerUpEvent>(e =>
            {
                if (!_drag) return;
                _drag = false;
                this.ReleasePointer(e.pointerId);
                EditEnded?.Invoke();
            });
            UpdateTip();
        }

        private void UpdateTip() => tooltip = Formatter != null ? Formatter(_value) : _value.ToString("0.00");

        private void Set(float v)
        {
            v = Mathf.Clamp(v, _range.Min, _range.Max);
            if (Mathf.Approximately(v, _value)) return;
            _value = v;
            UpdateTip();
            MarkDirtyRepaint();
            ValueChanged?.Invoke(v);
        }

        private void Draw(MeshGenerationContext ctx)
        {
            var p = ctx.painter2D;
            var r = contentRect;
            var c = r.center;
            float rad = Mathf.Min(r.width, r.height) * 0.5f - 2f;
            const float a0 = 135f, sweep = 270f;
            p.lineCap = LineCap.Round;
            p.lineWidth = 2.5f;
            p.strokeColor = StudioTheme.Inset;
            p.BeginPath();
            p.Arc(c, rad, Angle.Degrees(a0), Angle.Degrees(a0 + sweep), ArcDirection.Clockwise);
            p.Stroke();
            float n = _range.ToNorm(_value);
            float on = _range.Bipolar ? _range.ToNorm(_range.Origin) : 0f;
            float aa = a0 + Mathf.Min(n, on) * sweep, ab = a0 + Mathf.Max(n, on) * sweep;
            if (ab - aa > 0.5f)
            {
                p.strokeColor = enabledInHierarchy ? ArcColor : StudioTheme.TextFaint;
                p.BeginPath();
                p.Arc(c, rad, Angle.Degrees(aa), Angle.Degrees(ab), ArcDirection.Clockwise);
                p.Stroke();
            }
            PaintUtil.Circle(p, c, rad - 4f, StudioTheme.Elevated);
            float ang = (a0 + n * sweep) * Mathf.Deg2Rad;
            var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            PaintUtil.Line(p, c + dir * (rad * 0.2f), c + dir * (rad - 5f), StudioTheme.Text, 2f);
        }
    }

    /// <summary>Vertical mixer fader with a DAW-style taper (gain^(1/4)) from −∞ to +12 dB.</summary>
    public class VerticalFader : VisualElement
    {
        public const float MaxDb = 12f;
        private float _db;
        private bool _drag;
        private float _startY, _startN;
        public Color CapColor = StudioTheme.Text;
        public event Action<float> ValueChanged;
        public event Action EditBegan;
        public event Action EditEnded;

        public float ValueDb { get => _db; set { _db = Mathf.Clamp(value, DspMath.MinDb, MaxDb); MarkDirtyRepaint(); } }

        public static float DbToNorm(float db)
        {
            if (db <= -80f) return 0f;
            double g = DspMath.DbToGain((double)db) / DspMath.DbToGain((double)MaxDb);
            return (float)Math.Pow(g, 0.25);
        }

        public static float NormToDb(float n)
        {
            n = Mathf.Clamp01(n);
            if (n <= 0.0001f) return DspMath.MinDb;
            double g = Math.Pow(n, 4) * DspMath.DbToGain((double)MaxDb);
            return Mathf.Max(-80f, (float)(20 * Math.Log10(g)));
        }

        public VerticalFader(float db)
        {
            _db = db;
            AddToClassList("acs-fader");
            generateVisualContent += Draw;
            RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                if (e.clickCount == 2) { EditBegan?.Invoke(); Set(0f); EditEnded?.Invoke(); e.StopPropagation(); return; }
                _drag = true; _startY = e.localPosition.y; _startN = DbToNorm(_db);
                var tr = TrackRect;
                float capY = tr.yMax - _startN * tr.height;
                if (Mathf.Abs(e.localPosition.y - capY) > 10) { _startN = Mathf.Clamp01((tr.yMax - e.localPosition.y) / tr.height); }
                EditBegan?.Invoke();
                Set(NormToDb(_startN));
                this.CapturePointer(e.pointerId);
                e.StopPropagation();
            });
            RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!_drag) return;
                float scale = e.shiftKey ? 0.1f : 1f;
                Set(NormToDb(_startN + (_startY - e.localPosition.y) / TrackRect.height * scale));
            });
            RegisterCallback<PointerUpEvent>(e =>
            {
                if (!_drag) return;
                _drag = false;
                this.ReleasePointer(e.pointerId);
                EditEnded?.Invoke();
            });
        }

        private Rect TrackRect => new Rect(contentRect.width * 0.5f - 2, 10, 4, Mathf.Max(10, contentRect.height - 20));

        private void Set(float db)
        {
            db = Mathf.Clamp(db, DspMath.MinDb, MaxDb);
            if (Mathf.Approximately(db, _db)) return;
            _db = db;
            MarkDirtyRepaint();
            ValueChanged?.Invoke(db);
        }

        private void Draw(MeshGenerationContext ctx)
        {
            var p = ctx.painter2D;
            var tr = TrackRect;
            PaintUtil.FillRounded(p, tr, 2, StudioTheme.Inset);
            // Scale ticks at +12, +6, 0, -6, -12, -24, -48.
            foreach (var db in new[] { 12f, 6f, 0f, -6f, -12f, -24f, -48f })
            {
                float y = tr.yMax - DbToNorm(db) * tr.height;
                float w = db == 0f ? 10f : 6f;
                PaintUtil.Line(p, new Vector2(tr.x - w - 2, y), new Vector2(tr.x - 3, y), db == 0f ? StudioTheme.TextDim : StudioTheme.Border, 1f);
            }
            float n = DbToNorm(_db);
            float cy = tr.yMax - n * tr.height;
            var fill = new Rect(tr.x, cy, tr.width, tr.yMax - cy);
            PaintUtil.FillRounded(p, fill, 2, StudioTheme.Accent.WithAlpha(0.6f));
            var cap = new Rect(contentRect.width * 0.5f - 11, cy - 7, 22, 14);
            PaintUtil.FillRounded(p, cap, 4, StudioTheme.Elevated);
            PaintUtil.StrokeRounded(p, cap, 4, StudioTheme.Border, 1f);
            PaintUtil.Line(p, new Vector2(cap.x + 4, cy), new Vector2(cap.xMax - 4, cy), CapColor, 1.5f);
        }
    }

    /// <summary>
    /// Stereo level meter showing real rendered signal levels with ballistics: instant attack, 24 dB/s
    /// fall-back, 1.5 s peak hold and a latched clip indicator (click to reset).
    /// </summary>
    public class LevelMeterElement : VisualElement
    {
        public const float FloorDb = -60f, CeilDb = 6f;
        private readonly bool _horizontal;
        private readonly QuadBatch _quads = new QuadBatch();
        private float _l = FloorDb, _r = FloorDb, _holdL = FloorDb, _holdR = FloorDb;
        private double _holdTimeL, _holdTimeR, _lastTime;
        private bool _clip;
        public bool Clipped => _clip;
        public event Action ClipReset;

        public LevelMeterElement(bool horizontal)
        {
            _horizontal = horizontal;
            AddToClassList("acs-meter");
            AddToClassList(horizontal ? "acs-meter--h" : "acs-meter--v");
            generateVisualContent += Draw;
            tooltip = "Peak meter (real rendered signal). Click to reset the clip indicator.";
            this.AddManipulator(new Clickable(() => { _clip = false; MarkDirtyRepaint(); ClipReset?.Invoke(); }));
        }

        public void Push(LevelReading reading, double time)
        {
            double dt = _lastTime > 0 ? Math.Min(0.25, time - _lastTime) : 0.016;
            _lastTime = time;
            float fall = (float)(24.0 * dt);
            float dl = DspMath.GainToDb(reading.PeakL), dr = DspMath.GainToDb(reading.PeakR);
            _l = dl >= _l ? dl : Mathf.Max(dl, _l - fall);
            _r = dr >= _r ? dr : Mathf.Max(dr, _r - fall);
            if (dl >= _holdL || time - _holdTimeL > 1.5) { _holdL = dl; _holdTimeL = time; }
            if (dr >= _holdR || time - _holdTimeR > 1.5) { _holdR = dr; _holdTimeR = time; }
            if (reading.Clipped) _clip = true;
            MarkDirtyRepaint();
        }

        public void Decay(double time) => Push(LevelReading.Silent, time);

        public bool IsActive => _l > FloorDb + 0.5f || _r > FloorDb + 0.5f || _holdL > FloorDb + 0.5f;

        private static float Norm(float db) => Mathf.Clamp01((db - FloorDb) / (CeilDb - FloorDb));

        private void Draw(MeshGenerationContext ctx)
        {
            var r = contentRect;
            if (r.width <= 2 || r.height <= 2) return;
            float led = 6f;
            if (_horizontal)
            {
                float barH = (r.height - 3) * 0.5f;
                float w = r.width - led - 3;
                DrawBar(new Rect(0, 0, w, barH), _l, _holdL);
                DrawBar(new Rect(0, barH + 3, w, barH), _r, _holdR);
                _quads.Rect(r.width - led, 0, led, r.height, _clip ? StudioTheme.Error : StudioTheme.Inset);
            }
            else
            {
                float barW = (r.width - 3) * 0.5f;
                float h = r.height - led - 3;
                DrawBar(new Rect(0, led + 3, barW, h), _l, _holdL);
                DrawBar(new Rect(barW + 3, led + 3, barW, h), _r, _holdR);
                _quads.Rect(0, 0, r.width, led, _clip ? StudioTheme.Error : StudioTheme.Inset);
            }
            _quads.Flush(ctx);
        }

        private void DrawBar(Rect r, float db, float hold)
        {
            _quads.Rect(r, StudioTheme.Inset);
            float n = Norm(db);
            float nYellow = Norm(-12f), nRed = Norm(-3f);
            void Zone(float a, float b, Color c)
            {
                float lo = Mathf.Min(a, n), hi = Mathf.Min(b, n);
                if (hi <= lo) return;
                if (_horizontal) _quads.Rect(r.x + lo * r.width, r.y, (hi - lo) * r.width, r.height, c);
                else _quads.Rect(r.x, r.yMax - hi * r.height, r.width, (hi - lo) * r.height, c);
            }
            Zone(0, nYellow, StudioTheme.Success);
            Zone(nYellow, nRed, StudioTheme.Warning);
            Zone(nRed, 1f, StudioTheme.Error);
            float hn = Norm(hold);
            if (hn > 0.001f)
            {
                var hc = hold > -3f ? StudioTheme.Error : StudioTheme.Text;
                if (_horizontal) _quads.Rect(r.x + hn * r.width - 1, r.y, 2, r.height, hc);
                else _quads.Rect(r.x, r.yMax - hn * r.height, r.width, 2, hc);
            }
        }
    }
}
