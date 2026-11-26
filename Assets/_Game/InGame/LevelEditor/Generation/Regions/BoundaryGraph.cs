using System;
using System.Collections.Generic;
using System.Linq;

namespace Falcon.InGame.LevelEditor.Gen
{
    /// <summary>Đồ thị biên chung: mỗi biên giữa hai vùng được làm mượt đúng một lần nên hai láng giềng dùng cùng điểm (port từ boundary.js).</summary>
    public static class BoundaryGraph
    {
        public sealed class Result
        {
            public int[][] Polys; // theo id, null nếu không có
            public List<int[]>[] Holes;
        }

        private sealed class Arc
        {
            public int Id, StartNode, EndNode, Left, Right;
            public bool Closed, Raw;
            public List<P> Grid;
            public List<V> Sm;
            public List<P> Smooth;
        }

        private struct Ref
        {
            public Arc Arc;
            public bool Rev;
        }

        private const int R = 1, U = 2, L = 4, D = 8;
        private static readonly int[] DX = { 1, 0, -1, 0 };
        private static readonly int[] DY = { 0, 1, 0, -1 };
        private const double SpikeRatio = 0.5;
        private const int SpikeArmSteps = 3, MinHoleArea = 100, MaxArcDeviation = 15, MaxNodeShift = 20;
        private const double RatioLow = 0.8, RatioHigh = 1.25, RatioLowSmall = 0.35, RatioHighSmall = 2.0, SmallFraction = 0.004, ContinueDeg = 30, BlendSpan = 24;

        private static int Bits(int m) => (m & 1) + ((m >> 1) & 1) + ((m >> 2) & 1) + ((m >> 3) & 1);

        private static int Key(P p) => p.X * 20011 + p.Y;

        private static double SignedArea(IList<P> p)
        {
            double s = 0;
            for (int i = 0, j = p.Count - 1; i < p.Count; j = i++) s += (double)p[j].X * p[i].Y - (double)p[i].X * p[j].Y;
            return s * 0.5;
        }

        private static double RingArea(int[] flat)
        {
            double a = 0;
            var n = flat.Length / 2;
            for (int i = 0, j = n - 1; i < n; j = i++) a += (double)flat[j * 2] * flat[i * 2 + 1] - (double)flat[i * 2] * flat[j * 2 + 1];
            return a * 0.5;
        }

        private static int[] Flatten(IList<P> poly)
        {
            var f = new int[poly.Count * 2];
            for (var i = 0; i < poly.Count; i++) { f[i * 2] = poly[i].X; f[i * 2 + 1] = poly[i].Y; }
            return f;
        }

        private static List<P> Dedup(IList<P> pts)
        {
            var list = new List<P>();
            for (var i = 0; i < pts.Count; i++) if (list.Count == 0 || !list[list.Count - 1].Equals(pts[i])) list.Add(pts[i]);
            while (list.Count > 1 && list[0].Equals(list[list.Count - 1])) list.RemoveAt(list.Count - 1);
            return list;
        }

        private static bool Inside(IList<P> poly, P p)
        {
            var ins = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                if ((poly[i].Y > p.Y) == (poly[j].Y > p.Y)) continue;
                var x = poly[i].X + (double)(p.Y - poly[i].Y) * (poly[j].X - poly[i].X) / (poly[j].Y - poly[i].Y);
                if (p.X < x) ins = !ins;
            }
            return ins;
        }

        private static bool MostlyInside(IList<P> poly, IList<P> hole)
        {
            var step = Math.Max(1, hole.Count / 24);
            int ins = 0, total = 0;
            for (var i = 0; i < hole.Count; i += step) { total++; if (Inside(poly, hole[i])) ins++; }
            return ins * 2 > total;
        }

        private static long Orient(P p, P q, P r) => (long)(q.X - p.X) * (r.Y - p.Y) - (long)(q.Y - p.Y) * (r.X - p.X);

        private static bool ProperCross(P a, P b, P c, P d)
        {
            long d1 = Orient(a, b, c), d2 = Orient(a, b, d), d3 = Orient(c, d, a), d4 = Orient(c, d, b);
            return d1 != 0 && d2 != 0 && d3 != 0 && d4 != 0 && (d1 > 0) != (d2 > 0) && (d3 > 0) != (d4 > 0);
        }

        private static P Intersect(P a, P b, P c, P d)
        {
            double x1 = a.X, y1 = a.Y, x2 = b.X, y2 = b.Y, x3 = c.X, y3 = c.Y, x4 = d.X, y4 = d.Y;
            var den = (x1 - x2) * (y3 - y4) - (y1 - y2) * (x3 - x4);
            if (Math.Abs(den) < 1e-9) return b;
            var t = ((x1 - x3) * (y3 - y4) - (y1 - y3) * (x3 - x4)) / den;
            return new P((int)Ops.JsRound(x1 + t * (x2 - x1)), (int)Ops.JsRound(y1 + t * (y2 - y1)));
        }

        // cắt các vòng nhỏ (đường tự cắt, mảnh nơ): hai đoạn không kề nhau cắt nhau → bỏ các đỉnh giữa, nối tại giao điểm
        private static void RemoveLoops(List<P> pts, bool closed)
        {
            for (var pass = 0; pass < 200; pass++)
            {
                var n = pts.Count;
                var found = false;
                var segs = closed ? n : n - 1;
                for (var i = 0; i < segs && !found; i++)
                    for (var j = i + 2; j < segs; j++)
                    {
                        if (closed && i == 0 && j == n - 1) continue;
                        P a = pts[i], b = pts[(i + 1) % n], c = pts[j], d = pts[(j + 1) % n];
                        if (!ProperCross(a, b, c, d)) continue;
                        var hit = Intersect(a, b, c, d);
                        if (closed && j - i > n / 2.0)
                        {
                            var keep = new List<P>();
                            for (var k = i + 1; k <= j; k++) keep.Add(pts[k]);
                            keep.Add(hit);
                            pts.Clear();
                            pts.AddRange(keep);
                        }
                        else
                        {
                            pts.RemoveRange(i + 1, j - i);
                            pts.Insert(i + 1, hit);
                        }
                        found = true;
                        break;
                    }
                if (!found) return;
            }
        }

        private static List<P> ScalePts(IList<V> pts, int unit, bool closed, bool doRemoveLoops = true)
        {
            var list = new List<P>();
            for (var i = 0; i < pts.Count; i++)
            {
                var q = new P((int)Ops.JsRound(pts[i].X * unit), (int)Ops.JsRound(pts[i].Y * unit));
                if (list.Count == 0 || !list[list.Count - 1].Equals(q)) list.Add(q);
            }
            if (closed && list.Count > 1 && list[0].Equals(list[list.Count - 1])) list.RemoveAt(list.Count - 1);
            if (doRemoveLoops) RemoveLoops(list, closed);
            return list;
        }

        // gai nhọn (biên thắt nút): đỉnh quay ngược, hai láng giềng sát nhau, bị bỏ tới khi hết; đầu hở giữ nguyên
        private static List<P> RemoveSpikes(List<P> pts, bool closed, double maxArm)
        {
            var list = new List<P>(pts);
            var changed = true;
            for (var pass = 0; changed && pass < 50; pass++)
            {
                changed = false;
                for (var i = 0; i < list.Count && list.Count > (closed ? 3 : 2);)
                {
                    if (!closed && (i == 0 || i == list.Count - 1)) { i++; continue; }
                    P a = list[(i + list.Count - 1) % list.Count], b = list[i], c = list[(i + 1) % list.Count];
                    var arm = Math.Min(V.Dist(a.ToV(), b.ToV()), V.Dist(c.ToV(), b.ToV()));
                    if (arm <= maxArm && V.Dist(a.ToV(), c.ToV()) < SpikeRatio * arm) { list.RemoveAt(i); changed = true; } else i++;
                }
            }
            return list;
        }

        private static void RemoveRepeatLoops(List<P> ring)
        {
            for (var pass = 0; pass < 500; pass++)
            {
                var total = Math.Abs(SignedArea(ring));
                var seen = new Dictionary<int, int>();
                var cut = false;
                for (var j = 0; j < ring.Count && !cut; j++)
                {
                    var k = Key(ring[j]);
                    if (!seen.TryGetValue(k, out var i)) { seen[k] = j; continue; }
                    if (Math.Abs(SignedArea(ring.GetRange(i, j - i))) < 0.1 * total) { ring.RemoveRange(i + 1, j - i); cut = true; } else seen[k] = j;
                }
                if (!cut) return;
            }
        }

        private static void TrimLoops(List<P> ring)
        {
            var before = Math.Abs(SignedArea(ring));
            var copy = new List<P>(ring);
            RemoveLoops(copy, true);
            if (copy.Count < 3 || Math.Abs(SignedArea(copy)) < 0.9 * before) return;
            ring.Clear();
            ring.AddRange(copy);
        }

        // chèn extra vào poly bằng cầu rộng 0 tại hai đỉnh gần nhất
        private static void MergeLoop(List<P> poly, List<P> extra)
        {
            if (extra.Count < 3) return;
            int bi = 0, bj = 0;
            var bd = double.PositiveInfinity;
            for (var i = 0; i < poly.Count; i++)
                for (var j = 0; j < extra.Count; j++)
                {
                    double dx = poly[i].X - extra[j].X, dy = poly[i].Y - extra[j].Y, d = dx * dx + dy * dy;
                    if (d < bd) { bd = d; bi = i; bj = j; }
                }
            var merged = new List<P>();
            for (var i = 0; i <= bi; i++) merged.Add(poly[i]);
            for (var k = 0; k <= extra.Count; k++) merged.Add(extra[(bj + k) % extra.Count]);
            for (var i = bi; i < poly.Count; i++) merged.Add(poly[i]);
            poly.Clear();
            poly.AddRange(merged);
        }

        private static V EndDir(Arc a, bool atStart)
        {
            var g = a.Grid;
            var k = Math.Min(8, g.Count - 1);
            return atStart ? new V(g[k].X - g[0].X, g[k].Y - g[0].Y) : new V(g[g.Count - 1 - k].X - g[g.Count - 1].X, g[g.Count - 1 - k].Y - g[g.Count - 1].Y);
        }

        private static V FirstDir(Arc a, bool rev)
        {
            var g = a.Grid;
            return rev ? new V(g[g.Count - 2].X - g[g.Count - 1].X, g[g.Count - 2].Y - g[g.Count - 1].Y) : new V(g[1].X - g[0].X, g[1].Y - g[0].Y);
        }

        private static V LastDir(Arc a, bool rev)
        {
            var g = a.Grid;
            return rev ? new V(g[0].X - g[1].X, g[0].Y - g[1].Y) : new V(g[g.Count - 1].X - g[g.Count - 2].X, g[g.Count - 1].Y - g[g.Count - 2].Y);
        }

        // nút chữ T đúng 3 đường: nối 2 đường thẳng hàng nhất (trong ContinueDeg) thành một chuỗi để làm mượt một lần
        private static List<List<Ref>> Chains(List<Arc> arcs, double minLen)
        {
            var atNode = new Dictionary<int, List<(Arc arc, bool start)>>();
            foreach (var a in arcs)
            {
                if (a.Closed || a.Left == 0 || a.Right == 0 || (minLen > 0 && a.Grid.Count < minLen)) continue; // biên rất ngắn là một phần của nút, không phải đường chạy tiếp
                if (!atNode.TryGetValue(a.StartNode, out var ls)) atNode[a.StartNode] = ls = new();
                ls.Add((a, true));
                if (!atNode.TryGetValue(a.EndNode, out var le)) atNode[a.EndNode] = le = new();
                le.Add((a, false));
            }
            var join = new Dictionary<int, (Arc arc, bool start)>();
            static int Jk(Arc arc, bool start) => arc.Id * 2 + (start ? 1 : 0);
            foreach (var list in atNode.Values)
            {
                if (list.Count != 3 || list[0].arc == list[1].arc || list[0].arc == list[2].arc || list[1].arc == list[2].arc) continue;
                var bestAngle = 180 - ContinueDeg;
                int bi = -1, bj = -1;
                for (var i = 0; i < 3; i++)
                    for (var j = i + 1; j < 3; j++)
                    {
                        var ang = V.Angle(EndDir(list[i].arc, list[i].start), EndDir(list[j].arc, list[j].start));
                        if (ang < bestAngle) continue;
                        bestAngle = ang;
                        bi = i;
                        bj = j;
                    }
                if (bi < 0) continue;
                join[Jk(list[bi].arc, list[bi].start)] = list[bj];
                join[Jk(list[bj].arc, list[bj].start)] = list[bi];
            }
            var output = new List<List<Ref>>();
            var visited = new HashSet<Arc>();
            void Follow(Arc first, bool rev)
            {
                var chain = new List<Ref> { new() { Arc = first, Rev = rev } };
                visited.Add(first);
                var cur = new Ref { Arc = first, Rev = rev };
                for (; ; )
                {
                    if (!join.TryGetValue(Jk(cur.Arc, cur.Rev), out var nxt) || visited.Contains(nxt.arc)) break;
                    cur = new Ref { Arc = nxt.arc, Rev = !nxt.start };
                    visited.Add(cur.Arc);
                    chain.Add(cur);
                }
                output.Add(chain);
            }
            foreach (var a in arcs)
            {
                if (visited.Contains(a)) continue;
                if (!join.ContainsKey(Jk(a, true))) Follow(a, false);
                else if (!join.ContainsKey(Jk(a, false))) Follow(a, true);
            }
            foreach (var a in arcs) if (!visited.Contains(a)) Follow(a, false);
            return output;
        }

        private static void SmoothChain(List<Ref> chain, double sigma, Contour.Options opt, Dictionary<Arc, List<V>> smoothed, Dictionary<int, V> nodePos)
        {
            var pts = new List<P>();
            var nodeIdx = new List<int>();
            for (var i = 0; i < chain.Count; i++)
            {
                var g = chain[i].Arc.Grid;
                for (var k = 0; k < g.Count; k++)
                {
                    if (i > 0 && k == 0) continue;
                    pts.Add(g[chain[i].Rev ? g.Count - 1 - k : k]);
                }
                if (i + 1 < chain.Count) nodeIdx.Add(pts.Count - 1);
            }
            var sm = Contour.SmoothArc(pts, sigma, opt);
            var cuts = new List<int> { 0 };
            foreach (var ni in nodeIdx)
            {
                var target = pts[ni];
                var best = -1;
                var bestD = double.PositiveInfinity;
                for (var j = cuts[cuts.Count - 1] + 1; j < sm.Count - 1; j++)
                {
                    double dx = sm[j].X - target.X, dy = sm[j].Y - target.Y, d = dx * dx + dy * dy;
                    if (d < bestD) { bestD = d; best = j; }
                }
                if (best < 0) return;
                cuts.Add(best);
            }
            cuts.Add(sm.Count - 1);
            for (var i = 0; i < chain.Count; i++)
            {
                var piece = sm.GetRange(cuts[i], cuts[i + 1] + 1 - cuts[i]);
                if (chain[i].Rev) piece.Reverse();
                smoothed[chain[i].Arc] = piece;
                if (i + 1 < chain.Count) nodePos[chain[i].Rev ? chain[i].Arc.StartNode : chain[i].Arc.EndNode] = sm[cuts[i + 1]];
            }
        }

        // dời một đầu đường theo delta, giảm dần về 0 dọc BlendSpan đầu tiên
        private static void Blend(List<V> sm, V delta, bool atStart)
        {
            if (delta.X * delta.X + delta.Y * delta.Y < 1e-8 || sm.Count < 2) return;
            double total = 0;
            for (var i = 1; i < sm.Count; i++) total += V.Dist(sm[i], sm[i - 1]);
            var span = Math.Min(BlendSpan, total * 0.5);
            double s = 0;
            for (var k = 0; k < sm.Count; k++)
            {
                var i = atStart ? k : sm.Count - 1 - k;
                if (k > 0) s += V.Dist(sm[i], sm[atStart ? i - 1 : i + 1]);
                if (s >= span) break;
                var t = 1 - s / span;
                var e = t * t * (3 - 2 * t);
                sm[i] = new V(sm[i].X + delta.X * e, sm[i].Y + delta.Y * e);
            }
        }

        private static bool NearRaw(List<V> sm, List<P> raw, double tol)
        {
            if (tol <= 0) tol = MaxArcDeviation;
            double sx0 = double.PositiveInfinity, sy0 = double.PositiveInfinity, sx1 = double.NegativeInfinity, sy1 = double.NegativeInfinity;
            double rx0 = double.PositiveInfinity, ry0 = double.PositiveInfinity, rx1 = double.NegativeInfinity, ry1 = double.NegativeInfinity;
            foreach (var p in sm) { sx0 = Math.Min(sx0, p.X); sx1 = Math.Max(sx1, p.X); sy0 = Math.Min(sy0, p.Y); sy1 = Math.Max(sy1, p.Y); }
            foreach (var p in raw) { rx0 = Math.Min(rx0, p.X); rx1 = Math.Max(rx1, p.X); ry0 = Math.Min(ry0, p.Y); ry1 = Math.Max(ry1, p.Y); }
            return Math.Abs(sx0 - rx0) <= tol && Math.Abs(sx1 - rx1) <= tol && Math.Abs(sy0 - ry0) <= tol && Math.Abs(sy1 - ry1) <= tol;
        }

        private static List<P> RawLoop(List<Ref> list, int unit)
        {
            var pts = new List<P>();
            foreach (var e in list)
            {
                var g = e.Arc.Grid;
                if (e.Rev) for (var i = g.Count - 1; i >= (e.Arc.Closed ? 0 : 1); i--) pts.Add(new P(g[i].X * unit, g[i].Y * unit));
                else for (var i = 0; i < (e.Arc.Closed ? g.Count : g.Count - 1); i++) pts.Add(new P(g[i].X * unit, g[i].Y * unit));
            }
            var d = Dedup(pts);
            return d.Count >= 3 ? d : null;
        }

        private static void Append(List<P> dst, Arc a, bool rev)
        {
            var s = a.Smooth;
            if (a.Closed)
            {
                if (rev) for (var i = s.Count - 1; i >= 0; i--) dst.Add(s[i]); else for (var i = 0; i < s.Count; i++) dst.Add(s[i]);
                return;
            }
            if (rev) for (var i = s.Count - 1; i > 0; i--) dst.Add(s[i]); else for (var i = 0; i < s.Count - 1; i++) dst.Add(s[i]);
        }

        // tại một nút lấy đường đi ra chưa dùng nằm bên trái nhất (hoặc quay về đường xuất phát)
        private static Ref? NextArc(List<Ref> list, HashSet<int> used, int node, Ref cur, Ref start)
        {
            var inDir = LastDir(cur.Arc, cur.Rev);
            Ref? best = null;
            var bestAngle = double.NegativeInfinity;
            foreach (var c in list)
            {
                var from = c.Rev ? c.Arc.EndNode : c.Arc.StartNode;
                if (c.Arc.Closed || from != node) continue;
                if (used.Contains(c.Arc.Id * 2 + (c.Rev ? 1 : 0)) && !(c.Arc == start.Arc && c.Rev == start.Rev)) continue;
                var ang = V.SignedAngle(inDir, FirstDir(c.Arc, c.Rev));
                if (ang > bestAngle) { bestAngle = ang; best = c; }
            }
            return best;
        }

        // ---- nút giao. Gần nút giao biên truy vết bị lệch (nét đen ở đó chung của ba mảnh), nên trong khoảng một bề rộng nét quanh nó không tin truy vết:
        // mọi biên kết thúc ở đó được đo xa hơn (đường qua đoạn đáng tin), nút đặt nơi các đường gặp nhau, mỗi biên rời nút theo đường thẳng tới đầu đoạn đáng tin.
        private sealed class Line
        {
            public V C, U;
            public int N;
        }

        private static double PathLength(IList<V> pts)
        {
            double s = 0;
            for (var i = 1; i < pts.Count; i++) s += V.Dist(pts[i], pts[i - 1]);
            return s;
        }

        private static (V p, int i) PointAt(IList<V> pts, double d)
        {
            double s = 0;
            for (var i = 1; i < pts.Count; i++)
            {
                var l = V.Dist(pts[i], pts[i - 1]);
                if (s + l >= d)
                {
                    var t = l > 0 ? (d - s) / l : 0;
                    return (new V(pts[i - 1].X + (pts[i].X - pts[i - 1].X) * t, pts[i - 1].Y + (pts[i].Y - pts[i - 1].Y) * t), i);
                }
                s += l;
            }
            return (pts[pts.Count - 1], pts.Count - 1);
        }

        // đường bình phương tối thiểu qua đoạn polyline giữa hai khoảng cách tính từ đầu (lấy mẫu mỗi đơn vị)
        private static Line LineThrough(IList<V> pts, double from, double to)
        {
            var len = PathLength(pts);
            var take = new List<V>();
            var top = Math.Min(to, len);
            for (var d = from; d <= top; d += 1) take.Add(PointAt(pts, d).p);
            if (take.Count < 2) return null;
            double mx = 0, my = 0;
            foreach (var p in take) { mx += p.X; my += p.Y; }
            mx /= take.Count;
            my /= take.Count;
            double sxx = 0, sxy = 0, syy = 0;
            foreach (var p in take) { var dx = p.X - mx; var dy = p.Y - my; sxx += dx * dx; sxy += dx * dy; syy += dy * dy; }
            var ang = 0.5 * Math.Atan2(2 * sxy, sxx - syy);
            var u = new V(Math.Cos(ang), Math.Sin(ang));
            V last = take[take.Count - 1], first = take[0];
            if (u.X * (last.X - first.X) + u.Y * (last.Y - first.Y) < 0) u = new V(-u.X, -u.Y);
            return new Line { C = new V(mx, my), U = u, N = take.Count };
        }

        private sealed class Ray
        {
            public Line Line;
            public double W;
        }

        private static void FixJunctions(List<Arc> arcs, int vw, double inkW)
        {
            var rr = Math.Max(4, Math.Min(28, inkW * 0.5 + 2));
            var parent = new Dictionary<int, int>();
            int Find(int n)
            {
                while (parent.TryGetValue(n, out var pn) && pn != n) n = pn;
                return n;
            }
            V At(int n) => new(n % vw, n / vw);
            var usable = arcs.Where(a => !a.Closed && !a.Raw && a.Left != 0 && a.Right != 0 && a.Sm.Count >= 2).ToList();
            // hai nút nối bởi biên ngắn hơn khoảng một bề rộng nét là một nút (bốn mảnh gặp nhau ở một điểm ra hai nút 3 hướng trên lưới pixel)
            var members = new Dictionary<int, List<int>>();
            void Add(int n)
            {
                if (!parent.ContainsKey(n)) { parent[n] = n; members[n] = new List<int> { n }; }
            }
            foreach (var a in usable)
            {
                Add(a.StartNode);
                Add(a.EndNode);
                int ra = Find(a.StartNode), rb = Find(a.EndNode);
                if (ra == rb || PathLength(a.Sm) >= 1.5 * inkW) continue;
                var all = members[ra].Concat(members[rb]).ToList();
                var ok = all.Count <= 4;
                for (var i = 0; ok && i < all.Count; i++)
                    for (var j = i + 1; ok && j < all.Count; j++)
                        if (V.Dist(At(all[i]), At(all[j])) > 2.5 * inkW) ok = false;
                if (!ok) continue;
                parent[rb] = ra;
                members[ra] = all;
                members.Remove(rb);
            }
            var nodes = new Dictionary<int, List<Ray>>();
            var internalArcs = new HashSet<Arc>();
            foreach (var a in usable)
            {
                int ra = Find(a.StartNode), rb = Find(a.EndNode);
                if (ra == rb) { internalArcs.Add(a); continue; } // trong một nút: co lại thành một điểm
                foreach (var start in new[] { true, false })
                {
                    var pts = start ? a.Sm : Enumerable.Reverse(a.Sm).ToList();
                    var len = PathLength(pts);
                    var reach = Math.Min(Math.Max(2 * rr, 8), len * 0.5);
                    var ray = len > 2 * rr + 2 ? LineThrough(pts, rr, rr + reach) : LineThrough(pts, 0, len);
                    var root = start ? ra : rb;
                    if (!nodes.TryGetValue(root, out var list)) nodes[root] = list = new List<Ray>();
                    list.Add(new Ray { Line = ray, W = ray != null ? (len > 2 * rr + 2 ? 1 : 0.3) : 0 });
                }
            }
            // nút nằm trên khung tranh thì giữ nguyên
            foreach (var a in arcs) if (!a.Closed && (a.Left == 0 || a.Right == 0)) { nodes.Remove(Find(a.StartNode)); nodes.Remove(Find(a.EndNode)); }
            var where = new Dictionary<int, V>();
            foreach (var kv in nodes)
            {
                var root = kv.Key;
                var list = kv.Value;
                var cluster = members.TryGetValue(root, out var mem) ? mem : new List<int> { root };
                double cx = 0, cy = 0, s00 = 0, s01 = 0, s11 = 0, b0 = 0, b1 = 0;
                var used = 0;
                if (list.Count < 3) continue;
                foreach (var n in cluster) { cx += At(n).X / cluster.Count; cy += At(n).Y / cluster.Count; }
                foreach (var e in list)
                {
                    if (e.Line == null) continue;
                    V u = e.Line.U, c = e.Line.C;
                    var w = e.W;
                    double m00 = 1 - u.X * u.X, m01 = -u.X * u.Y, m11 = 1 - u.Y * u.Y; // I - u u^T: bình phương khoảng cách tới đường
                    s00 += w * m00; s01 += w * m01; s11 += w * m11;
                    b0 += w * (m00 * c.X + m01 * c.Y);
                    b1 += w * (m01 * c.X + m11 * c.Y);
                    used++;
                }
                var det = s00 * s11 - s01 * s01;
                if (used < 2 || Math.Abs(det) < 0.05) continue; // các đường không xác định một điểm (song song)
                var x = new V((s11 * b0 - s01 * b1) / det, (s00 * b1 - s01 * b0) / det);
                if (V.Dist(x, new V(cx, cy)) > Math.Max(MaxNodeShift, 2 * rr)) continue;
                where[root] = x;
            }
            foreach (var a in usable)
            {
                var hasS = where.TryGetValue(Find(a.StartNode), out var xs);
                var hasE = where.TryGetValue(Find(a.EndNode), out var xe);
                if (internalArcs.Contains(a)) { if (hasS) a.Sm = new List<V> { xs, xs }; continue; }
                if (!hasS && !hasE) continue;
                var pts = a.Sm;
                var len = PathLength(pts);
                var head = hasS ? xs : pts[0];
                var tail = hasE ? xe : pts[pts.Count - 1];
                if (len <= 2 * rr + 2) { a.Sm = new List<V> { head, tail }; continue; }
                (V p, int i) from = hasS ? PointAt(pts, rr) : default;
                (V p, int i) to = hasE ? PointAt(pts, len - rr) : default;
                var output = new List<V> { head };
                int i0 = hasS ? from.i : 1, i1 = hasE ? to.i - 1 : pts.Count - 2;
                if (hasS) output.Add(from.p);
                for (var i = i0; i <= i1; i++) output.Add(pts[i]);
                if (hasE) output.Add(to.p);
                output.Add(tail);
                a.Sm = output;
            }
        }

        /// <summary>map: bản đồ vùng. Vòng ngoài ngược chiều kim đồng hồ, lỗ cùng chiều kim đồng hồ.</summary>
        public static Result Build(RegionMap map, double sigma, int unit, double resampleStep, double fitTolerance, bool flow = false)
        {
            int w = map.W, h = map.H, vw = w + 1;
            var mask = new byte[vw * (h + 1)];
            int Lab(int px, int py) => px < 0 || py < 0 || px >= w || py >= h ? 0 : map.Reg[py * w + px];
            for (var y = 0; y <= h; y++)
                for (var x = 0; x < w; x++)
                    if (Lab(x, y) != Lab(x, y - 1)) { mask[y * vw + x] |= R; mask[y * vw + x + 1] |= L; }
            for (var y = 0; y < h; y++)
                for (var x = 0; x <= w; x++)
                    if (Lab(x, y) != Lab(x - 1, y)) { mask[y * vw + x] |= U; mask[(y + 1) * vw + x] |= D; }

            var arcs = new List<Arc>();
            var visited = new byte[mask.Length];
            bool IsNode(int vi) => Bits(mask[vi]) >= 3;
            (int, int) Sides(int px, int py, int dd) => dd switch
            {
                0 => (Lab(px, py), Lab(px, py - 1)),
                1 => (Lab(px - 1, py), Lab(px, py)),
                2 => (Lab(px - 1, py - 1), Lab(px - 1, py)),
                _ => (Lab(px, py - 1), Lab(px - 1, py - 1)),
            };
            Arc Walk(int start, int d0, bool loop)
            {
                var pts = new List<P> { new(start % vw, start / vw) };
                int cur = start, dd = d0;
                var a = new Arc { Id = arcs.Count, StartNode = start, Closed = loop };
                var sd = Sides(start % vw, start / vw, d0);
                a.Left = sd.Item1;
                a.Right = sd.Item2;
                for (; ; )
                {
                    visited[cur] |= (byte)(1 << dd);
                    int nx = cur % vw + DX[dd], ny = cur / vw + DY[dd], next = ny * vw + nx;
                    visited[next] |= (byte)(1 << ((dd + 2) & 3));
                    cur = next;
                    if (loop && cur == start) break;
                    pts.Add(new P(nx, ny));
                    if (!loop && IsNode(cur)) break;
                    var rest = mask[cur] & ~(1 << ((dd + 2) & 3));
                    dd = rest == R ? 0 : rest == U ? 1 : rest == L ? 2 : 3;
                }
                a.Grid = pts;
                a.EndNode = cur;
                return a;
            }
            for (var v = 0; v < mask.Length; v++)
            {
                if (!IsNode(v)) continue;
                for (var d = 0; d < 4; d++) if ((mask[v] & (1 << d)) != 0 && (visited[v] & (1 << d)) == 0) arcs.Add(Walk(v, d, false));
            }
            for (var v = 0; v < mask.Length; v++)
            {
                if (Bits(mask[v]) != 2) continue;
                for (var d = 0; d < 4; d++) if ((mask[v] & (1 << d)) != 0 && (visited[v] & (1 << d)) == 0) { arcs.Add(Walk(v, d, true)); break; }
            }

            var opt = new Contour.Options { Step = resampleStep, FitTol = fitTolerance, Flow = flow };
            var nodePos = new Dictionary<int, V>();
            var smoothed = new Dictionary<Arc, List<V>>();
            foreach (var chain in Chains(arcs, map.InkW > 0 ? 1.5 * map.InkW : 0)) if (chain.Count > 1) SmoothChain(chain, sigma, opt, smoothed, nodePos);
            foreach (var a in arcs)
            {
                if (smoothed.ContainsKey(a)) continue;
                smoothed[a] = a.Closed ? Contour.SmoothLoop(a.Grid, sigma, opt) : Contour.SmoothArc(a.Grid, sigma, opt);
            }
            foreach (var a in arcs)
            {
                var sm = smoothed[a];
                if (!a.Closed)
                {
                    if (nodePos.TryGetValue(a.StartNode, out var ps) && V.Dist(ps, sm[0]) < MaxNodeShift) Blend(sm, new V(ps.X - sm[0].X, ps.Y - sm[0].Y), true);
                    if (nodePos.TryGetValue(a.EndNode, out var pe) && V.Dist(pe, sm[sm.Count - 1]) < MaxNodeShift) Blend(sm, new V(pe.X - sm[sm.Count - 1].X, pe.Y - sm[sm.Count - 1].Y), false);
                }
                a.Raw = !NearRaw(sm, a.Grid, map.InkW > 0 ? Math.Max(MaxArcDeviation, 2.5 * map.InkW) : 0); // bước nút giao ghim lại đầu của tranh liền nét nên chúng có thể dời xa hơn
                a.Sm = a.Raw ? a.Grid.Select(p => p.ToV()).ToList() : sm;
            }
            if (map.InkW > 0) FixJunctions(arcs, vw, map.InkW); // tranh các mảnh chạm nhau: đặt mọi nút giao nơi các biên gặp nhau
            foreach (var a in arcs) a.Smooth = RemoveSpikes(ScalePts(a.Sm, unit, a.Closed), a.Closed, resampleStep * unit * SpikeArmSteps);

            var count = map.Count;
            var outgoing = new List<Ref>[count + 1];
            for (var i = 0; i <= count; i++) outgoing[i] = new List<Ref>();
            foreach (var a in arcs)
            {
                if (a.Left > 0 && a.Left <= count) outgoing[a.Left].Add(new Ref { Arc = a, Rev = false });
                if (a.Right > 0 && a.Right <= count) outgoing[a.Right].Add(new Ref { Arc = a, Rev = true });
            }

            Result Assemble()
            {
                var polys = new int[count + 1][];
                var holeSets = new List<int[]>[count + 1];
                for (var id = 1; id <= count; id++)
                {
                    var list = outgoing[id];
                    var used = new HashSet<int>();
                    var loops = new List<(List<P> pts, double area)>();
                    foreach (var start in list)
                    {
                        if (used.Contains(start.Arc.Id * 2 + (start.Rev ? 1 : 0))) continue;
                        var pts = new List<P>();
                        var cur = start;
                        var guard = list.Count + 2;
                        while (guard-- > 0)
                        {
                            used.Add(cur.Arc.Id * 2 + (cur.Rev ? 1 : 0));
                            Append(pts, cur.Arc, cur.Rev);
                            if (cur.Arc.Closed) break;
                            var endNode = cur.Rev ? cur.Arc.StartNode : cur.Arc.EndNode;
                            var next = NextArc(list, used, endNode, cur, start);
                            if (next == null || (next.Value.Arc == start.Arc && next.Value.Rev == start.Rev)) break;
                            cur = next.Value;
                        }
                        loops.Add((pts, SignedArea(pts)));
                    }
                    if (loops.Count == 0) continue;
                    var big = loops[0];
                    foreach (var l in loops) if (Math.Abs(l.area) > Math.Abs(big.area)) big = l;
                    var sign = big.area >= 0 ? 1 : -1;
                    List<P> Orient2(List<P> pts) => sign > 0 ? pts : Enumerable.Reverse(pts).ToList();
                    var poly = Dedup(Orient2(big.pts));
                    foreach (var l in loops.Where(l => !ReferenceEquals(l.pts, big.pts) && l.area * sign > 0 && Math.Abs(l.area) >= MinHoleArea * unit * unit).OrderByDescending(l => Math.Abs(l.area)))
                        MergeLoop(poly, Dedup(Orient2(l.pts)));
                    var holeLoops = loops.Where(l => l.area * sign < 0).Select(l => sign > 0 ? l.pts : Enumerable.Reverse(l.pts).ToList()).ToList();
                    if (poly.Count < 3 || Math.Abs(SignedArea(poly)) < 2) poly = RawLoop(list, unit) ?? poly;
                    if (poly.Count < 3 || Math.Abs(SignedArea(poly)) < 2) continue;
                    RemoveRepeatLoops(poly);
                    TrimLoops(poly);
                    polys[id] = Flatten(poly);
                    foreach (var hp0 in holeLoops)
                    {
                        var hp = Dedup(hp0);
                        RemoveRepeatLoops(hp);
                        TrimLoops(hp);
                        if (hp.Count < 3 || -SignedArea(hp) < MinHoleArea * unit * unit || !MostlyInside(poly, hp)) continue;
                        (holeSets[id] ??= new List<int[]>()).Add(Flatten(hp));
                    }
                }
                return new Result { Polys = polys, Holes = holeSets };
            }

            var res = Assemble();
            var pixels = new int[count + 1];
            var bad = new HashSet<int>();
            for (var i = 0; i < map.Reg.Length; i++) if (map.Reg[i] > 0) pixels[map.Reg[i]]++;
            for (var id2 = 1; id2 <= count; id2++)
            {
                if (pixels[id2] == 0) continue;
                var expected = (double)pixels[id2] * unit * unit;
                var got = res.Polys[id2] != null ? Math.Abs(RingArea(res.Polys[id2])) : 0;
                if (res.Holes[id2] != null) foreach (var hl in res.Holes[id2]) got -= Math.Abs(RingArea(hl));
                var small = pixels[id2] < SmallFraction * map.W * map.H;
                if (got < (small ? RatioLowSmall : RatioLow) * expected || got > (small ? RatioHighSmall : RatioHigh) * expected) bad.Add(id2);
            }
            if (bad.Count > 0)
            {
                foreach (var a in arcs)
                    if (bad.Contains(a.Left) || bad.Contains(a.Right))
                        a.Smooth = ScalePts(a.Grid.Select(p => p.ToV()).ToList(), unit, a.Closed, false);
                res = Assemble();
            }
            return res;
        }
    }
}
