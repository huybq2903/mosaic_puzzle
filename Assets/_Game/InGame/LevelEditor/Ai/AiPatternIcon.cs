using UnityEngine;
using UnityEngine.UIElements;

namespace Falcon.InGame.LevelEditor
{
    /// <summary>Biểu tượng kiểu chia nền (tia, sóng, mảnh kính, vòng) vẽ bằng nét trắng trên nền tối, như bản Studio web.</summary>
    public sealed class AiPatternIcon : VisualElement
    {
        private const float W = 60f, H = 46f;
        private readonly string _kind;

        public AiPatternIcon(string kind)
        {
            _kind = kind;
            pickingMode = PickingMode.Ignore;
            AddToClassList("ai-pat-icon");
            if (kind == "auto")
            {
                var glyph = new Label("A");
                glyph.AddToClassList("ai-pat-glyph");
                glyph.pickingMode = PickingMode.Ignore;
                Add(glyph);
                return;
            }
            generateVisualContent += Draw;
        }

        private void Draw(MeshGenerationContext ctx)
        {
            var box = contentRect;
            if (box.width < 2f || box.height < 2f) return;
            var k = Mathf.Min(box.width / W, box.height / H);
            var origin = new Vector2(box.x + (box.width - W * k) * 0.5f, box.y + (box.height - H * k) * 0.5f);
            Vector2 P(float x, float y) => origin + new Vector2(x, y) * k;
            var p = ctx.painter2D;
            p.strokeColor = new Color(0.9f, 0.9f, 0.92f);
            p.lineWidth = 1.4f * Mathf.Max(1f, k);
            p.lineJoin = LineJoin.Round;
            p.lineCap = LineCap.Round;
            switch (_kind)
            {
                case "rays":
                    foreach (var end in new[] { (2f, 4f), (30f, 0f), (58f, 4f), (60f, 23f), (58f, 42f), (30f, 46f), (2f, 42f), (0f, 23f) })
                    {
                        p.BeginPath();
                        p.MoveTo(P(30, 23));
                        p.LineTo(P(end.Item1, end.Item2));
                        p.Stroke();
                    }
                    break;
                case "waves":
                    foreach (var y in new[] { 12f, 24f, 36f })
                    {
                        p.BeginPath();
                        p.MoveTo(P(0, y));
                        p.QuadraticCurveTo(P(15, y - 8), P(30, y));
                        p.QuadraticCurveTo(P(45, y + 8), P(60, y));
                        p.Stroke();
                    }
                    break;
                case "shards":
                    Poly(p, P, 2, 40, 20, 6, 34, 30, 58, 10);
                    Poly(p, P, 20, 6, 34, 30, 30, 44);
                    Poly(p, P, 34, 30, 58, 40);
                    break;
                case "mix":
                    p.BeginPath();
                    p.Arc(P(30, 23), 10f * k, -50f, 250f);
                    p.Stroke();
                    p.BeginPath();
                    p.MoveTo(P(36, 8));
                    p.LineTo(P(38, 15));
                    p.LineTo(P(31, 16));
                    p.Stroke();
                    break;
                case "person":
                    p.BeginPath();
                    p.Arc(P(30, 15), 8f * k, 0f, 360f);
                    p.Stroke();
                    p.BeginPath();
                    p.MoveTo(P(12, 44));
                    p.BezierCurveTo(P(14, 30), P(22, 27), P(30, 27));
                    p.BezierCurveTo(P(38, 27), P(46, 30), P(48, 44));
                    p.Stroke();
                    break;
                case "scene":
                    Poly(p, P, 8, 6, 52, 6, 52, 40, 8, 40, 8, 6);
                    Poly(p, P, 8, 32, 22, 20, 32, 30, 40, 24, 52, 34);
                    p.BeginPath();
                    p.Arc(P(40, 14), 3f * k, 0f, 360f);
                    p.Stroke();
                    break;
                case "rings":
                    foreach (var r in new[] { 7f, 14f, 21f })
                    {
                        p.BeginPath();
                        p.Arc(P(30, 23), r * k, 0f, 360f);
                        p.Stroke();
                    }
                    break;
            }
        }

        private static void Poly(Painter2D p, System.Func<float, float, Vector2> P, params float[] xy)
        {
            p.BeginPath();
            p.MoveTo(P(xy[0], xy[1]));
            for (var i = 2; i + 1 < xy.Length; i += 2) p.LineTo(P(xy[i], xy[i + 1]));
            p.Stroke();
        }
    }
}
