/*
 * Author: Bui Quang Huy
 * Email: huybq@falcongames.com
 * Company: Falcon Games
 * Date: 2026-05-25
 */

using System;
using Falcon.Shared.BaseInGame;
using Falcon.Shared.Common;
using Falcon.Shared.EditorUi;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Falcon.Shared.BaseLevelEditor
{
    /// <summary>Khung chung của Level Editor (UI Toolkit): thanh trên, vùng panel của game, thanh trạng thái, hộp thoại dùng chung.</summary>
    public class LevelEditorMainUI : MonoBehaviour, IEditorManager, IEditorScreen
    {
        protected LevelEditorSaveLoad _saveLoad;

        // Log 1 dòng dưới cùng: log sau đè log trước, quá dài thì tự cắt.
        private static string _log;

        /// <summary>Ghi 1 dòng log vào thanh trạng thái dưới cùng (đè dòng cũ).</summary>
        public static void Log(string msg) => _log = msg;

        /// <summary>Hiện toast (chỉ toast, không ghi log).</summary>
        public static void Toast(string msg) => EditorUiHost.Instance?.Say(msg);

        /// <summary>Cảnh báo cần người dùng chú ý ngay: ghi log và hiện toast.</summary>
        public static void Warn(string msg)
        {
            Log(msg);
            EditorUiHost.Instance?.Say(msg, Severity.Warn);
        }

        /// <summary>Vùng giữa thanh trên và thanh trạng thái, không nhận chuột; game đặt panel vào đây.</summary>
        protected VisualElement Body { get; private set; }

        /// <summary>Class của chỗ trống trên thanh trên / thanh trạng thái, module khác tìm theo class rồi gắn phần tử vào.</summary>
        public const string TopSlotClass = "le-top-slot", StatusSlotClass = "le-status-slot";

        private VisualElement _shell, _menus;
        private Label _file, _status;
        private Button _play;
        private RecentModal _recent;
        private KitConfirm _discard;
        private Action _afterDiscard;
        private KitModal _notice;
        private Label _noticeText;
        private bool _menusBuilt;

        /// <summary>True khi OnPlay đang kiểm level: nút Chơi thử đổi thành "Đang kiểm…" và bấm lại bị bỏ qua.</summary>
        protected virtual bool IsPlayBusy => false;

        protected bool LevelOpen => _saveLoad != null && _saveLoad.LevelName != null;

        protected virtual void Awake()
        {
            LevelEditorManager.Register(this);
            EditorUiHost.Ensure()?.Add(this);
        }

        public virtual void Initialized()
        {
            _saveLoad = LevelEditorManager.Get<LevelEditorSaveLoad>();
            MenuBarRegistry.Initialize();
        }

        // ------------------------------------------------------------------ dựng
        public void Build(VisualElement root)
        {
            _shell = Kit.Col(root, KitGap.None, "le-shell");
            _shell.pickingMode = PickingMode.Ignore;

            // menu bar trên cùng, như bản Imui cũ
            var top = Kit.Row(_shell, KitGap.None, "le-menubar");
            var file = Kit.Row(top, KitGap.None, "le-menus");
            Kit.Button(file, "New", () => ConfirmDiscardChanges(OnNewLevel), KitTone.Subtle, KitSize.Sm);
            Kit.Button(file, "Open", () => ConfirmDiscardChanges(() => OnOpenLevel()), KitTone.Subtle, KitSize.Sm);
            Kit.Button(file, "Open Recent", () => _recent.Show(), KitTone.Subtle, KitSize.Sm);
            Kit.Button(file, "Save", OnSave, KitTone.Subtle, KitSize.Sm);
            Kit.Button(file, "Save As", OnSaveAs, KitTone.Subtle, KitSize.Sm);
            _menus = Kit.Row(top, KitGap.None, "le-menus");
            Kit.Spacer(top);
            Kit.Row(top, KitGap.Sm, TopSlotClass);

            // hộp tên file và nút Play nằm giữa, ngay dưới menu bar
            var nameRow = Kit.Row(_shell, KitGap.None, "le-namerow");
            nameRow.pickingMode = PickingMode.Ignore;
            var nameBox = Kit.Row(nameRow, KitGap.None, "le-namebox");
            _file = Kit.Text(nameBox, "", KitText.Body);
            _file.AddToClassList("le-file-name");
            _play = Kit.Button(nameRow, "Play (P)", () =>
            {
                if (!IsPlayBusy) OnPlay();
            }, KitTone.Neutral, KitSize.Sm);
            _play.AddToClassList("le-play");

            Body = Kit.Row(_shell, KitGap.None, "le-body");
            Body.pickingMode = PickingMode.Ignore;
            BuildPanels(Body);

            var bar = Kit.Row(_shell, KitGap.Sm, "le-logbar");
            _status = Kit.Text(bar, "", KitText.Dim);
            _status.AddToClassList("le-status-text");
            Kit.Row(bar, KitGap.Sm, StatusSlotClass);

            _recent = new RecentModal(OpenRecentPath, () => _saveLoad.CurrentPath);
            _recent.Build();

            _discard = Kit.Confirm("Có thay đổi chưa lưu", "Level đang mở có thay đổi chưa lưu. Bỏ các thay đổi đó và tiếp tục?",
                "Bỏ thay đổi", () =>
                {
                    var next = _afterDiscard;
                    _afterDiscard = null;
                    next?.Invoke();
                });
            _notice = Kit.Modal("Thông báo", KitModalSize.Sm);
            _noticeText = Kit.Text(_notice.Body, "", KitText.Body, true);
            Kit.Primary(_notice.Foot, "Đóng", () => _notice.SetOpen(false));
        }

        // Menu đăng ký qua MenuItemAttribute: mục lá là nút, mục có con mở danh sách xổ xuống.
        private void BuildMenus()
        {
            _menusBuilt = true;
            _menus.Clear();
            foreach (var node in MenuBarRegistry.TopLevel)
            {
                if (node.IsLeaf)
                {
                    var leaf = node;
                    Kit.Button(_menus, node.Label, () => leaf.Contributor.OnClick(), KitTone.Subtle, KitSize.Sm);
                    continue;
                }

                var menu = Kit.Menu();
                AddMenuItems(menu, node, "");
                Button trigger = null;
                trigger = Kit.Button(_menus, "", () => menu.Popover.Toggle(trigger), KitTone.Subtle, KitSize.Sm);
                trigger.AddToClassList("le-menu");
                // chữ và mũi tên là hai con xếp ngang, gắn mũi tên thẳng vào nút có chữ sẽ đè lên chữ
                Kit.Text(trigger, node.Label, KitText.Body).pickingMode = PickingMode.Ignore;
                Kit.Icon(trigger, KitGlyph.ChevronDown).AddToClassList("le-menu-arrow");
            }
        }

        private static void AddMenuItems(KitMenu menu, MenuNode node, string prefix)
        {
            foreach (var child in node.Children)
            {
                if (child.IsLeaf)
                {
                    var leaf = child;
                    menu.Item(prefix + child.Label, () => leaf.Contributor.OnClick());
                }
                else AddMenuItems(menu, child, prefix + child.Label + " › ");
            }
        }

        /// <summary>Game dựng panel của mình vào body (gọi một lần, trước Initialized).</summary>
        protected virtual void BuildPanels(VisualElement body)
        {
        }

        /// <summary>Đồng bộ panel của game mỗi khung hình, chỉ khi đang mở level.</summary>
        protected virtual void SyncPanels()
        {
        }

        // ------------------------------------------------------------------ mỗi khung hình
        public void Sync()
        {
            if (_saveLoad == null) return;
            if (!_menusBuilt) BuildMenus();

            // chưa có level (mới mở tool): vào thẳng một level trống
            if (!LevelOpen) OnNewLevel();
            Kit.SetShown(_shell, LevelOpen);
            if (!LevelOpen) return;

            Kit.SetText(_file, _saveLoad.LevelName + (_saveLoad.HasUnsavedChanges ? " *" : ""));
            Kit.SetBusy(_play, IsPlayBusy, "Checking…");
            Kit.SetText(_status, _log);
            _recent.Sync();
            SyncPanels();
        }

        // ------------------------------------------------------------------ hộp thoại
        /// <summary>Hộp thông báo một nút (lỗi lưu, không chơi thử được…).</summary>
        protected void ShowNotice(string title, string text)
        {
            _notice.SetTitle(title);
            Kit.SetText(_noticeText, text);
            _notice.SetOpen(true);
        }

        protected virtual void ConfirmDiscardChanges(Action onContinue)
        {
            if (!_saveLoad.HasUnsavedChanges)
            {
                onContinue?.Invoke();
                return;
            }
            _afterDiscard = onContinue;
            _discard.Ask();
        }

        // ------------------------------------------------------------------ thao tác file
        protected virtual void OnPlay()
        {
            _saveLoad.SaveTempToPlay();
            DataTempExtensions<string>.Set(GameModeKeys.MODE, GameModeKeys.EDITOR_PLAYTEST);
            SceneManager.LoadScene("GameScene");
        }

        protected virtual void OnNewLevel() => _saveLoad.NewFile();

        // Mở level từ Recent: hỏi bỏ thay đổi chưa lưu trước, mở xong thì gọi opened để cửa sổ tự đóng
        private void OpenRecentPath(string path, Action opened)
        {
            ConfirmDiscardChanges(() =>
            {
                try
                {
                    _saveLoad.OpenFile(path);
                    opened();
                }
                catch (Exception e)
                {
                    Warn($"Không mở được level: {e.Message}");
                }
            });
        }

        protected virtual bool OnOpenLevel()
        {
            _saveLoad.OpenFile();
            return _saveLoad.LevelName != null;
        }

        protected virtual void OnSave()
        {
            _saveLoad.SaveFileImmediately(out var failed);
            if (failed.Count > 0) ShowNotice("Không lưu được", string.Join("\n", failed));
        }

        protected virtual void OnSaveAs()
        {
            _saveLoad.SaveFileAs(out var failed);
            if (failed.Count > 0) ShowNotice("Không lưu được", string.Join("\n", failed));
        }
    }
}
