using System.Collections.Generic;
using System.Linq;
using Falcon.InGame.Core;
using Falcon.InGame.LevelEditor.Gen;
using Falcon.Shared.BaseLevelEditor;
using Falcon.Shared.EditorUi;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Falcon.InGame.LevelEditor
{
    public sealed partial class EditorPanels
    {
        // ------------------------------------------------------------------ phím
        private void HandleKeys()
        {
            var kb = Keyboard.current;
            if (kb == null || EditorUiHost.KeyboardBusy) return;
            var ctrl = kb.ctrlKey.isPressed;
            var shift = kb.shiftKey.isPressed;
            if (kb.zKey.wasPressedThisFrame && !shift && !_editor.IsBusy) { if (_history.Undo()) _dirty = true; return; }
            if ((kb.yKey.wasPressedThisFrame || (kb.zKey.wasPressedThisFrame && shift && ctrl)) && !_editor.IsBusy) { if (_history.Redo()) _dirty = true; return; }
            if (ctrl || kb.altKey.isPressed) return;
            if (kb.escapeKey.wasPressedThisFrame && _editor.PenActive) { _editor.CancelPen(); return; }
            if (_editor.PenActive) return;

            if (kb.hKey.wasPressedThisFrame)
            {
                if (_boxes.SelectedBox != null) _boxes.ToggleSelectedHidden();
                else _editor.ToggleSelectedHidden();
            }
            if (_boxes.SelectedBox != null && (kb.leftBracketKey.wasPressedThisFrame || kb.rightBracketKey.wasPressedThisFrame))
                _boxes.StepSelectedValue(kb.rightBracketKey.wasPressedThisFrame ? 1 : -1);
            if (kb.mKey.wasPressedThisFrame) _editor.MergeSelected();
            if (_tab == TabMechanic && _mechanic == Pair && kb.nKey.wasPressedThisFrame) _boxes.SetPairMode(!_boxes.PairMode);
            if (_tab == TabMechanic && _mechanic == Frozen && kb.fKey.wasPressedThisFrame) { _ice = !_ice; _dirty = true; }
            if (kb.escapeKey.wasPressedThisFrame)
            {
                if (_pop != Pop.None) { ClosePop(); return; }
                if (_boxes.PairMode && _boxes.PairFinish()) return;
                _editor.ClearSelection();
            }
        }

        // Dùng bởi công cụ chụp giao diện: dựng popup bằng dữ liệu mẫu, trả hình chữ nhật của hộp trên màn hình
        public Rect? AuditPopup(string state)
        {
            StartPopup.Current?.Preview(state == "start");
            if (state == "start") return StartPopup.Current?.ScreenRect();
            if (state == "busy")
            {
                _busy.Preview("Đang tạo tranh");
                return _busy.ScreenRect();
            }
            _busy.Preview(null);
            if (state == "none") { _ai.ClosePreview(); return null; }
            _ai.Preview(state);
            return _ai.ScreenRect();
        }

        // Hình chữ nhật hiện tại của popup đang xem trước (sau khi đã dàn trang)
        public Rect? AuditRect(string state) => state == "start" ? StartPopup.Current?.ScreenRect() : state == "busy" ? _busy.ScreenRect() : _ai.ScreenRect();

        // Khung kéo chọn: đổi toạ độ màn hình (y hướng lên) sang toạ độ trong khung chứa, theo tỉ lệ panel
        private void SyncBand()
        {
            var band = _editor.Band;
            Kit.SetShown(_band, band.HasValue);
            if (!band.HasValue || _band.panel == null || _band.parent == null) return;
            var tree = _band.panel.visualTree.layout;
            if (tree.width <= 0f || tree.height <= 0f) return;
            var origin = _band.parent.worldBound.position;
            Vector2 ToLocal(float x, float y) => new Vector2(x * tree.width / Screen.width, (Screen.height - y) * tree.height / Screen.height) - origin;
            var a = ToLocal(band.Value.xMin, band.Value.yMin);
            var b = ToLocal(band.Value.xMax, band.Value.yMax);
            _band.style.left = Mathf.Min(a.x, b.x);
            _band.style.top = Mathf.Min(a.y, b.y);
            _band.style.width = Mathf.Abs(b.x - a.x);
            _band.style.height = Mathf.Abs(b.y - a.y);
        }
    }
}
