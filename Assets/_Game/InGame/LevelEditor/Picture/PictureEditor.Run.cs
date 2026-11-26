using System.Collections.Generic;
using System.Linq;
using Falcon.InGame.Core;
using Falcon.InGame.LevelEditor.Gen;
using Falcon.Shared.BaseLevelEditor;
using Falcon.Shared.EditorUi;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Falcon.InGame.LevelEditor
{
    // Sửa biên: bấm biên mảnh đang chọn, kéo điểm neo và tay cầm Bézier, Enter chốt
    public partial class PictureEditor
    {
        private const float PickPixels = 10f, EditPixels = 8f, BarPixels = 40f, PointSpacing = 2f;

        private sealed class Anchor
        {
            public Vector2 Pos, Out, In;

            public Anchor Copy() => new() { Pos = Pos, Out = Out, In = In };
        }

        private enum Drag { None, Anchor, Out, In }

        private readonly List<Anchor> _anchors = new();
        private EdgeRun _run;
        private int _anchor = -1;
        private bool _runDirty;
        private Drag _drag;

        public bool RunEditing => _run != null;

        private Vector2 CellOf(Vector2 screen) => ToPicture(screen) / Mathf.Max(1, _propertyData.unit);

        private Vector2 ScreenOf(Vector2 cell) => _camera.WorldToScreenPoint(_view.PictureToWorld(cell));

        private float CellsPerPixel()
        {
            var a = ScreenOf(Vector2.zero);
            var b = ScreenOf(Vector2.right);
            return 1f / Mathf.Max(1e-4f, Vector2.Distance(a, b));
        }

        // Bấm gần biên chung của mảnh đang chọn thì nạp đoạn biên đó vào bút; false nếu không bấm trúng
        private bool TryBeginRun(Vector2 screen)
        {
            if (_selection.Count != 1 || !Valid(_selection[0]) || !HasPicture) return false;
            var index = _selection[0];
            var unit = Mathf.Max(1, _propertyData.unit);
            var cell = CellOf(screen);
            var regions = GenRegions();
            var radius = EditPixels * CellsPerPixel();
            if (!EdgeSkeleton.NearRing(regions, unit, index, cell.x, cell.y, radius)) return false;
            var run = EdgeSkeleton.Nearest(EdgeSkeleton.RunsOf(regions, index), unit, cell.x, cell.y, radius);
            if (run == null) return false;
            var cubics = EdgeSkeleton.Fit(run, unit);
            if (cubics.Count == 0) return false;

            _anchors.Clear();
            for (var i = 0; i <= cubics.Count; i++)
            {
                var v = i < cubics.Count ? cubics[i].P0 : cubics[i - 1].P3;
                var pos = new Vector2((float)v.X, (float)v.Y);
                var a = new Anchor { Pos = pos };
                if (i < cubics.Count) a.Out = new Vector2((float)cubics[i].P1.X, (float)cubics[i].P1.Y) - pos;
                if (i > 0) a.In = new Vector2((float)cubics[i - 1].P2.X, (float)cubics[i - 1].P2.Y) - pos;
                _anchors.Add(a);
            }
            _run = run;
            _anchor = -1;
            _runDirty = false;
            _drag = Drag.None;
            DrawRun();
            LevelEditorMainUI.Log("Sửa biên: kéo điểm hoặc đầu thanh cong, Shift+click biên để thêm điểm, Del xoá điểm, Enter chốt, Esc huỷ");
            Changed?.Invoke();
            return true;
        }

        private void StopRun()
        {
            if (_run == null) return;
            _run = null;
            _anchors.Clear();
            _anchor = -1;
            _drag = Drag.None;
            _runDirty = false;
            if (!_view) return;
            _view.SetCutPreview(false);
            _view.SetAnchorMarkers(null);
            _view.SetHandles(null);
        }

        // Hai đầu thanh cong của điểm i: (đầu ra, đầu vào); điểm thẳng hiện thanh ngắn theo đường qua hai điểm kề
        private (Vector2 outEnd, Vector2 inEnd) HandleEnds(int i)
        {
            var a = _anchors[i];
            Vector2 dir;
            if (a.Out != Vector2.zero) dir = a.Out;
            else if (a.In != Vector2.zero) dir = -a.In;
            else
            {
                var prev = i > 0 ? _anchors[i - 1].Pos : a.Pos;
                var next = i < _anchors.Count - 1 ? _anchors[i + 1].Pos : a.Pos;
                var d = next - prev;
                dir = d.sqrMagnitude < 1e-10f ? Vector2.right : d.normalized;
                dir *= BarPixels * CellsPerPixel();
            }
            var outEnd = a.Pos + (a.Out != Vector2.zero ? a.Out : dir);
            var inEnd = a.Pos + (a.In != Vector2.zero ? a.In : a.Out != Vector2.zero ? -a.Out : -dir);
            return (outEnd, inEnd);
        }

        private static Vector2 Bezier(Anchor a, Anchor b, float t)
        {
            Vector2 p0 = a.Pos, p1 = a.Pos + a.Out, p3 = b.Pos, p2 = b.Pos + b.In;
            var u = 1f - t;
            return u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3;
        }

        // Chuỗi Bézier giữa các điểm neo thành danh sách điểm (tay cầm 0 = đoạn thẳng)
        private List<Vector2> Flatten()
        {
            var o = new List<Vector2> { _anchors[0].Pos };
            for (var i = 0; i + 1 < _anchors.Count; i++)
            {
                var a = _anchors[i];
                var b = _anchors[i + 1];
                var curved = a.Out != Vector2.zero || b.In != Vector2.zero;
                var n = curved ? Mathf.Max(4, Mathf.Min(80, Mathf.CeilToInt((Vector2.Distance(a.Pos, b.Pos) + a.Out.magnitude + b.In.magnitude) / PointSpacing))) : 1;
                for (var k = 1; k <= n; k++) o.Add(Bezier(a, b, (float)k / n));
            }
            return o;
        }

        private void DrawRun()
        {
            if (_run == null || !_view) return;
            _view.SetCutPreview(_anchors.Count >= 2, Flatten().Select(p => _view.PictureToWorld(p)).ToList());
            _view.SetAnchorMarkers(_anchors.Select(a => _view.PictureToWorld(a.Pos)).ToList(), _anchor);
            if (_anchor >= 0 && _anchor < _anchors.Count)
            {
                var (outEnd, inEnd) = HandleEnds(_anchor);
                var pos = _view.PictureToWorld(_anchors[_anchor].Pos);
                _view.SetHandles(new List<Vector3> { pos, _view.PictureToWorld(outEnd), pos, _view.PictureToWorld(inEnd) });
            }
            else _view.SetHandles(null);
        }

        private int NearestAnchor(Vector2 screen)
        {
            var best = -1;
            var bestD = PickPixels * PickPixels;
            for (var i = 0; i < _anchors.Count; i++)
            {
                var d = (ScreenOf(_anchors[i].Pos) - screen).sqrMagnitude;
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        private Drag HandleHit(Vector2 screen)
        {
            var (outEnd, inEnd) = HandleEnds(_anchor);
            var lim = PickPixels * PickPixels;
            if ((ScreenOf(outEnd) - screen).sqrMagnitude < lim) return Drag.Out;
            if ((ScreenOf(inEnd) - screen).sqrMagnitude < lim) return Drag.In;
            return Drag.None;
        }

        // Bấm lên đường: chèn điểm neo mới tại đó mà không đổi hình (chia Bézier tại t)
        private bool InsertOnCurve(Vector2 screen)
        {
            var bestD = PickPixels * PickPixels;
            int seg = -1;
            var bestT = 0f;
            for (var i = 0; i + 1 < _anchors.Count; i++)
                for (var k = 1; k < 24; k++)
                {
                    var t = k / 24f;
                    var d = (ScreenOf(Bezier(_anchors[i], _anchors[i + 1], t)) - screen).sqrMagnitude;
                    if (d < bestD) { bestD = d; seg = i; bestT = t; }
                }
            if (seg < 0) return false;
            var left = _anchors[seg];
            var right = _anchors[seg + 1];
            Vector2 p0 = left.Pos, p1 = p0 + left.Out, p3 = right.Pos, p2 = p3 + right.In;
            var a = Vector2.Lerp(p0, p1, bestT);
            var b = Vector2.Lerp(p1, p2, bestT);
            var c = Vector2.Lerp(p2, p3, bestT);
            var d1 = Vector2.Lerp(a, b, bestT);
            var e1 = Vector2.Lerp(b, c, bestT);
            var f = Vector2.Lerp(d1, e1, bestT);
            left.Out = a - p0;
            right.In = c - p3;
            _anchors.Insert(seg + 1, new Anchor { Pos = f, Out = e1 - f, In = d1 - f });
            _anchor = seg + 1;
            _runDirty = true;
            return true;
        }

        // Hai đầu đoạn là điểm giao nhiều mảnh: dời được trừ khi nằm trên khung tranh
        private bool EndLocked(int i)
        {
            if (i != 0 && i != _anchors.Count - 1) return false;
            var u = Mathf.Max(1, _propertyData.unit);
            var p = _anchors[i].Pos;
            const float eps = 0.5f;
            return p.x < eps || p.y < eps || p.x > _propertyData.width - eps || p.y > _propertyData.height - eps || u < 1;
        }

        private void DeleteAnchor()
        {
            if (_anchor <= 0 || _anchor >= _anchors.Count - 1) return;
            _anchors.RemoveAt(_anchor);
            _anchor = Mathf.Min(_anchor, _anchors.Count - 2);
            _runDirty = true;
        }

        // Một khung hình khi đang sửa biên; true nếu đã dùng sự kiện chuột, false thì để chọn mảnh xử lý tiếp
        private bool HandleRun(Mouse mouse, Vector2 screen)
        {
            if (_run == null) return false;
            if (!Valid(_run.A) || (_selection.Count != 1 || _selection[0] != _run.A)) { CancelPen(); return false; }
            var over = EditorUiHost.IsPointerOverUi;
            var kb = Keyboard.current;
            var free = !over && !EditorUiHost.KeyboardBusy;

            if (free && mouse.rightButton.wasPressedThisFrame) { FinishRun(); return true; }
            if (free && kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) { FinishRun(); return true; }
            if (free && kb != null && (kb.deleteKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame)) { DeleteAnchor(); DrawRun(); return true; }

            if (free && mouse.leftButton.wasPressedThisFrame)
            {
                _drag = Drag.None;
                if (_anchor >= 0) _drag = HandleHit(screen);
                if (_drag == Drag.None)
                {
                    var near = NearestAnchor(screen);
                    if (near >= 0) { _anchor = near; _drag = Drag.Anchor; }
                    else if (kb != null && kb.shiftKey.isPressed && InsertOnCurve(screen)) _drag = Drag.Anchor;
                }
                if (_drag == Drag.None)
                {
                    if (_runDirty) FinishRun(); else CancelPen();
                    return false;
                }
            }

            if (_drag != Drag.None)
            {
                if (!mouse.leftButton.isPressed) _drag = Drag.None;
                else
                {
                    var a = _anchors[_anchor];
                    var cell = CellOf(screen);
                    if (_drag == Drag.Anchor)
                    {
                        if (!EndLocked(_anchor)) { a.Pos = cell; _runDirty = true; }
                    }
                    else
                    {
                        var off = cell - a.Pos;
                        a.Out = _drag == Drag.Out ? off : -off;
                        a.In = -a.Out;
                        _runDirty = true;
                    }
                }
            }
            DrawRun();
            return true;
        }

        private void FinishRun()
        {
            var run = _run;
            var dirty = _runDirty;
            var path = _anchors.Count >= 2 ? Flatten() : new List<Vector2>();
            StopPen();
            Changed?.Invoke();
            if (!dirty || run == null) return;

            var unit = Mathf.Max(1, _propertyData.unit);
            var chain = new List<P>();
            foreach (var c in path)
            {
                var q = new P(Mathf.RoundToInt(c.x * unit), Mathf.RoundToInt(c.y * unit));
                if (chain.Count == 0 || chain[^1].X != q.X || chain[^1].Y != q.Y) chain.Add(q);
            }
            if (chain.Count < 2) { LevelEditorMainUI.Warn("Biên quá ngắn"); return; }

            var regions = GenRegions();
            var p = _propertyData;
            var baseline = Capture();
            string error = null;
            RunBusy("Đang sửa biên…", () => EdgeSkeleton.ReplaceRun(p.width, p.height, p.unit, regions, run, chain, out error), result =>
            {
                if (Capture() != baseline) { LevelEditorMainUI.Warn("Dữ liệu đã đổi trong lúc sửa biên, bỏ kết quả"); return; }
                if (result == null) { LevelEditorMainUI.Warn(error); return; }
                Commit(() =>
                {
                    for (var i = 0; i < result.Count; i++)
                        if (!result[i].Points.SequenceEqual(_propertyData.regions[i].points)) _propertyData.regions[i].points = result[i].Points;
                    _propertyData.ClampToFrame();
                    PieceValue.Assign(_propertyData);
                });
                LevelEditorMainUI.Log("Đã sửa biên");
            });
        }
    }
}
