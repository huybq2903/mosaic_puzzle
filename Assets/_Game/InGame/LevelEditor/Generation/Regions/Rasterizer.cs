using System;
using System.Collections.Generic;

namespace Falcon.InGame.LevelEditor.Gen
{
    /// <summary>Đa giác mảnh → bản đồ pixel và láng giềng (port từ MS.rasterize, fillGaps1 và MS.adjacency của Studio).</summary>
    public static class Rasterizer
    {
        /// <summary>Quét từng hàng, chẵn lẻ, tâm pixel; khe 1 pixel giữa các mảnh được lấp. Mảnh thứ i có id i + 1. sx, sy: pixel trên một ô lưới (1 = bản đồ cùng cỡ lưới).</summary>
        public static RegionMap Rasterize(int width, int height, int unit, IReadOnlyList<GenRegion> regions, double sx = 1, double sy = 1)
        {
            int w = width, h = height;
            var map = new RegionMap { W = w, H = h, Reg = new int[w * h] };
            unit = Math.Max(1, unit);
            var xs = new List<double>();
            for (var i = 0; i < regions.Count; i++)
            {
                var rings = new List<double[]> { Scaled(regions[i].Points, unit, sx, sy) };
                if (regions[i].Holes != null) foreach (var hole in regions[i].Holes) rings.Add(Scaled(hole, unit, sx, sy));
                map.Colors.Add(regions[i].ColorId);

                int y0 = h, y1 = 0;
                foreach (var pts in rings)
                    for (var k = 1; k < pts.Length; k += 2)
                    {
                        y0 = Math.Min(y0, (int)Math.Floor(pts[k]));
                        y1 = Math.Max(y1, (int)Math.Ceiling(pts[k]));
                    }
                y0 = Math.Max(0, y0);
                y1 = Math.Min(h, y1);
                for (var y = y0; y < y1; y++)
                {
                    var yc = y + 0.5;
                    xs.Clear();
                    foreach (var pts in rings)
                    {
                        var n = pts.Length / 2;
                        for (int a = 0, b = n - 1; a < n; b = a++)
                        {
                            double ya = pts[a * 2 + 1], yb = pts[b * 2 + 1];
                            if ((ya > yc) == (yb > yc)) continue;
                            double xa = pts[a * 2], xb = pts[b * 2];
                            xs.Add(xa + (yc - ya) * (xb - xa) / (yb - ya));
                        }
                    }
                    xs.Sort();
                    for (var k = 0; k + 1 < xs.Count; k += 2)
                    {
                        var from = Math.Max(0, (int)Math.Ceiling(xs[k] - 0.5));
                        var to = Math.Min(w, (int)Math.Ceiling(xs[k + 1] - 0.5));
                        for (var x = from; x < to; x++) if (map.Reg[y * w + x] == 0) map.Reg[y * w + x] = i + 1;
                    }
                }
            }
            CloseCracks(map);
            return map;
        }

        private static double[] Scaled(int[] raw, int unit, double sx, double sy)
        {
            var o = new double[raw.Length];
            for (var k = 0; k < raw.Length; k++) o[k] = (double)raw[k] / unit * ((k & 1) == 0 ? sx : sy);
            return o;
        }

        // Pixel trống có từ 2 láng giềng trong mảnh nhận mảnh đầu tiên: lấp vết nứt 1 pixel giữa các đa giác
        private static void CloseCracks(RegionMap m)
        {
            var fill = new List<(int index, int id)>();
            for (var y = 0; y < m.H; y++)
                for (var x = 0; x < m.W; x++)
                {
                    var i = y * m.W + x;
                    if (m.Reg[i] != 0) continue;
                    int cnt = 0, id = 0;
                    void Check(int q)
                    {
                        if (m.Reg[q] <= 0) return;
                        cnt++;
                        if (id == 0) id = m.Reg[q];
                    }
                    if (x > 0) Check(i - 1);
                    if (x < m.W - 1) Check(i + 1);
                    if (y > 0) Check(i - m.W);
                    if (y < m.H - 1) Check(i + m.W);
                    if (cnt >= 2) fill.Add((i, id));
                }
            foreach (var (index, id) in fill) m.Reg[index] = id;
        }

        /// <summary>Láng giềng của từng mảnh (chỉ số 0-based, tăng dần): cặp mảnh chạm nhau hoặc cách nhau một khe mảnh.</summary>
        public static List<int>[] Neighbours(RegionMap map)
        {
            var share = RegionOps.SharedBoundary(map);
            var result = new List<int>[map.Count];
            for (var a = 1; a <= map.Count; a++)
            {
                var list = new List<int>();
                share[a].ForEach((_, b) => list.Add(b - 1));
                list.Sort();
                result[a - 1] = list;
            }
            return result;
        }
    }
}
