using UnityEngine;
using UnityEngine.UIElements;

namespace Falcon.Shared.EditorUi
{
    /// <summary>
    /// Giữ chuột trái rồi kéo để cuộn mọi vùng cuộn của editor (như giao diện Imui cũ) — ScrollView của UI Toolkit chỉ cho kéo bằng cảm
    /// ứng, chuột thì chỉ có con lăn. Bấm rồi kéo quá <see cref="Threshold"/> px mới tính là kéo: bấm thường vẫn là bấm, và một nút bị
    /// kéo qua thì không nhận cú bấm đó (nút mất pointer capture). Không giành chuột của phần tử tự xử lý kéo: ô nhập chữ, thanh trượt,
    /// thanh cuộn, phần tử đang giữ chuột cho việc khác (kéo thả ô xe, thanh chia panel…) và mọi thứ nằm trong class <see cref="NoDrag"/>.
    /// </summary>
    internal sealed class DragScroll
    {
        /// <summary>Gắn lên phần tử (hoặc cha) không được bắt đầu kéo cuộn từ đó.</summary>
        public const string NoDrag = "ed-nodrag";

        private const float Threshold = 6f;
        /// <summary>Đánh dấu phần tử đã gắn bộ nghe kéo (chỉ gắn một lần).</summary>
        private const string Hooked = "ed-dragscroll";

        private ScrollView _scroll;
        private int _pointer = -1;
        private Vector2 _from, _offset;
        private bool _dragging;

        public static void Attach(VisualElement root)
        {
            var d = new DragScroll();
            root.RegisterCallback<PointerDownEvent>(d.Down, TrickleDown.TrickleDown);
            root.RegisterCallback<PointerMoveEvent>(d.Move, TrickleDown.TrickleDown);
            root.RegisterCallback<PointerUpEvent>(d.Up, TrickleDown.TrickleDown);
            root.RegisterCallback<PointerCancelEvent>(_ => d.Reset(), TrickleDown.TrickleDown);
        }

        private void Down(PointerDownEvent e)
        {
            Reset();
            if (e.button != 0 || e.pointerType != UnityEngine.UIElements.PointerType.mouse) return;
            var scroll = Candidate(e.target as VisualElement);
            if (scroll == null) return;
            // phần tử đang giữ chuột (nút vừa bấm, rồi chính vùng cuộn khi đã kéo) nhận sự kiện chuột một mình, không đi qua gốc nữa:
            // nghe thêm trên mọi phần tử từ chỗ bấm lên tới vùng cuộn
            for (var el = e.target as VisualElement; el != null; el = el.parent)
            {
                Hook(el);
                if (el == scroll) break;
            }
            _scroll = scroll;
            _pointer = e.pointerId;
            _from = e.position;
            _offset = scroll.scrollOffset;
        }

        private void Hook(VisualElement el)
        {
            if (el.ClassListContains(Hooked)) return;
            el.AddToClassList(Hooked);
            el.RegisterCallback<PointerMoveEvent>(Move);
            el.RegisterCallback<PointerUpEvent>(Up);
        }

        /// <summary>Vùng cuộn gần nhất chứa <paramref name="target"/>, hoặc null nếu bấm vào thứ tự xử lý kéo.</summary>
        private static ScrollView Candidate(VisualElement target)
        {
            for (var e = target; e != null; e = e.parent)
            {
                if (e is ScrollView s) return s;
                if (e is TextField || e is Scroller || e is BaseSlider<float> || e is BaseSlider<int> || e.ClassListContains(NoDrag))
                    return null;
            }
            return null;
        }

        private void Move(PointerMoveEvent e)
        {
            if (_scroll == null || e.pointerId != _pointer) return;
            if ((e.pressedButtons & 1) == 0)
            {
                Reset();
                return;
            }
            var delta = (Vector2)e.position - _from;
            if (!_dragging)
            {
                if (delta.magnitude < Threshold) return;
                // phần tử khác đang giữ chuột cho việc riêng (kéo ô xe, thanh chia…): để yên. Nút giữ chuột chỉ để chờ thả → giành lại.
                var holder = _scroll.panel?.GetCapturingElement(_pointer) as VisualElement;
                if (holder != null && holder is not Button || !CanScroll(_scroll, delta))
                {
                    Reset();
                    return;
                }
                _dragging = true;
                _scroll.CapturePointer(_pointer);
            }
            _scroll.scrollOffset = _offset - delta;
            e.StopPropagation();
        }

        /// <summary>Nội dung dài hơn khung theo hướng đang kéo (kéo ngang trong vùng chỉ cuộn dọc thì không giành).</summary>
        private static bool CanScroll(ScrollView s, Vector2 delta)
        {
            var view = s.contentViewport.layout.size;
            var content = s.contentContainer.layout.size;
            var vertical = Mathf.Abs(delta.y) >= Mathf.Abs(delta.x);
            return vertical ? content.y > view.y + 1f : content.x > view.x + 1f;
        }

        private void Up(PointerUpEvent e)
        {
            if (_scroll == null || e.pointerId != _pointer) return;
            if (_dragging)
            {
                if (_scroll.HasPointerCapture(_pointer)) _scroll.ReleasePointer(_pointer);
                e.StopPropagation(); // vừa kéo xong: không phải cú bấm
            }
            Reset();
        }

        private void Reset()
        {
            if (_dragging && _scroll != null && _scroll.HasPointerCapture(_pointer)) _scroll.ReleasePointer(_pointer);
            _scroll = null;
            _pointer = -1;
            _dragging = false;
        }
    }
}
