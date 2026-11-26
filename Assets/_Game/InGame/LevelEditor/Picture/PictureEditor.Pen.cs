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
        // ------------------------------------------------------------------ bút tách mảnh
        private const float SnapFraction = 0.012f, ClosePixels = 18f;
        private readonly List<Vector2> _pen = new();
        private int _penRegion = -1;

        public PenMode Pen { get; private set; }

        public bool PenActive => Pen != PenMode.None || _run != null;

        public int PenCount => _pen.Count;

        /// <summary>Bật hoặc tắt bút tách trên mảnh đang chọn (đúng một mảnh); bấm lại cùng chế độ thì tắt.</summary>
        public void SetPen(PenMode mode)
        {
            if (IsBusy || _selection.Count != 1 || !Valid(_selection[0])) return;
            var next = Pen == mode ? PenMode.None : mode;
            StopPen();
            if (next != PenMode.None) { Pen = next; _penRegion = _selection[0]; }
            Changed?.Invoke();
        }

        /// <summary>Huỷ đường đang vẽ và tắt bút.</summary>
        public void CancelPen()
        {
            if (!PenActive) return;
            StopPen();
            Changed?.Invoke();
        }

        private void StopPen()
        {
            StopRun();
            Pen = PenMode.None;
            _pen.Clear();
            _penRegion = -1;
            if (!_view) return;
            _view.SetCutPreview(false);
            _view.SetAnchorMarkers(null);
            _view.SetSnapMarker(false);
        }

        private Vector2 PenPoint(Vector2 screen, out bool snapped)
        {
            var cell = ToPicture(screen) / Mathf.Max(1, _propertyData.unit);
            snapped = false;
            var near = PictureOps.NearestBoundaryPoint(GenRegions(), _propertyData.unit, cell.x, cell.y, SnapFraction * _propertyData.height);
            if (near == null) return cell;
            snapped = true;
            return new Vector2((float)near.Value.x, (float)near.Value.y);
        }

        private void HandlePen(Mouse mouse, Vector2 screen)
        {
            if (!Valid(_penRegion)) { CancelPen(); return; }
            var over = EditorUiHost.IsPointerOverUi;
            var cursor = PenPoint(screen, out var snapped);
            var kb = Keyboard.current;

            if (!over && !EditorUiHost.KeyboardBusy)
            {
                if (mouse.leftButton.wasPressedThisFrame)
                {
                    var closing = _pen.Count >= 3 && Vector2.Distance(_camera.WorldToScreenPoint(_view.PictureToWorld(_pen[0])), screen) < ClosePixels;
                    if (closing) { FinishPen(true); return; }
                    _pen.Add(cursor);
                }
                else if (mouse.rightButton.wasPressedThisFrame) { FinishPen(false); return; }
                else if (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) { FinishPen(false); return; }
            }
            DrawPen(cursor, snapped && !over, over);
        }

        private void DrawPen(Vector2 cursor, bool snapped, bool over)
        {
            var pts = _pen.Select(p => _view.PictureToWorld(p)).ToList();
            _view.SetAnchorMarkers(pts, -1);
            if (!over) pts.Add(_view.PictureToWorld(cursor));
            _view.SetCutPreview(pts.Count >= 2, pts);
            _view.SetSnapMarker(snapped, _view.PictureToWorld(cursor));
        }

        private void FinishPen(bool closed)
        {
            var mode = Pen;
            var index = _penRegion;
            var cells = _pen.ToList();
            StopPen();
            Changed?.Invoke();
            CutAlong(index, cells, closed);
        }

        private void CutAlong(int index, List<Vector2> cells, bool closed)
        {
            if (cells.Count < 2) { LevelEditorMainUI.Warn("Đường cắt quá ngắn"); return; }
            var path = cells.Select(c => ((double)c.x, (double)c.y)).ToList();
            if (closed) path.Add(path[0]);
            var regions = GenRegions();
            var p = _propertyData;
            var shape = Shape();
            var baseline = Capture();
            string error = null;
            RunBusy("Đang cắt mảnh…", () => PictureOps.SplitRegion(p.width, p.height, p.unit, shape, regions, index, path, out error), result =>
            {
                if (Capture() != baseline) { LevelEditorMainUI.Warn("Dữ liệu đã đổi trong lúc cắt, bỏ kết quả"); return; }
                if (result == null) { LevelEditorMainUI.Warn(error); return; }
                ApplySplit(result);
                LevelEditorMainUI.Log($"Cắt mảnh #{index + 1} thành {result.Count - regions.Count + 1} mảnh");
            });
        }
    }

    public enum PenMode { None, Cut }
}
