using System;
using System.Collections.Generic;
using System.Linq;

namespace Falcon.InGame.LevelEditor.Gen
{
    /// <summary>Cài đặt dựng đường viền khi sửa tranh (lấy từ GenSettings của tranh).</summary>
    public sealed class ShapeSettings
    {
        public double CurveSmooth = 3, FitTolerance = 1.6;
        public int SmoothScale = 3;
    }

    /// <summary>Mảnh quá bé để chứa số: bán kính vòng tròn lớn nhất bên trong nhỏ hơn ngưỡng; Target là mảnh kề có biên chung dài nhất (-1 nếu không có).</summary>
    public sealed class SmallPiece
    {
        public int Index, Area, Target;
        public double Radius;
    }

    /// <summary>Thao tác sửa tranh dựa trên bản đồ pixel (port từ PictureModel của Studio): gộp mảnh, tìm mảnh quá bé.</summary>
    public static class PictureOps
    {
        private const int TidyStep = 6, CropMargin = 4;
        private const double UnitEps = 0.75, SigmaPerLevel = 0.57, SmallRadius = 0.012, CutOverreach = 3;

        private static double SigmaOf(ShapeSettings g) => g.CurveSmooth * SigmaPerLevel * Math.Max(1, g.SmoothScale);

        // ------------------------------------------------------------------ viền pixel → đa giác
        /// <summary>Viền ngoài của một mảnh thành vòng các góc pixel (mảnh nằm bên trái), chỉ giữ chỗ rẽ.</summary>
        public static List<P> TraceOuter(int[] reg, int w, int h, int id, int bx0, int by0, int bx1, int by1)
        {
            int x0 = bx0, y0 = by0, x1 = bx1 + 1, y1 = by1 + 1, start = -1;
            for (var y = y0; y < y1 && start < 0; y++)
                for (var x = x0; x < x1; x++)
                    if (reg[y * w + x] == id) { start = y * w + x; break; }
            if (start < 0) return new List<P>();

            bool Inside(int px, int py) => px >= 0 && py >= 0 && px < w && py < h && reg[py * w + px] == id;
            var next = new Dictionary<long, List<(int x, int y)>>();
            void Add(int ax, int ay, int bx, int by)
            {
                var k = (long)ax * 100003 + ay;
                if (!next.TryGetValue(k, out var l)) next[k] = l = new List<(int, int)>();
                l.Add((bx, by));
            }
            for (var y = y0; y < y1; y++)
                for (var x = x0; x < x1; x++)
                {
                    if (reg[y * w + x] != id) continue;
                    if (!Inside(x, y - 1)) Add(x, y, x + 1, y);
                    if (!Inside(x + 1, y)) Add(x + 1, y, x + 1, y + 1);
                    if (!Inside(x, y + 1)) Add(x + 1, y + 1, x, y + 1);
                    if (!Inside(x - 1, y)) Add(x, y + 1, x, y);
                }

            int ox = start % w, oy = start / w, cx = ox, cy = oy, dx = 0, dy = 0;
            var loop = new List<P>();
            do
            {
                var outs = next[(long)cx * 100003 + cy];
                var pick = 0;
                if (outs.Count > 1)
                {
                    int lx = -dy, ly = dx; // chạm chéo: rẽ trái để hai pixel chéo tách rời
                    for (var i = 0; i < outs.Count; i++) if (outs[i].x - cx == lx && outs[i].y - cy == ly) pick = i;
                }
                var nx = outs[pick];
                outs.RemoveAt(pick);
                loop.Add(new P(cx, cy));
                dx = nx.x - cx;
                dy = nx.y - cy;
                cx = nx.x;
                cy = nx.y;
            } while ((cx != ox || cy != oy) && loop.Count <= 4 * reg.Length);

            var res = new List<P>();
            for (var j = 0; j < loop.Count; j++)
            {
                var prev = loop[(j + loop.Count - 1) % loop.Count];
                var c = loop[j];
                var nxt = loop[(j + 1) % loop.Count];
                if (c.X - prev.X != nxt.X - c.X || c.Y - prev.Y != nxt.Y - c.Y) res.Add(c);
            }
            return res;
        }

        private static double DistToSeg(P p, P a, P b)
        {
            double abx = b.X - a.X, aby = b.Y - a.Y, l2 = abx * abx + aby * aby;
            var t = l2 < 1e-9 ? 0 : Math.Max(0, Math.Min(1, ((p.X - a.X) * abx + (p.Y - a.Y) * aby) / l2));
            return Math.Sqrt(Math.Pow(p.X - (a.X + abx * t), 2) + Math.Pow(p.Y - (a.Y + aby * t), 2));
        }

        private static void Dp(IList<P> p, int i0, int i1, double eps, bool[] keep)
        {
            if (i1 - i0 < 2) return;
            P a = p[i0], b = p[i1 % p.Count];
            int idx = -1;
            double dmax = 0;
            for (var k = i0 + 1; k < i1; k++)
            {
                var d = DistToSeg(p[k], a, b);
                if (d > dmax) { dmax = d; idx = k; }
            }
            if (dmax <= eps) return;
            keep[idx] = true;
            Dp(p, i0, idx, eps, keep);
            Dp(p, idx, i1, eps, keep);
        }

        private static List<P> SimplifyLoop(List<P> poly, double eps)
        {
            var n = poly.Count;
            if (n <= 4) return new List<P>(poly);
            int far = 0;
            double best = -1;
            for (var i = 1; i < n; i++)
            {
                double dx = poly[i].X - poly[0].X, dy = poly[i].Y - poly[0].Y, d = dx * dx + dy * dy;
                if (d > best) { best = d; far = i; }
            }
            var keep = new bool[n];
            keep[0] = keep[far] = true;
            Dp(poly, 0, far, eps, keep);
            Dp(poly, far, n, eps, keep);
            var res = new List<P>();
            for (var i = 0; i < n; i++) if (keep[i]) res.Add(poly[i]);
            return res;
        }

        // Đa giác lưu trong tranh (số nguyên x unit); null khi suy biến
        private static int[] PolygonOf(List<P> outer, ShapeSettings g, int unit)
        {
            if (outer.Count < 3) return null;
            var poly = new List<(int x, int y)>();
            List<V> src;
            if (g.CurveSmooth > 0)
                src = Contour.SmoothLoop(outer, SigmaOf(g), new Contour.Options { Step = unit > 1 ? TidyStep : 0, FitTol = unit > 1 ? g.FitTolerance : 0 });
            else
                src = SimplifyLoop(outer, UnitEps).Select(v => new V(v.X, v.Y)).ToList();
            foreach (var v in src)
            {
                var r = ((int)Ops.JsRound(v.X * unit), (int)Ops.JsRound(v.Y * unit));
                if (poly.Count == 0 || poly[poly.Count - 1] != r) poly.Add(r);
            }
            if (poly.Count > 1 && poly[0] == poly[poly.Count - 1]) poly.RemoveAt(poly.Count - 1);
            if (poly.Count < 3) return null;
            long area2 = 0;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++) area2 += (long)poly[j].x * poly[i].y - (long)poly[i].x * poly[j].y;
            if (Math.Abs(area2) < 2L * unit * unit) return null;
            var pts = new int[poly.Count * 2];
            for (var k = 0; k < poly.Count; k++) { pts[k * 2] = poly[k].x; pts[k * 2 + 1] = poly[k].y; }
            return pts;
        }

        // ------------------------------------------------------------------ mô hình làm việc: bản đồ pixel + danh sách mảnh
        private sealed class Model
        {
            private readonly int _w, _h, _unit;
            private readonly ShapeSettings _g;
            public readonly List<GenRegion> Regions;
            public readonly RegionMap Map;

            public Model(int w, int h, int unit, ShapeSettings g, List<GenRegion> regions)
            {
                _w = w;
                _h = h;
                _unit = unit;
                _g = g;
                Regions = regions;
                Map = Rasterizer.Rasterize(w, h, unit, regions);
                _minPiece = Math.Max(4, (int)Math.Truncate((double)w * h / 10000));
            }

            private bool Touches(List<int> pixels, int id)
            {
                var reg = Map.Reg;
                foreach (var p in pixels)
                {
                    int x = p % _w, y = p / _w;
                    if ((x > 0 && reg[p - 1] == id) || (x < _w - 1 && reg[p + 1] == id) || (y > 0 && reg[p - _w] == id) || (y < _h - 1 && reg[p + _w] == id)) return true;
                }
                return false;
            }

            private int[] Trace(int id)
            {
                var b = RegionOps.BoundsOf(Map);
                var outer = TraceOuter(Map.Reg, _w, _h, id, b.X0[id], b.Y0[id], b.X1[id], b.Y1[id]);
                var pts = PolygonOf(outer, _g, _unit);
                if (pts != null) return pts;
                var poly = SimplifyLoop(outer, UnitEps); // suy biến sau khi làm mượt: dùng viền thô
                pts = new int[poly.Count * 2];
                for (var i = 0; i < poly.Count; i++) { pts[i * 2] = poly[i].X * _unit; pts[i * 2 + 1] = poly[i].Y * _unit; }
                return pts;
            }

            // Gộp other vào target; trả chỉ số mới của mảnh gộp, -1 kèm error
            public int Merge(int target, int other, out string error)
            {
                error = null;
                if (target < 0 || other < 0 || target == other) { error = "Chọn 2 mảnh khác nhau"; return -1; }
                int tid = target + 1, oid = other + 1;
                var otherPx = new List<int>();
                for (var p = 0; p < Map.Reg.Length; p++) if (Map.Reg[p] == oid) otherPx.Add(p);
                if (!Touches(otherPx, tid)) { error = "Hai mảnh không kề nhau"; return -1; }
                foreach (var p in otherPx) Map.Reg[p] = tid;
                Regions[target].Points = Trace(tid);

                Regions.RemoveAt(other); // removeRegion: bỏ mảnh, dồn id
                Map.Colors.RemoveAt(oid);
                for (var p = 0; p < Map.Reg.Length; p++) if (Map.Reg[p] > oid) Map.Reg[p]--;

                var merged = other < target ? target - 1 : target;
                Rebuild(new List<int> { merged + 1 });
                return merged;
            }

            private readonly int _minPiece;

            private List<int> PixelsOf(int id)
            {
                var list = new List<int>();
                for (var p = 0; p < Map.Reg.Length; p++) if (Map.Reg[p] == id) list.Add(p);
                return list;
            }

            // Các phần liên thông (4 hướng) của nhãn label trong pixels, theo thứ tự gặp
            private List<List<int>> Components(List<int> pixels, int label)
            {
                var reg = Map.Reg;
                var seen = new HashSet<int>();
                var result = new List<List<int>>();
                foreach (var seed in pixels)
                {
                    if (!seen.Add(seed)) continue;
                    var piece = new List<int> { seed };
                    var st = new Stack<int>();
                    st.Push(seed);
                    while (st.Count > 0)
                    {
                        var p = st.Pop();
                        int x = p % _w, y = p / _w;
                        void Take(int q)
                        {
                            if (reg[q] == label && seen.Add(q)) { piece.Add(q); st.Push(q); }
                        }
                        if (x > 0) Take(p - 1);
                        if (x < _w - 1) Take(p + 1);
                        if (y > 0) Take(p - _w);
                        if (y < _h - 1) Take(p + _w);
                    }
                    result.Add(piece);
                }
                return result;
            }

            private bool TouchesBorder(int id)
            {
                var reg = Map.Reg;
                for (var x = 0; x < _w; x++) if (reg[x] == id || reg[(_h - 1) * _w + x] == id) return true;
                for (var y = 0; y < _h; y++) if (reg[y * _w] == id || reg[y * _w + _w - 1] == id) return true;
                return false;
            }

            // Ô mới nhận màu kề từ bảng học: ô đầu giữ màu gốc, các ô sau tránh lặp màu ngay trước
            private void Recolor(int index, int firstNew)
            {
                var rnd = ColorAdjacency.Rng(index * 7919 + 17);
                var baseColor = Regions[index].ColorId;
                var baseLab = Palette.Lab[baseColor];
                var edge = TouchesBorder(index + 1);
                var prev = baseColor;
                for (var r = firstNew; r < Regions.Count; r++)
                {
                    var c = baseColor;
                    for (var t = 0; t < 12; t++)
                    {
                        var cand = ColorAdjacency.Pick(baseColor, prev, rnd);
                        var lc = Palette.Lab[cand];
                        var d = Math.Sqrt((lc[0] - baseLab[0]) * (lc[0] - baseLab[0]) + (lc[1] - baseLab[1]) * (lc[1] - baseLab[1]) + (lc[2] - baseLab[2]) * (lc[2] - baseLab[2]));
                        if (edge || d <= 38) { c = cand; break; }
                    }
                    Regions[r].ColorId = c;
                    Map.Colors[r + 1] = c;
                    prev = c;
                }
            }

            // Chia từng mảnh trong indices thành count ô theo kiểu pattern; trả số mảnh mới (0 kèm error)
            public int Subdivide(IReadOnlyList<int> indices, int count, int pattern, out string error)
            {
                var touched = new List<int>();
                var added = 0;
                foreach (var index in indices.Distinct().OrderBy(i => i))
                {
                    if (index < 0 || index >= Regions.Count) continue;
                    var id = index + 1;
                    var pix = PixelsOf(id);
                    var fa = Gen.Subdivide.FlowAssign(_w, _h, pix, count, pattern);
                    if (fa == null) continue;
                    var n = fa.Cells;
                    var groups = new List<int>[n];
                    for (var k = 0; k < n; k++) groups[k] = new List<int>();
                    for (var i = 0; i < pix.Count; i++) groups[fa.Cell[i]].Add(pix[i]);
                    for (var k = 0; k < n; k++) foreach (var q in groups[k]) Map.Reg[q] = -(k + 2);

                    var pieces = new List<List<int>>();
                    for (var k = 0; k < n; k++) pieces.AddRange(Components(groups[k], -(k + 2)));
                    for (var j = 0; j < pieces.Count; j++) foreach (var q in pieces[j]) Map.Reg[q] = -(j + 2);

                    // phần quá nhỏ nhập vào phần kề có biên chung dài nhất (nhỏ nhất trước)
                    var order = Enumerable.Range(0, pieces.Count).OrderBy(j => pieces[j].Count).ToList();
                    foreach (var j in order)
                    {
                        if (pieces[j].Count == 0 || pieces[j].Count >= _minPiece) continue;
                        var nearKeys = new List<int>();
                        var near = new Dictionary<int, int>();
                        foreach (var q in pieces[j])
                        {
                            int x = q % _w, y = q / _w;
                            void Look(int r)
                            {
                                var v = Map.Reg[r];
                                if (v >= -1 || v == -(j + 2)) return;
                                if (near.TryGetValue(v, out var c)) near[v] = c + 1;
                                else { near[v] = 1; nearKeys.Add(v); }
                            }
                            if (x > 0) Look(q - 1);
                            if (x < _w - 1) Look(q + 1);
                            if (y > 0) Look(q - _w);
                            if (y < _h - 1) Look(q + _w);
                        }
                        int? bestKey = null;
                        var bv = 0;
                        foreach (var nk in nearKeys) if (near[nk] > bv) { bv = near[nk]; bestKey = nk; }
                        if (bestKey == null) continue;
                        var to = -bestKey.Value - 2;
                        foreach (var q in pieces[j]) Map.Reg[q] = -(to + 2);
                        pieces[to].AddRange(pieces[j]);
                        pieces[j] = new List<int>();
                    }
                    pieces = pieces.Where(pc => pc.Count > 0).ToList();
                    if (pieces.Count < 2)
                    {
                        foreach (var q in pix) Map.Reg[q] = id;
                        continue;
                    }
                    var firstNew = Regions.Count;
                    added += Commit(index, id, pieces, touched);
                    Recolor(index, firstNew);
                }
                if (touched.Count == 0) { error = "Không chia được: mảnh quá nhỏ hoặc quá hẹp"; return 0; }
                error = null;
                Rebuild(touched, true);
                return added;
            }

            // Cắt mảnh index theo đường path (toạ độ ô lưới); trả số mảnh mới, 0 kèm error
            public int SplitPath(int index, IReadOnlyList<(double x, double y)> path, out string error)
            {
                error = null;
                int w = _w, h = _h, total = w * h, id = index + 1;
                if (index < 0 || index >= Regions.Count) { error = "Không có mảnh để cắt"; return 0; }
                if (path == null || path.Count < 2) { error = "Đường cắt quá ngắn"; return 0; }
                var pts = path.ToList();
                double Dist((double x, double y) a, (double x, double y) b) => Math.Sqrt((a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y));
                (double x, double y) Norm((double x, double y) a, (double x, double y) b)
                {
                    double dx = a.x - b.x, dy = a.y - b.y, l = Math.Sqrt(dx * dx + dy * dy);
                    return l < 1e-9 ? (0, 0) : (dx / l, dy / l);
                }
                var closed = pts.Count >= 3 && Dist(pts[0], pts[^1]) < 1.5;
                if (closed) pts.Add(pts[0]);
                else
                {
                    var d0 = Norm(pts[0], pts[Math.Min(3, pts.Count - 1)]);
                    var d1 = Norm(pts[^1], pts[Math.Max(0, pts.Count - 4)]);
                    var first = pts[0];
                    var last = pts[^1];
                    if (d0.x != 0 || d0.y != 0) pts.Insert(0, (first.x + d0.x * CutOverreach, first.y + d0.y * CutOverreach));
                    if (d1.x != 0 || d1.y != 0) pts.Add((last.x + d1.x * CutOverreach, last.y + d1.y * CutOverreach));
                }

                var reg = Map.Reg;
                var inRegion = new bool[total];
                var barrier = new bool[total];
                var barrierList = new List<int>();
                var barInfo = new Dictionary<int, double[]>();
                for (var i = 0; i < total; i++) inRegion[i] = reg[i] == id;
                for (var k = 0; k + 1 < pts.Count; k++)
                {
                    var a = pts[k];
                    var b = pts[k + 1];
                    var steps = Math.Max(1, (int)Math.Ceiling(Dist(a, b) * 2));
                    for (var t = 0; t <= steps; t++)
                    {
                        double qx = a.x + (b.x - a.x) * t / steps, qy = a.y + (b.y - a.y) * t / steps;
                        int x = (int)Math.Floor(qx), y = (int)Math.Floor(qy);
                        if (x < 0 || y < 0 || x >= w || y >= h) continue;
                        var pi = y * w + x;
                        if (inRegion[pi] && !barrier[pi]) { barrier[pi] = true; barrierList.Add(pi); barInfo[pi] = new[] { qx, qy, -(b.y - a.y), b.x - a.x }; }
                    }
                }
                if (barrierList.Count == 0) { error = "Đường cắt không đi qua mảnh"; return 0; }

                var comp = new int[total];
                Array.Fill(comp, -1);
                var comps = new List<List<int>>();
                var st = new Stack<int>();
                for (var seed = 0; seed < total; seed++)
                {
                    if (!inRegion[seed] || barrier[seed] || comp[seed] >= 0) continue;
                    var list = new List<int>();
                    comp[seed] = comps.Count;
                    st.Push(seed);
                    while (st.Count > 0)
                    {
                        var p = st.Pop();
                        list.Add(p);
                        int px = p % w, py = p / w;
                        void Visit(int n)
                        {
                            if (inRegion[n] && !barrier[n] && comp[n] < 0) { comp[n] = comps.Count; st.Push(n); }
                        }
                        if (px > 0) Visit(p - 1);
                        if (px < w - 1) Visit(p + 1);
                        if (py > 0) Visit(p - w);
                        if (py < h - 1) Visit(p + w);
                    }
                    comps.Add(list);
                }
                if (comps.Count < 2) { error = "Đường cắt chưa chia mảnh làm đôi (kéo xuyên qua mảnh)"; return 0; }

                var big = new List<List<int>>();
                var loose = new List<int>();
                foreach (var l in comps) { if (l.Count >= _minPiece) big.Add(l); else loose.AddRange(l); }
                if (big.Count < 2) { error = "Mảnh cắt ra quá nhỏ"; return 0; }
                comps = big;
                Array.Fill(comp, -1);
                for (var ci = 0; ci < comps.Count; ci++) foreach (var q in comps[ci]) comp[q] = ci;

                var rest = barrierList.Concat(loose).ToList();
                var pend = new bool[total];
                foreach (var q in rest) pend[q] = true;
                while (rest.Count > 0)
                {
                    var assign = new List<(int q, int c)>();
                    var still = new List<int>();
                    foreach (var q in rest)
                    {
                        int x = q % w, y = q / w;
                        var nbs = new List<(int c, int x, int y)>();
                        void Cand(int cx, int cy)
                        {
                            if (cx < 0 || cy < 0 || cx >= w || cy >= h) return;
                            var n2 = cy * w + cx;
                            if (inRegion[n2] && !pend[n2] && comp[n2] >= 0) nbs.Add((comp[n2], cx, cy));
                        }
                        Cand(x - 1, y); Cand(x + 1, y); Cand(x, y - 1); Cand(x, y + 1);
                        if (nbs.Count == 0) { still.Add(q); continue; }
                        var pick = nbs[0];
                        if (barInfo.TryGetValue(q, out var info) && nbs.Any(n3 => n3.c != pick.c))
                        {
                            var side = (x + 0.5 - info[0]) * info[2] + (y + 0.5 - info[1]) * info[3];
                            foreach (var n3 in nbs)
                                if (((n3.x + 0.5 - info[0]) * info[2] + (n3.y + 0.5 - info[1]) * info[3]) * side > 0) { pick = n3; break; }
                        }
                        else
                        {
                            var votes = new Dictionary<int, int>();
                            var bv = 0;
                            foreach (var n3 in nbs)
                            {
                                votes.TryGetValue(n3.c, out var v);
                                votes[n3.c] = ++v;
                                if (v > bv) { bv = v; pick = n3; }
                            }
                        }
                        assign.Add((q, pick.c));
                    }
                    if (assign.Count == 0) break;
                    foreach (var (q, c) in assign) { comp[q] = c; comps[c].Add(q); pend[q] = false; }
                    rest = still;
                }
                foreach (var q in rest) { comp[q] = 0; comps[0].Add(q); }

                var touched = new List<int>();
                var added = Commit(index, id, comps, touched);
                Rebuild(touched);
                return added;
            }

            // Cấp id cho các mảnh cắt ra từ index (mảnh đầu giữ id), dựng lại viền; trả số mảnh mới
            private int Commit(int index, int id, List<List<int>> pieces, List<int> touched)
            {
                var color = Regions[index].ColorId;
                var baseCount = Regions.Count;
                var ids = new int[pieces.Count];
                for (var k = 0; k < pieces.Count; k++)
                {
                    ids[k] = k == 0 ? id : baseCount + k;
                    foreach (var q in pieces[k]) Map.Reg[q] = ids[k];
                }
                for (var k = 1; k < pieces.Count; k++) Map.Colors.Add(color);
                var b = RegionOps.BoundsOf(Map);
                int[] TraceId(int rid)
                {
                    var outer = TraceOuter(Map.Reg, _w, _h, rid, b.X0[rid], b.Y0[rid], b.X1[rid], b.Y1[rid]);
                    var pts = PolygonOf(outer, _g, _unit);
                    if (pts != null) return pts;
                    var poly = SimplifyLoop(outer, UnitEps);
                    pts = new int[poly.Count * 2];
                    for (var i = 0; i < poly.Count; i++) { pts[i * 2] = poly[i].X * _unit; pts[i * 2 + 1] = poly[i].Y * _unit; }
                    return pts;
                }
                Regions[index].Points = TraceId(id);
                Regions[index].Holes = null;
                for (var k = 1; k < pieces.Count; k++)
                    Regions.Add(new GenRegion { Id = 0, Parent = Regions[index].Id, Points = TraceId(ids[k]), ColorId = color });
                touched.AddRange(ids);
                return pieces.Count - 1;
            }

            // Dựng lại biên chung của mảnh sửa và các mảnh kề cho khớp; mảnh khác giữ nguyên
            private void Rebuild(List<int> edited, bool flow = false)
            {
                if (_unit <= 1 || _g.CurveSmooth <= 0) return;
                var affected = new HashSet<int>(edited);
                var reg = Map.Reg;
                foreach (var id in edited)
                    for (var p = 0; p < reg.Length; p++)
                    {
                        if (reg[p] != id) continue;
                        int x = p % _w, y = p / _w;
                        if (x > 0 && reg[p - 1] > 0) affected.Add(reg[p - 1]);
                        if (x < _w - 1 && reg[p + 1] > 0) affected.Add(reg[p + 1]);
                        if (y > 0 && reg[p - _w] > 0) affected.Add(reg[p - _w]);
                        if (y < _h - 1 && reg[p + _w] > 0) affected.Add(reg[p + _w]);
                    }

                var all = RegionOps.BoundsOf(Map);
                int x0 = _w, y0 = _h, x1 = 0, y1 = 0;
                foreach (var id in affected)
                {
                    if (id < 1 || id >= all.X0.Length || all.X1[id] < 0) continue;
                    x0 = Math.Min(x0, all.X0[id]);
                    y0 = Math.Min(y0, all.Y0[id]);
                    x1 = Math.Max(x1, all.X1[id] + 1);
                    y1 = Math.Max(y1, all.Y1[id] + 1);
                }
                x0 = Math.Max(0, x0 - CropMargin);
                y0 = Math.Max(0, y0 - CropMargin);
                x1 = Math.Min(_w, x1 + CropMargin);
                y1 = Math.Min(_h, y1 + CropMargin);
                if (x1 <= x0 || y1 <= y0) return;

                int cw = x1 - x0, ch = y1 - y0;
                var crop = new RegionMap { W = cw, H = ch, Reg = new int[cw * ch], Colors = Map.Colors, Inked = Map.Inked, Gaps = Map.Gaps, Synthetic = Map.Synthetic };
                for (var y = 0; y < ch; y++) Array.Copy(reg, (y0 + y) * _w + x0, crop.Reg, y * cw, cw);
                var g2 = BoundaryGraph.Build(crop, SigmaOf(_g), _unit, TidyStep, _g.FitTolerance, flow);
                int ox = x0 * _unit, oy = y0 * _unit;
                int[] Shift(int[] a)
                {
                    var r = new int[a.Length];
                    for (var k = 0; k + 1 < a.Length; k += 2) { r[k] = a[k] + ox; r[k + 1] = a[k + 1] + oy; }
                    return r;
                }
                foreach (var id in affected)
                {
                    if (id < 1 || id >= g2.Polys.Length || id > Regions.Count || g2.Polys[id] == null) continue;
                    Regions[id - 1].Points = Shift(g2.Polys[id]);
                    Regions[id - 1].Holes = g2.Holes[id] != null ? g2.Holes[id].Select(Shift).ToList() : null;
                }
            }
        }

        // ------------------------------------------------------------------ gộp nhiều mảnh
        /// <summary>
        /// Gộp các mảnh indices vào mảnh đầu; mảnh chưa kề thì đợi tới khi kề. Trả danh sách mới (mảnh còn lại giữ thứ tự và Id), null kèm error nếu không gộp hết được.
        /// </summary>
        public static List<GenRegion> MergeMany(int width, int height, int unit, ShapeSettings g, IReadOnlyList<GenRegion> regions, IReadOnlyList<int> indices, out string error)
        {
            error = null;
            var cur = regions.Select(Clone).ToList();
            var target = indices[0];
            var rest = new List<int>();
            foreach (var i in indices.Skip(1)) if (i != target && !rest.Contains(i)) rest.Add(i);

            while (rest.Count > 0)
            {
                var done = false;
                for (var k = 0; k < rest.Count && !done; k++)
                {
                    var model = new Model(width, height, unit, g, cur.Select(Clone).ToList());
                    var other = rest[k];
                    var t = model.Merge(target, other, out var e);
                    if (t < 0) { error = e; continue; }
                    cur = model.Regions;
                    target = t;
                    rest.RemoveAt(k);
                    for (var j = 0; j < rest.Count; j++) if (rest[j] > other) rest[j]--;
                    done = true;
                }
                if (!done)
                {
                    error = "Các mảnh chọn không liền nhau, không gộp được hết" + (error != null ? " (" + error + ")" : "");
                    return null;
                }
            }
            error = null;
            return cur;
        }

        private static GenRegion Clone(GenRegion r) => new()
        {
            Id = r.Id,
            Parent = r.Parent,
            Points = (int[])r.Points.Clone(),
            ColorId = r.ColorId,
            Holes = r.Holes?.Select(h => (int[])h.Clone()).ToList(),
        };

        /// <summary>
        /// Chia các mảnh indices thành count ô theo kiểu pattern (chỉ số trong Subdivide.Patterns). Trả danh sách mới: mảnh cũ giữ Id, ô mới có Id = 0 và Parent là Id mảnh gốc; null kèm error nếu không chia được.
        /// </summary>
        public static List<GenRegion> SubdivideRegions(int width, int height, int unit, ShapeSettings g, IReadOnlyList<GenRegion> regions, IReadOnlyList<int> indices, int count, int pattern, out string error)
        {
            var model = new Model(width, height, unit, g, regions.Select(Clone).ToList());
            var added = model.Subdivide(indices, count, pattern, out error);
            return added > 0 ? model.Regions : null;
        }

        // ------------------------------------------------------------------ cắt theo đường
        /// <summary>Cắt mảnh index theo đường path (toạ độ ô lưới). Trả danh sách mới (mảnh mới Id = 0, Parent = Id mảnh gốc), null kèm error.</summary>
        public static List<GenRegion> SplitRegion(int width, int height, int unit, ShapeSettings g, IReadOnlyList<GenRegion> regions, int index, IReadOnlyList<(double x, double y)> path, out string error)
        {
            var model = new Model(width, height, unit, g, regions.Select(Clone).ToList());
            return model.SplitPath(index, path, out error) > 0 ? model.Regions : null;
        }

        /// <summary>Điểm gần nhất trên biên các mảnh (hoặc riêng region, -1 = tất cả) trong bán kính radius ô lưới; null nếu không có.</summary>
        public static (double x, double y)? NearestBoundaryPoint(IReadOnlyList<GenRegion> regions, int unit, double px, double py, double radius, int region = -1)
        {
            unit = Math.Max(1, unit);
            var bestD = radius;
            (double x, double y)? found = null;
            for (var ri = 0; ri < regions.Count; ri++)
            {
                if (region >= 0 && ri != region) continue;
                var pts = regions[ri].Points;
                int n = pts.Length / 2;
                for (var i = 0; i < n; i++)
                {
                    int j = (i + 1) % n;
                    double ax = pts[i * 2] / (double)unit, ay = pts[i * 2 + 1] / (double)unit, bx = pts[j * 2] / (double)unit, by = pts[j * 2 + 1] / (double)unit;
                    double abx = bx - ax, aby = by - ay, l2 = abx * abx + aby * aby;
                    var t = l2 < 1e-6 ? 0 : Math.Min(1, Math.Max(0, ((px - ax) * abx + (py - ay) * aby) / l2));
                    double qx = ax + abx * t, qy = ay + aby * t, d = Math.Sqrt((px - qx) * (px - qx) + (py - qy) * (py - qy));
                    if (d < bestD) { bestD = d; found = (qx, qy); }
                }
            }
            return found;
        }

        // ------------------------------------------------------------------ mảnh quá bé
        /// <summary>Các mảnh có vòng tròn lớn nhất bên trong nhỏ hơn 1,2% cạnh ngắn của tranh, bé nhất trước.</summary>
        public static List<SmallPiece> FindSmall(int width, int height, int unit, IReadOnlyList<GenRegion> regions)
        {
            var map = Rasterizer.Rasterize(width, height, unit, regions);
            var radius = RegionOps.InscribedRadii(map);
            var share = RegionOps.SharedBoundary(map);
            var limit = SmallRadius * Math.Min(map.W, map.H);
            var area = new int[radius.Length];
            foreach (var id in map.Reg) if (id > 0) area[id]++;

            var list = new List<SmallPiece>();
            for (var id = 1; id < radius.Length; id++)
            {
                if (!(radius[id] > 0 && radius[id] < limit)) continue;
                int best = 0, bl = 0;
                share[id].ForEach((len, o) => { if (len > bl) { bl = len; best = o; } });
                list.Add(new SmallPiece { Index = id - 1, Radius = radius[id], Area = area[id], Target = best > 0 ? best - 1 : -1 });
            }
            return list.OrderBy(s => s.Radius).ToList();
        }

        /// <summary>
        /// Mỗi mảnh bé gộp vào mảnh kề đề nghị; mảnh đích cũng bé thì chuyển tiếp tới đích của nó, hai mảnh trỏ nhau thì mảnh bé hơn gộp vào mảnh lớn hơn.
        /// Id của mỗi GenRegion phải duy nhất. Trả danh sách mới, số mảnh đã gộp (0 nếu không gộp được) và Id các mảnh đích.
        /// </summary>
        public static List<GenRegion> MergeSmall(int width, int height, int unit, ShapeSettings g, IReadOnlyList<GenRegion> regions, IReadOnlyList<SmallPiece> small, out int merged, out List<int> targetIds)
        {
            merged = 0;
            targetIds = new List<int>();
            var ids = regions.Select(r => r.Id).ToList();
            var by = small.ToDictionary(s => s.Index);
            var goes = new Dictionary<int, int>();
            foreach (var s in small) if (s.Target >= 0) goes[s.Index] = s.Target;

            var groups = new List<(int target, List<int> members)>();
            foreach (var s in small)
            {
                if (s.Target < 0) continue;
                var t = s.Target;
                var seen = new HashSet<int> { s.Index };
                while (goes.TryGetValue(t, out var nextTarget) && !seen.Contains(t)) { seen.Add(t); t = nextTarget; }
                if (t == s.Index)
                {
                    if (!by.TryGetValue(s.Target, out var o) || !(s.Area < o.Area || (s.Area == o.Area && s.Index < o.Index))) continue;
                    t = s.Target;
                }
                var tid = ids[t];
                var group = groups.FirstOrDefault(x => x.target == tid);
                if (group.members == null) groups.Add(group = (tid, new List<int>()));
                group.members.Add(ids[s.Index]);
            }
            if (groups.Count == 0) return null;

            var cur = regions.ToList();
            foreach (var (tid, members) in groups)
            {
                int At(int id) => cur.FindIndex(r => r.Id == id);
                var ti = At(tid);
                var idx = new List<int> { ti };
                idx.AddRange(members.Select(At).Where(k => k >= 0 && k != ti));
                if (ti < 0 || idx.Count < 2) continue;
                var res = MergeMany(width, height, unit, g, cur, idx, out _);
                if (res == null) continue;
                cur = res;
                merged += idx.Count - 1;
                targetIds.Add(tid);
            }
            return merged > 0 ? cur : null;
        }
    }
}
