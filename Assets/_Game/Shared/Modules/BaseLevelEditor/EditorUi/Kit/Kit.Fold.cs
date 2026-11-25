using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Falcon.Shared.EditorUi
{
    public sealed class KitFold
    {
        public VisualElement Root;
        public VisualElement Head;
        public VisualElement Body;
        public VisualElement Arrow;
        public Label Title;
        public Label Summary;
        public VisualElement Actions;

        private ScrollView holder;
        private Action<float> onHolderScroll;
        private float headSlide = -1f;

        public bool IsOpen => Root.ClassListContains("vk-open");

        public void SetOpen(bool open)
        {
            Root.EnableInClassList("vk-open", open);

            PinHead();
        }

        public void SetTitle(string title)
        {
            Kit.SetText(Title, (title ?? "").ToUpperInvariant());
        }

        public void SetSummary(string text)
        {
            Kit.SetText(Summary, text);
        }

        public void PinHead()
        {
            var view = Holder()?.contentViewport;

            if (view == null) return;

            var card = Root.worldBound;
            var room = Mathf.Max(0f, card.height - Head.worldBound.height);
            var slide = Mathf.Clamp(view.worldBound.yMin - card.yMin, 0f, room);

            if (Mathf.Approximately(headSlide, slide)) return;

            headSlide = slide;
            Head.style.translate = new Translate(0f, slide);
        }

        public void DropHolder()
        {
            if (holder != null && onHolderScroll != null) holder.verticalScroller.valueChanged -= onHolderScroll;

            holder = null;
            onHolderScroll = null;
            headSlide = -1f;
        }

        private ScrollView Holder()
        {
            if (holder != null) return holder;

            for (var walk = Root.parent; walk != null; walk = walk.parent)
                if (walk is ScrollView found)
                {
                    holder = found;
                    break;
                }

            if (holder == null) return null;

            onHolderScroll = _ => PinHead();
            holder.verticalScroller.valueChanged += onHolderScroll;

            return holder;
        }

        internal bool ClickIsOnControl(VisualElement target)
        {
            if (Kit.Holds(Actions, target)) return true;

            for (var walk = target; walk != null && walk != Head; walk = walk.parent)
                if (walk is Button || walk is Toggle || walk is TextField)
                    return true;

            return false;
        }
    }

    public static partial class Kit
    {
        public static VisualElement Card(VisualElement parent, string title = null)
        {
            var card = El(parent, "vk-card");

            if (!string.IsNullOrEmpty(title)) Head(card, title);

            return card;
        }

        public static KitFold Fold(VisualElement parent, string title, bool open, Action<bool> onToggle)
        {
            var fold = new KitFold { Root = El(parent, "vk-card vk-fold") };

            fold.Body = Col(fold.Root, KitGap.Sm, "vk-fold-body");

            fold.Head = Row(fold.Root, KitGap.Sm, "vk-fold-head");
            fold.Head.tooltip = title;
            fold.Arrow = Icon(fold.Head, KitGlyph.ChevronRight);
            fold.Arrow.AddToClassList("vk-fold-arrow");

            fold.Title = Text(fold.Head, "", KitText.Section);
            fold.SetTitle(title);
            Spacer(fold.Head);

            fold.Summary = Text(fold.Head, "", KitText.Faint);
            fold.Summary.AddToClassList("vk-fold-summary");

            fold.Actions = Row(fold.Head, KitGap.Xs, "vk-fold-actions");

            fold.Root.RegisterCallback<GeometryChangedEvent>(_ => fold.PinHead());
            fold.Root.RegisterCallback<AttachToPanelEvent>(_ => fold.PinHead());
            fold.Root.RegisterCallback<DetachFromPanelEvent>(_ => fold.DropHolder());

            fold.SetOpen(open);

            fold.Head.RegisterCallback<ClickEvent>(evt =>
            {
                if (fold.ClickIsOnControl(evt.target as VisualElement)) return;

                var next = !fold.IsOpen;

                fold.SetOpen(next);
                onToggle?.Invoke(next);
            });

            return fold;
        }

        public static Toggle Check(VisualElement parent, string label, bool value, Action<bool> onChange)
        {
            // chữ đứng sau ô tick (text, không phải label của field) để dòng tick thẳng hàng bên trái như các ô khác
            var toggle = new Toggle { text = label, value = value, focusable = false };

            toggle.AddToClassList("vk-check");

            if (onChange != null) toggle.RegisterValueChangedCallback(evt => onChange(evt.newValue));

            return Put(parent, toggle);
        }

        public static SliderInt Slider(VisualElement parent, string label, int value, int min, int max,
            Action<int> onChange, bool input = true)
        {
            var slider = new SliderInt(label, min, max) { value = value, showInputField = input };

            slider.AddToClassList("vk-slider");

            if (onChange != null) slider.RegisterValueChangedCallback(evt => onChange(evt.newValue));

            return Put(parent, slider);
        }

        public static Slider SliderFloat(VisualElement parent, string label, float value, float min, float max,
            Action<float> onChange, bool input = true)
        {
            var slider = new Slider(label, min, max) { value = value, showInputField = input };

            slider.AddToClassList("vk-slider");

            if (onChange != null) slider.RegisterValueChangedCallback(evt => onChange(evt.newValue));

            return Put(parent, slider);
        }

        public static void SyncValue<T>(BaseField<T> field, T value)
        {
            if (field == null || System.Collections.Generic.EqualityComparer<T>.Default.Equals(field.value, value))
                return;

            var focused = field.focusController?.focusedElement as VisualElement;

            if (focused != null && Holds(field, focused)) return;

            field.SetValueWithoutNotify(value);
            if (field is TextField text) ClearPlaceholderLook(text);
        }

        /// <summary>
        /// Unity giữ kiểu chữ gợi ý (nghiêng, mờ) trên ô có placeholder khi giá trị được đặt bằng code — bỏ kiểu đó khi ô đã có chữ.
        /// </summary>
        public static void ClearPlaceholderLook(TextField field)
        {
            if (string.IsNullOrEmpty(field.value)) return;
            field.Q(className: "unity-base-text-field__input")?.RemoveFromClassList("unity-base-text-field__input--placeholder");
        }

        public static Button HelpTip(VisualElement parent, string tip)
        {
            var mark = IconButton(parent, KitGlyph.Help, tip, null, KitTone.Subtle, KitSize.Sm);

            mark.AddToClassList("vk-help");
            mark.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());

            return mark;
        }

        public static Button IconButton(VisualElement parent, Action<Painter2D, Rect> draw, string tip, Action action,
            KitTone tone = KitTone.Subtle, KitSize size = KitSize.Md)
        {
            var button = Button(parent, "", action, tone, size, tip);

            button.AddToClassList("vk-icon-btn");
            Icon(button, draw);

            return button;
        }
    }
}
