using Falcon.Shared.BaseLevelEditor;
using Falcon.Shared.EditorUi;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Falcon.InGame.LevelEditor
{
    /// <summary>Gắn phần Color Sort vào mỗi scene có LevelEditorManager (EditorScene): handler tranh và panel, không phải sửa scene.</summary>
    internal static class EditorBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Subscribe()
        {
            RecentGrid.Preview = PictureThumb.FromFile;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded; // chạy sau Awake và trước Start: kịp đăng ký vào LevelEditorManager
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            var manager = Object.FindFirstObjectByType<LevelEditorManager>();
            if (!manager || manager.GetComponent<PictureEditor>()) return;
            var picture = manager.gameObject.AddComponent<PictureEditor>();
            LevelEditorManager.Register(picture);
            var boxes = manager.gameObject.AddComponent<BoxQueueEditor>();
            LevelEditorManager.Register(boxes);
            var history = new EditHistory(picture, boxes);
            picture.History = history;
            boxes.History = history;
            var panels = new EditorPanels(picture, boxes, history);
            EditorUiHost.Ensure()?.Add(panels, 5);
            EditorUiHost.Ensure()?.Add(new StartPopup(), 6);
        }
    }
}
