using UnityEngine.UIElements;

namespace Falcon.Shared.EditorUi
{
    public sealed class KitCallout
    {
        public VisualElement Root;
        public VisualElement Bar;
        public Label Text;

        public void Set(string text, Severity severity)
        {
            var shown = !string.IsNullOrEmpty(text);

            Kit.SetShown(Root, shown);

            if (!shown) return;

            Kit.SetText(Text, text);
            SetSeverity(severity);
        }

        public void SetSeverity(Severity severity)
        {
            Kit.Tint(Root, severity);
        }
    }

    public static partial class Kit
    {
        public static KitCallout Callout(VisualElement parent, Severity severity, string classes = null,
            KitText role = KitText.Body)
        {
            var callout = new KitCallout { Root = El(parent, "vk-callout vk-surface") };

            Classes(callout.Root, classes);

            callout.Bar = El(callout.Root, "vk-callout-bar");
            callout.Bar.pickingMode = PickingMode.Ignore;
            callout.Text = Text(callout.Root, "", role, true);
            callout.Text.AddToClassList("vk-callout-text");
            callout.SetSeverity(severity);

            return callout;
        }
    }
}
