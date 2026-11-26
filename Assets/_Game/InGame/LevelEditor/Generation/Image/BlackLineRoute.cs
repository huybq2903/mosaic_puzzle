using System;
using System.Collections.Generic;

namespace Falcon.InGame.LevelEditor.Gen
{
    /// <summary>Route ảnh kính màu viền đen: tìm các mặt giữa nét đen (port từ regions.js).</summary>
    public static class BlackLineRoute
    {
        private const double DarkMaxBase = 35, DarkMaxHigh = 50, SwallowShare = 0.25;
        private const int MinArea = 185, MinPieces = 15, EdgeInk = 6, VeinWindow = 7, VeinContrast = 60, VeinMax = 120;
        private const int MinInkArea = 60, MaxRing = 16, GapReach = 8;
        private const double MaxLineHalfWidth = 3.2, RingFraction = 0.6, MaxRingDepth = 0.05, MinCoverage = 0.55;
        private const int LineProbe = 8;

        private sealed class Band
        {
            private readonly int _l, _r, _t, _b, _w, _h;
            private readonly bool _rl, _rr, _rt, _rb;

            public Band(int l, int r, int t, int b, int w, int h, bool rl, bool rr, bool rt, bool rb)
            {
                _l = l; _r = r; _t = t; _b = b; _w = w; _h = h; _rl = rl; _rr = rr; _rt = rt; _rb = rb;
            }

            public bool Contains(int x, int y) => x < _l || x >= _w - _r || y < _t || y >= _h - _b;

            private bool Kept(int x, int y) => (_rl && x < _l) || (_rr && x >= _w - _r) || (_rt && y < _t) || (_rb && y >= _h - _b);

            public bool Filled(int x, int y) => Contains(x, y) && !Kept(x, y);
        }

        private static (int depth, bool ring) Side(Func<int, double> frac, int maxD)
        {
            var d = 0;
            while (d < maxD && frac(d) < RingFraction) d++;
            if (d >= maxD) return (EdgeInk, false);
            var start = d;
            while (d < maxD + MaxRing && frac(d) >= RingFraction) d++;
            return d - start > MaxRing ? (EdgeInk, false) : (d + 1, true);
        }

        // nét đen cuối mỗi cạnh ảnh: khung dày giữ lại như viền, cạnh không có khung thì chỉ bỏ vài pixel
        private static Band Bands(float[] peak, int w, int h)
        {
            var maxD = Math.Max(EdgeInk + 1, (int)Ops.JsRound(MaxRingDepth * Math.Min(w, h)));
            double Col(int x)
            {
                double c = 0, t = 0;
                for (var y = h / 10; y < h - h / 10; y++) { t++; if (peak[y * w + x] < DarkMaxBase) c++; }
                return c / t;
            }
            double Row(int y)
            {
                double c = 0, t = 0;
                for (var x = w / 10; x < w - w / 10; x++) { t++; if (peak[y * w + x] < DarkMaxBase) c++; }
                return c / t;
            }
            var l = Side(d => Col(d), maxD);
            var r = Side(d => Col(w - 1 - d), maxD);
            var t2 = Side(d => Row(d), maxD);
            var b = Side(d => Row(h - 1 - d), maxD);
            return new Band(l.depth, r.depth, t2.depth, b.depth, w, h, l.ring, r.ring, t2.ring, b.ring);
        }

        // nối các khe hở nhỏ giữa đầu nét và nét khác bằng đường 2 px
        private static void CloseSmallGaps(byte[] free, int w, int h)
        {
            var n = w * h;
            var ink = new byte[n];
            for (var i = 0; i < n; i++) ink[i] = (byte)(free[i] != 0 ? 0 : 1);
            var sk = Skeleton.Thin(ink, w, h);
            var before = (byte[])sk.Clone();
            var width = Ops.Edt(ink, w, h);
            var thick = new byte[n];
            for (var i = 0; i < n; i++) thick[i] = (byte)(width[i] > MaxLineHalfWidth ? 1 : 0);
            Skeleton.CloseGaps(sk, w, h, GapReach, null, thick);
            var bridge = new byte[n];
            for (var i = 0; i < n; i++) bridge[i] = (byte)(sk[i] != 0 && before[i] == 0 ? 1 : 0);
            bridge = Ops.Dilate3(bridge, w, h);
            for (var i = 0; i < n; i++) if (bridge[i] != 0) free[i] = 0;
        }

        // lan nhãn mặt sang điểm chưa có nhãn mà không phải nét thật (ô nhỏ bị bỏ, dải khung ở mép)
        private static void FillCells(int[] faces, byte[] free, int w, int h, Band band)
        {
            var n = w * h;
            var notFree = new byte[n];
            for (var i = 0; i < n; i++) notFree[i] = (byte)(free[i] != 0 ? 0 : 1);
            var inkCells = Ops.Components(notFree, w, h, true);
            var inkArea = new int[inkCells.Count + 1];
            for (var i = 0; i < n; i++) if (inkCells.Lab[i] > 0) inkArea[inkCells.Lab[i]]++;
            bool Fillable(int p) => free[p] != 0 || band.Filled(p % w, p / w) || (inkCells.Lab[p] > 0 && inkArea[inkCells.Lab[p]] < MinInkArea);
            var q = new int[n];
            int head = 0, tail = 0;
            for (var i = 0; i < n; i++) if (faces[i] > 0) q[tail++] = i;
            while (head < tail)
            {
                int p = q[head++], x = p % w, y = p / w, nb;
                if (x > 0) { nb = p - 1; if (faces[nb] == 0 && Fillable(nb)) { faces[nb] = faces[p]; q[tail++] = nb; } }
                if (x < w - 1) { nb = p + 1; if (faces[nb] == 0 && Fillable(nb)) { faces[nb] = faces[p]; q[tail++] = nb; } }
                if (y > 0) { nb = p - w; if (faces[nb] == 0 && Fillable(nb)) { faces[nb] = faces[p]; q[tail++] = nb; } }
                if (y < h - 1) { nb = p + w; if (faces[nb] == 0 && Fillable(nb)) { faces[nb] = faces[p]; q[tail++] = nb; } }
            }
        }

        // trục chính (PCA) của các điểm nét sau c, trong bán kính rad; null khi quá ít điểm
        private static double[] LineDirection(byte[] ink, int w, int h, double cx, double cy, double tox, double toy, double rad)
        {
            int x0 = Math.Max(0, (int)Math.Floor(cx - rad)), x1 = Math.Min(w - 1, (int)Math.Ceiling(cx + rad)), y0 = Math.Max(0, (int)Math.Floor(cy - rad)), y1 = Math.Min(h - 1, (int)Math.Ceiling(cy + rad));
            bool Behind(int x, int y)
            {
                double dx = x - cx, dy = y - cy;
                return ink[y * w + x] != 0 && dx * dx + dy * dy <= rad * rad && dx * tox + dy * toy <= 0.5;
            }
            double sx = 0, sy = 0;
            var k = 0;
            for (var y = y0; y <= y1; y++) for (var x = x0; x <= x1; x++) if (Behind(x, y)) { sx += x; sy += y; k++; }
            if (k < 3) return null;
            double mx = sx / k, my = sy / k, cxx = 0, cxy = 0, cyy = 0;
            for (var y = y0; y <= y1; y++)
                for (var x = x0; x <= x1; x++)
                    if (Behind(x, y)) { var dx = x - mx; var dy = y - my; cxx += dx * dx; cxy += dx * dy; cyy += dy * dy; }
            var ang = 0.5 * Math.Atan2(2 * cxy, cxx - cyy);
            return new[] { Math.Cos(ang), Math.Sin(ang) };
        }

        // nét chạm dải khung ở cạnh không có khung thì kéo thẳng (cùng độ dày) ra mép ảnh
        private static void ExtendLines(int[] faces, Band band, int w, int h)
        {
            var n = w * h;
            var ink = new byte[n];
            for (var i = 0; i < n; i++) ink[i] = (byte)(faces[i] == 0 && !band.Filled(i % w, i / w) ? 1 : 0);
            var dist = Ops.Edt(ink, w, h);
            var contact = new byte[n];
            var inX = new float[n];
            var inY = new float[n];
            for (var i = 0; i < n; i++)
            {
                if (ink[i] == 0) continue;
                int x = i % w, y = i / w;
                for (var d = 0; d < 4; d++)
                {
                    int dx = d == 0 ? -1 : d == 1 ? 1 : 0, dy = d == 2 ? -1 : d == 3 ? 1 : 0, nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h || !band.Filled(nx, ny)) continue;
                    contact[i] = 1;
                    inX[i] += dx;
                    inY[i] += dy;
                }
            }
            var groups = Ops.Components(contact, w, h, true);
            var cnt = groups.Count;
            var members = new List<int>[cnt + 1];
            for (var i = 0; i < n; i++)
            {
                if (groups.Lab[i] <= 0) continue;
                (members[groups.Lab[i]] ??= new List<int>()).Add(i);
            }
            for (var g = 1; g <= cnt; g++)
            {
                var m = members[g];
                double cx = 0, cy = 0, tx = 0, ty = 0, thick = 0;
                int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
                foreach (var p in m)
                {
                    int px = p % w, py = p / w;
                    cx += px; cy += py; tx += inX[p]; ty += inY[p];
                    thick = Math.Max(thick, 2.0 * dist[p]);
                    minX = Math.Min(minX, px); maxX = Math.Max(maxX, px); minY = Math.Min(minY, py); maxY = Math.Max(maxY, py);
                }
                cx /= m.Count;
                cy /= m.Count;
                if (thick < 1.5 || tx * tx + ty * ty < 0.01) continue;
                if (Math.Max(maxX - minX, maxY - minY) + 1 > 3 * thick + 6) continue;
                var tl = Math.Sqrt(tx * tx + ty * ty);
                tx /= tl;
                ty /= tl;
                var dir = LineDirection(ink, w, h, cx, cy, tx, ty, LineProbe);
                if (dir == null) continue;
                if (dir[0] * tx + dir[1] * ty < 0) { dir[0] = -dir[0]; dir[1] = -dir[1]; }
                if (dir[0] * tx + dir[1] * ty < 0.3) continue;
                var r = thick * 0.5;
                var ri = (int)Math.Ceiling(r);
                for (double t = 0; t < w + h; t += 0.5)
                {
                    var qx = (int)Ops.JsRound(cx + dir[0] * t);
                    var qy = (int)Ops.JsRound(cy + dir[1] * t);
                    if (qx < -ri || qy < -ri || qx >= w + ri || qy >= h + ri) break;
                    for (var yy = -ri; yy <= ri; yy++)
                        for (var xx = -ri; xx <= ri; xx++)
                        {
                            int x2 = qx + xx, y2 = qy + yy;
                            if (x2 < 0 || y2 < 0 || x2 >= w || y2 >= h || xx * xx + yy * yy > r * r + 0.25) continue;
                            if (band.Filled(x2, y2)) faces[y2 * w + x2] = 0;
                        }
                }
            }
        }

        private sealed class FaceSet
        {
            public byte[] Free;
            public Labels Faces;
            public double Share;
        }

        /// <summary>px: RGBA (alpha 255), lab: Lab xen kẽ; null khi ảnh không thuộc loại viền đen.</summary>
        public static RegionMap Run(byte[] px, float[] lab, int w, int h)
        {
            var n = w * h;
            var peak = new float[n];
            for (var i = 0; i < n; i++) peak[i] = Math.Max(px[i * 4], Math.Max(px[i * 4 + 1], px[i * 4 + 2]));
            var closed = Ops.Close(peak, w, h, VeinWindow);
            var band = Bands(peak, w, h);

            // các mặt của ảnh với một ngưỡng nét; share = mặt lớn nhất / cả ảnh
            FaceSet MakeFaces(double dark)
            {
                var free = new byte[n];
                for (var j = 0; j < n; j++)
                    free[j] = (byte)(peak[j] >= dark && !((double)closed[j] - peak[j] > VeinContrast && peak[j] < VeinMax) && !band.Contains(j % w, j / w) ? 1 : 0);
                CloseSmallGaps(free, w, h);
                var f = Ops.Components(free, w, h, false);
                var ar = new int[f.Count + 1];
                var big = 0;
                for (var j = 0; j < n; j++) if (f.Lab[j] > 0) ar[f.Lab[j]]++;
                for (var j = 1; j <= f.Count; j++) if (ar[j] > big) big = ar[j];
                return new FaceSet { Free = free, Faces = f, Share = (double)big / n };
            }

            // nét xanh đậm mảnh trên nền đậm nằm trên ngưỡng đen và bị đứt: một mặt nuốt cả mảng ảnh thì thử lại với ngưỡng cao hơn
            var set = MakeFaces(DarkMaxBase);
            if (set.Share >= SwallowShare)
            {
                var alt = MakeFaces(DarkMaxHigh);
                if (alt.Share < set.Share - 0.05) set = alt;
            }
            var free2 = set.Free;
            var faces = set.Faces.Lab;
            var cnt = set.Faces.Count;
            var area = new int[cnt + 1];
            for (var i = 0; i < n; i++) if (faces[i] > 0) area[faces[i]]++;
            int kept = 0, covered = 0;
            for (var i = 1; i <= cnt; i++) if (area[i] >= MinArea) { kept++; covered += area[i]; }
            if (kept < MinPieces || covered < MinCoverage * n) return null;
            for (var i = 0; i < n; i++) if (faces[i] > 0 && area[faces[i]] < MinArea) faces[i] = 0;
            FillCells(faces, free2, w, h, band);
            ExtendLines(faces, band, w, h);
            var ids = Ops.Compact(faces);
            var cols = RegionOps.FaceColors(lab, faces, ids, free2);
            var map = new RegionMap { W = w, H = h, Reg = (int[])faces.Clone(), Inked = true, Gaps = true, Synthetic = false };
            for (var id = 1; id <= ids; id++) map.Colors.Add(cols[id]);
            return map;
        }
    }
}
