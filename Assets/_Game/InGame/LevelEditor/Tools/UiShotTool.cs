#if UNITY_EDITOR
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Falcon.InGame.LevelEditor
{
    /// <summary>
    /// Chụp popup để so với mockup mà không cần ai đụng vào cửa sổ (chỉ trong Editor, khi đang Play): thấy file Temp/ui_shot.request thì dựng từng popup
    /// bằng dữ liệu mẫu và ghi ảnh cắt đúng hộp vào Temp/ui_shots/&lt;tên&gt;.png. Nội dung request: các tên cách nhau dấu phẩy (config, run, results, busy, start), trống = tất cả.
    /// </summary>
    public sealed class UiShot : MonoBehaviour
    {
        private static readonly string[] All = { "config", "run", "results", "busy", "start" };
        private bool _running;

        private static string Dir => Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, "Temp");

        private static string Request => Path.Combine(Dir, "ui_shot.request");

        private static string Out => Path.Combine(Dir, "ui_shots");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            var go = new GameObject("[UiShot]") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            go.AddComponent<UiShot>();
        }

        private void Update()
        {
            if (_running || Time.frameCount % 20 != 0 || !File.Exists(Request) || EditorPanels.Current == null) return;
            var names = File.ReadAllText(Request).Split(new[] { ',', ' ', '\n', '\r' }, System.StringSplitOptions.RemoveEmptyEntries);
            File.Delete(Request);
            StartCoroutine(Run(names.Length > 0 ? names : All));
        }

        private IEnumerator Run(string[] names)
        {
            _running = true;
            Directory.CreateDirectory(Out);
            foreach (var old in Directory.GetFiles(Out)) File.Delete(old);
            var log = "";
            var picture = Falcon.Shared.BaseLevelEditor.LevelEditorManager.Get<PictureEditor>();
            if (picture != null && !picture.HasSource && !picture.HasPicture) picture.LoadSample();
            foreach (var name in names)
            {
                EditorPanels.Current.AuditPopup(name);
                for (var i = 0; i < 14; i++) yield return null;
                var rect = EditorPanels.Current.AuditRect(name);
                yield return new WaitForEndOfFrame();
                var full = ScreenCapture.CaptureScreenshotAsTexture();
                var box = rect ?? new Rect(0, 0, full.width, full.height);
                var x = Mathf.Clamp(Mathf.FloorToInt(box.x), 0, full.width - 1);
                var y = Mathf.Clamp(Mathf.FloorToInt(box.y), 0, full.height - 1);
                var w = Mathf.Clamp(Mathf.CeilToInt(box.width), 1, full.width - x);
                var h = Mathf.Clamp(Mathf.CeilToInt(box.height), 1, full.height - y);
                var crop = new Texture2D(w, h, TextureFormat.RGB24, false);
                crop.SetPixels(full.GetPixels(x, y, w, h));
                crop.Apply();
                File.WriteAllBytes(Path.Combine(Out, name + ".png"), crop.EncodeToPNG());
                log += $"{name}: {w}x{h} (màn hình {full.width}x{full.height})\n";
                Destroy(full);
                Destroy(crop);
            }
            EditorPanels.Current.AuditPopup("none");
            File.WriteAllText(Path.Combine(Out, "done.txt"), log);
            _running = false;
        }
    }
}
#endif
