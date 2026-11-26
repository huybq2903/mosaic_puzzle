using System.Collections.Generic;
using System.Linq;
using Falcon.InGame.Core;
using Falcon.Shared.EditorUi;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Falcon.InGame.LevelEditor
{
    // Cột hộp ở tab Hàng chờ: kéo thả hộp, kéo khung chọn nhiều hộp, chuột phải mở bảng màu và số cát
    public sealed partial class EditorPanels
    {
        private const float DragPixels = 6f;

        private enum Pop { None, Box, New }

        private VisualElement _overlay, _ghost, _drop, _boxBand, _boxPop;
        private Label _ghostText;
        private bool _pressing, _dragging, _banding, _pressAdd;
        private string _pressId;
        private Vector2 _pressPos;
        private (int col, int row)? _dropAt;
        private Pop _pop = Pop.None;
        private int _popCol;
        private Vector2 _popPos;
        private readonly Button[] _popSwatches = new Button[ColorPalette.Count];

        // ------------------------------------------------------------------ gắn sự kiện chuột vào cột hộp
        private void AttachBoxes(VisualElement host)
        {
            host.RegisterCallback<PointerDownEvent>(OnBoxDown);
            host.RegisterCallback<PointerMoveEvent>(OnBoxMove);
            host.RegisterCallback<PointerUpEvent>(OnBoxUp);
            host.RegisterCallback<PointerCaptureOutEvent>(_ => EndPress());
        }

        // Lớp phủ chứa bóng kéo, vạch thả, khung chọn và bảng chuột phải (cùng khung với khung kéo chọn mảnh)
        private void EnsureOverlay()
        {
            if (_overlay != null || _band?.parent == null) return;
            _overlay = _band.parent;
            _ghost = Kit.El(_overlay, "ps-ghost");
            _ghostText = Kit.Text(_ghost, "", KitText.Body);
            _drop = Kit.El(_overlay, "ps-drop");
            _boxBand = Kit.El(_overlay, "ps-band");
            _boxPop = Kit.Col(_overlay, KitGap.Sm, "ps-pop");
            foreach (var e in new[] { _ghost, _ghostText, _drop, _boxBand }) e.pickingMode = PickingMode.Ignore;
            Kit.SetShown(_ghost, false);
            Kit.SetShown(_drop, false);
            Kit.SetShown(_boxBand, false);
            Kit.SetShown(_boxPop, false);
            _overlay.RegisterCallback<PointerDownEvent>(e =>
            {
                if (_pop != Pop.None && !Inside(e.target as VisualElement, _boxPop)) ClosePop();
            }, TrickleDown.TrickleDown);
        }

        private static bool Inside(VisualElement e, VisualElement ancestor)
        {
            for (; e != null; e = e.parent) if (e == ancestor) return true;
            return false;
        }

        private static string ChipId(VisualElement e)
        {
            for (; e != null; e = e.parent) if (e.ClassListContains("ps-chip")) return e.userData as string;
            return null;
        }

        // Cột của hàng thêm hộp mà e nằm trong; -1 nếu không phải
        private static int AddColumn(VisualElement e)
        {
            for (; e != null; e = e.parent) if (e.ClassListContains("ps-add")) return (int)e.userData;
            return -1;
        }

        private static bool CtrlHeld()
        {
            var kb = Keyboard.current;
            return kb != null && kb.ctrlKey.isPressed;
        }

        private Vector2 Local(Vector2 panel) => panel - _overlay.worldBound.position;

        // ------------------------------------------------------------------ chuột
        private void OnBoxDown(PointerDownEvent e)
        {
            EnsureOverlay();
            if (_pop != Pop.None) { ClosePop(); e.StopPropagation(); return; }
            var target = e.target as VisualElement;
            var id = ChipId(target);
            if (e.button == 1)
            {
                var col = AddColumn(target);
                if (id != null)
                {
                    if (!_boxes.IsSelected(id)) { _editor.Select(-1); _boxes.SelectOnly(id); }
                    OpenPop(Pop.Box, 0, e.position);
                }
                else if (col >= 0) OpenPop(Pop.New, col, e.position);
                e.StopPropagation();
                return;
            }
            if (e.button != 0 || AddColumn(target) >= 0) return;
            _pressing = true;
            _dragging = _banding = false;
            _pressId = id;
            _pressPos = e.position;
            _pressAdd = CtrlHeld();
            ((VisualElement)e.currentTarget).CapturePointer(e.pointerId);
            e.StopPropagation();
        }

        private void OnBoxMove(PointerMoveEvent e)
        {
            if (!_pressing) return;
            var far = Vector2.Distance(e.position, _pressPos) > DragPixels;
            if (_pressId != null && !_dragging && far)
            {
                _dragging = true;
                _editor.Select(-1);
                _boxes.SelectOnly(_pressId);
                var box = _boxes.Find(_pressId);
                if (box != null)
                {
                    var color = (Color)ColorPalette.Get(box.colorId);
                    _ghost.style.backgroundColor = color;
                    _ghostText.style.color = color.grayscale > 0.6f ? new Color(0.06f, 0.09f, 0.16f) : new Color(0.97f, 0.98f, 0.99f);
                    _ghostText.text = box.value.ToString();
                }
                Kit.SetShown(_ghost, true);
            }
            if (_pressId == null && !_banding && far) _banding = true;
            if (_dragging)
            {
                var at = Local(e.position);
                _ghost.style.left = at.x - 24;
                _ghost.style.top = at.y - 16;
                _dropAt = DropTarget(e.position);
                ShowDrop();
            }
            if (_banding) ShowBoxBand(e.position);
        }

        private void OnBoxUp(PointerUpEvent e)
        {
            if (!_pressing) return;
            ((VisualElement)e.currentTarget).ReleasePointer(e.pointerId);
            var id = _pressId;
            var dragged = _dragging;
            var banded = _banding;
            var add = _pressAdd;
            var at = _dropAt;
            var start = _pressPos;
            EndPress();
            if (e.button != 0) return;
            if (dragged) { if (at.HasValue) _boxes.Move(id, at.Value.col, at.Value.row); }
            else if (id != null) { _editor.Select(-1); _boxes.Select(id, add); }
            else if (banded)
            {
                var rect = Rect.MinMaxRect(Mathf.Min(start.x, e.position.x), Mathf.Min(start.y, e.position.y), Mathf.Max(start.x, e.position.x), Mathf.Max(start.y, e.position.y));
                var ids = _columns.Query<Button>(className: "ps-chip").ToList().Where(c => c.worldBound.Overlaps(rect)).Select(c => (string)c.userData).ToList();
                _editor.Select(-1);
                _boxes.SelectMany(ids, add);
            }
            else if (!add) _boxes.Deselect();
        }

        private void EndPress()
        {
            _pressing = _dragging = _banding = false;
            _pressId = null;
            _dropAt = null;
            if (_overlay == null) return;
            Kit.SetShown(_ghost, false);
            Kit.SetShown(_drop, false);
            Kit.SetShown(_boxBand, false);
        }

        private void ShowBoxBand(Vector2 now)
        {
            var a = Local(_pressPos);
            var b = Local(now);
            Kit.SetShown(_boxBand, true);
            _boxBand.style.left = Mathf.Min(a.x, b.x);
            _boxBand.style.top = Mathf.Min(a.y, b.y);
            _boxBand.style.width = Mathf.Abs(b.x - a.x);
            _boxBand.style.height = Mathf.Abs(b.y - a.y);
        }

        // ------------------------------------------------------------------ thả hộp
        private List<VisualElement> ColumnElements() => _columns.Children().Where(c => c.ClassListContains("ps-col")).ToList();

        // Cột và hàng mà hộp đang kéo sẽ rơi vào (hàng đếm không tính chính nó)
        private (int col, int row)? DropTarget(Vector2 pos)
        {
            var cols = ColumnElements();
            for (var c = 0; c < cols.Count; c++)
            {
                var r = cols[c].worldBound;
                if (pos.x < r.xMin || pos.x > r.xMax) continue;
                var row = 0;
                foreach (var chip in cols[c].Query<Button>(className: "ps-chip").ToList())
                {
                    if ((string)chip.userData == _pressId) continue;
                    if (chip.worldBound.center.y < pos.y) row++;
                }
                return (c, row);
            }
            return null;
        }

        private void ShowDrop()
        {
            if (!_dropAt.HasValue) { Kit.SetShown(_drop, false); return; }
            var cols = ColumnElements();
            var (c, row) = _dropAt.Value;
            if (c >= cols.Count) { Kit.SetShown(_drop, false); return; }
            var chips = cols[c].Query<Button>(className: "ps-chip").ToList().Where(x => (string)x.userData != _pressId).ToList();
            var rect = cols[c].worldBound;
            float y;
            if (row < chips.Count) y = chips[row].worldBound.yMin - 2;
            else if (chips.Count > 0) y = chips[^1].worldBound.yMax + 1;
            else y = rect.yMin + 40;
            var at = Local(new Vector2(rect.xMin, y));
            Kit.SetShown(_drop, true);
            _drop.style.left = at.x;
            _drop.style.top = at.y;
            _drop.style.width = rect.width;
        }

        // ------------------------------------------------------------------ chuột phải: bảng màu và số cát
        private void OpenPop(Pop kind, int col, Vector2 pos)
        {
            _pop = kind;
            _popCol = col;
            _popPos = pos;
            _dirty = true;
        }

        // Bấm ra ngoài giao diện (vào tranh) đóng bảng chuột phải và bỏ chọn hộp
        private void PollPop()
        {
            var mouse = Mouse.current;
            if (mouse == null || EditorUiHost.IsPointerOverUi) return;
            if (_pop != Pop.None && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame)) ClosePop();
            if (mouse.leftButton.wasPressedThisFrame && _boxes.Selection.Count > 0) _boxes.Deselect();
        }

        private void ClosePop()
        {
            if (_pop == Pop.None) return;
            _pop = Pop.None;
            _dirty = true;
        }

        private void SyncPop()
        {
            EnsureOverlay();
            if (_overlay == null) return;
            Kit.SetShown(_boxPop, _pop != Pop.None);
            if (_pop == Pop.None) return;
            _boxPop.Clear();
            var p = _editor.Picture;
            var used = new HashSet<int>(p == null ? new List<int>() : p.regions.Select(r => r.colorId));
            int current;
            int? value;
            if (_pop == Pop.Box)
            {
                var boxes = _boxes.SelectedBoxes;
                var colors = boxes.Select(b => b.colorId).Distinct().ToList();
                current = colors.Count == 1 ? colors[0] : -1;
                var values = boxes.Select(b => b.value).Distinct().ToList();
                value = values.Count == 1 ? values[0] : null;
            }
            else
            {
                current = _boxes.NewColor;
                value = _addValue.GetValueOrDefault(_popCol, 10);
            }
            var grid = Kit.El(_boxPop, "ps-palette");
            for (var i = 0; i < _popSwatches.Length; i++)
            {
                var id = i;
                var swatch = new Button(() => PopColor(id)) { focusable = false };
                swatch.AddToClassList("ps-swatch");
                swatch.style.backgroundColor = (Color)ColorPalette.Get(i);
                swatch.EnableInClassList("ps-on", i == current);
                swatch.SetEnabled(used.Contains(i));
                _popSwatches[i] = swatch;
                grid.Add(swatch);
            }
            var row = Kit.Row(_boxPop, KitGap.Sm);
            Kit.Button(row, "−", () => PopStep(-1), KitTone.Neutral, KitSize.Sm);
            var label = Kit.Text(row, value.HasValue ? value.Value.ToString() : "—", KitText.Body);
            label.AddToClassList("ps-pop-value");
            Kit.Button(row, "+", () => PopStep(1), KitTone.Neutral, KitSize.Sm);
            var at = Local(_popPos);
            var limit = _overlay.layout;
            _boxPop.style.left = Mathf.Clamp(at.x - 224, 8, Mathf.Max(8, limit.width - 240));
            _boxPop.style.top = Mathf.Clamp(at.y - 40, 8, Mathf.Max(8, limit.height - 200));
        }

        private void PopColor(int id)
        {
            if (_pop == Pop.Box) _boxes.SetSelectedColor(id);
            else _boxes.SetNewColor(id);
        }

        private void PopStep(int steps)
        {
            if (_pop == Pop.Box) { _boxes.StepSelectedValue(steps); return; }
            _addValue[_popCol] = PieceValue.Clamp(_addValue.GetValueOrDefault(_popCol, 10) + steps * PieceValue.Step);
            _dirty = true;
        }
    }
}
