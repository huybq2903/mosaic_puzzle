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
        // ------------------------------------------------------------------ panel trái
        private void BuildLeft(VisualElement root)
        {
            _left = Kit.Col(root, KitGap.None, "ps-panel ps-left");
            var tabs = _tabs = Kit.Tabs(_left, OnTab);
            BuildSource(Scrolled(tabs.Add("1 · Tạo tranh")));
            var queue = tabs.Add("2 · Hàng chờ");
            BuildQueueHeader(queue);
            BuildQueue(Scrolled(queue));
            _mechColumns = Kit.Row(Scrolled(tabs.Add("3 · Mechanic")), KitGap.Sm, "ps-cols");
        }

        /// <summary>Đặt editor vào một trạng thái xem (dùng cho công cụ rà layout): tab, mechanic, tooltip chia mảnh.</summary>
        public void AuditShow(int tab, int mechanic, bool tip)
        {
            _tabs.Select(tab, true);
            _mechanic = mechanic;
            ResetModes();
            _tipOpen = tip;
            _dirty = true;
        }

        private void OnTab(int index)
        {
            _tab = index;
            ResetModes();
            _dirty = true;
        }

        // Vùng cuộn của một tab: các khối xếp dọc cách đều nhau
        private static VisualElement Scrolled(VisualElement page)
        {
            var body = Kit.Scroll(page, "ps-scroll").contentContainer;
            Kit.Classes(body, "vk-col vk-gap-sm");
            return body;
        }

        private void BuildSource(VisualElement page)
        {
            var row = Kit.Row(page, KitGap.Sm);
            _import = Kit.Button(row, "Import Image...", _editor.OpenImage, KitTone.Primary, KitSize.Sm, "Chọn ảnh kính màu viền đen");
            _paste = Kit.Button(row, "Paste", _editor.PasteImage, KitTone.Neutral, KitSize.Sm, "Dán ảnh từ clipboard (ảnh được copy, hoặc file ảnh vừa copy)");
            _clear = Kit.Button(row, "Clear", _editor.Clear, KitTone.Neutral, KitSize.Sm, "Xoá ảnh và tranh đang mở");
            var aiRow = Kit.Row(page, KitGap.Sm);
            _aiBtn = Kit.Button(aiRow, "✦ Tạo ảnh AI", _ai.Open, KitTone.Neutral, KitSize.Sm, "Nhờ server AI vẽ ảnh kính màu từ ảnh gốc");
            _aiBtn.AddToClassList("ai-go");
            _aiBtn.style.flexGrow = 1;
            _source = Kit.Text(page, "", KitText.Dim, true);
            _thumb = new Image { scaleMode = ScaleMode.ScaleToFit };
            _thumb.AddToClassList("ps-thumb");
            Kit.Put(page, _thumb);

            _frameHead = Kit.Head(page, "Khung");
            _frameRow = Kit.Row(page, KitGap.Sm, "ps-wrap");
            Kit.Text(_frameRow, "W", KitText.Dim);
            _frameW = Kit.Stepper(_frameRow, _editor.FrameW, 8, 400, v => _editor.SetFrame(v, _editor.FrameH), null, 1, false);
            Kit.Text(_frameRow, "H", KitText.Dim);
            _frameH = Kit.Stepper(_frameRow, _editor.FrameH, 8, 400, v => _editor.SetFrame(_editor.FrameW, v), null, 1, false);
            Kit.Button(_frameRow, "Theo tỉ lệ ảnh", _editor.FitFrame, KitTone.Neutral, KitSize.Sm);
            _generate = Kit.Button(page, "Generate", _editor.Generate, KitTone.Primary, KitSize.Md, "Tách mảnh từ ảnh đang nạp");

            Kit.Head(page, "Xem");
            _showColor = Kit.Check(page, "Hiện màu (tắt = xám)", true, v => _editor.SetShowColor(v));
            _showGlass = Kit.Check(page, "Hiệu ứng kính", true, v => _editor.SetShowGlass(v));
            _showValues = Kit.Check(page, "Hiện số cát", true, v => _editor.SetShowValues(v));
        }

        // Generate và số ô chờ luôn thấy, không cuộn theo danh sách hộp
        private void BuildQueueHeader(VisualElement page)
        {
            var row = Kit.Row(page, KitGap.Sm);
            _genQueue = Kit.Button(row, "Generate", Generate, KitTone.Primary, KitSize.Sm, "Tạo lại hàng chờ bằng cách chơi thử level theo luật Shooter để chắc thắng được");
            Kit.Spacer(row);
            Kit.Text(row, "Số ô chờ", KitText.Dim);
            _slots = Kit.Stepper(row, BoxQueueProperty.MaxSlots, BoxQueueProperty.MinSlots, BoxQueueProperty.MaxSlots, _boxes.SetSlots, null, 1, false);
        }

        private void BuildQueue(VisualElement page)
        {
            Kit.Head(page, "Cân bằng cát (có − cần)");
            _balance = Kit.Row(page, KitGap.Xs, "ps-wrap");
            Kit.Head(page, "Cột hộp");
            _columns = Kit.Row(page, KitGap.Sm, "ps-cols");
            AttachBoxes(_columns);
        }

        private void Generate()
        {
            if (!_boxes.Generate(_editor.Picture, _boxes.Queue.columns)) LevelEditorMainUI.Warn("Chưa có tranh");
        }
    }
}
