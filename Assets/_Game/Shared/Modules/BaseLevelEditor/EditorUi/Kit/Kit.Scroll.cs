using UnityEngine.UIElements;

namespace Falcon.Shared.EditorUi
{
    public static partial class Kit
    {
        public static ScrollView Scroll(VisualElement parent, string classes = null)
        {
            var scroll = new ScrollView(ScrollViewMode.Vertical);

            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;

            Classes(scroll, classes);
            GrabBar(scroll.verticalScroller);

            return Put(parent, scroll);
        }

        private static void GrabBar(Scroller bar)
        {
            var knob = bar.Q(className: "unity-base-slider__dragger");

            if (knob == null) return;

            var grabValue = 0f;
            var grabY = 0f;
            var span = 0f;

            bar.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;

                var track = knob.parent.worldBound;
                var box = knob.worldBound;

                span = track.height - box.height;

                if (span <= 1f) return;

                var range = bar.highValue - bar.lowValue;
                var point = evt.position.y;

                if (point < box.yMin || point > box.yMax)
                    bar.value = bar.lowValue + (point - track.yMin - box.height * 0.5f) / span * range;

                grabValue = bar.value;
                grabY = point;

                bar.CapturePointer(evt.pointerId);
                evt.StopPropagation();
            }, TrickleDown.TrickleDown);

            bar.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!bar.HasPointerCapture(evt.pointerId) || span <= 1f) return;

                bar.value = grabValue + (evt.position.y - grabY) / span * (bar.highValue - bar.lowValue);
                evt.StopPropagation();
            });

            bar.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!bar.HasPointerCapture(evt.pointerId)) return;

                bar.ReleasePointer(evt.pointerId);
                evt.StopPropagation();
            });
        }
    }
}
