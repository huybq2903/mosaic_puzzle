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
        // ------------------------------------------------------------------ panel mechanic nổi
        private void BuildMechanic(VisualElement root)
        {
            _mech = Kit.Col(root, KitGap.Sm, "ps-mech");
            _mechTabs = Kit.Segmented(_mech, new[] { "Ẩn màu", "Pair", "Frozen" }, _mechanic, i =>
            {
                _mechanic = i;
                ResetModes();
                _dirty = true;
            });
            Kit.Divider(_mech);
            _mechBody = Kit.Scroll(_mech, "ps-scroll").contentContainer;
            Kit.Classes(_mechBody, "vk-col vk-gap-sm");
        }

        // Chế độ bấm chỉ sống khi đúng tab và đúng mechanic
        private void ResetModes()
        {
            if (!(_tab == TabMechanic && _mechanic == Hidden)) _editor.HiddenMode = false;
            if (!(_tab == TabMechanic && _mechanic == Frozen)) _ice = false;
            if (!(_tab == TabMechanic && _mechanic == Pair) && _boxes.PairMode) _boxes.SetPairMode(false);
        }

        private void SyncMechanic()
        {
            Kit.SetShown(_mech, _tab == TabMechanic);
            if (_tab != TabMechanic) return;
            Kit.SyncSegmented(_mechTabs, _mechanic);
            _mechBody.Clear();
            if (_mechanic == Hidden) DrawHidden();
            else if (_mechanic == Pair) DrawPair();
            else DrawFrozen();
        }

        private void DrawHidden()
        {
            var row = Kit.Row(_mechBody, KitGap.Sm);
            Kit.Toggle(row, "Chọn ẩn", _editor.HiddenMode, v => { _editor.HiddenMode = v; _dirty = true; }, KitSize.Sm, "Bật rồi bấm mảnh hoặc hộp để ẩn / hiện màu");
            Kit.Toggle(row, "Xem màu thật", !_editor.Cover, v => _editor.SetCover(!v), KitSize.Sm, "Chỉ đổi cách xem: bật để thấy màu thật của mảnh và hộp ẩn, tắt để che như trong game");
            if (_editor.HiddenMode) Kit.Text(_mechBody, "Bấm mảnh hoặc hộp để ẩn / hiện", KitText.Dim, true);

            var count = 0;
            var pic = _editor.Picture;
            if (pic != null)
                for (var i = 0; i < pic.regions.Count; i++)
                {
                    var index = i;
                    var r = pic.regions[i];
                    if (!r.hidden) continue;
                    count++;
                    MechRow(_mechBody, $"Mảnh #{i + 1}", r.colorId, _editor.IsSelected(i) && _boxes.Selected == null, null, r.value, PieceValue.Min, PieceValue.Max,
                        () => PickPiece(index), v => { PickPiece(index); _editor.SetValue(v); }, () => _editor.ToggleHidden(index), "Bỏ ẩn");
                }
            foreach (var b in _boxes.Queue.list.Where(b => b.hidden))
            {
                var id = b.id;
                count++;
                MechRow(_mechBody, "Hộp " + _boxes.PosOf(b), b.colorId, _boxes.IsSelected(id), null, b.value, PieceValue.Min, PieceValue.Max,
                    () => FocusBox(id), v => { FocusBox(id); _boxes.SetValue(id, v); }, () => _boxes.ToggleHidden(id), "Bỏ ẩn");
            }
            if (count == 0) Kit.Text(_mechBody, "Chưa có mảnh hay hộp nào bị ẩn", KitText.Dim, true);
        }

        private void DrawPair()
        {
            var hint = !_boxes.PairMode ? "Bật để nối hộp"
                : _boxes.PairPend != null ? "Bấm hộp tiếp theo"
                : _boxes.PairActive != 0 ? "Đang nối nhóm: bấm hộp để thêm, bấm lại hộp trong nhóm (hoặc Esc) để xong"
                : "Bấm hộp đầu tiên của nhóm";
            var row = Kit.Row(_mechBody, KitGap.Sm);
            Kit.Toggle(row, "Nối (N)", _boxes.PairMode, _boxes.SetPairMode, KitSize.Sm, "Bấm hộp đầu rồi các hộp tiếp theo; chuột phải gỡ hộp khỏi nhóm");
            Kit.Text(_mechBody, hint, KitText.Dim, true);
            var bad = _boxes.PairFaultText();
            if (bad.Length > 0) Kit.Note(_mechBody, bad, Severity.Error);

            var groups = _boxes.Groups();
            foreach (var g in groups)
            {
                var group = g;
                var label = string.Join(" · ", g.Members.Select(m => _boxes.PosOf(m)));
                var row2 = MechRow(_mechBody, label, -1, g.Members.All(m => _boxes.IsSelected(m.id)), "P" + (g.Index + 1), 0, 0, 0,
                    () => FocusBox(group.Members[0].id), null, () => _boxes.PairDissolve(group), "Gỡ nhóm");
                row2.style.borderLeftColor = g.Fault.Length > 0 ? new Color(0.94f, 0.27f, 0.27f) : PairColors[g.Index % PairColors.Length];
            }
            if (groups.Count == 0) Kit.Text(_mechBody, "Chưa có nhóm nào", KitText.Dim, true);
        }

        private void DrawFrozen()
        {
            var row = Kit.Row(_mechBody, KitGap.Sm);
            Kit.Toggle(row, "Chọn băng (F)", _ice, v => { _ice = v; _dirty = true; }, KitSize.Sm, "Bật rồi bấm hộp để băng; chuột phải bỏ băng");
            Kit.Text(_mechBody, _ice ? "Bấm hộp để băng · chuột phải bỏ băng" : "Bật để băng hộp; sửa số đợi ở bảng bên phải", KitText.Dim, true);
            var conflicts = _boxes.FrozenConflicts();
            if (conflicts > 0) Kit.Note(_mechBody, conflicts + " hộp vừa băng vừa ẩn màu (chặn lưu)", Severity.Error);

            var any = false;
            foreach (var b in _boxes.Queue.list.Where(b => b.frozen > 0))
            {
                any = true;
                var id = b.id;
                MechRow(_mechBody, "Hộp " + _boxes.PosOf(b), b.colorId, _boxes.IsSelected(id), null, b.frozen, 1, PieceValue.Max,
                    () => FocusBox(id), v => { FocusBox(id); _boxes.SetFrozen(id, v); }, () => _boxes.Unfreeze(id), "Bỏ băng");
            }
            if (!any) Kit.Text(_mechBody, "Chưa có hộp nào bị băng", KitText.Dim, true);
        }

        // Một dòng danh sách mechanic: chấm màu, nhãn, huy hiệu, ô số sửa tại chỗ, nút ×
        private VisualElement MechRow(VisualElement parent, string label, int colorId, bool selected, string badge, int number, int min, int max,
            System.Action pick, System.Action<int> edit, System.Action remove, string removeTip)
        {
            var row = Kit.Row(parent, KitGap.Sm, "ps-mrow");
            row.EnableInClassList("ps-on", selected);
            if (colorId >= 0)
            {
                var dot = Kit.El(row, "ps-dot");
                dot.style.backgroundColor = (Color)ColorPalette.Get(colorId);
            }
            var text = Kit.Text(row, label, KitText.Body);
            text.AddToClassList("ps-grow");
            if (badge != null) Kit.Text(row, badge, KitText.Caption);
            if (edit != null) Kit.Stepper(row, number, min, max, edit, null, 1, false);
            var x = Kit.Button(row, "×", remove, KitTone.Subtle, KitSize.Sm, removeTip);
            x.AddToClassList("ps-x");
            row.RegisterCallback<ClickEvent>(e =>
            {
                for (var w = e.target as VisualElement; w != null && w != row; w = w.parent)
                    if (w is Button || w is TextField || w is IntegerField) return;
                pick();
            });
            return row;
        }

        private void PickPiece(int index)
        {
            _boxes.Deselect();
            if (_editor.Selected != index) _editor.Select(index);
        }

        // Bấm chip: chọn, bấm lại bỏ chọn
        private void PickBox(string id)
        {
            _editor.Select(-1);
            var kb = UnityEngine.InputSystem.Keyboard.current;
            _boxes.Select(id, kb != null && kb.ctrlKey.isPressed);
        }

        // Bấm dòng danh sách: luôn chọn hộp đó
        private void FocusBox(string id)
        {
            _editor.Select(-1);
            _boxes.SelectOnly(id);
        }
    }
}
