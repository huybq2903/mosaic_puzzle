using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;

namespace Falcon.InGame.LevelEditor.Gen
{
    /// <summary>Các bước dọn bản đồ vùng (port từ regions.js): gộp, loại mảnh nhỏ, lấp khe, phóng to và làm mượt biên.</summary>
    public static class RegionOps
    {
        private const double TextRadiusFraction = 0.012;

        // hộp bao theo id: x1 = -1 với id không có điểm
        public sealed class Bounds
        {
            public int[] X0, Y0, X1, Y1;
        }

        public static Bounds BoundsOf(RegionMap m)
        {
            var n = m.Colors.Count;
            var b = new Bounds { X0 = new int[n], Y0 = new int[n], X1 = new int[n], Y1 = new int[n] };
            Array.Fill(b.X0, int.MaxValue);
            Array.Fill(b.Y0, int.MaxValue);
            Array.Fill(b.X1, -1);
            Array.Fill(b.Y1, -1);
            for (var p = 0; p < m.Reg.Length; p++)
            {
                var id = m.Reg[p];
                if (id <= 0) continue;
                int x = p % m.W, y = p / m.W;
                if (x < b.X0[id]) b.X0[id] = x;
                if (x > b.X1[id]) b.X1[id] = x;
                if (y < b.Y0[id]) b.Y0[id] = y;
                if (y > b.Y1[id]) b.Y1[id] = y;
            }
            return b;
        }

        /// <summary>Tách các phần rời thành mảnh riêng và đánh số lại 1..count.</summary>
        public static void Relabel(RegionMap m)
        {
            var old = m.Reg;
            var reg = new int[old.Length];
            var colors = new List<int> { -1 };
            int w = m.W, h = m.H;
            var st = new Stack<int>();
            for (var i = 0; i < old.Length; i++)
            {
                if (old[i] == 0 || reg[i] != 0) continue;
                var oid = old[i];
                var id = colors.Count;
                colors.Add(m.Colors[oid]);
                reg[i] = id;
                st.Push(i);
                while (st.Count > 0)
                {
                    int p = st.Pop(), x = p % w, y = p / w, q;
                    if (x > 0) { q = p - 1; if (reg[q] == 0 && old[q] == oid) { reg[q] = id; st.Push(q); } }
                    if (x < w - 1) { q = p + 1; if (reg[q] == 0 && old[q] == oid) { reg[q] = id; st.Push(q); } }
                    if (y > 0) { q = p - w; if (reg[q] == 0 && old[q] == oid) { reg[q] = id; st.Push(q); } }
                    if (y < h - 1) { q = p + w; if (reg[q] == 0 && old[q] == oid) { reg[q] = id; st.Push(q); } }
                }
            }
            m.Reg = reg;
            m.Colors = colors;
        }

        private static float Median(List<float> a)
        {
            a.Sort();
            return a[a.Count / 2];
        }

        /// <summary>Màu bảng của mỗi mặt = màu gần median Lab của phần lõi (cả mặt khi lõi quá nhỏ).</summary>
        public static int[] FaceColors(float[] lab, int[] labels, int ids, byte[] core)
        {
            var coreV = new List<float>[ids + 1][];
            var allV = new List<float>[ids + 1][];
            for (var i = 0; i <= ids; i++)
            {
                coreV[i] = new[] { new List<float>(), new List<float>(), new List<float>() };
                allV[i] = new[] { new List<float>(), new List<float>(), new List<float>() };
            }
            for (var i = 0; i < labels.Length; i++)
            {
                var l = labels[i];
                if (l <= 0) continue;
                for (var c = 0; c < 3; c++) allV[l][c].Add(lab[i * 3 + c]);
                if (core == null || core[i] != 0) for (var c = 0; c < 3; c++) coreV[l][c].Add(lab[i * 3 + c]);
            }
            var cols = new int[ids + 1];
            for (var id = 1; id <= ids; id++)
            {
                var src = coreV[id][0].Count >= 5 ? coreV[id] : allV[id];
                cols[id] = src[0].Count == 0 ? 0 : Palette.NearestLab(Median(src[0]), Median(src[1]), Median(src[2]));
            }
            return cols;
        }

        // ================= điểm thắt chéo và đảo nhỏ =================

        /// <summary>Điểm thắt chéo (hai điểm cùng nhãn chạm góc) làm biên tự cắt: đảo nhỏ ở đó bị bỏ.</summary>
        public static int RemovePinches(RegionMap m, int maxArea = 64)
        {
            int w = m.W, h = m.H, removed = 0;
            List<int> Island(int p)
            {
                var label = m.Reg[p];
                var list = new List<int>();
                var seen = new HashSet<int> { p };
                var st = new Stack<int>();
                st.Push(p);
                while (st.Count > 0)
                {
                    var q = st.Pop();
                    list.Add(q);
                    if (list.Count > maxArea) return null;
                    int x = q % w, y = q / w, r;
                    if (x > 0) { r = q - 1; if (m.Reg[r] == label && seen.Add(r)) st.Push(r); }
                    if (x < w - 1) { r = q + 1; if (m.Reg[r] == label && seen.Add(r)) st.Push(r); }
                    if (y > 0) { r = q - w; if (m.Reg[r] == label && seen.Add(r)) st.Push(r); }
                    if (y < h - 1) { r = q + w; if (m.Reg[r] == label && seen.Add(r)) st.Push(r); }
                }
                return list;
            }
            void Drop(List<int> list)
            {
                var label = m.Reg[list[0]];
                var to = 0;
                if (!m.Gaps)
                {
                    var votes = new SortedDictionary<int, int>();
                    foreach (var q in list)
                    {
                        int x = q % w, y = q / w;
                        foreach (var r in new[] { x > 0 ? q - 1 : -1, x < w - 1 ? q + 1 : -1, y > 0 ? q - w : -1, y < h - 1 ? q + w : -1 })
                        {
                            if (r < 0) continue;
                            var l = m.Reg[r];
                            if (l > 0 && l != label) votes[l] = (votes.TryGetValue(l, out var v) ? v : 0) + 1;
                        }
                    }
                    var bv = 0;
                    foreach (var kv in votes) if (kv.Value > bv) { bv = kv.Value; to = kv.Key; }
                }
                foreach (var q in list) m.Reg[q] = to;
                removed++;
            }
            void Pinch(int p1, int p2, int q1, int q2)
            {
                var i1 = Island(p1);
                var i2 = Island(p2);
                if (i1 != null && i2 != null) return;
                if (i1 != null) { Drop(i1); return; }
                if (i2 != null) { Drop(i2); return; }
                if (!m.Gaps) return;
                var label = m.Reg[p1];
                foreach (var q in new[] { q1, q2 })
                {
                    if (m.Reg[q] != 0) continue;
                    var j = Island(q);
                    if (j == null) continue;
                    foreach (var r in j) m.Reg[r] = label;
                    removed++;
                    return;
                }
            }
            for (var y = 1; y < h; y++)
                for (var x = 1; x < w; x++)
                {
                    int ia = (y - 1) * w + x - 1, ib = ia + 1, ic = y * w + x - 1, id = ic + 1;
                    int a = m.Reg[ia], b = m.Reg[ib], c = m.Reg[ic], d = m.Reg[id];
                    if (a > 0 && a == d && b != a && c != a) Pinch(ia, id, ib, ic);
                    else if (b > 0 && b == c && a != b && d != b) Pinch(ib, ic, ia, id);
                }
            return removed;
        }

        private sealed class Comp
        {
            public int Label;
            public List<int> Px = new();
        }

        /// <summary>Mảnh nhỏ bị cắt rời thân mảnh cùng nhãn (vụn ở góc/mép) chuyển sang mảnh gần nhất trong reach pixel.</summary>
        public static void AbsorbIslands(RegionMap m, int maxArea, int reach)
        {
            int w = m.W, h = m.H, n = w * h;
            var comp = new int[n];
            var comps = new List<Comp>();
            var st = new Stack<int>();
            for (var s0 = 0; s0 < n; s0++)
            {
                if (m.Reg[s0] <= 0 || comp[s0] != 0) continue;
                var label = m.Reg[s0];
                var c = new Comp { Label = label };
                comps.Add(c);
                comp[s0] = comps.Count;
                st.Push(s0);
                while (st.Count > 0)
                {
                    var p = st.Pop();
                    c.Px.Add(p);
                    int x = p % w, y = p / w, q;
                    if (x > 0) { q = p - 1; if (m.Reg[q] == label && comp[q] == 0) { comp[q] = comps.Count; st.Push(q); } }
                    if (x < w - 1) { q = p + 1; if (m.Reg[q] == label && comp[q] == 0) { comp[q] = comps.Count; st.Push(q); } }
                    if (y > 0) { q = p - w; if (m.Reg[q] == label && comp[q] == 0) { comp[q] = comps.Count; st.Push(q); } }
                    if (y < h - 1) { q = p + w; if (m.Reg[q] == label && comp[q] == 0) { comp[q] = comps.Count; st.Push(q); } }
                }
            }
            var biggest = new Dictionary<int, int>();
            for (var i = 0; i < comps.Count; i++)
            {
                var c = comps[i];
                if (!biggest.TryGetValue(c.Label, out var b) || comps[b].Px.Count < c.Px.Count) biggest[c.Label] = i;
            }
            foreach (var c in comps)
            {
                if (c.Px.Count > maxArea || ReferenceEquals(comps[biggest[c.Label]], c)) continue;
                var votes = new SortedDictionary<int, int>();
                foreach (var p in c.Px)
                {
                    int x = p % w, y = p / w;
                    for (var dy = -reach; dy <= reach; dy++)
                        for (var dx = -reach; dx <= reach; dx++)
                        {
                            int xx = x + dx, yy = y + dy;
                            if (xx < 0 || yy < 0 || xx >= w || yy >= h || dx * dx + dy * dy > reach * reach) continue;
                            var l = m.Reg[yy * w + xx];
                            if (l > 0 && l != c.Label) votes[l] = (votes.TryGetValue(l, out var v) ? v : 0) + 1;
                        }
                }
                int best = 0, bv = 0;
                foreach (var kv in votes) if (kv.Value > bv) { bv = kv.Value; best = kv.Key; }
                if (best == 0) continue;
                foreach (var p in c.Px) m.Reg[p] = best;
            }
        }

        // ================= cỡ mảnh =================

        /// <summary>Bán kính (pixel) vòng tròn nội tiếp lớn nhất của mỗi mảnh (chamfer 3-4); mép ảnh tính là biên.</summary>
        public static float[] InscribedRadii(RegionMap m)
        {
            int w = m.W, h = m.H, n = w * h;
            const int Inf = 1073741823;
            var d = new int[n];
            var cntp = m.Count;
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    var i = y * w + x;
                    var l = m.Reg[i];
                    if (l <= 0) continue;
                    var edge = x == 0 || y == 0 || x == w - 1 || y == h - 1 || m.Reg[i - 1] != l || m.Reg[i + 1] != l || m.Reg[i - w] != l || m.Reg[i + w] != l;
                    d[i] = edge ? 3 : Inf;
                }
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    var i = y * w + x;
                    if (d[i] == 0 || d[i] == 3) continue;
                    var v = d[i];
                    if (x > 0) v = Math.Min(v, d[i - 1] + 3);
                    if (y > 0) v = Math.Min(v, d[i - w] + 3);
                    if (x > 0 && y > 0) v = Math.Min(v, d[i - w - 1] + 4);
                    if (x < w - 1 && y > 0) v = Math.Min(v, d[i - w + 1] + 4);
                    d[i] = v;
                }
            var best = new int[cntp + 1];
            for (var y = h - 1; y >= 0; y--)
                for (var x = w - 1; x >= 0; x--)
                {
                    var i = y * w + x;
                    if (d[i] == 0) continue;
                    var v = d[i];
                    if (v != 3)
                    {
                        if (x < w - 1) v = Math.Min(v, d[i + 1] + 3);
                        if (y < h - 1) v = Math.Min(v, d[i + w] + 3);
                        if (x < w - 1 && y < h - 1) v = Math.Min(v, d[i + w + 1] + 4);
                        if (x > 0 && y < h - 1) v = Math.Min(v, d[i + w - 1] + 4);
                    }
                    if (v < Inf && v > best[m.Reg[i]]) best[m.Reg[i]] = v;
                }
            var res = new float[cntp + 1];
            for (var id = 1; id < res.Length; id++) res[id] = (float)(best[id] / 3.0);
            return res;
        }

        // khe (nét đen) giữa các mảnh rộng tới ngần này vẫn tính là liền kề
        public static int MaxGap(RegionMap m) => Math.Max(1, m.W / 48);

        // mỗi dải điểm nhãn 0 rộng 1..MaxGap nằm giữa hai mảnh khác nhau dọc một hàng/cột
        public static void ForEachGap(RegionMap m, Action<int, int, int, int, int> f)
        {
            int w = m.W, h = m.H, g = MaxGap(m);
            void Scan(int start, int stride, int n)
            {
                int prev = 0, run = 0;
                for (var k = 0; k < n; k++)
                {
                    var i = start + k * stride;
                    var r = m.Reg[i];
                    if (r <= 0) { run++; continue; }
                    if (prev > 0 && prev != r && run > 0 && run <= g) f(prev, r, i - run * stride, run, stride);
                    prev = r;
                    run = 0;
                }
            }
            for (var y = 0; y < h; y++) Scan(y * w, 1, w);
            for (var x = 0; x < w; x++) Scan(x, w, h);
        }

        /// <summary>Với mỗi mảnh: độ dài biên chung (cặp pixel, tính cả khe) với từng láng giềng.</summary>
        public static OrderedCounter[] SharedBoundary(RegionMap m)
        {
            int w = m.W, h = m.H;
            var res = new OrderedCounter[m.Count + 1];
            for (var i = 0; i <= m.Count; i++) res[i] = new OrderedCounter();
            void Pair(int a, int b)
            {
                if (a <= 0 || b <= 0 || a == b) return;
                res[a].Add(b, 1);
                res[b].Add(a, 1);
            }
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    var i = y * w + x;
                    if (x + 1 < w) Pair(m.Reg[i], m.Reg[i + 1]);
                    if (y + 1 < h) Pair(m.Reg[i], m.Reg[i + w]);
                }
            ForEachGap(m, (a, b, s, len, stride) => Pair(a, b));
            return res;
        }

        private static byte[] MergedPixels(RegionMap m, Func<int, int> find)
        {
            var res = new byte[m.Reg.Length];
            if (!m.Gaps) return res;
            for (var i = 0; i < res.Length; i++) res[i] = (byte)(m.Reg[i] > 0 && find(m.Reg[i]) != m.Reg[i] ? 1 : 0);
            return res;
        }

        // hai mảnh sắp gộp mà cách nhau một khe: lấp khe
        private static void BridgeMerged(RegionMap m, Func<int, int> find, byte[] mp)
        {
            ForEachGap(m, (a, b, s, len, stride) =>
            {
                var fa = find(a);
                if (fa != find(b)) return;
                for (var t = 0; t < len; t++) { m.Reg[s + t * stride] = fa; mp[s + t * stride] = 1; }
            });
        }

        // sau gộp, vòng nét quanh mảnh bị nuốt là khe mồ côi: cụm nhãn 0 chạm một mảnh, cạnh chỗ gộp, không ở mép
        private static void FillOrphanGaps(RegionMap m, byte[] mp)
        {
            if (!m.Gaps) return;
            int w = m.W, h = m.H, n = w * h;
            var seen = new byte[n];
            var st = new Stack<int>();
            var comp = new List<int>();
            for (var s0 = 0; s0 < n; s0++)
            {
                if (m.Reg[s0] != 0 || seen[s0] != 0) continue;
                comp.Clear();
                st.Push(s0);
                seen[s0] = 1;
                int label = 0;
                bool single = true, border = false, nearMerge = false;
                while (st.Count > 0)
                {
                    var p = st.Pop();
                    comp.Add(p);
                    int x = p % w, y = p / w;
                    if (x == 0 || y == 0 || x == w - 1 || y == h - 1) border = true;
                    var nb = new[] { x > 0 ? p - 1 : -1, x < w - 1 ? p + 1 : -1, y > 0 ? p - w : -1, y < h - 1 ? p + w : -1 };
                    for (var k = 0; k < 4; k++)
                    {
                        var q = nb[k];
                        if (q < 0) continue;
                        if (m.Reg[q] == 0) { if (seen[q] == 0) { seen[q] = 1; st.Push(q); } continue; }
                        if (mp[q] != 0) nearMerge = true;
                        if (label == 0) label = m.Reg[q]; else if (label != m.Reg[q]) single = false;
                    }
                }
                if (single && !border && nearMerge && label > 0) foreach (var c in comp) m.Reg[c] = label;
            }
        }

        private sealed class UnionFinder
        {
            public readonly int[] Parent;

            public UnionFinder(int size)
            {
                Parent = new int[size + 1];
                for (var i = 0; i < Parent.Length; i++) Parent[i] = i;
            }

            public int Find(int x)
            {
                while (Parent[x] != x) x = Parent[x] = Parent[Parent[x]];
                return x;
            }
        }

        private static void ApplyMerge(RegionMap m, UnionFinder uf)
        {
            var mp = MergedPixels(m, uf.Find);
            BridgeMerged(m, uf.Find, mp);
            for (var i = 0; i < m.Reg.Length; i++) if (m.Reg[i] > 0) m.Reg[i] = uf.Find(m.Reg[i]);
            FillOrphanGaps(m, mp);
            Relabel(m);
        }

        private static int[] Areas(RegionMap m)
        {
            var a = new int[m.Count + 1];
            for (var i = 0; i < m.Reg.Length; i++) if (m.Reg[i] > 0) a[m.Reg[i]]++;
            return a;
        }

        /// <summary>Mảnh nào cũng phải chứa số: mảnh có vòng nội tiếp nhỏ hơn minRadius gộp vào láng giềng có biên chung dài nhất.</summary>
        public static void EnsureTextFits(RegionMap m, double? minRadiusArg = null)
        {
            var minRadius = minRadiusArg ?? TextRadiusFraction * Math.Min(m.W, m.H);
            for (var pass = 0; pass < 12; pass++)
            {
                var radius = InscribedRadii(m);
                var share = SharedBoundary(m);
                var area = Areas(m);
                var small = new List<int>();
                for (var id = 1; id < radius.Length; id++) if (radius[id] > 0 && radius[id] < minRadius) small.Add(id);
                if (small.Count == 0) return;
                small = small.OrderBy(a => area[a]).ToList();
                var uf = new UnionFinder(m.Count);
                var changed = false;
                foreach (var id in small)
                {
                    if (uf.Find(id) != id) continue;
                    int best = 0, bl = 0;
                    share[id].ForEach((len, k) =>
                    {
                        var o = uf.Find(k);
                        if (o == id) return;
                        if (len > bl) { bl = len; best = o; }
                    });
                    if (best == 0) continue;
                    uf.Parent[id] = best;
                    area[best] += area[id];
                    changed = true;
                }
                if (!changed) return;
                ApplyMerge(m, uf);
            }
        }

        /// <summary>Vụn nhỏ hơn fraction của ảnh gộp vào láng giềng có biên chung dài nhất.</summary>
        public static void RemoveSpecks(RegionMap m, double fraction)
        {
            for (var pass = 0; pass < 3; pass++)
            {
                var area = Areas(m);
                var limit = fraction * m.W * m.H;
                var share = SharedBoundary(m);
                var uf = new UnionFinder(m.Count);
                var changed = false;
                for (var id = 1; id <= m.Count; id++)
                {
                    if (area[id] == 0 || area[id] >= limit) continue;
                    int best = 0, bl = 0;
                    share[id].ForEach((len, k) => { if (len > bl && area[k] >= limit) { bl = len; best = k; } });
                    if (best == 0) continue;
                    uf.Parent[id] = best;
                    changed = true;
                }
                if (!changed) return;
                ApplyMerge(m, uf);
            }
        }

        /// <summary>Quá nhiều mảnh: gộp mảnh nhỏ nhất vào láng giềng gần màu và có biên chung dài.</summary>
        public static void LimitCount(RegionMap m, int max)
        {
            for (var guard = 0; guard < 400 && m.Count > max; guard++)
            {
                var area = Areas(m);
                var share = SharedBoundary(m);
                var order = Enumerable.Range(1, m.Count).OrderBy(a => area[a]).ToList();
                var uf = new UnionFinder(m.Count);
                var excess = m.Count - max;
                var merged = 0;
                foreach (var id in order)
                {
                    if (merged >= excess) break;
                    if (uf.Find(id) != id || share[id].Size == 0) continue;
                    var mine = Palette.Lab[m.Colors[id]];
                    int best = 0;
                    double bs = -1;
                    share[id].ForEach((len, k) =>
                    {
                        var o = uf.Find(k);
                        if (o == id) return;
                        var lo = Palette.Lab[m.Colors[o]];
                        double d0 = lo[0] - mine[0], d1 = lo[1] - mine[1], d2 = lo[2] - mine[2];
                        var de = Math.Sqrt(d0 * d0 + d1 * d1 + d2 * d2) / 20;
                        var score = len / (1 + de * de);
                        if (score > bs) { bs = score; best = o; }
                    });
                    if (best == 0) continue;
                    uf.Parent[id] = best;
                    merged++;
                }
                if (merged == 0) return;
                ApplyMerge(m, uf);
            }
        }

        /// <summary>Điểm nét (nhãn 0) về mảnh gần nhất, rồi về mảnh chiếm nhiều nhất trong đĩa quanh bằng bề rộng nét; các mảnh phủ kín ảnh.</summary>
        public static void FillGaps(RegionMap m)
        {
            int w = m.W, h = m.H, n = w * h;
            var lab = m.Reg;
            var ink = 0;
            for (var i = 0; i < n; i++) if (lab[i] == 0) ink++;
            if (ink == 0 || ink == n) { m.Gaps = false; return; }
            // điểm mảnh gần nhất: hai lượt mang toạ độ hạt giống gần nhất (8SSEDT)
            var sx = new short[n];
            var sy = new short[n];
            var dd = new int[n];
            const int Big = 1 << 30;
            for (var i = 0; i < n; i++)
            {
                if (lab[i] != 0) { sx[i] = (short)(i % w); sy[i] = (short)(i / w); dd[i] = 0; } else dd[i] = Big;
            }
            void Look(int i, int x, int y, int nx, int ny)
            {
                if (nx < 0 || ny < 0 || nx >= w || ny >= h) return;
                var j = ny * w + nx;
                if (dd[j] == Big) return;
                int dx = x - sx[j], dy = y - sy[j], d = dx * dx + dy * dy;
                if (d < dd[i]) { dd[i] = d; sx[i] = sx[j]; sy[i] = sy[j]; }
            }
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++) { var i = y * w + x; Look(i, x, y, x - 1, y); Look(i, x, y, x - 1, y - 1); Look(i, x, y, x, y - 1); Look(i, x, y, x + 1, y - 1); }
                for (var x = w - 1; x >= 0; x--) { var i = y * w + x; Look(i, x, y, x + 1, y); }
            }
            for (var y = h - 1; y >= 0; y--)
            {
                for (var x = w - 1; x >= 0; x--) { var i = y * w + x; Look(i, x, y, x + 1, y); Look(i, x, y, x + 1, y + 1); Look(i, x, y, x, y + 1); Look(i, x, y, x - 1, y + 1); }
                for (var x = 0; x < w; x++) { var i = y * w + x; Look(i, x, y, x - 1, y); }
            }
            var filled = new int[n];
            var inkIdx = new List<int>();
            for (var i = 0; i < n; i++)
            {
                if (lab[i] != 0) filled[i] = lab[i];
                else { filled[i] = lab[sy[i] * w + sx[i]]; inkIdx.Add(i); }
            }
            // bề rộng nét: hai lần khoảng cách từ sống nét tới phía sáng
            var mask = new byte[n];
            for (var i = 0; i < n; i++) mask[i] = (byte)(lab[i] == 0 ? 1 : 0);
            var d2 = Ops.Edt(mask, w, h);
            var ridge = new List<float>();
            for (var y = 1; y < h - 1; y += 2)
                for (var x = 1; x < w - 1; x += 2)
                {
                    var i = y * w + x;
                    var v = d2[i];
                    if (v < 1) continue;
                    if ((v >= d2[i - 1] && v >= d2[i + 1] && (v > d2[i - 1] || v > d2[i + 1])) || (v >= d2[i - w] && v >= d2[i + w] && (v > d2[i - w] || v > d2[i + w]))) ridge.Add(v);
                }
            if (ridge.Count > 50)
            {
                ridge.Sort();
                m.InkW = Math.Max(2, 2 * (double)ridge[ridge.Count >> 1] - 1);
                var r = (int)Math.Max(2, Math.Min(8, Ops.JsRound(m.InkW)));
                var off = new List<int>();
                for (var dy = -r; dy <= r; dy++) for (var dx = -r; dx <= r; dx++) if (dx * dx + dy * dy <= r * r) off.Add(dy * w + dx);
                var half = off.Count / 2.0;
                var offs = off.ToArray();
                var inkArr = inkIdx.ToArray();
                for (var pass = 0; pass < 3; pass++)
                {
                    var prev = (int[])filled.Clone();
                    var changed = 0;
                    Parallel.For(0, inkArr.Length, () => (ids: new int[64], cnt: new int[64], changed: 0), (k, _, st) =>
                    {
                        var i = inkArr[k];
                        int x = i % w, y = i / w;
                        if (x < r || y < r || x >= w - r || y >= h - r) return st;
                        var ids = st.ids;
                        var cnt = st.cnt;
                        var nl = 0;
                        for (var o = 0; o < offs.Length; o++)
                        {
                            var c = prev[i + offs[o]];
                            int j;
                            for (j = 0; j < nl && ids[j] != c; j++) { }
                            if (j == nl) { if (nl == 64) continue; ids[nl] = c; cnt[nl++] = 1; } else cnt[j]++;
                        }
                        var best = 0;
                        for (var j = 1; j < nl; j++) if (cnt[j] > cnt[best]) best = j;
                        if (ids[best] != prev[i] && cnt[best] > half) { filled[i] = ids[best]; st.changed++; }
                        return st;
                    }, st => System.Threading.Interlocked.Add(ref changed, st.changed));
                    if (changed == 0) break;
                }
            }
            foreach (var q in inkIdx) lab[q] = filled[q];
            m.Gaps = false;
        }

        // ================= phóng to =================

        private static float[] GaussianKernel(double sigma)
        {
            var r = (int)Math.Ceiling(sigma * 3);
            var k = new float[r * 2 + 1];
            double sum = 0;
            for (var i = -r; i <= r; i++)
            {
                var e = Math.Exp(-i * i / (2 * sigma * sigma));
                k[i + r] = (float)e;
                sum += e;
            }
            for (var i = 0; i < k.Length; i++) k[i] = (float)(k[i] / sum);
            return k;
        }

        // làm mờ tách được, ngoài vùng đệm là 0; mỗi hàng độc lập nên chạy song song, thứ tự cộng giữ nguyên
        private static float[] Blur(float[] src, int w, int h, float[] k)
        {
            var r = k.Length >> 1;
            var tmp = new float[src.Length];
            var dst = new float[src.Length];
            Parallel.For(0, h, y =>
            {
                var row = y * w;
                for (var x = 0; x < w; x++)
                {
                    double s = 0;
                    int lo = Math.Max(0, x - r), hi = Math.Min(w - 1, x + r);
                    for (var xx = lo; xx <= hi; xx++) s += (double)src[row + xx] * k[xx - x + r];
                    tmp[row + x] = (float)s;
                }
            });
            Parallel.For(0, h, y =>
            {
                var acc = new double[w];
                int lo = Math.Max(0, y - r), hi = Math.Min(h - 1, y + r);
                for (var yy = lo; yy <= hi; yy++)
                {
                    var kv = k[yy - y + r];
                    var row = yy * w;
                    for (var x = 0; x < w; x++) acc[x] += (double)tmp[row + x] * kv;
                }
                var o = y * w;
                for (var x = 0; x < w; x++) dst[o + x] = (float)acc[x];
            });
            return dst;
        }

        // nét mảnh (1-2 px) nặng hơn để mảnh không nuốt nó khi làm mượt
        private static float[] ThinGapWeights(RegionMap m)
        {
            if (!m.Gaps) return null;
            var ink = new byte[m.Reg.Length];
            for (var i = 0; i < ink.Length; i++) ink[i] = (byte)(m.Reg[i] == 0 ? 1 : 0);
            var d = Ops.Edt(ink, m.W, m.H);
            var wt = new float[d.Length];
            for (var y = 0; y < m.H; y++)
                for (var x = 0; x < m.W; x++)
                {
                    if (d[y * m.W + x] <= 0) continue;
                    double peak = 0;
                    for (var dy = -2; dy <= 2; dy++)
                        for (var dx = -2; dx <= 2; dx++)
                        {
                            int xx = x + dx, yy = y + dy;
                            if (xx >= 0 && yy >= 0 && xx < m.W && yy < m.H) peak = Math.Max(peak, d[yy * m.W + xx]);
                        }
                    wt[y * m.W + x] = (float)(peak <= 1.5 ? 1.8 : 1);
                }
            for (var i = 0; i < wt.Length; i++) if (wt[i] == 0) wt[i] = 1;
            return wt;
        }

        /// <summary>Phóng bản đồ lên scale lần và làm mượt biên: làm mờ mặt nạ từng vùng, mỗi pixel nhận vùng mạnh nhất.</summary>
        public static RegionMap Upscale(RegionMap m, int scale)
        {
            if (scale <= 1) return m;
            int W = m.W * scale, H = m.H * scale;
            var outReg = new int[W * H];
            var kernel = GaussianKernel(scale);
            var bb = BoundsOf(m);
            const int margin = 3;
            var outside = new float[W * H];
            var wt = ThinGapWeights(m);
            for (var Y = 0; Y < H; Y++)
                for (var X = 0; X < W; X++)
                {
                    var src = (Y / scale) * m.W + (X / scale);
                    outside[Y * W + X] = m.Reg[src] == 0 ? (wt != null ? wt[src] : 1) : 0;
                }
            var best = Blur(outside, W, H, kernel);
            for (var id = 1; id <= m.Count; id++)
            {
                if (bb.X1[id] < 0) continue;
                int x0 = Math.Max(0, bb.X0[id] - margin), y0 = Math.Max(0, bb.Y0[id] - margin), x1 = Math.Min(m.W, bb.X1[id] + 1 + margin), y1 = Math.Min(m.H, bb.Y1[id] + 1 + margin);
                int cw = (x1 - x0) * scale, ch = (y1 - y0) * scale;
                var buf = new float[cw * ch];
                for (var y = 0; y < ch; y++)
                    for (var x = 0; x < cw; x++)
                        buf[y * cw + x] = m.Reg[(y0 + y / scale) * m.W + x0 + x / scale] == id ? 1 : 0;
                buf = Blur(buf, cw, ch, kernel);
                for (var y = 0; y < ch; y++)
                    for (var x = 0; x < cw; x++)
                    {
                        var v = buf[y * cw + x];
                        var gi = (y0 * scale + y) * W + x0 * scale + x;
                        if (v > best[gi]) { best[gi] = v; outReg[gi] = id; }
                    }
            }
            return new RegionMap { W = W, H = H, Reg = outReg, Colors = new List<int>(m.Colors), Inked = m.Inked, Gaps = m.Gaps, Synthetic = m.Synthetic };
        }
    }
}
