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
        // ------------------------------------------------------------------ tooltip Chia mảnh
        private void BuildTip(VisualElement root)
        {
            _tip = Kit.Col(root, KitGap.None, "ps-tip");
            var body = Kit.Scroll(_tip, "ps-scroll").contentContainer;
            Kit.Classes(body, "vk-col vk-gap-sm");
            Kit.Text(body, "Chia mảnh", KitText.Heading);
            var row = Kit.Row(body, KitGap.Sm);
            Kit.Text(row, "Số mảnh", KitText.Dim);
            _splitCount = Kit.Stepper(row, Subdivide.MinCount, Subdivide.MinCount, Subdivide.MaxCount, v => _editor.SplitCount = v, null, 1);
            Kit.Text(body, "Bấm một kiểu để chia", KitText.Dim);
            var grid = Kit.El(body, "ps-patterns");
            for (var i = 0; i < Subdivide.Labels.Length; i++)
            {
                var pattern = i;
                var b = Kit.Button(grid, "", () =>
                {
                    _editor.SplitPattern = pattern;
                    _editor.SubdivideSelected();
                    _tipOpen = false;
                    _dirty = true;
                }, KitTone.Neutral, KitSize.Sm, Subdivide.Labels[i]);
                b.AddToClassList("ps-pattern");
                var label = Kit.Text(b, Subdivide.Labels[i], KitText.Caption);
                label.pickingMode = PickingMode.Ignore;
                var thumb = PatternThumbs.Get(i);
                if (thumb != null)
                {
                    var image = new Image { image = thumb, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
                    image.AddToClassList("ps-pattern-img");
                    b.Add(image);
                }
                else
                {
                    var auto = Kit.Text(b, "A", KitText.Heading);
                    auto.AddToClassList("ps-pattern-img");
                    auto.pickingMode = PickingMode.Ignore;
                }
                _patterns[i] = b;
            }
            Kit.SetShown(_tip, false);
        }

        private void ToggleTip()
        {
            _tipOpen = !_tipOpen;
            _dirty = true;
        }

        private void SyncTip(bool show)
        {
            if (!show) _tipOpen = false;
            Kit.SetShown(_tip, _tipOpen);
            if (_splitBtn != null) Kit.SetOn(_splitBtn, _tipOpen);
            if (!_tipOpen) return;
            _splitCount.Sync(_editor.SplitCount);
            for (var i = 0; i < _patterns.Length; i++) Kit.SetOn(_patterns[i], i == _editor.SplitPattern);
        }

        // Nút Tách và dòng hướng dẫn
        private void SyncPen()
        {
            Kit.SetOn(_cutBtn, _editor.Pen == PenMode.Cut);
            Kit.SetEnabled(_cutBtn, !_editor.IsBusy);
            Kit.SetShown(_penHint, _editor.PenActive);
            Kit.SetText(_penHint, _editor.RunEditing
                ? "Sửa biên: kéo điểm hoặc đầu thanh cong, Shift+click biên để thêm điểm, Del xoá điểm. Enter chốt, Esc huỷ."
                : "Bấm đặt điểm, kéo xuyên hết mảnh (điểm hút vào biên). Enter hoặc chuột phải để cắt, Esc để huỷ.");
        }
    }
}
