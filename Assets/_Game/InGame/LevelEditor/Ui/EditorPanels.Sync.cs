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
        // ------------------------------------------------------------------ đồng bộ
        public void Sync()
        {
            _ai.Tick();
            _busy.Sync(_editor.IsBusy || _boxes.IsBusy, _editor.IsBusy ? _editor.BusyLabel : "Đang chơi thử level");
            PollPop();
            HandleKeys();
            _boxes.Poll();
            if (_genQueue != null) Kit.SetBusy(_genQueue, _boxes.IsBusy, "Đang chơi thử…");
            SyncBand();
            if (!_dirty && _shownVersion == _editor.SelectionVersion) return;
            if (_editor.Selection.Count > 0 && _editor.SelectionVersion != _shownVersion && _boxes.Selection.Count > 0) _boxes.Deselect();
            _dirty = false;
            _shownVersion = _editor.SelectionVersion;

            var p = _editor.Picture;
            var has = p != null && p.regions.Count > 0;
            Kit.SetText(_source, _editor.SourceName != null ? "Ảnh: " + _editor.SourceName : "Chưa nạp ảnh");
            Kit.SetShown(_frameRow, !has && _editor.HasSource);
            Kit.SetShown(_frameHead.Root, !has && _editor.HasSource);
            if (!has) { _frameW.Sync(_editor.FrameW); _frameH.Sync(_editor.FrameH); }
            Kit.SetShown(_generate, !has && _editor.HasSource);
            Kit.SetEnabled(_generate, !_editor.IsBusy);
            Kit.SetShown(_import, !has);
            Kit.SetShown(_paste, !has);
            Kit.SetEnabled(_paste, !_editor.IsBusy);
            Kit.SetShown(_aiBtn, !has && _editor.HasSource);
            Kit.SetShown(_thumb, _editor.HasSource);
            if (_thumb.image != _editor.SourceTexture) _thumb.image = _editor.SourceTexture;
            Kit.SetBusy(_generate, _editor.IsBusy, "Đang tạo tranh…");
            Kit.SetEnabled(_import, !_editor.IsBusy);
            _import.EnableInClassList(Kit.ToneClass(KitTone.Primary), !_editor.HasSource);
            _import.EnableInClassList(Kit.ToneClass(KitTone.Neutral), _editor.HasSource);
            Kit.SetEnabled(_clear, !_editor.IsBusy && (has || _editor.HasSource));
            _showValues.SetValueWithoutNotify(_editor.ShowValues);
            _showColor.SetValueWithoutNotify(_editor.ShowColor);
            _showGlass.SetValueWithoutNotify(_editor.ShowGlass);

            _slots.Sync(_boxes.Queue.slots);
            SyncQueue(p);
            SyncMechanic();
            SyncInspector(p, has);
        }

        private void SyncQueue(PictureProperty p)
        {
            _balance.Clear();
            var balance = _boxes.Balance(p);
            foreach (var pair in balance.OrderBy(x => x.Key))
            {
                var color = pair.Key;
                var chip = Kit.Button(_balance, (pair.Value > 0 ? "+" : "−") + Mathf.Abs(pair.Value), () => _boxes.SetNewColor(color), KitTone.Neutral, KitSize.Sm, "Bấm để chọn màu cho hộp mới");
                Tint(chip, (Color)ColorPalette.Get(color), color == _boxes.NewColor);
            }
            if (balance.Count == 0 && p != null && p.regions.Count > 0) Kit.Text(_balance, "✔ Cát các hộp khớp với tranh", KitText.Dim);

            BuildColumns(_columns, true);
            BuildColumns(_mechColumns, false);
            SyncPop();
        }

        private void BuildColumns(VisualElement host, bool edit)
        {
            host.Clear();
            var queue = _boxes.Queue;
            var count = queue.ColumnCount;
            var shown = edit ? Mathf.Min(count + 1, BoxQueueProperty.MaxColumns) : count;
            var groups = _boxes.Groups();
            for (var c = 0; c < shown; c++)
            {
                var column = c;
                var col = Kit.Col(host, KitGap.Xs, "ps-col");
                Kit.Text(col, column == count ? "Mới" : "Cột " + column, KitText.Dim);
                if (edit)
                {
                    var add = Kit.Row(col, KitGap.Xs);
                    var amount = _addValue.GetValueOrDefault(column, 10);
                    Kit.Stepper(add, amount, PieceValue.Min, PieceValue.Max, v => _addValue[column] = v, null, PieceValue.Step, false);
                    add.userData = column;
                    add.AddToClassList("ps-add");
                    var plus = Kit.Button(add, "+", () => _boxes.Add(column, _boxes.NewColor, _addValue.GetValueOrDefault(column, 10)), KitTone.Neutral, KitSize.Sm, "Thêm hộp");
                    Tint(plus, (Color)ColorPalette.Get(_boxes.NewColor), false);
                }
                foreach (var box in queue.Column(column)) AddChip(col, box, groups, edit);
            }
        }

        private void AddChip(VisualElement col, BoxData box, List<PairGroup> groups, bool edit)
        {
            var id = box.id;
            var covered = box.hidden && _editor.Cover;
            var chip = Kit.Button(col, "", () => OnChip(id, edit), KitTone.Neutral, KitSize.Sm);
            chip.AddToClassList("ps-chip");
            chip.userData = id;
            if (edit) chip.clickable = null;
            var color = covered ? HiddenGrey : (Color)ColorPalette.Get(box.colorId);
            Tint(chip, color, _boxes.IsSelected(id));
            var ink = color.grayscale > 0.6f ? new Color(0.06f, 0.09f, 0.16f) : new Color(0.97f, 0.98f, 0.99f);
            Badge(chip, (covered ? "? " : "") + box.value, ink, null);
            if (box.frozen > 0) Badge(chip, "❄" + box.frozen, Color.white, new Color(0.31f, 0.58f, 0.79f));
            var group = groups.FirstOrDefault(g => g.Members.Contains(box));
            if (group != null)
            {
                var tint = group.Fault.Length > 0 ? new Color(0.94f, 0.27f, 0.27f) : PairColors[group.Index % PairColors.Length];
                Badge(chip, "P" + (group.Index + 1), Color.black, tint);
                chip.style.borderBottomColor = chip.style.borderTopColor = chip.style.borderLeftColor = chip.style.borderRightColor = tint;
                chip.style.borderBottomWidth = chip.style.borderTopWidth = chip.style.borderLeftWidth = chip.style.borderRightWidth = 2;
            }
            if (!edit) chip.RegisterCallback<PointerDownEvent>(e => { if (e.button == 1) OnChipRight(id); });
        }

        private static void Badge(VisualElement chip, string text, Color ink, Color? fill)
        {
            var label = Kit.Text(chip, text, KitText.Caption);
            label.pickingMode = PickingMode.Ignore;
            label.style.color = ink;
            if (fill.HasValue) { label.style.backgroundColor = fill.Value; label.AddToClassList("ps-badge"); }
        }

        // Bấm hộp theo chế độ của mechanic đang bật
        private void OnChip(string id, bool edit)
        {
            if (!edit && _mechanic == Hidden && _editor.HiddenMode) _boxes.ToggleHidden(id);
            else if (!edit && _mechanic == Pair && _boxes.PairMode) _boxes.PairClick(id);
            else if (!edit && _mechanic == Frozen && _ice) _boxes.Freeze(id);
            else PickBox(id);
        }

        private void OnChipRight(string id)
        {
            if (_mechanic == Pair && _boxes.PairMode) _boxes.PairRemove(id);
            else if (_mechanic == Frozen && _ice) _boxes.Unfreeze(id);
        }

        private static void Tint(Button b, Color c, bool on)
        {
            b.style.backgroundColor = c;
            b.style.color = c.grayscale > 0.6f ? new Color(0.06f, 0.09f, 0.16f) : new Color(0.97f, 0.98f, 0.99f);
            Kit.SetOn(b, on);
            b.EnableInClassList("ps-on", on);
        }

        // Thông tin chung của tranh khi không chọn gì
        private void FillInfo(PictureProperty p)
        {
            var sand = new SortedDictionary<int, int>();
            var manual = 0;
            foreach (var r in p.regions)
            {
                sand[r.colorId] = sand.GetValueOrDefault(r.colorId) + r.value;
                if (r.valueManual) manual++;
            }
            Kit.SetText(_infoFrame, $"Khung: {p.width}x{p.height}");
            Kit.SetText(_infoCount, $"{p.regions.Count} mảnh · {sand.Count} màu");
            Kit.SetText(_infoSand, "Tổng cát: " + sand.Values.Sum());
            Kit.SetText(_infoManual, $"Đặt tay: {manual} mảnh");
            _infoColors.Clear();
            foreach (var pair in sand.OrderBy(x => x.Key))
            {
                var id = pair.Key;
                var color = (Color)ColorPalette.Get(id);
                var cell = Kit.Button(_infoColors, pair.Value.ToString(), () => _editor.SelectByColor(id), KitTone.Neutral, KitSize.Sm, $"Màu #{id}: bấm để chọn mọi mảnh màu này");
                cell.AddToClassList("ps-count");
                Tint(cell, color, false);
            }
            FillSmall();
        }

        private void FillSmall()
        {
            _infoSmall.Clear();
            var small = _editor.SmallPieces;
            if (small.Count == 0)
            {
                Kit.Text(_infoSmall, "✔ Không có mảnh nào quá bé", KitText.Dim);
                return;
            }
            Kit.Text(_infoSmall, $"{small.Count} mảnh có vòng tròn lớn nhất bên trong nhỏ hơn 1,2% cạnh tranh. Nên gộp vào mảnh kề có biên chung dài nhất:", KitText.Dim, true);
            foreach (var s in small)
            {
                var piece = s;
                var row = Kit.Row(_infoSmall, KitGap.Xs, "ps-mrow");
                var text = Kit.Text(row, $"#{piece.Index + 1} (bán kính {Mathf.RoundToInt((float)piece.Radius)})" + (piece.Target >= 0 ? $" → #{piece.Target + 1}" : " (không có mảnh kề)"), KitText.Body);
                text.AddToClassList("ps-grow");
                Kit.Button(row, "Chọn", () => _editor.SelectMany(new[] { piece.Index }, false), KitTone.Neutral, KitSize.Sm);
                if (piece.Target >= 0)
                    Kit.Button(row, "Gộp", () => { _editor.SelectMany(new[] { piece.Target, piece.Index }, false); _editor.MergeSelected(); }, KitTone.Neutral, KitSize.Sm);
            }
            if (small.Any(x => x.Target >= 0)) Kit.Button(_infoSmall, $"Gộp tất cả {small.Count} mảnh", _editor.MergeAllSmall, KitTone.Neutral, KitSize.Sm);
        }

        private void SyncInspector(PictureProperty p, bool has)
        {
            var box = _boxes.SelectedBox;
            var count = p?.regions.Count ?? 0;
            var picked = _editor.Selection.Where(i => i < count).ToList();
            var piece = box == null && has && picked.Count > 0;
            SyncTip(piece);

            Kit.SetShown(_boxBox, box != null);
            Kit.SetShown(_pieceBox, piece);
            Kit.SetShown(_empty, !has);
            Kit.SetShown(_info, has && box == null && !piece);
            if (has && box == null && !piece) FillInfo(p);

            if (box != null)
            {
                var boxes = _boxes.SelectedBoxes;
                var boxColors = boxes.Select(x => x.colorId).Distinct().ToList();
                var frozen = boxes.Where(x => x.frozen > 0).ToList();
                Kit.SetText(_boxHead, boxes.Count == 1 ? "Hộp " + _boxes.PosOf(box) : $"{boxes.Count} hộp");
                Kit.SetText(_boxSub, boxColors.Count == 1 ? $"Màu #{boxColors[0]}" : $"{boxColors.Count} màu khác nhau");
                _boxValue.Sync(box.value);
                Kit.SetShown(_boxDiffer, boxes.Any(x => x.value != box.value));
                for (var i = 0; i < _boxSwatches.Length; i++) _boxSwatches[i].EnableInClassList("ps-on", boxColors.Count == 1 && i == boxColors[0]);
                Kit.SetText(_boxHide, boxes.All(x => x.hidden) ? "Hiện màu (H)" : "Ẩn màu (H)");
                Kit.SetShown(_boxIdle, frozen.Count == 0);
                Kit.SetShown(_boxFrozen, frozen.Count > 0);
                Kit.SetText(_freezeNow, $"Băng (mặc định {_boxes.FrozenDefault(box)})");
                if (frozen.Count > 0) _boxFreeze.Sync(frozen[0].frozen);
                Kit.SetText(_boxDelete, boxes.Count > 1 ? $"Xoá {boxes.Count} hộp" : "Xoá hộp");
            }
            if (!piece) return;

            var regions = picked.Select(i => p.regions[i]).ToList();
            var first = regions[0];
            var colors = regions.Select(r => r.colorId).Distinct().ToList();
            var sameValue = regions.All(r => r.value == first.value);
            Kit.SetText(_head, picked.Count == 1 ? $"Mảnh #{picked[0] + 1}" : $"{picked.Count} mảnh");
            Kit.SetText(_headSub, picked.Count == 1 ? $"Màu #{first.colorId} · gợi ý theo diện tích: {PieceValue.Suggest(p, first)}" : colors.Count == 1 ? $"Màu #{colors[0]}" : $"{colors.Count} màu khác nhau");
            _value.Sync(first.value);
            Kit.SetShown(_differ, !sameValue);
            Kit.SetText(_pieceHide, regions.All(r => r.hidden) ? "Hiện màu (H)" : "Ẩn màu (H)");
            Kit.SetShown(_cutBtn, picked.Count == 1);
            SyncPen();
            Kit.SetShown(_mergeBtn, picked.Count >= 2);
            Kit.SetEnabled(_mergeBtn, !_editor.IsBusy);
            Kit.SetText(_deleteBtn, "Xoá mảnh");
            for (var i = 0; i < _pieceSwatches.Length; i++) _pieceSwatches[i].EnableInClassList("ps-on", colors.Count == 1 && i == colors[0]);
        }
    }
}
