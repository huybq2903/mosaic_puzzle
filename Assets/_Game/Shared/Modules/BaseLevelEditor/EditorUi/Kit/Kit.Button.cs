using System;
using UnityEngine.UIElements;

namespace Falcon.Shared.EditorUi
{
    public enum KitTone
    {
        Neutral,
        Primary,
        Subtle,
        Danger
    }

    public enum KitSize
    {
        Sm,
        Md,
        Lg
    }

    public static partial class Kit
    {
        public static Button Button(VisualElement parent, string label, Action action,
            KitTone tone = KitTone.Neutral, KitSize size = KitSize.Md, string tip = null)
        {
            var button = new Button(action) { text = label ?? "", focusable = false };

            button.AddToClassList("vk-btn");
            button.AddToClassList(ToneClass(tone));
            button.AddToClassList(SizeClass(size));

            Put(parent, button);

            if (!string.IsNullOrEmpty(tip)) Tip(button, tip);

            return button;
        }

        public static Button Primary(VisualElement parent, string label, Action action, string tip = null)
        {
            return Button(parent, label, action, KitTone.Primary, KitSize.Md, tip);
        }

        public static Button Subtle(VisualElement parent, string label, Action action, string tip = null)
        {
            return Button(parent, label, action, KitTone.Subtle, KitSize.Md, tip);
        }

        public static Button Danger(VisualElement parent, string label, Action action, string tip = null)
        {
            return Button(parent, label, action, KitTone.Danger, KitSize.Md, tip);
        }

        public static Button IconButton(VisualElement parent, KitGlyph glyph, string tip, Action action,
            KitTone tone = KitTone.Subtle, KitSize size = KitSize.Md)
        {
            var button = Button(parent, "", action, tone, size, tip);

            button.AddToClassList("vk-icon-btn");
            Icon(button, glyph);

            return button;
        }

        public static Button Close(VisualElement parent, Action action, string tip = "Đóng",
            KitSize size = KitSize.Sm)
        {
            return DangerHover(IconButton(parent, KitGlyph.Close, tip, action, KitTone.Subtle, size));
        }

        public static Button Trash(VisualElement parent, Action action, string tip = null, KitSize size = KitSize.Md)
        {
            return DangerHover(IconButton(parent, KitGlyph.Trash, tip, action, KitTone.Subtle, size));
        }

        private static Button DangerHover(Button button)
        {
            button.AddToClassList("vk-hover-danger");

            return button;
        }

        public static Button Toggle(VisualElement parent, string label, bool on, Action<bool> onChange,
            KitSize size = KitSize.Md, string tip = null)
        {
            Button button = null;

            button = Button(parent, label, () =>
            {
                var next = !button.ClassListContains("vk-on");

                SetOn(button, next);
                onChange?.Invoke(next);
            }, KitTone.Neutral, size, tip);

            button.AddToClassList("vk-toggle");
            SetOn(button, on);

            return button;
        }

        public static VisualElement Group(VisualElement parent, string classes = null)
        {
            var group = El(parent, "vk-group");

            Classes(group, classes);

            return group;
        }

        private sealed class BusyMemo
        {
            public string Label;
            public bool Enabled;
        }

        public static void SetBusy(Button button, bool busy, string busyLabel = null)
        {
            if (button == null || button.ClassListContains("vk-busy") == busy) return;

            button.EnableInClassList("vk-busy", busy);

            if (busy)
            {
                button.userData = new BusyMemo { Label = button.text, Enabled = button.enabledSelf };
                button.SetEnabled(false);

                if (busyLabel != null) button.text = busyLabel;

                return;
            }

            if (button.userData is not BusyMemo memo) return;

            button.text = memo.Label;
            button.SetEnabled(memo.Enabled);
            button.userData = null;
        }

        public static string ToneClass(KitTone tone)
        {
            return tone switch
                   {
                       KitTone.Primary => "vk-primary",
                       KitTone.Subtle => "vk-subtle",
                       KitTone.Danger => "vk-danger",
                       _ => "vk-neutral"
                   };
        }

        public static string SizeClass(KitSize size)
        {
            return size switch
                   {
                       KitSize.Sm => "vk-sm",
                       KitSize.Lg => "vk-lg",
                       _ => "vk-md"
                   };
        }
    }
}
