using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Falcon.Shared.EditorUi
{
    public enum KitSide
    {
        Below,
        Above,
        Right
    }

    public static class KitMouse
    {
        public const int Left = 1;
        public const int Right = 2;
        public const int Middle = 4;
    }

    public interface IKitLayer
    {
        VisualElement Root { get; }
        bool IsOpen { get; }
        bool Blocking { get; }
        bool Floating { get; }
        bool TakesEscape { get; }
        void Dismiss();
        void Close();
    }

    public static partial class Kit
    {
        public static VisualElement ModalLayer;

        private static readonly List<IKitLayer> openLayers = new();
        private static readonly List<VisualElement> earlyModals = new();
        private static readonly List<VisualElement> earlyPopups = new();

        public static bool AnyBlockingOpen
        {
            get
            {
                Prune();

                foreach (var layer in openLayers)
                    if (layer.Blocking && OnAppPanel(layer))
                        return true;

                return false;
            }
        }

        public static bool AnyLayerOpen
        {
            get
            {
                Prune();

                foreach (var layer in openLayers)
                    if (OnAppPanel(layer))
                        return true;

                return false;
            }
        }

        public static void BuildLayers(VisualElement overlay)
        {
            openLayers.Clear();

            ModalLayer = El(overlay, "vk-layer vk-modal-layer");
            ModalLayer.pickingMode = PickingMode.Ignore;

            PopupLayer = El(overlay, "vk-layer vk-popup-layer");
            PopupLayer.pickingMode = PickingMode.Ignore;

            foreach (var element in earlyModals) ModalLayer.Add(element);
            foreach (var element in earlyPopups) PopupLayer.Add(element);

            earlyModals.Clear();
            earlyPopups.Clear();
        }

        public static void ResetLayers()
        {
            ModalLayer = null;
            PopupLayer = null;

            earlyModals.Clear();
            earlyPopups.Clear();
        }

        internal static VisualElement LayerRoot(VisualElement host, bool modal, string classes)
        {
            var element = El(host, classes);

            if (host == null) (modal ? earlyModals : earlyPopups).Add(element);

            return element;
        }

        public static bool Escape()
        {
            Prune();

            for (var index = openLayers.Count - 1; index >= 0; index--)
            {
                var layer = openLayers[index];

                if (!OnAppPanel(layer)) continue;
                if (!layer.TakesEscape) return false;

                layer.Dismiss();

                return true;
            }

            return false;
        }

        public static void CloseFloating(int button)
        {
            Prune();

            for (var index = openLayers.Count - 1; index >= 0; index--)
            {
                var layer = openLayers[index];

                // A popover opened by this very click on the board (e.g. a board key's colour picker) must survive it.
                if (layer is KitPopover popover && popover.ClosesOn(button) && popover.OpenedFrame != UnityEngine.Time.frameCount &&
                    OnAppPanel(layer)) layer.Close();
            }
        }

        /// <summary>Lớp đang nằm trên cùng (không bị cửa sổ / popup nào khác che): để phím tắt của một cửa sổ chỉ chạy khi nó ở trên cùng.</summary>
        public static bool IsTop(IKitLayer layer)
        {
            Prune();

            for (var index = openLayers.Count - 1; index >= 0; index--)
                if (OnAppPanel(openLayers[index]))
                    return openLayers[index] == layer;

            return false;
        }

        internal static void Opened(IKitLayer layer)
        {
            openLayers.Remove(layer);
            openLayers.Add(layer);
        }

        internal static void Closed(IKitLayer layer)
        {
            openLayers.Remove(layer);
        }

        private static void Prune()
        {
            openLayers.RemoveAll(layer => !layer.IsOpen || layer.Root?.panel == null);
        }

        private static bool OnAppPanel(IKitLayer layer)
        {
            var appPanel = ModalLayer?.panel;

            return appPanel != null && layer.Root.panel == appPanel;
        }

        public static void Place(VisualElement floater, VisualElement anchor, VisualElement host, float gap = 4f,
            KitSide side = KitSide.Below)
        {
            if (floater == null || anchor == null || host == null) return;

            var bounds = host.worldBound;
            var target = anchor.worldBound;
            var size = new Vector2(floater.resolvedStyle.width, floater.resolvedStyle.height);

            if (float.IsNaN(size.x) || float.IsNaN(size.y)) return;

            float x;
            float y;

            if (side == KitSide.Right)
            {
                var right = target.xMax + gap;
                var left = target.xMin - gap - size.x;

                x = right + size.x <= bounds.xMax || left < bounds.xMin ? right : left;
                y = target.yMin;
            }
            else
            {
                var below = target.yMax + gap;
                var above = target.yMin - gap - size.y;

                if (side == KitSide.Above)
                    y = above >= bounds.yMin || below + size.y > bounds.yMax ? above : below;
                else
                    y = below + size.y <= bounds.yMax || above < bounds.yMin ? below : above;

                x = target.xMin;
            }

            x = Mathf.Clamp(x, bounds.xMin + gap, Mathf.Max(bounds.xMin + gap, bounds.xMax - size.x - gap));

            y = Mathf.Clamp(y, bounds.yMin + gap, Mathf.Max(bounds.yMin + gap, bounds.yMax - size.y - gap));

            var local = host.WorldToLocal(new Vector2(x, y));

            floater.style.left = local.x;
            floater.style.top = local.y;
        }
    }
}
