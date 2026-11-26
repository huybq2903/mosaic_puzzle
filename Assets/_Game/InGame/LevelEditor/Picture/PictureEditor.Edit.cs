using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Falcon.InGame.Core;
using Falcon.InGame.LevelEditor.Gen;
using Falcon.Shared.BaseLevelEditor;
using Falcon.Shared.EditorUi;
using Newtonsoft.Json;
using SFB;
using UnityEngine;
using UnityEngine.InputSystem;
using Debug = UnityEngine.Debug;

namespace Falcon.InGame.LevelEditor
{
    public partial class PictureEditor
    {
        // ------------------------------------------------------------------ sửa mảnh đang chọn
        public void SetColor(int colorId)
        {
            if (_selection.Count == 0 || _selection.All(i => !Valid(i) || _propertyData.regions[i].colorId == colorId)) return;
            Commit(() => { foreach (var i in _selection) if (Valid(i)) _propertyData.regions[i].colorId = colorId; });
        }

        public void SetValue(int value)
        {
            var v = PieceValue.Clamp(value);
            if (_selection.Count == 0 || _selection.All(i => !Valid(i) || (_propertyData.regions[i].value == v && _propertyData.regions[i].valueManual))) return;
            Commit(() =>
            {
                foreach (var i in _selection)
                {
                    if (!Valid(i)) continue;
                    _propertyData.regions[i].value = v;
                    _propertyData.regions[i].valueManual = true;
                }
            });
        }

        /// <summary>Ẩn màu mảnh đang chọn; nếu tất cả đã ẩn thì hiện lại.</summary>
        public void ToggleSelectedHidden()
        {
            var picked = _selection.Where(Valid).ToList();
            if (picked.Count == 0) return;
            var hide = !picked.All(i => _propertyData.regions[i].hidden);
            Commit(() => { foreach (var i in picked) _propertyData.regions[i].hidden = hide; });
        }

        public void ToggleHidden(int index)
        {
            if (!Valid(index)) return;
            Commit(() => _propertyData.regions[index].hidden = !_propertyData.regions[index].hidden);
        }

        public void DeleteSelected()
        {
            if (IsBusy) return;
            var picked = _selection.Where(Valid).Distinct().OrderByDescending(i => i).ToList();
            if (picked.Count == 0) return;
            _selection.Clear();
            SelectionVersion++;
            Commit(() =>
            {
                DropDecor(picked);
                foreach (var i in picked) _propertyData.regions.RemoveAt(i);
            });
            LevelEditorMainUI.Log($"Đã xoá {picked.Count} mảnh");
        }

        // Xoá nét trang trí nằm trong các mảnh sắp xoá (quá nửa số đỉnh nằm trong mảnh)
        private void DropDecor(List<int> gone)
        {
            var p = _propertyData;
            bool Owned(int[] ring)
            {
                var n = ring.Length / 2;
                var inside = 0;
                for (var k = 0; k < n; k++)
                {
                    var pt = new Vector2(ring[k * 2], ring[k * 2 + 1]);
                    if (gone.Any(i => Inside(p.regions[i].points, pt))) inside++;
                }
                return n > 0 && inside * 2 > n;
            }
            p.decor?.RemoveAll(Owned);
        }

        // ------------------------------------------------------------------ gộp
        private ShapeSettings Shape()
        {
            var g = _propertyData.gen;
            return new ShapeSettings { CurveSmooth = g.curveSmooth, FitTolerance = g.fitTolerance, SmoothScale = g.smoothScale };
        }

        /// <summary>Gộp các mảnh đang chọn vào mảnh chọn đầu tiên (mảnh chưa kề đợi tới khi kề).</summary>
        public void MergeSelected()
        {
            if (IsBusy || _selection.Count < 2) return;
            var picked = _selection.Where(Valid).ToList();
            var regions = GenRegions();
            var p = _propertyData;
            var shape = Shape();
            var baseline = Capture();
            string error = null;
            RunBusy("Đang gộp mảnh…", () => PictureOps.MergeMany(p.width, p.height, p.unit, shape, regions, picked, out error), merged =>
            {
                if (Capture() != baseline) { LevelEditorMainUI.Warn("Dữ liệu đã đổi trong lúc gộp, bỏ kết quả"); return; }
                if (merged == null) { LevelEditorMainUI.Warn(error); return; }
                ApplyMerged(merged, new[] { picked[0] + 1 });
                LevelEditorMainUI.Log($"Gộp {picked.Count} mảnh thành 1");
            });
        }

        // Thay danh sách mảnh bằng kết quả gộp: giữ trường của mảnh gốc (theo Id), đặt hình mới; mảnh đích tính lại số cát
        private void ApplyMerged(List<GenRegion> merged, IEnumerable<int> targetIds)
        {
            var targets = new HashSet<int>(targetIds);
            var focus = new List<int>();
            _selection.Clear();
            SelectionVersion++;
            Commit(() =>
            {
                var originals = _propertyData.regions;
                var next = new List<RegionData>();
                foreach (var g in merged)
                {
                    var r = originals[g.Id - 1];
                    r.points = g.Points;
                    r.holes = g.Holes;
                    if (targets.Contains(g.Id)) { r.valueManual = false; focus.Add(next.Count); }
                    next.Add(r);
                }
                _propertyData.regions = next;
                _propertyData.EnsureIds();
                _propertyData.ClampToFrame();
                PieceValue.Assign(_propertyData);
            });
            SelectMany(focus.Take(1), false);
        }

        // ------------------------------------------------------------------ mảnh quá bé
        /// <summary>Mảnh quá bé để chứa số, kèm mảnh kề nên gộp vào (tính lại sau mỗi lần sửa).</summary>
        public IReadOnlyList<SmallPiece> SmallPieces
        {
            get
            {
                if (_small != null) return _small;
                if (!HasPicture) return _small = new List<SmallPiece>();
                return _small = PictureOps.FindSmall(_propertyData.width, _propertyData.height, Mathf.Max(1, _propertyData.unit), GenRegions());
            }
        }

        public void MergeAllSmall()
        {
            var small = SmallPieces.ToList();
            if (IsBusy || small.Count == 0) return;
            var regions = GenRegions();
            var p = _propertyData;
            var shape = Shape();
            var baseline = Capture();
            List<int> targets = null;
            var count = 0;
            RunBusy("Đang gộp mảnh nhỏ…", () => PictureOps.MergeSmall(p.width, p.height, p.unit, shape, regions, small, out count, out targets), merged =>
            {
                if (Capture() != baseline) { LevelEditorMainUI.Warn("Dữ liệu đã đổi trong lúc gộp, bỏ kết quả"); return; }
                if (merged == null) { LevelEditorMainUI.Warn("Không gộp được: các mảnh không liền nhau"); return; }
                ApplyMerged(merged, targets);
                LevelEditorMainUI.Log($"Đã gộp {count} mảnh quá bé");
            });
        }

        // ------------------------------------------------------------------ chia mảnh
        private int _splitCount = Subdivide.MinCount;
        private bool _splitAuto = true;

        /// <summary>Kiểu chia (chỉ số trong Subdivide.Patterns; 0 = tự động).</summary>
        public int SplitPattern { get; set; }

        /// <summary>Số mảnh muốn chia: mặc định gợi ý theo diện tích mảnh chính, người dùng đổi tay thì giữ.</summary>
        public int SplitCount
        {
            get => _splitAuto ? RecommendSplit(Selected) : _splitCount;
            set
            {
                _splitCount = Mathf.Clamp(value, Subdivide.MinCount, Subdivide.MaxCount);
                _splitAuto = false;
            }
        }

        /// <summary>Số mảnh gợi ý cho một mảnh: ô con cỡ một mảnh trong game.</summary>
        public int RecommendSplit(int index)
        {
            if (!HasPicture || !Valid(index)) return Subdivide.MinCount;
            var unit = Mathf.Max(1, _propertyData.unit);
            _map ??= Rasterizer.Rasterize(_propertyData.width, _propertyData.height, unit, GenRegions());
            var pixels = 0;
            foreach (var id in _map.Reg) if (id == index + 1) pixels++;
            return Subdivide.Recommend((double)pixels / ((long)_map.W * _map.H));
        }

        /// <summary>Chia các mảnh đang chọn thành SplitCount ô theo SplitPattern.</summary>
        public void SubdivideSelected()
        {
            if (IsBusy || _selection.Count == 0 || !HasPicture) return;
            var picked = _selection.Where(Valid).ToList();
            var count = SplitCount;
            var pattern = SplitPattern;
            var regions = GenRegions();
            var p = _propertyData;
            var shape = Shape();
            var baseline = Capture();
            string error = null;
            RunBusy("Đang chia mảnh…", () => PictureOps.SubdivideRegions(p.width, p.height, p.unit, shape, regions, picked, count, pattern, out error), result =>
            {
                if (Capture() != baseline) { LevelEditorMainUI.Warn("Dữ liệu đã đổi trong lúc chia, bỏ kết quả"); return; }
                if (result == null) { LevelEditorMainUI.Warn(error); return; }
                ApplySplit(result);
                LevelEditorMainUI.Log($"Chia {picked.Count} mảnh thành {picked.Count + result.Count(r => r.Id == 0)} mảnh");
            });
        }

        // Mảnh cũ giữ trường của mình (đặt hình, màu mới); ô mới thừa hưởng ẩn màu của mảnh gốc; mảnh bị chia tính lại số cát
        private void ApplySplit(List<GenRegion> result)
        {
            _selection.Clear();
            SelectionVersion++;
            Commit(() =>
            {
                var originals = _propertyData.regions;
                var split = new HashSet<int>(result.Where(g => g.Parent > 0).Select(g => g.Parent));
                var next = new List<RegionData>();
                foreach (var g in result)
                {
                    if (g.Id > 0)
                    {
                        var r = originals[g.Id - 1];
                        r.points = g.Points;
                        r.holes = g.Holes;
                        r.colorId = g.ColorId;
                        if (split.Contains(g.Id)) r.valueManual = false;
                        next.Add(r);
                    }
                    else next.Add(new RegionData { points = g.Points, holes = g.Holes, colorId = g.ColorId, valueManual = false, hidden = originals[g.Parent - 1].hidden });
                }
                _propertyData.regions = next;
                _propertyData.EnsureIds();
                _propertyData.ClampToFrame();
                PieceValue.Assign(_propertyData);
            });
        }
    }
}
