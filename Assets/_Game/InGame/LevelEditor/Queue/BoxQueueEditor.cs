using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Falcon.InGame.Core;
using Falcon.InGame.LevelEditor.Gen;
using Falcon.Shared.BaseLevelEditor;
using UnityEngine;

namespace Falcon.InGame.LevelEditor
{
    /// <summary>Handler hàng chờ hộp của level editor Color Sort: hộp, số ô chờ, và mechanic Ẩn màu, Pair, Frozen như Studio.</summary>
    public class BoxQueueEditor : APropertyDataHandler<BoxQueueProperty>, IEditorManager
    {
        public const string Gap = QueueRules.Gap, Long = QueueRules.Long;

        public event Action Changed;

        public EditHistory History { get; set; }

        public BoxQueueProperty Queue => _propertyData;

        private readonly List<string> _selection = new();

        public IReadOnlyList<string> Selection => _selection;

        public string Selected => _selection.Count > 0 ? _selection[0] : null;

        public bool IsSelected(string id) => id != null && _selection.Contains(id);

        public List<BoxData> SelectedBoxes => _selection.Select(Find).Where(b => b != null).ToList();

        public int NewColor { get; private set; } = 1;

        public bool PairMode { get; private set; }

        public string PairPend { get; private set; }

        public int PairActive { get; private set; }

        public BoxData SelectedBox => Find(Selected);

        public void Initialized()
        {
            EnsureQueue();
            var saveLoad = LevelEditorManager.Get<LevelEditorSaveLoad>();
            saveLoad?.RegisterSaveCondition(new SaveCondition("Có hộp vừa băng vừa ẩn màu: bỏ một trong hai", () => FrozenConflicts() > 0));
            saveLoad?.RegisterSaveCondition(new SaveCondition("Nhóm nối hộp bị lỗi: xem tab Mechanic > Pair", () => PairFaultText().Length > 0));
            saveLoad?.RegisterSaveCondition(new SaveCondition("Có hộp màu mà tranh không có mảnh nào cùng màu: đổi màu hộp", HasOrphanColor));
        }

        protected override void OnLoadLevel(OnLoadLevel data)
        {
            base.OnLoadLevel(data);
            EnsureQueue();
            History?.Clear();
            _selection.Clear();
            PairMode = false;
            PairPend = null;
            PairActive = 0;
            Changed?.Invoke();
        }

        // Hộp có màu mà không mảnh nào dùng (cân bằng cát sẽ luôn lệch)
        private bool HasOrphanColor()
        {
            var picture = LevelEditorManager.Get<PictureEditor>()?.Picture;
            if (picture == null || picture.regions.Count == 0) return false;
            var used = new HashSet<int>(picture.regions.Select(r => r.colorId));
            return _propertyData.list.Any(b => !used.Contains(b.colorId));
        }

        public string Capture() => Newtonsoft.Json.JsonConvert.SerializeObject(_propertyData);

        public void Restore(string json)
        {
            _propertyData = Newtonsoft.Json.JsonConvert.DeserializeObject<BoxQueueProperty>(json) ?? new BoxQueueProperty();
            _selection.Clear();
            PairMode = false;
            PairPend = null;
            PairActive = 0;
            SavePropertyData();
            Changed?.Invoke();
        }

        private void EnsureQueue() => _propertyData ??= new BoxQueueProperty();

        public BoxData Find(string id) => id == null || _propertyData == null ? null : _propertyData.list.Find(b => b.id == id);

        public string PosOf(BoxData box) => $"c{box.column}·h{_propertyData.Column(box.column).IndexOf(box) + 1}";

        // ------------------------------------------------------------------ cân bằng, chọn
        // Cát các hộp trừ cát các mảnh theo màu: chỉ giữ màu lệch
        public Dictionary<int, int> Balance(PictureProperty picture)
        {
            var map = new Dictionary<int, int>();
            foreach (var b in _propertyData.list) map[b.colorId] = map.GetValueOrDefault(b.colorId) + b.value;
            if (picture != null) foreach (var r in picture.regions) map[r.colorId] = map.GetValueOrDefault(r.colorId) - r.value;
            return map.Where(p => p.Value != 0).ToDictionary(p => p.Key, p => p.Value);
        }

        public void SetNewColor(int colorId)
        {
            NewColor = colorId;
            Changed?.Invoke();
        }

        // Bấm hộp: bấm lại đúng hộp duy nhất đang chọn thì bỏ chọn; additive thì thêm hoặc bớt
        public void Select(string id, bool additive = false)
        {
            if (additive) { if (!_selection.Remove(id)) _selection.Add(id); }
            else if (_selection.Count == 1 && _selection[0] == id) _selection.Clear();
            else
            {
                _selection.Clear();
                _selection.Add(id);
            }
            Changed?.Invoke();
        }

        public void SelectOnly(string id)
        {
            _selection.Clear();
            if (id != null) _selection.Add(id);
            Changed?.Invoke();
        }

        public void SelectMany(IEnumerable<string> ids, bool additive)
        {
            if (!additive) _selection.Clear();
            foreach (var id in ids) if (!_selection.Contains(id)) _selection.Add(id);
            Changed?.Invoke();
        }

        public void Deselect()
        {
            if (_selection.Count == 0) return;
            _selection.Clear();
            Changed?.Invoke();
        }

        // ------------------------------------------------------------------ sửa
        private void Commit()
        {
            var q = _propertyData;
            q.EnsureIds();
            DropLoneGroups();
            q.columns = Mathf.Clamp(Mathf.Max(q.columns, q.ColumnCount), 1, BoxQueueProperty.MaxColumns);
            _selection.RemoveAll(i => Find(i) == null);
            SavePropertyData();
            Changed?.Invoke();
        }

        public void SetSlots(int slots)
        {
            History?.Record();
            _propertyData.slots = Mathf.Clamp(slots, BoxQueueProperty.MinSlots, BoxQueueProperty.MaxSlots);
            Commit();
        }

        public void Add(int column, int colorId, int value)
        {
            if (column < 0 || column >= BoxQueueProperty.MaxColumns) return;
            History?.Record();
            _propertyData.list.Add(new BoxData { colorId = colorId, value = PieceValue.Clamp(value), column = column });
            Commit();
        }

        public void SetColor(string id, int colorId)
        {
            var box = Find(id);
            if (box == null) return;
            History?.Record();
            box.colorId = colorId;
            Commit();
        }

        public void SetValue(string id, int value)
        {
            var box = Find(id);
            if (box == null) return;
            History?.Record();
            box.value = PieceValue.Clamp(value);
            Commit();
        }

        private void EditSelected(Action<BoxData> change)
        {
            var boxes = SelectedBoxes;
            if (boxes.Count == 0) return;
            History?.Record();
            foreach (var b in boxes) change(b);
            Commit();
        }

        public void SetSelectedColor(int colorId) => EditSelected(b => b.colorId = colorId);

        public void SetSelectedValue(int value) => EditSelected(b => b.value = PieceValue.Clamp(value));

        public void StepSelectedValue(int steps) => EditSelected(b => b.value = PieceValue.Clamp(b.value + steps * PieceValue.Step));

        // Mọi hộp đang chọn đã ẩn thì hiện, không thì ẩn hết
        public void ToggleSelectedHidden()
        {
            var boxes = SelectedBoxes;
            var all = boxes.Count > 0 && boxes.All(b => b.hidden);
            EditSelected(b => b.hidden = !all);
        }

        public void FreezeSelected() => EditSelected(b => { if (b.frozen == 0) b.frozen = FrozenDefault(b); });

        public void UnfreezeSelected() => EditSelected(b => b.frozen = 0);

        public void SetSelectedFrozen(int count) => EditSelected(b => { if (b.frozen > 0) b.frozen = Mathf.Max(1, count); });

        public void DeleteSelected()
        {
            var boxes = SelectedBoxes;
            if (boxes.Count == 0) return;
            History?.Record();
            foreach (var b in boxes) _propertyData.list.Remove(b);
            _selection.Clear();
            Commit();
        }

        /// <summary>Đặt hộp vào cột column ở hàng row (cột bằng số cột hiện có thì mở cột mới).</summary>
        public void Move(string id, int column, int row)
        {
            var box = Find(id);
            if (box == null || column < 0 || column >= BoxQueueProperty.MaxColumns) return;
            History?.Record();
            var list = _propertyData.list;
            list.Remove(box);
            var col = list.Where(b => b.column == column).ToList();
            var at = row < col.Count ? list.IndexOf(col[row]) : col.Count > 0 ? list.IndexOf(col[^1]) + 1 : list.Count;
            box.column = column;
            list.Insert(at, box);
            Commit();
        }

        // ------------------------------------------------------------------ Ẩn màu
        public void ToggleHidden(string id)
        {
            var box = Find(id);
            if (box == null) return;
            History?.Record();
            box.hidden = !box.hidden;
            Commit();
        }

        // ------------------------------------------------------------------ Pair
        // Nhóm có dưới 2 hộp thì tan
        private void DropLoneGroups()
        {
            foreach (var g in _propertyData.list.Where(b => b.pair > 0).GroupBy(b => b.pair).Where(g => g.Count() < 2).ToList())
                foreach (var b in g) b.pair = 0;
        }

        public string FaultOf(List<BoxData> members) => QueueRules.FaultOf(_propertyData, members);

        public List<PairGroup> Groups() => QueueRules.Groups(_propertyData);

        public string PairFaultText() => QueueRules.PairFaultText(_propertyData);

        public void SetPairMode(bool on)
        {
            PairMode = on;
            PairPend = null;
            PairActive = 0;
            Changed?.Invoke();
        }

        // Esc: kết thúc nhóm đang nối; true nếu có gì để kết thúc
        public bool PairFinish()
        {
            if (PairPend == null && PairActive == 0) return false;
            PairPend = null;
            PairActive = 0;
            Changed?.Invoke();
            return true;
        }

        public void PairClick(string id)
        {
            var box = Find(id);
            if (box == null) return;
            var groups = Groups();
            var group = groups.FirstOrDefault(g => g.Members.Any(m => m.id == id));
            var active = PairActive != 0 ? groups.FirstOrDefault(g => g.Key == PairActive) : null;
            if (PairActive != 0 && active == null) PairActive = 0;

            if (active != null)
            {
                if (group == active) { PairActive = 0; Changed?.Invoke(); return; }
                if (group != null) { LevelEditorMainUI.Toast("Hộp này đã thuộc nhóm khác: chuột phải để gỡ trước"); return; }
                PairTry(active.Members.Append(box).ToList(), active.Key);
                return;
            }
            if (PairPend != null)
            {
                if (PairPend == id) { PairPend = null; Changed?.Invoke(); return; }
                if (group != null) { LevelEditorMainUI.Toast("Hộp này đã thuộc nhóm khác: chuột phải để gỡ trước"); return; }
                var first = Find(PairPend);
                PairPend = null;
                PairTry(new List<BoxData> { first, box }, _propertyData.list.Max(b => b.pair) + 1);
                return;
            }
            if (group != null) PairActive = group.Key;
            else PairPend = id;
            Changed?.Invoke();
        }

        private void PairTry(List<BoxData> members, int key)
        {
            var fault = FaultOf(members);
            if (fault.Length > 0)
            {
                LevelEditorMainUI.Toast(fault == Gap ? "Không nối được: hộp cùng cột trong nhóm phải LIỀN NHAU" : "Không nối được: nhóm dài hơn số ô chờ (" + _propertyData.slots + ")");
                Changed?.Invoke();
                return;
            }
            History?.Record();
            foreach (var m in members) m.pair = key;
            PairActive = key;
            Commit();
        }

        public void PairRemove(string id)
        {
            var box = Find(id);
            if (box == null || box.pair == 0) return;
            History?.Record();
            box.pair = 0;
            Commit();
        }

        public void PairDissolve(PairGroup group)
        {
            History?.Record();
            foreach (var m in group.Members) m.pair = 0;
            Commit();
        }

        // ------------------------------------------------------------------ Frozen
        public int FrozenDefault(BoxData box) => QueueRules.FrozenDefault(_propertyData, box);

        public void Freeze(string id)
        {
            var box = Find(id);
            if (box == null || box.frozen > 0) return;
            History?.Record();
            box.frozen = FrozenDefault(box);
            Commit();
        }

        public void Unfreeze(string id)
        {
            var box = Find(id);
            if (box == null || box.frozen == 0) return;
            History?.Record();
            box.frozen = 0;
            Commit();
        }

        public void SetFrozen(string id, int count)
        {
            var box = Find(id);
            if (box == null || box.frozen == 0) return;
            History?.Record();
            box.frozen = Mathf.Max(1, count);
            Commit();
        }

        public int FrozenConflicts() => _propertyData == null ? 0 : QueueRules.FrozenConflicts(_propertyData);

        // ------------------------------------------------------------------ tạo lại
        private Task<(List<BoxData> boxes, double win)> _job;
        private int _jobColumns;

        public bool IsBusy => _job != null;

        /// <summary>Bắt đầu tạo lại hàng chờ từ tranh bằng cách chơi thử (chạy nền); xong thì Poll áp kết quả.</summary>
        public bool Generate(PictureProperty picture, int columns)
        {
            if (picture == null || picture.regions.Count == 0) return false;
            if (IsBusy) return true;
            _jobColumns = Mathf.Clamp(columns, 1, BoxQueueProperty.MaxColumns);
            var snap = QueueRules.Snapshot(picture);
            var slots = _propertyData.slots;
            var cols = _jobColumns;
            _job = Task.Run(() => (QueueRules.Make(snap, slots, cols, out var win), win));
            Changed?.Invoke();
            return true;
        }

        /// <summary>Mỗi khung hình: áp kết quả khi mô phỏng xong.</summary>
        public void Poll()
        {
            if (_job == null || !_job.IsCompleted) return;
            var job = _job;
            _job = null;
            if (job.IsFaulted)
            {
                LevelEditorMainUI.Warn("Không tạo được hàng chờ: " + job.Exception?.GetBaseException().Message);
                Changed?.Invoke();
                return;
            }
            var (boxes, win) = job.Result;
            History?.Record();
            _propertyData.list = boxes;
            _propertyData.columns = _jobColumns;
            _selection.Clear();
            PairMode = false;
            PairPend = null;
            PairActive = 0;
            Commit();
            LevelEditorMainUI.Log($"Đã tạo {boxes.Count} hộp trong {_jobColumns} cột · thắng {Mathf.RoundToInt((float)win * 100)}% trong 150 lượt chơi thử");
            if (win < 1) LevelEditorMainUI.Warn($"Hàng chờ này vẫn có thể kẹt ({Mathf.RoundToInt((float)win * 100)}% lượt chơi thử thắng): bấm Generate lại hoặc đổi thứ tự hộp");
        }
    }
}
