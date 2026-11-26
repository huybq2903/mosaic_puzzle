#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace Falcon.InGame.LevelEditor
{
    /// <summary>
    /// Công cụ rà layout bằng dữ liệu (chỉ trong Editor): thấy file Temp/studio_audit.request thì dựng sẵn các trạng thái của editor,
    /// duyệt cây UI ở từng trạng thái và ghi Temp/studio_layout.txt: phần tử tràn màn hình hoặc khung cha, chữ bị cắt, hai khối chồng nhau, kích thước và khoảng cách.
    /// </summary>
    public sealed class LayoutAudit : MonoBehaviour
    {
        private PictureEditor _picture;
        private BoxQueueEditor _boxes;
        private EditorPanels _panels;
        private bool _running;

        private static string Dir => Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, "Temp");

        private static string Request => Path.Combine(Dir, "studio_audit.request");

        private static string Report => Path.Combine(Dir, "studio_layout.txt");

        // Tự cài khi vào Play, không cần ai tham chiếu tới
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            var go = new GameObject("[LayoutAudit]") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            go.AddComponent<LayoutAudit>();
        }

        private void Update()
        {
            if (_running || Time.frameCount % 20 != 0 || !File.Exists(Request)) return;
            _picture = Falcon.Shared.BaseLevelEditor.LevelEditorManager.Get<PictureEditor>();
            _boxes = Falcon.Shared.BaseLevelEditor.LevelEditorManager.Get<BoxQueueEditor>();
            _panels = EditorPanels.Current;
            if (!_picture || !_boxes || _panels == null) return;
            File.Delete(Request);
            StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            _running = true;
            var sb = new StringBuilder();
            IEnumerator Snap(string label)
            {
                for (var i = 0; i < 4; i++) yield return null;
                sb.Append(Dump(label));
            }

            if (_picture.HasPicture) { _picture.Clear(); yield return null; }
            _picture.LoadSample();
            _panels.AuditShow(0, 0, false);
            yield return Snap("tab1-nap-anh");

            _picture.Generate();
            while (_picture.IsBusy) yield return null;
            yield return Snap("tab1-da-tao-tranh");

            _boxes.Generate(_picture.Picture, 3);
            _panels.AuditShow(1, 0, false);
            yield return Snap("tab2-hang-cho");

            _panels.AuditShow(2, 0, false);
            yield return Snap("tab3-an-mau-trong");

            var c0 = _boxes.Queue.Column(0);
            _picture.ToggleHidden(0);
            if (c0.Count > 2) _boxes.ToggleHidden(c0[2].id);
            yield return Snap("tab3-an-mau-co-muc");

            if (c0.Count > 1)
            {
                _boxes.SetPairMode(true);
                _boxes.PairClick(c0[0].id);
                _boxes.PairClick(c0[1].id);
                _boxes.SetPairMode(false);
            }
            _panels.AuditShow(2, 1, false);
            yield return Snap("tab3-pair");

            var c1 = _boxes.Queue.Column(1);
            if (c1.Count > 0) _boxes.Freeze(c1[0].id);
            _panels.AuditShow(2, 2, false);
            yield return Snap("tab3-frozen");

            _panels.AuditShow(0, 0, false);
            _picture.Select(5);
            yield return Snap("inspector-mot-manh");

            _picture.SelectMany(new[] { 5, 6, 7 }, false);
            yield return Snap("inspector-nhieu-manh");

            _panels.AuditShow(0, 0, true);
            yield return Snap("tooltip-chia-manh");

            _panels.AuditShow(0, 0, false);
            _picture.ClearSelection();
            if (c0.Count > 3) _boxes.Select(c0[3].id);
            yield return Snap("inspector-hop");

            _boxes.Deselect();
            yield return Snap("tab1-cuoi");

            Directory.CreateDirectory(Dir);
            File.WriteAllText(Report, sb.ToString());
            LevelEditorMainUILog("Đã ghi báo cáo layout: " + Report);
            _running = false;
        }

        private static void LevelEditorMainUILog(string text) => Falcon.Shared.BaseLevelEditor.LevelEditorMainUI.Log(text);

        // ------------------------------------------------------------------ báo cáo
        private string Dump(string label)
        {
            var root = _panels.Root.panel.visualTree;
            var screen = root.worldBound;
            var issues = new List<string>();
            var tree = new StringBuilder();
            Walk(root, 0, issues, tree, screen, null);
            var sb = new StringBuilder();
            sb.AppendLine($"=== {label} · màn {screen.width:0}x{screen.height:0} · {issues.Count} vấn đề ===");
            foreach (var issue in issues) sb.AppendLine("  ! " + issue);
            sb.Append(tree);
            sb.AppendLine();
            return sb.ToString();
        }

        private static string Describe(VisualElement e)
        {
            var cls = e.GetClasses().FirstOrDefault(c => c.StartsWith("ps-") || c.StartsWith("le-") || c.StartsWith("vk-") && c != "vk-after" && !c.StartsWith("vk-gap") && c != "vk-row" && c != "vk-col");
            var name = string.IsNullOrEmpty(e.name) ? "" : "#" + e.name;
            var text = e is TextElement t && !string.IsNullOrEmpty(t.text) ? $" \"{(t.text.Length > 28 ? t.text.Substring(0, 28) + "…" : t.text)}\"" : "";
            return $"{e.GetType().Name}{name}{(cls != null ? "." + cls : "")}{text}";
        }

        private static bool Shown(VisualElement e) => e.resolvedStyle.display != DisplayStyle.None && e.resolvedStyle.visibility == Visibility.Visible;

        private void Walk(VisualElement e, int depth, List<string> issues, StringBuilder tree, Rect screen, Rect? clip)
        {
            if (!Shown(e)) return;
            var r = e.worldBound;
            if (float.IsNaN(r.x) || float.IsNaN(r.width)) return;
            var who = Describe(e);
            var sized = r.width > 0.5f && r.height > 0.5f;

            if (sized && e.resolvedStyle.position != Position.Absolute || sized && depth > 0)
            {
                // tràn ra ngoài vùng nhìn thấy (màn hình, hoặc chiều ngang vùng cuộn)
                if (clip == null)
                {
                    if (r.xMin < screen.xMin - 1f || r.xMax > screen.xMax + 1f || r.yMin < screen.yMin - 1f || r.yMax > screen.yMax + 1f)
                        issues.Add($"TRÀN MÀN HÌNH {who} [{R(r)}]");
                }
                else if (r.xMin < clip.Value.xMin - 1f || r.xMax > clip.Value.xMax + 1f)
                    issues.Add($"TRÀN NGANG VÙNG CUỘN {who} [{R(r)}] vùng [{R(clip.Value)}]");

                // rộng quá khung cha
                var p = e.parent;
                if (p != null && e.resolvedStyle.position == Position.Relative && p.worldBound.width > 0.5f && !(p is ScrollView) && p.name != "unity-content-container")
                {
                    var pr = p.worldBound;
                    if (r.xMax > pr.xMax + 1.5f || r.xMin < pr.xMin - 1.5f) issues.Add($"VƯỢT KHUNG CHA {who} [{R(r)}] cha {Describe(p)} [{R(pr)}]");
                }
            }

            // chữ bị cắt
            if (e is TextElement te && !string.IsNullOrEmpty(te.text) && sized)
            {
                var content = te.contentRect;
                if (te.resolvedStyle.whiteSpace == WhiteSpace.Normal)
                {
                    var need = te.MeasureTextSize(te.text, content.width, VisualElement.MeasureMode.Exactly, 0, VisualElement.MeasureMode.Undefined);
                    if (need.y > content.height + 1.5f) issues.Add($"CHỮ BỊ CẮT DỌC {who} cần cao {need.y:0} có {content.height:0}");
                }
                else
                {
                    var need = te.MeasureTextSize(te.text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined);
                    if (need.x > content.width + 1.5f) issues.Add($"CHỮ BỊ CẮT {who} cần rộng {need.x:0} có {content.width:0}");
                }
            }

            // hai khối cùng cấp chồng nhau
            var kids = e.Children().Where(c => Shown(c) && c.resolvedStyle.position == Position.Relative && c.worldBound.width > 0.5f && c.worldBound.height > 0.5f).ToList();
            for (var i = 0; i < kids.Count; i++)
                for (var j = i + 1; j < kids.Count; j++)
                {
                    var a = kids[i].worldBound;
                    var b = kids[j].worldBound;
                    var w = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin);
                    var h = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
                    if (w > 2f && h > 2f && w * h > 12f) issues.Add($"CHỒNG NHAU {Describe(kids[i])} [{R(a)}] và {Describe(kids[j])} [{R(b)}]");
                }

            // cây rút gọn: tới độ sâu 9, bỏ phần nội bộ nhỏ của Unity, gộp dãy con dài
            if (depth <= 9 && sized) tree.AppendLine($"{new string(' ', depth * 2)}{who} [{R(r)}]");

            var childClip = e is ScrollView sv ? sv.contentViewport.worldBound : clip;
            var count = 0;
            var total = e.Children().Count(Shown);
            foreach (var c in e.Children())
            {
                if (!Shown(c)) continue;
                count++;
                if (total > 10 && count > 3 && count < total - 1)
                {
                    if (count == 4 && depth <= 9) tree.AppendLine($"{new string(' ', (depth + 1) * 2)}… {total - 4} phần tử cùng cấp, đã bỏ qua trong cây (vẫn được kiểm tra)");
                    Walk(c, 99, issues, tree, screen, childClip); // độ sâu lớn: kiểm tra nhưng không in
                    continue;
                }
                Walk(c, depth + 1, issues, tree, screen, childClip);
            }
        }

        private static string R(Rect r) => $"{r.x:0},{r.y:0} {r.width:0}x{r.height:0}";
    }
}
#endif
