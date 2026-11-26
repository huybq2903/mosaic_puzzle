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
        // ------------------------------------------------------------------ chọn
        /// <summary>Các mảnh đang chọn, theo thứ tự chọn (mảnh đầu là mảnh chính).</summary>
        public IReadOnlyList<int> Selection => _selection;

        public int Selected => _selection.Count > 0 ? _selection[0] : -1;

        public int SelectionVersion { get; private set; }

        /// <summary>Khung đang kéo chọn trên màn hình (pixel, y hướng lên); null nếu không kéo.</summary>
        public Rect? Band { get; private set; }

        public bool IsSelected(int index) => _selection.Contains(index);

        private bool Valid(int i) => _propertyData != null && i >= 0 && i < _propertyData.regions.Count;

        private void SelectionChanged()
        {
            SelectionVersion++;
            _splitAuto = true;
            StopPen();
            _view?.SetSelection(_selection.Count > 0 ? _selection.ToArray() : null);
            Changed?.Invoke();
        }

        /// <summary>Chọn đúng một mảnh; chỉ số âm thì bỏ chọn.</summary>
        public void Select(int index) => SelectMany(index >= 0 ? new[] { index } : Array.Empty<int>(), false);

        public void SelectMany(IEnumerable<int> indices, bool add)
        {
            if (!add) _selection.Clear();
            foreach (var i in indices) if (Valid(i) && !_selection.Contains(i)) _selection.Add(i);
            SelectionChanged();
        }

        public void Toggle(int index)
        {
            if (!Valid(index)) return;
            if (!_selection.Remove(index)) _selection.Add(index);
            SelectionChanged();
        }

        public void ClearSelection()
        {
            if (_selection.Count == 0) return;
            _selection.Clear();
            SelectionChanged();
        }

        /// <summary>Chọn mọi mảnh cùng màu.</summary>
        public void SelectByColor(int colorId)
        {
            if (_propertyData == null) return;
            var ids = _propertyData.regions.Select((r, i) => (r, i)).Where(x => x.r.colorId == colorId).Select(x => x.i).ToList();
            if (ids.Count == 0) return;
            SelectMany(ids, false);
            LevelEditorMainUI.Log($"Chọn {ids.Count} mảnh màu #{colorId}");
        }

        // ------------------------------------------------------------------ click và kéo khung chọn
        private Vector2 ToPicture(Vector2 screen)
        {
            var world = _camera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, -_camera.transform.position.z));
            return _view.WorldToPicture(world) * Mathf.Max(1, _propertyData.unit);
        }

        private void HandlePick()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;
            if (!_camera) _camera = FindFirstObjectByType<Camera>();
            if (!_camera) return;
            var screen = mouse.position.ReadValue();
            UpdateHover(screen);
            if (_run != null && HandleRun(mouse, screen)) return;
            if (Pen != PenMode.None) { HandlePen(mouse, screen); return; }

            if (mouse.leftButton.wasPressedThisFrame && !EditorUiHost.IsPointerOverUi && !EditorUiHost.KeyboardBusy)
            {
                var kbd = Keyboard.current;
                var extra = kbd != null && (kbd.shiftKey.isPressed || kbd.ctrlKey.isPressed);
                if (!extra && !HiddenMode && TryBeginRun(screen)) return;
                _pressing = true;
                _banding = false;
                _pressScreen = screen;
                _pressPicture = ToPicture(screen);
                var kb = Keyboard.current;
                _pressAdd = kb != null && (kb.shiftKey.isPressed || kb.ctrlKey.isPressed);
            }
            if (!_pressing) return;

            if (mouse.leftButton.isPressed)
            {
                if (!_banding && (screen - _pressScreen).magnitude > DragPixels && !HiddenMode) _banding = true;
                Band = _banding ? Rect.MinMaxRect(Mathf.Min(_pressScreen.x, screen.x), Mathf.Min(_pressScreen.y, screen.y), Mathf.Max(_pressScreen.x, screen.x), Mathf.Max(_pressScreen.y, screen.y)) : null;
                return;
            }

            _pressing = false;
            var wasBanding = _banding;
            _banding = false;
            Band = null;
            if (wasBanding)
            {
                SelectMany(RegionsInRect(_pressPicture, ToPicture(screen)), _pressAdd);
                return;
            }
            var hit = HitTest(_pressPicture);
            if (HiddenMode && hit >= 0) ToggleHidden(hit);
            else if (hit < 0) { if (!_pressAdd) ClearSelection(); }
            else if (_pressAdd) Toggle(hit);
            else if (_selection.Count == 1 && _selection[0] == hit) ClearSelection();
            else Select(hit);
        }

        // Rê chuột vào mảnh thì viền sáng nhẹ mảnh đó
        private void UpdateHover(Vector2 screen)
        {
            var idle = _run == null && Pen == PenMode.None && !_pressing && !HiddenMode && !EditorUiHost.IsPointerOverUi;
            _view.SetHighlight(idle ? HitTest(ToPicture(screen)) : -1);
        }

        // mảnh chứa điểm (toạ độ như points); -1 nếu không có
        private int HitTest(Vector2 p)
        {
            for (var i = 0; i < _propertyData.regions.Count; i++)
            {
                var r = _propertyData.regions[i];
                if (!Inside(r.points, p)) continue;
                var inHole = false;
                if (r.holes != null) foreach (var h in r.holes) if (Inside(h, p)) { inHole = true; break; }
                if (!inHole) return i;
            }
            return -1;
        }

        private static bool Inside(int[] ring, Vector2 p)
        {
            var ins = false;
            var n = ring.Length / 2;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                float xi = ring[i * 2], yi = ring[i * 2 + 1], xj = ring[j * 2], yj = ring[j * 2 + 1];
                if ((yi > p.y) == (yj > p.y)) continue;
                if (p.x < xi + (p.y - yi) * (xj - xi) / (yj - yi)) ins = !ins;
            }
            return ins;
        }

        private List<GenRegion> GenRegions() => _propertyData.regions.Select((r, i) => new GenRegion { Id = i + 1, Points = r.points, Holes = r.holes, ColorId = r.colorId }).ToList();

        // Mọi mảnh có pixel trong khung (toạ độ points)
        private List<int> RegionsInRect(Vector2 a, Vector2 b)
        {
            var unit = Mathf.Max(1, _propertyData.unit);
            _map ??= Rasterizer.Rasterize(_propertyData.width, _propertyData.height, unit, GenRegions());
            int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, b.x) / unit)), x1 = Mathf.Min(_map.W - 1, Mathf.FloorToInt(Mathf.Max(a.x, b.x) / unit));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, b.y) / unit)), y1 = Mathf.Min(_map.H - 1, Mathf.FloorToInt(Mathf.Max(a.y, b.y) / unit));
            var set = new SortedSet<int>();
            for (var y = y0; y <= y1; y++)
                for (var x = x0; x <= x1; x++)
                {
                    var id = _map.Reg[y * _map.W + x];
                    if (id > 0) set.Add(id - 1);
                }
            return set.ToList();
        }
    }
}
