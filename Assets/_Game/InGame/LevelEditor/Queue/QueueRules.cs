using System;
using System.Collections.Generic;
using System.Linq;
using Falcon.InGame.Core;
using Falcon.InGame.LevelEditor.Gen;

namespace Falcon.InGame.LevelEditor
{
    /// <summary>Nhóm Pair: các hộp nối nhau; Fault là lý do nhóm không đi được (rỗng nếu hợp lệ).</summary>
    public sealed class PairGroup
    {
        public int Key, Index;
        public List<BoxData> Members;
        public string Fault;
    }

    /// <summary>Luật hàng chờ không phụ thuộc giao diện: nhóm Pair, băng, và tạo hàng chờ từ tranh.</summary>
    public static class QueueRules
    {
        public const string Gap = "gap", Long = "long";


        // Hộp cùng cột trong nhóm phải ở các hàng liền nhau; nhóm không dài hơn số ô chờ
        public static string FaultOf(BoxQueueProperty q, List<BoxData> members)
        {
            foreach (var byColumn in members.GroupBy(b => b.column))
            {
                var rows = byColumn.Select(b => q.Column(b.column).IndexOf(b)).OrderBy(r => r).ToList();
                for (var i = 1; i < rows.Count; i++) if (rows[i] != rows[i - 1] + 1) return Gap;
            }
            return members.Count > q.slots ? Long : "";
        }

        // Nhóm theo thứ tự bảng (cột, rồi hàng của hộp đầu)
        public static List<PairGroup> Groups(BoxQueueProperty q)
        {
            var at = new Dictionary<string, (int col, int row)>();
            for (var c = 0; c < q.ColumnCount; c++)
            {
                var col = q.Column(c);
                for (var r = 0; r < col.Count; r++) at[col[r].id] = (c, r);
            }
            var groups = q.list.Where(b => b.pair > 0).GroupBy(b => b.pair)
                .Where(g => g.Count() >= 2)
                .Select(g => new PairGroup { Key = g.Key, Members = g.OrderBy(b => at[b.id]).ToList() })
                .OrderBy(g => at[g.Members[0].id]).ToList();
            for (var i = 0; i < groups.Count; i++)
            {
                groups[i].Index = i;
                groups[i].Fault = FaultOf(q, groups[i].Members);
            }
            return groups;
        }

        public static string PairFaultText(BoxQueueProperty q)
        {
            var groups = Groups(q);
            var gap = groups.Count(g => g.Fault == Gap);
            var tooLong = groups.Count(g => g.Fault == Long);
            var parts = new List<string>();
            if (gap > 0) parts.Add(gap + " nhóm có hộp cùng cột KHÔNG liền nhau");
            if (tooLong > 0) parts.Add(tooLong + " nhóm dài hơn số ô chờ (" + q.slots + ")");
            return string.Join(" + ", parts);
        }

        // Mặc định: số hộp đứng ở các hàng phía trước hàng của hộp này trên mọi cột, tối thiểu 1
        public static int FrozenDefault(BoxQueueProperty q, BoxData box)
        {
            var row = q.Column(box.column).IndexOf(box);
            var ahead = 0;
            for (var c = 0; c < q.ColumnCount; c++) ahead += Math.Min(row, q.Column(c).Count);
            return Math.Max(1, ahead);
        }

        public static int FrozenConflicts(BoxQueueProperty q) => q.list.Count(b => b.frozen > 0 && b.hidden);

        /// <summary>Mảnh của tranh cho mô phỏng: sao chép dữ liệu để chạy nền không đụng tranh đang sửa.</summary>
        public static (List<GenRegion> regions, List<int> weights, int width, int height, int unit) Snapshot(PictureProperty picture)
        {
            var regions = picture.regions.Select((r, i) => new GenRegion { Id = i + 1, Points = r.points, Holes = r.holes, ColorId = r.colorId }).ToList();
            return (regions, picture.regions.Select(r => Math.Max(1, r.value)).ToList(), picture.width, picture.height, picture.unit);
        }

        /// <summary>Hàng chờ thắng được: chơi thử level theo luật Shooter (xem QueueSim), hộp 5, 10, 15, 20 chia vòng vào các cột. winRate là tỉ lệ thắng trong 150 lượt thử.</summary>
        public static List<BoxData> Make((List<GenRegion> regions, List<int> weights, int width, int height, int unit) snap, int slots, int columns, out double winRate)
        {
            var pieces = QueueSim.PiecesOf(snap.width, snap.height, snap.unit, snap.regions, snap.weights);
            var queue = QueueSim.Make(pieces, "level", slots, columns);
            winRate = queue.WinRate;
            var boxes = new List<BoxData>();
            for (var c = 0; c < queue.Columns.Count; c++)
                foreach (var box in queue.Columns[c]) boxes.Add(new BoxData { colorId = box.Color, value = box.Amount, column = c });
            return boxes;
        }
    }
}
