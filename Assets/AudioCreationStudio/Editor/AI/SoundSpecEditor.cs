using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AudioCreationStudio.EditorUI
{
    /// <summary>
    /// Review / override form for the nine analysed sound parameters. Every field is editable; fields that differ from
    /// the AI's answer are marked, and "Reset to AI" restores them. List fields (texture, constraints) are comma
    /// separated, and ▾ menus offer common values. Raises <see cref="Changed"/> on every user edit.
    /// </summary>
    public sealed class SoundSpecEditor : VisualElement
    {
        public event Action Changed;

        private static readonly string[] TextureHints = { "Wet", "Bubbly", "Muffled", "Metallic", "Crunchy", "Airy", "Hollow", "Gritty", "Glassy", "Woody", "Rubbery", "Squishy", "Smooth", "Sharp", "Breathy", "Raspy" };
        private static readonly string[] PitchHints = { "Low", "Medium", "High", "Very high", "Variable", "Rising from low to high", "Falling from high to low" };
        private static readonly string[] SpacingHints = { "Short pauses", "Even spacing", "Rapid succession", "Irregular spacing", "Long pauses" };
        private static readonly string[] IntensityHints = { "Soft", "Moderate", "Strong", "Aggressive", "Explosive", "Dynamic, building up", "Dynamic, fading out" };
        private static readonly string[] StyleHints = { "Realistic", "Cartoon", "Casual game", "Cartoon casual game", "Arcade", "Retro 8-bit", "Cinematic", "Fantasy", "Sci-fi", "Horror", "UI" };
        private static readonly string[] AttackHints = { "Immediate", "Sharp", "Soft", "Slow fade-in" };
        private static readonly string[] ReleaseHints = { "Short natural decay", "Abrupt stop", "Long tail", "Slow fade-out", "Ringing tail" };
        private static readonly string[] ConstraintHints = { "No music", "No background ambience", "No dialogue", "No reverb", "No distortion", "No voice", "No echo" };

        private readonly TextField _source, _action, _texture, _pitch, _pattern, _spacing, _intensity, _style, _attack, _sustain, _release, _constraints;
        private readonly IntegerField _reps;
        private readonly Label _aiNote;
        private readonly StudioButton _resetBtn;
        private readonly Dictionary<VisualElement, Func<SoundSpec, string>> _rowValue = new Dictionary<VisualElement, Func<SoundSpec, string>>();
        private SoundSpec _ai;
        private bool _loading;

        /// <summary>Slot in the Duration / Structure group where the host puts its duration controls.</summary>
        public readonly VisualElement DurationSlot;

        public bool HasAiValues => _ai != null;

        public SoundSpecEditor()
        {
            AddToClassList("acs-spec");

            var head = new VisualElement();
            head.AddToClassList("acs-row");
            head.AddToClassList("acs-spec__head");
            _aiNote = UIUtil.Label("Fill these in yourself, or let AI analyse the description above.", "acs-hint", "acs-grow");
            head.Add(_aiNote);
            _resetBtn = new StudioButton(Icon.Undo, "Reset to AI", ResetToAi, "Restore every field to the AI's analysis", "acs-btn--sm");
            _resetBtn.SetEnabled(false);
            head.Add(_resetBtn);
            head.Add(new StudioButton(Icon.Trash, null, Clear, "Clear all fields", "acs-btn--sm"));
            Add(head);

            Group("SOURCE & ACTION");
            _source = Row("Sound Source", "Object, character, machine, creature or environment producing the sound", s => s.soundSource, null);
            _action = Row("Action / Event", "Physical action or event that makes the sound", s => s.action, null);

            Group("CHARACTER");
            _texture = Row("Sound Texture", "Acoustic qualities, comma separated (wet, bubbly, metallic…)", s => SoundSpec.JoinList(s.texture), TextureHints, true);
            _pitch = Row("Pitch", "Low, medium, high, variable, or a pitch profile", s => s.pitch, PitchHints);
            _intensity = Row("Intensity", "Soft, moderate, strong, aggressive, explosive or dynamic", s => s.intensity, IntensityHints);
            _style = Row("Style", "Cartoon, casual game, realistic, cinematic, arcade, fantasy…", s => s.style, StyleHints);

            Group("RHYTHM / TIMING");
            _pattern = Row("Pattern", "What happens over time, e.g. three short gurgles", s => s.rhythm.pattern, null);
            var repRow = NewRow("Repetitions", "Number of distinct events (0 = continuous or not counted)");
            _reps = new IntegerField { value = 0 };
            _reps.AddToClassList("acs-field");
            _reps.AddToClassList("acs-spec__reps");
            _reps.RegisterValueChangedCallback(e =>
            {
                if (e.newValue < 0) _reps.SetValueWithoutNotify(0);
                OnEdited();
            });
            repRow.Add(_reps);
            repRow.Add(UIUtil.Label("Spacing", "acs-spec__inlinelabel"));
            _spacing = Field(repRow, s => s.rhythm.spacing, SpacingHints, "Spacing between events");
            _rowValue[repRow] = s => s.rhythm.repetitions + "|" + s.rhythm.spacing;

            Group("DURATION / STRUCTURE");
            DurationSlot = new VisualElement();
            DurationSlot.AddToClassList("acs-spec__durationslot");
            Add(DurationSlot);
            _attack = Row("Attack", "How the sound starts", s => s.duration.attack, AttackHints);
            _sustain = Row("Sustain", "What happens in the middle, e.g. three distinct vocal bursts", s => s.duration.sustain, null);
            _release = Row("Release", "How it ends", s => s.duration.release, ReleaseHints);

            Group("CONSTRAINTS");
            _constraints = Row("Avoid", "Unwanted elements, comma separated (no music, no ambience, no dialogue…)", s => SoundSpec.JoinList(s.constraints), ConstraintHints, true);
        }

        // ------------------------------------------------------------------ Public

        /// <summary>Loads an AI analysis: fills every field and makes it the baseline for "edited" markers.</summary>
        public void LoadAi(SoundSpec spec, string note)
        {
            _ai = spec.Clone().Normalize();
            Fill(_ai);
            _aiNote.text = note;
            _resetBtn.SetEnabled(true);
            UpdateMarkers();
        }

        /// <summary>The current values (user overrides included). Duration seconds / loop / influence are left to the host.</summary>
        public SoundSpec Read()
        {
            var s = _ai != null ? _ai.Clone() : new SoundSpec();
            s.soundSource = _source.value;
            s.action = _action.value;
            s.texture = SoundSpec.SplitList(_texture.value);
            s.pitch = _pitch.value;
            s.rhythm.pattern = _pattern.value;
            s.rhythm.repetitions = Math.Max(0, _reps.value);
            s.rhythm.spacing = _spacing.value;
            s.intensity = _intensity.value;
            s.style = _style.value;
            s.duration.attack = _attack.value;
            s.duration.sustain = _sustain.value;
            s.duration.release = _release.value;
            s.constraints = SoundSpec.SplitList(_constraints.value);
            return s.Normalize();
        }

        public bool IsEmpty
        {
            get
            {
                var s = Read();
                return s.soundSource.Length == 0 && s.action.Length == 0 && s.texture.Length == 0 && s.rhythm.pattern.Length == 0;
            }
        }

        /// <summary>Number of fields that differ from the AI analysis.</summary>
        public int EditedCount { get; private set; }

        /// <summary>Appends text to the rhythm pattern (used by the reference clip's timing hint).</summary>
        public void AppendToPattern(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            string cur = (_pattern.value ?? "").Trim();
            if (cur.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0) return;
            _pattern.value = cur.Length == 0 ? text : $"{cur}; {text}";
        }

        // ------------------------------------------------------------------ Build

        private void Group(string title) => Add(UIUtil.Label(title, "acs-spec__group"));

        private VisualElement NewRow(string label, string tooltip)
        {
            var row = new VisualElement();
            row.AddToClassList("acs-spec__row");
            var l = UIUtil.Label(label, "acs-spec__label");
            l.tooltip = tooltip;
            row.Add(l);
            Add(row);
            return row;
        }

        private TextField Row(string label, string tooltip, Func<SoundSpec, string> value, string[] hints, bool appendHints = false)
        {
            var row = NewRow(label, tooltip);
            var f = Field(row, value, hints, tooltip, appendHints);
            _rowValue[row] = value;
            return f;
        }

        private TextField Field(VisualElement row, Func<SoundSpec, string> value, string[] hints, string tooltip, bool appendHints = false)
        {
            var f = new TextField { value = "" };
            f.AddToClassList("acs-field");
            f.AddToClassList("acs-spec__field");
            f.tooltip = tooltip;
            f.RegisterValueChangedCallback(_ => OnEdited());
            row.Add(f);
            if (hints != null)
            {
                StudioButton b = null;
                b = new StudioButton(Icon.ChevronDown, null, () =>
                {
                    var m = new GenericMenu();
                    var current = SoundSpec.SplitList(f.value);
                    foreach (var h in hints)
                    {
                        string hint = h;
                        bool on = appendHints ? Array.Exists(current, c => string.Equals(c, hint, StringComparison.OrdinalIgnoreCase))
                                              : string.Equals((f.value ?? "").Trim(), hint, StringComparison.OrdinalIgnoreCase);
                        m.AddItem(new GUIContent(hint), on, () =>
                        {
                            if (!appendHints) { f.value = hint; return; }
                            var list = new List<string>(SoundSpec.SplitList(f.value));
                            int i = list.FindIndex(c => string.Equals(c, hint, StringComparison.OrdinalIgnoreCase));
                            if (i >= 0) list.RemoveAt(i); else list.Add(hint);
                            f.value = SoundSpec.JoinList(list);
                        });
                    }
                    m.DropDown(b.worldBound);
                }, appendHints ? "Add or remove common values" : "Pick a common value", "acs-btn--sm");
                b.AddToClassList("acs-spec__hint");
                row.Add(b);
            }
            return f;
        }

        // ------------------------------------------------------------------ State

        private void Fill(SoundSpec s)
        {
            _loading = true;
            _source.SetValueWithoutNotify(s.soundSource);
            _action.SetValueWithoutNotify(s.action);
            _texture.SetValueWithoutNotify(SoundSpec.JoinList(s.texture));
            _pitch.SetValueWithoutNotify(s.pitch);
            _pattern.SetValueWithoutNotify(s.rhythm.pattern);
            _reps.SetValueWithoutNotify(s.rhythm.repetitions);
            _spacing.SetValueWithoutNotify(s.rhythm.spacing);
            _intensity.SetValueWithoutNotify(s.intensity);
            _style.SetValueWithoutNotify(s.style);
            _attack.SetValueWithoutNotify(s.duration.attack);
            _sustain.SetValueWithoutNotify(s.duration.sustain);
            _release.SetValueWithoutNotify(s.duration.release);
            _constraints.SetValueWithoutNotify(SoundSpec.JoinList(s.constraints));
            _loading = false;
        }

        private void ResetToAi()
        {
            if (_ai == null) return;
            Fill(_ai);
            UpdateMarkers();
            Changed?.Invoke();
        }

        private void Clear()
        {
            Fill(new SoundSpec());
            UpdateMarkers();
            Changed?.Invoke();
        }

        private void OnEdited()
        {
            if (_loading) return;
            UpdateMarkers();
            Changed?.Invoke();
        }

        private void UpdateMarkers()
        {
            var cur = Read();
            int edited = 0;
            foreach (var kv in _rowValue)
            {
                bool diff = _ai != null && !string.Equals(Norm(kv.Value(cur)), Norm(kv.Value(_ai)), StringComparison.OrdinalIgnoreCase);
                kv.Key.EnableInClassList("acs-spec__row--edited", diff);
                kv.Key.tooltip = diff ? "Edited by you · AI suggested: " + (string.IsNullOrEmpty(kv.Value(_ai)) ? "(empty)" : kv.Value(_ai)) : null;
                if (diff) edited++;
            }
            EditedCount = edited;
        }

        private static string Norm(string s) => SoundSpec.JoinList(SoundSpec.SplitList(s));
    }
}
