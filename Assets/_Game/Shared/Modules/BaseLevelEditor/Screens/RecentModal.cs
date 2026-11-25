using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Falcon.Shared.EditorUi;
using UnityEngine;
using UnityEngine.UIElements;

namespace Falcon.Shared.BaseLevelEditor
{
    /// <summary>Cửa sổ Open Recent: lưới level gần đây, mở, đổi tên, xoá khỏi danh sách, xoá file.</summary>
    public sealed class RecentModal
    {
        private readonly Action<string, Action> _open; // (đường dẫn, gọi khi đã mở xong)
        private readonly Func<string> _currentPath;
        private readonly Dictionary<string, Texture2D> _thumbs = new();

        private KitModal _modal, _renameModal;
        private VisualElement _grid;
        private KitEmpty _empty;
        private Button _clear;
        private KitMenu _menu;
        private KitConfirm _delete;
        private TextField _renameField;
        private Label _renameError;
        private RecentLevels.Entry _target;
        private int _version = -1;

        public RecentModal(Action<string, Action> open, Func<string> currentPath)
        {
            _open = open;
            _currentPath = currentPath;
        }

        public void Build()
        {
            _modal = Kit.Modal("Open Recent", KitModalSize.Lg);
            var scroll = Kit.Scroll(_modal.Body);
            scroll.AddToClassList("le-recent-scroll");
            _grid = Kit.El(scroll, "le-recent-grid");
            _empty = Kit.Empty(_modal.Body, "Chưa có level gần đây");
            _clear = Kit.Button(_modal.Foot, "Xoá danh sách", RecentLevels.Clear, KitTone.Subtle, KitSize.Sm);

            _menu = Kit.Menu();
            _menu.Item("Mở", () => Open(_target));
            _menu.Item("Đổi tên", StartRename);
            _menu.Item("Hiện trong thư mục", () => Reveal(_target));
            _menu.Item("Sao chép đường dẫn", () => Copy(_target));
            _menu.Item("Xoá khỏi danh sách", () => RecentLevels.Remove(_target));
            _menu.Item("Xoá file…", AskDelete);

            _delete = Kit.Confirm("Xoá file", "", "Xoá file", () =>
            {
                if (!RecentLevels.DeleteFile(_target, out var error)) LevelEditorMainUI.Warn("Không xoá được file: " + error);
            });

            _renameModal = Kit.Modal("Đổi tên", KitModalSize.Sm);
            _renameField = Kit.Input(_renameModal.Body, "", _ => Kit.SetText(_renameError, ""), "Tên level");
            _renameField.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode is KeyCode.Return or KeyCode.KeypadEnter) CommitRename();
            });
            _renameError = Kit.Text(_renameModal.Body, "", KitText.Dim, true);
            Kit.Primary(_renameModal.Foot, "Lưu", CommitRename);
        }

        public void Show()
        {
            _modal.SetOpen(true);
            Rebuild();
        }

        public void Sync()
        {
            if (_modal != null && _modal.IsOpen && _version != RecentLevels.Version) Rebuild();
        }

        private void Rebuild()
        {
            _version = RecentLevels.Version;
            _grid.Clear();
            foreach (var t in _thumbs.Values)
                if (t) UnityEngine.Object.Destroy(t);
            _thumbs.Clear();

            var items = RecentLevels.Items;
            Kit.SetShown(_grid, items.Count > 0);
            Kit.SetShown(_clear, items.Count > 0);
            _empty.Set(items.Count == 0 ? "Chưa có level gần đây" : null);
            foreach (var e in items) AddCard(e);
        }

        private void AddCard(RecentLevels.Entry e)
        {
            var exists = RecentLevels.Exists(e);
            var card = Kit.El(_grid, "le-card");
            if (!exists) card.AddToClassList("le-missing");
            if (IsCurrent(e)) card.AddToClassList("le-current");

            var thumb = Kit.El(card, "le-thumb");
            thumb.pickingMode = PickingMode.Ignore;
            var tex = exists ? ThumbFor(e) : null;
            if (tex != null)
            {
                var image = new Image { image = tex, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
                image.AddToClassList("le-thumb-img");
                thumb.Add(image);
            }
            else Kit.Text(thumb, exists ? "Not preview" : "Not found", KitText.Dim).pickingMode = PickingMode.Ignore;

            Kit.Text(card, Path.GetFileNameWithoutExtension(e.path), KitText.Body).pickingMode = PickingMode.Ignore;
            var sub = exists ? RecentLevels.TimeAgo(e.opened) + (IsCurrent(e) ? " · đang mở" : "") : "File đã bị dời hoặc xoá";
            Kit.Text(card, sub, KitText.Dim).pickingMode = PickingMode.Ignore;

            Button more = null;
            more = Kit.IconButton(card, KitGlyph.More, "Tuỳ chọn", () =>
            {
                _target = e;
                _menu.Popover.Toggle(more);
            });
            more.AddToClassList("le-card-more");

            card.RegisterCallback<ClickEvent>(ev =>
            {
                var t = ev.target as VisualElement;
                if (t is Button || t?.GetFirstAncestorOfType<Button>() != null) return;
                Open(e);
            });
        }

        private void Open(RecentLevels.Entry e)
        {
            if (e != null && RecentLevels.Exists(e)) _open(e.path, () => _modal.SetOpen(false));
        }

        private void StartRename()
        {
            _renameField.SetValueWithoutNotify(Path.GetFileNameWithoutExtension(_target.path));
            Kit.SetText(_renameError, "");
            _renameModal.SetOpen(true);
            _renameField.Focus();
        }

        private void CommitRename()
        {
            if (_target == null) return;
            if (RecentLevels.Rename(_target, _renameField.value, out var error)) _renameModal.SetOpen(false);
            else Kit.SetText(_renameError, error);
        }

        private void AskDelete()
        {
            if (_target == null) return;
            if (IsCurrent(_target))
            {
                LevelEditorMainUI.Warn("Không xoá được level đang mở");
                return;
            }
            _delete.Ask($"Xoá file {Path.GetFileName(_target.path)} khỏi ổ đĩa?\nFile sẽ được chuyển vào Thùng rác của Windows.");
        }

        private static void Reveal(RecentLevels.Entry e)
        {
            if (e == null || !RecentLevels.Exists(e)) return;
            if (Application.platform is RuntimePlatform.WindowsEditor or RuntimePlatform.WindowsPlayer)
                Process.Start("explorer.exe", $"/select,\"{e.path}\"");
            else
                Application.OpenURL("file://" + Path.GetDirectoryName(e.path));
        }

        private static void Copy(RecentLevels.Entry e)
        {
            if (e == null) return;
            GUIUtility.systemCopyBuffer = e.path;
            LevelEditorMainUI.Log("Đã sao chép đường dẫn: " + e.path);
        }

        private bool IsCurrent(RecentLevels.Entry e)
        {
            var cur = _currentPath?.Invoke();
            return !string.IsNullOrEmpty(cur) && string.Equals(Path.GetFullPath(cur), e.path, StringComparison.OrdinalIgnoreCase);
        }

        private Texture2D ThumbFor(RecentLevels.Entry e)
        {
            var p = RecentLevels.ThumbPath(e);
            if (p == null) return null;
            if (_thumbs.TryGetValue(p, out var cached)) return cached;

            Texture2D tex = null;
            if (File.Exists(p))
            {
                tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!tex.LoadImage(File.ReadAllBytes(p)))
                {
                    UnityEngine.Object.Destroy(tex);
                    tex = null;
                }
            }

            _thumbs[p] = tex;
            return tex;
        }
    }
}
