using System;
using System.Collections.Generic;

namespace Falcon.InGame.LevelEditor.Gen
{
    /// <summary>Tham số tạo tranh (port từ MS.defaults của Mosaic Studio).</summary>
    public sealed class GenOptions
    {
        public int WorkSize = 640, FrameW = 100, FrameH = 100, SmoothScale = 3, MaxPieces = 150;
        public bool FitCover = true;
        public double CurveSmooth = 3, FitTolerance = 1.6;
    }

    /// <summary>Ảnh nguồn RGBA 8 bit.</summary>
    public sealed class SourceImage
    {
        public byte[] Px;
        public int Width, Height;
    }

    public struct IntRect
    {
        public int X, Y, W, H;
    }

    public sealed class GenRegion
    {
        public int Id;
        public int[] Points;
        public int ColorId;
        public int Parent; // mảnh mới do chia: Id của mảnh gốc (0 = mảnh có sẵn)
        public List<int[]> Holes;
    }

    /// <summary>Kết quả vector hoá: toạ độ nguyên theo 1/Unit của lưới Width×Height.</summary>
    public sealed class GeneratedPicture
    {
        public int Width, Height, Unit;
        public string Engine; // old = bộ cắt cũ, null = route viền đen
        public List<GenRegion> Regions = new();
        public List<int[]> Decor = new(); // nét trang trí: vòng kín, tô nonzero, cùng đơn vị với Points
    }

    /// <summary>Ảnh → mảnh: cắt/đặt vào khung, route viền đen, dọn mảnh, vector hoá biên (port từ generate.js của Mosaic Studio, đường viền đen).</summary>
    public static class PictureGenerator
    {
        public const int Unit = 4;
        private const double SigmaPerLevel = 0.57, SpeckFraction = 0.0004, GaplessTextRadius = 0.008;
        private const int IslandMaxArea = 150, IslandReach = 7, BorderDiff = 70, TidyStep = 6;
        private const double MinSubjectFraction = 0.6;

        // ================= đặt ảnh vào khung =================
        public struct Placement
        {
            public double PX, PY, PW, PH, UX, UY, UW, UH;
        }

        /// <summary>Vị trí ảnh trong khung (0..1, y xuống) và phần ảnh dùng (uv 0..1); cover: phủ kín khung, cắt phần thừa.</summary>
        public static Placement Place(double srcW, double srcH, double frameW, double frameH, bool cover)
        {
            var sa = srcW / srcH;
            var fa = frameW / frameH;
            var full = new Placement { PX = 0, PY = 0, PW = 1, PH = 1, UX = 0, UY = 0, UW = 1, UH = 1 };
            if (Math.Abs(sa - fa) < 1e-6) return full;
            var wider = sa > fa;
            if (cover)
            {
                var keep = wider ? fa / sa : sa / fa;
                full.UX = wider ? (1 - keep) * 0.5 : 0;
                full.UY = wider ? 0 : (1 - keep) * 0.5;
                full.UW = wider ? keep : 1;
                full.UH = wider ? 1 : keep;
                return full;
            }
            var fill = wider ? fa / sa : sa / fa;
            full.PX = wider ? 0 : (1 - fill) * 0.5;
            full.PY = wider ? (1 - fill) * 0.5 : 0;
            full.PW = wider ? 1 : fill;
            full.PH = wider ? fill : 1;
            return full;
        }

        /// <summary>Cạnh dài của canvas = longSide, giữ tỉ lệ khung.</summary>
        public static (int w, int h) CanvasSize(int frameW, int frameH, int longSide)
        {
            var k = (double)longSide / Math.Max(frameW, frameH);
            return (Math.Max(1, (int)Ops.JsRound(frameW * k)), Math.Max(1, (int)Ops.JsRound(frameH * k)));
        }

        // lấy mẫu song tuyến tính (tâm pixel ở +0.5, mép kẹp); u, v trong 0..1 → [r,g,b,a] 0..255
        private static double[] Bilinear(SourceImage src, double u, double v)
        {
            int w = src.Width, h = src.Height;
            var x = u * w - 0.5;
            var y = v * h - 0.5;
            var x0 = (int)Math.Floor(x);
            var y0 = (int)Math.Floor(y);
            var fx = x - x0;
            var fy = y - y0;
            int xa = Ops.Clamp(x0, 0, w - 1), xb = Ops.Clamp(x0 + 1, 0, w - 1), ya = Ops.Clamp(y0, 0, h - 1), yb = Ops.Clamp(y0 + 1, 0, h - 1);
            var px = src.Px;
            var o = new double[4];
            for (var c = 0; c < 4; c++)
            {
                double p00 = px[(ya * w + xa) * 4 + c], p10 = px[(ya * w + xb) * 4 + c], p01 = px[(yb * w + xa) * 4 + c], p11 = px[(yb * w + xb) * 4 + c];
                o[c] = (p00 + (p10 - p00) * fx) * (1 - fy) + (p01 + (p11 - p01) * fx) * fy;
            }
            return o;
        }

        private static int Diff(byte[] px, int i, int r, int g, int b) => Math.Abs(px[i * 4] - r) + Math.Abs(px[i * 4 + 1] - g) + Math.Abs(px[i * 4 + 2] - b);

        // hộp bao các điểm khác màu viền (đồng nhất); cả ảnh khi không có nền đồng nhất
        private static int[] SubjectBounds(SourceImage src)
        {
            var px = src.Px;
            int w = src.Width, h = src.Height;
            long r = 0, g = 0, b = 0, n = 0;
            void Add(int k) { r += px[k * 4]; g += px[k * 4 + 1]; b += px[k * 4 + 2]; n++; }
            for (var x = 0; x < w; x++) { Add(x); Add((h - 1) * w + x); }
            for (var y = 1; y < h - 1; y++) { Add(y * w); Add(y * w + w - 1); }
            int mr = (int)(r / n), mg = (int)(g / n), mb = (int)(b / n);
            int dev = 0, total = 0;
            for (var x = 0; x < w; x++) { dev += Diff(px, x, mr, mg, mb) > BorderDiff ? 1 : 0; total++; }
            var whole = new[] { 0, 0, w - 1, h - 1 };
            if (dev > 0.1 * total) return whole;
            var cols = new int[w];
            var rows = new int[h];
            int mx = Math.Max(1, w * 3 / 200), my = Math.Max(1, h * 3 / 200);
            for (var y = my; y < h - my; y++)
                for (var x = mx; x < w - mx; x++)
                {
                    if (Diff(px, y * w + x, mr, mg, mb) <= BorderDiff) continue;
                    cols[x]++;
                    rows[y]++;
                }
            int cmin = Math.Max(3, h / 250), rmin = Math.Max(3, w / 250), x0 = w, y0 = h, x1 = -1, y1 = -1;
            for (var x = 0; x < w; x++) if (cols[x] >= cmin) { x0 = Math.Min(x0, x); x1 = x; }
            for (var y = 0; y < h; y++) if (rows[y] >= rmin) { y0 = Math.Min(y0, y); y1 = y; }
            if (x1 < 0 || (long)(x1 - x0 + 1) * (y1 - y0 + 1) > MinSubjectFraction * w * h) return whole;
            return new[] { x0, y0, x1, y1 };
        }

        /// <summary>Hộp quanh các điểm đục (alpha &gt; 128) cộng lề nhỏ; vật nhỏ trên nền trơn được cắt gọn.</summary>
        public static IntRect OpaqueBounds(SourceImage src)
        {
            int w = src.Width, h = src.Height, x0 = w, y0 = h, x1 = -1, y1 = -1;
            var px = src.Px;
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    if (px[(y * w + x) * 4 + 3] <= 128) continue;
                    if (x < x0) x0 = x;
                    if (x > x1) x1 = x;
                    if (y < y0) y0 = y;
                    if (y > y1) y1 = y;
                }
            if (x1 < 0) return new IntRect { X = 0, Y = 0, W = w, H = h };
            if (x0 == 0 && y0 == 0 && x1 == w - 1 && y1 == h - 1)
            {
                var sb = SubjectBounds(src);
                x0 = sb[0]; y0 = sb[1]; x1 = sb[2]; y1 = sb[3];
            }
            var pad = Math.Max(1, (int)Ops.JsRound(0.03 * Math.Max(x1 - x0 + 1, y1 - y0 + 1)));
            x0 = Math.Max(0, x0 - pad);
            y0 = Math.Max(0, y0 - pad);
            x1 = Math.Min(w - 1, x1 + pad);
            y1 = Math.Min(h - 1, y1 + pad);
            return new IntRect { X = x0, Y = y0, W = x1 - x0 + 1, H = y1 - y0 + 1 };
        }

        // nền cho phần lề: trung bình viền ảnh (ảnh đặc) hoặc màu bảng xa vật nhất (ảnh trong suốt)
        private static int[] Background(SourceImage src, IntRect crop)
        {
            var px = src.Px;
            long r = 0, g = 0, b = 0, n = 0;
            var transparent = false;
            var used = new int[Palette.Count];
            var sn = 0;
            for (var y = crop.Y; y < crop.Y + crop.H; y++)
                for (var x = crop.X; x < crop.X + crop.W; x++)
                {
                    var i = (y * src.Width + x) * 4;
                    if (px[i + 3] <= 128) { transparent = true; continue; }
                    if (((x + y) & 1) == 0)
                    {
                        var lab = Palette.ToLab(px[i], px[i + 1], px[i + 2]);
                        used[Palette.NearestLab(lab[0], lab[1], lab[2])]++;
                        sn++;
                    }
                    if (x != crop.X && x != crop.X + crop.W - 1 && y != crop.Y && y != crop.Y + crop.H - 1) continue;
                    r += px[i]; g += px[i + 1]; b += px[i + 2]; n++;
                }
            if (transparent || n == 0)
            {
                var subject = new List<double[]>();
                for (var k = 0; k < used.Length; k++) if (sn > 0 && used[k] >= 0.005 * sn) subject.Add(Palette.Lab[k]);
                var best = 0;
                double bd = -1;
                for (var c = 0; c < Palette.Limit; c++)
                {
                    var l = Palette.Lab[c];
                    if (l[0] < 70 || Math.Sqrt(l[1] * l[1] + l[2] * l[2]) > 30) continue;
                    var d = double.PositiveInfinity;
                    foreach (var s in subject)
                    {
                        double a0 = l[0] - s[0], a1 = l[1] - s[1], a2 = l[2] - s[2];
                        d = Math.Min(d, Math.Sqrt(a0 * a0 + a1 * a1 + a2 * a2));
                    }
                    if (d > bd) { bd = d; best = c; }
                }
                return new[] { (int)Palette.Rgb[best][0], Palette.Rgb[best][1], Palette.Rgb[best][2] };
            }
            return new[] { (int)(r / n), (int)(g / n), (int)(b / n) };
        }

        public sealed class Composed
        {
            public byte[] Px;
            public int W, H;
            public IntRect Crop;
        }

        /// <summary>Cắt theo vật, đặt vào khung, lấy mẫu lại ra canvas làm việc (cạnh dài = workSize); canvas luôn đục.</summary>
        public static Composed Compose(SourceImage src, GenOptions s, IntRect? knownCrop = null)
        {
            var crop = knownCrop ?? OpaqueBounds(src);
            var (w, h) = CanvasSize(s.FrameW, s.FrameH, s.WorkSize);
            var pl = Place(crop.W, crop.H, s.FrameW, s.FrameH, s.FitCover);
            var bg = Background(src, crop);
            var px = new byte[w * h * 4];
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    double u = (x + 0.5) / w, v = (y + 0.5) / h;
                    var o = (y * w + x) * 4;
                    if (u < pl.PX || u >= pl.PX + pl.PW || v < pl.PY || v >= pl.PY + pl.PH)
                    {
                        px[o] = (byte)bg[0]; px[o + 1] = (byte)bg[1]; px[o + 2] = (byte)bg[2]; px[o + 3] = 255;
                        continue;
                    }
                    var su = pl.UX + (u - pl.PX) / pl.PW * pl.UW;
                    var sv = pl.UY + (v - pl.PY) / pl.PH * pl.UH;
                    var t = Bilinear(src, (crop.X + su * crop.W) / src.Width, (crop.Y + sv * crop.H) / src.Height);
                    var a = t[3] / 255;
                    px[o] = ClampByte(Ops.JsRound(bg[0] + (t[0] - bg[0]) * a));
                    px[o + 1] = ClampByte(Ops.JsRound(bg[1] + (t[1] - bg[1]) * a));
                    px[o + 2] = ClampByte(Ops.JsRound(bg[2] + (t[2] - bg[2]) * a));
                    px[o + 3] = 255;
                }
            return new Composed { Px = px, W = w, H = h, Crop = crop };
        }

        private static byte ClampByte(double v) => (byte)(v < 0 ? 0 : v > 255 ? 255 : v);

        // ================= ảnh → bản đồ vùng đã dọn =================

        /// <summary>Bản đồ vùng sau dọn mảnh; null khi ảnh không phải ảnh viền đen. stage: móc quan sát từng bước.</summary>
        public static RegionMap RegionsOf(byte[] px, int w, int h, GenOptions s, Action<string, RegionMap> stage = null)
        {
            var lab = Ops.LabOf(px, w * h);
            var map = BlackLineRoute.Run(px, lab, w, h);
            stage?.Invoke("route", map);
            if (map == null) return null;
            map.Route = "black-line";
            var scale = Math.Max(1, s.SmoothScale);
            if (scale > 1) map = RegionOps.Upscale(map, scale);
            stage?.Invoke("upscale", map);
            RegionOps.AbsorbIslands(map, IslandMaxArea, IslandReach);
            stage?.Invoke("absorb", map);
            RegionOps.RemoveSpecks(map, SpeckFraction);
            stage?.Invoke("specks", map);
            RegionOps.LimitCount(map, s.MaxPieces);
            stage?.Invoke("limit", map);
            RegionOps.EnsureTextFits(map, GaplessTextRadius * Math.Min(map.W, map.H)); // các mảnh chạm nhau giữ mảnh nhỏ (cuống, vụn): nút giao của chúng nằm cách nhau
            stage?.Invoke("textfits", map);
            RegionOps.FillGaps(map); // các mảnh chia chung biên như level cũ
            stage?.Invoke("fillgaps", map);
            return map;
        }

        /// <summary>Bản đồ vùng → đa giác trên lưới làm việc (nhân Unit).</summary>
        public static GeneratedPicture Vectorise(RegionMap map, GenOptions s)
        {
            RegionOps.RemovePinches(map);
            var sigma = s.CurveSmooth * SigmaPerLevel * Math.Max(1, s.SmoothScale);
            var g = BoundaryGraph.Build(map, sigma, Unit, TidyStep, s.FitTolerance);
            int maxX = map.W * Unit, maxY = map.H * Unit;
            void ClampRing(int[] a)
            {
                if (a == null) return;
                for (var k = 0; k + 1 < a.Length; k += 2)
                {
                    a[k] = Ops.Clamp(a[k], 0, maxX);
                    a[k + 1] = Ops.Clamp(a[k + 1], 0, maxY);
                }
            }
            var pic = new GeneratedPicture { Width = map.W, Height = map.H, Unit = Unit };
            for (var id = 1; id < g.Polys.Length; id++)
            {
                if (g.Polys[id] == null) continue;
                ClampRing(g.Polys[id]);
                g.Holes[id]?.ForEach(ClampRing);
                pic.Regions.Add(new GenRegion { Id = id, Points = g.Polys[id], ColorId = map.Colors[id], Holes = g.Holes[id] });
            }
            return pic;
        }

        /// <summary>Ảnh nguồn → các mảnh (khung vuông thử bộ cắt cũ trước); null khi không phải ảnh viền đen.</summary>
        public static GeneratedPicture Generate(SourceImage src, GenOptions s, Action<string, RegionMap> stage = null, IntRect? knownCrop = null)
        {
            s ??= new GenOptions();
            if (s.FrameW == s.FrameH)
            {
                GeneratedPicture old = null;
                try { old = OldCutGenerate.Generate(src, s); }
                catch (Exception) { old = null; } // lỗi bộ cắt cũ thì rơi về route viền đen
                if (old != null) return old;
            }
            var comp = Compose(src, s, knownCrop);
            var map = RegionsOf(comp.Px, comp.W, comp.H, s, stage);
            return map == null ? null : Vectorise(map, s);
        }
    }
}
