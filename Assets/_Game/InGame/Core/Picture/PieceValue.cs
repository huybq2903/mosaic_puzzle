using UnityEngine;

namespace Falcon.InGame.Core
{
    /// <summary>Số cát của mảnh như Studio: số nguyên từ 1 trở lên (không trần), gợi ý theo diện tích bậc 5 trong 5..30.</summary>
    public static class PieceValue
    {
        public const int Step = 5, Min = 1, Max = 9999, AutoMax = 30;
        private const float PerFraction = 712.5f; // giá trị thô của mảnh chiếm toàn bộ tranh

        public static int FromAreaFraction(float fraction) =>
            Mathf.Clamp(Mathf.RoundToInt(fraction * PerFraction / Step) * Step, Step, AutoMax);

        // Số nguyên bất kỳ từ 1 trở lên
        public static int Clamp(int value) => Mathf.Clamp(value, Min, Max);

        // Giá trị gợi ý theo diện tích (đa giác trừ lỗ, theo toạ độ lưu)
        public static int Suggest(PictureProperty p, RegionData r)
        {
            var unit = Mathf.Max(1, p.unit);
            var total = (double)p.width * unit * p.height * unit;
            if (total <= 0) return r.value;
            var a = Area(r.points);
            if (r.holes != null) foreach (var h in r.holes) a -= Area(h);
            return FromAreaFraction((float)(a / total));
        }

        // Tính lại giá trị các mảnh chưa đặt tay
        public static void Assign(PictureProperty p)
        {
            if (p == null) return;
            foreach (var r in p.regions)
                if (!r.valueManual) r.value = Suggest(p, r);
        }

        private static double Area(int[] pts)
        {
            double s = 0;
            var n = pts.Length / 2;
            for (int i = 0, j = n - 1; i < n; j = i++) s += (double)pts[j * 2] * pts[i * 2 + 1] - (double)pts[i * 2] * pts[j * 2 + 1];
            return System.Math.Abs(s) * 0.5;
        }
    }
}
