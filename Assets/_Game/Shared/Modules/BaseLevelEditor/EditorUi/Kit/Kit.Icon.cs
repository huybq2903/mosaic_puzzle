using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Falcon.Shared.EditorUi
{
    public enum KitGlyph
    {
        Close,
        Plus,
        Minus,
        Check,
        ChevronDown,
        ChevronRight,
        ChevronLeft,
        ChevronUp,
        Search,
        Reload,
        Folder,
        Trash,
        Eye,
        EyeOff,
        More,
        Help,
        Sound,
        SoundOff,
        Speed,
        Settings
    }

    public static partial class Kit
    {
        private const float IconInset = 2f;
        private const float IconStroke = 1.6f;

        public static VisualElement Icon(VisualElement parent, KitGlyph glyph)
        {
            return Icon(parent, (painter, box) => DrawGlyph(painter, box, glyph));
        }

        public static VisualElement Icon(VisualElement parent, Action<Painter2D, Rect> draw)
        {
            var icon = El(parent, "vk-icon");

            icon.pickingMode = PickingMode.Ignore;
            icon.generateVisualContent += context =>
            {
                var element = context.visualElement;
                var size = element.layout.size;
                var side = Mathf.Min(size.x, size.y) - IconInset;

                if (side <= 0f) return;

                var painter = context.painter2D;
                var ink = element.resolvedStyle.color;

                painter.lineWidth = IconStroke;
                painter.strokeColor = ink;
                painter.fillColor = ink;
                painter.lineJoin = LineJoin.Round;
                painter.lineCap = LineCap.Round;

                var centre = size * 0.5f;

                draw(painter, new Rect(centre - Vector2.one * (side * 0.5f), Vector2.one * side));
            };

            return icon;
        }

        public static void DrawGlyph(Painter2D painter, Rect box, KitGlyph glyph)
        {
            var c = box.center;
            var r = box.width * 0.5f;

            switch (glyph)
            {
                case KitGlyph.Close:
                    Stroke(painter, c + new Vector2(-r, -r) * 0.6f, c + new Vector2(r, r) * 0.6f);
                    Stroke(painter, c + new Vector2(r, -r) * 0.6f, c + new Vector2(-r, r) * 0.6f);
                    break;

                case KitGlyph.Plus:
                    Stroke(painter, c + Vector2.left * r * 0.7f, c + Vector2.right * r * 0.7f);
                    Stroke(painter, c + Vector2.down * r * 0.7f, c + Vector2.up * r * 0.7f);
                    break;

                case KitGlyph.Minus:
                    Stroke(painter, c + Vector2.left * r * 0.7f, c + Vector2.right * r * 0.7f);
                    break;

                case KitGlyph.Check:
                    Polyline(painter, c + new Vector2(-0.65f, 0.05f) * r, c + new Vector2(-0.2f, 0.5f) * r,
                        c + new Vector2(0.7f, -0.5f) * r);
                    break;

                case KitGlyph.ChevronDown:
                    Polyline(painter, c + new Vector2(-0.55f, -0.25f) * r, c + new Vector2(0f, 0.3f) * r,
                        c + new Vector2(0.55f, -0.25f) * r);
                    break;

                case KitGlyph.ChevronUp:
                    Polyline(painter, c + new Vector2(-0.55f, 0.25f) * r, c + new Vector2(0f, -0.3f) * r,
                        c + new Vector2(0.55f, 0.25f) * r);
                    break;

                case KitGlyph.ChevronRight:
                    Polyline(painter, c + new Vector2(-0.25f, -0.55f) * r, c + new Vector2(0.3f, 0f) * r,
                        c + new Vector2(-0.25f, 0.55f) * r);
                    break;

                case KitGlyph.ChevronLeft:
                    Polyline(painter, c + new Vector2(0.25f, -0.55f) * r, c + new Vector2(-0.3f, 0f) * r,
                        c + new Vector2(0.25f, 0.55f) * r);
                    break;

                case KitGlyph.Search:
                    painter.BeginPath();
                    painter.Arc(c + new Vector2(-0.15f, -0.15f) * r, r * 0.5f, Angle.Degrees(0f),
                        Angle.Degrees(360f));
                    painter.Stroke();
                    Stroke(painter, c + new Vector2(0.22f, 0.22f) * r, c + new Vector2(0.75f, 0.75f) * r);
                    break;

                case KitGlyph.Reload:
                    painter.BeginPath();
                    painter.Arc(c, r * 0.62f, Angle.Degrees(20f), Angle.Degrees(300f));
                    painter.Stroke();
                    Polyline(painter, c + new Vector2(0.62f, -0.62f) * r, c + new Vector2(0.62f, -0.12f) * r,
                        c + new Vector2(0.12f, -0.12f) * r);
                    break;

                case KitGlyph.Folder:
                    Polyline(painter, c + new Vector2(-0.8f, -0.55f) * r, c + new Vector2(-0.25f, -0.55f) * r,
                        c + new Vector2(-0.1f, -0.35f) * r, c + new Vector2(0.8f, -0.35f) * r,
                        c + new Vector2(0.8f, 0.6f) * r, c + new Vector2(-0.8f, 0.6f) * r,
                        c + new Vector2(-0.8f, -0.55f) * r);
                    break;

                case KitGlyph.Trash:
                    Stroke(painter, c + new Vector2(-0.75f, -0.5f) * r, c + new Vector2(0.75f, -0.5f) * r);
                    Stroke(painter, c + new Vector2(-0.25f, -0.75f) * r, c + new Vector2(0.25f, -0.75f) * r);
                    Polyline(painter, c + new Vector2(-0.55f, -0.5f) * r, c + new Vector2(-0.45f, 0.75f) * r,
                        c + new Vector2(0.45f, 0.75f) * r, c + new Vector2(0.55f, -0.5f) * r);
                    break;

                case KitGlyph.Eye:
                    painter.BeginPath();
                    painter.MoveTo(c + new Vector2(-0.85f, 0f) * r);
                    painter.QuadraticCurveTo(c + new Vector2(0f, -0.85f) * r, c + new Vector2(0.85f, 0f) * r);
                    painter.QuadraticCurveTo(c + new Vector2(0f, 0.85f) * r, c + new Vector2(-0.85f, 0f) * r);
                    painter.Stroke();
                    Dot(painter, c, r * 0.22f);
                    break;

                case KitGlyph.EyeOff:
                    painter.BeginPath();
                    painter.MoveTo(c + new Vector2(-0.85f, 0f) * r);
                    painter.QuadraticCurveTo(c + new Vector2(0f, -0.85f) * r, c + new Vector2(0.85f, 0f) * r);
                    painter.QuadraticCurveTo(c + new Vector2(0f, 0.85f) * r, c + new Vector2(-0.85f, 0f) * r);
                    painter.Stroke();
                    Stroke(painter, c + new Vector2(-0.75f, -0.75f) * r, c + new Vector2(0.75f, 0.75f) * r);
                    break;

                case KitGlyph.More:
                    Dot(painter, c + Vector2.left * r * 0.6f, r * 0.13f);
                    Dot(painter, c, r * 0.13f);
                    Dot(painter, c + Vector2.right * r * 0.6f, r * 0.13f);
                    break;

                case KitGlyph.Sound:
                    Speaker(painter, c, r);
                    painter.BeginPath();
                    painter.Arc(c + new Vector2(0.05f, 0f) * r, r * 0.45f, Angle.Degrees(-45f), Angle.Degrees(45f));
                    painter.Stroke();
                    painter.BeginPath();
                    painter.Arc(c + new Vector2(0.05f, 0f) * r, r * 0.78f, Angle.Degrees(-45f), Angle.Degrees(45f));
                    painter.Stroke();
                    break;

                case KitGlyph.SoundOff:
                    Speaker(painter, c, r);
                    Stroke(painter, c + new Vector2(0.35f, -0.3f) * r, c + new Vector2(0.85f, 0.3f) * r);
                    Stroke(painter, c + new Vector2(0.85f, -0.3f) * r, c + new Vector2(0.35f, 0.3f) * r);
                    break;

                case KitGlyph.Speed:
                {
                    var hub = c + new Vector2(0f, 0.25f) * r;

                    painter.BeginPath();
                    painter.Arc(hub, r * 0.8f, Angle.Degrees(180f), Angle.Degrees(360f));
                    painter.Stroke();
                    Stroke(painter, hub, hub + new Vector2(0.4f, -0.55f) * r);
                    Dot(painter, hub, r * 0.14f);
                    break;
                }

                case KitGlyph.Settings:
                {
                    for (var tooth = 0; tooth < 8; tooth++)
                    {
                        var angle = tooth * Mathf.PI * 0.25f;
                        var along = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

                        Stroke(painter, c + along * r * 0.55f, c + along * r * 0.88f);
                    }

                    painter.BeginPath();
                    painter.Arc(c, r * 0.55f, Angle.Degrees(0f), Angle.Degrees(360f));
                    painter.Stroke();

                    painter.BeginPath();
                    painter.Arc(c, r * 0.2f, Angle.Degrees(0f), Angle.Degrees(360f));
                    painter.Stroke();
                    break;
                }

                case KitGlyph.Help:
                    painter.BeginPath();
                    painter.Arc(c + Vector2.down * r * 0.2f, r * 0.35f, Angle.Degrees(180f), Angle.Degrees(400f));
                    painter.LineTo(c + Vector2.up * r * 0.3f);
                    painter.Stroke();
                    Dot(painter, c + Vector2.up * r * 0.72f, r * 0.1f);
                    break;
            }
        }

        private static void Speaker(Painter2D painter, Vector2 c, float r)
        {
            painter.BeginPath();
            painter.MoveTo(c + new Vector2(-0.85f, -0.28f) * r);
            painter.LineTo(c + new Vector2(-0.5f, -0.28f) * r);
            painter.LineTo(c + new Vector2(-0.1f, -0.65f) * r);
            painter.LineTo(c + new Vector2(-0.1f, 0.65f) * r);
            painter.LineTo(c + new Vector2(-0.5f, 0.28f) * r);
            painter.LineTo(c + new Vector2(-0.85f, 0.28f) * r);
            painter.ClosePath();
            painter.Fill();
        }

        private static void Stroke(Painter2D painter, Vector2 from, Vector2 to)
        {
            painter.BeginPath();
            painter.MoveTo(from);
            painter.LineTo(to);
            painter.Stroke();
        }

        private static void Polyline(Painter2D painter, params Vector2[] points)
        {
            painter.BeginPath();
            painter.MoveTo(points[0]);

            for (var index = 1; index < points.Length; index++) painter.LineTo(points[index]);

            painter.Stroke();
        }

        private static void Dot(Painter2D painter, Vector2 centre, float radius)
        {
            painter.BeginPath();
            painter.Arc(centre, radius, Angle.Degrees(0f), Angle.Degrees(360f));
            painter.Fill();
        }
    }
}
