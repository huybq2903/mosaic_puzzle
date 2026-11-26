using Falcon.Shared.EditorUi;
using UnityEngine;
using UnityEngine.UIElements;

namespace Falcon.InGame.LevelEditor
{
    /// <summary>Popup chặn "Đang xử lý" như Studio web: hiện khi editor đang chạy việc nền (tạo tranh, tách, gộp, sửa biên, chơi thử…), tự đóng khi xong.</summary>
    public sealed class BusyPopup
    {
        private const float ShowDelay = 0.25f, DotSeconds = 0.4f;

        private KitModal _modal;
        private BusySpinner _spinner;
        private Label _text;
        private string _label = "";
        private float _since = -1f;
        private bool _preview;

        private void Build()
        {
            if (_modal != null) return;
            _modal = Kit.Modal("Đang xử lý", KitModalSize.Sm);
            _modal.Dismissable = false;
            _modal.ScrimCloses = false;
            _modal.Box.AddToClassList("busy-win");
            _modal.Title.AddToClassList("busy-title");
            _modal.Title.pickingMode = PickingMode.Ignore;
            _modal.CloseButton.style.display = DisplayStyle.None;
            _modal.Foot.style.display = DisplayStyle.None;
            _spinner = new BusySpinner();
            _spinner.AddToClassList("busy-spinner");
            _modal.Body.Add(_spinner);
            _text = Kit.Text(_modal.Body, "", KitText.Heading);
            _text.AddToClassList("busy-text");
            Kit.Text(_modal.Body, "Vui lòng chờ, ảnh lớn có thể mất vài giây", KitText.Dim, true).AddToClassList("busy-sub");
        }

        /// <summary>Gọi mỗi khung hình. Popup chỉ hiện khi việc kéo dài hơn một chút, để việc ngắn không chớp.</summary>
        public void Sync(bool busy, string label)
        {
            if (_preview) return;
            if (!busy)
            {
                _since = -1f;
                if (_modal != null && _modal.IsOpen) _modal.SetOpen(false);
                return;
            }
            var now = Time.realtimeSinceStartup;
            if (_since < 0f) _since = now;
            if (now - _since < ShowDelay) return;
            Build();
            if (!_modal.IsOpen) _modal.SetOpen(true);
            Paint(label, now - _since);
        }

        private void Paint(string label, float seconds)
        {
            _label = (label ?? "Đang xử lý").TrimEnd('.', '…', ' ');
            _text.text = _label + new string('.', Mathf.FloorToInt(seconds / DotSeconds) % 4);
            _spinner.Step(Time.realtimeSinceStartup);
        }

        // Công cụ chụp giao diện: hiện hoặc đóng popup với tên việc mẫu
        public void Preview(string label)
        {
            _preview = label != null;
            if (label == null)
            {
                if (_modal != null && _modal.IsOpen) _modal.SetOpen(false);
                return;
            }
            Build();
            _modal.SetOpen(true);
            Paint(label, 1.3f);
        }

        public Rect? ScreenRect()
        {
            if (_modal?.Box == null || _modal.Box.panel == null) return null;
            var layout = _modal.Box.panel.visualTree.layout;
            if (layout.width <= 0f) return null;
            var k = Screen.width / layout.width;
            var b = _modal.Box.worldBound;
            return Rect.MinMaxRect(b.xMin * k, Screen.height - b.yMax * k, b.xMax * k, Screen.height - b.yMin * k);
        }
    }

    /// <summary>Vòng 12 vạch xanh nhạt xoay, vạch đậm dần theo chiều quay (như spinner của Studio web).</summary>
    public sealed class BusySpinner : VisualElement
    {
        private const int Bars = 12;
        private float _phase;

        public BusySpinner()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        public void Step(float time)
        {
            var phase = Mathf.Floor(time * Bars) % Bars;
            if (Mathf.Approximately(phase, _phase)) return;
            _phase = phase;
            MarkDirtyRepaint();
        }

        private void Draw(MeshGenerationContext ctx)
        {
            var box = contentRect;
            var size = Mathf.Min(box.width, box.height);
            if (size < 4f) return;
            var center = box.center;
            var outer = size * 0.5f;
            var inner = outer * 0.5f;
            var p = ctx.painter2D;
            p.lineWidth = Mathf.Max(2f, size * 0.12f);
            p.lineCap = LineCap.Round;
            for (var i = 0; i < Bars; i++)
            {
                var angle = (i * 360f / Bars - 90f) * Mathf.Deg2Rad;
                var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                var age = ((i - (int)_phase) % Bars + Bars) % Bars;
                p.strokeColor = new Color(0.49f, 0.83f, 0.99f, Mathf.Lerp(1f, 0.15f, age / (float)(Bars - 1)));
                p.BeginPath();
                p.MoveTo(center + dir * inner);
                p.LineTo(center + dir * (outer - p.lineWidth * 0.5f));
                p.Stroke();
            }
        }
    }
}
