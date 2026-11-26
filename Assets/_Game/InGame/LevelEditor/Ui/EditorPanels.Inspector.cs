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
        // ------------------------------------------------------------------ inspector phải
        private void BuildRight(VisualElement root)
        {
            var panel = Kit.Col(root, KitGap.None, "ps-panel ps-right");
            _right = Kit.Scroll(panel, "ps-scroll");
            _empty = Kit.Text(_right, "Chưa có tranh", KitText.Dim, true);

            _info = Kit.Col(_right, KitGap.Sm);
            Kit.Text(_info, "Tranh", KitText.Heading);
            Kit.Text(_info, "Bấm một mảnh hoặc một hộp để xem và sửa thuộc tính.", KitText.Dim, true);
            Kit.Head(_info, "Thông tin chung");
            _infoFrame = Kit.Text(_info, "", KitText.Dim, true);
            _infoCount = Kit.Text(_info, "", KitText.Dim, true);
            _infoSand = Kit.Text(_info, "", KitText.Dim, true);
            _infoManual = Kit.Text(_info, "", KitText.Dim, true);
            Kit.Head(_info, "Số cát theo màu");
            _infoColors = Kit.El(_info, "ps-palette");
            Kit.Head(_info, "Mảnh quá bé (số cát không vừa)");
            _infoSmall = Kit.Col(_info, KitGap.Xs);

            _pieceBox = Kit.Col(_right, KitGap.Sm);
            _head = Kit.Text(_pieceBox, "", KitText.Heading);
            _headSub = Kit.Text(_pieceBox, "", KitText.Dim);
            Kit.Head(_pieceBox, "Bảng màu (bấm để đổi màu)");
            Palette(_pieceBox, _pieceSwatches, id => _editor.SetColor(id));
            Kit.Head(_pieceBox, "Số cát");
            _value = Kit.Stepper(_pieceBox, 10, PieceValue.Min, PieceValue.Max, v => _editor.SetValue(v), null, PieceValue.Step);
            _differ = Kit.Text(_pieceBox, "Số cát đang khác nhau giữa các mảnh", KitText.Dim, true);
            Kit.Head(_pieceBox, "Ẩn màu");
            _pieceHide = Kit.Button(_pieceBox, "Ẩn màu (H)", _editor.ToggleSelectedHidden, KitTone.Neutral, KitSize.Sm, "Mảnh ẩn mang lớp phủ trong game cho tới khi mở được");
            Kit.Head(_pieceBox, "Thao tác");
            var ops = Kit.Row(_pieceBox, KitGap.Sm);
            ops.AddToClassList("ps-trio");
            _cutBtn = Kit.Button(ops, "Tách", () => _editor.SetPen(PenMode.Cut), KitTone.Neutral, KitSize.Sm, "Vẽ đường cắt xuyên qua mảnh để tách làm đôi");
            _mergeBtn = Kit.Button(ops, "Gộp (M)", _editor.MergeSelected, KitTone.Neutral, KitSize.Sm, "Gộp các mảnh đã chọn vào mảnh chọn đầu tiên");
            _splitBtn = Kit.Button(ops, "Chia mảnh", ToggleTip, KitTone.Neutral, KitSize.Sm, "Chia mảnh đã chọn thành nhiều ô theo một kiểu mosaic");
            _deleteBtn = Kit.Button(ops, "Xoá mảnh", _editor.DeleteSelected, KitTone.Neutral, KitSize.Sm);
            _penHint = Kit.Text(_pieceBox, "", KitText.Dim, true);

            _boxBox = Kit.Col(_right, KitGap.Sm);
            _boxHead = Kit.Text(_boxBox, "", KitText.Heading);
            _boxSub = Kit.Text(_boxBox, "", KitText.Dim);
            Kit.Head(_boxBox, "Bảng màu (bấm để đổi màu)");
            Palette(_boxBox, _boxSwatches, id => _boxes.SetSelectedColor(id));
            Kit.Head(_boxBox, "Số cát");
            _boxValue = Kit.Stepper(_boxBox, 10, PieceValue.Min, PieceValue.Max, v => _boxes.SetSelectedValue(v), null, PieceValue.Step);
            _boxDiffer = Kit.Text(_boxBox, "Số cát đang khác nhau giữa các hộp", KitText.Dim, true);
            Kit.Head(_boxBox, "Ẩn màu");
            _boxHide = Kit.Button(_boxBox, "Ẩn màu (H)", _boxes.ToggleSelectedHidden, KitTone.Neutral, KitSize.Sm, "Hộp ẩn mang lớp phủ trong game cho tới khi lên đầu cột hoặc vào ô chờ");
            Kit.Head(_boxBox, "Băng");
            _boxIdle = Kit.Col(_boxBox, KitGap.Sm);
            _freezeNow = Kit.Button(_boxIdle, "Băng", _boxes.FreezeSelected, KitTone.Neutral, KitSize.Sm);
            _boxFrozen = Kit.Row(_boxBox, KitGap.Sm);
            Kit.Text(_boxFrozen, "Đợi", KitText.Dim);
            _boxFreeze = Kit.Stepper(_boxFrozen, 1, 1, PieceValue.Max, v => _boxes.SetSelectedFrozen(v), null, 1, false);
            Kit.Text(_boxFrozen, "hộp rời ô chờ", KitText.Dim);
            Kit.Button(_boxFrozen, "Bỏ băng", _boxes.UnfreezeSelected, KitTone.Subtle, KitSize.Sm);
            Kit.Head(_boxBox, "Thao tác");
            _boxDelete = Kit.Button(_boxBox, "Xoá hộp", _boxes.DeleteSelected, KitTone.Danger, KitSize.Sm);
        }

        private static void Palette(VisualElement parent, Button[] store, System.Action<int> pick)
        {
            var palette = Kit.El(parent, "ps-palette");
            for (var i = 0; i < store.Length; i++)
            {
                var id = i;
                var b = new Button(() => pick(id)) { focusable = false };
                b.AddToClassList("ps-swatch");
                b.style.backgroundColor = (Color)ColorPalette.Get(i);
                store[i] = b;
                palette.Add(b);
            }
        }
    }
}
