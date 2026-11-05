using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace AudioCreationStudio.EditorUI
{
    /// <summary>Flat icon/text button with hover, active, toggled, disabled and keyboard-focus states (styled in USS).</summary>
    public class StudioButton : VisualElement
    {
        public readonly IconElement IconView;
        public readonly Label LabelView;
        private Action _onClick;
        private bool _toggled;

        public bool Toggled
        {
            get => _toggled;
            set { _toggled = value; EnableInClassList("acs-btn--on", value); }
        }

        public string Text
        {
            get => LabelView?.text;
            set { if (LabelView != null) LabelView.text = value; }
        }

        public StudioButton(Icon icon, string text, Action onClick, string tooltipText = null, params string[] classes)
        {
            _onClick = onClick;
            AddToClassList("acs-btn");
            foreach (var c in classes) AddToClassList(c);
            focusable = true;
            tooltip = tooltipText;
            if (icon != Icon.None)
            {
                IconView = new IconElement(icon, 16);
                Add(IconView);
                AddToClassList("acs-btn--hasicon");
            }
            if (!string.IsNullOrEmpty(text))
            {
                LabelView = new Label(text);
                LabelView.AddToClassList("acs-btn__label");
                LabelView.pickingMode = PickingMode.Ignore;
                Add(LabelView);
            }
            else AddToClassList("acs-btn--icon");
            this.AddManipulator(new Clickable(() => { if (enabledInHierarchy) _onClick?.Invoke(); }));
            RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { _onClick?.Invoke(); e.StopPropagation(); }
            });
        }

        public void SetAction(Action a) => _onClick = a;
    }

    /// <summary>Small two-state text chip (Mute/Solo/Bypass).</summary>
    public class ToggleChip : VisualElement
    {
        private readonly Label _label;
        private bool _value;
        private readonly string _onClass;
        public event Action<bool> Changed;

        public bool Value
        {
            get => _value;
            set { _value = value; EnableInClassList(_onClass, value); }
        }

        public ToggleChip(string text, string onClass, string tooltipText)
        {
            _onClass = onClass;
            AddToClassList("acs-chip");
            tooltip = tooltipText;
            _label = new Label(text) { pickingMode = PickingMode.Ignore };
            Add(_label);
            this.AddManipulator(new Clickable(() => { Value = !Value; Changed?.Invoke(Value); }));
            RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
        }
    }

    /// <summary>Pill-shaped on/off switch.</summary>
    public class SwitchToggle : VisualElement
    {
        private bool _value;
        public event Action<bool> Changed;
        public bool Value
        {
            get => _value;
            set { _value = value; EnableInClassList("acs-switch--on", value); }
        }

        public SwitchToggle(bool value = false, string tooltipText = null)
        {
            AddToClassList("acs-switch");
            tooltip = tooltipText;
            var knob = new VisualElement { pickingMode = PickingMode.Ignore };
            knob.AddToClassList("acs-switch__knob");
            Add(knob);
            Value = value;
            focusable = true;
            this.AddManipulator(new Clickable(() => { Value = !Value; Changed?.Invoke(Value); }));
        }
    }

    /// <summary>Segmented control: mutually exclusive options.</summary>
    public class SegmentedControl : VisualElement
    {
        private readonly VisualElement[] _items;
        private int _index;
        public event Action<int> Changed;

        public int Index
        {
            get => _index;
            set
            {
                _index = value;
                for (int i = 0; i < _items.Length; i++) _items[i].EnableInClassList("acs-seg__item--on", i == value);
            }
        }

        public SegmentedControl(string[] options, int index)
        {
            AddToClassList("acs-seg");
            _items = new VisualElement[options.Length];
            for (int i = 0; i < options.Length; i++)
            {
                int k = i;
                var item = new Label(options[i]);
                item.AddToClassList("acs-seg__item");
                item.AddManipulator(new Clickable(() => { Index = k; Changed?.Invoke(k); }));
                _items[i] = item;
                Add(item);
            }
            Index = index;
        }
    }

    /// <summary>Card container with header (title + actions) and collapsible body.</summary>
    public class SectionCard : VisualElement
    {
        public readonly VisualElement Header;
        public readonly VisualElement Actions;
        public readonly VisualElement Body;
        private readonly IconElement _chevron;
        public readonly Label Title;

        public SectionCard(string title, bool collapsible = false, bool expanded = true, Color? accent = null)
        {
            AddToClassList("acs-card");
            Header = new VisualElement();
            Header.AddToClassList("acs-card__header");
            if (accent.HasValue)
            {
                var dot = new VisualElement();
                dot.AddToClassList("acs-card__dot");
                dot.style.backgroundColor = accent.Value;
                Header.Add(dot);
            }
            if (collapsible)
            {
                _chevron = new IconElement(expanded ? Icon.ChevronDown : Icon.ChevronRight, 14);
                Header.Add(_chevron);
            }
            Title = new Label(title);
            Title.AddToClassList("acs-card__title");
            Header.Add(Title);
            var spacer = new VisualElement { style = { flexGrow = 1 } };
            Header.Add(spacer);
            Actions = new VisualElement();
            Actions.AddToClassList("acs-card__actions");
            Header.Add(Actions);
            Add(Header);
            Body = new VisualElement();
            Body.AddToClassList("acs-card__body");
            Add(Body);
            if (collapsible)
            {
                SetExpanded(expanded);
                Title.AddManipulator(new Clickable(() => SetExpanded(Body.style.display == DisplayStyle.None)));
                _chevron.pickingMode = PickingMode.Position;
                _chevron.AddManipulator(new Clickable(() => SetExpanded(Body.style.display == DisplayStyle.None)));
            }
        }

        public event Action<bool> ExpandedChanged;

        public void SetExpanded(bool expanded)
        {
            Body.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;
            if (_chevron != null) _chevron.Icon = expanded ? Icon.ChevronDown : Icon.ChevronRight;
            EnableInClassList("acs-card--collapsed", !expanded);
            ExpandedChanged?.Invoke(expanded);
        }
    }

    public static class UIUtil
    {
        public static VisualElement Row(params VisualElement[] children)
        {
            var r = new VisualElement();
            r.AddToClassList("acs-row");
            foreach (var c in children) r.Add(c);
            return r;
        }

        public static VisualElement Spacer(float width = -1)
        {
            var s = new VisualElement();
            if (width < 0) s.style.flexGrow = 1; else s.style.width = width;
            return s;
        }

        public static VisualElement Divider()
        {
            var d = new VisualElement();
            d.AddToClassList("acs-divider");
            return d;
        }

        public static Label Label(string text, params string[] classes)
        {
            var l = new Label(text);
            foreach (var c in classes) l.AddToClassList(c);
            return l;
        }

        public static VisualElement EmptyState(Icon icon, string title, string body)
        {
            var e = new VisualElement();
            e.AddToClassList("acs-empty");
            var ic = new IconElement(icon, 36) { Color = StudioTheme.TextFaint };
            e.Add(ic);
            e.Add(Label(title, "acs-empty__title"));
            e.Add(Label(body, "acs-empty__body"));
            return e;
        }

        public static bool IsTextInputFocused(VisualElement root)
        {
            var fe = root?.panel?.focusController?.focusedElement as VisualElement;
            while (fe != null)
            {
                if (fe.ClassListContains("unity-base-text-field") || fe.ClassListContains("unity-text-input")
                    || fe.ClassListContains("unity-base-text-field__input")) return true;
                fe = fe.parent;
            }
            return false;
        }
    }
}
