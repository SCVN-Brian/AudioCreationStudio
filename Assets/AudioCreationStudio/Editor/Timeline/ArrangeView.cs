using System;
using AudioCreationStudio.Models;
using UnityEngine;
using UnityEngine.UIElements;

namespace AudioCreationStudio.EditorUI
{
    /// <summary>Track headers + ruler + canvas + scrollbars + zoom controls, kept in sync.</summary>
    public sealed class ArrangeView : VisualElement
    {
        private readonly StudioContext _ctx;
        public readonly TrackHeaderPanel Headers;
        public readonly TimelineRuler Ruler;
        public readonly TimelineCanvas Canvas;
        private readonly Scroller _hScroll;
        private readonly Scroller _vScroll;
        private readonly VisualElement _empty;
        private readonly Label _zoomLabel;
        private bool _syncing;

        private TimelineViewState V => _ctx.Session.View;

        public ArrangeView(StudioContext ctx)
        {
            _ctx = ctx;
            AddToClassList("acs-arrange");
            var split = new TwoPaneSplitView(0, 248, TwoPaneSplitViewOrientation.Horizontal);
            split.AddToClassList("acs-split");
            Add(split);

            Headers = new TrackHeaderPanel(ctx);
            split.Add(Headers);

            var right = new VisualElement();
            right.AddToClassList("acs-timeline");
            split.Add(right);

            Ruler = new TimelineRuler(ctx);
            right.Add(Ruler);

            var mid = new VisualElement();
            mid.AddToClassList("acs-timeline__mid");
            right.Add(mid);
            var viewport = new VisualElement();
            viewport.AddToClassList("acs-timeline__viewport");
            mid.Add(viewport);
            Canvas = new TimelineCanvas(ctx);
            viewport.Add(Canvas);
            Canvas.ViewChanged += OnViewChanged;

            _empty = UIUtil.EmptyState(Icon.Waveform, "Start a new sound",
                "Drag AudioClips here from the Project window, use Import, or right-click to insert a generated sound.");
            _empty.pickingMode = PickingMode.Ignore;
            viewport.Add(_empty);

            _vScroll = new Scroller(0, 100, v => { if (_syncing) return; V.scrollY = v; OnViewChanged(); }, SliderDirection.Vertical);
            _vScroll.AddToClassList("acs-vscroll");
            mid.Add(_vScroll);

            var bottom = new VisualElement();
            bottom.AddToClassList("acs-timeline__bottom");
            _hScroll = new Scroller(0, 100, v => { if (_syncing) return; V.scrollSeconds = v; OnViewChanged(); }, SliderDirection.Horizontal);
            _hScroll.AddToClassList("acs-hscroll");
            bottom.Add(_hScroll);
            _zoomLabel = UIUtil.Label("", "acs-label-dim", "acs-zoomlabel");
            bottom.Add(_zoomLabel);
            bottom.Add(new StudioButton(Icon.None, "−", () => { V.ZoomAt(Canvas.contentRect.width * 0.5f, 1 / 1.5); OnViewChanged(); }, "Zoom out (⌘ + scroll)", "acs-btn--xs"));
            bottom.Add(new StudioButton(Icon.None, "+", () => { V.ZoomAt(Canvas.contentRect.width * 0.5f, 1.5); OnViewChanged(); }, "Zoom in (⌘ + scroll)", "acs-btn--xs"));
            bottom.Add(new StudioButton(Icon.None, "Fit", ZoomToFit, "Zoom to fit the whole recipe", "acs-btn--xs"));
            right.Add(bottom);

            RegisterCallback<GeometryChangedEvent>(_ => UpdateScrollers());
        }

        public void ZoomToFit()
        {
            long end = Math.Max(_ctx.Data.ContentEnd, _ctx.Data.sampleRate / 2);
            double seconds = end / (double)_ctx.Data.sampleRate;
            float w = Mathf.Max(100, Canvas.contentRect.width - 40);
            V.pixelsPerSecond = Math.Max(TimelineViewState.MinPps, Math.Min(TimelineViewState.MaxPps, w / seconds));
            V.scrollSeconds = 0;
            OnViewChanged();
        }

        private void OnViewChanged()
        {
            _ctx.Session.Notify(StudioChange.View);
        }

        public void RefreshAll(StudioChange change)
        {
            if ((change & (StudioChange.Structure | StudioChange.Recipe)) != 0) Headers.Rebuild();
            else if ((change & (StudioChange.Content | StudioChange.Selection)) != 0) Headers.RefreshValues();
            Headers.SyncScroll();
            Canvas.Refresh();
            Ruler.Refresh();
            UpdateScrollers();
            bool empty = true;
            foreach (var t in _ctx.Data.tracks) if (t.clips.Count > 0) { empty = false; break; }
            _empty.style.display = empty ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void RefreshPlayhead()
        {
            Canvas.MarkDirtyRepaint();
            Ruler.MarkDirtyRepaint();
        }

        /// <summary>Keeps the playhead visible during playback by paging the view.</summary>
        public void FollowPlayhead()
        {
            float x = V.X(_ctx.Session.Playhead, _ctx.Data.sampleRate);
            float w = Canvas.contentRect.width;
            if (w <= 0) return;
            if (x > w - 20 || x < 0)
            {
                V.scrollSeconds = Math.Max(0, _ctx.Session.Playhead / (double)_ctx.Data.sampleRate - 20 / V.pixelsPerSecond);
                OnViewChanged();
            }
        }

        private void UpdateScrollers()
        {
            _syncing = true;
            double contentSec = Math.Max(_ctx.Data.ContentEnd / (double)_ctx.Data.sampleRate + 2.0, 4.0);
            double visible = Canvas.contentRect.width / V.pixelsPerSecond;
            _hScroll.lowValue = 0;
            _hScroll.highValue = (float)Math.Max(0.0001, contentSec - visible * 0.5);
            _hScroll.Adjust(Mathf.Clamp01((float)(visible / (contentSec + visible))));
            _hScroll.value = (float)V.scrollSeconds;
            float ch = TimelineViewState.ContentHeight(_ctx.Data) + 40;
            float vh = Canvas.contentRect.height;
            _vScroll.lowValue = 0;
            _vScroll.highValue = Mathf.Max(0.0001f, ch - vh);
            _vScroll.Adjust(vh <= 0 ? 1 : Mathf.Clamp01(vh / ch));
            _vScroll.style.display = ch > vh ? DisplayStyle.Flex : DisplayStyle.None;
            if (V.scrollY > Mathf.Max(0, ch - vh)) V.scrollY = Mathf.Max(0, ch - vh);
            _vScroll.value = V.scrollY;
            _zoomLabel.text = V.pixelsPerSecond >= 1000 ? $"{V.pixelsPerSecond / 1000:0.#}k px/s" : $"{V.pixelsPerSecond:0} px/s";
            _syncing = false;
        }
    }
}
