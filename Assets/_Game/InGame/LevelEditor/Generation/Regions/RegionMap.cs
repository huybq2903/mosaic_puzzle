using System.Collections.Generic;

namespace Falcon.InGame.LevelEditor.Gen
{
    /// <summary>Bản đồ vùng: Reg[i] = id mảnh (0 = nét đen/ngoài), Colors[id] = id màu bảng (Colors[0] = -1).</summary>
    public sealed class RegionMap
    {
        public int W, H;
        public int[] Reg;
        public List<int> Colors = new() { -1 };
        public bool Inked, Gaps, Synthetic;
        public double InkW; // bề rộng nét (pixel bản đồ) sau FillGaps; 0 = chưa đo
        public string Route;

        public int Count => Colors.Count - 1;
    }

    /// <summary>Danh sách khoá giữ thứ tự thêm như Map của JS; Add cộng dồn giá trị.</summary>
    public sealed class OrderedCounter
    {
        private readonly Dictionary<int, int> _map = new();
        private readonly List<int> _keys = new();

        public int Size => _keys.Count;

        public void Add(int key, int n)
        {
            if (_map.TryGetValue(key, out var v)) _map[key] = v + n;
            else { _map[key] = n; _keys.Add(key); }
        }

        public void ForEach(System.Action<int, int> f)
        {
            foreach (var k in _keys) f(_map[k], k); // (giá trị, khoá) như Map.forEach của JS
        }
    }
}
