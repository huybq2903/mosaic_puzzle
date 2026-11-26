using System;
using System.Collections.Generic;
using System.Linq;

namespace Falcon.InGame.LevelEditor.Gen
{
    /// <summary>Mảnh như mô phỏng thấy: số cát, màu, độ thấp, mở sẵn (chạm cạnh dưới), láng giềng.</summary>
    public sealed class SimPiece
    {
        public int Weight, Color;
        public double Bottom;
        public bool StartOpen;
        public int[] Neighbors;
    }

    public readonly struct SimBox
    {
        public readonly int Color, Amount;

        public SimBox(int color, int amount)
        {
            Color = color;
            Amount = amount;
        }
    }

    public sealed class SimQueue
    {
        public int Slots;
        public List<List<SimBox>> Columns;
        public double WinRate;
    }

    /// <summary>
    /// Sinh hàng chờ thắng được bằng cách chơi thử level theo luật Shooter (port buildQueue, simulateQueue, queueScore, makeQueue của generate.js).
    /// Toạ độ y của mảnh theo kiểu Studio (y xuống): cạnh dưới là y lớn nhất.
    /// </summary>
    public static class QueueSim
    {
        private const double Size = 817, SideTol = 0.6;
        private static readonly double[] AmountOdds = { 0.30, 0.57, 0.77, 1 };
        private const int AmountUnit = 5;

        // ------------------------------------------------------------------ rng
        public sealed class Rng
        {
            private int _a;

            public Rng(int seed) => _a = seed;

            public double Next()
            {
                unchecked
                {
                    _a += 0x6D2B79F5;
                    var t = Imul(_a ^ Lsr(_a, 15), 1 | _a);
                    t = (t + Imul(t ^ Lsr(t, 7), 61 | t)) ^ t;
                    return (uint)(t ^ Lsr(t, 14)) / 4294967296.0;
                }
            }

            private static int Imul(int a, int b) => unchecked(a * b);

            private static int Lsr(int v, int n) => (int)((uint)v >> n);
        }

        public static int Hash(string s)
        {
            unchecked
            {
                var h = 2166136261u;
                foreach (var c in s)
                {
                    h ^= c;
                    h *= 16777619u;
                }
                return (int)h;
            }
        }

        // ------------------------------------------------------------------ mảnh
        private static double Area2(List<(double x, double y)> r)
        {
            double s = 0;
            for (int i = 0, j = r.Count - 1; i < r.Count; j = i++) s += r[j].x * r[i].y - r[i].x * r[j].y;
            return Math.Abs(s) * 0.5;
        }

        /// <summary>
        /// Mảnh mô phỏng từ tranh: points theo 1/unit của lưới width×height (y lên), weights là số cát từng mảnh.
        /// </summary>
        public static List<SimPiece> PiecesOf(int width, int height, int unit, IReadOnlyList<GenRegion> regions, IReadOnlyList<int> weights)
        {
            var dw = width >= height ? Size : Math.Max(1, Math.Round(Size * width / height));
            var dh = width >= height ? Math.Max(1, Math.Round(Size * height / width)) : Size;
            var k = dw / ((double)width * unit);
            var snap = 8 * Size / 817;
            var map = Rasterizer.Rasterize(width, height, unit, regions);
            var near = Rasterizer.Neighbours(map);
            double Edge(double v, double max) => v < snap ? 0 : v > max - snap ? max : v;
            double Round1(double v) => Math.Round(v * 10, MidpointRounding.AwayFromZero) / 10;

            List<(double x, double y)> Ring(int[] pts, bool snapOn)
            {
                var o = new List<(double x, double y)>();
                for (var i = 0; i + 1 < pts.Length; i += 2)
                {
                    var x = Round1(pts[i] * k);
                    var y = Round1(((double)height * unit - pts[i + 1]) * k);
                    if (snapOn) { x = Edge(x, dw); y = Edge(y, dh); }
                    if (o.Count >= 1 && o[^1].x == x && o[^1].y == y) continue;
                    o.Add((x, y));
                }
                return o;
            }

            var list = new List<SimPiece>();
            for (var i = 0; i < regions.Count; i++)
            {
                var plain = Ring(regions[i].Points, false);
                var on = Ring(regions[i].Points, true);
                var snapped = Area2(on) >= 0.6 * Area2(plain);
                var outer = snapped ? on : plain;
                double bottom = double.NegativeInfinity;
                var touches = false;
                foreach (var v in outer)
                {
                    if (v.y > bottom) bottom = v.y;
                    if (Math.Abs(v.y - dh) < SideTol) touches = true;
                }
                list.Add(new SimPiece
                {
                    Weight = Math.Max(1, weights[i]),
                    Color = regions[i].ColorId,
                    Bottom = bottom,
                    StartOpen = touches,
                    Neighbors = near[i].ToArray(),
                });
            }
            return list;
        }

        // ------------------------------------------------------------------ sinh hàng chờ bằng cách chơi level
        private static SimQueue Build(IReadOnlyList<SimPiece> pieces, string name, int slots, int columns)
        {
            var rnd = new Rng(Hash(name));
            var n = pieces.Count;
            var avail = pieces.Select(p => p.Weight).ToArray();
            var open = pieces.Select(p => p.StartOpen).ToArray();
            var seq = new int[n];
            var opened = 0;
            var remaining = avail.Sum();
            var objects = new List<SimBox>();

            void OpenPiece(int k)
            {
                if (open[k]) return;
                open[k] = true;
                seq[k] = ++opened;
            }

            while (remaining > 0)
            {
                var sum = new SortedDictionary<int, int>();
                var any = false;
                for (var i = 0; i < n; i++)
                    if (open[i] && avail[i] > 0)
                    {
                        sum.TryGetValue(pieces[i].Color, out var s);
                        sum[pieces[i].Color] = s + avail[i];
                        any = true;
                    }
                if (!any)
                {
                    var low = -1;
                    for (var i = 0; i < n; i++)
                        if (!open[i] && avail[i] > 0 && (low < 0 || pieces[i].Bottom > pieces[low].Bottom)) low = i;
                    OpenPiece(low);
                    continue;
                }
                var colours = sum.Keys.ToList();
                var tot = sum.Values.Sum();
                var r = rnd.Next() * tot;
                var colour = colours[^1];
                foreach (var c in colours)
                {
                    r -= sum[c];
                    if (r < 0) { colour = c; break; }
                }
                var q = rnd.Next();
                var amount = AmountUnit * (q < AmountOdds[0] ? 1 : q < AmountOdds[1] ? 2 : q < AmountOdds[2] ? 3 : 4);
                amount = Math.Min(amount, sum[colour]);
                for (var left = amount; left > 0;)
                {
                    var best = -1;
                    var bk = double.PositiveInfinity;
                    for (var i = 0; i < n; i++)
                    {
                        if (!open[i] || pieces[i].Color != colour || avail[i] <= 0) continue;
                        var key = seq[i] * 1e7 - pieces[i].Bottom * 1e3 + i;
                        if (key < bk) { bk = key; best = i; }
                    }
                    var take = Math.Min(avail[best], left);
                    avail[best] -= take;
                    left -= take;
                    remaining -= take;
                    if (avail[best] == 0) foreach (var nb in pieces[best].Neighbors) OpenPiece(nb);
                }
                objects.Add(new SimBox(colour, amount));
            }

            var cols = new List<List<SimBox>>();
            for (var i = 0; i < columns; i++) cols.Add(new List<SimBox>());
            for (var k = 0; k < objects.Count; k++) cols[k % columns].Add(objects[k]);
            return new SimQueue { Slots = slots, Columns = cols };
        }

        // ------------------------------------------------------------------ chơi thử theo luật Shooter
        private sealed class Shooter
        {
            public int Color, Free, Delay;
            public readonly List<(int piece, int amount)> Jobs = new();
        }

        // Tỉ lệ lượt chơi tới cuối mà không kẹt; fixed (every, flight, indexOrder) bỏ giá trị ngẫu nhiên
        public static double Simulate(IReadOnlyList<SimPiece> pieces, SimQueue queue, int runs, Rng rnd, (int every, int flight, bool indexOrder)? fixedPlayer = null)
        {
            var n = pieces.Count;
            var won = 0;
            for (var run = 0; run < runs; run++)
            {
                var avail = pieces.Select(p => p.Weight).ToArray();
                var left = (int[])avail.Clone();
                var open = pieces.Select(p => p.StartOpen).ToArray();
                var vis = (bool[])open.Clone();
                var seq = new int[n];
                var opened = 0;
                var todo = avail.Sum();
                var cols = queue.Columns.Select(c => new Queue<SimBox>(c)).ToList();
                var belt = new List<Shooter>();
                var every = rnd.Next() < 0.5 ? 1 : 1 + (int)Math.Floor(rnd.Next() * 8);
                var flight = 3 + (int)Math.Floor(rnd.Next() * 22);
                var indexOrder = rnd.Next() < 0.5;
                var ok = false;
                var idle = 0;
                if (fixedPlayer.HasValue) (every, flight, indexOrder) = fixedPlayer.Value;

                bool HasTarget(int color)
                {
                    for (var k = 0; k < n; k++) if (open[k] && avail[k] > 0 && pieces[k].Color == color) return true;
                    return false;
                }

                void Reserve()
                {
                    var changed = true;
                    while (changed)
                    {
                        changed = false;
                        for (var bi = 0; bi < belt.Count; bi++)
                        {
                            var sh = belt[bi];
                            while (sh.Free > 0)
                            {
                                var best = -1;
                                var bk = double.PositiveInfinity;
                                for (var k = 0; k < n; k++)
                                {
                                    if (!open[k] || pieces[k].Color != sh.Color || avail[k] <= 0) continue;
                                    var key = seq[k] * 1e7 - pieces[k].Bottom * 1e3 + k;
                                    if (key < bk) { bk = key; best = k; }
                                }
                                if (best < 0) break;
                                var take = Math.Min(avail[best], sh.Free);
                                avail[best] -= take;
                                sh.Free -= take;
                                sh.Jobs.Add((best, take));
                                changed = true;
                                if (avail[best] == 0)
                                    foreach (var q in pieces[best].Neighbors)
                                        if (!open[q]) { open[q] = true; seq[q] = ++opened; }
                            }
                        }
                    }
                }

                bool Tick()
                {
                    var shot = false;
                    foreach (var sh in belt)
                    {
                        if (sh.Delay > 0) { sh.Delay--; shot = true; continue; }
                        if (sh.Jobs.Count == 0 || !vis[sh.Jobs[0].piece]) continue;
                        var j = sh.Jobs[0];
                        j.amount--;
                        left[j.piece]--;
                        todo--;
                        shot = true;
                        if (j.amount == 0) sh.Jobs.RemoveAt(0);
                        else sh.Jobs[0] = j;
                        if (left[j.piece] == 0)
                            foreach (var q in pieces[j.piece].Neighbors)
                                if (open[q]) vis[q] = true;
                    }
                    belt.RemoveAll(b => !(b.Free > 0 || b.Jobs.Count > 0));
                    Reserve();
                    return shot;
                }

                var order = Enumerable.Range(0, cols.Count).ToList();
                for (var step = 0; step < 20000; step++)
                {
                    var tapped = false;
                    if (step % every == 0)
                    {
                        var seqOrder = indexOrder ? order : order.OrderBy(_ => rnd.Next() - 0.5).ToList();
                        foreach (var c in seqOrder)
                        {
                            if (cols[c].Count == 0 || belt.Count >= queue.Slots || !HasTarget(cols[c].Peek().Color)) continue;
                            var bx = cols[c].Dequeue();
                            belt.Add(new Shooter { Color = bx.Color, Free = bx.Amount, Delay = flight });
                            Reserve();
                            tapped = true;
                        }
                    }
                    var moved = Tick();
                    if (todo == 0) { ok = true; break; }
                    if (!tapped && !moved)
                    {
                        idle++;
                        if (idle > every + 2) break;
                    }
                    else idle = 0;
                }
                if (ok) won++;
            }
            return (double)won / runs;
        }

        // Tỉ lệ thắng trên lưới người chơi cố định (tốc độ bấm × thời gian bay × thứ tự cột) cộng 120 người ngẫu nhiên
        public static double Score(IReadOnlyList<SimPiece> pieces, SimQueue queue, Rng rnd)
        {
            double ok = 0, total = 0;
            foreach (var e in new[] { 1, 2, 4 })
                foreach (var f in new[] { 4, 8, 12, 20, 28 })
                    foreach (var o in new[] { true, false })
                    {
                        ok += Simulate(pieces, queue, 1, rnd, (e, f, o));
                        total++;
                    }
            ok += Simulate(pieces, queue, 120, rnd) * 120;
            total += 120;
            return ok / total;
        }

        /// <summary>Hàng chờ thắng được: thử nhiều thứ tự, lấy cái đầu tiên thắng mọi lượt mô phỏng (không thì cái tốt nhất).</summary>
        public static SimQueue Make(IReadOnlyList<SimPiece> pieces, string name, int slots, int columns)
        {
            SimQueue best = null;
            double bestScore = -1;
            for (var attempt = 0; attempt < 80; attempt++)
            {
                var q = Build(pieces, attempt > 0 ? name + "#" + attempt : name, slots, columns);
                var score = Score(pieces, q, new Rng(Hash(name) + attempt));
                if (score == 1) score = Math.Min(score, Simulate(pieces, q, 400, new Rng(Hash(name) + attempt + 7919)));
                if (score > bestScore) { best = q; bestScore = score; }
                if (score == 1) break;
            }
            best.WinRate = bestScore;
            return best;
        }
    }
}
