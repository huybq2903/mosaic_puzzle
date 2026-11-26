using System;
using System.Collections.Generic;
using Falcon.InGame.LevelEditor.Gen;
using UnityEngine;

namespace Falcon.InGame.LevelEditor
{
    /// <summary>Ảnh xem trước của từng kiểu chia: chính thuật toán chia chạy trên một hình mẫu nhỏ, nên hình luôn khớp kết quả (port từ pattern_thumbs.js).</summary>
    public static class PatternThumbs
    {
        private const int Size = 96, Cells = 9;
        private static Texture2D[] _cache;

        /// <summary>Ảnh của kiểu i; null cho Tự động (không có hình cố định).</summary>
        public static Texture2D Get(int pattern)
        {
            _cache ??= Build();
            return pattern >= 0 && pattern < _cache.Length ? _cache[pattern] : null;
        }

        private static Color32 Pastel(int i)
        {
            double h = (i * 0.381966) % 1, s = 0.38, v = 0.95;
            var k = h * 6;
            var f = k - Math.Floor(k);
            double p = v * (1 - s), q = v * (1 - s * f), t = v * (1 - s * (1 - f));
            var c = ((int)Math.Floor(k) % 6) switch
            {
                0 => new[] { v, t, p },
                1 => new[] { q, v, p },
                2 => new[] { p, v, t },
                3 => new[] { p, q, v },
                4 => new[] { t, p, v },
                _ => new[] { v, p, q },
            };
            return new Color32((byte)Ops.JsRound(c[0] * 255), (byte)Ops.JsRound(c[1] * 255), (byte)Ops.JsRound(c[2] * 255), 255);
        }

        private static Texture2D Render(int[] owner)
        {
            var pixels = new Color32[Size * Size];
            for (var y = 0; y < Size; y++)
                for (var x = 0; x < Size; x++)
                {
                    var i = y * Size + x;
                    Color32 c;
                    if (owner[i] < 0) c = new Color32(38, 38, 38, 255);
                    else
                    {
                        var edge = false;
                        for (var k = 0; k < 4 && !edge; k++)
                        {
                            int nx = x + (k == 0 ? -1 : k == 1 ? 1 : 0), ny = y + (k == 2 ? -1 : k == 3 ? 1 : 0);
                            edge = nx < 0 || ny < 0 || nx >= Size || ny >= Size || owner[ny * Size + nx] != owner[i];
                        }
                        c = edge ? new Color32(14, 14, 20, 255) : Pastel(owner[i]);
                    }
                    pixels[(Size - 1 - y) * Size + x] = c; // hàng 0 của texture ở dưới
                }
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        private static Texture2D[] Build()
        {
            var pix = new List<int>();
            for (var y = 0; y < Size; y++)
                for (var x = 0; x < Size; x++)
                {
                    double dx = x - Size / 2.0, dy = y - Size / 2.0, th = Math.Atan2(dy, dx);
                    var radius = 0.43 * Size * (1 + 0.12 * Math.Sin(3 * th + 0.5) + 0.06 * Math.Sin(5 * th + 1.3));
                    if (Math.Sqrt(dx * dx + dy * dy) >= radius) continue;
                    pix.Add(y * Size + x);
                }
            var result = new Texture2D[Subdivide.Labels.Length];
            for (var p = 1; p < result.Length; p++)
            {
                var fa = Subdivide.FlowAssign(Size, Size, pix, Cells, p);
                var owner = new int[Size * Size];
                Array.Fill(owner, -1);
                for (var i = 0; i < pix.Count; i++) owner[pix[i]] = fa != null ? fa.Cell[i] : 0;
                result[p] = Render(owner);
            }
            return result;
        }
    }
}
