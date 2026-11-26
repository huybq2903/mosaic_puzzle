using System;
using System.Collections.Generic;
using System.Linq;

namespace Falcon.InGame.LevelEditor.Gen
{
    /// <summary>Sửa hình học trên vòng đã lên lưới tranh: mũi nhọn có móc và vòng tự cắt (port từ oldcut_gen.js).</summary>
    public static partial class OldCutGenerate
    {
        private sealed class User
        {
            public OldCutPart Part;
            public int I, Owner;
            public bool Hole;
        }

        private sealed class PartIndex
        {
            public Dictionary<int, List<User>> M = new Dictionary<int, List<User>>();
            public List<OldCutPart> All = new List<OldCutPart>();

            public List<User> Get(int k) => M.TryGetValue(k, out var v) ? v : new List<User>();
        }

        private static int KeyXY(double x, double y) => (int)(x * 8192 + y);

        private static double AreaOf(IList<int> p)
        {
            double s = 0;
            for (int i = 0, j = p.Count - 2; i < p.Count; j = i, i += 2) s += (double)p[j] * p[i + 1] - (double)p[i] * p[j + 1];
            return s / 2;
        }

        private static double Cross2(double ax, double ay, double bx, double by, double cx, double cy) => (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);

        private static double SegDist(double px, double py, double ax, double ay, double bx, double by)
        {
            double dx = bx - ax, dy = by - ay, l2 = dx * dx + dy * dy;
            var t = l2 != 0 ? Math.Max(0, Math.Min(1, ((px - ax) * dx + (py - ay) * dy) / l2)) : 0;
            return Hyp(px - ax - t * dx, py - ay - t * dy);
        }

        private static double LineDist(double px, double py, double ax, double ay, double bx, double by)
        {
            double dx = bx - ax, dy = by - ay, l = Hyp(dx, dy);
            if (l == 0) l = 1;
            return Math.Abs((px - ax) * dy - (py - ay) * dx) / l;
        }

        private static bool SegsCross(double[] a, double[] b, double[] c, double[] d)
        {
            if ((a[0] == c[0] && a[1] == c[1]) || (a[0] == d[0] && a[1] == d[1]) || (b[0] == c[0] && b[1] == c[1]) || (b[0] == d[0] && b[1] == d[1])) return false;
            double d1 = Cross2(a[0], a[1], b[0], b[1], c[0], c[1]), d2 = Cross2(a[0], a[1], b[0], b[1], d[0], d[1]);
            double d3 = Cross2(c[0], c[1], d[0], d[1], a[0], a[1]), d4 = Cross2(c[0], c[1], d[0], d[1], b[0], b[1]);
            return ((d1 > 0) != (d2 > 0)) && ((d3 > 0) != (d4 > 0)) && d1 != 0 && d2 != 0 && d3 != 0 && d4 != 0;
        }

        private static double[] Xy(int[] p, int k) => new double[] { p[k * 2], p[k * 2 + 1] };

        private static bool SelfCross(int[] p)
        {
            var n = p.Length / 2;
            for (var i = 0; i < n; i++)
                for (var j = i + 2; j < n; j++)
                {
                    if (i == 0 && j == n - 1) continue;
                    if (SegsCross(Xy(p, i), Xy(p, (i + 1) % n), Xy(p, j), Xy(p, (j + 1) % n))) return true;
                }
            return false;
        }

        private static bool AnyClash(PartIndex ix, HashSet<OldCutPart> skip, List<double[][]> added, Func<double[], double[], double[], double[], bool> hit, bool endsShared)
        {
            foreach (var o in ix.All)
            {
                if (skip.Contains(o)) continue;
                var rings = new List<int[]> { o.Points };
                if (o.Holes != null) rings.AddRange(o.Holes);
                foreach (var r in rings)
                {
                    var nn = r.Length / 2;
                    for (var e = 0; e < nn; e++)
                    {
                        var a = Xy(r, e);
                        var b = Xy(r, (e + 1) % nn);
                        foreach (var ed in added)
                        {
                            if (endsShared && ((ed[0][0] == a[0] && ed[0][1] == a[1]) || (ed[0][0] == b[0] && ed[0][1] == b[1]) || (ed[1][0] == a[0] && ed[1][1] == a[1]) || (ed[1][0] == b[0] && ed[1][1] == b[1]))) continue;
                            if (hit(ed[0], ed[1], a, b)) return true;
                        }
                    }
                }
            }
            return false;
        }

        private static long EdgeKey(int a, int b) => ((long)a << 32) | (uint)b;

        // ---------- mũi nhọn có móc ----------
        private sealed class SideInfo
        {
            public int Near, Far;
            public List<int> Hook;
        }

        /// <summary>Mũi nhọn mà hai biên cong khỏi đường thẳng của chúng gần nút (móc nơi mực dày nhập vào): nút dời tới chỗ các đường thẳng gặp nhau, hoặc bỏ móc chữ S.</summary>
        public static void FixTipHooks(List<List<OldCutPart>> rings, int W)
        {
            double PX = (double)Side / W, LONG = 20 * PX, HOOK_MAX = 35 * PX, DEV = 8 * PX, J_MAX = 25 * PX, ON_LINE = 2 * PX, J_MIN = 1.5 * PX;
            const double ANG_MAX = 60, ANG_PAR = 5, AREA_MAX = 0.03, HOOK_TURN = 5;
            double HOOK_DEV = 1.2 * PX;
            PartIndex Index()
            {
                var ix = new PartIndex();
                for (var owner = 0; owner < rings.Count; owner++)
                    foreach (var part in rings[owner])
                    {
                        ix.All.Add(part);
                        for (var i = 0; i < part.Points.Length; i += 2)
                        {
                            var kk = KeyXY(part.Points[i], part.Points[i + 1]);
                            if (!ix.M.TryGetValue(kk, out var v)) ix.M[kk] = v = new List<User>();
                            v.Add(new User { Part = part, I = i / 2, Owner = owner });
                        }
                        foreach (var h in part.Holes ?? new List<int[]>())
                            for (var i = 0; i < h.Length; i += 2)
                            {
                                var kk = KeyXY(h[i], h[i + 1]);
                                if (!ix.M.TryGetValue(kk, out var v)) ix.M[kk] = v = new List<User>();
                                v.Add(new User { Part = part, I = -1, Owner = owner, Hole = true });
                            }
                    }
                return ix;
            }
            SideInfo SideOf(int[] p, int n, int i, int dir)
            {
                var path = new List<int> { i };
                double len = 0;
                for (var c = 0; c < 6; c++)
                {
                    var a = path[path.Count - 1];
                    var b = (a + dir + n) % n;
                    var l = Hyp(p[b * 2] - p[a * 2], p[b * 2 + 1] - p[a * 2 + 1]);
                    if (l >= LONG) return new SideInfo { Near = a, Far = b, Hook = path.GetRange(1, Math.Max(0, path.Count - 2)) };
                    len += l;
                    if (len > HOOK_MAX) return null;
                    path.Add(b);
                }
                return null;
            }
            bool TryFix(PartIndex ix, OldCutPart part, int i)
            {
                var p = part.Points;
                var n = p.Length / 2;
                SideInfo s1 = SideOf(p, n, i, -1), s2 = SideOf(p, n, i, 1);
                if (s1 == null || s2 == null || (s1.Hook.Count == 0 && s2.Hook.Count == 0)) return false;
                double vx = p[i * 2], vy = p[i * 2 + 1];
                var sides = new[] { s1, s2 };
                var L = sides.Select(s => new[] { (double)p[s.Near * 2], p[s.Near * 2 + 1], p[s.Far * 2], p[s.Far * 2 + 1] }).ToArray();
                double d1x = L[0][2] - L[0][0], d1y = L[0][3] - L[0][1], d2x = L[1][2] - L[1][0], d2y = L[1][3] - L[1][1];
                double den = d1x * d2y - d1y * d2x, l1 = Hyp(d1x, d1y), l2 = Hyp(d2x, d2y);
                if (Math.Abs(den) / (l1 * l2) < Math.Sin(ANG_PAR * Pi / 180)) return false;
                var t = ((L[1][0] - L[0][0]) * d2y - (L[1][1] - L[0][1]) * d2x) / den;
                double jx = L[0][0] + t * d1x, jy = L[0][1] + t * d1y;
                var shift = Hyp(jx - vx, jy - vy);
                var straight = shift < J_MIN;
                if (shift > J_MAX) return false;
                var ok = true;
                for (var si = 0; si < 2; si++)
                {
                    var s = sides[si];
                    var q = L[si];
                    if (LineDist(vx, vy, q[0], q[1], q[2], q[3]) > DEV) ok = false;
                    foreach (var h in s.Hook) if (LineDist(p[h * 2], p[h * 2 + 1], q[0], q[1], q[2], q[3]) > DEV) ok = false;
                    if ((jx - q[0]) * (q[0] - q[2]) + (jy - q[1]) * (q[1] - q[3]) < 0) ok = false;
                }
                if (!ok) return false;
                var drop = new HashSet<int>();
                var J = new[] { Ops.JsRound(jx), Ops.JsRound(jy) };
                var vk = KeyXY(vx, vy);
                var users = ix.Get(vk);
                var moveParts = new List<OldCutPart>();
                if (straight)
                {
                    J = new[] { vx, vy };
                    foreach (var s in sides)
                    {
                        if (s.Hook.Count == 0) continue;
                        var path = new List<int> { s.Near };
                        for (var z = s.Hook.Count - 1; z >= 0; z--) path.Add(s.Hook[z]);
                        path.Add(i);
                        var turns = new List<double>();
                        double dev = 0;
                        for (var m = 1; m < path.Count - 1; m++)
                        {
                            int A0 = path[m - 1], B0 = path[m], C0 = path[m + 1];
                            double ux = p[B0 * 2] - p[A0 * 2], uy = p[B0 * 2 + 1] - p[A0 * 2 + 1], wx = p[C0 * 2] - p[B0 * 2], wy = p[C0 * 2 + 1] - p[B0 * 2 + 1];
                            turns.Add(Math.Atan2(ux * wy - uy * wx, ux * wx + uy * wy) * 180 / Pi);
                            dev = Math.Max(dev, SegDist(p[B0 * 2], p[B0 * 2 + 1], p[s.Near * 2], p[s.Near * 2 + 1], vx, vy));
                        }
                        var pos = turns.Any(a => a > HOOK_TURN);
                        var neg = turns.Any(a => a < -HOOK_TURN);
                        if (pos && neg && dev > HOOK_DEV) foreach (var h in s.Hook) drop.Add(KeyXY(p[h * 2], p[h * 2 + 1]));
                    }
                    if (drop.Count == 0) return false;
                }
                else
                {
                    if (users.Count != 3 || users.Any(u => u.Hole)) return false;
                    if (users.Count(u => u.Part == part) != 1) return false;
                    var others = users.Where(u => u.Part != part).ToList();
                    if (others.Count != 2) return false;
                    var aN = new[] { KeyXY(p[((i + n - 1) % n) * 2], p[((i + n - 1) % n) * 2 + 1]), KeyXY(p[((i + 1) % n) * 2], p[((i + 1) % n) * 2 + 1]) };
                    var B = others[0];
                    var bp = B.Part.Points;
                    var bn = bp.Length / 2;
                    var nb = new[] { new[] { (B.I + bn - 1) % bn, -1 }, new[] { (B.I + 1) % bn, 1 } };
                    var third = nb.Where(q => Array.IndexOf(aN, KeyXY(bp[q[0] * 2], bp[q[0] * 2 + 1])) < 0).ToList();
                    if (third.Count != 1) return false;
                    var dir = third[0][1];
                    var P = new List<double[]> { new[] { vx, vy } };
                    var idx = B.I;
                    for (var c = 0; c < 3; c++)
                    {
                        idx = (idx + dir + bn) % bn;
                        P.Add(new double[] { bp[idx * 2], bp[idx * 2 + 1] });
                    }
                    var kHit = -1;
                    for (var kk = 0; kk < 3; kk++) if (SegDist(jx, jy, P[kk][0], P[kk][1], P[kk + 1][0], P[kk + 1][1]) <= ON_LINE) { kHit = kk; break; }
                    if (kHit < 0) return false;
                    foreach (var h in s1.Hook) drop.Add(KeyXY(p[h * 2], p[h * 2 + 1]));
                    foreach (var h in s2.Hook) drop.Add(KeyXY(p[h * 2], p[h * 2 + 1]));
                    for (var q = 1; q <= kHit; q++) drop.Add(KeyXY(P[q][0], P[q][1]));
                    foreach (var u in users) moveParts.Add(u.Part);
                }
                var hit = new List<OldCutPart>();
                foreach (var mp in moveParts) if (!hit.Contains(mp)) hit.Add(mp);
                foreach (var kd in drop) foreach (var u in ix.Get(kd)) if (!u.Hole && !hit.Contains(u.Part)) hit.Add(u.Part);
                var made = new List<(OldCutPart part, int[] pts)>();
                var added = new List<double[][]>();
                foreach (var hp in hit)
                {
                    var src = hp.Points;
                    var outp = new List<int>();
                    var oldE = new HashSet<long>();
                    for (var z = 0; z < src.Length; z += 2)
                    {
                        var kz = KeyXY(src[z], src[z + 1]);
                        if (!straight && kz == vk) { outp.Add((int)J[0]); outp.Add((int)J[1]); }
                        else if (!drop.Contains(kz)) { outp.Add(src[z]); outp.Add(src[z + 1]); }
                    }
                    double a0 = AreaOf(src), a1 = AreaOf(outp);
                    var oa = outp.ToArray();
                    if (oa.Length < 6 || Math.Sign(a0) != Math.Sign(a1) || Math.Abs(a1 - a0) > AREA_MAX * Math.Abs(a0) || SelfCross(oa)) return false;
                    for (var z = 0; z < src.Length; z += 2)
                    {
                        var z2 = (z + 2) % src.Length;
                        int k1 = KeyXY(src[z], src[z + 1]), k2 = KeyXY(src[z2], src[z2 + 1]);
                        oldE.Add(EdgeKey(k1, k2)); oldE.Add(EdgeKey(k2, k1));
                    }
                    for (var z = 0; z < oa.Length; z += 2)
                    {
                        var z3 = (z + 2) % oa.Length;
                        if (!oldE.Contains(EdgeKey(KeyXY(oa[z], oa[z + 1]), KeyXY(oa[z3], oa[z3 + 1])))) added.Add(new[] { new double[] { oa[z], oa[z + 1] }, new double[] { oa[z3], oa[z3 + 1] } });
                    }
                    made.Add((hp, oa));
                }
                if (AnyClash(ix, new HashSet<OldCutPart>(hit), added, SegsCross, false)) return false;
                foreach (var m in made) m.part.Points = m.pts;
                return true;
            }
            for (var guard = 0; guard < 400; guard++)
            {
                var ix = Index();
                var done = false;
                var tried = new HashSet<long>();
                for (var a = 0; a < ix.All.Count && !done; a++)
                {
                    var part = ix.All[a];
                    var p = part.Points;
                    var n = p.Length / 2;
                    var sg = Math.Sign(AreaOf(p));
                    if (sg == 0) sg = 1;
                    for (var i = 0; i < n && !done; i++)
                    {
                        double pa0 = p[((i + n - 1) % n) * 2] - p[i * 2], pa1 = p[((i + n - 1) % n) * 2 + 1] - p[i * 2 + 1];
                        double pb0 = p[((i + 1) % n) * 2] - p[i * 2], pb1 = p[((i + 1) % n) * 2 + 1] - p[i * 2 + 1];
                        var cr = pa0 * pb1 - pa1 * pb0;
                        var ang = Math.Atan2(Math.Abs(cr), pa0 * pb0 + pa1 * pb1) * 180 / Pi;
                        if (ang >= ANG_MAX || Math.Sign(-cr) != sg) continue;
                        var kk = ((long)KeyXY(p[i * 2], p[i * 2 + 1]) << 20) | (long)a;
                        if (!tried.Add(kk)) continue;
                        if (TryFix(ix, part, i)) done = true;
                    }
                }
                if (!done) break;
            }
        }

        // ---------- vòng tự cắt ----------
        private sealed class Crossing
        {
            public int I, J;
            public double[] X;
        }

        private static double[] HitPt(double[] a, double[] b, double[] c, double[] d)
        {
            double d1 = Cross2(a[0], a[1], b[0], b[1], c[0], c[1]), d2 = Cross2(a[0], a[1], b[0], b[1], d[0], d[1]);
            double d3 = Cross2(c[0], c[1], d[0], d[1], a[0], a[1]), d4 = Cross2(c[0], c[1], d[0], d[1], b[0], b[1]);
            if (d1 == 0 || d2 == 0 || d3 == 0 || d4 == 0 || (d1 > 0) == (d2 > 0) || (d3 > 0) == (d4 > 0)) return null;
            var t = d3 / (d3 - d4);
            return new[] { a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t };
        }

        private static double[] PtAt(int[] p, int i)
        {
            var n = p.Length / 2;
            var k = ((i % n) + n) % n;
            return new double[] { p[k * 2], p[k * 2 + 1] };
        }

        private static Crossing CrossingOf(int[] p)
        {
            var n = p.Length / 2;
            for (var i = 0; i < n; i++)
                for (var j = i + 2; j < n; j++)
                {
                    if (i == 0 && j == n - 1) continue;
                    var x = HitPt(PtAt(p, i), PtAt(p, i + 1), PtAt(p, j), PtAt(p, j + 1));
                    if (x != null) return new Crossing { I = i, J = j, X = x };
                }
            return null;
        }

        private static int[] Dedupe(List<int> o)
        {
            var res = new List<int>();
            for (var i = 0; i < o.Count; i += 2)
            {
                var l = res.Count;
                if (l >= 2 && res[l - 2] == o[i] && res[l - 1] == o[i + 1]) continue;
                res.Add(o[i]); res.Add(o[i + 1]);
            }
            while (res.Count > 2 && res[0] == res[res.Count - 2] && res[1] == res[res.Count - 1]) res.RemoveRange(res.Count - 2, 2);
            return res.ToArray();
        }

        /// <summary>Vòng tự cắt nơi mảnh mỏng thắt lại (nơ): vòng nhỏ co về điểm cắt, cả ở các vòng chung biên với nó.</summary>
        public static void FixCrossings(List<List<OldCutPart>> rings, int W)
        {
            const double LOBE_MAX = 0.25, AREA_MAX = 0.25;
            const int LOBE_VERTS = 6;
            var failed = new HashSet<OldCutPart>();
            PartIndex Index()
            {
                var ix = new PartIndex();
                foreach (var parts in rings)
                    foreach (var part in parts)
                    {
                        ix.All.Add(part);
                        for (var i = 0; i < part.Points.Length; i += 2)
                        {
                            var kk = KeyXY(part.Points[i], part.Points[i + 1]);
                            if (!ix.M.TryGetValue(kk, out var v)) ix.M[kk] = v = new List<User>();
                            if (!v.Any(u => u.Part == part)) v.Add(new User { Part = part });
                        }
                        foreach (var h in part.Holes ?? new List<int[]>())
                            for (var i = 0; i < h.Length; i += 2)
                            {
                                var kk = KeyXY(h[i], h[i + 1]);
                                if (!ix.M.TryGetValue(kk, out var v)) ix.M[kk] = v = new List<User>();
                                v.Add(new User { Part = part, Hole = true });
                            }
                    }
                return ix;
            }
            double LoopArea(double[] X, int[] p, List<int> idx)
            {
                var f = new List<int> { (int)X[0], (int)X[1] };
                foreach (var k in idx) { var q = PtAt(p, k); f.Add((int)q[0]); f.Add((int)q[1]); }
                return Math.Abs(AreaOf(f));
            }
            bool Repair(PartIndex ix, OldCutPart part)
            {
                var p = part.Points;
                var n = p.Length / 2;
                var c = CrossingOf(p);
                if (c == null) return false;
                var X = new[] { Ops.JsRound(c.X[0]), Ops.JsRound(c.X[1]) };
                var l1 = new List<int>();
                var l2 = new List<int>();
                for (var k = c.I + 1; k <= c.J; k++) l1.Add(k);
                for (var k = c.J + 1; k <= c.I + n; k++) l2.Add(k % n);
                double a1 = LoopArea(X, p, l1), a2 = LoopArea(X, p, l2);
                var lobe = a1 <= a2 ? l1 : l2;
                var big = a1 <= a2 ? a2 : a1;
                var small = a1 <= a2 ? a1 : a2;
                if (lobe.Count == 0 || lobe.Count > LOBE_VERTS || small > LOBE_MAX * big) return false;
                var ia = ((lobe[0] - 1) % n + n) % n;
                var id = (lobe[lobe.Count - 1] + 1) % n;
                double[] A = PtAt(p, ia), B = PtAt(p, lobe[0]), C = PtAt(p, lobe[lobe.Count - 1]), D = PtAt(p, id);
                var spur = new HashSet<int>();
                var lobeKeys = new HashSet<int>();
                var bad = false;
                foreach (var q in lobe)
                {
                    var v = PtAt(p, q);
                    var kq = KeyXY(v[0], v[1]);
                    var users = ix.Get(kq);
                    var others = users.Where(u => u.Part != part).ToList();
                    if (users.Any(u => u.Hole)) bad = true;
                    lobeKeys.Add(kq);
                    if (others.Count == 1) spur.Add(kq);
                    else if (others.Count > 2) bad = true;
                }
                if (bad) return false;
                var outp = new List<int>();
                var s = id;
                for (var k = 0; k < n - lobe.Count; k++)
                {
                    var v = PtAt(p, s);
                    outp.Add((int)v[0]); outp.Add((int)v[1]);
                    s = (s + 1) % n;
                }
                outp.Add((int)X[0]); outp.Add((int)X[1]);
                var made = new List<(OldCutPart part, int[] pts)> { (part, Dedupe(outp)) };
                int ka = KeyXY(A[0], A[1]), kb = KeyXY(B[0], B[1]), kc = KeyXY(C[0], C[1]), kd = KeyXY(D[0], D[1]);
                var seen = new HashSet<OldCutPart> { part };
                var touch = new List<OldCutPart>();
                foreach (var kq in lobeKeys) foreach (var u in ix.Get(kq)) if (u.Part != part && !touch.Contains(u.Part)) touch.Add(u.Part);
                foreach (var kq in new[] { kb, ka, kc, kd }) foreach (var u in ix.Get(kq)) if (u.Part != part && !u.Hole && !touch.Contains(u.Part)) touch.Add(u.Part);
                foreach (var S in touch)
                {
                    if (!seen.Add(S)) continue;
                    var sp = S.Points;
                    var o = new List<int>();
                    var m = sp.Length / 2;
                    for (var i2 = 0; i2 < m; i2++)
                    {
                        var kq = KeyXY(sp[i2 * 2], sp[i2 * 2 + 1]);
                        if (spur.Contains(kq)) { o.Add((int)X[0]); o.Add((int)X[1]); } else { o.Add(sp[i2 * 2]); o.Add(sp[i2 * 2 + 1]); }
                    }
                    void InsertBetween(int k1, int k2)
                    {
                        var r = new List<int>();
                        var mm = o.Count / 2;
                        var did = false;
                        for (var t = 0; t < mm; t++)
                        {
                            int q1x = o[t * 2], q1y = o[t * 2 + 1], q2x = o[((t + 1) % mm) * 2], q2y = o[((t + 1) % mm) * 2 + 1];
                            r.Add(q1x); r.Add(q1y);
                            int a = KeyXY(q1x, q1y), b = KeyXY(q2x, q2y);
                            if (!did && ((a == k1 && b == k2) || (a == k2 && b == k1))) { r.Add((int)X[0]); r.Add((int)X[1]); did = true; }
                        }
                        o = r;
                    }
                    if (!spur.Contains(kb) && !lobeKeys.Contains(ka)) InsertBetween(ka, kb);
                    if (!spur.Contains(kc) && !lobeKeys.Contains(kd)) InsertBetween(kc, kd);
                    var od = Dedupe(o);
                    if (od.Length == sp.Length && od.SequenceEqual(sp)) continue;
                    made.Add((S, od));
                }
                var added = new List<double[][]>();
                foreach (var m0 in made)
                {
                    var src = m0.part.Points;
                    double a0 = AreaOf(src), a1n = AreaOf(m0.pts);
                    var oldE = new HashSet<long>();
                    if (m0.pts.Length < 6 || Math.Sign(a0) != Math.Sign(a1n) || Math.Abs(a1n - a0) > AREA_MAX * Math.Abs(a0) || CrossingOf(m0.pts) != null) return false;
                    for (var z = 0; z < src.Length; z += 2)
                    {
                        var z2 = (z + 2) % src.Length;
                        int k1 = KeyXY(src[z], src[z + 1]), k2 = KeyXY(src[z2], src[z2 + 1]);
                        oldE.Add(EdgeKey(k1, k2)); oldE.Add(EdgeKey(k2, k1));
                    }
                    for (var z = 0; z < m0.pts.Length; z += 2)
                    {
                        var z3 = (z + 2) % m0.pts.Length;
                        if (!oldE.Contains(EdgeKey(KeyXY(m0.pts[z], m0.pts[z + 1]), KeyXY(m0.pts[z3], m0.pts[z3 + 1])))) added.Add(new[] { new double[] { m0.pts[z], m0.pts[z + 1] }, new double[] { m0.pts[z3], m0.pts[z3 + 1] } });
                    }
                }
                if (AnyClash(ix, new HashSet<OldCutPart>(made.Select(q => q.part)), added, (a, b, cc, dd) => HitPt(a, b, cc, dd) != null, true)) return false;
                foreach (var q in made) q.part.Points = q.pts;
                return true;
            }
            for (var guard = 0; guard < 300; guard++)
            {
                var ix = Index();
                var done = false;
                for (var q = 0; q < ix.All.Count && !done; q++)
                {
                    var part = ix.All[q];
                    if (failed.Contains(part) || CrossingOf(part.Points) == null) continue;
                    if (Repair(ix, part)) done = true; else failed.Add(part);
                }
                if (!done) break;
            }
        }
    }
}
