using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AudioCreationStudio.EditorUI
{
    /// <summary>
    /// "AI Generate" tab: the ElevenLabs prompt form on the left, the folder-organised take library on the right,
    /// separated by a draggable splitter. The form starts at half the width; the chosen ratio is remembered and kept
    /// when the window is resized. The procedural synth lives in its own "Custom Generate" tab.
    /// </summary>
    public sealed class AIGenerateView : VisualElement
    {
        private const string RatioPref = "ACS.AIGenerate.FormRatio";
        private const float DefaultRatio = 0.5f;
        private const float MinForm = 360f, MinLibrary = 460f;

        public readonly ElevenLabsPanel Panel;
        private readonly TwoPaneSplitView _split;
        private readonly VisualElement _formPane;
        private float _ratio;
        private float _lastTotal = -1f;
        private bool _dragging;

        public AIGenerateView(StudioContext ctx)
        {
            AddToClassList("acs-aiview");
            _ratio = Mathf.Clamp(EditorPrefs.GetFloat(RatioPref, DefaultRatio), 0.25f, 0.75f);

            _split = new TwoPaneSplitView(0, 600, TwoPaneSplitViewOrientation.Horizontal);
            _split.AddToClassList("acs-aiview__split");

            _formPane = new VisualElement();
            _formPane.AddToClassList("acs-aiview__formpane");
            var form = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            form.AddToClassList("acs-aiview__form");
            Panel = new ElevenLabsPanel(ctx);
            form.Add(Panel);
            _formPane.Add(form);
            _split.Add(_formPane);

            var library = new VisualElement();
            library.AddToClassList("acs-aiview__library");
            library.Add(Panel.Browser);
            _split.Add(library);
            Add(_split);

            // Window resize → keep the ratio. The ratio is only recorded when the user releases the splitter,
            // so the split view's own first-layout sizing never overwrites it.
            _split.RegisterCallback<GeometryChangedEvent>(_ => ApplyRatio());
            RegisterCallback<PointerDownEvent>(e => _dragging = IsDragLine(e.target as VisualElement), TrickleDown.TrickleDown);
            RegisterCallback<PointerUpEvent>(_ =>
            {
                if (!_dragging) return;
                _dragging = false;
                schedule.Execute(() =>
                {
                    float total = _split.layout.width, w = _formPane.layout.width;
                    if (float.IsNaN(total) || total <= 0 || float.IsNaN(w) || w <= 0) return;
                    _ratio = Mathf.Clamp(w / total, 0.2f, 0.8f);
                    EditorPrefs.SetFloat(RatioPref, _ratio);
                });
            }, TrickleDown.TrickleDown);
            // Double-click the splitter to go back to half / half.
            RegisterCallback<MouseDownEvent>(e =>
            {
                if (e.clickCount == 2 && IsDragLine(e.target as VisualElement))
                {
                    _ratio = DefaultRatio;
                    EditorPrefs.SetFloat(RatioPref, _ratio);
                    _lastTotal = -1f;
                    ApplyRatio();
                }
            }, TrickleDown.TrickleDown);
        }

        private static bool IsDragLine(VisualElement t)
        {
            for (; t != null; t = t.parent)
                if (t.ClassListContains("unity-two-pane-split-view__dragline-anchor")) return true;
            return false;
        }

        private void ApplyRatio()
        {
            float total = _split.layout.width;
            if (float.IsNaN(total) || total <= 0 || Mathf.Abs(total - _lastTotal) < 0.5f) return;
            _lastTotal = total;
            float max = Mathf.Max(MinForm, total - MinLibrary);
            float w = Mathf.Clamp(Mathf.Round(total * _ratio), MinForm, max);
            _split.fixedPaneInitialDimension = w; // the split view's own first-layout sizing uses this
            _formPane.style.width = w;
        }
    }
}
