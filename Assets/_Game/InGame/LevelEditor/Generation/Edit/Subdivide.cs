using System;
using System.Collections.Generic;
using System.Linq;

namespace Falcon.InGame.LevelEditor.Gen
{
    /// <summary>Kết quả chia: chỉ số ô của từng pixel (cùng thứ tự với pixel đầu vào) và số ô.</summary>
    public sealed class CellAssign
    {
        public int[] Cell;
        public int Cells;
    }

    /// <summary>Chia một mảnh thành các ô theo một kiểu mosaic (port từ subdivide.js của Studio).</summary>
    public static class Subdivide
    {
        public static readonly string[] Patterns = { "Auto", "Brick", "Bands", "Rays", "Rings", "Scales", "Hex", "Mondrian", "Diamond" };
        public static readonly string[] Labels = { "Tự động", "Gạch", "Dải cong", "Tia", "Vòng", "Vảy", "Tổ ong", "Mondrian", "Kim cương" };
        public const int MinCount = 2, MaxCount = 20;

        private const double TileAspect = 1.35, StaggerJitter = 0.12, RowWobble = 0.08, MinTileFraction = 0.4, TypicalFraction = 0.0157;
        private const int Offset = 4096, Stride = 8192;

        /// <summary>Số mảnh gợi ý cho mảnh chiếm areaFraction (0..1) của tranh: ô con cỡ một mảnh trong game.</summary>
        public static int Recommend(double areaFraction) => Math.Max(MinCount, Math.Min(MaxCount, (int)Ops.JsRound(areaFraction / TypicalFraction)));

        // ------------------------------------------------------------------ tiện ích số
        private static Func<double> Mulberry32(int a)
        {
            return () =>
            {
                unchecked
                {
                    a += 0x6D2B79F5;
                    var t = (uint)a;
                    t = (t ^ (t >> 15)) * (1u | (uint)a);
                    t = (t + (t ^ (t >> 7)) * (61u | t)) ^ t;
                    return ((t ^ (t >> 14))) / 4294967296.0;
                }
            };
        }

        private static int Key(int a, int b) => unchecked((a + Offset) * Stride + b + Offset);

        private static double Hash(int row)
        {
            unchecked
            {
                var x = (uint)(row * 73856093) ^ 0x9E3779B9u;
                x ^= x >> 15;
                x *= 0x2C1B3C6Du;
                x ^= x >> 12;
                return (x & 0xFFFF) / 32767.5 - 1;
            }
        }

        private static double Clamp(double v, double a, double b) => v < a ? a : v > b ? b : v;

        private static int Floor(double v) => (int)Math.Floor(v);

        // ------------------------------------------------------------------ khung trục chính của mảnh
        private sealed class Frame
        {
            public int N;
            public float[] U, V;
            public double UMin = double.PositiveInfinity, UMax = double.NegativeInfinity, VMin = double.PositiveInfinity, VMax = double.NegativeInfinity;
            public double Su, Sv, Aspect;
        }

        // Toạ độ pixel theo trục chính (gốc ở trọng tâm): u dọc trục dài, v ngang
        private static Frame MakeFrame(IReadOnlyList<int> pix, int w, double mx, double my)
        {
            double sxx = 0, sxy = 0, syy = 0;
            var n = pix.Count;
            for (var i = 0; i < n; i++)
            {
                double dx = pix[i] % w - mx, dy = pix[i] / w - my;
                sxx += dx * dx;
                syy += dy * dy;
                sxy += dx * dy;
            }
            var ang = 0.5 * Math.Atan2(2 * sxy, sxx - syy);
            double ux = Math.Cos(ang), uy = Math.Sin(ang);
            var f = new Frame { N = n, U = new float[n], V = new float[n] };
            double su2 = 0, sv2 = 0;
            for (var i = 0; i < n; i++)
            {
                double ddx = pix[i] % w - mx, ddy = pix[i] / w - my, u = ddx * ux + ddy * uy, v = -ddx * uy + ddy * ux;
                f.U[i] = (float)u;
                f.V[i] = (float)v;
                f.UMin = Math.Min(f.UMin, u);
                f.UMax = Math.Max(f.UMax, u);
                f.VMin = Math.Min(f.VMin, v);
                f.VMax = Math.Max(f.VMax, v);
                su2 += u * u;
                sv2 += v * v;
            }
            f.Su = Math.Max(Math.Abs(f.UMin), Math.Abs(f.UMax));
            f.Sv = Math.Max(Math.Abs(f.VMin), Math.Abs(f.VMax));
            f.Aspect = Math.Sqrt(su2 / Math.Max(1e-6, sv2));
            return f;
        }

        // Mảnh sát mép tranh (nền) dùng kiểu trang trí, mảnh chủ thể theo hình của nó
        private static string Choose(Frame f, int target, bool background, int seed)
        {
            var pick = (seed & 0x7FFFFFFF) % 1000;
            string Of(string[] options) => options[pick % options.Length];
            if (background)
            {
                if (f.Aspect >= 2.5) return Of(new[] { "Brick", "Mondrian", "Bands" });
                if (target >= 10) return Of(new[] { "Rings", "Scales", "Hex", "Rays", "Mondrian" });
                if (target >= 6) return Of(new[] { "Rays", "Hex", "Mondrian", "Scales" });
                return Of(new[] { "Mondrian", "Brick", "Diamond" });
            }
            if (f.Aspect >= 2) return "Bands";
            if (f.Aspect < 1.4 && target >= 12) return "Rings";
            return "Brick";
        }

        // ------------------------------------------------------------------ các kiểu
        private static int[] BrickKeys(Frame f, double r)
        {
            var c = r * TileAspect;
            var keys = new int[f.N];
            for (var i = 0; i < f.N; i++)
            {
                var wobble = RowWobble * r * Math.Sin((f.U[i] - f.UMin) / (5 * c) * 2 * Math.PI);
                var row = Floor((f.V[i] - f.VMin + wobble) / r);
                var shift = (row & 1) * 0.5 * c + StaggerJitter * c * Hash(row);
                keys[i] = Key(row, Floor((f.U[i] - f.UMin + shift) / c));
            }
            return keys;
        }

        private static int[] HexKeys(Frame f, double a)
        {
            var keys = new int[f.N];
            for (var i = 0; i < f.N; i++)
            {
                var q = (0.57735 * f.U[i] - 0.33333 * f.V[i]) / a;
                var rr = 0.66667 * f.V[i] / a;
                double x = q, z = rr, y = -x - z;
                double rx = Ops.JsRound(x), ry = Ops.JsRound(y), rz = Ops.JsRound(z);
                double dx = Math.Abs(rx - x), dy = Math.Abs(ry - y), dz = Math.Abs(rz - z);
                if (dx > dy && dx > dz) rx = -ry - rz;
                else if (dy > dz) ry = -rx - rz;
                else rz = -rx - ry;
                keys[i] = Key((int)rz, (int)rx);
            }
            return keys;
        }

        private static int[] DiamondKeys(Frame f, double s)
        {
            var keys = new int[f.N];
            for (var i = 0; i < f.N; i++) keys[i] = Key(Floor(((double)f.U[i] + f.V[i]) / s), Floor(((double)f.U[i] - f.V[i]) / s));
            return keys;
        }

        private static int[] ScaleKeys(Frame f, double rad)
        {
            var rowH = 0.75 * rad;
            var keys = new int[f.N];
            for (var i = 0; i < f.N; i++)
            {
                var u = f.U[i] - f.UMin;
                var v = f.V[i] - f.VMin;
                int top = Floor((v + rad) / rowH), bottom = (int)Math.Ceiling((v - rad) / rowH);
                int? k = null;
                for (var row = top; row >= bottom; row--)
                {
                    var shift = (row & 1) * rad;
                    var j = Ops.JsRound((u - shift) / (2 * rad));
                    var dx = u - (shift + j * 2 * rad);
                    var dy = v - row * rowH;
                    if (dx * dx + dy * dy <= rad * rad) { k = Key(row, (int)j); break; }
                }
                k ??= Key((int)Ops.JsRound(v / rowH), (int)Ops.JsRound(u / (2 * rad)));
                keys[i] = k.Value;
            }
            return keys;
        }

        private static void SmoothArr(float[] a, int passes)
        {
            var t = new float[a.Length];
            for (var p = 0; p < passes; p++)
            {
                for (var i = 0; i < a.Length; i++) t[i] = (float)((a[Math.Max(0, i - 1)] + 2.0 * a[i] + a[Math.Min(a.Length - 1, i + 1)]) * 0.25);
                Array.Copy(t, a, a.Length);
            }
        }

        private static int[] BandKeys(Frame f, int target)
        {
            const int Bins = 48;
            var lo = new float[Bins];
            var hi = new float[Bins];
            Array.Fill(lo, float.PositiveInfinity);
            Array.Fill(hi, float.NegativeInfinity);
            var span = Math.Max(1e-3, f.UMax - f.UMin);
            for (var i = 0; i < f.N; i++)
            {
                var b = (int)Clamp(Math.Truncate((f.U[i] - f.UMin) / span * Bins), 0, Bins - 1);
                lo[b] = Math.Min(lo[b], f.V[i]);
                hi[b] = Math.Max(hi[b], f.V[i]);
            }
            var mid = new float[Bins];
            var half = new float[Bins];
            var have = new bool[Bins];
            double meanHalf = 0;
            var hn = 0;
            for (var b = 0; b < Bins; b++)
            {
                have[b] = hi[b] >= lo[b];
                if (!have[b]) continue;
                mid[b] = (float)(((double)lo[b] + hi[b]) * 0.5);
                half[b] = (float)(((double)hi[b] - lo[b]) * 0.5);
                meanHalf += half[b];
                hn++;
            }
            if (hn < 2) return null;
            meanHalf /= hn;
            for (var b = 0; b < Bins; b++)
            {
                if (have[b]) continue;
                int l = b, r = b;
                while (l >= 0 && !have[l]) l--;
                while (r < Bins && !have[r]) r++;
                if (l < 0) l = r;
                if (r >= Bins) r = l;
                mid[b] = (float)(((double)mid[l] + mid[r]) * 0.5);
                half[b] = (float)(((double)half[l] + half[r]) * 0.5);
            }
            SmoothArr(mid, 3);
            SmoothArr(half, 3);
            var width = f.VMax - f.VMin;
            var nbands = (int)Clamp(Ops.JsRound(Math.Sqrt(target * width / span)), 2, 8);
            var nseg = Math.Max(1, (int)Math.Ceiling((double)target / nbands));
            var segLen = span / nseg;
            var keys = new int[f.N];
            for (var i = 0; i < f.N; i++)
            {
                var t = Clamp((f.U[i] - f.UMin) / span * Bins - 0.5, 0, Bins - 1.001);
                var b0 = (int)Math.Truncate(t);
                var fr = t - b0;
                double m = mid[b0] + ((double)mid[b0 + 1] - mid[b0]) * fr;
                var h = Math.Max(0.35 * meanHalf, half[b0] + ((double)half[b0 + 1] - half[b0]) * fr);
                var vv = Clamp((f.V[i] - m) / h, -1, 1);
                var band = Math.Min(nbands - 1, Floor((vv + 1) * 0.5 * nbands));
                var seg = Floor((f.U[i] - f.UMin + (band & 1) * 0.5 * segLen) / segLen);
                keys[i] = Key(band, seg);
            }
            return keys;
        }

        private static int[] RayKeys(Frame f, int target)
        {
            var n = (int)Clamp(target, 3, 28);
            var hub = n >= 9 ? 0.14 : 0;
            var rmax = Math.Sqrt(f.Su * f.Su + f.Sv * f.Sv);
            var keys = new int[f.N];
            for (var i = 0; i < f.N; i++)
            {
                if (hub > 0 && (double)f.U[i] * f.U[i] + (double)f.V[i] * f.V[i] < hub * hub * rmax * rmax) { keys[i] = Key(0, 0); continue; }
                var th = Math.Atan2(f.V[i], f.U[i]) + Math.PI;
                keys[i] = Key(1, Math.Min(n - 1, Floor(th / (2 * Math.PI) * n)) + 1);
            }
            return keys;
        }

        private static int RingCount(int k) => Math.Max(4, (int)Ops.JsRound(2 * Math.PI * (k + 0.5) / 1.25));

        private static int[] RingKeys(Frame f, int target)
        {
            int Cells(int nr)
            {
                var t = 1;
                for (var k = 1; k < nr; k++) t += RingCount(k);
                return t;
            }
            int best = 2, bestD = int.MaxValue;
            for (var nr = 2; nr <= 8; nr++)
            {
                var d = Math.Abs(Cells(nr) - target);
                if (d < bestD) { bestD = d; best = nr; }
            }
            var nrings = best;
            double rmax = 1e-3;
            var rad = new float[f.N];
            var ang = new float[f.N];
            for (var i = 0; i < f.N; i++)
            {
                var x = f.U[i] / Math.Max(1e-3, f.Su);
                var y = f.V[i] / Math.Max(1e-3, f.Sv);
                rad[i] = (float)Math.Sqrt(x * x + y * y);
                ang[i] = (float)(Math.Atan2(y, x) + Math.PI);
                rmax = Math.Max(rmax, rad[i]);
            }
            var keys = new int[f.N];
            var hub = 0.55 * rmax / nrings;
            var ringW = (rmax - hub) / (nrings - 1);
            for (var i = 0; i < f.N; i++)
            {
                if (rad[i] < hub) { keys[i] = Key(0, 0); continue; }
                var ring = Math.Min(nrings - 1, 1 + Floor((rad[i] - hub) / ringW));
                var cnt = RingCount(ring);
                var sec = Floor(ang[i] / (2 * Math.PI) * cnt + 0.5 * (ring & 1)) % cnt;
                keys[i] = Key(ring, sec);
            }
            return keys;
        }

        private static int[] MondrianKeys(Frame f, int target)
        {
            var rnd = Mulberry32(f.N * 31 + target);
            var groups = new List<List<int>> { Enumerable.Range(0, f.N).ToList() };
            var sample = new float[512];
            while (groups.Count < target)
            {
                var gi = 0;
                for (var k = 1; k < groups.Count; k++) if (groups[k].Count > groups[gi].Count) gi = k;
                var g = groups[gi];
                if (g.Count < 16) break;
                double u0 = double.PositiveInfinity, u1 = double.NegativeInfinity, v0 = double.PositiveInfinity, v1 = double.NegativeInfinity;
                foreach (var j in g)
                {
                    u0 = Math.Min(u0, f.U[j]);
                    u1 = Math.Max(u1, f.U[j]);
                    v0 = Math.Min(v0, f.V[j]);
                    v1 = Math.Max(v1, f.V[j]);
                }
                var vals = (u1 - u0) >= (v1 - v0) ? f.U : f.V;
                for (var s = 0; s < sample.Length; s++) sample[s] = vals[g[Floor(rnd() * g.Count)]];
                Array.Sort(sample);
                var q = 0.35 + 0.3 * rnd();
                double cut = sample[(int)Clamp(Math.Truncate(q * sample.Length), 0, sample.Length - 1)];
                var a = new List<int>();
                var b = new List<int>();
                foreach (var j in g) (vals[j] < cut ? a : b).Add(j);
                if (a.Count == 0 || b.Count == 0) break;
                groups[gi] = a;
                groups.Add(b);
            }
            var keys = new int[f.N];
            for (var k = 0; k < groups.Count; k++) foreach (var j in groups[k]) keys[j] = Key(0, k);
            return keys;
        }

        private static int CountCells(int[] keys, double full)
        {
            var counts = new Dictionary<int, int>();
            foreach (var k in keys) counts[k] = counts.GetValueOrDefault(k) + 1;
            var big = 0;
            foreach (var n in counts.Values) if (n >= MinTileFraction * full) big++;
            return big;
        }

        // Kiểu có cỡ liên tục: tìm cỡ cho ra khoảng target ô nguyên
        private static int[] Searched(int target, double area, Func<double, int[]> keysAt, Func<double, double> cellArea)
        {
            var nominal = Math.Sqrt(area / target / cellArea(1));
            double lo = nominal * 0.4, hi = nominal * 2.5;
            for (var it = 0; it < 14; it++)
            {
                var mid = (lo + hi) * 0.5;
                if (CountCells(keysAt(mid), cellArea(mid)) > target) lo = mid;
                else hi = mid;
            }
            return keysAt(hi);
        }

        private static int[] KeysFor(string pattern, Frame f, int target, double area)
        {
            switch (pattern)
            {
                case "Bands": return BandKeys(f, target);
                case "Rays": return RayKeys(f, target);
                case "Rings": return RingKeys(f, target);
                case "Mondrian": return MondrianKeys(f, target);
                case "Scales": return Searched(target, area, r => ScaleKeys(f, r), r => 1.5 * r * r);
                case "Hex": return Searched(target, area, a => HexKeys(f, a), a => 2.598 * a * a);
                case "Diamond": return Searched(target, area, s => DiamondKeys(f, s), s => 0.5 * s * s);
                default: return Searched(target, area, r => BrickKeys(f, r), r => TileAspect * r * r);
            }
        }

        // ------------------------------------------------------------------ gán ô cho pixel
        /// <summary>Ô của từng pixel trong pix; null khi mảnh quá nhỏ hoặc hẹp.</summary>
        public static CellAssign FlowAssign(int mapW, int mapH, IReadOnlyList<int> pix, int target, int patternIndex)
        {
            var w = mapW;
            if (pix.Count == 0) return null;
            target = Math.Min(target, (int)Math.Truncate((double)pix.Count / Math.Max(8, (int)Math.Truncate((double)mapW * mapH / 5000))));
            if (target < 2) return null;
            int bx0 = int.MaxValue, by0 = int.MaxValue, bx1 = -1, by1 = -1;
            double mx = 0, my = 0;
            for (var i = 0; i < pix.Count; i++)
            {
                int x = pix[i] % w, y = pix[i] / w;
                bx0 = Math.Min(bx0, x);
                bx1 = Math.Max(bx1, x);
                by0 = Math.Min(by0, y);
                by1 = Math.Max(by1, y);
                mx += x;
                my += y;
            }
            mx /= pix.Count;
            my /= pix.Count;
            int lw = bx1 - bx0 + 1, lh = by1 - by0 + 1;
            var f = MakeFrame(pix, w, mx, my);
            var pattern = Patterns[patternIndex];
            if (pattern == "Auto")
            {
                var margin = Math.Max(2, (int)Ops.JsRound(0.04 * Math.Max(mapW, mapH)));
                var background = bx0 <= margin || by0 <= margin || bx1 >= mapW - 1 - margin || by1 >= mapH - 1 - margin;
                pattern = Choose(f, target, background, pix.Count * 31 + pix[0]);
            }
            var keys = KeysFor(pattern, f, target, pix.Count);
            if (keys == null) return null;

            var keyMap = new int[lw * lh];
            Array.Fill(keyMap, -1);
            var local = new int[pix.Count];
            for (var i = 0; i < pix.Count; i++)
            {
                local[i] = (pix[i] / w - by0) * lw + pix[i] % w - bx0;
                keyMap[local[i]] = keys[i];
            }

            // phần liên thông của mỗi khoá (ô bị vùng lõm cắt đôi thành hai phần), rồi phần nhỏ nhập vào ô kề có biên chung dài nhất
            var comp = new int[lw * lh];
            var count = 0;
            var st = new Stack<int>();
            for (var i = 0; i < keyMap.Length; i++)
            {
                if (keyMap[i] < 0 || comp[i] != 0) continue;
                count++;
                comp[i] = count;
                st.Push(i);
                while (st.Count > 0)
                {
                    var p = st.Pop();
                    int px = p % lw, py = p / lw;
                    void Visit(int q)
                    {
                        if (comp[q] == 0 && keyMap[q] == keyMap[i]) { comp[q] = count; st.Push(q); }
                    }
                    if (px > 0) Visit(p - 1);
                    if (px < lw - 1) Visit(p + 1);
                    if (py > 0) Visit(p - lw);
                    if (py < lh - 1) Visit(p + lw);
                }
            }
            if (count > 40 * target) return null;

            var area = new int[count + 1];
            var parent = new int[count + 1];
            foreach (var c in comp) if (c > 0) area[c]++;
            for (var i = 0; i < parent.Length; i++) parent[i] = i;
            int Find(int x)
            {
                while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; }
                return x;
            }

            var borderKeys = new List<long>();
            var border = new Dictionary<long, int>();
            for (var y2 = 0; y2 < lh; y2++)
                for (var x2 = 0; x2 < lw; x2++)
                {
                    var a1 = comp[y2 * lw + x2];
                    if (a1 == 0) continue;
                    void Pair(int j)
                    {
                        var b1 = comp[j];
                        if (b1 == 0 || b1 == a1) return;
                        var k = (long)Math.Min(a1, b1) * (count + 1) + Math.Max(a1, b1);
                        if (border.TryGetValue(k, out var v)) border[k] = v + 1;
                        else { border[k] = 1; borderKeys.Add(k); }
                    }
                    if (x2 + 1 < lw) Pair(y2 * lw + x2 + 1);
                    if (y2 + 1 < lh) Pair((y2 + 1) * lw + x2);
                }

            var minArea = MinTileFraction * pix.Count / target;
            for (; ; )
            {
                var small = 0;
                for (var i = 1; i <= count; i++) if (Find(i) == i && (small == 0 || area[i] < area[small])) small = i;
                if (small == 0 || area[small] >= minArea) break;
                int bestN = 0, bestL = 0;
                foreach (var kk in borderKeys)
                {
                    var len = border[kk];
                    var p = Find((int)(kk / (count + 1)));
                    var q = Find((int)(kk % (count + 1)));
                    if (p == q || (p != small && q != small)) continue;
                    var other = p == small ? q : p;
                    if (len > bestL) { bestL = len; bestN = other; }
                }
                if (bestN == 0) break;
                parent[small] = bestN;
                area[bestN] += area[small];
            }

            var index = new Dictionary<int, int>();
            var result = new int[pix.Count];
            for (var i = 0; i < pix.Count; i++)
            {
                var root = Find(comp[local[i]]);
                if (!index.TryGetValue(root, out var id)) index[root] = id = index.Count;
                result[i] = id;
            }
            return index.Count < 2 ? null : new CellAssign { Cell = result, Cells = index.Count };
        }
    }

    /// <summary>Màu kề nhau học từ 100 level game (23 màu game); màu từ 23 trở lên dùng màu game gần nhất.</summary>
    public static class ColorAdjacency
    {
        private static readonly int[][] Counts =
        {
            new[] { 134, 69, 168, 104, 156, 17, 120, 139, 95, 137, 55, 31, 141, 16, 56, 36, 7, 13, 6, 193, 13, 20, 19 },
            new[] { 69, 46, 163, 87, 79, 25, 90, 108, 62, 60, 28, 28, 106, 18, 28, 4, 5, 3, 2, 155, 13, 13, 20 },
            new[] { 168, 163, 165, 93, 143, 59, 245, 257, 97, 147, 63, 63, 185, 27, 82, 13, 15, 25, 7, 357, 20, 26, 32 },
            new[] { 104, 87, 93, 64, 299, 19, 103, 118, 78, 72, 43, 61, 45, 5, 42, 7, 67, 3, 14, 101, 7, 18, 13 },
            new[] { 156, 79, 143, 299, 117, 42, 159, 202, 94, 111, 66, 73, 103, 2, 37, 3, 44, 16, 11, 128, 38, 32, 28 },
            new[] { 17, 25, 59, 19, 42, 23, 78, 43, 13, 51, 48, 26, 58, 0, 16, 2, 7, 10, 4, 35, 2, 18, 46 },
            new[] { 120, 90, 245, 103, 159, 78, 87, 509, 107, 155, 68, 62, 176, 17, 33, 7, 9, 17, 5, 125, 26, 14, 68 },
            new[] { 139, 108, 257, 118, 202, 43, 509, 156, 125, 195, 77, 56, 216, 7, 30, 3, 20, 21, 13, 178, 14, 14, 132 },
            new[] { 95, 62, 97, 78, 94, 13, 107, 125, 31, 102, 33, 24, 72, 4, 30, 3, 2, 2, 1, 78, 21, 23, 16 },
            new[] { 137, 60, 147, 72, 111, 51, 155, 195, 102, 77, 122, 31, 112, 3, 42, 1, 14, 13, 4, 180, 11, 23, 33 },
            new[] { 55, 28, 63, 43, 66, 48, 68, 77, 33, 122, 40, 50, 87, 9, 27, 0, 17, 12, 1, 100, 3, 22, 14 },
            new[] { 31, 28, 63, 61, 73, 26, 62, 56, 24, 31, 50, 77, 64, 7, 60, 3, 9, 6, 0, 92, 9, 17, 28 },
            new[] { 141, 106, 185, 45, 103, 58, 176, 216, 72, 112, 87, 64, 127, 23, 76, 17, 6, 13, 9, 147, 11, 16, 70 },
            new[] { 16, 18, 27, 5, 2, 0, 17, 7, 4, 3, 9, 7, 23, 9, 9, 3, 0, 0, 0, 20, 3, 2, 1 },
            new[] { 56, 28, 82, 42, 37, 16, 33, 30, 30, 42, 27, 60, 76, 9, 45, 4, 15, 8, 2, 63, 6, 18, 26 },
            new[] { 36, 4, 13, 7, 3, 2, 7, 3, 3, 1, 0, 3, 17, 3, 4, 4, 0, 2, 0, 7, 0, 6, 5 },
            new[] { 7, 5, 15, 67, 44, 7, 9, 20, 2, 14, 17, 9, 6, 0, 15, 0, 29, 0, 0, 14, 0, 3, 12 },
            new[] { 13, 3, 25, 3, 16, 10, 17, 21, 2, 13, 12, 6, 13, 0, 8, 2, 0, 7, 3, 16, 6, 4, 0 },
            new[] { 6, 2, 7, 14, 11, 4, 5, 13, 1, 4, 1, 0, 9, 0, 2, 0, 0, 3, 11, 3, 0, 0, 4 },
            new[] { 193, 155, 357, 101, 128, 35, 125, 178, 78, 180, 100, 92, 147, 20, 63, 7, 14, 16, 3, 162, 18, 31, 21 },
            new[] { 13, 13, 20, 7, 38, 2, 26, 14, 21, 11, 3, 9, 11, 3, 6, 0, 0, 6, 0, 18, 14, 12, 4 },
            new[] { 20, 13, 26, 18, 32, 18, 14, 14, 23, 23, 22, 17, 16, 2, 18, 6, 3, 4, 0, 31, 12, 11, 12 },
            new[] { 19, 20, 32, 13, 28, 46, 68, 132, 16, 33, 14, 28, 70, 1, 26, 5, 12, 0, 4, 21, 4, 12, 30 },
        };

        private static readonly List<int> Above = Enumerable.Range(23, Palette.Count - 23).ToList();

        private static int Anchor(int colorId)
        {
            if (colorId < 23) return colorId;
            var lab = Palette.Lab[colorId];
            return Palette.NearestLab(lab[0], lab[1], lab[2], Above);
        }

        /// <summary>Màu ngẫu nhiên có trọng số kề baseColor, tránh avoid (-1 = không; vẫn cho phép 10% như game).</summary>
        public static int Pick(int baseColor, int avoid, Func<double> rnd)
        {
            var row = Counts[Anchor(baseColor)];
            double total = 0;
            var w = new double[23];
            for (var c = 0; c < 23; c++)
            {
                w[c] = row[c];
                if (c == avoid && rnd() > 0.1) w[c] = 0;
                total += w[c];
            }
            if (total <= 0) return baseColor;
            var r = rnd() * total;
            for (var c = 0; c < 23; c++)
            {
                r -= w[c];
                if (r <= 0 && w[c] > 0) return c;
            }
            return baseColor;
        }

        internal static Func<double> Rng(int seed)
        {
            var a = seed;
            return () =>
            {
                unchecked
                {
                    a += 0x6D2B79F5;
                    var t = (uint)a;
                    t = (t ^ (t >> 15)) * (1u | (uint)a);
                    t = (t + (t ^ (t >> 7)) * (61u | t)) ^ t;
                    return (t ^ (t >> 14)) / 4294967296.0;
                }
            };
        }
    }
}
