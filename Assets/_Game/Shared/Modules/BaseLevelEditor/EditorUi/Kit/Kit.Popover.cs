using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Falcon.Shared.EditorUi
{
    public sealed class KitPopover : IKitLayer
    {
        public VisualElement Root;
        public VisualElement Anchor;
        public Action OnClose;
        public KitSide Side = KitSide.Below;
        public float Gap = 4f;
        public int CloseButtons = KitMouse.Left;
        public bool CloseOnScroll = true;

        private EventCallback<PointerDownEvent> outsideWatch;
        private IPanel watchedPanel;
        private readonly List<ScrollView> watchedScrolls = new();
        private Action<float> onScroll;

        public bool IsOpen { get; private set; }
        public int OpenedFrame { get; private set; } = -1;
        public bool Blocking => false;
        public bool Floating => true;
        public bool TakesEscape { get; set; } = true;

        public bool ClosesOn(int button)
        {
            return button >= 0 && button < 3 && (CloseButtons & (1 << button)) != 0;
        }

        VisualElement IKitLayer.Root => Root;

        public void Open(VisualElement anchor)
        {
            Anchor = anchor ?? Anchor;
            OpenedFrame = UnityEngine.Time.frameCount;

            if (!IsOpen)
            {
                IsOpen = true;
                Kit.SetShown(Root, true);
                Root.BringToFront();
                Kit.Opened(this);
                WatchOutside();
                WatchScrolls();
            }

            Root.schedule.Execute(Place);
        }

        public void Close()
        {
            if (!IsOpen) return;

            IsOpen = false;
            Kit.SetShown(Root, false);
            Kit.Closed(this);
            UnwatchOutside();
            UnwatchScrolls();
            OnClose?.Invoke();
        }

        public void Toggle(VisualElement anchor)
        {
            if (IsOpen) Close();
            else Open(anchor);
        }

        public void Dismiss()
        {
            Close();
        }

        public void Place()
        {
            if (IsOpen) Kit.Place(Root, Anchor, Root.parent, Gap, Side);
        }

        private void WatchOutside()
        {
            watchedPanel = Root.panel;

            if (watchedPanel == null) return;

            outsideWatch ??= evt =>
            {
                var target = evt.target as VisualElement;

                if (!ClosesOn(evt.button)) return;
                if (Kit.Holds(Root, target) || Kit.Holds(Anchor, target)) return;

                Close();
            };

            watchedPanel.visualTree.RegisterCallback(outsideWatch, TrickleDown.TrickleDown);
        }

        private void UnwatchOutside()
        {
            watchedPanel?.visualTree.UnregisterCallback(outsideWatch, TrickleDown.TrickleDown);
            watchedPanel = null;
        }

        private void WatchScrolls()
        {
            if (!CloseOnScroll) return;

            onScroll ??= _ => Close();

            for (var walk = Anchor?.parent; walk != null; walk = walk.parent)
                if (walk is ScrollView scroll && !Kit.Holds(Root, scroll))
                {
                    scroll.verticalScroller.valueChanged += onScroll;
                    watchedScrolls.Add(scroll);
                }
        }

        private void UnwatchScrolls()
        {
            foreach (var scroll in watchedScrolls) scroll.verticalScroller.valueChanged -= onScroll;

            watchedScrolls.Clear();
        }
    }

    public sealed class KitDropdown
    {
        public Button Trigger;
        public Label Text;
        public KitPopover Popover;
        public KitSearch Search;
        public VisualElement List;
        public Action<int> OnPick;
        public Action<VisualElement> CustomFill;
        public Func<int, string> ItemTip;
        public string Empty = "Không có mục nào khớp";

        public readonly List<string> Labels = new();

        public int Current { get; private set; } = -1;

        public void SetItems(IEnumerable<string> labels, int current = -1)
        {
            Labels.Clear();
            Labels.AddRange(labels);
            SetCurrent(current);

            if (Popover.IsOpen) Fill();
        }

        public void SetCurrent(int index)
        {
            Current = index;
            Kit.SetText(Text, index >= 0 && index < Labels.Count ? Labels[index] : "—");
        }

        public void Open()
        {
            Search?.SetQuery("");
            Fill();
            Popover.Open(Trigger);

            if (Search != null) Popover.Root.schedule.Execute(() => Search.Field.Focus());
        }

        public void Refresh()
        {
            if (Popover.IsOpen) Fill();
        }

        internal void Fill()
        {
            var query = Search?.Query.Trim() ?? "";
            var shown = 0;

            List.Clear();

            if (CustomFill != null)
            {
                CustomFill(List);
                return;
            }

            for (var index = 0; index < Labels.Count; index++)
            {
                if (query.Length > 0 && Labels[index].IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                var slot = index;
                var item = Kit.Button(List, Labels[index], () => Pick(slot), KitTone.Subtle, KitSize.Sm);

                item.AddToClassList("vk-menu-item");
                Kit.SetOn(item, index == Current);

                var tip = ItemTip?.Invoke(index);

                if (!string.IsNullOrEmpty(tip)) Kit.Tip(item, tip);

                shown++;
            }

            if (shown == 0) Kit.Text(List, Empty, KitText.Faint).AddToClassList("vk-menu-empty");
        }

        private void Pick(int index)
        {
            SetCurrent(index);
            Popover.Close();
            OnPick?.Invoke(index);
        }
    }

    public sealed class KitMenu
    {
        public KitPopover Popover;
        public VisualElement List;

        public Button Item(string label, Action action, KitTone tone = KitTone.Subtle, string tip = null)
        {
            var item = Kit.Button(List, label, () =>
            {
                Popover.Close();
                action?.Invoke();
            }, tone, KitSize.Sm, tip);

            item.AddToClassList("vk-menu-item");

            return item;
        }

        public void Separator()
        {
            Kit.Divider(List);
        }
    }

    public static partial class Kit
    {
        public static KitPopover Popover(string classes = null, VisualElement layer = null)
        {
            var host = layer ?? PopupLayer;
            var popover = new KitPopover { Root = LayerRoot(host, false, "vk-popover") };

            Classes(popover.Root, classes);
            SetShown(popover.Root, false);

            popover.Root.RegisterCallback<GeometryChangedEvent>(_ => popover.Place());

            return popover;
        }

        public static KitDropdown Dropdown(VisualElement parent, Action<int> onPick, bool searchable = false,
            VisualElement layer = null)
        {
            var drop = new KitDropdown { OnPick = onPick, Popover = Popover("vk-menu", layer) };

            drop.Trigger = Button(parent, "", null, KitTone.Neutral, KitSize.Md);
            drop.Trigger.AddToClassList("vk-dropdown");
            drop.Text = Text(drop.Trigger, "—", KitText.Body);
            drop.Text.AddToClassList("vk-grow");
            Icon(drop.Trigger, KitGlyph.ChevronDown).AddToClassList("vk-dropdown-arrow");

            drop.Trigger.clicked += () =>
            {
                if (drop.Popover.IsOpen) drop.Popover.Close();
                else drop.Open();
            };

            if (searchable) drop.Search = Search(drop.Popover.Root, _ => drop.Fill());

            drop.List = Scroll(drop.Popover.Root, "vk-menu-scroll").contentContainer;

            return drop;
        }

        public static KitMenu Menu(VisualElement layer = null)
        {
            var menu = new KitMenu { Popover = Popover("vk-menu", layer) };

            menu.List = Col(menu.Popover.Root, KitGap.None, "vk-menu-list");

            return menu;
        }
    }
}
