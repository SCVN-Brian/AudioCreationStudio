using System;
using AudioCreationStudio.DSP;
using AudioCreationStudio.Models;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AudioCreationStudio.EditorUI
{
    /// <summary>Factory for inspector rows bound to the session (with grouped undo for drags).</summary>
    public static class Fields
    {
        public static ParamRow Param(StudioContext ctx, string label, ValueRange range, float value, Func<float, string> fmt,
            string undo, Action<RecipeData, float> set, object origin, string tooltip = null)
        {
            var row = new ParamRow(label, range, value, fmt, tooltip);
            row.EditBegan += () => ctx.Session.BeginContinuous(undo);
            row.ValueChanged += v => ctx.Session.Continuous(d => set(d, v), StudioChange.Content, origin);
            row.EditEnded += () => ctx.Session.EndContinuous();
            return row;
        }

        public static ParamRow Db(StudioContext ctx, string label, float value, string undo, Action<RecipeData, float> set, object origin, float min = -60f, float max = 24f)
            => Param(ctx, label, new ValueRange(min, max, 0f, false, true), value, v => v <= -59.9f ? "-∞ dB" : $"{v:+0.0;-0.0;0.0} dB", undo, set, origin);

        public static ParamRow Pan(StudioContext ctx, string label, float value, string undo, Action<RecipeData, float> set, object origin)
            => Param(ctx, label, new ValueRange(-1f, 1f, 0f, false, true), value, AutomationTargetInfo.FormatPan, undo, set, origin);

        /// <summary>Seconds field that stores sample-accurate values.</summary>
        public static VisualElement Time(StudioContext ctx, string label, long samples, string undo, Action<RecipeData, long> set, object origin, string tooltip = null, int sampleRate = 0)
        {
            int sr = sampleRate > 0 ? sampleRate : ctx.SampleRate;
            var row = new VisualElement();
            row.AddToClassList("acs-param");
            var l = UIUtil.Label(label, "acs-param__label");
            l.tooltip = tooltip;
            row.Add(l);
            var f = new DoubleField { isDelayed = true, value = Math.Round(samples / (double)sr, 6) };
            f.AddToClassList("acs-field");
            f.style.flexGrow = 1;
            row.Add(f);
            var unit = UIUtil.Label("s", "acs-param__unit");
            row.Add(unit);
            var smp = UIUtil.Label($"{samples:N0} smp", "acs-param__hint");
            row.Add(smp);
            f.RegisterValueChangedCallback(e =>
            {
                long s = Math.Max(0, SampleTime.SecondsToSamples(e.newValue, sr));
                ctx.Session.Modify(undo, d => set(d, s), StudioChange.Content, null);
            });
            return row;
        }

        public static VisualElement Switch(string label, bool value, Action<bool> changed, string tooltip = null)
        {
            var row = new VisualElement();
            row.AddToClassList("acs-param");
            var l = UIUtil.Label(label, "acs-param__label");
            l.tooltip = tooltip;
            row.Add(l);
            var s = new SwitchToggle(value, tooltip);
            s.Changed += changed;
            row.Add(s);
            row.Add(UIUtil.Spacer());
            return row;
        }

        public static VisualElement Enum<T>(string label, T value, Action<T> changed, string tooltip = null) where T : Enum
        {
            var row = new VisualElement();
            row.AddToClassList("acs-param");
            var l = UIUtil.Label(label, "acs-param__label");
            l.tooltip = tooltip;
            row.Add(l);
            var f = new EnumField(value);
            f.AddToClassList("acs-field");
            f.style.flexGrow = 1;
            f.RegisterValueChangedCallback(e => changed((T)e.newValue));
            row.Add(f);
            return row;
        }

        public static VisualElement Text(string label, string value, Action<string> changed)
        {
            var row = new VisualElement();
            row.AddToClassList("acs-param");
            row.Add(UIUtil.Label(label, "acs-param__label"));
            var f = new TextField { isDelayed = true, value = value ?? "" };
            f.AddToClassList("acs-field");
            f.style.flexGrow = 1;
            f.RegisterValueChangedCallback(e => changed(e.newValue));
            row.Add(f);
            return row;
        }

        public static VisualElement Info(string label, string value)
        {
            var row = new VisualElement();
            row.AddToClassList("acs-param");
            row.Add(UIUtil.Label(label, "acs-param__label"));
            row.Add(UIUtil.Label(value, "acs-param__info"));
            return row;
        }

        public static VisualElement Popup(string label, string[] options, int index, Action<int> changed)
        {
            var row = new VisualElement();
            row.AddToClassList("acs-param");
            row.Add(UIUtil.Label(label, "acs-param__label"));
            var f = new PopupField<string>(new System.Collections.Generic.List<string>(options), Mathf.Clamp(index, 0, options.Length - 1));
            f.AddToClassList("acs-field");
            f.style.flexGrow = 1;
            f.RegisterValueChangedCallback(e => changed(Array.IndexOf(options, e.newValue)));
            row.Add(f);
            return row;
        }

        public static VisualElement Warning(string text, bool error = false)
        {
            var w = new VisualElement();
            w.AddToClassList("acs-callout");
            w.AddToClassList(error ? "acs-callout--error" : "acs-callout--warning");
            w.Add(new IconElement(Icon.Warning, 14) { Color = error ? StudioTheme.Error : StudioTheme.Warning });
            var l = UIUtil.Label(text, "acs-callout__text");
            w.Add(l);
            return w;
        }
    }
}
