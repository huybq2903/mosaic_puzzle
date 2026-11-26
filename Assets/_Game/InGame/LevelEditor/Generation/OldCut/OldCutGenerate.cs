using System;
using System.Collections.Generic;
using System.Linq;

namespace Falcon.InGame.LevelEditor.Gen
{
    /// <summary>Một phần của mảnh trên lưới tranh: vòng ngoài và các lỗ.</summary>
    public sealed class OldCutPart
    {
        public int[] Points;
        public List<int[]> Holes;
    }

    /// <summary>Ghép bộ cắt cũ thành tranh: ảnh nguồn → biên mảnh dùng chung → danh sách vùng (port từ oldcut_gen.js).</summary>
    public static partial class OldCutGenerate
    {
        public const int Grid = 1920, Unit = 4, Side = Grid * Unit;
        private const double Smooth = 4.0, SimplifyEps = 0.35, OutSize = 817;
        private const double TipRadius = 24;
        private const int StraightFar = 14, StraightNear = 7, TailMin = 8, TailMax = 48, TailWidth = 20, TailEnd = 4;
        private const int BlobLen = 30, BlobReach = 32, BlobLine = 44, BlobShift = 16, BlobSpan = 26, BlobPiece = 200, StraightShift = 26;
        private const double BlobThick = 4.5;
        private const int DiscMin = 3, DiscMax = 16, DiscR = 70, DiscStep = 3;
        private const double DiscDev = 0.2, DiscFill = 0.75;
        private const int IslandDark = 80, IslandColor = 40;
        private const double IslandArea = 0.02;
        private const double Pi = Math.PI;

        private struct IPt
        {
            public int X, Y;
        }

        private static double Hyp(double a, double b) => OldCutVector.Hyp(a, b);

        private static double SignedArea(List<IPt> p)
        {
            double s = 0;
            for (int i = 0, j = p.Count - 1; i < p.Count; j = i++) s += (double)p[j].X * p[i].Y - (double)p[i].X * p[j].Y;
            return s * 0.5;
        }

        private static double RingArea(int[] d)
        {
            double s = 0;
            for (int i = 0, j = d.Length - 2; i < d.Length; j = i, i += 2) s += (double)d[j] * d[i + 1] - (double)d[i] * d[j + 1];
            return s / 2;
        }

        private static List<IPt> Dedup(List<IPt> pts)
        {
            var res = new List<IPt>();
            for (var i = 0; i < pts.Count; i++) if (res.Count == 0 || res[res.Count - 1].X != pts[i].X || res[res.Count - 1].Y != pts[i].Y) res.Add(pts[i]);
            while (res.Count > 1 && res[0].X == res[res.Count - 1].X && res[0].Y == res[res.Count - 1].Y) res.RemoveAt(res.Count - 1);
            return res;
        }

        public static byte[] DarkOf(OldCutSeg seg)
        {
            var d = new byte[seg.W * seg.W];
            for (var i = 0; i < d.Length; i++) d[i] = Math.Max(seg.Work[i * 3], Math.Max(seg.Work[i * 3 + 1], seg.Work[i * 3 + 2])) < seg.LineDark ? (byte)1 : (byte)0;
            return d;
        }

        private static bool InInk(byte[] dark, int W, double x, double y)
        {
            var idx = (long)Math.Floor(y) * W + (long)Math.Floor(x);
            return idx >= 0 && idx < dark.Length && dark[idx] == 1;
        }

        // ---------- cắt ngang nhau giữa các biên ----------
        private static bool SegX(Pt a, Pt b, Pt c, Pt d)
        {
            double O(Pt p, Pt q, Pt r) => (q.X - p.X) * (r.Y - p.Y) - (q.Y - p.Y) * (r.X - p.X);
            return O(a, b, c) * O(a, b, d) < 0 && O(c, d, a) * O(c, d, b) < 0;
        }

        private sealed class Upd
        {
            public int Ci;
            public List<Pt> Pts;
        }

        private static bool PathsCross(List<Pt> pts, List<Upd> upd, int ci, Pt T)
        {
            bool Near(Pt a, Pt b) => (Math.Abs(a.X - T.X) < 40 && Math.Abs(a.Y - T.Y) < 40) || (Math.Abs(b.X - T.X) < 40 && Math.Abs(b.Y - T.Y) < 40);
            foreach (var u in upd)
            {
                if (u.Ci < ci && upd.Any(v => v.Ci == ci)) continue;
                for (var i = 0; i + 1 < pts.Count; i++)
                {
                    if (!Near(pts[i], pts[i + 1])) continue;
                    for (var j = u.Ci == ci ? i + 2 : 0; j + 1 < u.Pts.Count; j++)
                        if (Near(u.Pts[j], u.Pts[j + 1]) && SegX(pts[i], pts[i + 1], u.Pts[j], u.Pts[j + 1])) return true;
                }
            }
            return false;
        }

        private static Pt BlobMiddle(double x, double y, int W, float[] d2)
        {
            const int R = 8;
            int x0 = (int)Ops.JsRound(x), y0 = (int)Ops.JsRound(y);
            double best = 0, sx = 0, sy = 0;
            var cnt = 0;
            for (var yy = Math.Max(0, y0 - R); yy <= Math.Min(W - 1, y0 + R); yy++)
                for (var xx = Math.Max(0, x0 - R); xx <= Math.Min(W - 1, x0 + R); xx++) best = Math.Max(best, d2[yy * W + xx]);
            if (best <= 0) return new Pt(x, y);
            for (var yy = Math.Max(0, y0 - R); yy <= Math.Min(W - 1, y0 + R); yy++)
                for (var xx = Math.Max(0, x0 - R); xx <= Math.Min(W - 1, x0 + R); xx++)
                    if (d2[yy * W + xx] >= 0.8 * best) { sx += xx + 0.5; sy += yy + 0.5; cnt++; }
            return new Pt(sx / cnt, sy / cnt);
        }

        private struct Line
        {
            public double X, Y, Ux, Uy;
        }

        // điểm gần các đường nhất; đường lệch quá 3 px thì bỏ rồi tìm lại
        private static Pt? MeetPoint(List<Line> lines)
        {
            for (var round = 0; round < 3 && lines.Count >= 3; round++)
            {
                var L = new double[4];
                var R = new double[2];
                foreach (var q in lines)
                {
                    var m = new[] { 1 - q.Ux * q.Ux, -q.Ux * q.Uy, -q.Ux * q.Uy, 1 - q.Uy * q.Uy };
                    for (var i = 0; i < 4; i++) L[i] += m[i];
                    R[0] += m[0] * q.X + m[1] * q.Y; R[1] += m[2] * q.X + m[3] * q.Y;
                }
                var det = L[0] * L[3] - L[1] * L[2];
                if (det < 0.3) return null;
                var T = new Pt((L[3] * R[0] - L[1] * R[1]) / det, (L[0] * R[1] - L[2] * R[0]) / det);
                var worst = -1;
                double wd = 3;
                for (var i = 0; i < lines.Count; i++)
                {
                    var q = lines[i];
                    var d = Math.Abs((T.X - q.X) * q.Uy - (T.Y - q.Y) * q.Ux);
                    if (d > wd) { wd = d; worst = i; }
                }
                if (worst < 0 || lines.Count <= 3) return T;
                var next = new List<Line>();
                for (var i = 0; i < lines.Count; i++) if (i != worst) next.Add(lines[i]);
                lines = next;
            }
            return null;
        }

        private sealed class EndRef
        {
            public int Ci, End, Step;
        }

        private sealed class Hub
        {
            public Pt P;
            public List<EndRef> Ends = new List<EndRef>();
        }

        private static int Cnt(Dictionary<int, int> d, int k) => d.TryGetValue(k, out var v) ? v : 0;

        private static double ChainLen(Chain c)
        {
            double l = 0;
            for (var j = 1; j < c.Pts.Count; j++) l += Hyp(c.Pts[j].X - c.Pts[j - 1].X, c.Pts[j].Y - c.Pts[j - 1].Y);
            return l;
        }

        // các nút trong một cục mực (đầu lá): biên ngắn giữa chúng bỏ, biên còn lại chạy thẳng tới giữa cục
        private static Topo ContractBlobs(Topo topo, int W, byte[] dark)
        {
            var n = W * W;
            var light = new byte[n];
            for (var i = 0; i < n; i++) light[i] = dark[i] != 0 ? (byte)0 : (byte)1;
            var d2 = OldCutImage.Edt(light, W, W).D2;
            var at = new Dictionary<long, Hub>();
            var order = new List<long>();
            var parent = new Dictionary<long, long>();
            for (var ci = 0; ci < topo.Chains.Count; ci++)
            {
                var c = topo.Chains[ci];
                if (c.Closed || c.Pts.Count < 2) continue;
                foreach (var e in new[] { new[] { 0, 1 }, new[] { c.Pts.Count - 1, -1 } })
                {
                    var k = OldCutVector.KeyOf(c.Pts[e[0]]);
                    if (!at.TryGetValue(k, out var v)) { at[k] = v = new Hub { P = c.Pts[e[0]] }; order.Add(k); }
                    v.Ends.Add(new EndRef { Ci = ci, End = e[0], Step = e[1] });
                }
            }
            long Find(long k)
            {
                while (parent.ContainsKey(k) && parent[k] != k) k = parent[k];
                return k;
            }
            var links = new List<(int ci, long a, long b)>();
            var share = new Dictionary<int, int>();
            var dc = new Dictionary<int, int>();
            var round = new Dictionary<int, double>();
            var members = new Dictionary<long, List<Pt>>();
            var cand = new List<(int ci, long a, long b, double len)>();
            foreach (var c in topo.Chains)
            {
                var l = ChainLen(c);
                share[c.Left] = Cnt(share, c.Left) + 1; share[c.Right] = Cnt(share, c.Right) + 1;
                round.TryGetValue(c.Left, out var rl); round[c.Left] = rl + l;
                round.TryGetValue(c.Right, out var rr); round[c.Right] = rr + l;
            }
            bool Small(int id) => round.TryGetValue(id, out var v) && v < BlobPiece;
            for (var ci = 0; ci < topo.Chains.Count; ci++)
            {
                var c = topo.Chains[ci];
                if (c.Closed || c.Pts.Count < 2) continue;
                long a = OldCutVector.KeyOf(c.Pts[0]), b = OldCutVector.KeyOf(c.Pts[c.Pts.Count - 1]);
                if (a == b || at[a].Ends.Count < 3 || at[b].Ends.Count < 3) continue;
                var len = ChainLen(c);
                int mx = (int)Math.Floor((c.Pts[0].X + c.Pts[c.Pts.Count - 1].X) / 2), my = (int)Math.Floor((c.Pts[0].Y + c.Pts[c.Pts.Count - 1].Y) / 2);
                double best = 0;
                for (var y = Math.Max(0, my - 3); y <= Math.Min(W - 1, my + 3); y++)
                    for (var x = Math.Max(0, mx - 3); x <= Math.Min(W - 1, mx + 3); x++) best = Math.Max(best, d2[y * W + x]);
                if (len > BlobLen || new[] { a, b }.Any(k => at[k].Ends.Any(e => { var t = topo.Chains[e.Ci]; return Small(t.Left) || Small(t.Right); })) || Math.Sqrt(best) < BlobThick) continue;
                cand.Add((ci, a, b, len));
            }
            cand = cand.OrderBy(x => x.len).ToList();
            foreach (var cd in cand)
            {
                var c = topo.Chains[cd.ci];
                long ra = Find(cd.a), rb = Find(cd.b);
                if (ra == rb) continue;
                var all = new List<Pt>(members.TryGetValue(ra, out var ma) ? ma : new List<Pt> { at[cd.a].P });
                all.AddRange(members.TryGetValue(rb, out var mb) ? mb : new List<Pt> { at[cd.b].P });
                double span = 0;
                foreach (var u in all) foreach (var v in all) span = Math.Max(span, Hyp(u.X - v.X, u.Y - v.Y));
                if (span > BlobSpan) continue;
                dc.TryGetValue(c.Left, out var dl); dc.TryGetValue(c.Right, out var dr);
                if (dl + 2 > Cnt(share, c.Left) - 1 || dr + 2 > Cnt(share, c.Right) - 1) continue;
                dc[c.Left] = Cnt(dc, c.Left) + 1; dc[c.Right] = Cnt(dc, c.Right) + 1;
                links.Add((cd.ci, cd.a, cd.b)); parent[ra] = rb; members[rb] = all;
            }
            if (links.Count == 0) return topo;
            var groups = new Dictionary<long, (List<long> keys, List<int> links)>();
            var gorder = new List<long>();
            foreach (var k in order)
            {
                var r = Find(k);
                if (r == k && !parent.ContainsKey(k) && !links.Any(l => Find(l.a) == r)) continue;
                if (!groups.ContainsKey(r)) { groups[r] = (new List<long>(), new List<int>()); gorder.Add(r); }
                groups[r].keys.Add(k);
            }
            foreach (var l in links) groups[Find(l.a)].links.Add(l.ci);
            var chains = new List<Chain>(topo.Chains);
            var gone = new HashSet<int>();
            foreach (var gk in gorder)
            {
                var g = groups[gk];
                double cx = 0, cy = 0;
                var refused = false;
                foreach (var k in g.keys) { cx += at[k].P.X; cy += at[k].P.Y; }
                var M = BlobMiddle(cx / g.keys.Count, cy / g.keys.Count, W, d2);
                var lines = new List<Line>();
                foreach (var k in g.keys)
                    foreach (var e in at[k].Ends)
                    {
                        if (g.links.Contains(e.Ci)) continue;
                        var pts = chains[e.Ci].Pts;
                        int a = e.End, b = e.End;
                        double len = 0;
                        while (b + e.Step > 0 && b + e.Step < pts.Count - 1 && len < BlobLine)
                        {
                            len += Hyp(pts[b + e.Step].X - pts[b].X, pts[b + e.Step].Y - pts[b].Y); b += e.Step;
                            if (len < BlobLine / 2.0) a = b;
                        }
                        double ux = pts[a].X - pts[b].X, uy = pts[a].Y - pts[b].Y, l = Hyp(ux, uy);
                        if (l >= 4) lines.Add(new Line { X = pts[a].X, Y = pts[a].Y, Ux = ux / l, Uy = uy / l });
                    }
                var T = MeetPoint(lines);
                if (T.HasValue && Hyp(T.Value.X - M.X, T.Value.Y - M.Y) <= BlobShift && InInk(dark, W, T.Value.X, T.Value.Y)) M = T.Value;
                var drop = new HashSet<int>(g.links);
                var upd = new List<Upd>();
                foreach (var k in g.keys)
                    foreach (var e in at[k].Ends)
                    {
                        if (drop.Contains(e.Ci)) continue;
                        var c = chains[e.Ci];
                        var pts = new List<Pt>(c.Pts);
                        var fi = e.End;
                        double len = 0;
                        var reach = BlobReach;
                        bool clear;
                        for (; ; reach -= 4)
                        {
                            fi = e.End; len = 0;
                            while (fi + e.Step > 0 && fi + e.Step < pts.Count - 1 && len < reach)
                            {
                                len += Hyp(pts[fi + e.Step].X - pts[fi].X, pts[fi + e.Step].Y - pts[fi].Y); fi += e.Step;
                            }
                            var steps = (int)Math.Ceiling(Hyp(pts[fi].X - M.X, pts[fi].Y - M.Y));
                            clear = true;
                            for (var q = 1; q < steps && clear; q++) clear = InInk(dark, W, M.X + (pts[fi].X - M.X) * q / steps, M.Y + (pts[fi].Y - M.Y) * q / steps);
                            if (clear || reach <= 12) break;
                        }
                        if (!clear) { refused = true; continue; }
                        var m = Math.Abs(fi - e.End);
                        for (var q = 1; q < m; q++)
                        {
                            double w = (double)q / m;
                            pts[e.End + e.Step * q] = new Pt(M.X + (pts[fi].X - M.X) * w, M.Y + (pts[fi].Y - M.Y) * w);
                        }
                        pts[e.End] = M;
                        upd.Add(new Upd { Ci = e.Ci, Pts = pts });
                    }
                if (refused) continue;
                var mine = new Dictionary<int, List<Pt>>();
                foreach (var u in upd) mine[u.Ci] = u.Pts;
                var bad = false;
                for (var ci = 0; ci < chains.Count && !bad; ci++)
                {
                    if (drop.Contains(ci) || gone.Contains(ci)) continue;
                    if (PathsCross(mine.TryGetValue(ci, out var mp) ? mp : chains[ci].Pts, upd, ci, M)) bad = true;
                }
                if (bad) continue;
                foreach (var u in upd) chains[u.Ci] = chains[u.Ci].With(u.Pts);
                foreach (var ci in drop) gone.Add(ci);
            }
            var kept = new List<Chain>();
            for (var ci = 0; ci < chains.Count; ci++) if (!gone.Contains(ci)) kept.Add(chains[ci]);
            return new Topo { W = topo.W, H = topo.H, Chains = kept };
        }

        private static double InkWidth(byte[] dark, int W, double x, double y, double nx, double ny)
        {
            double w = 0;
            double r;
            for (r = 0.5; r < 15; r += 0.5) { if (!InInk(dark, W, x + nx * r, y + ny * r)) break; w += 0.5; }
            for (r = 0.5; r < 15; r += 0.5) { if (!InInk(dark, W, x - nx * r, y - ny * r)) break; w += 0.5; }
            return w;
        }

        private static bool InkRun(byte[] dark, int W, Pt a, Pt b, double allow)
        {
            var n = (int)Math.Ceiling(Hyp(b.X - a.X, b.Y - a.Y) * 2);
            var miss = 0;
            for (var i = 0; i <= n; i++)
                if (!InInk(dark, W, a.X + (b.X - a.X) * i / n, a.Y + (b.Y - a.Y) * i / n) && ++miss > (allow != 0 ? allow * n : 2)) return false;
            return true;
        }

        // đuôi mực đi tiếp qua nút, ngược phía các biên gặp nhau; null khi mực dừng trong TailMin px
        private static Pt? InkTip(byte[] dark, int W, Pt T, List<Pt> fars)
        {
            double dx = 0, dy = 0;
            foreach (var q in fars)
            {
                var l = Hyp(q.X - T.X, q.Y - T.Y);
                if (l == 0) l = 1;
                dx += (q.X - T.X) / l; dy += (q.Y - T.Y) / l;
            }
            var l0 = Hyp(dx, dy);
            if (l0 < 0.2) return null;
            var bas = Math.Atan2(-dy, -dx);
            double best = 0, bx = T.X, by = T.Y;
            for (var a = -0.25; a <= 0.251; a += 0.05)
            {
                double ux = Math.Cos(bas + a), uy = Math.Sin(bas + a), reach = 0;
                var miss = 0;
                var ended = false;
                for (var r = 0.5; r <= TailMax; r += 0.5)
                {
                    if (!InInk(dark, W, T.X + ux * r, T.Y + uy * r)) { if (++miss > 1) { ended = true; break; } }
                    else { miss = 0; reach = r; }
                }
                if (ended && reach > best && InkWidth(dark, W, T.X + ux * reach * 0.5, T.Y + uy * reach * 0.5, -uy, ux) <= TailWidth
                    && InkWidth(dark, W, T.X + ux * Math.Max(0, reach - 2), T.Y + uy * Math.Max(0, reach - 2), -uy, ux) <= TailEnd)
                {
                    best = reach; bx = T.X + ux * reach; by = T.Y + uy * reach;
                }
            }
            return best >= TailMin ? new Pt(bx, by) : (Pt?)null;
        }

        private struct FarInfo
        {
            public Pt P;
            public int I;
            public double Len;
        }

        private static FarInfo Far(Chain c, int end, int step, double reach)
        {
            double len = 0;
            var i = end;
            while (i + step > 0 && i + step < c.Pts.Count - 1 && len < reach)
            {
                len += Hyp(c.Pts[i + step].X - c.Pts[i].X, c.Pts[i + step].Y - c.Pts[i].Y); i += step;
            }
            return new FarInfo { P = c.Pts[i], I = i, Len = len };
        }

        /// <summary>Ba biên gặp nhau: hai biên thường chạy tiếp gần thẳng; nút dời lên đường thẳng, điểm cạnh nút theo sau.</summary>
        public static Topo StraightenJunctions(Topo topo, int W, byte[] dark)
        {
            var at = new Dictionary<long, List<EndRef>>();
            var order = new List<long>();
            for (var ci = 0; ci < topo.Chains.Count; ci++)
            {
                var c = topo.Chains[ci];
                if (c.Closed || c.Pts.Count < 4) continue;
                foreach (var e in new[] { new[] { 0, 1 }, new[] { c.Pts.Count - 1, -1 } })
                {
                    var k = OldCutVector.KeyOf(c.Pts[e[0]]);
                    if (!at.TryGetValue(k, out var v)) { at[k] = v = new List<EndRef>(); order.Add(k); }
                    v.Add(new EndRef { Ci = ci, End = e[0], Step = e[1] });
                }
            }
            var chains = new List<Chain>(topo.Chains);
            var moved = 0;
            foreach (var key in order)
            {
                var ends = at[key];
                if (ends.Count != 3) continue;
                var J = topo.Chains[ends[0].Ci].Pts[ends[0].End];
                var M = new double[4];
                var B = new double[2];
                var ok = true;
                foreach (var e in ends)
                {
                    var c = topo.Chains[e.Ci];
                    FarInfo n = Far(c, e.End, e.Step, StraightNear), g = Far(c, e.End, e.Step, StraightFar);
                    if (g.Len < StraightFar * 0.8 || g.I == n.I) { ok = false; continue; }
                    double ux = n.P.X - g.P.X, uy = n.P.Y - g.P.Y, l = Hyp(ux, uy);
                    if (l < 1e-6) { ok = false; continue; }
                    ux /= l; uy /= l;
                    var m = new[] { 1 - ux * ux, -ux * uy, -ux * uy, 1 - uy * uy };
                    for (var q = 0; q < 4; q++) M[q] += m[q];
                    B[0] += m[0] * n.P.X + m[1] * n.P.Y; B[1] += m[2] * n.P.X + m[3] * n.P.Y;
                }
                var det = M[0] * M[3] - M[1] * M[2];
                if (!ok || det < 0.3) continue;
                var T = new Pt((M[3] * B[0] - M[1] * B[1]) / det, (M[0] * B[1] - M[2] * B[0]) / det);
                var shift = Hyp(T.X - J.X, T.Y - J.Y);
                if (shift > StraightShift) continue;
                var fars = ends.Select(e => Far(topo.Chains[e.Ci], e.End, e.Step, StraightFar).P).ToList();
                var tip = InkTip(dark, W, T, fars);
                if (tip.HasValue) { T = tip.Value; shift = Hyp(T.X - J.X, T.Y - J.Y); }
                if (shift < 0.4 || shift > TailMax + StraightShift) continue;
                var reach = Math.Max(shift + 5, StraightNear + 5);
                var f = ends.Select(e => Far(topo.Chains[e.Ci], e.End, e.Step, reach)).ToList();
                if (f.Any(q => q.Len < reach - 1)) continue;
                if (!(InInk(dark, W, T.X, T.Y) && f.All(q => InkRun(dark, W, T, q.P, tip.HasValue ? 0.3 : 0)))) continue;
                var upd = new List<Upd>();
                for (var ei = 0; ei < ends.Count; ei++)
                {
                    var e = ends[ei];
                    var pts = new List<Pt>(chains[e.Ci].Pts);
                    var fi = f[ei].I;
                    var n = Math.Abs(fi - e.End);
                    for (var i = 1; i < n; i++)
                    {
                        var q = e.End + e.Step * i;
                        double w = (double)i / n;
                        pts[q] = new Pt(T.X + (pts[fi].X - T.X) * w, T.Y + (pts[fi].Y - T.Y) * w);
                    }
                    pts[e.End] = T;
                    upd.Add(new Upd { Ci = e.Ci, Pts = pts });
                }
                var mine = new Dictionary<int, List<Pt>>();
                foreach (var u in upd) mine[u.Ci] = u.Pts;
                var bad = false;
                for (var ci = 0; ci < chains.Count && !bad; ci++) if (PathsCross(mine.TryGetValue(ci, out var mp) ? mp : chains[ci].Pts, upd, ci, T)) bad = true;
                if (bad) continue;
                foreach (var u in upd) chains[u.Ci] = chains[u.Ci].With(u.Pts);
                moved++;
            }
            var res = moved > 0 ? new Topo { W = topo.W, H = topo.H, Chains = chains } : topo;
            return ContractBlobs(res, W, dark);
        }

        // vòng mực đen quanh nhuỵ hoa: tia từ nhuỵ tìm chỗ mực bắt đầu và hết, vòng tròn khớp mép trong đẩy ra nửa bề dày
        private static (double x, double y, double r)? InkRing(byte[] dark, int W, double cx, double cy, double R)
        {
            const int N = 48;
            var ri = new List<Pt>();
            var th = new List<double>();
            bool On(double x, double y)
            {
                var idx = (long)Math.Floor(y) * W + (long)Math.Floor(x);
                return idx >= 0 && idx < dark.Length && dark[idx] == 1;
            }
            {
                var idx = (long)Math.Floor(cy) * W + (long)Math.Floor(cx);
                if (idx >= 0 && idx < dark.Length && dark[idx] != 0) return null;
            }
            for (var k = 0; k < N; k++)
            {
                double ux = Math.Cos(2 * Pi * k / N), uy = Math.Sin(2 * Pi * k / N), a = -1, b = -1;
                for (var r = 1.0; r < 1.8 * R; r += 0.5)
                {
                    var on = On(cx + ux * r, cy + uy * r);
                    if (a < 0 && on) a = r; else if (a >= 0 && !on) { b = r; break; }
                }
                if (a > 0.3 * R && b > 0) { ri.Add(new Pt(cx + ux * a, cy + uy * a)); th.Add(b - a); }
            }
            if (ri.Count < N * 0.6) return null;
            th.Sort();
            var t = th[th.Count / 2];
            double sx = 0, sy = 0;
            var n = ri.Count;
            foreach (var q in ri) { sx += q.X; sy += q.Y; }
            sx /= n; sy /= n;
            double suu = 0, suv = 0, svv = 0, suuu = 0, svvv = 0, suvv = 0, svuu = 0;
            foreach (var q in ri)
            {
                var u = q.X - sx;
                var v = q.Y - sy;
                suu += u * u; suv += u * v; svv += v * v; suuu += u * u * u; svvv += v * v * v; suvv += u * v * v; svuu += v * u * u;
            }
            var det = suu * svv - suv * suv;
            if (Math.Abs(det) < 1e-6) return null;
            var uc = (0.5 * (suuu + suvv) * svv - 0.5 * (svvv + svuu) * suv) / det;
            var vc = (0.5 * (svvv + svuu) * suu - 0.5 * (suuu + suvv) * suv) / det;
            double x = sx + uc, y = sy + vc;
            var rr = Math.Sqrt(uc * uc + vc * vc + (suu + svv) / n);
            double dev = 0;
            foreach (var q in ri) dev = Math.Max(dev, Math.Abs(Hyp(q.X - x, q.Y - y) - rr));
            if (dev > 0.2 * rr || Hyp(x - cx, y - cy) > 0.5 * R) return null;
            return (x, y, rr + t / 2);
        }

        // mảnh tròn nhỏ (nhuỵ hoa) gồm vài biên giữa các khe cánh: các biên thành cung của một đường tròn qua các nút
        private static Topo RoundDiscs(Topo topo, int W, byte[] dark)
        {
            var chains = new List<Chain>(topo.Chains);
            var byPiece = new Dictionary<int, List<int>>();
            var changed = 0;
            for (var ci = 0; ci < topo.Chains.Count; ci++)
            {
                var c = topo.Chains[ci];
                foreach (var id in new[] { c.Left, c.Right })
                {
                    if (!byPiece.TryGetValue(id, out var l)) byPiece[id] = l = new List<int>();
                    l.Add(ci);
                }
            }
            // khoá số nguyên không âm xếp tăng dần, khoá âm theo thứ tự thêm (như Object.keys của JS)
            var ids = byPiece.Keys.Where(k => k >= 0).OrderBy(k => k).Concat(byPiece.Keys.Where(k => k < 0)).ToList();
            foreach (var id in ids)
            {
                var list = byPiece[id];
                if (list.Count < DiscMin || list.Count > DiscMax || list.Any(ci => { var c = topo.Chains[ci]; return c.Closed || c.Left == c.Right; })) continue;
                var nodes = new Dictionary<long, (Pt p, int n)>();
                var nodeKeys = new List<long>();
                foreach (var ci in list)
                {
                    var c = topo.Chains[ci];
                    foreach (var p in new[] { c.Pts[0], c.Pts[c.Pts.Count - 1] })
                    {
                        var k = OldCutVector.KeyOf(p);
                        if (!nodes.TryGetValue(k, out var nd)) { nd = (p, 0); nodeKeys.Add(k); }
                        nodes[k] = (nd.p, nd.n + 1);
                    }
                }
                var ns = nodeKeys.Select(k => nodes[k]).ToList();
                if (ns.Count != list.Count || ns.Any(v => v.n != 2)) continue;
                double cx = 0, cy = 0;
                foreach (var v in ns) { cx += v.p.X; cy += v.p.Y; }
                cx /= ns.Count; cy /= ns.Count;
                var rs = ns.Select(v => Hyp(v.p.X - cx, v.p.Y - cy)).ToList();
                double R = 0;
                foreach (var r in rs) R += r;
                R /= rs.Count;
                if (R > DiscR || rs.Any(r => Math.Abs(r - R) > DiscDev * R)) continue;
                var ok = list.All(ci => topo.Chains[ci].Pts.All(q => { var r = Hyp(q.X - cx, q.Y - cy); return r > 0.5 * R && r < 1.25 * R; }));
                if (ok)
                {
                    var all = new List<(double x, double y, double a)>();
                    foreach (var ci in list) foreach (var q in topo.Chains[ci].Pts) all.Add((q.X, q.Y, Math.Atan2(q.Y - cy, q.X - cx)));
                    all = all.OrderBy(q => q.a).ToList();
                    double ar = 0;
                    for (var i = 0; i < all.Count; i++)
                    {
                        var u = all[i];
                        var v = all[(i + 1) % all.Count];
                        ar += u.x * v.y - v.x * u.y;
                    }
                    ar = Math.Abs(ar) / 2 / (Pi * R * R);
                    ok = ar >= DiscFill && ar <= 1.1;
                }
                if (ok)
                {
                    var ord = ns.Select(v => (k: OldCutVector.KeyOf(v.p), a: Math.Atan2(v.p.Y - cy, v.p.X - cx))).OrderBy(v => v.a).ToList();
                    var at2 = new Dictionary<long, int>();
                    for (var oi = 0; oi < ord.Count; oi++) at2[ord[oi].k] = oi;
                    ok = list.All(ci =>
                    {
                        var c = topo.Chains[ci];
                        var dI = Math.Abs(at2[OldCutVector.KeyOf(c.Pts[0])] - at2[OldCutVector.KeyOf(c.Pts[c.Pts.Count - 1])]);
                        return dI == 1 || dI == ord.Count - 1;
                    });
                }
                if (!ok) continue;
                var ring = InkRing(dark, W, cx, cy, R);
                if (!ring.HasValue) continue;
                cx = ring.Value.x; cy = ring.Value.y; R = ring.Value.r;
                foreach (var v in ns)
                {
                    var ang = Math.Atan2(v.p.Y - cy, v.p.X - cx);
                    var to = new Pt(cx + R * Math.Cos(ang), cy + R * Math.Sin(ang));
                    var vk = OldCutVector.KeyOf(v.p);
                    for (var cj = 0; cj < chains.Count; cj++)
                    {
                        var c = chains[cj];
                        var pts = new List<Pt>(c.Pts);
                        var changedEnd = false;
                        foreach (var e in new[] { new[] { 0, 1 }, new[] { pts.Count - 1, -1 } })
                        {
                            if (OldCutVector.KeyOf(c.Pts[e[0]]) != vk) continue;
                            double dx = to.X - v.p.X, dy = to.Y - v.p.Y, len = 0;
                            var q = e[0] + e[1];
                            while (q > 0 && q < pts.Count - 1 && !list.Contains(cj))
                            {
                                len += Hyp(c.Pts[q].X - c.Pts[q - e[1]].X, c.Pts[q].Y - c.Pts[q - e[1]].Y);
                                if (len >= 8) break;
                                var w = 1 - len / 8;
                                pts[q] = new Pt(c.Pts[q].X + dx * w, c.Pts[q].Y + dy * w);
                                q += e[1];
                            }
                            pts[e[0]] = to; changedEnd = true;
                        }
                        if (changedEnd) chains[cj] = c.With(pts);
                    }
                }
                foreach (var ci in list)
                {
                    var c = chains[ci];
                    Pt a = c.Pts[0], b = c.Pts[c.Pts.Count - 1];
                    double a0 = Math.Atan2(a.Y - cy, a.X - cx), a1 = Math.Atan2(b.Y - cy, b.X - cx), d = a1 - a0;
                    while (d > Pi) d -= 2 * Pi;
                    while (d < -Pi) d += 2 * Pi;
                    var n = Math.Max(2, (int)Math.Ceiling(Math.Abs(d) * R / DiscStep));
                    var pts = new List<Pt> { a };
                    for (var i = 1; i < n; i++) pts.Add(new Pt(cx + R * Math.Cos(a0 + d * i / n), cy + R * Math.Sin(a0 + d * i / n)));
                    pts.Add(b);
                    chains[ci] = c.With(pts); changed++;
                }
            }
            return changed > 0 ? new Topo { W = topo.W, H = topo.H, Chains = chains } : topo;
        }

        /// <summary>Mỗi biên đơn giản hoá một lần (hai mảnh kề giữ điểm giống nhau) rồi nối vòng từng mảnh và đặt lên lưới tranh.</summary>
        public static List<List<OldCutPart>> StudioRings(Topo topo, int n, int W, byte[] dark)
        {
            topo = StraightenJunctions(topo, W, dark);
            topo = RoundDiscs(topo, W, dark);
            var eps = (float)(SimplifyEps * W / OutSize);
            var k = (double)Side / W;
            var chains = new List<Chain>();
            foreach (var c in topo.Chains)
            {
                var simple = OldCutVector.Simplify(c.Pts, eps, c.Closed);
                chains.Add(new Chain { Left = c.Left, Right = c.Right, Closed = c.Closed, Pts = simple.Count >= 2 ? simple : c.Pts });
            }
            var t2 = new Topo { W = topo.W, H = topo.H, Chains = chains };
            var res = new List<List<OldCutPart>>();
            for (var id = 0; id < n; id++)
            {
                var loops = new List<List<IPt>>();
                foreach (var loop in OldCutVector.PieceLoops(t2, id))
                {
                    var pts = loop.Select(p => new IPt { X = (int)Math.Min(Side, Math.Max(0, Ops.JsRound(p.X * k))), Y = (int)Math.Min(Side, Math.Max(0, Ops.JsRound(p.Y * k))) }).ToList();
                    pts = Dedup(pts);
                    if (pts.Count >= 3 && Math.Abs(SignedArea(pts)) >= 1) loops.Add(pts);
                }
                var parts = new List<OldCutPart>();
                res.Add(parts);
                if (loops.Count == 0) continue;
                var areas = loops.Select(SignedArea).ToList();
                var big = 0;
                for (var i = 1; i < loops.Count; i++) if (Math.Abs(areas[i]) > Math.Abs(areas[big])) big = i;
                var sign = areas[big] >= 0 ? 1 : -1;
                var outers = new List<List<IPt>>();
                var holes = new List<List<IPt>>();
                for (var i = 0; i < loops.Count; i++)
                {
                    var l = sign > 0 ? loops[i] : Enumerable.Reverse(loops[i]).ToList();
                    (areas[i] * sign > 0 ? outers : holes).Add(l);
                }
                foreach (var o in outers)
                {
                    var mine = holes.Where(hl => Inside(o, hl[0])).Select(Flat).ToList();
                    parts.Add(new OldCutPart { Points = Flat(o), Holes = mine.Count > 0 ? mine : null });
                }
            }
            return res;
        }

        private static int[] Flat(List<IPt> l)
        {
            var o = new int[l.Count * 2];
            for (var i = 0; i < l.Count; i++) { o[i * 2] = l[i].X; o[i * 2 + 1] = l[i].Y; }
            return o;
        }

        private static bool Inside(List<IPt> poly, IPt pt)
        {
            var c = false;
            for (int a = 0, b = poly.Count - 1; a < poly.Count; b = a++)
                if ((poly[a].Y > pt.Y) != (poly[b].Y > pt.Y) && pt.X < (double)(poly[b].X - poly[a].X) * (pt.Y - poly[a].Y) / (poly[b].Y - poly[a].Y) + poly[a].X) c = !c;
            return c;
        }

        // ---------- gộp mảnh ----------
        /// <summary>Dải mỏng tối (giữa hai nét của vùng tối) không phải mảnh: gộp vào láng giềng chung biên dài nhất; trả màu mới hoặc null.</summary>
        public static List<int[]> MergeSlivers(int[] labels, int W, List<int[]> colors)
        {
            var n = 0;
            for (var i = 0; i < labels.Length; i++) if (labels[i] + 1 > n) n = labels[i] + 1;
            var edge = new byte[W * W];
            var area = new int[n];
            var mx = new float[n];
            for (var y = 0; y < W; y++)
                for (var x = 0; x < W; x++)
                {
                    var i = y * W + x;
                    var l = labels[i];
                    area[l]++;
                    if (x == 0 || y == 0 || x == W - 1 || y == W - 1 || labels[i + 1] != l || labels[i - 1] != l || labels[i + W] != l || labels[i - W] != l) edge[i] = 1;
                }
            var d2 = OldCutImage.Edt(edge, W, W).D2;
            for (var i = 0; i < labels.Length; i++)
            {
                double v = Math.Sqrt(d2[i]);
                if (v > mx[labels[i]]) mx[labels[i]] = (float)v;
            }
            var share = new Dictionary<int, int>[n];
            for (var i = 0; i < n; i++) share[i] = new Dictionary<int, int>();
            void Link(int a, int b)
            {
                share[a][b] = Cnt(share[a], b) + 1;
                share[b][a] = Cnt(share[b], a) + 1;
            }
            for (var y = 0; y < W; y++)
                for (var x = 0; x < W; x++)
                {
                    var i = y * W + x;
                    var a = labels[i];
                    if (x + 1 < W && labels[i + 1] != a) Link(a, labels[i + 1]);
                    if (y + 1 < W && labels[i + W] != a) Link(a, labels[i + W]);
                }
            var parent = new int[n];
            for (var i = 0; i < n; i++) parent[i] = i;
            int Find(int a)
            {
                while (parent[a] != a) { parent[a] = parent[parent[a]]; a = parent[a]; }
                return a;
            }
            var order = new List<int>();
            for (var i = 0; i < n; i++)
                if (mx[i] < 9 && Pi * mx[i] * mx[i] / area[i] < 0.25 && Math.Max(colors[i][0], Math.Max(colors[i][1], colors[i][2])) < IslandDark) order.Add(i);
            order = order.OrderBy(a => mx[a]).ToList();
            var merged = 0;
            foreach (var a in order)
            {
                var best = -1;
                var bl = 0;
                foreach (var kv in share[a])
                {
                    var r = Find(kv.Key);
                    if (r != Find(a) && kv.Value > bl) { bl = kv.Value; best = r; }
                }
                if (best >= 0) { parent[Find(a)] = best; merged++; }
            }
            if (merged == 0) return null;
            var remap = new int[n];
            for (var i = 0; i < n; i++) remap[i] = -1;
            var m = 0;
            for (var i = 0; i < n; i++) { var r2 = Find(i); if (remap[r2] < 0) remap[r2] = m++; }
            for (var i = 0; i < labels.Length; i++) labels[i] = remap[Find(labels[i])];
            var nc = new int[m][];
            for (var i = 0; i < n; i++) if (Find(i) == i) nc[remap[i]] = colors[i];
            return nc.ToList();
        }

        /// <summary>Mảnh nhỏ gần đen (chim, đồng tử) là nét đen trên mảnh nó chung biên dài nhất: nhập vào mảnh đó; trả màu mới hoặc null.</summary>
        public static List<int[]> MergeIslands(int[] labels, int W, byte[] work, List<int[]> colors, out byte[] mask)
        {
            mask = null;
            var n = colors.Count;
            var area = new int[n];
            var nb = new Dictionary<int, int>[n];
            var edge = new bool[n];
            for (var i = 0; i < n; i++) nb[i] = new Dictionary<int, int>();
            void Link(int p, int q)
            {
                nb[p][q] = Cnt(nb[p], q) + 1;
                nb[q][p] = Cnt(nb[q], p) + 1;
            }
            for (var y = 0; y < W; y++)
                for (var x = 0; x < W; x++)
                {
                    var i = y * W + x;
                    var a = labels[i];
                    area[a]++;
                    if (x == 0 || y == 0 || x == W - 1 || y == W - 1) edge[a] = true;
                    if (x + 1 < W && labels[i + 1] != a) Link(a, labels[i + 1]);
                    if (y + 1 < W && labels[i + W] != a) Link(a, labels[i + W]);
                }
            var to = new int[n];
            for (var i = 0; i < n; i++) to[i] = -1;
            var count = 0;
            for (var i = 0; i < n; i++)
            {
                var c = colors[i];
                if (nb[i].Count == 0 || edge[i] || Math.Max(c[0], Math.Max(c[1], c[2])) >= IslandColor || area[i] >= IslandArea * W * W) continue;
                var best = -1;
                var bl = 0;
                foreach (var kv in nb[i]) if (kv.Value > bl) { bl = kv.Value; best = kv.Key; }
                to[i] = best; count++;
            }
            if (count == 0) return null;
            for (var i = 0; i < n; i++) while (to[i] >= 0 && to[to[i]] >= 0 && to[i] != to[to[i]]) to[i] = to[to[i]];
            mask = new byte[W * W];
            for (var i = 0; i < W * W; i++)
            {
                var l = labels[i];
                if (to[l] < 0) continue;
                if (Math.Max(work[i * 3], Math.Max(work[i * 3 + 1], work[i * 3 + 2])) < IslandDark) mask[i] = 1;
                labels[i] = to[l];
            }
            var remap = new int[n];
            for (var i = 0; i < n; i++) remap[i] = -1;
            var m = 0;
            var nc = new List<int[]>();
            for (var i = 0; i < n; i++) if (to[i] < 0) { remap[i] = m++; nc.Add(colors[i]); }
            for (var i = 0; i < W * W; i++) labels[i] = remap[labels[i]];
            return nc;
        }

        /// <summary>Bản đồ nhãn → vòng biên từng mảnh trên lưới tranh (đã sửa mũi nhọn móc và vòng tự cắt).</summary>
        public static List<List<OldCutPart>> RingsOf(int[] labels, int n, OldCutSeg seg, out Topo topo)
        {
            topo = OldCutVector.TraceBoundaries(labels, seg.W, seg.W);
            OldCutVector.ShapeBorders(topo, Smooth, seg.LineW);
            var rings = StudioRings(topo, n, seg.W, DarkOf(seg));
            FixTipHooks(rings, seg.W);
            FixCrossings(rings, seg.W);
            return rings;
        }

        /// <summary>Ảnh nguồn → các mảnh bằng bộ cắt cũ; null khi không tìm thấy mảnh nào.</summary>
        public static GeneratedPicture Generate(SourceImage src, GenOptions s)
        {
            int w = src.Width, h = src.Height;
            var rgb = new byte[w * h * 3];
            for (var i = 0; i < w * h; i++)
            {
                var a = src.Px[i * 4 + 3] / 255.0;
                for (var c = 0; c < 3; c++) rgb[i * 3 + c] = (byte)Ops.JsRound(src.Px[i * 4 + c] * a + 255 * (1 - a));
            }
            var seg = OldCutSegment.Run(rgb, w, h, new OldCutParams());
            if (seg == null) return null;
            var n = 0;
            for (var i = 0; i < seg.Labels.Length; i++) if (seg.Labels[i] + 1 > n) n = seg.Labels[i] + 1;
            var labels = (int[])seg.Labels.Clone();
            var cols = seg.Colors;
            byte[] birds = null, masses = null;
            var isl = MergeIslands(labels, seg.W, seg.Work, cols, out var imask);
            if (isl != null)
            {
                cols = isl; n = cols.Count;
                seg.Deco ??= new byte[seg.W * seg.W];
                var d0 = (byte[])seg.Deco.Clone();
                var all = MarkPixels(imask, d0, seg.W, seg.Work);
                (birds, masses) = SplitMarks(all, d0, seg.W, DarkOf(seg));
                for (var i = 0; i < all.Length; i++) if (all[i] != 0) seg.Deco[i] = 0;
            }
            var mg = MergeSlivers(labels, seg.W, cols);
            if (mg != null) { cols = mg; n = cols.Count; }
            var rings = RingsOf(labels, n, seg, out _);
            var pic = new GeneratedPicture { Width = Grid, Height = Grid, Unit = Unit, Engine = "old" };
            pic.Decor = BuildDecor(seg, birds, masses, rings);
            pic.Decor.ForEach(Clamp);
            var id = 0;
            for (var i = 0; i < n; i++)
            {
                if (rings[i].Count == 0) continue;
                var col = cols[i];
                var lab = Palette.ToLab(col[0], col[1], col[2]);
                var colorId = Palette.NearestLab(lab[0], lab[1], lab[2]);
                foreach (var part in rings[i])
                {
                    Clamp(part.Points);
                    part.Holes?.ForEach(Clamp);
                    pic.Regions.Add(new GenRegion { Id = ++id, Points = part.Points, ColorId = colorId, Holes = part.Holes });
                }
            }
            return pic.Regions.Count == 0 ? null : pic;
        }

        private static void Clamp(int[] a)
        {
            for (var k = 0; k < a.Length; k++) a[k] = Ops.Clamp(a[k], 0, Side);
        }
    }
}
