using System;
using Falcon.Shared.BaseLevelEditor;
using Falcon.Shared.EditorUi;
using UnityEngine;
using UnityEngine.UIElements;

namespace Falcon.InGame.LevelEditor
{
    /// <summary>Popup đầu phiên như Studio: bên trái NEW LEVEL và OPEN LEVEL, bên phải lưới Recent; hiện một lần khi editor mở.</summary>
    public sealed class StartPopup : IEditorScreen
    {
        private KitModal _modal;
        private RecentGrid _grid;
        private bool _shown;

        public static StartPopup Current { get; private set; }

        public void Build(VisualElement root)
        {
            Current = this;
            _modal = Kit.Modal("Level Editor", KitModalSize.Lg);
            _modal.Box.style.width = 880;
            _modal.Foot.style.display = DisplayStyle.None;
            var split = Kit.Row(_modal.Body, KitGap.Lg, "ps-start");

            var lefts = Kit.Col(split, KitGap.Md, "ps-start-left");
            Kit.Text(lefts, "New or Open Level", KitText.Dim).AddToClassList("ps-start-hint");
            Kit.Button(lefts, "NEW LEVEL", () => _modal.SetOpen(false), KitTone.Primary, KitSize.Lg).AddToClassList("ps-start-btn");
            Kit.Button(lefts, "OPEN LEVEL", OpenLevel, KitTone.Neutral, KitSize.Lg).AddToClassList("ps-start-btn");

            var recent = Kit.Col(split, KitGap.Sm, "ps-start-recent");
            var head = Kit.Row(recent, KitGap.Sm, "ps-start-head");
            Kit.Text(head, "Recent", KitText.Heading);
            Kit.El(head, "ps-grow");
            _grid = new RecentGrid(OpenPath, () => LevelEditorManager.Get<LevelEditorSaveLoad>()?.CurrentPath);
            _grid.Build(recent, head);
        }

        public void Sync()
        {
            if (_modal == null) return;
            if (_shown)
            {
                if (_modal.IsOpen) _grid.Sync();
                return;
            }
            var saveLoad = LevelEditorManager.Get<LevelEditorSaveLoad>();
            if (saveLoad == null || saveLoad.LevelName == null) return;
            _shown = true;
            _grid.Rebuild();
            _modal.SetOpen(true);
        }

        // Công cụ chụp giao diện: mở hoặc đóng popup để chụp
        public void Preview(bool on)
        {
            if (_modal == null) return;
            if (on) _grid.Rebuild();
            _modal.SetOpen(on);
        }

        public Rect? ScreenRect()
        {
            if (_modal?.Box == null || _modal.Box.panel == null) return null;
            var layout = _modal.Box.panel.visualTree.layout;
            if (layout.width <= 0f) return null;
            var k = UnityEngine.Screen.width / layout.width;
            var b = _modal.Box.worldBound;
            return Rect.MinMaxRect(b.xMin * k, UnityEngine.Screen.height - b.yMax * k, b.xMax * k, UnityEngine.Screen.height - b.yMin * k);
        }

        private void OpenLevel()
        {
            var saveLoad = LevelEditorManager.Get<LevelEditorSaveLoad>();
            saveLoad.OpenFile();
            if (saveLoad.LevelName != null && saveLoad.CurrentPath != null) _modal.SetOpen(false);
        }

        private void OpenPath(string path)
        {
            try
            {
                LevelEditorManager.Get<LevelEditorSaveLoad>().OpenFile(path);
                _modal.SetOpen(false);
            }
            catch (Exception ex)
            {
                LevelEditorMainUI.Warn($"Không mở được level: {ex.Message}");
            }
        }
    }
}
