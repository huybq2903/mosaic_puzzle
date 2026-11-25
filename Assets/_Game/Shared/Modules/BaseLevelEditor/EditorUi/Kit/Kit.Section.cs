using UnityEngine.UIElements;

namespace Falcon.Shared.EditorUi
{
    public sealed class KitHead
    {
        public VisualElement Root;
        public Label Title;
        public Label Summary;
        public VisualElement Actions;

        public void SetTitle(string title)
        {
            Kit.SetText(Title, (title ?? "").ToUpperInvariant());
        }

        public void SetSummary(string text)
        {
            Kit.SetTextOrHide(Summary, text);
        }
    }

    public sealed class KitSection
    {
        public VisualElement Root;
        public KitHead Head;
        public VisualElement Body;

        public Label Title => Head.Title;
        public VisualElement Actions => Head.Actions;

        public void SetSummary(string text)
        {
            Head.SetSummary(text);
        }
    }

    public static partial class Kit
    {
        public static KitHead Head(VisualElement parent, string title)
        {
            var head = new KitHead { Root = Row(parent, KitGap.Sm, "vk-section-head") };

            head.Title = Text(head.Root, "", KitText.Section);
            head.SetTitle(title);
            head.Summary = Text(head.Root, "", KitText.Faint);
            head.Summary.AddToClassList("vk-section-summary");
            Spacer(head.Root);
            head.Actions = Row(head.Root, KitGap.Xs, "vk-section-actions");

            SetShown(head.Summary, false);

            return head;
        }

        public static KitSection Section(VisualElement parent, string title, bool card = true)
        {
            var section = new KitSection { Root = El(parent, card ? "vk-section vk-card" : "vk-section") };

            section.Head = Head(section.Root, title);
            section.Body = Col(section.Root, KitGap.Sm, "vk-section-body");

            return section;
        }

        public static VisualElement Panel(VisualElement parent, string classes = null)
        {
            var panel = El(parent, "vk-panel");

            Classes(panel, classes);

            return panel;
        }

        public static VisualElement PanelHead(VisualElement parent, string title, out Label label)
        {
            var head = Row(parent, KitGap.Sm, "vk-panel-head");

            label = Text(head, title, KitText.Heading);
            Spacer(head);

            return head;
        }
    }
}
