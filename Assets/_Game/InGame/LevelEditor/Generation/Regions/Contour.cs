using System;
using System.Collections.Generic;

namespace Falcon.InGame.LevelEditor.Gen
{
    /// <summary>Điểm thực.</summary>
    public readonly struct V
    {
        public readonly double X, Y;

        public V(double x, double y)
        {
            X = x;
            Y = y;
        }

        public static V operator -(V a, V b) => new(a.X - b.X, a.Y - b.Y);

        public static V operator +(V a, V b) => new(a.X + b.X, a.Y + b.Y);

        public static V operator *(V a, double k) => new(a.X * k, a.Y * k);

        public static double Dot(V a, V b) => a.X * b.X + a.Y * b.Y;

        public static double Len(V a) => Math.Sqrt(a.X * a.X + a.Y * a.Y);

        public static double Dist(V a, V b)
        {
            var dx = a.X - b.X;
            var dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        public static V Norm(V a)
        {
            var l = Len(a);
            return l < 1e-9 ? new V(0, 0) : new V(a.X / l, a.Y / l);
        }

        public static V Lerp(V a, V b, double t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

        public static double SignedAngle(V a, V b) => Math.Atan2(a.X * b.Y - a.Y * b.X, a.X * b.X + a.Y * b.Y) * 180 / Math.PI;

        public static double Angle(V a, V b) => Math.Abs(SignedAngle(a, b));
    }

    /// <summary>Điểm nguyên trên lưới.</summary>
    public readonly struct P : IEquatable<P>
    {
        public readonly int X, Y;

        public P(int x, int y)
        {
            X = x;
            Y = y;
        }

        public bool Equals(P o) => X == o.X && Y == o.Y;

        public override bool Equals(object obj) => obj is P p && Equals(p);

        public override int GetHashCode() => X * 20011 + Y;

        public V ToV() => new(X, Y);
    }

    /// <summary>Làm mượt viền: giữ góc, Gauss giữa các góc, khớp Bézier bậc ba (Schneider) — port từ contour.js.</summary>
    public static class Contour
    {
        public struct Cubic
        {
            public V P0, P1, P2, P3;
        }

        public sealed class Options
        {
            public double CornerDeg = 55, Epsilon = 0.4, Step, FitTol;
            public bool Flow;
        }

        // ================= khớp đường cong (Schneider) =================
        public static V CubicAt(Cubic c, double t)
        {
            double u = 1 - t, a = u * u * u, b = 3 * u * u * t, d = 3 * u * t * t, e = t * t * t;
            return new V(a * c.P0.X + b * c.P1.X + d * c.P2.X + e * c.P3.X, a * c.P0.Y + b * c.P1.Y + d * c.P2.Y + e * c.P3.Y);
        }

        private static double B0(double u) => (1 - u) * (1 - u) * (1 - u);

        private static double B1(double u) => 3 * u * (1 - u) * (1 - u);

        private static double B2(double u) => 3 * u * u * (1 - u);

        private static double B3(double u) => u * u * u;

        private static double[] ChordLength(IList<V> d, int first, int last)
        {
            var u = new double[last - first + 1];
            for (var i = first + 1; i <= last; i++) u[i - first] = u[i - first - 1] + V.Dist(d[i], d[i - 1]);
            var total = u[last - first];
            for (var i = 1; i < u.Length; i++) u[i] = total > 1e-9 ? u[i] / total : (double)i / (u.Length - 1);
            return u;
        }

        private static Cubic Generate(IList<V> d, int first, int last, double[] u, V t1, V t2)
        {
            V p0 = d[first], p3 = d[last];
            double c00 = 0, c01 = 0, c11 = 0, x0 = 0, x1 = 0;
            for (var i = 0; i <= last - first; i++)
            {
                double t = u[i], b0 = B0(t), b1 = B1(t), b2 = B2(t), b3 = B3(t);
                var a1 = t1 * b1;
                var a2 = t2 * b2;
                c00 += V.Dot(a1, a1);
                c01 += V.Dot(a1, a2);
                c11 += V.Dot(a2, a2);
                var tmp = d[first + i] - (p0 * (b0 + b1) + p3 * (b2 + b3));
                x0 += V.Dot(a1, tmp);
                x1 += V.Dot(a2, tmp);
            }
            var det = c00 * c11 - c01 * c01;
            var alphaL = Math.Abs(det) > 1e-12 ? (x0 * c11 - x1 * c01) / det : 0;
            var alphaR = Math.Abs(det) > 1e-12 ? (c00 * x1 - c01 * x0) / det : 0;
            var seg = V.Dist(p0, p3);
            var eps = 1e-6 * seg;
            if (alphaL < eps || alphaR < eps || alphaL > seg * 2 || alphaR > seg * 2) alphaL = alphaR = seg / 3;
            return new Cubic { P0 = p0, P1 = p0 + t1 * alphaL, P2 = p3 + t2 * alphaR, P3 = p3 };
        }

        private static double NewtonRaphson(Cubic q, V p, double u)
        {
            var q0 = CubicAt(q, u);
            var o = 1 - u;
            var d1 = ((q.P1 - q.P0) * (o * o) + (q.P2 - q.P1) * (2 * o * u) + (q.P3 - q.P2) * (u * u)) * 3;
            var d2 = ((q.P2 - q.P1 * 2 + q.P0) * o + (q.P3 - q.P2 * 2 + q.P1) * u) * 6;
            var num = V.Dot(q0 - p, d1);
            var den = V.Dot(d1, d1) + V.Dot(q0 - p, d2);
            if (Math.Abs(den) < 1e-12) return u;
            return Ops.Clamp(u - num / den, 0, 1);
        }

        private static (double err, int split) MaxError(IList<V> d, int first, int last, Cubic bez, double[] u)
        {
            var split = Ops.Clamp(((last - first + 1) >> 1) + first, first + 1, last - 1);
            double max = 0;
            for (var i = first + 1; i < last; i++)
            {
                var p = CubicAt(bez, u[i - first]);
                double dx = p.X - d[i].X, dy = p.Y - d[i].Y, dd = dx * dx + dy * dy;
                if (dd >= max) { max = dd; split = i; }
            }
            return (max, split);
        }

        private static void FitRec(IList<V> d, int first, int last, V tHat1, V tHat2, double error, List<Cubic> output)
        {
            if (last - first == 1)
            {
                var dd = V.Dist(d[first], d[last]) / 3;
                output.Add(new Cubic { P0 = d[first], P1 = d[first] + tHat1 * dd, P2 = d[last] + tHat2 * dd, P3 = d[last] });
                return;
            }
            var u = ChordLength(d, first, last);
            var bez = Generate(d, first, last, u, tHat1, tHat2);
            var me = MaxError(d, first, last, bez, u);
            var split = me.split;
            if (me.err < error) { output.Add(bez); return; }
            if (me.err < error * 16)
            {
                for (var i = 0; i < 6; i++)
                {
                    var up = new double[u.Length];
                    for (var k = 0; k < u.Length; k++) up[k] = NewtonRaphson(bez, d[first + k], u[k]);
                    bez = Generate(d, first, last, up, tHat1, tHat2);
                    me = MaxError(d, first, last, bez, up);
                    split = me.split;
                    if (me.err < error) { output.Add(bez); return; }
                    u = up;
                }
            }
            var center = (d[split - 1] - d[split]) + (d[split] - d[split + 1]);
            var tCenter = center.X * center.X + center.Y * center.Y < 1e-12 ? V.Norm(d[split - 1] - d[split + 1]) : V.Norm(center);
            FitRec(d, first, split, tHat1, tCenter, error, output);
            FitRec(d, split, last, tCenter * -1, tHat2, error, output);
        }

        /// <summary>Ít Bézier bậc ba nhất trong tolerance; t1: tiếp tuyến ra khỏi điểm đầu, t2: ra khỏi điểm cuối (hướng vào trong).</summary>
        public static List<Cubic> Fit(IList<V> d, double tolerance, V? t1, V? t2)
        {
            var output = new List<Cubic>();
            if (d.Count < 2) return output;
            var arm = Math.Min(4, d.Count - 1);
            var a = t1 ?? V.Norm(d[arm] - d[0]);
            var b = t2 ?? V.Norm(d[d.Count - 1 - arm] - d[d.Count - 1]);
            FitRec(d, 0, d.Count - 1, a, b, tolerance * tolerance, output);
            return output;
        }

        // ================= làm mượt viền =================
        private const int MaxCornerArm = 10, SigmaFraction = 8, MinStraightLength = 24;
        private const double StraightTolFactor = 2, StraightTolPerLength = 0.02, StraightTolBase = 1.5;
        private const double FitMinTolerance = 1.6, FitSampleStep = 36, FitSagitta = 0.5;
        private static readonly int[] FlowDivisors = { 9, 14, 24 };
        private const double FlowDrift = 0.012;
        private const int FlowMinLength = 24;

        private static List<V> FlowSmooth(List<V> seg, double sigma)
        {
            var basic = GaussOpen(seg, FitSigma(sigma, seg.Count));
            if (seg.Count < FlowMinLength) return basic;
            var limit = FlowDrift * seg.Count;
            for (var d = 0; d < FlowDivisors.Length; d++)
            {
                var s = (double)seg.Count / FlowDivisors[d];
                if (s <= sigma) break;
                var o = GaussOpen(seg, s);
                double dev = 0;
                for (var i = 0; i < o.Count && dev <= limit; i++) dev = Math.Max(dev, V.Dist(o[i], basic[i]));
                if (dev <= limit) return o;
            }
            return basic;
        }

        private static double FitSigma(double sigma, int length) => Math.Min(sigma, Math.Max(1.5, (double)length / SigmaFraction));

        private static int Arm(int n, int divisor) => Ops.Clamp(n / divisor, 4, MaxCornerArm);

        private static double[] Kernel(double sigma)
        {
            var r = (int)Math.Ceiling(sigma * 3);
            var k = new double[r * 2 + 1];
            double sum = 0;
            for (var i = -r; i <= r; i++)
            {
                k[i + r] = Math.Exp(-i * i / (2 * sigma * sigma));
                sum += k[i + r];
            }
            for (var i = 0; i < k.Length; i++) k[i] /= sum;
            return k;
        }

        private static List<V> GaussCircular(List<V> q, double sigma)
        {
            if (sigma <= 0) return q;
            var k = Kernel(sigma);
            var r = k.Length >> 1;
            var n = q.Count;
            var o = new List<V>(n);
            for (var i = 0; i < n; i++)
            {
                double sx = 0, sy = 0;
                for (var j = -r; j <= r; j++)
                {
                    var p = q[(((i + j) % n) + n) % n];
                    sx += p.X * k[j + r];
                    sy += p.Y * k[j + r];
                }
                o.Add(new V(sx, sy));
            }
            return o;
        }

        // phản xạ lẻ ở hai đầu giữ nguyên điểm đầu cuối và tiếp tuyến
        private static List<V> GaussOpen(List<V> s, double sigma)
        {
            var n = s.Count;
            if (n < 3) return s;
            var k = Kernel(sigma);
            var r = k.Length >> 1;
            var o = new List<V>(n);
            for (var i = 0; i < n; i++)
            {
                double ax = 0, ay = 0;
                for (var j = -r; j <= r; j++)
                {
                    var idx = i + j;
                    double px, py;
                    if (idx < 0) { var m = s[Math.Min(-idx, n - 1)]; px = 2 * s[0].X - m.X; py = 2 * s[0].Y - m.Y; }
                    else if (idx >= n) { var m2 = s[Math.Max(2 * (n - 1) - idx, 0)]; px = 2 * s[n - 1].X - m2.X; py = 2 * s[n - 1].Y - m2.Y; }
                    else { px = s[idx].X; py = s[idx].Y; }
                    ax += px * k[j + r];
                    ay += py * k[j + r];
                }
                o.Add(new V(ax, ay));
            }
            o[0] = s[0];
            o[n - 1] = s[n - 1];
            return o;
        }

        private static List<V> Densify(IList<V> loop)
        {
            var list = new List<V>();
            for (var i = 0; i < loop.Count; i++)
            {
                var a = loop[i];
                var b = loop[(i + 1) % loop.Count];
                var steps = Math.Max(1, (int)Ops.JsRound(V.Dist(a, b)));
                for (var k = 0; k < steps; k++) list.Add(V.Lerp(a, b, (double)k / steps));
            }
            return list;
        }

        private static List<V> DensifyOpen(IList<V> arc)
        {
            var list = new List<V>();
            for (var i = 0; i + 1 < arc.Count; i++)
            {
                var a = arc[i];
                var b = arc[i + 1];
                var steps = Math.Max(1, (int)Ops.JsRound(V.Dist(a, b)));
                for (var k = 0; k < steps; k++) list.Add(V.Lerp(a, b, (double)k / steps));
            }
            if (arc.Count > 0) list.Add(arc[arc.Count - 1]);
            return list;
        }

        private static List<V> ToFloat(IList<V> loop) => new(loop);

        private static List<int> FindCorners(List<V> q, double sigma0, double thetaDeg)
        {
            var n = q.Count;
            var q0 = GaussCircular(q, sigma0);
            var a = Arm(n, 8);
            var ang = new double[n];
            for (var i = 0; i < n; i++) ang[i] = V.Angle(q0[i] - q0[(i - a + n * 2) % n], q0[(i + a) % n] - q0[i]);
            var corners = new List<int>();
            for (var i = 0; i < n; i++)
            {
                if (ang[i] <= thetaDeg) continue;
                var isMax = true;
                for (var j = 1; j <= a && isMax; j++) isMax = ang[i] > ang[(i - j + n) % n] && ang[i] >= ang[(i + j) % n];
                if (isMax) corners.Add(i);
            }
            return corners;
        }

        private static List<V> Segment(List<V> q, int from, int to, bool whole)
        {
            var n = q.Count;
            var l = whole ? n + 1 : ((to - from + n) % n) + 1;
            var seg = new List<V>(l);
            for (var k = 0; k < l; k++) seg.Add(q[(from + k) % n]);
            return seg;
        }

        private static double DistToLine(V p, V a, V dir)
        {
            var l = V.Len(dir);
            return l < 1e-6 ? V.Dist(p, a) : Math.Abs(dir.X * (p.Y - a.Y) - dir.Y * (p.X - a.X)) / l;
        }

        private static bool IsStraight(List<V> pts, double tol)
        {
            var chord = pts[pts.Count - 1] - pts[0];
            if (V.Len(chord) < MinStraightLength) return false;
            tol = Math.Min(tol, StraightTolPerLength * V.Len(chord) + StraightTolBase);
            for (var i = 0; i < pts.Count; i++) if (DistToLine(pts[i], pts[0], chord) > tol) return false;
            return true;
        }

        private static void FitPiece(List<V> dst, List<V> pts, double tol, double step, bool closedPiece)
        {
            tol = Math.Max(tol, FitMinTolerance);
            step = Math.Max(step, FitSampleStep);
            tol = Math.Min(tol, Math.Max(0.8, pts.Count / 50.0));
            if (!closedPiece && IsStraight(pts, StraightTolFactor * tol)) { dst.Add(pts[0]); return; }
            V? t1 = null, t2 = null;
            if (closedPiece)
            {
                var a = Math.Min(4, (pts.Count - 1) / 2);
                var t = V.Norm(pts[a] - pts[pts.Count - 1 - a]);
                t1 = t;
                t2 = t * -1;
            }
            var cubics = Fit(pts, tol, t1, t2);
            foreach (var c in cubics)
            {
                var l = V.Dist(c.P0, c.P1) + V.Dist(c.P1, c.P2) + V.Dist(c.P2, c.P3);
                var chord = c.P3 - c.P0;
                var dev = Math.Max(DistToLine(c.P1, c.P0, chord), DistToLine(c.P2, c.P0, chord));
                var k = Math.Max(1, Math.Max((int)Math.Ceiling(l / step), (int)Math.Ceiling(Math.Sqrt(0.75 * dev / FitSagitta))));
                for (var j = 0; j < k; j++) dst.Add(j == 0 ? c.P0 : CubicAt(c, (double)j / k));
            }
        }

        private static List<V> Simplify(List<V> pts, double eps)
        {
            var keep = new bool[pts.Count];
            var stack = new Stack<(int lo, int hi)>();
            stack.Push((0, pts.Count - 1));
            keep[0] = keep[pts.Count - 1] = true;
            while (stack.Count > 0)
            {
                var (lo, hi) = stack.Pop();
                if (hi - lo < 2) continue;
                var a = pts[lo];
                var ab = pts[hi] - a;
                var l2 = V.Dot(ab, ab);
                var best = -1;
                double dmax = 0;
                for (var i = lo + 1; i < hi; i++)
                {
                    var t = l2 < 1e-9 ? 0 : Ops.Clamp(V.Dot(pts[i] - a, ab) / l2, 0, 1);
                    var d = V.Dist(pts[i], a + ab * t);
                    if (d > dmax) { dmax = d; best = i; }
                }
                if (dmax <= eps) continue;
                keep[best] = true;
                stack.Push((lo, best));
                stack.Push((best, hi));
            }
            var r = new List<V>();
            for (var k = 0; k < pts.Count; k++) if (keep[k]) r.Add(pts[k]);
            return r;
        }

        private static double Knot(V a, V b, double alpha) => Math.Max(1e-4, Math.Pow(V.Dist(a, b), alpha));

        private static V Comb(V a, V b, double ka, double kb) => a * ka + b * kb;

        private static V CatmullRom(V p0, V p1, V p2, V p3, double u)
        {
            double t0 = 0, t1 = t0 + Knot(p0, p1, 0.5), t2 = t1 + Knot(p1, p2, 0.5), t3 = t2 + Knot(p2, p3, 0.5), t = t1 + (t2 - t1) * u;
            var a1 = Comb(p0, p1, (t1 - t) / (t1 - t0), (t - t0) / (t1 - t0));
            var a2 = Comb(p1, p2, (t2 - t) / (t2 - t1), (t - t1) / (t2 - t1));
            var a3 = Comb(p2, p3, (t3 - t) / (t3 - t2), (t - t2) / (t3 - t2));
            var b1 = Comb(a1, a2, (t2 - t) / (t2 - t0), (t - t0) / (t2 - t0));
            var b2 = Comb(a2, a3, (t3 - t) / (t3 - t1), (t - t1) / (t3 - t1));
            return Comb(b1, b2, (t2 - t) / (t2 - t1), (t - t1) / (t2 - t1));
        }

        private static void Spline(List<V> dst, List<V> p, double step)
        {
            var m = p.Count - 1;
            for (var i = 0; i < m; i++)
            {
                V p1 = p[i], p2 = p[i + 1];
                var p0 = i > 0 ? p[i - 1] : p1 * 2 - p2;
                var p3 = i + 2 <= m ? p[i + 2] : p2 * 2 - p1;
                var k = Math.Max(1, (int)Math.Ceiling(V.Dist(p1, p2) / step));
                for (var j = 0; j < k; j++) dst.Add(k == 1 ? p1 : CatmullRom(p0, p1, p2, p3, (double)j / k));
            }
        }

        // rút gọn bằng Douglas-Peucker rồi nối bằng spline (hoặc khớp Bézier); thêm mọi điểm trừ điểm cuối
        private static void AddPiece(List<V> dst, List<V> pts, double eps, double step, double fitTol, bool closedPiece)
        {
            if (fitTol > 0 && step > 0 && pts.Count >= 3) { FitPiece(dst, pts, fitTol, step, closedPiece); return; }
            var key = Simplify(pts, eps);
            if (step <= 0) { for (var i = 0; i < key.Count - 1; i++) dst.Add(key[i]); return; }
            Spline(dst, key, step);
        }

        /// <summary>Vòng kín các điểm nguyên → điểm đã làm mượt.</summary>
        public static List<V> SmoothLoop(IList<P> loop, double sigma, Options opt)
        {
            opt ??= new Options();
            var floatLoop = new List<V>(loop.Count);
            foreach (var p in loop) floatLoop.Add(p.ToV());
            if (sigma <= 0 || loop.Count < 4) return floatLoop;
            var q = Densify(floatLoop);
            if (q.Count < 8) return floatLoop;
            var corners = FindCorners(q, Math.Min(2, sigma), opt.CornerDeg);
            var result = new List<V>();
            if (corners.Count == 0)
            {
                var s = GaussCircular(q, FitSigma(sigma, q.Count));
                var closed = new List<V>(s) { s[0] };
                AddPiece(result, closed, opt.Epsilon, opt.Step, opt.FitTol, true);
            }
            else
            {
                for (var i = 0; i < corners.Count; i++)
                {
                    var seg = Segment(q, corners[i], corners[(i + 1) % corners.Count], corners.Count == 1);
                    AddPiece(result, opt.Flow ? FlowSmooth(seg, sigma) : GaussOpen(seg, FitSigma(sigma, seg.Count)), opt.Epsilon, opt.Step, opt.FitTol, false);
                }
            }
            return result.Count >= 3 ? result : floatLoop;
        }

        /// <summary>Đường hở từ nút tới nút: hai đầu cố định; trả các điểm từ đầu tới cuối (gồm cả hai).</summary>
        public static List<V> SmoothArc(IList<P> arcPts, double sigma, Options opt)
        {
            opt ??= new Options();
            var arcV = new List<V>(arcPts.Count);
            foreach (var p in arcPts) arcV.Add(p.ToV());
            return SmoothArc(arcV, sigma, opt);
        }

        public static List<V> SmoothArc(List<V> arcPts, double sigma, Options opt)
        {
            opt ??= new Options();
            var q = DensifyOpen(arcPts);
            if (sigma <= 0 || q.Count < 8) return ToFloat(arcPts);
            var q0 = GaussOpen(q, Math.Min(2, sigma));
            var n = q.Count;
            var a = Arm(n, 4);
            var ang = new double[n];
            for (var i = a; i < n - a; i++) ang[i] = V.Angle(q0[i] - q0[i - a], q0[i + a] - q0[i]);
            var cuts = new List<int> { 0 };
            for (var i = a; i < n - a; i++)
            {
                if (ang[i] <= opt.CornerDeg) continue;
                var isMax = true;
                for (var j = 1; j <= a && isMax; j++) isMax = ang[i] > ang[i - j] && ang[i] >= ang[i + j];
                if (isMax) cuts.Add(i);
            }
            cuts.Add(n - 1);
            var result = new List<V>();
            for (var c = 0; c + 1 < cuts.Count; c++)
            {
                var seg = q.GetRange(cuts[c], cuts[c + 1] + 1 - cuts[c]);
                AddPiece(result, opt.Flow ? FlowSmooth(seg, sigma) : GaussOpen(seg, FitSigma(sigma, seg.Count)), opt.Epsilon, opt.Step, opt.FitTol, false);
            }
            result.Add(q[n - 1]);
            return result;
        }
    }
}
