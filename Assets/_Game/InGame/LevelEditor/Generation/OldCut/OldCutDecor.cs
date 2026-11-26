using System;
using System.Collections.Generic;

namespace Falcon.InGame.LevelEditor.Gen
{
    /// <summary>Nét trang trí của bộ cắt cũ: râu/gân (decorShapes) và vết đen (markShapes), port từ oldcut_gen.js.</summary>
    public static partial class OldCutGenerate
    {
        private const int Arm = 16, ArmBird = 48, ArmErode = 1, ArmClose = 4, MarkRing = 4, LooseArea = 150, LooseReach = 5;
        private const double BorderPx = 5.75, TipEps = 0.3;

        // vết đen: đảo + nét dính vào nó, tràn qua đúng các điểm deco
        public static byte[] MarkPixels(byte[] isl, byte[] deco, int W, byte[] work)
        {
            var o = new byte[W * W];
            var st = new Stack<int>();
            var d2 = OldCutImage.Edt(isl, W, W).D2;
            double r2 = MarkRing * MarkRing;
            for (var i = 0; i < o.Length; i++)
                if (isl[i] != 0 || (d2[i] <= r2 && Math.Max(work[i * 3], Math.Max(work[i * 3 + 1], work[i * 3 + 2])) < IslandDark)) { o[i] = 1; st.Push(i); }
            while (st.Count > 0)
            {
                var i = st.Pop();
                int x = i % W, y = i / W;
                for (var k = 0; k < 4; k++)
                {
                    var q = k == 0 ? (x > 0 ? i - 1 : -1) : k == 1 ? (x < W - 1 ? i + 1 : -1) : k == 2 ? (y > 0 ? i - W : -1) : (y < W - 1 ? i + W : -1);
                    if (q >= 0 && o[q] == 0 && deco[q] != 0) { o[q] = 1; st.Push(q); }
                }
            }
            return o;
        }

        // thành phần liên thông của mask: chạm nét deco là chim, còn lại là cục mực chỗ nét gặp nhau
        public static (byte[] birds, byte[] masses) SplitMarks(byte[] all, byte[] deco, int W, byte[] dark)
        {
            var n = W * W;
            var seen = new byte[n];
            var birds = new byte[n];
            var masses = new byte[n];
            var st = new Stack<int>();
            for (var i = 0; i < n; i++)
            {
                if (all[i] == 0 || seen[i] != 0) continue;
                var comp = new List<int>();
                var wings = false;
                st.Push(i); seen[i] = 1;
                while (st.Count > 0)
                {
                    var q = st.Pop();
                    comp.Add(q);
                    if (deco[q] != 0) wings = true;
                    int x = q % W, y = q / W;
                    for (var k = 0; k < 4; k++)
                    {
                        var m = k == 0 ? (x > 0 ? q - 1 : -1) : k == 1 ? (x < W - 1 ? q + 1 : -1) : k == 2 ? (y > 0 ? q - W : -1) : (y < W - 1 ? q + W : -1);
                        if (m >= 0 && all[m] != 0 && seen[m] == 0) { seen[m] = 1; st.Push(m); }
                    }
                }
                if (!wings && comp.Count < LooseArea && !TouchesInk(comp, all, dark, W)) continue;
                var target = wings ? birds : masses;
                foreach (var c in comp) target[c] = 1;
            }
            return (birds, masses);
        }

        private static bool TouchesInk(List<int> comp, byte[] all, byte[] dark, int W)
        {
            int x0 = W, y0 = W, x1 = 0, y1 = 0;
            foreach (var c in comp)
            {
                int x = c % W, y = c / W;
                if (x < x0) x0 = x;
                if (x > x1) x1 = x;
                if (y < y0) y0 = y;
                if (y > y1) y1 = y;
            }
            for (var y = Math.Max(0, y0 - LooseReach); y <= Math.Min(W - 1, y1 + LooseReach); y++)
                for (var x = Math.Max(0, x0 - LooseReach); x <= Math.Min(W - 1, x1 + LooseReach); x++)
                    if (dark[y * W + x] != 0 && all[y * W + x] == 0) return true;
            return false;
        }

        // khoảng cách có dấu tới mép mask (dương bên trong), làm mờ 3x3
        private static float[] MaskDistance(byte[] mask, int W)
        {
            var n = W * W;
            var inv = new byte[n];
            for (var i = 0; i < n; i++) inv[i] = (byte)(1 - mask[i]);
            var dIn = OldCutImage.Edt(inv, W, W).D2;
            var dOut = OldCutImage.Edt(mask, W, W).D2;
            var sd = new float[n];
            var o = new float[n];
            for (var i = 0; i < n; i++) sd[i] = mask[i] != 0 ? (float)(Math.Sqrt(dIn[i]) - 0.5) : (float)-(Math.Sqrt(dOut[i]) - 0.5);
            for (var y = 0; y < W; y++)
                for (var x = 0; x < W; x++)
                {
                    double s = 0, c = 0;
                    for (var dy = -1; dy <= 1; dy++)
                        for (var dx = -1; dx <= 1; dx++)
                        {
                            int X = x + dx, Y = y + dy;
                            if (X >= 0 && Y >= 0 && X < W && Y < W) { var wt = (dx != 0 ? 1 : 2) * (dy != 0 ? 1 : 2); s += wt * sd[Y * W + X]; c += wt; }
                        }
                    o[y * W + x] = (float)(s / c);
                }
            return o;
        }

        // đóng hình thái mask bằng đĩa bán kính r
        public static byte[] CloseMask(byte[] m, int W, double r)
        {
            var n = W * W;
            var d = OldCutImage.Edt(m, W, W).D2;
            var grown = new byte[n];
            var inv = new byte[n];
            for (var i = 0; i < n; i++) { grown[i] = d[i] <= r * r ? (byte)1 : (byte)0; inv[i] = (byte)(1 - grown[i]); }
            var e = OldCutImage.Edt(inv, W, W).D2;
            var o = new byte[n];
            for (var i = 0; i < n; i++) o[i] = grown[i] != 0 && e[i] > r * r ? (byte)1 : (byte)0;
            return o;
        }

        // marching squares trên toàn ảnh, vòng kín theo toạ độ work; phía mực cố định
        private static List<List<double[]>> InkLoops(float[] sd, float[] adj, int W)
        {
            var v = new float[W * W];
            for (var i = 0; i < v.Length; i++) v[i] = (float)(-sd[i] + adj[i]);
            var segs = new Dictionary<long, (long to, double fx, double fy)>();
            var order = new List<long>();
            long Key(int kind, int a, int b) => ((long)b * (W + 1) + a) * 2 + kind;
            double Lerp(double va, double vb) => (0 - va) / (vb - va);
            for (var y = 0; y + 1 < W; y++)
                for (var x = 0; x + 1 < W; x++)
                {
                    double tl = v[y * W + x], tr = v[y * W + x + 1], br = v[(y + 1) * W + x + 1], bl = v[(y + 1) * W + x];
                    var c = (tl < 0 ? 8 : 0) | (tr < 0 ? 4 : 0) | (br < 0 ? 2 : 0) | (bl < 0 ? 1 : 0);
                    if (c == 0 || c == 15) continue;
                    // cạnh: 0 trên, 1 phải, 2 dưới, 3 trái
                    long[] ek = { Key(0, x, y), Key(1, x + 1, y), Key(0, x, y + 1), Key(1, x, y) };
                    double[][] ep = { new[] { x + Lerp(tl, tr), (double)y }, new[] { x + 1.0, y + Lerp(tr, br) }, new[] { x + Lerp(bl, br), y + 1.0 }, new[] { (double)x, y + Lerp(tl, bl) } };
                    int[] pr;
                    switch (c)
                    {
                        case 1: pr = new[] { 3, 2 }; break;
                        case 2: pr = new[] { 2, 1 }; break;
                        case 3: pr = new[] { 3, 1 }; break;
                        case 4: pr = new[] { 1, 0 }; break;
                        case 5: pr = new[] { 1, 0, 3, 2 }; break;
                        case 6: pr = new[] { 2, 0 }; break;
                        case 7: pr = new[] { 3, 0 }; break;
                        case 8: pr = new[] { 0, 3 }; break;
                        case 9: pr = new[] { 0, 2 }; break;
                        case 10: pr = new[] { 0, 3, 2, 1 }; break;
                        case 11: pr = new[] { 0, 1 }; break;
                        case 12: pr = new[] { 1, 3 }; break;
                        case 13: pr = new[] { 1, 2 }; break;
                        default: pr = new[] { 2, 3 }; break;
                    }
                    for (var q = 0; q < pr.Length; q += 2)
                    {
                        var k = ek[pr[q]];
                        if (!segs.ContainsKey(k)) order.Add(k);
                        segs[k] = (ek[pr[q + 1]], ep[pr[q]][0], ep[pr[q]][1]);
                    }
                }
            var loops = new List<List<double[]>>();
            var used = new HashSet<long>();
            foreach (var k0 in order)
            {
                if (used.Contains(k0)) continue;
                var loop = new List<double[]>();
                var k = k0;
                var guard = 0;
                while (segs.ContainsKey(k) && !used.Contains(k) && guard++ < 100000)
                {
                    var s = segs[k];
                    used.Add(k);
                    loop.Add(new[] { s.fx + 0.5, s.fy + 0.5 });
                    k = s.to;
                }
                if (k == k0 && loop.Count >= 3) loops.Add(loop);
            }
            return loops;
        }

        // vết đen thành hình kín bám theo mask, toạ độ lưới tranh
        public static List<int[]> MarkShapes(byte[] mask, int W, byte[] dark, int arm)
        {
            var k = (double)Side / W;
            var near = OldCutImage.Edt(mask, W, W).D2;
            var grown = new byte[W * W];
            for (var i = 0; i < grown.Length; i++) grown[i] = mask[i] != 0 || (dark[i] != 0 && near[i] <= arm * arm) ? (byte)1 : (byte)0;
            grown = CloseMask(grown, W, ArmClose);
            var field = MaskDistance(grown, W);
            var adj = new float[W * W];
            for (var i = 0; i < adj.Length; i++)
            {
                var t = Math.Min(1, Math.Sqrt(near[i]) / arm);
                adj[i] = (float)(ArmErode * t * t * (3 - 2 * t));
            }
            var shapes = new List<int[]>();
            foreach (var loop in InkLoops(field, adj, W))
            {
                var pts = new List<Pt>();
                foreach (var q in loop) pts.Add(new Pt(q[0], q[1]));
                var sp = OldCutVector.Simplify(pts, (float)TipEps, true);
                if (sp.Count < 3) continue;
                var flat = new int[sp.Count * 2];
                for (var i = 0; i < sp.Count; i++) { flat[i * 2] = (int)Ops.JsRound(sp[i].X * k); flat[i * 2 + 1] = (int)Ops.JsRound(sp[i].Y * k); }
                shapes.Add(flat);
            }
            return shapes;
        }

        private static void SmoothChains(Topo t, double sigma)
        {
            if (sigma <= 0) return;
            var r = Math.Max(1, (int)Math.Ceiling(sigma * 3));
            var ker = new float[2 * r + 1];
            double sum = 0;
            for (var i = -r; i <= r; i++) { ker[i + r] = (float)Math.Exp(-i * i / (2 * sigma * sigma)); sum += ker[i + r]; }
            for (var i = 0; i < ker.Length; i++) ker[i] = (float)(ker[i] / sum);
            foreach (var c in t.Chains)
            {
                var n = c.Pts.Count;
                if (n < 4) continue;
                var inp = c.Pts;
                var o = new List<Pt>(inp);
                if (c.Closed)
                {
                    var m = n - 1;
                    for (var i = 0; i < m; i++)
                    {
                        double sx = 0, sy = 0;
                        for (var j = -r; j <= r; j++)
                        {
                            var p = inp[(((i + j) % m) + m) % m];
                            sx += (float)(ker[j + r] * p.X); sy += (float)(ker[j + r] * p.Y);
                        }
                        o[i] = OldCutVector.P(sx, sy);
                    }
                    o[n - 1] = o[0];
                }
                else
                {
                    Pt Get(int ix)
                    {
                        if (ix < 0) { var j1 = Math.Min(-ix, n - 1); return OldCutVector.P((float)(2 * inp[0].X) - inp[j1].X, (float)(2 * inp[0].Y) - inp[j1].Y); }
                        if (ix >= n) { var j2 = Math.Max(2 * (n - 1) - ix, 0); return OldCutVector.P((float)(2 * inp[n - 1].X) - inp[j2].X, (float)(2 * inp[n - 1].Y) - inp[j2].Y); }
                        return inp[ix];
                    }
                    for (var i = 1; i < n - 1; i++)
                    {
                        double sx = 0, sy = 0;
                        for (var j = -r; j <= r; j++)
                        {
                            var p = Get(i + j);
                            sx += (float)(ker[j + r] * p.X); sy += (float)(ker[j + r] * p.Y);
                        }
                        o[i] = OldCutVector.P(sx, sy);
                    }
                }
                c.Pts = o;
            }
        }

        // mask deco thành vòng kín: nở tới bề dày nét, làm mượt rồi khớp đường cong
        public static List<List<Pt>> DecoLoops(byte[] deco, int W, double lineW, double size, double linePx, double smooth)
        {
            var labPx = linePx * W / size;
            var delta = lineW > 0 ? Ops.Clamp(0.45 * (labPx - lineW), -2.5, 3.0) : 0.0;
            var m = deco;
            if (Math.Abs(delta) >= 0.4)
            {
                var o = new byte[deco.Length];
                if (delta > 0)
                {
                    var d2 = OldCutImage.Edt(deco, W, W).D2;
                    for (var i = 0; i < o.Length; i++) o[i] = d2[i] <= delta * delta ? (byte)1 : (byte)0;
                }
                else
                {
                    var inv = new byte[deco.Length];
                    for (var i = 0; i < inv.Length; i++) inv[i] = deco[i] != 0 ? (byte)0 : (byte)1;
                    var d3 = OldCutImage.Edt(inv, W, W).D2;
                    for (var i = 0; i < o.Length; i++) o[i] = deco[i] != 0 && d3[i] > delta * delta ? (byte)1 : (byte)0;
                }
                m = o;
            }
            var bl = new int[m.Length];
            for (var i = 0; i < bl.Length; i++) bl[i] = m[i];
            var t = OldCutVector.TraceBoundaries(bl, W, W);
            SmoothChains(t, smooth * 0.35);
            var tol = (float)(smooth * 0.18);
            foreach (var c in t.Chains) OldCutVector.FitChain(c, tol, 50);
            return OldCutVector.PieceLoops(t, 1);
        }

        // nét trang trí (râu, gân, cỏ) thành hình kín trên lưới tranh
        public static List<int[]> DecorShapes(byte[] deco, int W, double lineW)
        {
            var k = (double)Side / W;
            var eps = (float)(0.15 * W / OutSize);
            var shapes = new List<int[]>();
            foreach (var loop in DecoLoops(deco, W, lineW, OutSize, BorderPx, Smooth))
            {
                var sp = OldCutVector.Simplify(loop, eps, true);
                var pts = new List<IPt>();
                foreach (var p in sp) pts.Add(new IPt { X = (int)Math.Min(Side, Math.Max(0, Ops.JsRound(p.X * k))), Y = (int)Math.Min(Side, Math.Max(0, Ops.JsRound(p.Y * k))) });
                pts = Dedup(pts);
                if (pts.Count < 3) continue;
                var flat = new int[pts.Count * 2];
                for (var i = 0; i < pts.Count; i++) { flat[i * 2] = pts[i].X; flat[i * 2 + 1] = pts[i].Y; }
                shapes.Add(flat);
            }
            return shapes;
        }

        private static int[] Reverse(int[] d)
        {
            var o = new int[d.Length];
            for (int i = d.Length - 2, j = 0; i >= 0; i -= 2, j += 2) { o[j] = d[i]; o[j + 1] = d[i + 1]; }
            return o;
        }

        // hình deco không chạm biên mảnh nào (đồng tử, khớp hai nét) thì true
        private static bool ClearOfBorders(int[] shape, List<List<OldCutPart>> rings, int W)
        {
            double near = 4.0 * Side / W;
            foreach (var parts in rings)
                foreach (var part in parts)
                {
                    var rs = new List<int[]> { part.Points };
                    if (part.Holes != null) rs.AddRange(part.Holes);
                    foreach (var r in rs)
                    {
                        var n = r.Length / 2;
                        for (var e = 0; e < n; e++)
                        {
                            double ax = r[e * 2], ay = r[e * 2 + 1], bx = r[(e + 1) % n * 2], by = r[(e + 1) % n * 2 + 1], dx = bx - ax, dy = by - ay, l2 = dx * dx + dy * dy;
                            for (var i = 0; i < shape.Length; i += 2)
                            {
                                var t = l2 != 0 ? Math.Max(0, Math.Min(1, ((shape[i] - ax) * dx + (shape[i + 1] - ay) * dy) / l2)) : 0;
                                if (Hyp(shape[i] - ax - t * dx, shape[i + 1] - ay - t * dy) < near) return false;
                            }
                        }
                    }
                }
            return true;
        }

        /// <summary>Toàn bộ decor: nét + vết đen (lọc sát biên, mảnh nhỏ, cùng chiều quay).</summary>
        public static List<int[]> BuildDecor(OldCutSeg seg, byte[] birds, byte[] masses, List<List<OldCutPart>> rings)
        {
            var W = seg.W;
            var strokes = seg.Deco != null ? DecorShapes(seg.Deco, W, seg.LineW) : new List<int[]>();
            double refA = 0, refS = 0;
            foreach (var d in strokes) { var a = RingArea(d); if (Math.Abs(a) > refA) { refA = Math.Abs(a); refS = Math.Sign(a); } }
            var blobs = new List<int[]>();
            var dark = DarkOf(seg);
            if (birds != null) blobs.AddRange(MarkShapes(birds, W, dark, ArmBird));
            if (masses != null) foreach (var m in MarkShapes(masses, W, dark, Arm)) if (ClearOfBorders(m, rings, W)) blobs.Add(m);
            var minA = 12.0 * ((double)Side / W) * ((double)Side / W);
            blobs = blobs.FindAll(m => Math.Abs(RingArea(m)) >= minA);
            double big = 0, bigA = 0;
            foreach (var d in blobs) { var a = RingArea(d); if (Math.Abs(a) > bigA) { bigA = Math.Abs(a); big = Math.Sign(a); } }
            if (refS != 0 && big != 0 && big != refS) blobs = blobs.ConvertAll(Reverse);
            strokes.AddRange(blobs);
            return strokes;
        }
    }
}
