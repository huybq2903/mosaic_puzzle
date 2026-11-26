using System;
using System.Collections.Generic;

namespace Falcon.InGame.LevelEditor.Gen
{
    /// <summary>Nhãn thành phần liên thông: Lab[i] = 1..Count (0 = không thuộc tập).</summary>
    public sealed class Labels
    {
        public int[] Lab;
        public int Count;
    }

    /// <summary>Phép xử lý ảnh dùng chung (port từ core.js của Mosaic Studio); điểm ảnh là RGBA byte, mảng thực là float như Float32Array của JS.</summary>
    public static class Ops
    {
        /// <summary>Math.round của JS: làm tròn nửa lên.</summary>
        public static double JsRound(double x) => Math.Floor(x + 0.5);

        public static int Clamp(int v, int a, int b) => v < a ? a : v > b ? b : v;

        public static double Clamp(double v, double a, double b) => v < a ? a : v > b ? b : v;

        public static float[] LabOf(byte[] px, int n)
        {
            var lab = new float[n * 3];
            for (var i = 0; i < n; i++)
            {
                var c = Palette.ToLab(px[i * 4], px[i * 4 + 1], px[i * 4 + 2]);
                lab[i * 3] = (float)c[0];
                lab[i * 3 + 1] = (float)c[1];
                lab[i * 3 + 2] = (float)c[2];
            }
            return lab;
        }

        public static float[] Gauss(float[] src, int w, int h, double sigma)
        {
            if (sigma <= 0) return (float[])src.Clone();
            var r = Math.Max(1, (int)Math.Ceiling(3 * sigma));
            var k = new float[2 * r + 1];
            double sum = 0;
            for (var i = -r; i <= r; i++)
            {
                var e = Math.Exp(-i * i / (2 * sigma * sigma));
                k[i + r] = (float)e;
                sum += e; // như JS: giá trị biểu thức gán là số double, không phải số float đã lưu
            }
            for (var i = 0; i < k.Length; i++) k[i] = (float)(k[i] / sum);
            var tmp = new float[src.Length];
            var res = new float[src.Length];
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    double a = 0;
                    for (var i = -r; i <= r; i++) a += (double)k[i + r] * src[y * w + Clamp(x + i, 0, w - 1)];
                    tmp[y * w + x] = (float)a;
                }
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    double b = 0;
                    for (var i = -r; i <= r; i++) b += (double)k[i + r] * tmp[Clamp(y + i, 0, h - 1) * w + x];
                    res[y * w + x] = (float)b;
                }
            return res;
        }

        private static float[] Extreme(float[] src, int w, int h, int k, bool max)
        {
            var r = k / 2;
            var tmp = new float[src.Length];
            var res = new float[src.Length];
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    var v = src[y * w + x];
                    for (var i = -r; i <= r; i++)
                    {
                        var q = src[y * w + Clamp(x + i, 0, w - 1)];
                        if (max ? q > v : q < v) v = q;
                    }
                    tmp[y * w + x] = v;
                }
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    var v = tmp[y * w + x];
                    for (var i = -r; i <= r; i++)
                    {
                        var q = tmp[Clamp(y + i, 0, h - 1) * w + x];
                        if (max ? q > v : q < v) v = q;
                    }
                    res[y * w + x] = v;
                }
            return res;
        }

        public static float[] Open(float[] g, int w, int h, int k) => Extreme(Extreme(g, w, h, k, false), w, h, k, true);

        public static float[] Close(float[] g, int w, int h, int k) => Extreme(Extreme(g, w, h, k, true), w, h, k, false);

        private static byte[] Morph3(byte[] m, int w, int h, bool dilate)
        {
            var res = new byte[m.Length];
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    var v = dilate ? 0 : 1;
                    for (var dy = -1; dy <= 1 && v == (dilate ? 0 : 1); dy++)
                        for (var dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            var q = nx >= 0 && ny >= 0 && nx < w && ny < h && m[ny * w + nx] != 0 ? 1 : 0;
                            if (dilate ? q != 0 : q == 0) { v = dilate ? 1 : 0; break; }
                        }
                    res[y * w + x] = (byte)v;
                }
            return res;
        }

        public static byte[] Dilate3(byte[] m, int w, int h) => Morph3(m, w, h, true);

        public static byte[] Erode3(byte[] m, int w, int h) => Morph3(m, w, h, false);

        /// <summary>Thành phần liên thông của các điểm đã bật; eight: 8 láng giềng.</summary>
        public static Labels Components(byte[] free, int w, int h, bool eight)
        {
            var lab = new int[free.Length];
            var count = 0;
            var st = new Stack<int>();
            for (var i = 0; i < free.Length; i++)
            {
                if (free[i] == 0 || lab[i] != 0) continue;
                count++;
                lab[i] = count;
                st.Push(i);
                while (st.Count > 0)
                {
                    int p = st.Pop(), x = p % w, y = p / w;
                    for (var dy = -1; dy <= 1; dy++)
                        for (var dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            if (!eight && dx != 0 && dy != 0) continue;
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                            var q = ny * w + nx;
                            if (free[q] != 0 && lab[q] == 0) { lab[q] = count; st.Push(q); }
                        }
                }
            }
            return new Labels { Lab = lab, Count = count };
        }

        /// <summary>Khoảng cách Euclid (pixel) từ mỗi điểm bật tới điểm tắt gần nhất (Felzenszwalb, hai lượt, double).</summary>
        public static float[] Edt(byte[] m, int w, int h)
        {
            const double Inf = 1e9;
            var f = new double[w * h];
            var tmp = new double[w * h];
            for (var i = 0; i < f.Length; i++) f[i] = m[i] != 0 ? Inf : 0;
            Pass(f, tmp, w, h, true);
            Pass(tmp, f, w, h, false);
            var res = new float[w * h];
            for (var i = 0; i < res.Length; i++) res[i] = (float)Math.Sqrt(f[i]);
            return res;
        }

        private static void Pass(double[] src, double[] dst, int w, int h, bool columns)
        {
            int len = columns ? h : w, cnt = columns ? w : h;
            var v = new int[len];
            var z = new double[len + 1];
            var d = new double[len];
            for (var c = 0; c < cnt; c++)
            {
                double At(int i) => columns ? src[i * w + c] : src[c * w + i];
                var k = 0;
                v[0] = 0;
                z[0] = double.NegativeInfinity;
                z[1] = double.PositiveInfinity;
                for (var q = 1; q < len; q++)
                {
                    double s;
                    for (; ; )
                    {
                        var p = v[k];
                        s = ((At(q) + (double)q * q) - (At(p) + (double)p * p)) / (2 * q - 2 * p);
                        if (s <= z[k]) k--; else break;
                    }
                    k++;
                    v[k] = q;
                    z[k] = s;
                    z[k + 1] = double.PositiveInfinity;
                }
                k = 0;
                for (var q = 0; q < len; q++)
                {
                    while (z[k + 1] < q) k++;
                    var p = v[k];
                    d[q] = (double)(q - p) * (q - p) + At(p);
                }
                for (var q = 0; q < len; q++)
                {
                    if (columns) dst[q * w + c] = d[q]; else dst[c * w + q] = d[q];
                }
            }
        }

        /// <summary>Đánh số lại nhãn liên tiếp 1..n (nhãn ≤ 0 giữ nguyên); trả số nhãn.</summary>
        public static int Compact(int[] lab)
        {
            var map = new Dictionary<int, int>();
            var n = 0;
            for (var i = 0; i < lab.Length; i++)
            {
                if (lab[i] <= 0) continue;
                if (!map.TryGetValue(lab[i], out var id)) { id = ++n; map[lab[i]] = id; }
                lab[i] = id;
            }
            return n;
        }
    }

    /// <summary>Khung xương: Zhang-Suen làm mảnh, nối đầu hở (port từ core.js).</summary>
    public static class Skeleton
    {
        public static byte[] Thin(byte[] src, int w, int h)
        {
            var m = (byte[])src.Clone();
            var del = new List<int>();
            bool changed;
            do
            {
                changed = false;
                for (var pass = 0; pass < 2; pass++)
                {
                    del.Clear();
                    for (var y = 1; y < h - 1; y++)
                        for (var x = 1; x < w - 1; x++)
                        {
                            var i = y * w + x;
                            if (m[i] == 0) continue;
                            int p2 = m[i - w], p3 = m[i - w + 1], p4 = m[i + 1], p5 = m[i + w + 1], p6 = m[i + w], p7 = m[i + w - 1], p8 = m[i - 1], p9 = m[i - w - 1];
                            var b = p2 + p3 + p4 + p5 + p6 + p7 + p8 + p9;
                            if (b < 2 || b > 6) continue;
                            var a = (p2 == 0 && p3 != 0 ? 1 : 0) + (p3 == 0 && p4 != 0 ? 1 : 0) + (p4 == 0 && p5 != 0 ? 1 : 0) + (p5 == 0 && p6 != 0 ? 1 : 0)
                                  + (p6 == 0 && p7 != 0 ? 1 : 0) + (p7 == 0 && p8 != 0 ? 1 : 0) + (p8 == 0 && p9 != 0 ? 1 : 0) + (p9 == 0 && p2 != 0 ? 1 : 0);
                            if (a != 1) continue;
                            if (pass == 0 ? (p2 != 0 && p4 != 0 && p6 != 0) || (p4 != 0 && p6 != 0 && p8 != 0) : (p2 != 0 && p4 != 0 && p8 != 0) || (p2 != 0 && p6 != 0 && p8 != 0)) continue;
                            del.Add(i);
                        }
                    for (var k = 0; k < del.Count; k++) m[del[k]] = 0;
                    if (del.Count > 0) changed = true;
                }
            } while (changed);
            return m;
        }

        public static int Degree(byte[] sk, int w, int h, int i)
        {
            int x = i % w, y = i / w, n = 0;
            for (var dy = -1; dy <= 1; dy++)
                for (var dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nx = x + dx, ny = y + dy;
                    if (nx >= 0 && ny >= 0 && nx < w && ny < h && sk[ny * w + nx] != 0) n++;
                }
            return n;
        }

        // đầu mút vẽ thành bậc chéo: hai láng giềng chạm nhau
        private static bool StairEnd(byte[] sk, int w, int h, int i)
        {
            int x = i % w, y = i / w;
            var nb = new List<(int dx, int dy)>();
            for (var dy = -1; dy <= 1; dy++)
                for (var dx = -1; dx <= 1; dx++)
                    if ((dx != 0 || dy != 0) && x + dx >= 0 && y + dy >= 0 && x + dx < w && y + dy < h && sk[(y + dy) * w + x + dx] != 0) nb.Add((dx, dy));
            return nb.Count == 2 && Math.Abs(nb[0].dx - nb[1].dx) <= 1 && Math.Abs(nb[0].dy - nb[1].dy) <= 1;
        }

        /// <summary>Cắt các nhánh cụt ngắn hơn len.</summary>
        public static void PruneSpurs(byte[] sk, int w, int h, int len)
        {
            for (var round = 0; round < 2; round++)
            {
                var any = false;
                for (var i = 0; i < sk.Length; i++)
                {
                    if (sk[i] == 0 || Degree(sk, w, h, i) > 1) continue;
                    var path = new List<int> { i };
                    int prev = -1, cur = i;
                    var junction = false;
                    while (path.Count <= len)
                    {
                        int next = -1, x = cur % w, y = cur / w, cnt = 0;
                        for (var dy = -1; dy <= 1; dy++)
                            for (var dx = -1; dx <= 1; dx++)
                            {
                                if (dx == 0 && dy == 0) continue;
                                int nx = x + dx, ny = y + dy;
                                if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                                var q = ny * w + nx;
                                if (sk[q] == 0 || q == prev || path.Contains(q)) continue;
                                cnt++;
                                next = q;
                            }
                        if (cnt == 0) break;
                        if (cnt > 1 || Degree(sk, w, h, next) > 2) { junction = true; break; }
                        prev = cur;
                        cur = next;
                        path.Add(cur);
                    }
                    if (path.Count > len) continue;
                    if (!junction && Degree(sk, w, h, cur) > 1) continue;
                    for (var k = 0; k < path.Count; k++) sk[path[k]] = 0;
                    any = true;
                }
                if (!any) return;
            }
        }

        private static bool OnInk(byte[] ink, int w, double ax, double ay, double bx, double by)
        {
            var steps = (int)Math.Ceiling(Math.Max(Math.Abs(bx - ax), Math.Abs(by - ay)));
            if (steps == 0) return true;
            var hit = 0;
            for (var t = 0; t <= steps; t++)
            {
                var px = (int)Ops.JsRound(ax + (bx - ax) * t / steps);
                var py = (int)Ops.JsRound(ay + (by - ay) * t / steps);
                if (ink[py * w + px] != 0) hit++;
            }
            return hit >= 0.85 * (steps + 1);
        }

        /// <summary>Nối mỗi đầu mút với đường khác phía trước trong maxDist.</summary>
        public static void CloseGaps(byte[] sk, int w, int h, int maxDist, byte[] ink, byte[] skipEnd, double minCos = 0.7, bool stairEnds = false)
        {
            if (minCos == 0) minCos = 0.7;
            var ends = new List<int>();
            for (var i = 0; i < sk.Length; i++)
                if (sk[i] != 0 && (Degree(sk, w, h, i) <= 1 || (stairEnds && StairEnd(sk, w, h, i)))) ends.Add(i);
            foreach (var e in ends)
            {
                if (skipEnd != null && skipEnd[e] != 0) continue;
                var branch = new List<int> { e };
                int prev = -1, cur = e;
                for (var k = 0; k < 8; k++)
                {
                    int x = cur % w, y = cur / w, next = -1;
                    for (var dy = -1; dy <= 1 && next < 0; dy++)
                        for (var dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                            var q = ny * w + nx;
                            if (sk[q] != 0 && q != prev && !branch.Contains(q)) { next = q; break; }
                        }
                    if (next < 0) break;
                    prev = cur;
                    cur = next;
                    branch.Add(cur);
                }
                int ex = e % w, ey = e / w;
                double dirX = ex - cur % w, dirY = ey - cur / w, dl = Math.Sqrt(dirX * dirX + dirY * dirY);
                if (dl < 1e-3) continue;
                dirX /= dl;
                dirY /= dl;
                var best = -1;
                var bd = double.PositiveInfinity;
                for (var yy = Math.Max(0, ey - maxDist); yy <= Math.Min(h - 1, ey + maxDist); yy++)
                    for (var xx = Math.Max(0, ex - maxDist); xx <= Math.Min(w - 1, ex + maxDist); xx++)
                    {
                        var qq = yy * w + xx;
                        if (sk[qq] == 0 || branch.Contains(qq)) continue;
                        double vx = xx - ex, vy = yy - ey, d = Math.Sqrt(vx * vx + vy * vy);
                        if (d < 1.5 || d > maxDist || (vx * dirX + vy * dirY) / d < minCos) continue;
                        if (d >= bd) continue;
                        if (ink != null && !OnInk(ink, w, ex, ey, xx, yy)) continue;
                        bd = d;
                        best = qq;
                    }
                if (best < 0) continue;
                var steps = (int)Math.Ceiling(bd * 2);
                int tx = best % w, ty = best / w;
                for (var t = 0; t <= steps; t++)
                {
                    var px = (int)Ops.JsRound(ex + (double)(tx - ex) * t / steps);
                    var py = (int)Ops.JsRound(ey + (double)(ty - ey) * t / steps);
                    sk[py * w + px] = 1;
                }
            }
        }
    }
}
