using System;
using System.Collections.Generic;

namespace Falcon.InGame.LevelEditor.Gen
{
    /// <summary>Bảng 40 màu của Color Sort (cùng giá trị với ColorPalette runtime), có Lab để tìm màu gần nhất; không phụ thuộc UnityEngine.</summary>
    public static class Palette
    {
        public static readonly uint[] Hex =
        {
            0xFFBBBB, 0xF9241A, 0xFF8A7A, 0xAB441A, 0x5C2A12, 0xF1621A, 0xF6B48A, 0xE0A46C, 0xFFDFBE, 0xD4871F,
            0xFDAA13, 0xFFF3A0, 0xFFE306, 0x7A8C1A, 0xB7D91D, 0x3AE96D, 0x6AE0A7, 0x0B5A34, 0x029D5D, 0x0AA5A0,
            0x1CD8F2, 0xB7E0FC, 0x2FA6FF, 0x8FA0B3, 0x3A6FB0, 0x1837E5, 0x0D1B6B, 0xAB8EFF, 0x4A1470, 0xD48CFA,
            0x8A1FBB, 0xC148F7, 0xFD55C2, 0xAF0A6F, 0x8A0F1C, 0xF2F4F4, 0xC9F2C7, 0xCFD3D6, 0x4C4C4C, 0x1A1A1A,
        };

        public const int Count = 40;
        public static readonly int Limit = Count;
        public static readonly byte[][] Rgb;
        public static readonly double[][] Lab;

        static Palette()
        {
            Rgb = new byte[Hex.Length][];
            Lab = new double[Hex.Length][];
            for (var i = 0; i < Hex.Length; i++)
            {
                Rgb[i] = new[] { (byte)((Hex[i] >> 16) & 255), (byte)((Hex[i] >> 8) & 255), (byte)(Hex[i] & 255) };
                Lab[i] = ToLab(Rgb[i][0], Rgb[i][1], Rgb[i][2]);
            }
        }

        private static double Lin(double v)
        {
            var s = v / 255;
            return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        private static double F(double t) => t > 0.008856 ? Math.Pow(t, 1.0 / 3.0) : 7.787 * t + 16.0 / 116.0;

        public static double[] ToLab(double r, double g, double b)
        {
            double R = Lin(r), G = Lin(g), B = Lin(b);
            var x = (0.4124 * R + 0.3576 * G + 0.1805 * B) / 0.95047;
            var y = 0.2126 * R + 0.7152 * G + 0.0722 * B;
            var z = (0.0193 * R + 0.1192 * G + 0.9505 * B) / 1.08883;
            return new[] { 116 * F(y) - 16, 500 * (F(x) - F(y)), 200 * (F(y) - F(z)) };
        }

        /// <summary>Màu bảng gần nhất theo Lab; exclude: các id bỏ qua.</summary>
        public static int NearestLab(double l, double a, double b, IList<int> exclude = null)
        {
            var best = -1;
            var bd = double.PositiveInfinity;
            for (var i = 0; i < Limit; i++)
            {
                if (exclude != null && exclude.Contains(i)) continue;
                double d0 = Lab[i][0] - l, d1 = Lab[i][1] - a, d2 = Lab[i][2] - b, d = d0 * d0 + d1 * d1 + d2 * d2;
                if (d < bd) { bd = d; best = i; }
            }
            return best >= 0 ? best : NearestLab(l, a, b);
        }

        public static string Css(int i) => "#" + Hex[i].ToString("x6");
    }
}
