using System;
using System.Collections.Generic;
using System.Linq;

namespace Falcon.InGame.LevelEditor.Gen
{
    /// <summary>Một đoạn biên của mảnh A: chung với mảnh kề B (-1 = biên ngoài tranh), từ đỉnh Lo tới Hi của vòng A, Pts gồm cả hai đầu.</summary>
    public sealed class EdgeRun
    {
        public int A, B, Lo, Hi;
        public List<P> Pts;
    }

    /// <summary>Chia biên mảnh thành các đoạn, khớp Bézier để sửa, lấy mẫu lại và thay đoạn trong tranh (port edge_skeleton.js, chưa hỗ trợ khe nét).</summary>
    public static class EdgeSkeleton
    {
        private const double FitTolerance = 1, TangentReach = 6, WarpCells = 30, WarpRatio = 3;

        private static long Key(P p) => (long)p.X * 100003 + p.Y;

        private static List<P> RingOf(int[] a)
        {
            var o = new List<P>(a.Length / 2);
            for (var i = 0; i + 1 < a.Length; i += 2) o.Add(new P(a[i], a[i + 1]));
            return o;
        }

        private static double DistSeg(double px, double py, double ax, double ay, double bx, double by)
        {
            double abx = bx - ax, aby = by - ay, l2 = abx * abx + aby * aby;
            var t = l2 < 1e-9 ? 0 : Math.Max(0, Math.Min(1, ((px - ax) * abx + (py - ay) * aby) / l2));
            return Math.Sqrt((px - (ax + abx * t)) * (px - (ax + abx * t)) + (py - (ay + aby * t)) * (py - (ay + aby * t)));
        }

        /// <summary>Biên của mảnh region thành các đoạn (đoạn chung với từng mảnh kề, đoạn ngoài tranh có B = -1).</summary>
        public static List<EdgeRun> RunsOf(IReadOnlyList<GenRegion> regions, int region)
        {
            var ring = RingOf(regions[region].Points);
            int n = ring.Count;
            var rings = new List<P>[regions.Count];
            var owners = new Dictionary<long, List<(int r, int i)>>();
            for (var r = 0; r < regions.Count; r++)
            {
                if (r == region) continue;
                rings[r] = RingOf(regions[r].Points);
                for (var i = 0; i < rings[r].Count; i++)
                {
                    var k = Key(rings[r][i]);
                    if (!owners.TryGetValue(k, out var l)) owners[k] = l = new List<(int, int)>();
                    l.Add((r, i));
                }
            }
            var keys = new int[n];
            Array.Fill(keys, -1);
            for (var i = 0; i < n; i++)
            {
                if (!owners.TryGetValue(Key(ring[i]), out var x) || !owners.TryGetValue(Key(ring[(i + 1) % n]), out var y)) continue;
                foreach (var (rr, j) in x)
                {
                    if (keys[i] >= 0 && rr >= keys[i]) continue;
                    var m = rings[rr].Count;
                    foreach (var (yr, yi) in y)
                        if (yr == rr && (yi == (j + 1) % m || yi == (j + m - 1) % m)) { keys[i] = rr; break; }
                }
            }
            var start = -1;
            for (var i = 0; i < n; i++) if (keys[i] != keys[(i + n - 1) % n]) { start = i; break; }
            var runs = new List<EdgeRun>();
            if (start < 0)
            {
                var closed = new List<P>(ring) { ring[0] };
                runs.Add(new EdgeRun { A = region, B = keys[0], Lo = 0, Hi = 0, Pts = closed });
                return runs;
            }
            for (var s = 0; s < n;)
            {
                var kk = keys[(start + s) % n];
                var pts = new List<P> { ring[(start + s) % n] };
                var len = 0;
                while (s + len < n && keys[(start + s + len) % n] == kk)
                {
                    len++;
                    pts.Add(ring[(start + s + len) % n]);
                }
                runs.Add(new EdgeRun { A = region, B = kk, Lo = (start + s) % n, Hi = (start + s + len) % n, Pts = pts });
                s += len;
            }
            return runs;
        }

        /// <summary>Biên vòng ngoài của mảnh có nằm trong radius (ô lưới) quanh điểm (px, py) không.</summary>
        public static bool NearRing(IReadOnlyList<GenRegion> regions, int unit, int region, double px, double py, double radius)
        {
            unit = Math.Max(1, unit);
            var ring = RingOf(regions[region].Points);
            for (var i = 0; i < ring.Count; i++)
            {
                var a = ring[i];
                var b = ring[(i + 1) % ring.Count];
                if (DistSeg(px, py, (double)a.X / unit, (double)a.Y / unit, (double)b.X / unit, (double)b.Y / unit) <= radius) return true;
            }
            return false;
        }

        /// <summary>Đoạn chung với mảnh kề gần điểm nhất trong radius; null nếu không có (biên ngoài tranh không sửa được).</summary>
        public static EdgeRun Nearest(IEnumerable<EdgeRun> runs, int unit, double px, double py, double radius)
        {
            EdgeRun best = null;
            var bestD = radius;
            foreach (var r in runs)
            {
                if (r.B < 0) continue;
                for (var i = 0; i + 1 < r.Pts.Count; i++)
                {
                    var d = DistSeg(px, py, (double)r.Pts[i].X / unit, (double)r.Pts[i].Y / unit, (double)r.Pts[i + 1].X / unit, (double)r.Pts[i + 1].Y / unit);
                    if (d < bestD) { bestD = d; best = r; }
                }
            }
            return best;
        }

        private static V EndTangent(List<V> d, int end, int dir)
        {
            var i = end + dir;
            while (i + dir >= 0 && i + dir < d.Count && V.Dist(d[i], d[end]) < TangentReach) i += dir;
            return V.Norm(d[i] - d[end]);
        }

        /// <summary>Các Bézier (ô lưới) đi qua đoạn với ít đường cong nhất trong dung sai.</summary>
        public static List<Contour.Cubic> Fit(EdgeRun run, int unit)
        {
            var pts = new List<V>();
            foreach (var v in run.Pts)
            {
                var q = new V((double)v.X / unit, (double)v.Y / unit);
                if (pts.Count == 0 || (pts[^1].X - q.X) * (pts[^1].X - q.X) + (pts[^1].Y - q.Y) * (pts[^1].Y - q.Y) > 1e-6) pts.Add(q);
            }
            if (pts.Count < 2) return new List<Contour.Cubic>();
            return Contour.Fit(pts, FitTolerance, EndTangent(pts, 0, 1), EndTangent(pts, pts.Count - 1, -1));
        }

        /// <summary>Lấy mẫu chuỗi Bézier (ô lưới) thành đa tuyến nguyên (nhân unit); hai đầu đúng bằng đầu và cuối đường cong.</summary>
        public static List<P> Sample(IReadOnlyList<Contour.Cubic> curve, int unit, double step)
        {
            var o = new List<P>();
            void Add(V p)
            {
                var q = new P((int)Math.Round(p.X * unit, MidpointRounding.AwayFromZero), (int)Math.Round(p.Y * unit, MidpointRounding.AwayFromZero));
                if (o.Count == 0 || o[^1].X != q.X || o[^1].Y != q.Y) o.Add(q);
            }
            foreach (var c in curve)
            {
                var len = V.Dist(c.P0, c.P3) + V.Dist(c.P0, c.P1) + V.Dist(c.P2, c.P3);
                var n = Math.Max(1, Math.Min(80, (int)Math.Ceiling(len / step)));
                for (var k = 0; k < n; k++) Add(Contour.CubicAt(c, (double)k / n));
            }
            if (curve.Count > 0) Add(curve[^1].P3);
            return o;
        }

        // ------------------------------------------------------------------ thay đoạn trong tranh
        private static int IndexOf(int[] pts, P v)
        {
            for (var i = 0; i < pts.Length / 2; i++) if (pts[i * 2] == v.X && pts[i * 2 + 1] == v.Y) return i;
            return -1;
        }

        private static int[] Replace(int[] pts, int lo, int hi, IEnumerable<P> chain)
        {
            var n = pts.Length / 2;
            var res = new List<int>();
            foreach (var v in chain) { res.Add(v.X); res.Add(v.Y); }
            for (var i = (hi + 1) % n; i != lo; i = (i + 1) % n)
            {
                res.Add(pts[i * 2]);
                res.Add(pts[i * 2 + 1]);
                if ((i + 1) % n == lo) break;
            }
            return res.ToArray();
        }

        private static bool SegCross(P a, P b, P c, P d)
        {
            if ((a.X == c.X && a.Y == c.Y) || (a.X == d.X && a.Y == d.Y) || (b.X == c.X && b.Y == c.Y) || (b.X == d.X && b.Y == d.Y)) return false;
            long Cr(P p, P q, P r) => (long)(q.X - p.X) * (r.Y - p.Y) - (long)(q.Y - p.Y) * (r.X - p.X);
            long d1 = Cr(a, b, c), d2 = Cr(a, b, d), d3 = Cr(c, d, a), d4 = Cr(c, d, b);
            return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
        }

        // Chuỗi mới có cắt biên mảnh khác hoặc chính nó không: trả mảnh bị cắt, -1 = tự cắt, null = ổn
        private static int? CrossesOthers(IReadOnlyList<GenRegion> regions, List<P> chain, List<P> oldRun, int ra, int rb)
        {
            var old = new HashSet<(long, long)>();
            for (var i = 0; i + 1 < oldRun.Count; i++) { old.Add((Key(oldRun[i]), Key(oldRun[i + 1]))); old.Add((Key(oldRun[i + 1]), Key(oldRun[i]))); }
            var bounds = regions.Select(rg =>
            {
                int x0 = int.MaxValue, y0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue;
                for (var q = 0; q + 1 < rg.Points.Length; q += 2)
                {
                    x0 = Math.Min(x0, rg.Points[q]); x1 = Math.Max(x1, rg.Points[q]);
                    y0 = Math.Min(y0, rg.Points[q + 1]); y1 = Math.Max(y1, rg.Points[q + 1]);
                }
                return (x0, y0, x1, y1);
            }).ToList();
            var segs = chain.Count - 1;
            for (var k = 0; k < segs; k++)
            {
                var a = chain[k];
                var b = chain[k + 1];
                int minX = Math.Min(a.X, b.X), maxX = Math.Max(a.X, b.X), minY = Math.Min(a.Y, b.Y), maxY = Math.Max(a.Y, b.Y);
                for (var m = k + 2; m < segs; m++)
                {
                    int mx0 = Math.Min(chain[m].X, chain[m + 1].X), mx1 = Math.Max(chain[m].X, chain[m + 1].X);
                    int my0 = Math.Min(chain[m].Y, chain[m + 1].Y), my1 = Math.Max(chain[m].Y, chain[m + 1].Y);
                    if (maxX < mx0 || minX > mx1 || maxY < my0 || minY > my1) continue;
                    if (SegCross(a, b, chain[m], chain[m + 1])) return -1;
                }
                for (var r = 0; r < regions.Count; r++)
                {
                    var bb = bounds[r];
                    if (maxX < bb.x0 || minX > bb.x1 || maxY < bb.y0 || minY > bb.y1) continue;
                    var pts = regions[r].Points;
                    var n = pts.Length / 2;
                    for (var i = 0; i < n; i++)
                    {
                        var j = (i + 1) % n;
                        int cx = pts[i * 2], cy = pts[i * 2 + 1], dx = pts[j * 2], dy = pts[j * 2 + 1];
                        if (maxX < Math.Min(cx, dx) || minX > Math.Max(cx, dx) || maxY < Math.Min(cy, dy) || minY > Math.Max(cy, dy)) continue;
                        var c = new P(cx, cy);
                        var d = new P(dx, dy);
                        if ((r == ra || r == rb) && old.Contains((Key(c), Key(d)))) continue;
                        if (SegCross(a, b, c, d)) return r;
                    }
                }
            }
            return null;
        }

        // Đoạn nối vào đỉnh v vừa dời có cắt cạnh nào khác không (cạnh thuộc đoạn đang thay được bỏ qua)
        private static bool MovedCross(IReadOnlyList<GenRegion> regions, List<P> moved, HashSet<(long, long)> skip)
        {
            foreach (var v in moved)
                foreach (var rg in regions)
                {
                    var pts = rg.Points;
                    var n = pts.Length / 2;
                    for (var i = 0; i < n; i++)
                    {
                        if (pts[i * 2] != v.X || pts[i * 2 + 1] != v.Y) continue;
                        var prev = new P(pts[((i + n - 1) % n) * 2], pts[((i + n - 1) % n) * 2 + 1]);
                        var next = new P(pts[((i + 1) % n) * 2], pts[((i + 1) % n) * 2 + 1]);
                        foreach (var edge in new[] { (prev, v), (v, next) })
                        {
                            if (skip.Contains((Key(edge.Item1), Key(edge.Item2))) || skip.Contains((Key(edge.Item2), Key(edge.Item1)))) continue;
                            foreach (var other in regions)
                            {
                                var q = other.Points;
                                var m = q.Length / 2;
                                for (var k = 0; k < m; k++)
                                {
                                    var c = new P(q[k * 2], q[k * 2 + 1]);
                                    var d = new P(q[((k + 1) % m) * 2], q[((k + 1) % m) * 2 + 1]);
                                    if (skip.Contains((Key(c), Key(d))) || skip.Contains((Key(d), Key(c)))) continue;
                                    if (SegCross(edge.Item1, edge.Item2, c, d)) return true;
                                }
                            }
                        }
                    }
                }
            return false;
        }

        /// <summary>
        /// Thay đoạn run bằng chain, cập nhật cả mảnh A và mảnh kề B. Hai đầu chain khác đầu cũ nghĩa là điểm giao bị dời: mọi mảnh dùng đỉnh đó đi theo.
        /// Trả danh sách mới, null kèm error.
        /// </summary>
        public static List<GenRegion> ReplaceRun(int width, int height, int unit, IReadOnlyList<GenRegion> regions, EdgeRun run, List<P> chain, out string error)
        {
            error = null;
            if (run.B < 0) { error = "Biên ngoài tranh không sửa được"; return null; }
            if (chain.Count < 2) { error = "Biên quá ngắn"; return null; }
            var oldFirst = run.Pts[0];
            var oldLast = run.Pts[^1];
            var first = chain[0];
            var last = chain[^1];
            if (oldFirst.X == oldLast.X && oldFirst.Y == oldLast.Y) { error = "Đường biên khép kín: dùng Gộp/Tách"; return null; }
            if (first.X == last.X && first.Y == last.Y) { error = "Hai đầu đoạn biên trùng nhau, kéo lại"; return null; }

            var cur = regions.Select(r => new GenRegion { Id = r.Id, Parent = r.Parent, Points = (int[])r.Points.Clone(), ColorId = r.ColorId, Holes = r.Holes }).ToList();
            var oldPts = run.Pts;
            var movedPts = new List<P>();
            var movedFirst = first.X != oldFirst.X || first.Y != oldFirst.Y;
            var movedLast = last.X != oldLast.X || last.Y != oldLast.Y;
            if (movedFirst || movedLast)
            {
                var taken = new HashSet<long>();
                foreach (var rg in cur) for (var i = 0; i < rg.Points.Length / 2; i++) taken.Add(Key(new P(rg.Points[i * 2], rg.Points[i * 2 + 1])));
                if ((movedFirst && taken.Contains(Key(first))) || (movedLast && taken.Contains(Key(last)))) { error = "Điểm giao trùng một đỉnh khác, kéo lại"; return null; }
                var fixedKeys = new HashSet<long>(run.Pts.Select(Key));
                int maxX = width * unit, maxY = height * unit;
                // Điểm giao dời: mọi đỉnh quanh đó trượt theo mượt dần (kéo dẻo) để các biên kề không bị cắt nhau
                void Move(P from, P to)
                {
                    double dx = to.X - from.X, dy = to.Y - from.Y, dist = Math.Sqrt(dx * dx + dy * dy);
                    var reach = Math.Max(WarpCells * unit, WarpRatio * dist);
                    var shifted = new Dictionary<long, P>();
                    foreach (var rg in cur)
                        for (var i = 0; i < rg.Points.Length / 2; i++)
                        {
                            int vx = rg.Points[i * 2], vy = rg.Points[i * 2 + 1];
                            var key = Key(new P(vx, vy));
                            if (shifted.ContainsKey(key) || fixedKeys.Contains(key) || vx <= 0 || vy <= 0 || vx >= maxX || vy >= maxY) continue;
                            var t = Math.Sqrt((vx - from.X) * (double)(vx - from.X) + (vy - from.Y) * (double)(vy - from.Y)) / reach;
                            if (t >= 1) continue;
                            var k = (1 - t) * (1 - t);
                            shifted[key] = new P(vx + (int)Math.Round(dx * k, MidpointRounding.AwayFromZero), vy + (int)Math.Round(dy * k, MidpointRounding.AwayFromZero));
                        }
                    foreach (var rg in cur)
                        for (var i = 0; i < rg.Points.Length / 2; i++)
                        {
                            var key = Key(new P(rg.Points[i * 2], rg.Points[i * 2 + 1]));
                            if (shifted.TryGetValue(key, out var np)) { rg.Points[i * 2] = np.X; rg.Points[i * 2 + 1] = np.Y; }
                            else if (rg.Points[i * 2] == from.X && rg.Points[i * 2 + 1] == from.Y) { rg.Points[i * 2] = to.X; rg.Points[i * 2 + 1] = to.Y; }
                        }
                    movedPts.AddRange(shifted.Values);
                    movedPts.Add(to);
                }
                if (movedFirst) Move(oldFirst, first);
                if (movedLast) Move(oldLast, last);
                oldPts = new List<P>(run.Pts);
                oldPts[0] = first;
                oldPts[^1] = last;
            }

            var cross = CrossesOthers(cur, chain, oldPts, run.A, run.B);
            if (cross != null)
            {
                error = cross == run.A ? "Biên mới cắt qua chính mảnh này (quá sát biên đối diện), kéo lại"
                    : cross >= 0 ? "Biên mới cắt qua biên mảnh #" + (cross + 1) + ", kéo lại"
                    : "Biên mới tự cắt chính nó, kéo lại";
                return null;
            }
            if (movedPts.Count > 0)
            {
                var skip = new HashSet<(long, long)>();
                for (var i = 0; i + 1 < oldPts.Count; i++) { skip.Add((Key(oldPts[i]), Key(oldPts[i + 1]))); }
                if (MovedCross(cur, movedPts, skip)) { error = "Điểm giao dời làm các biên quanh nó cắt nhau, kéo lại"; return null; }
            }

            var pa = cur[run.A].Points;
            var pb = cur[run.B].Points;
            int bl = IndexOf(pb, last), bh = IndexOf(pb, first);
            if (run.Lo < 0 || run.Hi < 0 || run.Lo >= pa.Length / 2 || run.Hi >= pa.Length / 2 || bl < 0 || bh < 0) { error = "Biên không khớp mảnh kề"; return null; }
            cur[run.A].Points = Replace(pa, run.Lo, run.Hi, chain);
            cur[run.B].Points = Replace(pb, bl, bh, Enumerable.Reverse(chain));

            var map = Rasterizer.Rasterize(width, height, unit, cur);
            var minPx = Math.Max(4, (int)Math.Truncate((double)width * height / 10000));
            var count = new int[cur.Count + 1];
            foreach (var id in map.Reg) if (id > 0 && id < count.Length) count[id]++;
            var touched = new HashSet<int> { run.A, run.B };
            if (movedPts.Count > 0)
                for (var r = 0; r < cur.Count; r++)
                    foreach (var v in movedPts)
                        for (var i = 0; i < cur[r].Points.Length / 2; i++)
                            if (cur[r].Points[i * 2] == v.X && cur[r].Points[i * 2 + 1] == v.Y) touched.Add(r);
            foreach (var r in touched) if (count[r + 1] < minPx) { error = "Mảnh bị ép quá nhỏ"; return null; }
            return cur;
        }
    }
}
