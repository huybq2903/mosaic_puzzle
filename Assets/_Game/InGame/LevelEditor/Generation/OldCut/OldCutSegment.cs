using System;
using System.Collections.Generic;

namespace Falcon.InGame.LevelEditor.Gen
{
    /// <summary>Tham số bộ cắt cũ (mặc định của phương pháp Outlines).</summary>
    public sealed class OldCutParams
    {
        public int Work = 1024, LineDark = 80, Bridge = 12, GrowTips = 8;
        public double CloseGap = 1, MinArea = 0.00008;
        public bool Decor = true, SafeFrame = true, JoinDeco = true, DarkFill = true;
    }

    /// <summary>Kết quả phân vùng: bản đồ nhãn 0..n-1 trên lưới Work x Work.</summary>
    public sealed class OldCutSeg
    {
        public int W, LineDark;
        public int[] Labels;
        public List<int[]> Colors;
        public byte[] Deco;
        public double LineW, InkW;
        public byte[] Work;
    }

    /// <summary>Bộ phân vùng Outlines của bộ cắt cũ (port từ oldcut.js, segOutlines).</summary>
    public static class OldCutSegment
    {
        private static float F(double v) => (float)v;

        private static int Median(int[] hist, int count)
        {
            // trung vị từ histogram, cắt phần thập phân như Math.trunc
            var mi = count / 2;
            var hi = Kth(hist, mi);
            if (count % 2 == 1) return hi;
            return (int)((Kth(hist, mi - 1) + hi) / 2.0);
        }

        private static int Kth(int[] hist, int k)
        {
            var cum = 0;
            for (var v = 0; v < 256; v++)
            {
                cum += hist[v];
                if (cum > k) return v;
            }
            return 255;
        }

        public static OldCutSeg Run(byte[] rgb, int w, int h, OldCutParams p)
        {
            var lineDark = p.LineDark;
            if (p.DarkFill) lineDark = OldCutImage.InkThreshold(rgb, w, h, p.LineDark);
            int W = p.Work, npx = W * W, i, x, y;
            var img = OldCutImage.WorkImage(rgb, w, h, W, lineDark, p.SafeFrame);
            var dark = new byte[npx];
            for (i = 0; i < npx; i++) dark[i] = OldCutImage.MaxC(img, i) < lineDark ? (byte)1 : (byte)0;
            // nét mực dày hơn cỡ này là vùng tối thật (chân đen, mắt...)
            var k = Math.Max(3, (int)(W * 0.022)) | 1;
            var r2 = F(((k - 1) / 2) * ((k - 1) / 2));
            var feat = new byte[npx];
            var eroded = new byte[npx];
            var thick = new byte[npx];
            var lines = new byte[npx];
            for (i = 0; i < npx; i++) feat[i] = dark[i] != 0 ? (byte)0 : (byte)1;
            var d2 = OldCutImage.Edt(feat, W, W).D2;
            for (i = 0; i < npx; i++) eroded[i] = dark[i] != 0 && d2[i] > r2 ? (byte)1 : (byte)0;
            d2 = OldCutImage.Edt(eroded, W, W).D2;
            for (i = 0; i < npx; i++) thick[i] = d2[i] <= r2 ? (byte)1 : (byte)0;
            // vùng tối lớn có thể mang nét riêng chỉ đậm hơn nền một chút
            {
                var val = new float[npx];
                for (i = 0; i < npx; i++) val[i] = OldCutImage.MaxC(img, i);
                OldCutImage.GaussBlur(val, W, W, Math.Max(1.0, W / 400.0));
                var tlr = OldCutImage.Label4(thick, W, W);
                var tl = tlr.Lab;
                var nt = tlr.Count;
                var vals = new List<int>[nt + 1];
                for (i = 0; i <= nt; i++) vals[i] = new List<int>();
                for (i = 0; i < npx; i++) if (tl[i] != 0) vals[tl[i]].Add(OldCutImage.MaxC(img, i));
                var thr = new float[nt + 1];
                for (var c = 1; c <= nt; c++)
                {
                    var v = vals[c];
                    if (v.Count < npx * 0.004) continue;
                    v.Sort();
                    double m = v[v.Count / 2];
                    if (m >= 14 && m <= 70) thr[c] = F(F(0.72) * m);
                }
                var inner = new byte[npx];
                var anyInner = false;
                for (i = 0; i < npx; i++) if (tl[i] != 0 && thr[tl[i]] > 0 && val[i] < thr[tl[i]]) { inner[i] = 1; anyInner = true; }
                if (anyInner)
                {
                    var ilr = OldCutImage.Label4(inner, W, W);
                    var il = ilr.Lab;
                    var area = new int[ilr.Count + 1];
                    var minLine = Math.Max(60, (int)(npx * 0.0002));
                    for (i = 0; i < npx; i++) area[il[i]]++;
                    for (i = 0; i < npx; i++) if (il[i] != 0 && area[il[i]] >= minLine) thick[i] = 0;
                }
            }
            for (i = 0; i < npx; i++) lines[i] = dark[i] != 0 && thick[i] == 0 ? (byte)1 : (byte)0;
            // bề rộng nét điển hình: hai lần khoảng cách từ sống nét tới phía sáng
            double inkW = 0;
            {
                var dl = OldCutImage.Edt(feat, W, W).D2;
                var hv = new List<float>();
                for (y = 1; y < W - 1; y++)
                    for (x = 1; x < W - 1; x++)
                    {
                        i = y * W + x;
                        if (lines[i] == 0 || dl[i] < 1) continue;
                        if (Peak(dl[i], dl[i - 1], dl[i + 1]) || Peak(dl[i], dl[i - W], dl[i + W])) hv.Add(F(Math.Sqrt(dl[i])));
                    }
                if (hv.Count > 100)
                {
                    hv.Sort();
                    inkW = Math.Min(Math.Max(F(F(2 * hv[hv.Count / 2]) - 1), 2), k);
                }
            }
            // nét mờ dần giữa chừng được nối vào nét nó chỉ tới
            if (p.Bridge > 0)
            {
                var skB = Skeleton.Thin(dark, W, W);
                Skeleton.PruneSpurs(skB, W, W, 8);
                var before = (byte[])skB.Clone();
                Skeleton.CloseGaps(skB, W, W, p.Bridge, null, null, 0.9, true);
                for (i = 0; i < npx; i++) if (skB[i] != 0 && before[i] == 0) lines[i] = 1;
            }
            // bịt khe nhỏ trong nét
            lines = p.CloseGap > 0 ? OldCutImage.DilateDisk(lines, W, W, p.CloseGap) : OldCutImage.Morph3(OldCutImage.Morph3(lines, W, W, true), W, W, false);
            var light = new byte[npx];
            for (i = 0; i < npx; i++) light[i] = lines[i] == 0 && thick[i] == 0 ? (byte)1 : (byte)0;
            var lr = OldCutImage.Label4(light, W, W);
            var lightLab = lr.Lab;
            int n1 = lr.Count;
            var dr = OldCutImage.Label4(thick, W, W);
            var darkLab = dr.Lab;
            int n2 = dr.Count;
            var labels = new int[npx];
            for (i = 0; i < npx; i++) labels[i] = darkLab[i] > 0 ? darkLab[i] + n1 : lightLab[i];
            var rawLabels = (int[])labels.Clone();
            var minPx = p.MinArea * W * W;
            var sizes = new int[n1 + n2 + 1];
            var any = false;
            for (i = 0; i < npx; i++) sizes[labels[i]]++;
            for (i = 0; i < npx; i++)
            {
                if (labels[i] != 0 && sizes[labels[i]] < minPx) labels[i] = 0;
                else if (labels[i] != 0) any = true;
            }
            if (!any) return null;
            // vùng tối giữ nét của nó: trả lại một dải rộng một nét cho mực
            var band = new byte[npx];
            if (inkW > 0 && n2 > 0)
            {
                var notThick = new byte[npx];
                for (i = 0; i < npx; i++) notThick[i] = thick[i] != 0 ? (byte)0 : (byte)1;
                var dIn = OldCutImage.Edt(notThick, W, W).D2;
                var widest = new float[n2 + 1];
                for (i = 0; i < npx; i++) if (darkLab[i] != 0) widest[darkLab[i]] = Math.Max(widest[darkLab[i]], dIn[i]);
                var rr = Math.Max(2, inkW);
                for (i = 0; i < npx; i++) if (darkLab[i] != 0 && labels[i] != 0 && widest[darkLab[i]] < F(rr * rr)) { labels[i] = 0; band[i] = 1; }
                var dt = OldCutImage.Edt(feat, W, W).D2;
                var b2 = F(inkW * inkW);
                var core = new int[n2 + 1];
                for (i = 0; i < npx; i++) if (darkLab[i] != 0 && labels[i] != 0 && dt[i] > b2) core[darkLab[i]]++;
                for (i = 0; i < npx; i++) if (darkLab[i] != 0 && labels[i] != 0 && dt[i] <= b2 && core[darkLab[i]] >= minPx) { labels[i] = 0; band[i] = 1; }
            }
            if (p.GrowTips > 0)
            {
                var front = new List<int>();
                for (i = 0; i < npx; i++) if (labels[i] != 0) front.Add(i);
                var nb = new int[4];
                for (var step = 0; step < p.GrowTips && front.Count > 0; step++)
                {
                    var next = new List<int>();
                    for (var gi = 0; gi < front.Count; gi++)
                    {
                        int gq = front[gi], gx = gq % W, gy = gq / W;
                        nb[0] = gx > 0 ? gq - 1 : -1; nb[1] = gx < W - 1 ? gq + 1 : -1; nb[2] = gy > 0 ? gq - W : -1; nb[3] = gy < W - 1 ? gq + W : -1;
                        for (var gj = 0; gj < 4; gj++)
                        {
                            var gn = nb[gj];
                            if (gn < 0 || labels[gn] != 0 || rawLabels[gn] != 0 || thick[gn] != 0) continue;
                            int gr = img[gn * 3], gg = img[gn * 3 + 1], gb = img[gn * 3 + 2];
                            int gmx = Math.Max(gr, Math.Max(gg, gb)), gmn = Math.Min(gr, Math.Min(gg, gb));
                            if (gmx >= 50 && gmx - gmn >= 30) { labels[gn] = labels[gq]; next.Add(gn); }
                        }
                    }
                    front = next;
                }
            }
            // phần chưa gán (nét, mảnh vụn, hình mờ) vào vùng gần nhất
            for (i = 0; i < npx; i++) feat[i] = labels[i] != 0 ? (byte)1 : (byte)0;
            var near = OldCutImage.Edt(feat, W, W, true).Nearest;
            var filled = new int[npx];
            for (i = 0; i < npx; i++) filled[i] = labels[near[i]];
            // trong nét mực: điểm thuộc vùng chiếm đa số trong đĩa cỡ một nét
            if (inkW > 0)
            {
                var rad = Math.Min(Math.Max((int)Ops.JsRound(inkW), 2), 8);
                var off = new List<int>();
                for (var dy = -rad; dy <= rad; dy++)
                    for (var dx = -rad; dx <= rad; dx++) if (dx * dx + dy * dy <= rad * rad) off.Add(dy * W + dx);
                var inkList = new List<int>();
                for (y = rad; y < W - rad; y++)
                    for (x = rad; x < W - rad; x++) if (labels[y * W + x] == 0) inkList.Add(y * W + x);
                var cntL = new int[off.Count + 1];
                var cntN = new int[off.Count + 1];
                for (var pass = 0; pass < 3; pass++)
                {
                    var prev = (int[])filled.Clone();
                    var changed = 0;
                    for (var q = 0; q < inkList.Count; q++)
                    {
                        i = inkList[q];
                        var ncnt = 0;
                        for (var o = 0; o < off.Count; o++)
                        {
                            var l = prev[i + off[o]];
                            int j;
                            for (j = 0; j < ncnt && cntL[j] != l; j++) { }
                            if (j == ncnt) { cntL[ncnt] = l; cntN[ncnt] = 1; ncnt++; } else cntN[j]++;
                        }
                        var bi = 0;
                        for (var j = 1; j < ncnt; j++) if (cntN[bi] < cntN[j]) bi = j;
                        if (cntL[bi] != prev[i] && 2 * cntN[bi] > off.Count) { filled[i] = cntL[bi]; changed++; }
                    }
                    if (changed == 0) break;
                }
            }
            var remap = new int[n1 + n2 + 1];
            for (i = 0; i < remap.Length; i++) remap[i] = -1;
            var n = 0;
            for (i = 0; i < npx; i++) remap[filled[i]] = 0;
            for (i = 0; i < remap.Length; i++) if (remap[i] == 0) remap[i] = n++;
            for (i = 0; i < npx; i++) filled[i] = remap[filled[i]];
            // nét trang trí: vệt tối không chia gì (ria, cỏ, mắt); bề rộng nét của tranh cũng ra từ bước này
            byte[] deco = null;
            double lineW = 0;
            if (p.Decor)
            {
                var border = new byte[npx];
                var lightP = new byte[npx];
                var cand = new byte[npx];
                for (y = 0; y < W; y++)
                    for (x = 0; x < W; x++)
                    {
                        i = y * W + x;
                        if (x + 1 < W && filled[i] != filled[i + 1]) border[i] = border[i + 1] = 1;
                        if (y + 1 < W && filled[i] != filled[i + W]) border[i] = border[i + W] = 1;
                        lightP[i] = dark[i] != 0 ? (byte)0 : (byte)1;
                        cand[i] = dark[i] != 0 && band[i] == 0 && !(darkLab[i] > 0 && labels[i] != 0) ? (byte)1 : (byte)0;
                    }
                var dLight = OldCutImage.Edt(lightP, W, W).D2;
                var dBorder = OldCutImage.Edt(border, W, W).D2;
                float[] dAll;
                {
                    var rc = new double[(n1 + n2 + 1) * 3];
                    var pc = new double[n * 3];
                    var pn = new int[n];
                    var offR = new int[n1 + n2 + 1];
                    var nearB = new float[n1 + n2 + 1];
                    for (i = 0; i < nearB.Length; i++) nearB[i] = F(1e30);
                    for (i = 0; i < npx; i++)
                    {
                        if (rawLabels[i] != 0)
                        {
                            nearB[rawLabels[i]] = Math.Min(nearB[rawLabels[i]], dBorder[i]);
                            for (var cc = 0; cc < 3; cc++) rc[rawLabels[i] * 3 + cc] += img[i * 3 + cc];
                        }
                        if (labels[i] != 0)
                        {
                            pn[filled[i]]++;
                            for (var cc = 0; cc < 3; cc++) pc[filled[i] * 3 + cc] += img[i * 3 + cc];
                        }
                    }
                    for (i = 0; i < npx; i++)
                    {
                        int l2 = rawLabels[i], q2 = filled[i];
                        if (l2 == 0 || sizes[l2] >= minPx || pn[q2] == 0) continue;
                        double dsum = 0;
                        for (var c3 = 0; c3 < 3; c3++)
                        {
                            var e = rc[l2 * 3 + c3] / sizes[l2] - pc[q2 * 3 + c3] / pn[q2];
                            dsum += e * e;
                        }
                        offR[l2] += dsum > 60.0 * 60.0 ? 1 : -1;
                    }
                    var allBorder = (byte[])border.Clone();
                    var mx = Math.Max(2, inkW);
                    var reachB = F(F(F(mx * 1.5) + 2) * F(F(mx * 1.5) + 2));
                    var isRegion = new byte[npx];
                    for (i = 0; i < npx; i++)
                    {
                        var l3 = rawLabels[i];
                        isRegion[i] = l3 != 0 && (sizes[l3] >= minPx || (offR[l3] > 0 && nearB[l3] <= reachB)) ? (byte)1 : (byte)0;
                    }
                    var nr = OldCutImage.Edt(isRegion, W, W, true).Nearest;
                    for (y = 0; y < W; y++)
                        for (x = 0; x < W; x++)
                        {
                            i = y * W + x;
                            var a = rawLabels[nr[i]];
                            if (x + 1 < W && a != rawLabels[nr[i + 1]]) allBorder[i] = allBorder[i + 1] = 1;
                            if (y + 1 < W && a != rawLabels[nr[i + W]]) allBorder[i] = allBorder[i + W] = 1;
                        }
                    dAll = OldCutImage.Edt(allBorder, W, W).D2;
                }
                var halves = new List<float>();
                for (i = 0; i < npx; i += 3) if (border[i] != 0 && dark[i] != 0) halves.Add(F(Math.Sqrt(dLight[i])));
                if (halves.Count > 50)
                {
                    halves.Sort();
                    double halfW = halves[halves.Count / 2];
                    lineW = 2.0 * halfW;
                    double tt = F(F(halfW * 1.3) + 1);
                    var seeds = new byte[npx];
                    for (i = 0; i < npx; i++) seeds[i] = cand[i] != 0 && dAll[i] > F(tt * tt) ? (byte)1 : (byte)0;
                    var r0 = Math.Max(1, (int)F(halfW * 0.3));
                    var inv = new byte[npx];
                    var erodedS = new byte[npx];
                    var opened = new byte[npx];
                    for (i = 0; i < npx; i++) inv[i] = seeds[i] != 0 ? (byte)0 : (byte)1;
                    var dNon = OldCutImage.Edt(inv, W, W).D2;
                    for (i = 0; i < npx; i++) erodedS[i] = seeds[i] != 0 && dNon[i] > F(r0 * r0) ? (byte)1 : (byte)0;
                    var dEr = OldCutImage.Edt(erodedS, W, W).D2;
                    for (i = 0; i < npx; i++) opened[i] = seeds[i] != 0 && dEr[i] <= F(F(r0 * r0) + 1) ? (byte)1 : (byte)0;
                    deco = new byte[npx];
                    var clr = OldCutImage.Label4(opened, W, W);
                    var cl = clr.Lab;
                    var nc = clr.Count;
                    var areaC = new int[nc + 1];
                    var reach = new float[nc + 1];
                    for (i = 0; i < npx; i++) areaC[cl[i]]++;
                    for (i = 0; i < npx; i++) if (cl[i] != 0) reach[cl[i]] = Math.Max(reach[cl[i]], dAll[i]);
                    double far = F(tt + halfW);
                    var farKeep = p.JoinDeco ? F(far * 2.1) : far;
                    var kept = 0;
                    for (i = 0; i < npx; i++) if (cl[i] != 0 && areaC[cl[i]] >= 24 && reach[cl[i]] > F(farKeep * farKeep)) { deco[i] = 1; kept++; }
                    if (kept == 0) deco = null;
                    else if (p.JoinDeco)
                    {
                        var eJ = OldCutImage.Edt(deco, W, W, true);
                        var dDeco = eJ.D2;
                        var nnJ = eJ.Nearest;
                        var reachJ = F(F(tt + halfW) + 1);
                        var add = new List<int>();
                        for (i = 0; i < npx; i++)
                        {
                            if (deco[i] != 0 || dark[i] == 0 || dAll[i] > F(tt * tt) || dDeco[i] > F(reachJ * reachJ)) continue;
                            if (Math.Sqrt(dAll[nnJ[i]]) - Math.Sqrt(dAll[i]) >= 0.95 * Math.Sqrt(dDeco[i])) add.Add(i);
                        }
                        foreach (var a in add) deco[a] = 1;
                    }
                }
            }
            // màu vùng = trung vị các điểm trong lõi (xa mực)
            var coreH = new int[n][];
            var allH = new int[n][];
            var coreN = new int[n];
            var allN = new int[n];
            for (i = 0; i < n; i++) { coreH[i] = new int[768]; allH[i] = new int[768]; }
            for (i = 0; i < npx; i++)
            {
                var li = filled[i];
                allN[li]++;
                var core = dark[i] == 0 || thick[i] != 0;
                if (core) coreN[li]++;
                for (var ch = 0; ch < 3; ch++)
                {
                    allH[li][ch * 256 + img[i * 3 + ch]]++;
                    if (core) coreH[li][ch * 256 + img[i * 3 + ch]]++;
                }
            }
            var cols = new List<int[]>();
            var tmp = new int[256];
            for (i = 0; i < n; i++)
            {
                var hasCore = coreN[i] > 0;
                var col = new int[3];
                for (var ch = 0; ch < 3; ch++)
                {
                    Array.Copy(hasCore ? coreH[i] : allH[i], ch * 256, tmp, 0, 256);
                    col[ch] = Median(tmp, hasCore ? coreN[i] : allN[i]);
                }
                cols.Add(col);
            }
            return new OldCutSeg { W = W, Labels = filled, Colors = cols, Deco = deco, LineW = lineW, InkW = inkW, Work = img, LineDark = lineDark };
        }

        private static bool Peak(float d, float a, float b) => d >= a && d >= b && (d > a || d > b);
    }
}
