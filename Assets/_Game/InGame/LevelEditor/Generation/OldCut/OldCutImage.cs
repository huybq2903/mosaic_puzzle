using System;
using System.Collections.Generic;

namespace Falcon.InGame.LevelEditor.Gen
{
    /// <summary>Kết quả biến đổi khoảng cách: bình phương khoảng cách và (tuỳ chọn) chỉ số điểm gần nhất.</summary>
    public sealed class EdtResult
    {
        public float[] D2;
        public int[] Nearest;
    }

    /// <summary>Bộ công cụ ảnh của bộ cắt cũ (port từ oldcut.js, số học float32 như bản C++).</summary>
    public static class OldCutImage
    {
        private const float KInf = 1e9f;
        private static readonly float Small = (float)Math.Pow(2, -120);
        private static readonly float Inv255 = F(1.0 / 255);

        /// <summary>Math.fround của JS.</summary>
        public static float F(double v) => (float)v;

        private static void Dt1d(float[] f, int n, float[] d, int[] arg, int[] v, double[] z)
        {
            int k = 0;
            v[0] = 0; z[0] = -1e30; z[1] = 1e30;
            for (var q = 1; q < n; q++)
            {
                var s = ((f[q] + (double)q * q) - (f[v[k]] + (double)v[k] * v[k])) / (2.0 * q - 2.0 * v[k]);
                while (s <= z[k])
                {
                    k--;
                    s = ((f[q] + (double)q * q) - (f[v[k]] + (double)v[k] * v[k])) / (2.0 * q - 2.0 * v[k]);
                }
                k++; v[k] = q; z[k] = s; z[k + 1] = 1e30;
            }
            k = 0;
            for (var q = 0; q < n; q++)
            {
                while (z[k + 1] < q) k++;
                d[q] = (float)((double)(q - v[k]) * (q - v[k]) + f[v[k]]);
                arg[q] = v[k];
            }
        }

        /// <summary>Bình phương khoảng cách tới điểm đặc trưng gần nhất (feat != 0); wantNearest: kèm chỉ số điểm đó.</summary>
        public static EdtResult Edt(byte[] feat, int w, int h, bool wantNearest = false)
        {
            var m = Math.Max(w, h);
            var f = new float[m];
            var d = new float[m];
            var arg = new int[m];
            var v = new int[m];
            var z = new double[m + 1];
            var d1 = new float[w * h];
            var ay = new int[w * h];
            var d2 = new float[w * h];
            var nearest = wantNearest ? new int[w * h] : null;
            for (var x = 0; x < w; x++)
            {
                for (var y = 0; y < h; y++) f[y] = feat[y * w + x] != 0 ? 0 : KInf;
                Dt1d(f, h, d, arg, v, z);
                for (var y = 0; y < h; y++) { d1[y * w + x] = d[y]; ay[y * w + x] = arg[y]; }
            }
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++) f[x] = d1[y * w + x];
                Dt1d(f, w, d, arg, v, z);
                for (var x = 0; x < w; x++)
                {
                    d2[y * w + x] = d[x];
                    if (nearest != null) nearest[y * w + x] = ay[y * w + arg[x]] * w + arg[x];
                }
            }
            return new EdtResult { D2 = d2, Nearest = nearest };
        }

        /// <summary>Gán nhãn 4 láng giềng cho mask != 0: nhãn từ 1 theo thứ tự quét của điểm đầu.</summary>
        public static Labels Label4(byte[] mask, int w, int h)
        {
            var lab = new int[w * h];
            var n = 0;
            var stack = new Stack<int>();
            for (var s = 0; s < w * h; s++)
            {
                if (mask[s] == 0 || lab[s] != 0) continue;
                n++; lab[s] = n; stack.Push(s);
                while (stack.Count > 0)
                {
                    var p = stack.Pop();
                    int x = p % w, y = p / w;
                    if (x > 0 && mask[p - 1] != 0 && lab[p - 1] == 0) { lab[p - 1] = n; stack.Push(p - 1); }
                    if (x + 1 < w && mask[p + 1] != 0 && lab[p + 1] == 0) { lab[p + 1] = n; stack.Push(p + 1); }
                    if (y > 0 && mask[p - w] != 0 && lab[p - w] == 0) { lab[p - w] = n; stack.Push(p - w); }
                    if (y + 1 < h && mask[p + w] != 0 && lab[p + w] == 0) { lab[p + w] = n; stack.Push(p + w); }
                }
            }
            return new Labels { Lab = lab, Count = n };
        }

        public static byte[] DilateDisk(byte[] inp, int w, int h, double r)
        {
            var d2 = Edt(inp, w, h).D2;
            var res = new byte[inp.Length];
            var r2 = F(r * r);
            for (var i = 0; i < inp.Length; i++) res[i] = d2[i] <= r2 ? (byte)1 : (byte)0;
            return res;
        }

        public static byte[] Morph3(byte[] inp, int w, int h, bool dilate)
        {
            var res = new byte[inp.Length];
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    bool any = false, all = true;
                    for (var dy = -1; dy <= 1; dy++)
                        for (var dx = -1; dx <= 1; dx++)
                        {
                            int xx = x + dx, yy = y + dy;
                            var v = (xx >= 0 && xx < w && yy >= 0 && yy < h) ? inp[yy * w + xx] != 0 : !dilate;
                            any = any || v; all = all && v;
                        }
                    res[y * w + x] = (dilate ? any : all) ? (byte)1 : (byte)0;
                }
            return res;
        }

        /// <summary>Làm mờ Gauss tách trục trên chỗ, số học float, mép kẹp.</summary>
        public static void GaussBlur(float[] a, int w, int h, double sigma)
        {
            var r = Math.Max(1, (int)Math.Ceiling(sigma * 3));
            var k = new float[2 * r + 1];
            double sum = 0;
            for (var i = -r; i <= r; i++) { k[i + r] = (float)Math.Exp(-(double)i * i / (2 * sigma * sigma)); sum += k[i + r]; }
            for (var i = 0; i < k.Length; i++) k[i] = (float)(k[i] / sum);
            var tmp = new float[a.Length];
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    float s = 0;
                    for (var i = -r; i <= r; i++) s = (float)(s + (float)((double)k[i + r] * a[y * w + Math.Min(Math.Max(x + i, 0), w - 1)]));
                    tmp[y * w + x] = s;
                }
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    float s = 0;
                    for (var i = -r; i <= r; i++) s = (float)(s + (float)((double)k[i + r] * tmp[Math.Min(Math.Max(y + i, 0), h - 1) * w + x]));
                    a[y * w + x] = s;
                }
        }

        // ---------- resize kiểu stb_image_resize2 ----------
        private static double Catmull(double x)
        {
            x = Math.Abs(x);
            if (x < 1) return F(1 - F(F(x * x) * F(F(2.5) - F(F(1.5) * x))));
            if (x < 2) return F(2 - F(x * F(4 + F(x * F(F(F(0.5) * x) - F(2.5))))));
            return 0;
        }

        private static double Mitchell(double x)
        {
            x = Math.Abs(x);
            if (x < 1) return F(F(16 + F(F(x * x) * F(F(21 * x) - 36))) / 18);
            if (x < 2) return F(F(32 + F(x * F(-60 + F(x * F(36 - F(7 * x)))))) / 18);
            return 0;
        }

        private sealed class Contrib
        {
            public int N0, N1;
            public double[] C;
        }

        private static void SetAt(List<double> l, int i, double v)
        {
            while (l.Count <= i) l.Add(0);
            l[i] = v;
        }

        private static Contrib[] FilterAxis(int inSize, int outSize)
        {
            double scaleD = (double)outSize / inSize;
            var scale = F(scaleD);
            var inv = F(1 / scaleD);
            var res = new Contrib[outSize];
            var up = scale >= F(1 - Small);
            if (up && scale <= F(1 + Small))
            {
                for (var n = 0; n < outSize; n++) res[n] = new Contrib { N0 = n, N1 = n, C = new double[] { 1 } };
                return res;
            }
            Func<double, double> kernel = up ? (Func<double, double>)Catmull : Mitchell;
            if (up)
            {
                var radius = F(2 * scale);
                for (var n = 0; n < outSize; n++)
                {
                    double oc = F(n + 0.5), inCenter = F(oc * inv), lo = F(F(oc - radius) * inv), hi = F(F(oc + radius) * inv);
                    var first = (int)Math.Floor(F(lo + 0.5));
                    var last = (int)Math.Floor(F(hi - 0.5));
                    var cs = new List<double>();
                    var lastNz = -1;
                    if (last < first) last = first;
                    for (var i = 0; i <= last - first; i++)
                    {
                        double ipc = F(first + i + 0.5), cf = kernel(F(inCenter - ipc));
                        if (cf < Small && cf > -Small)
                        {
                            if (i == 0) { first++; i--; continue; }
                            cf = 0;
                        }
                        else lastNz = i;
                        SetAt(cs, i, cf);
                    }
                    res[n] = Finish(first, first + lastNz, cs.GetRange(0, lastNz + 1), inSize);
                }
            }
            else
            {
                var inRadius = F(2 * inv);
                var margin = (int)Math.Floor(Math.Ceiling(F(F(2 * 2) / scale)) / 2);
                var n0 = new int[outSize];
                var n1 = new int[outSize];
                var cl = new List<double>[outSize];
                var init = new bool[outSize];
                for (var n = 0; n < outSize; n++) { n1[n] = -1; cl[n] = new List<double>(); }
                for (var ip = -margin; ip < inSize + margin; ip++)
                {
                    double ic = F(ip + 0.5), ocIn = F(ic * scale), lo2 = F(F(ic - inRadius) * scale), hi2 = F(F(ic + inRadius) * scale);
                    var of = (int)Math.Floor(F(lo2 + 0.5));
                    var ol = (int)Math.Floor(F(hi2 - 0.5));
                    if (of < 0) of = 0;
                    if (ol >= outSize) ol = outSize - 1;
                    if (of > ol) continue;
                    for (var i = 0; i <= ol - of; i++)
                    {
                        var o = of + i;
                        double x = F(F(o + 0.5) - ocIn), cf2 = F(kernel(x) * scale);
                        if (cf2 < Small && cf2 > -Small) cf2 = 0;
                        if (!init[o]) { init[o] = true; n0[o] = ip; n1[o] = ip; SetAt(cl[o], 0, cf2); }
                        else
                        {
                            if (cl[o][0] == 0) n0[o] = ip;
                            n1[o] = ip;
                            SetAt(cl[o], ip - n0[o], cf2);
                        }
                    }
                }
                for (var n = 0; n < outSize; n++) res[n] = Finish(n0[n], n1[n], cl[n], inSize);
            }
            return res;
        }

        // chuẩn hoá trọng số (tổng double) và gấp phần ngoài ảnh vào điểm mép
        private static Contrib Finish(int n0, int n1, List<double> c, int inSize)
        {
            double total = 0;
            var e = n1 - n0;
            for (var i = 0; i <= e; i++) total += c[i];
            if (total < Small && total > -Small) return new Contrib { N0 = n0, N1 = n0, C = new double[] { 0 } };
            if (total < F(1 - Small) || total > F(1 + Small))
            {
                var fs = 1 / total;
                for (var i = 0; i <= e; i++) c[i] = F(c[i] * fs);
            }
            var acc = new Dictionary<int, double>();
            int lo = int.MaxValue, hi = int.MinValue;
            for (var i = 0; i <= e; i++)
            {
                var p = Math.Min(Math.Max(n0 + i, 0), inSize - 1);
                acc.TryGetValue(p, out var cur);
                acc[p] = F(cur + c[i]);
                if (p < lo) lo = p;
                if (p > hi) hi = p;
            }
            var cc = new double[hi - lo + 1];
            for (var i = lo; i <= hi; i++) cc[i - lo] = acc.TryGetValue(i, out var val) ? val : 0;
            return new Contrib { N0 = lo, N1 = hi, C = cc };
        }

        private static byte Enc(double t)
        {
            var q = F(F(t * 255) + 0.5);
            return q < 0 ? (byte)0 : q > 255 ? (byte)255 : (byte)(int)q;
        }

        /// <summary>RGB (w*h*3) → RGB (w2*h2*3), lọc ngang rồi dọc.</summary>
        public static byte[] ResizeRgb(byte[] src, int w, int h, int w2, int h2)
        {
            var hx = FilterAxis(w, w2);
            var vy = FilterAxis(h, h2);
            var res = new byte[w2 * h2 * 3];
            var mid = new float[w2 * h * 3];
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w2; x++)
                {
                    var ct = hx[x];
                    for (var c = 0; c < 3; c++)
                    {
                        double s = 0;
                        for (var i = ct.N0; i <= ct.N1; i++) s = F(s + F(ct.C[i - ct.N0] * F(src[(y * w + i) * 3 + c] * Inv255)));
                        mid[(y * w2 + x) * 3 + c] = (float)s;
                    }
                }
            for (var y = 0; y < h2; y++)
            {
                var cy = vy[y];
                for (var x = 0; x < w2; x++)
                    for (var c = 0; c < 3; c++)
                    {
                        double t = 0;
                        for (var i = cy.N0; i <= cy.N1; i++) t = F(t + F(cy.C[i - cy.N0] * mid[(i * w2 + x) * 3 + c]));
                        res[(y * w2 + x) * 3 + c] = Enc(t);
                    }
            }
            return res;
        }

        public static int MaxC(byte[] px, int i)
        {
            int a = px[i * 3], b = px[i * 3 + 1], c = px[i * 3 + 2];
            return a > b ? (a > c ? a : c) : (b > c ? b : c);
        }

        private static int EdgeOf(double[] prof, int n, bool rev, bool safe)
        {
            var span = (int)(n * 0.08);
            var hit = 0;
            var found = false;
            if (safe)
            {
                var first = -1;
                var near = (int)(n * 0.04);
                for (var j0 = 0; j0 < near && first < 0; j0++) if (prof[rev ? n - 1 - j0 : j0] > 0.55) first = j0;
                if (first >= 0)
                {
                    var last = first;
                    var gap = 0;
                    for (var j0 = first; j0 < span && gap <= 2; j0++)
                    {
                        if (prof[rev ? n - 1 - j0 : j0] > 0.55) { last = j0; gap = 0; } else gap++;
                    }
                    hit = rev ? n - 1 - last : last;
                    found = true;
                }
                return found ? hit : (rev ? n : -1);
            }
            for (var j = 0; j < span; j++)
            {
                var i = rev ? n - 1 - j : j;
                if (prof[i] > 0.55) { hit = i; found = true; }
            }
            return found ? hit : (rev ? n : -1);
        }

        /// <summary>Cắt vào trong khung đen ngoài cùng (bỏ lề trắng bên ngoài); safe: khung phải bắt đầu trong 4% mép.</summary>
        public static byte[] CropFrame(byte[] img, int w, int h, byte[] dark, bool safe, out int ow, out int oh)
        {
            var rows = new double[h];
            var cols = new double[w];
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                    if (dark[y * w + x] != 0) { rows[y] += 1.0 / w; cols[x] += 1.0 / h; }
            int t = EdgeOf(rows, h, false, safe), b = EdgeOf(rows, h, true, safe), l = EdgeOf(cols, w, false, safe), r = EdgeOf(cols, w, true, safe);
            if (b - t < h * 0.5 || r - l < w * 0.5) { ow = w; oh = h; return img; }
            ow = r - l - 1; oh = b - t - 1;
            var res = new byte[ow * oh * 3];
            for (var y = 0; y < oh; y++) Buffer.BlockCopy(img, ((t + 1 + y) * w + l + 1) * 3, res, y * ow * 3, ow * 3);
            return res;
        }

        /// <summary>Ảnh làm việc: cắt khung rồi co về work x work.</summary>
        public static byte[] WorkImage(byte[] rgb, int w, int h, int work, int lineDark, bool safeFrame)
        {
            var dark = new byte[w * h];
            for (var i = 0; i < dark.Length; i++) dark[i] = MaxC(rgb, i) < lineDark ? (byte)1 : (byte)0;
            var cr = CropFrame(rgb, w, h, dark, safeFrame, out var cw, out var ch);
            return ResizeRgb(cr, cw, ch, work, work);
        }

        /// <summary>Ngưỡng mực: ảnh có nền gần đen thì ngưỡng tụt xuống dưới đỉnh thứ hai để nền không bị đọc là mực.</summary>
        public static int InkThreshold(byte[] rgb, int w, int h, int dflt)
        {
            var bins = new double[dflt / 5];
            var n = w * h;
            for (var i = 0; i < n; i++)
            {
                var m = MaxC(rgb, i);
                if (m < dflt) bins[m / 5]++;
            }
            var peak = 2;
            var sorted = new double[bins.Length - 2];
            Array.Copy(bins, 2, sorted, 0, sorted.Length);
            Array.Sort(sorted);
            var floor = sorted[(bins.Length - 2) >> 1];
            for (var i = 3; i < bins.Length; i++) if (bins[i] > bins[peak]) peak = i;
            if (bins[peak] < n * 0.005 || bins[peak] < 6 * Math.Max(floor, 1)) return dflt;
            var lowMax = bins[0];
            for (var v = 1; v < peak; v++)
            {
                if (lowMax >= 0.3 * bins[peak] && bins[v] <= 0.1 * lowMax) return Math.Max(20, peak * 5 - 10);
                lowMax = Math.Max(lowMax, bins[v]);
            }
            return dflt;
        }
    }
}
