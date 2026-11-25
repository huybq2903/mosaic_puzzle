using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Falcon.Shared.EditorUi
{
    public sealed class KitSwatch
    {
        public VisualElement Root;
        public VisualElement High;
        public VisualElement Low;

        private Color high = Color.clear;
        private Color low = Color.clear;
        private bool split;

        public void Paint(Color color)
        {
            Paint(color, color);
        }

        public void Paint(Color first, Color second)
        {
            var wantSplit = first != second;

            if (first == high && second == low && wantSplit == split) return;

            high = first;
            low = second;
            split = wantSplit;

            High.style.backgroundColor = first;
            Low.style.backgroundColor = second;
            Kit.SetShown(Low, wantSplit);
        }
    }

    public static partial class Kit
    {
        public static Button Chip(VisualElement parent, string label, Action action, string tip = null)
        {
            var chip = Button(parent, label, action, KitTone.Neutral, KitSize.Sm, tip);

            chip.AddToClassList("vk-chip");

            return chip;
        }

        public static Button ToggleChip(VisualElement parent, string label, bool on, Action<bool> onChange,
            string tip = null)
        {
            var chip = Toggle(parent, label, on, onChange, KitSize.Sm, tip);

            chip.AddToClassList("vk-chip");

            return chip;
        }

        public static Label Badge(VisualElement parent, string text, Severity severity = Severity.Info)
        {
            var badge = Text(parent, text, KitText.Caption);

            badge.AddToClassList("vk-badge");
            Tint(badge, severity);

            return badge;
        }

        public static void SetBadge(Label badge, string text, Severity severity)
        {
            SetTextOrHide(badge, text);
            Tint(badge, severity);
        }

        public static VisualElement Dot(VisualElement parent, Severity severity)
        {
            var dot = El(parent, "vk-dot");

            Tint(dot, severity);

            return dot;
        }

        public static KitSwatch Swatch(VisualElement parent, KitSize size = KitSize.Md)
        {
            var swatch = new KitSwatch { Root = El(parent, "vk-swatch " + SizeClass(size)) };

            swatch.Root.pickingMode = PickingMode.Ignore;
            swatch.High = El(swatch.Root, "vk-swatch-half");
            swatch.Low = El(swatch.Root, "vk-swatch-half");

            SetShown(swatch.Low, false);

            return swatch;
        }

        public static KitSwatch SwatchLabel(VisualElement parent, Color color, string name, out Label label)
        {
            var row = Row(parent, KitGap.Xs, "vk-swatch-line");
            var swatch = Swatch(row, KitSize.Sm);

            swatch.Paint(color);
            label = Text(row, name, KitText.Dim);

            return swatch;
        }
    }
}
