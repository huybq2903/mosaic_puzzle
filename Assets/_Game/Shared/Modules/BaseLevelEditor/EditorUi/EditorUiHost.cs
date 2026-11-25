using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Falcon.Shared.EditorUi
{
    /// <summary>Một màn hình của editor: dựng một lần, mỗi khung hình đồng bộ giá trị (chỉ ghi khi đổi).</summary>
    public interface IEditorScreen
    {
        void Build(VisualElement root);
        void Sync();
    }

    /// <summary>
    /// Chủ của toàn bộ giao diện Level Editor (UI Toolkit): một UIDocument tạo lúc chạy, nạp USS theo thứ tự tên file trong mọi thư
    /// mục Resources/EditorUi/Styles (bộ kit + USS riêng của từng module), lớp cửa sổ / popup của Kit, tooltip và toast dùng chung,
    /// rồi mỗi khung hình gọi Sync của từng màn hình — cách làm của tool editor Bee Flow. Màn hình xếp theo <c>order</c>: số lớn
    /// vẽ sau (nằm trên). Esc đóng cửa sổ trên cùng.
    /// </summary>
    public sealed class EditorUiHost : MonoBehaviour
    {
        private const string Folder = "EditorUi";

        public static EditorUiHost Instance { get; private set; }

        /// <summary>
        /// Đang gõ trong một ô nhập, hoặc đang có cửa sổ che board: phím tắt của editor (B, D, G…) không được chạy.
        /// <c>LevelEditorInputHandler</c> đọc cờ này.
        /// </summary>
        public static bool KeyboardBusy => KeyboardGuard.Busy;

        private readonly List<(IEditorScreen screen, int order, VisualElement box)> _screens = new();
        private VisualElement _root;
        /// <summary>Chỗ chứa các màn hình — nằm dưới lớp cửa sổ / popup, tooltip và toast (thứ tự vẽ = thứ tự anh em).</summary>
        private VisualElement _screensHost;
        private Label _tip;
        private VisualElement _tipOwner;
        private KitCallout _toast;
        private string _toastText;
        private Severity _toastSeverity;
        private float _toastUntil;

        public VisualElement Root => _root;

        /// <summary>Host của scene hiện tại; chưa có thì tạo (trả null nếu thiếu tài nguyên giao diện).</summary>
        public static EditorUiHost Ensure()
        {
            if (Instance != null) return Instance;
            var settings = Resources.Load<PanelSettings>(Folder + "/EditorUiPanel");
            if (settings == null)
            {
                Debug.LogError("[EditorUi] thiếu Resources/EditorUi/EditorUiPanel.asset — giao diện editor không mở được.");
                return null;
            }
            var go = new GameObject("[Editor UI]");
            go.SetActive(false);
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = settings;
            var host = go.AddComponent<EditorUiHost>();
            go.SetActive(true);
            return host;
        }

        /// <summary>Thêm một màn hình; <paramref name="order"/> lớn hơn thì vẽ sau (nằm trên các màn khác).</summary>
        public void Add(IEditorScreen screen, int order = 0)
        {
            var at = _screens.FindIndex(s => s.order > order);
            if (at < 0) at = _screens.Count;
            _screens.Insert(at, (screen, order, null));
            if (_screensHost != null) BuildAt(at);
        }

        public bool Contains<T>() where T : IEditorScreen => _screens.Any(s => s.screen is T);

        private void BuildAt(int index)
        {
            var (screen, order, _) = _screens[index];
            var box = Fill(null, "ed-screen");
            _screensHost.Insert(index, box);
            _screens[index] = (screen, order, box);
            try
            {
                screen.Build(box);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            KeyboardGuard.Release();
            Kit.ResetLayers();
        }

        private void OnEnable()
        {
            var doc = GetComponent<UIDocument>();
            PreloadGlyphs(doc.panelSettings);
            _root = doc.rootVisualElement;
            _root.Clear();
            _root.styleSheets.Clear();
            foreach (var sheet in Resources.LoadAll<StyleSheet>(Folder + "/Styles").OrderBy(s => s.name, StringComparer.Ordinal))
                _root.styleSheets.Add(sheet);
            _root.AddToClassList(Kit.RootClass);
            _root.pickingMode = PickingMode.Ignore;
            Kit.TipHook = Tip;
            Kit.ResetLayers();

            var overlay = Fill(_root, "ed-overlay");
            _screensHost = Fill(overlay, "ed-screens");
            for (var i = 0; i < _screens.Count; i++) BuildAt(i);
            Kit.BuildLayers(overlay);
            _toast = Kit.Callout(overlay, Severity.Info, "ed-toast");
            _toast.Root.RegisterCallback<ClickEvent>(_ => _toastUntil = 0f);
            _tip = Kit.Text(overlay, "", KitText.Body, true);
            Kit.Classes(_tip, "ed-tip vk-surface");
            _tip.pickingMode = PickingMode.Ignore;
            _tip.RegisterCallback<GeometryChangedEvent>(_ => PlaceTip());
            Kit.SetShown(_tip, false);
            KeyboardGuard.Watch(_root);
            DragScroll.Attach(_root); // giữ chuột trái kéo để cuộn mọi vùng cuộn
        }

        private static bool _glyphsLoaded;

        /// <summary>
        /// Bản build: nạp sẵn mọi ký tự giao diện dùng vào font động TRƯỚC khi dựng giao diện. Font động chỉ thêm ký tự vào atlas khi
        /// gặp lần đầu, và bản build đo chữ trước khi ký tự kịp vào — nút được dựng hụt vài pixel rồi cắt chữ ("Lưu thành…" thành
        /// "Lưu thàn…"). Trong Unity Editor atlas đã có sẵn nên không bị; nạp ở đó còn ghi bẩn file font asset, nên bỏ qua.
        /// </summary>
        private static void PreloadGlyphs(PanelSettings settings)
        {
            if (_glyphsLoaded || Application.isEditor) return;
            _glyphsLoaded = true;
            var font = settings != null && settings.textSettings != null ? settings.textSettings.defaultFontAsset : null;
            if (font == null) return;
            var sb = new System.Text.StringBuilder();
            void Range(int from, int to)
            {
                for (var c = from; c <= to; c++) sb.Append((char)c);
            }
            Range(0x20, 0x7E);     // ASCII
            Range(0xA0, 0x24F);    // Latin-1 + Latin mở rộng A/B: ă â đ ê ô ơ ư…
            Range(0x300, 0x30F);   // dấu ghép
            Range(0x1EA0, 0x1EF9); // chữ có dấu tiếng Việt dựng sẵn
            sb.Append("…·–—×•▸▾✓✕“”‘’≈≤≥±°");
            try
            {
                font.TryAddCharacters(sb.ToString(), out _); // ký tự font không có thì bỏ qua
            }
            catch (Exception e)
            {
                Debug.LogWarning("[EditorUi] không nạp sẵn được ký tự cho font: " + e.Message);
            }
        }

        private static VisualElement Fill(VisualElement parent, string classes)
        {
            var e = new VisualElement();
            Kit.Classes(e, classes);
            parent?.Add(e);
            e.pickingMode = PickingMode.Ignore;
            e.style.position = Position.Absolute;
            e.style.left = e.style.top = e.style.right = e.style.bottom = 0;
            return e;
        }

        private void LateUpdate()
        {
            if (_root == null) return;
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) Kit.Escape();
            // bấm chuột ra ngoài giao diện (lên board) thì đóng danh sách xổ xuống / menu đang mở
            var pointer = Mouse.current;
            if (pointer != null)
            {
                if (pointer.leftButton.wasPressedThisFrame && !PointerOverUi()) Kit.CloseFloating(0);
                if (pointer.rightButton.wasPressedThisFrame && !PointerOverUi()) Kit.CloseFloating(1);
                if (pointer.middleButton.wasPressedThisFrame && !PointerOverUi()) Kit.CloseFloating(2);
            }
            // cửa sổ (modal) đang mở che board: phím tắt của editor phía sau không được chạy
            KeyboardGuard.SetModal(Kit.AnyBlockingOpen);
            foreach (var s in _screens)
            {
                try
                {
                    s.screen.Sync();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
            _toast.Set(!string.IsNullOrEmpty(_toastText) && Time.unscaledTime <= _toastUntil ? _toastText : null, _toastSeverity);
            if (_tipOwner != null && _tipOwner.panel == null) HideTip(null);
        }

        /// <summary>Con trỏ đang nằm trên một phần tử nhận chuột của giao diện editor (không có host thì false).</summary>
        public static bool IsPointerOverUi => Instance != null && Instance.PointerOverUi();

        /// <summary>Con trỏ đang nằm trên một phần tử nhận chuột của giao diện (không tính vùng trống để lộ board).</summary>
        public bool PointerOverUi()
        {
            var panel = _root?.panel;
            if (panel == null) return false;
            var mouse = Mouse.current;
            if (mouse == null) return false;
            var at = mouse.position.ReadValue();
            var pos = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(at.x, Screen.height - at.y));
            var hit = panel.Pick(pos);
            return hit != null && hit != _root;
        }

        // ------------------------------------------------------------------ toast: một dòng báo ngắn, bấm để tắt
        public void Say(string text, Severity severity = Severity.Info)
        {
            _toastText = text;
            _toastSeverity = severity;
            _toastUntil = Time.unscaledTime + (severity == Severity.Error ? 30f : 8f);
        }

        // ------------------------------------------------------------------ tooltip dùng chung (đặt trên phần tử, hết chỗ thì dưới)
        private void Tip(VisualElement target, string text)
        {
            if (target == null) return;
            target.tooltip = text ?? "";
            if (target.ClassListContains("ed-tipped")) return;
            target.AddToClassList("ed-tipped");
            target.RegisterCallback<PointerEnterEvent>(_ => ShowTip(target, target.tooltip));
            target.RegisterCallback<PointerLeaveEvent>(_ => HideTip(target));
        }

        private void ShowTip(VisualElement target, string text)
        {
            if (_tip == null || string.IsNullOrEmpty(text)) return;
            _tipOwner = target;
            Kit.SetText(_tip, text);
            Kit.SetShown(_tip, true);
            _tip.BringToFront();
            PlaceTip();
        }

        private void HideTip(VisualElement target)
        {
            if (_tip == null || (target != null && _tipOwner != target)) return;
            _tipOwner = null;
            Kit.SetShown(_tip, false);
        }

        private void PlaceTip()
        {
            if (_tip == null || _tipOwner == null) return;
            var anchor = _tipOwner.worldBound;
            float w = _tip.resolvedStyle.width, h = _tip.resolvedStyle.height;
            var above = anchor.yMin - h - 6f;
            _tip.style.left = Mathf.Clamp(anchor.center.x - w * 0.5f, 4f, Mathf.Max(4f, _root.resolvedStyle.width - w - 4f));
            _tip.style.top = Mathf.Clamp(above < 4f ? anchor.yMax + 6f : above, 4f, Mathf.Max(4f, _root.resolvedStyle.height - h - 4f));
        }
    }

    /// <summary>
    /// Theo dõi khi nào bàn phím thuộc về giao diện: một ô nhập (TextField, ô số của Stepper…) đang được gõ, hoặc một cửa sổ đang
    /// che board. Khi đó phím tắt của editor phải im — gõ "b" trong ô chat không được bật công cụ Vẽ.
    /// </summary>
    internal static class KeyboardGuard
    {
        private static bool _typing, _modal;

        public static bool Busy => _typing || _modal;

        public static void Watch(VisualElement root)
        {
            root.RegisterCallback<FocusInEvent>(e => _typing = IsTextInput(e.target as VisualElement), TrickleDown.TrickleDown);
            root.RegisterCallback<FocusOutEvent>(_ => _typing = false, TrickleDown.TrickleDown);
        }

        public static void SetModal(bool open) => _modal = open;

        public static void Release() => _typing = _modal = false;

        // mọi ô nhập chữ / số của UI Toolkit (TextField, IntegerField, FloatField…) mang class này
        private static bool IsTextInput(VisualElement e)
        {
            for (; e != null; e = e.parent)
                if (e.ClassListContains("unity-base-text-field")) return true;
            return false;
        }
    }
}
