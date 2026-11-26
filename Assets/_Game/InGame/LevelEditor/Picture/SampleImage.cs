using UnityEngine;

namespace Falcon.InGame.LevelEditor
{
    /// <summary>Ảnh kính màu viền đen sinh bằng code (các ô Voronoi), dùng để thử editor mà không phải chọn file.</summary>
    public static class SampleImage
    {
        public static Texture2D Make(int size = 512, int cells = 34)
        {
            var rng = new System.Random(7);
            var seeds = new Vector2[cells];
            var colors = new Color32[cells];
            for (var i = 0; i < cells; i++)
            {
                seeds[i] = new Vector2((float)rng.NextDouble() * size, (float)rng.NextDouble() * size);
                colors[i] = Color.HSVToRGB((float)rng.NextDouble(), 0.55f + 0.3f * (float)rng.NextDouble(), 0.75f + 0.2f * (float)rng.NextDouble());
            }
            var pixels = new Color32[size * size];
            var black = new Color32(14, 14, 18, 255);
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    float d1 = float.MaxValue, d2 = float.MaxValue;
                    var best = 0;
                    for (var i = 0; i < cells; i++)
                    {
                        var dx = x - seeds[i].x;
                        var dy = y - seeds[i].y;
                        var d = Mathf.Sqrt(dx * dx + dy * dy);
                        if (d < d1) { d2 = d1; d1 = d; best = i; }
                        else if (d < d2) d2 = d;
                    }
                    var border = x < 6 || y < 6 || x >= size - 6 || y >= size - 6;
                    pixels[y * size + x] = border || d2 - d1 < 3.4f ? black : colors[best];
                }
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }
    }
}
