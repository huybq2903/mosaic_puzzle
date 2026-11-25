// Author: Bui Quang Huy
// Email: huybq@falcongames.com
// Company: Falcon Games
// Date: 2026-09-09

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Falcon.InGame.UI
{
    /// <summary>HUD thay thế khi playtest từ Level Editor, xem ModeEditorPlaytest.</summary>
    public class EditorHUD : MonoBehaviour
    {
        [SerializeField, Tooltip("Nút quay về EditorScene.")]
        private Button btnBackToEditor;

        private const string EDITOR_SCENE = "EditorScene";

        private void Awake()
        {
            EnsureEventSystem();
            btnBackToEditor.onClick.AddListener(() => SceneManager.LoadScene(EDITOR_SCENE));
        }

        // Vào playtest từ EditorScene thì chưa có EventSystem nào, nút UGUI không nhận click.
        private static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null) return;

            var go = new GameObject("EventSystem", typeof(EventSystem));
            // tạo lúc chạy thì module không tự có bộ phím mặc định
            go.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }
    }
}
