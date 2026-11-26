using System;
using System.Collections.Generic;

namespace Falcon.InGame.LevelEditor.Gen
{
    /// <summary>Điểm 2D; toạ độ có thể đã tròn float (P) hoặc double thô.</summary>
    public struct Pt
    {
        public double X, Y;

        public Pt(double x, double y)
        {
            X = x;
            Y = y;
        }
    }

    /// <summary>Một chuỗi biên chung giữa hai nhãn (left/right), -1 là ngoài khung.</summary>
    public sealed class Chain
    {
        public List<Pt> Pts;
        public List<Pt[]> Bez = new List<Pt[]>();
        public int Left = -1, Right = -1;
        public bool Closed;

        public Chain With(List<Pt> pts) => new Chain { Pts = pts, Bez = Bez, Left = Left, Right = Right, Closed = Closed };
    }

    public sealed class Topo
    {
        public int W, H;
        public List<Chain> Chains = new List<Chain>();
    }

    /// <summary>Bộ vector hoá biên của bộ cắt cũ (port từ oldcut_vec.js, điểm là float như bản C++).</summary>
    public static class OldCutVector
    {
        private const double Pi = 3.14159265358979;

        // ---------- điểm float ----------
        public static Pt P(double x, double y) => new Pt((float)x, (float)y);
        public static Pt Add(Pt a, Pt b) => P(a.X + b.X, a.Y + b.Y);
        public static Pt Sub(Pt a, Pt b) => P(a.X - b.X, a.Y - b.Y);
        public static Pt Mul(Pt a, double k) => P(a.X * k, a.Y * k);
        public static double Dot(Pt a, Pt b) => a.X * b.X + a.Y * b.Y;
        public static double Len(Pt a) => Math.Sqrt(Dot(a, a));
        public static Pt Unit(Pt a)
        {
            var l = Len(a);
            return l > 1e-9 ? Mul(a, 1.0 / l) : P(0, 0);
        }
        private static double Clamp(double v, double a, double b) => v < a ? a : v > b ? b : v;

        /// <summary>Math.hypot của V8 (chia theo max, cộng Kahan).</summary>
        public static double Hyp(double a, double b)
        {
            a = Math.Abs(a); b = Math.Abs(b);
            var max = Math.Max(a, b);
            if (max == 0) return 0;
            double sum = 0, comp = 0;
            for (var k = 0; k < 2; k++)
            {
                var n = (k == 0 ? a : b) / max;
                var summand = n * n - comp;
                var pre = sum + summand;
                comp = (pre - sum) - summand;
                sum = pre;
            }
            return Math.Sqrt(sum) * max;
        }

        public static long KeyOf(Pt p) => (long)Ops.JsRound(p.X) * 1000003 + (long)Ops.JsRound(p.Y);

        // ---------- truy viền ----------
        private struct Edge
        {
            public int Id, Nx, Ny;
        }

        public static Topo TraceBoundaries(int[] labels, int W, int H)
        {
            int At(int x, int y) => (x < 0 || y < 0 || x >= W || y >= H) ? -1 : labels[y * W + x];
            bool Gh(int x, int y) => At(x, y - 1) != At(x, y);
            bool Gv(int x, int y) => At(x - 1, y) != At(x, y);
            var nh = (H + 1) * W;
            var nv = H * (W + 1);
            var seen = new bool[nh + nv];
            var chains = new List<Chain>();
            int Hid(int x, int y) => y * W + x;
            int Vid(int x, int y) => nh + y * (W + 1) + x;
            int EdgesAt(int x, int y, Edge[] o)
            {
                var n = 0;
                if (x > 0 && Gh(x - 1, y)) o[n++] = new Edge { Id = Hid(x - 1, y), Nx = x - 1, Ny = y };
                if (x < W && Gh(x, y)) o[n++] = new Edge { Id = Hid(x, y), Nx = x + 1, Ny = y };
                if (y > 0 && Gv(x, y - 1)) o[n++] = new Edge { Id = Vid(x, y - 1), Nx = x, Ny = y - 1 };
                if (y < H && Gv(x, y)) o[n++] = new Edge { Id = Vid(x, y), Nx = x, Ny = y + 1 };
                return n;
            }
            void Sides(int x0, int y0, int x1, int y1, out int left, out int right)
            {
                if (y0 == y1)
                {
                    int xe = Math.Min(x0, x1), above = At(xe, y0 - 1), below = At(xe, y0);
                    if (x1 > x0) { left = above; right = below; } else { left = below; right = above; }
                }
                else
                {
                    int ye = Math.Min(y0, y1), lp = At(x0 - 1, ye), rp = At(x0, ye);
                    if (y1 > y0) { left = rp; right = lp; } else { left = lp; right = rp; }
                }
            }
            var so = new Edge[4];
            var lo = new Edge[4];
            void Trace(int sx, int sy, Edge first)
            {
                var startJunction = EdgesAt(sx, sy, so) >= 3;
                var c = new Chain { Pts = new List<Pt> { P(sx, sy) } };
                Sides(sx, sy, first.Nx, first.Ny, out c.Left, out c.Right);
                seen[first.Id] = true;
                int x = first.Nx, y = first.Ny;
                c.Pts.Add(P(x, y));
                for (; ; )
                {
                    if (x == sx && y == sy) { c.Closed = !startJunction; break; }
                    var n = EdgesAt(x, y, lo);
                    if (n != 2) break;
                    var has = false;
                    Edge next = default;
                    for (var i = 0; i < n; i++) if (!seen[lo[i].Id]) { next = lo[i]; has = true; }
                    if (!has) break;
                    seen[next.Id] = true; x = next.Nx; y = next.Ny;
                    c.Pts.Add(P(x, y));
                }
                chains.Add(c);
            }
            var ob = new Edge[4];
            for (var y = 0; y <= H; y++)
                for (var x = 0; x <= W; x++)
                {
                    var n = EdgesAt(x, y, ob);
                    if (n < 3) continue;
                    for (var i = 0; i < n; i++) if (!seen[ob[i].Id]) Trace(x, y, ob[i]);
                }
            for (var y = 0; y <= H; y++)
                for (var x = 0; x <= W; x++)
                {
                    var n = EdgesAt(x, y, ob);
                    for (var i = 0; i < n; i++) if (!seen[ob[i].Id]) Trace(x, y, ob[i]);
                }
            return new Topo { W = W, H = H, Chains = chains };
        }

        // ---------- Douglas-Peucker (số học float) ----------
        private static double Fl(double v) => (float)v;

        public static List<Pt> Simplify(List<Pt> pts, double eps, bool closed)
        {
            var n = pts.Count;
            if (n <= 3) return pts;
            var keep = new bool[n];
            keep[0] = keep[n - 1] = true;
            var stack = new Stack<(int, int)>();
            stack.Push((0, n - 1));
            if (closed)
            {
                var far = 0;
                double bd = 0;
                for (var i = 1; i < n - 1; i++)
                {
                    double dx0 = Fl(pts[i].X - pts[0].X), dy0 = Fl(pts[i].Y - pts[0].Y), d = Fl(Fl(dx0 * dx0) + Fl(dy0 * dy0));
                    if (d > bd) { bd = d; far = i; }
                }
                keep[far] = true;
                stack.Clear();
                stack.Push((0, far));
                stack.Push((far, n - 1));
            }
            while (stack.Count > 0)
            {
                var (a, b) = stack.Pop();
                double dx = Fl(pts[b].X - pts[a].X), dy = Fl(pts[b].Y - pts[a].Y), ln = Fl(Math.Sqrt(Fl(Fl(dx * dx) + Fl(dy * dy))));
                var worst = -1;
                var wd = eps;
                for (var i = a + 1; i < b; i++)
                {
                    var d2 = ln > Fl(1e-6)
                        ? Fl(Math.Abs(Fl(Fl(Fl(pts[i].X - pts[a].X) * dy) - Fl(Fl(pts[i].Y - pts[a].Y) * dx))) / ln)
                        : Fl(Hyp(Fl(pts[i].X - pts[a].X), Fl(pts[i].Y - pts[a].Y)));
                    if (d2 > wd) { wd = d2; worst = i; }
                }
                if (worst >= 0) { keep[worst] = true; stack.Push((a, worst)); stack.Push((worst, b)); }
            }
            var res = new List<Pt>();
            for (var i = 0; i < n; i++) if (keep[i]) res.Add(pts[i]);
            return res;
        }

        // ---------- khớp đường cong (Schneider) ----------
        private static Pt BezAt(Pt[] c, double t)
        {
            var m = 1 - t;
            return Add(Add(Add(Mul(c[0], m * m * m), Mul(c[1], 3 * m * m * t)), Mul(c[2], 3 * m * t * t)), Mul(c[3], t * t * t));
        }

        private static Pt BezD1(Pt[] c, double t)
        {
            var m = 1 - t;
            return Add(Add(Mul(Sub(c[1], c[0]), 3 * m * m), Mul(Sub(c[2], c[1]), 6 * m * t)), Mul(Sub(c[3], c[2]), 3 * t * t));
        }

        private static Pt BezD2(Pt[] c, double t)
        {
            var m = 1 - t;
            return Add(Mul(Add(Sub(c[2], Mul(c[1], 2.0)), c[0]), 6 * m), Mul(Add(Sub(c[3], Mul(c[2], 2.0)), c[1]), 6 * t));
        }

        private sealed class Fitter
        {
            private readonly List<Pt> d;
            private readonly List<Pt[]> res;
            private readonly double tol;

            public Fitter(List<Pt> d, List<Pt[]> res, double tol)
            {
                this.d = d; this.res = res; this.tol = tol;
            }

            private Pt[] Generate(int first, int last, double[] u, Pt t1, Pt t2)
            {
                var n = last - first + 1;
                double c00 = 0, c01 = 0, c11 = 0, x0 = 0, x1 = 0;
                for (var i = 0; i < n; i++)
                {
                    double ui = u[i], m = 1 - ui, b0 = m * m * m, b1 = 3 * ui * m * m, b2 = 3 * ui * ui * m, b3 = ui * ui * ui;
                    var a1 = Mul(t1, b1);
                    var a2 = Mul(t2, b2);
                    c00 += Dot(a1, a1); c01 += Dot(a1, a2); c11 += Dot(a2, a2);
                    var tmp = Sub(d[first + i], Add(Mul(d[first], b0 + b1), Mul(d[last], b2 + b3)));
                    x0 += Dot(a1, tmp); x1 += Dot(a2, tmp);
                }
                var det = c00 * c11 - c01 * c01;
                double al = 0, ar = 0;
                if (Math.Abs(det) > 1e-12) { al = (x0 * c11 - x1 * c01) / det; ar = (c00 * x1 - c01 * x0) / det; }
                var seg = Len(Sub(d[last], d[first]));
                var eps = 1e-6 * seg;
                if (al < eps || ar < eps || al > seg || ar > seg) al = ar = seg / 3.0;
                var b = new Pt[4];
                b[0] = d[first]; b[3] = d[last];
                b[1] = Add(b[0], Mul(t1, al)); b[2] = Add(b[3], Mul(t2, ar));
                return b;
            }

            private double MaxError(int first, int last, Pt[] bez, double[] u, out int split)
            {
                double worst = 0;
                split = (last - first + 1) / 2;
                for (var i = first + 1; i < last; i++)
                {
                    var p = BezAt(bez, u[i - first]);
                    var q = Sub(p, d[i]);
                    var e = Dot(q, q);
                    if (e >= worst) { worst = e; split = i; }
                }
                return worst;
            }

            private void Reparam(int first, int last, double[] u, Pt[] bez)
            {
                for (var i = first; i <= last; i++)
                {
                    var t = u[i - first];
                    Pt q = Sub(BezAt(bez, t), d[i]), q1 = BezD1(bez, t), q2 = BezD2(bez, t);
                    double num = Dot(q, q1), den = Dot(q1, q1) + Dot(q, q2);
                    if (Math.Abs(den) > 1e-12) u[i - first] = Clamp(t - num / den, 0.0, 1.0);
                }
            }

            public void Fit(int first, int last, Pt t1, Pt t2, int depth = 0)
            {
                if (last - first == 1)
                {
                    var dist = Len(Sub(d[last], d[first])) / 3.0;
                    res.Add(new[] { d[first], Add(d[first], Mul(t1, dist)), Add(d[last], Mul(t2, dist)), d[last] });
                    return;
                }
                var u = new double[last - first + 1];
                for (var i = first + 1; i <= last; i++) u[i - first] = u[i - first - 1] + Len(Sub(d[i], d[i - 1]));
                var total = u[u.Length - 1];
                if (total < 1e-9) { res.Add(new[] { d[first], d[first], d[first], d[last] }); return; }
                for (var i = 0; i < u.Length; i++) u[i] /= total;
                var b = Generate(first, last, u, t1, t2);
                var err = MaxError(first, last, b, u, out var split);
                if (err < tol * tol) { res.Add(b); return; }
                if (err < tol * tol * 16)
                {
                    for (var it = 0; it < 6; it++)
                    {
                        Reparam(first, last, u, b);
                        b = Generate(first, last, u, t1, t2);
                        err = MaxError(first, last, b, u, out split);
                        if (err < tol * tol) { res.Add(b); return; }
                    }
                }
                if (depth > 40) { res.Add(b); return; }
                split = (int)Clamp(split, first + 1, last - 1);
                var tc = Unit(Sub(d[split - 1], d[split + 1]));
                Fit(first, split, t1, tc, depth + 1);
                Fit(split, last, Mul(tc, -1.0), t2, depth + 1);
            }
        }

        // góc rẽ (độ) tại điểm i, nhìn w điểm mỗi phía
        private static double TurnAt(List<Pt> p, int i, int w, bool cyc)
        {
            var n = p.Count;
            Pt At(int k) => cyc ? p[((k % n) + n) % n] : p[(int)Clamp(k, 0, n - 1)];
            var a = Sub(At(i), At(i - w));
            var b = Sub(At(i + w), At(i));
            double la = Len(a), lb = Len(b);
            if (la < 1e-9 || lb < 1e-9) return 0;
            var c = Clamp(Dot(a, b) / (la * lb), -1.0, 1.0);
            return Math.Acos(c) * 180.0 / Pi;
        }

        public static void FitChain(Chain c, double tolerance, double cornerDeg, Pt? tStart = null, Pt? tEnd = null)
        {
            var n = c.Pts.Count;
            var closed = c.Closed;
            if (n < (closed ? 9 : 5)) return;
            var Pp = c.Pts.GetRange(0, closed ? n - 1 : n);
            var m = Pp.Count;
            const int w = 5;
            var corner = new bool[m];
            var turn = new double[m];
            for (var i = 0; i < m; i++) turn[i] = TurnAt(Pp, i, w, closed);
            for (var i = 0; i < m; i++)
            {
                if (turn[i] < cornerDeg) continue;
                var top = true;
                for (var k = -w; k <= w && top; k++)
                {
                    var j = closed ? ((i + k) % m + m) % m : i + k;
                    if (j < 0 || j >= m || j == i) continue;
                    if (turn[j] > turn[i] || (turn[j] == turn[i] && j < i)) top = false;
                }
                if (top && (closed || (i > 0 && i < m - 1))) corner[i] = true;
            }
            var cut = new List<int>();
            if (closed)
            {
                for (var i = 0; i < m; i++) if (corner[i]) cut.Add(i);
                if (cut.Count == 0) { cut.Add(0); cut.Add(m / 2); }
                if (cut.Count == 1)
                {
                    var far = 0;
                    double best = -1;
                    for (var i = 0; i < m; i++)
                    {
                        var dd = Len(Sub(Pp[i], Pp[cut[0]]));
                        if (dd > best) { best = dd; far = i; }
                    }
                    cut.Add(far);
                    cut.Sort();
                }
            }
            else
            {
                cut.Add(0);
                for (var i = 1; i < m - 1; i++) if (corner[i]) cut.Add(i);
            }
            var res = new List<Pt[]>();
            var pieces = cut.Count;
            for (var s = 0; s < pieces; s++)
            {
                var a = cut[s];
                var b = closed ? cut[(s + 1) % pieces] : (s + 1 < pieces ? cut[s + 1] : m - 1);
                if (!closed && s + 1 == pieces && a == m - 1) break;
                var seg = new List<Pt>();
                if (closed)
                {
                    var cnt = ((b - a) % m + m) % m;
                    if (cnt == 0) cnt = m;
                    for (var k = 0; k <= cnt; k++) seg.Add(Pp[(a + k) % m]);
                }
                else for (var k = a; k <= b; k++) seg.Add(Pp[k]);
                if (seg.Count < 2) continue;
                Pt Tangent(int idx, bool forward, bool isCorner)
                {
                    const int kk = 3;
                    if (isCorner)
                    {
                        var last = seg.Count - 1;
                        if (forward) return Unit(Sub(seg[Math.Min(kk, last)], seg[0]));
                        return Unit(Sub(seg[Math.Max(last - kk, 0)], seg[last]));
                    }
                    var before = Pp[((idx - kk) % m + m) % m];
                    var after = Pp[(idx + kk) % m];
                    return forward ? Unit(Sub(after, before)) : Unit(Sub(before, after));
                }
                var half = m / 2;
                var aCorner = closed ? (corner[a] && !(cut.Count == 2 && cut[0] == 0 && cut[1] == half && !corner[0])) : true;
                var bCorner = closed ? corner[b] : true;
                if (closed && cut.Count == 2 && cut[0] == 0 && cut[1] == half && !corner[0] && !corner[half]) aCorner = bCorner = false;
                var t1 = Tangent(a, true, aCorner);
                var t2 = Tangent(b, false, bCorner);
                if (!closed && s == 0 && tStart.HasValue) t1 = tStart.Value;
                if (!closed && b == m - 1 && tEnd.HasValue) t2 = tEnd.Value;
                new Fitter(seg, res, tolerance).Fit(0, seg.Count - 1, t1, t2);
            }
            if (res.Count == 0) return;
            if (!closed)
            {
                res[0][0] = c.Pts[0];
                res[res.Count - 1][3] = c.Pts[c.Pts.Count - 1];
            }
            var flat = new List<Pt> { res[0][0] };
            foreach (var bz in res)
            {
                var chord = Len(Sub(bz[1], bz[0])) + Len(Sub(bz[2], bz[1])) + Len(Sub(bz[3], bz[2]));
                var ns = (int)Clamp(Math.Truncate(chord / 0.5), 4, 64);
                for (var kk = 1; kk <= ns; kk++) flat.Add(BezAt(bz, (double)kk / ns));
            }
            flat[flat.Count - 1] = res[res.Count - 1][3];
            c.Pts = flat;
            c.Bez = res;
        }

        // ---------- biên mảnh ----------
        private static List<Pt> GaussSmooth(List<Pt> inp, double sigma, bool cyclic)
        {
            var n = inp.Count;
            if (sigma <= 0 || n < 3) return inp;
            var r = Math.Max(1, (int)Math.Ceiling(sigma * 3));
            var k = new double[2 * r + 1];
            double sum = 0;
            for (var i = -r; i <= r; i++) { k[i + r] = Math.Exp(-(double)i * i / (2 * sigma * sigma)); sum += k[i + r]; }
            for (var i = 0; i < k.Length; i++) k[i] /= sum;
            Pt Get(int ix)
            {
                if (cyclic) return inp[((ix % n) + n) % n];
                if (ix < 0) return Sub(Mul(inp[0], 2.0), inp[Math.Min(-ix, n - 1)]);
                if (ix >= n) return Sub(Mul(inp[n - 1], 2.0), inp[Math.Max(2 * (n - 1) - ix, 0)]);
                return inp[ix];
            }
            var res = new List<Pt>(inp);
            for (var i = cyclic ? 0 : 1; i < (cyclic ? n : n - 1); i++)
            {
                double sx = 0, sy = 0;
                for (var j = -r; j <= r; j++)
                {
                    var p = Get(i + j);
                    sx += k[j + r] * p.X; sy += k[j + r] * p.Y;
                }
                res[i] = P(sx, sy);
            }
            return res;
        }

        private sealed class Ray
        {
            public Pt C, U;
            public double W;
        }

        private static Ray NewRay() => new Ray { C = P(0, 0), U = P(0, 0), W = 0 };

        private static Ray FitRay(List<Pt> pts)
        {
            var r = NewRay();
            var n = pts.Count;
            if (n < 2) return r;
            double mx = 0, my = 0;
            for (var i = 0; i < n; i++) { mx += pts[i].X; my += pts[i].Y; }
            mx /= n; my /= n;
            double sxx = 0, sxy = 0, syy = 0;
            for (var i = 0; i < n; i++)
            {
                var dx = pts[i].X - mx;
                var dy = pts[i].Y - my;
                sxx += dx * dx; sxy += dx * dy; syy += dy * dy;
            }
            var ang = 0.5 * Math.Atan2(2 * sxy, sxx - syy);
            r.C = P(mx, my); r.U = P(Math.Cos(ang), Math.Sin(ang));
            if (Dot(r.U, Sub(pts[n - 1], pts[0])) < 0) r.U = Mul(r.U, -1.0);
            r.W = n;
            return r;
        }

        private static void DropBehind(List<Pt> qa, Pt jp, Pt u, double R)
        {
            if (Len(u) <= 0 || qa.Count < 3) return;
            const double margin = 0.75;
            var reach = 4 * R;
            var cutI = 0;
            double along = 0;
            for (var ix = 1; ix + 1 < qa.Count && along <= reach; ix++)
            {
                along += Len(Sub(qa[ix], qa[ix - 1]));
                if (Dot(Sub(qa[ix], jp), u) < margin) cutI = ix; else break;
            }
            if (cutI > 0) qa.RemoveRange(1, cutI);
        }

        /// <summary>Đặt nút giao đúng chỗ, dựng lại từng biên từ nút và phần tin cậy rồi khớp đường cong.</summary>
        public static void ShapeBorders(Topo t, double smooth, double inkW, double cornerDeg = 50)
        {
            if (smooth <= 0) return;
            var sigma = smooth * 0.5;
            var tol = (float)(smooth * 0.25);
            var R = inkW > 0 ? Clamp(inkW * 0.5 + 2.0, 4.0, 14.0) : 3.0;
            var nc = t.Chains.Count;
            bool IsFrame(Chain c) => c.Left < 0 || c.Right < 0;
            var core = new List<Pt>[nc];
            var rayStart = new Ray[nc];
            var rayEnd = new Ray[nc];
            var trusted = new bool[nc];
            var endPts = new List<Pt>[2 * nc];
            var farPts = new List<Pt>[2 * nc];
            var farRay = new Ray[2 * nc];
            for (var ci = 0; ci < nc; ci++) { core[ci] = new List<Pt>(); rayStart[ci] = NewRay(); rayEnd[ci] = NewRay(); trusted[ci] = true; }
            for (var i = 0; i < 2 * nc; i++) { endPts[i] = new List<Pt>(); farPts[i] = new List<Pt>(); farRay[i] = NewRay(); }
            var trimT = 1.5 * R;
            var reachT = Math.Max(3 * R, 12.0);
            for (var ci = 0; ci < nc; ci++)
            {
                var c = t.Chains[ci];
                var n = c.Pts.Count;
                if (c.Closed || n < 2) continue;
                var L = new double[n];
                for (var i = 1; i < n; i++) L[i] = L[i - 1] + Len(Sub(c.Pts[i], c.Pts[i - 1]));
                var total = L[n - 1];
                if (IsFrame(c)) core[ci] = c.Pts;
                else
                {
                    var cutL = total > 2 * R + 2 ? R : total * 0.25;
                    var mid = new List<Pt>();
                    for (var i = 0; i < n; i++) if (L[i] >= cutL && L[i] <= total - cutL) mid.Add(c.Pts[i]);
                    if (mid.Count >= 2) core[ci] = GaussSmooth(mid, sigma, false);
                    if (total <= 2 * R + 2) trusted[ci] = false;
                }
                var src = core[ci].Count == 0 ? c.Pts : core[ci];
                var m = src.Count;
                var Ls = new double[m];
                for (var i = 1; i < m; i++) Ls[i] = Ls[i - 1] + Len(Sub(src[i], src[i - 1]));
                var reach = Math.Min(Math.Max(2 * R, 8.0), Ls[m - 1] * 0.5);
                var a = new List<Pt>();
                var b = new List<Pt>();
                for (var i = 0; i < m; i++)
                {
                    if (Ls[i] <= reach || i < 2) a.Add(src[i]);
                    if (Ls[m - 1] - Ls[i] <= reach || i >= m - 2) b.Add(src[i]);
                }
                b.Reverse();
                rayStart[ci] = FitRay(a); rayEnd[ci] = FitRay(b);
                endPts[2 * ci] = a; endPts[2 * ci + 1] = b;
                var wgt = IsFrame(c) ? 20.0 : trusted[ci] ? 1.0 : 0.3;
                rayStart[ci].W *= wgt; rayEnd[ci].W *= wgt;
                if (!IsFrame(c) && trusted[ci] && core[ci].Count >= 2)
                {
                    for (var e = 0; e < 2; e++)
                    {
                        var q = new List<Pt>(core[ci]);
                        var f = new List<Pt>();
                        if (e == 1) q.Reverse();
                        double along = 0;
                        for (var i = 0; i < q.Count; i++)
                        {
                            if (i > 0) along += Len(Sub(q[i], q[i - 1]));
                            if (along > trimT + reachT) break;
                            if (along >= trimT) f.Add(q[i]);
                        }
                        if (f.Count < 4 || along < trimT + reachT) continue;
                        farRay[2 * ci + e] = FitRay(f); farPts[2 * ci + e] = f;
                    }
                }
            }
            // nút giao đặt ở chỗ các biên gặp nhau
            var at = new Dictionary<long, List<int>>();
            var order = new List<long>();
            var endPos = new Pt[2 * nc];
            var endTrim = new double[2 * nc];
            for (var ci = 0; ci < nc; ci++)
            {
                var cc = t.Chains[ci];
                if (cc.Closed || cc.Pts.Count < 2) continue;
                endPos[2 * ci] = cc.Pts[0]; endPos[2 * ci + 1] = cc.Pts[cc.Pts.Count - 1];
                var k0 = KeyOf(cc.Pts[0]);
                var k1 = KeyOf(cc.Pts[cc.Pts.Count - 1]);
                if (!at.ContainsKey(k0)) { at[k0] = new List<int>(); order.Add(k0); }
                at[k0].Add(2 * ci);
                if (!at.ContainsKey(k1)) { at[k1] = new List<int>(); order.Add(k1); }
                at[k1].Add(2 * ci + 1);
            }
            Ray RayOf(int ei) => (ei & 1) != 0 ? rayEnd[ei >> 1] : rayStart[ei >> 1];
            foreach (var key in order)
            {
                var ends = at[key];
                var J = endPos[ends[0]];
                int ta = -1, tb = -1;
                var farT = false;
                var best = -Math.Cos(25 * Pi / 180);
                for (var ii = 0; ii < ends.Count; ii++)
                    for (var jj = ii + 1; jj < ends.Count; jj++)
                    {
                        Ray ri = RayOf(ends[ii]), rj = RayOf(ends[jj]);
                        if (ri.W <= 0 || rj.W <= 0) continue;
                        var d = Dot(ri.U, rj.U);
                        if (d < best) { best = d; ta = ends[ii]; tb = ends[jj]; farT = false; }
                        Ray fi = farRay[ends[ii]], fj = farRay[ends[jj]];
                        if (fi.W > 0 && fj.W > 0)
                        {
                            d = Dot(fi.U, fj.U);
                            if (d < best) { best = d; ta = ends[ii]; tb = ends[jj]; farT = true; }
                        }
                    }
                var X = J;
                if (ta >= 0)
                {
                    Ray ra = RayOf(ta), rb = RayOf(tb);
                    if (farRay[ta].W > 0 && farRay[tb].W > 0)
                    {
                        farT = true; endTrim[ta] = endTrim[tb] = trimT; endPts[ta] = farPts[ta]; endPts[tb] = farPts[tb];
                    }
                    Ray da = farT ? farRay[ta] : ra, db = farT ? farRay[tb] : rb;
                    var dv = Unit(Sub(Mul(da.U, ra.W), Mul(db.U, rb.W)));
                    var nrm = P(-dv.Y, dv.X);
                    var s = new double[5];
                    var sy = new double[3];
                    foreach (var ee in new[] { ta, tb })
                    {
                        var r = RayOf(ee);
                        var w = r.W / Math.Max(1, endPts[ee].Count);
                        foreach (var qq in endPts[ee])
                        {
                            var x = Dot(Sub(qq, J), dv);
                            var y = Dot(Sub(qq, J), nrm);
                            double xp = 1;
                            for (var p = 0; p < 5; p++) { s[p] += w * xp; xp *= x; }
                            sy[0] += w * y; sy[1] += w * y * x; sy[2] += w * y * x * x;
                        }
                    }
                    var A = new[] { new[] { s[0], s[1], s[2], sy[0] }, new[] { s[1], s[2], s[3], sy[1] }, new[] { s[2], s[3], s[4], sy[2] } };
                    var co = new double[3];
                    bool Solve(int nn)
                    {
                        var M = new double[3][];
                        for (var q = 0; q < 3; q++) M[q] = (double[])A[q].Clone();
                        for (var i2 = 0; i2 < nn; i2++)
                        {
                            var piv = i2;
                            for (var r2 = i2 + 1; r2 < nn; r2++) if (Math.Abs(M[r2][i2]) > Math.Abs(M[piv][i2])) piv = r2;
                            if (Math.Abs(M[piv][i2]) < 1e-9) return false;
                            for (var j2 = 0; j2 < 4; j2++) { var tmp = M[i2][j2]; M[i2][j2] = M[piv][j2]; M[piv][j2] = tmp; }
                            for (var r2 = 0; r2 < nn; r2++)
                            {
                                if (r2 == i2) continue;
                                var fct = M[r2][i2] / M[i2][i2];
                                for (var j2 = i2; j2 < nn; j2++) M[r2][j2] -= fct * M[i2][j2];
                                M[r2][3] -= fct * M[i2][3];
                            }
                        }
                        for (var i2 = 0; i2 < 3; i2++) co[i2] = i2 < nn ? M[i2][3] / M[i2][i2] : 0;
                        return true;
                    }
                    if (!Solve(3) || Math.Abs(co[2]) > 0.2) Solve(2);
                    Pt AtX(double x) => Add(Add(J, Mul(dv, x)), Mul(nrm, co[0] + co[1] * x + co[2] * x * x));
                    double x0 = 0;
                    Ray stem = null;
                    for (var q2 = 0; q2 < ends.Count; q2++)
                    {
                        var ee2 = ends[q2];
                        if (ee2 != ta && ee2 != tb && RayOf(ee2).W > 0 && (stem == null || RayOf(ee2).W > stem.W)) stem = RayOf(ee2);
                    }
                    if (stem != null && Math.Abs(Dot(stem.U, dv)) < 0.97)
                    {
                        for (var it = 0; it < 8; it++)
                        {
                            var pp = Sub(AtX(x0), stem.C);
                            var fv = Fl(Fl(pp.X * stem.U.Y) - Fl(pp.Y * stem.U.X));
                            var dp = Add(dv, Mul(nrm, co[1] + 2 * co[2] * x0));
                            var dfv = Fl(Fl(dp.X * stem.U.Y) - Fl(dp.Y * stem.U.X));
                            if (Math.Abs(dfv) < 1e-9) break;
                            x0 -= fv / dfv;
                        }
                        if (!(Math.Abs(x0) <= 2 * R)) x0 = 0;
                    }
                    X = AtX(x0);
                    var tdir = Unit(Add(dv, Mul(nrm, co[1] + 2 * co[2] * x0)));
                    if (Len(tdir) > 0) { ra.U = tdir; rb.U = Mul(tdir, -1.0); }
                }
                else
                {
                    double a11 = 0, a12 = 0, a22 = 0, b1 = 0, b2 = 0;
                    for (var q3 = 0; q3 < ends.Count; q3++)
                    {
                        var r3 = RayOf(ends[q3]);
                        if (r3.W <= 0) continue;
                        double nx = -r3.U.Y, ny = r3.U.X, ddv = nx * r3.C.X + ny * r3.C.Y;
                        a11 += r3.W * nx * nx; a12 += r3.W * nx * ny; a22 += r3.W * ny * ny; b1 += r3.W * nx * ddv; b2 += r3.W * ny * ddv;
                    }
                    var lam = 0.02 * (a11 + a22) + 1e-6;
                    a11 += lam; a22 += lam; b1 += lam * J.X; b2 += lam * J.Y;
                    var det = a11 * a22 - a12 * a12;
                    X = P((b1 * a22 - b2 * a12) / det, (a11 * b2 - a12 * b1) / det);
                }
                if (!(Len(Sub(X, J)) <= 2 * R)) X = J;
                foreach (var ee3 in ends) endPos[ee3] = X;
            }
            // dựng lại mỗi biên từ nút và phần tin cậy rồi khớp đường cong
            for (var ci = 0; ci < nc; ci++)
            {
                var ch = t.Chains[ci];
                if (ch.Closed)
                {
                    if (!IsFrame(ch))
                    {
                        var Pc = ch.Pts.GetRange(0, ch.Pts.Count - 1);
                        Pc = GaussSmooth(Pc, sigma, true);
                        if (ReferenceEquals(Pc, ch.Pts)) Pc = new List<Pt>(Pc);
                        Pc.Add(Pc[0]);
                        ch.Pts = Pc;
                    }
                    FitChain(ch, tol, cornerDeg);
                    continue;
                }
                if (ch.Pts.Count < 2) continue;
                Pt J0 = endPos[2 * ci], J1 = endPos[2 * ci + 1];
                var Q = new List<Pt> { J0 };
                if (IsFrame(ch)) for (var i = 1; i < ch.Pts.Count - 1; i++) Q.Add(ch.Pts[i]);
                else
                {
                    var qq2 = core[ci];
                    int aa = 0, bb = qq2.Count;
                    double al2;
                    for (al2 = 0; aa + 1 < bb && al2 < endTrim[2 * ci]; aa++) al2 += Len(Sub(qq2[aa + 1], qq2[aa]));
                    for (al2 = 0; bb > aa + 1 && al2 < endTrim[2 * ci + 1]; bb--) al2 += Len(Sub(qq2[bb - 1], qq2[bb - 2]));
                    for (var i = aa; i < bb; i++) if (Len(Sub(qq2[i], J0)) > 0.5 && Len(Sub(qq2[i], J1)) > 0.5) Q.Add(qq2[i]);
                }
                Q.Add(J1);
                DropBehind(Q, J0, rayStart[ci].U, R);
                Q.Reverse(); DropBehind(Q, J1, rayEnd[ci].U, R); Q.Reverse();
                ch.Pts = Q;
                if (ch.Pts.Count < 5)
                {
                    var dd2 = Sub(J1, J0);
                    ch.Bez = new List<Pt[]> { new[] { J0, Add(J0, Mul(dd2, 1.0 / 3)), Add(J0, Mul(dd2, 2.0 / 3)), J1 } };
                    continue;
                }
                Pt t1 = rayStart[ci].U, t2 = rayEnd[ci].U;
                FitChain(ch, tol, cornerDeg, Len(t1) > 0 ? t1 : (Pt?)null, Len(t2) > 0 ? t2 : (Pt?)null);
            }
        }

        // ---------- hình mảnh ----------
        private struct Ref
        {
            public int Chain;
            public bool Rev;
        }

        private static Pt PointAt(Topo t, Ref r, int k)
        {
            var p = t.Chains[r.Chain].Pts;
            return r.Rev ? p[p.Count - 1 - k] : p[k];
        }

        private static List<List<Ref>> AssembleLoops(Topo t, int id)
        {
            var refs = new List<Ref>();
            for (var i = 0; i < t.Chains.Count; i++)
            {
                if (t.Chains[i].Left == id) refs.Add(new Ref { Chain = i, Rev = false });
                if (t.Chains[i].Right == id) refs.Add(new Ref { Chain = i, Rev = true });
            }
            long Key(Pt p) => (long)Ops.JsRound(p.Y) * (t.W + 2) + (long)Ops.JsRound(p.X);
            var loops = new List<List<Ref>>();
            var startAt = new Dictionary<long, List<int>>();
            var used = new bool[refs.Count];
            for (var i = 0; i < refs.Count; i++)
            {
                if (t.Chains[refs[i].Chain].Closed) { loops.Add(new List<Ref> { refs[i] }); used[i] = true; }
                else
                {
                    var k = Key(PointAt(t, refs[i], 0));
                    if (!startAt.TryGetValue(k, out var l)) startAt[k] = l = new List<int>();
                    l.Add(i);
                }
            }
            for (var i = 0; i < refs.Count; i++)
            {
                if (used[i]) continue;
                var loop = new List<Ref>();
                var startKey = Key(PointAt(t, refs[i], 0));
                var cur = i;
                for (; ; )
                {
                    used[cur] = true; loop.Add(refs[cur]);
                    var n = t.Chains[refs[cur].Chain].Pts.Count;
                    var end = PointAt(t, refs[cur], n - 1);
                    var ek = Key(end);
                    if (ek == startKey) break;
                    if (!startAt.TryGetValue(ek, out var cands)) break;
                    var before = PointAt(t, refs[cur], n - 2);
                    double ix = Fl(end.X - before.X), iy = Fl(end.Y - before.Y), bt = Fl(1e9);
                    var pick = -1;
                    foreach (var cand in cands)
                    {
                        if (used[cand]) continue;
                        Pt s0 = PointAt(t, refs[cand], 0), s1 = PointAt(t, refs[cand], 1);
                        double ox = Fl(s1.X - s0.X), oy = Fl(s1.Y - s0.Y);
                        var turn = Fl(Math.Atan2(Fl(Fl(ix * oy) - Fl(iy * ox)), Fl(Fl(ix * ox) + Fl(iy * oy))));
                        if (turn < bt) { bt = turn; pick = cand; }
                    }
                    if (pick < 0) break;
                    cur = pick;
                }
                loops.Add(loop);
            }
            return loops;
        }

        /// <summary>Các vòng biên của mảnh id (điểm cuối lặp lại điểm đầu).</summary>
        public static List<List<Pt>> PieceLoops(Topo t, int id)
        {
            var res = new List<List<Pt>>();
            foreach (var refs in AssembleLoops(t, id))
            {
                var loop = new List<Pt>();
                foreach (var r in refs)
                {
                    var p = t.Chains[r.Chain].Pts;
                    var n = p.Count;
                    for (var k = 0; k + 1 < n; k++) loop.Add(r.Rev ? p[n - 1 - k] : p[k]);
                }
                if (loop.Count > 0) loop.Add(loop[0]);
                res.Add(loop);
            }
            return res;
        }
    }
}
