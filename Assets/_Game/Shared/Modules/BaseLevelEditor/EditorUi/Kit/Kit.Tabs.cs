using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Falcon.Shared.EditorUi
{
    public sealed class KitTabs
    {
        public VisualElement Root;
        public VisualElement Strip;
        public VisualElement Pages;
        public Action<int> OnChange;

        public readonly List<Button> Heads = new();
        public readonly List<VisualElement> Bodies = new();

        public int Current { get; private set; } = -1;

        public VisualElement Add(string label, string tip = null)
        {
            var slot = Heads.Count;
            var head = Kit.Button(Strip, label, () => Select(slot, true), KitTone.Subtle, KitSize.Md, tip);

            head.AddToClassList("vk-tab");
            Heads.Add(head);

            var body = Kit.Col(Pages, KitGap.Sm, "vk-tab-page");

            Bodies.Add(body);
            Kit.SetShown(body, false);

            if (Current < 0) Select(0, false);

            return body;
        }

        public void Select(int index, bool notify = false)
        {
            if (index < 0 || index >= Heads.Count || index == Current) return;

            Current = index;

            for (var slot = 0; slot < Heads.Count; slot++)
            {
                Kit.SetOn(Heads[slot], slot == index);
                Kit.SetShown(Bodies[slot], slot == index);
            }

            if (notify) OnChange?.Invoke(index);
        }

        public void SetLabel(int index, string label)
        {
            if (index >= 0 && index < Heads.Count) Kit.SetText(Heads[index], label);
        }
    }

    public static partial class Kit
    {
        public static KitTabs Tabs(VisualElement parent, Action<int> onChange = null)
        {
            var tabs = new KitTabs { Root = Col(parent, KitGap.None, "vk-tabs"), OnChange = onChange };

            tabs.Strip = Row(tabs.Root, KitGap.None, "vk-tab-strip");
            tabs.Pages = Col(tabs.Root, KitGap.None, "vk-tab-pages");

            return tabs;
        }

        public static Button[] TabStrip(VisualElement parent, string[] labels, int current, Action<int> onPick)
        {
            var strip = Row(parent, KitGap.None, "vk-tab-strip");
            var tabs = new Button[labels.Length];

            for (var index = 0; index < labels.Length; index++)
            {
                var slot = index;

                tabs[index] = Button(strip, labels[index], () => onPick(slot), KitTone.Subtle, KitSize.Md);
                tabs[index].AddToClassList("vk-tab");
            }

            SyncSegmented(tabs, current);

            return tabs;
        }

        public static Button[] Segmented(VisualElement parent, string[] labels, int current, Action<int> onPick)
        {
            var strip = Row(parent, KitGap.None, "vk-seg");
            var cells = new Button[labels.Length];

            for (var index = 0; index < labels.Length; index++)
            {
                var slot = index;

                cells[index] = Button(strip, labels[index], () => onPick(slot), KitTone.Subtle, KitSize.Sm);
                cells[index].AddToClassList("vk-seg-cell");
            }

            SyncSegmented(cells, current);

            return cells;
        }

        public static void SyncSegmented(Button[] cells, int current)
        {
            for (var index = 0; index < cells.Length; index++) SetOn(cells[index], index == current);
        }
    }
}
