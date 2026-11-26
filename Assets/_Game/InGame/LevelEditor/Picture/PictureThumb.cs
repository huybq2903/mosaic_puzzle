using System.IO;
using System.Linq;
using Falcon.InGame.Core;
using Falcon.InGame.LevelEditor.Gen;
using Falcon.Shared.BaseInGame;
using UnityEngine;

namespace Falcon.InGame.LevelEditor
{
    /// <summary>Ảnh preview của level vẽ lại từ dữ liệu tranh: mỗi mảnh một màu bảng màu, khe là nét tối.</summary>
    public static class PictureThumb
    {
        public static Texture2D FromFile(string path)
        {
            if (!File.Exists(path)) return null;
            var data = LevelData.FromJson(File.ReadAllText(path).Decompress());
            return Render(data?.GetProperty<PictureProperty>());
        }

        public static Texture2D Render(PictureProperty p)
        {
            if (p == null || p.regions == null || p.regions.Count == 0 || p.width <= 0 || p.height <= 0) return null;
            var regions = p.regions.Select((r, i) => new GenRegion { Id = i + 1, Points = r.points, Holes = r.holes, ColorId = r.colorId }).ToList();
            var map = Rasterizer.Rasterize(p.width, p.height, Mathf.Max(1, p.unit), regions);
            var ink = new Color32(26, 26, 31, 255);
            int w = map.W, h = map.H;
            var line = new bool[w * h];
            var reach = Mathf.Max(1, w / 150); // nét dày theo cỡ ảnh để thu nhỏ vào thẻ vẫn thấy
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    var id = map.Reg[y * w + x];
                    var edge = (x + 1 < w && map.Reg[y * w + x + 1] != id) || (y + 1 < h && map.Reg[(y + 1) * w + x] != id);
                    if (!edge) continue;
                    for (var dy = -reach; dy <= reach; dy++)
                        for (var dx = -reach; dx <= reach; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx >= 0 && ny >= 0 && nx < w && ny < h) line[ny * w + nx] = true;
                        }
                }
            if (p.decor != null && p.decor.Count > 0) FillDecor(p, w, h, line);
            var colors = new Color32[w * h];
            for (var i = 0; i < colors.Length; i++)
            {
                var id = map.Reg[i];
                colors[i] = line[i] || id <= 0 || id > p.regions.Count ? ink : ColorPalette.Get(p.regions[id - 1].colorId);
            }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels32(colors);
            tex.Apply();
            return tex;
        }

        // Hình trang trí kín tô theo nonzero: pixel nằm trong khi tổng số vòng quấn khác 0
        private static void FillDecor(PictureProperty p, int w, int h, bool[] mask)
        {
            var unit = Mathf.Max(1, p.unit);
            foreach (var ring in p.decor)
            {
                if (ring == null || ring.Length < 6) continue;
                float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                for (var k = 0; k < ring.Length; k += 2)
                {
                    minX = Mathf.Min(minX, ring[k]); maxX = Mathf.Max(maxX, ring[k]);
                    minY = Mathf.Min(minY, ring[k + 1]); maxY = Mathf.Max(maxY, ring[k + 1]);
                }
                int x0 = Mathf.Max(0, Mathf.FloorToInt(minX / unit)), x1 = Mathf.Min(w - 1, Mathf.CeilToInt(maxX / unit));
                int y0 = Mathf.Max(0, Mathf.FloorToInt(minY / unit)), y1 = Mathf.Min(h - 1, Mathf.CeilToInt(maxY / unit));
                for (var y = y0; y <= y1; y++)
                    for (var x = x0; x <= x1; x++)
                        if (Winding(ring, (x + 0.5f) * unit, (y + 0.5f) * unit) != 0) mask[y * w + x] = true;
            }
        }

        private static int Winding(int[] ring, float px, float py)
        {
            var wn = 0;
            var n = ring.Length / 2;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                float xi = ring[i * 2], yi = ring[i * 2 + 1], xj = ring[j * 2], yj = ring[j * 2 + 1];
                if (yj <= py)
                {
                    if (yi > py && (xi - xj) * (py - yj) - (px - xj) * (yi - yj) > 0) wn++;
                }
                else if (yi <= py && (xi - xj) * (py - yj) - (px - xj) * (yi - yj) < 0) wn--;
            }
            return wn;
        }
    }
}
