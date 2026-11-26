using System.Collections.Generic;
using Falcon.InGame.Core;
using Falcon.InGame.LevelEditor.Gen;
using UnityEngine;

namespace Falcon.InGame.LevelEditor
{
    /// <summary>Chuyển giữa ảnh Unity / kết quả tách mảnh thuần và PictureProperty của game.</summary>
    public static class PictureConvert
    {
        /// <summary>Texture → ảnh nguồn RGBA; giữ thứ tự hàng của Unity (hàng 0 ở dưới) nên toạ độ mảnh ra y hướng lên như PictureView cần.</summary>
        public static SourceImage ToSource(Texture2D tex)
        {
            var colors = tex.GetPixels32();
            var px = new byte[colors.Length * 4];
            for (var i = 0; i < colors.Length; i++)
            {
                px[i * 4] = colors[i].r;
                px[i * 4 + 1] = colors[i].g;
                px[i * 4 + 2] = colors[i].b;
                px[i * 4 + 3] = colors[i].a;
            }
            return new SourceImage { Px = px, Width = tex.width, Height = tex.height };
        }

        /// <summary>Kết quả tách mảnh → PictureProperty: id, kẹp khung, số cát theo diện tích (như MS.settle của Studio).</summary>
        public static PictureProperty ToProperty(GeneratedPicture pic, GenOptions s, string sourceName)
        {
            var p = new PictureProperty { width = pic.Width, height = pic.Height, unit = pic.Unit };
            p.gen.workSize = s.WorkSize;
            p.gen.frameW = s.FrameW;
            p.gen.frameH = s.FrameH;
            p.gen.fitCover = s.FitCover;
            p.gen.smoothScale = s.SmoothScale;
            p.gen.curveSmooth = (float)s.CurveSmooth;
            p.gen.fitTolerance = (float)s.FitTolerance;
            p.gen.inkGaps = false;
            p.gen.sourceName = sourceName;
            foreach (var r in pic.Regions)
            {
                p.regions.Add(new RegionData
                {
                    points = r.Points,
                    colorId = r.ColorId,
                    value = PieceValue.Min,
                    valueManual = false,
                    holes = r.Holes != null ? new List<int[]>(r.Holes) : null,
                });
            }
            foreach (var d in pic.Decor) p.decor.Add(d);
            p.EnsureIds();
            p.ClampToFrame();
            PieceValue.Assign(p);
            return p;
        }
    }
}
