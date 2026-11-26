using System;
using Falcon.Shared.EditorUi;

namespace Falcon.Shared.BaseLevelEditor
{
    /// <summary>Cửa sổ Open Recent: lưới level gần đây (RecentGrid) trong một modal.</summary>
    public sealed class RecentModal
    {
        private readonly Action<string, Action> _open; // (đường dẫn, gọi khi đã mở xong)
        private readonly RecentGrid _grid;
        private KitModal _modal;

        public RecentModal(Action<string, Action> open, Func<string> currentPath)
        {
            _open = open;
            _grid = new RecentGrid(path => _open(path, () => _modal.SetOpen(false)), currentPath);
        }

        public void Build()
        {
            _modal = Kit.Modal("Open Recent", KitModalSize.Lg);
            _grid.Build(_modal.Body, _modal.Foot);
        }

        public void Show()
        {
            _modal.SetOpen(true);
            _grid.Rebuild();
        }

        public void Sync()
        {
            if (_modal != null && _modal.IsOpen) _grid.Sync();
        }
    }
}
