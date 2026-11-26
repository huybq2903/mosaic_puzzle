using System.Collections.Generic;

namespace Falcon.InGame.LevelEditor
{
    /// <summary>Hoàn tác và làm lại cho tranh và hàng chờ: mỗi bước là ảnh chụp JSON của cả hai trước khi sửa.</summary>
    public sealed class EditHistory
    {
        private const int Limit = 50;

        private readonly struct Snap
        {
            public readonly string Picture, Queue;

            public Snap(string picture, string queue)
            {
                Picture = picture;
                Queue = queue;
            }
        }

        private readonly PictureEditor _picture;
        private readonly BoxQueueEditor _queue;
        private readonly List<Snap> _undo = new(), _redo = new();

        public EditHistory(PictureEditor picture, BoxQueueEditor queue)
        {
            _picture = picture;
            _queue = queue;
        }

        private Snap Capture() => new(_picture.Capture(), _queue.Capture());

        private void Apply(Snap s)
        {
            _picture.Restore(s.Picture);
            _queue.Restore(s.Queue);
        }

        /// <summary>Gọi TRƯỚC khi sửa: lưu trạng thái hiện tại để quay lại.</summary>
        public void Record()
        {
            _undo.Add(Capture());
            if (_undo.Count > Limit) _undo.RemoveAt(0);
            _redo.Clear();
        }

        public bool Undo()
        {
            if (_undo.Count == 0) return false;
            var snap = _undo[^1];
            _undo.RemoveAt(_undo.Count - 1);
            _redo.Add(Capture());
            Apply(snap);
            return true;
        }

        public bool Redo()
        {
            if (_redo.Count == 0) return false;
            var snap = _redo[^1];
            _redo.RemoveAt(_redo.Count - 1);
            _undo.Add(Capture());
            Apply(snap);
            return true;
        }

        public void Clear()
        {
            _undo.Clear();
            _redo.Clear();
        }
    }
}
