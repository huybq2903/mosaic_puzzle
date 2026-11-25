using System;
using UnityEngine.UIElements;

namespace Falcon.Shared.EditorUi
{
    public enum Severity
    {
        Info,
        Ok,
        Warn,
        Error
    }

    public enum KitText
    {
        Title,
        Heading,
        Section,
        Sub,
        Body,
        Dim,
        Faint,
        Caption,
        Value
    }

    public enum KitGap
    {
        None,
        Xs,
        Sm,
        Md,
        Lg
    }

    public static partial class Kit
    {
        public const string RootClass = "ed-root";

        public static Action<VisualElement, string> TipHook;

        public static VisualElement PopupLayer;

        public static VisualElement El(VisualElement parent, string classes = null)
        {
            var element = new VisualElement();

            Classes(element, classes);
            Put(parent, element);

            return element;
        }

        public static VisualElement Row(VisualElement parent, KitGap gap = KitGap.Sm, string classes = null)
        {
            var row = El(parent, "vk-row " + GapClass(gap));

            Classes(row, classes);

            return row;
        }

        public static VisualElement Col(VisualElement parent, KitGap gap = KitGap.Sm, string classes = null)
        {
            var col = El(parent, "vk-col " + GapClass(gap));

            Classes(col, classes);

            return col;
        }

        public static VisualElement Spacer(VisualElement parent)
        {
            parent?.AddToClassList("vk-spaced");

            return El(parent, "vk-spacer");
        }

        public static VisualElement Divider(VisualElement parent, bool vertical = false)
        {
            return El(parent, vertical ? "vk-divider vk-divider-v" : "vk-divider");
        }

        public static Label Text(VisualElement parent, string text, KitText role = KitText.Body, bool wrap = false)
        {
            var label = new Label(text ?? "");

            label.AddToClassList("vk-text");
            label.AddToClassList(RoleClass(role));
            label.AddToClassList(wrap ? "vk-wrap" : "vk-clip");

            Put(parent, label);

            return label;
        }

        public static Label Note(VisualElement parent, string text, Severity severity)
        {
            var label = Text(parent, text, KitText.Dim, true);

            Tint(label, severity);

            return label;
        }

        public static void Tint(VisualElement element, Severity severity)
        {
            element.EnableInClassList("vk-sev-info", severity == Severity.Info);
            element.EnableInClassList("vk-sev-ok", severity == Severity.Ok);
            element.EnableInClassList("vk-sev-warn", severity == Severity.Warn);
            element.EnableInClassList("vk-sev-error", severity == Severity.Error);
        }

        public static void Tip(VisualElement element, string text)
        {
            if (element == null) return;

            var appTip = TipHook != null &&
                         (element.panel == null || ModalLayer == null || element.panel == ModalLayer.panel);

            if (appTip) TipHook(element, text);
            else element.tooltip = text ?? "";
        }

        public static void SetText(TextElement element, string text)
        {
            text ??= "";

            if (element != null && element.text != text) element.text = text;
        }

        public static void SetTextOrHide(TextElement element, string text)
        {
            var shown = !string.IsNullOrEmpty(text);

            SetShown(element, shown);

            if (shown) SetText(element, text);
        }

        public static void SetShown(VisualElement element, bool shown)
        {
            if (element == null) return;

            var wanted = shown ? DisplayStyle.Flex : DisplayStyle.None;

            if (element.style.display != wanted) element.style.display = wanted;
        }

        public static void SetOn(VisualElement element, bool on)
        {
            element?.EnableInClassList("vk-on", on);
        }

        public static void SetEnabled(VisualElement element, bool enabled)
        {
            if (element != null && element.enabledSelf != enabled) element.SetEnabled(enabled);
        }

        public static T Put<T>(VisualElement parent, T element) where T : VisualElement
        {
            if (parent == null) return element;

            if (parent.childCount > 0 && IsGapped(parent)) element.AddToClassList("vk-after");

            parent.Add(element);

            return element;
        }

        public static void Classes(VisualElement element, string classes)
        {
            if (element == null || string.IsNullOrEmpty(classes)) return;

            foreach (var name in classes.Split(' '))
                if (name.Length > 0)
                    element.AddToClassList(name);
        }

        public static bool Holds(VisualElement root, VisualElement child)
        {
            for (var walk = child; walk != null; walk = walk.parent)
                if (walk == root)
                    return true;

            return false;
        }

        private static bool IsGapped(VisualElement parent)
        {
            return parent.ClassListContains("vk-gap-xs") || parent.ClassListContains("vk-gap-sm") ||
                   parent.ClassListContains("vk-gap-md") || parent.ClassListContains("vk-gap-lg") ||
                   parent.ClassListContains("vk-group");
        }

        private static string GapClass(KitGap gap)
        {
            return gap switch
                   {
                       KitGap.Xs => "vk-gap-xs",
                       KitGap.Sm => "vk-gap-sm",
                       KitGap.Md => "vk-gap-md",
                       KitGap.Lg => "vk-gap-lg",
                       _ => "vk-gap-none"
                   };
        }

        private static string RoleClass(KitText role)
        {
            return role switch
                   {
                       KitText.Title => "vk-t-title",
                       KitText.Heading => "vk-t-heading",
                       KitText.Section => "vk-t-section",
                       KitText.Sub => "vk-t-sub",
                       KitText.Dim => "vk-t-dim",
                       KitText.Faint => "vk-t-faint",
                       KitText.Caption => "vk-t-caption",
                       KitText.Value => "vk-t-value",
                       _ => "vk-t-body"
                   };
        }
    }
}
