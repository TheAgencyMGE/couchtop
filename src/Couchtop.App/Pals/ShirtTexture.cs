using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Couchtop.Core.Pals;

namespace Couchtop.App.Pals;

/// <summary>
/// Paints the fabric wrapped around a Pal's torso. The image wraps once around the body with the chest in the
/// middle (x = 0.5) and the collar at the top, so prints, pockets and zips land on the front.
/// </summary>
public static class ShirtTexture
{
    private const int Width = 512;
    private const int Height = 256;

    public static Brush Paint(PalProfile p)
    {
        var top = AvatarMaterials.Parse(p.TopColor);
        var accent = AvatarMaterials.Parse(p.TopAccent);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(AvatarMaterials.Brush(top), null, new Rect(0, 0, Width, Height));
            DrawPattern(dc, p.TopPattern, top, accent);
            DrawStyle(dc, p.TopStyle, top, accent);

            // Soft shading: a little darker at the hem and under the arms so the torso reads as round.
            var hem = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(46, 0, 0, 0), 90);
            hem.GradientStops[0].Offset = 0.72;
            hem.Freeze();
            dc.DrawRectangle(hem, null, new Rect(0, 0, Width, Height));
        }
        var bitmap = new RenderTargetBitmap(Width, Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        var brush = new ImageBrush(bitmap) { Stretch = Stretch.Fill };
        brush.Freeze();
        return brush;
    }

    private static void DrawPattern(DrawingContext dc, string pattern, Color top, Color accent)
    {
        var ink = AvatarMaterials.Brush(accent);
        var chest = new Point(Width / 2.0, Height * 0.42);
        switch (pattern)
        {
            case "stripes":
                for (var y = 30.0; y < Height; y += 44) dc.DrawRectangle(ink, null, new Rect(0, y, Width, 16));
                break;
            case "dots":
                for (var y = 20; y < Height; y += 36)
                for (var x = (y / 36 % 2) * 18 + 10; x < Width; x += 36)
                    dc.DrawEllipse(ink, null, new Point(x, y), 7, 7);
                break;
            case "star":
                dc.DrawGeometry(ink, null, Polygon(AvatarMeshes.Star(34, 15), chest, flipY: true));
                break;
            case "heart":
            {
                var g = new StreamGeometry();
                using (var ctx = g.Open())
                {
                    ctx.BeginFigure(new Point(chest.X, chest.Y + 30), true, true);
                    ctx.BezierTo(new Point(chest.X - 48, chest.Y - 4), new Point(chest.X - 26, chest.Y - 40), new Point(chest.X, chest.Y - 16), true, true);
                    ctx.BezierTo(new Point(chest.X + 26, chest.Y - 40), new Point(chest.X + 48, chest.Y - 4), new Point(chest.X, chest.Y + 30), true, true);
                }
                dc.DrawGeometry(ink, null, g);
                break;
            }
            case "bolt":
                dc.DrawGeometry(ink, null, Geometry.Parse($"M {chest.X + 8},{chest.Y - 40} L {chest.X - 22},{chest.Y + 4} L {chest.X - 2},{chest.Y + 4} L {chest.X - 12},{chest.Y + 40} L {chest.X + 22},{chest.Y - 8} L {chest.X + 2},{chest.Y - 8} Z"));
                break;
            case "controller":
            {
                var body = new RectangleGeometry(new Rect(chest.X - 38, chest.Y - 16, 76, 34), 16, 16);
                dc.DrawGeometry(ink, null, body);
                var detail = AvatarMaterials.Brush(top);
                dc.DrawRectangle(detail, null, new Rect(chest.X - 26, chest.Y - 2, 16, 5));
                dc.DrawRectangle(detail, null, new Rect(chest.X - 20.5, chest.Y - 8, 5, 16));
                dc.DrawEllipse(detail, null, new Point(chest.X + 16, chest.Y - 3), 4, 4);
                dc.DrawEllipse(detail, null, new Point(chest.X + 25, chest.Y + 4), 4, 4);
                break;
            }
        }
    }

    private static void DrawStyle(DrawingContext dc, string style, Color top, Color accent)
    {
        var dark = AvatarMaterials.Brush(AvatarMaterials.Darken(top, 0.2));
        var seam = new Pen(AvatarMaterials.Brush(AvatarMaterials.Darken(top, 0.28)), 3);
        seam.Freeze();
        switch (style)
        {
            case "hoodie":
                // Front pocket and a ribbed hem.
                dc.DrawGeometry(dark, seam, Geometry.Parse($"M {Width / 2.0 - 58},{Height * 0.62} L {Width / 2.0 + 58},{Height * 0.62} L {Width / 2.0 + 72},{Height * 0.88} L {Width / 2.0 - 72},{Height * 0.88} Z"));
                dc.DrawRectangle(dark, null, new Rect(0, Height * 0.9, Width, Height * 0.1));
                break;
            case "sweater":
                for (var x = 0.0; x < Width; x += 10) dc.DrawRectangle(dark, null, new Rect(x, Height * 0.88, 4, Height * 0.12));
                break;
            case "jacket":
            {
                // Open front: the shirt underneath shows as a panel, with a zip down each edge.
                dc.DrawRectangle(AvatarMaterials.Brush(accent), null, new Rect(Width / 2.0 - 30, 0, 60, Height));
                var zip = new Pen(AvatarMaterials.Brush(Color.FromRgb(200, 205, 212)), 5);
                zip.Freeze();
                dc.DrawLine(zip, new Point(Width / 2.0 - 32, 0), new Point(Width / 2.0 - 32, Height));
                dc.DrawLine(zip, new Point(Width / 2.0 + 32, 0), new Point(Width / 2.0 + 32, Height));
                dc.DrawRectangle(dark, null, new Rect(Width / 2.0 - 100, Height * 0.62, 36, 8));
                dc.DrawRectangle(dark, null, new Rect(Width / 2.0 + 64, Height * 0.62, 36, 8));
                break;
            }
            case "collared":
            {
                dc.DrawLine(seam, new Point(Width / 2.0, Height * 0.1), new Point(Width / 2.0, Height));
                var button = AvatarMaterials.Brush(accent);
                for (var y = Height * 0.24; y < Height * 0.95; y += Height * 0.18) dc.DrawEllipse(button, null, new Point(Width / 2.0 + 10, y), 5, 5);
                dc.DrawRectangle(dark, null, new Rect(Width / 2.0 - 90, Height * 0.3, 44, 34));
                break;
            }
            case "tank":
                dc.DrawRectangle(dark, null, new Rect(0, 0, Width, 10));
                break;
            case "dress":
                dc.DrawRectangle(AvatarMaterials.Brush(accent), null, new Rect(0, Height * 0.84, Width, 18));
                break;
            default: // tee
                dc.DrawRectangle(dark, null, new Rect(0, 0, Width, 8));
                break;
        }
    }

    private static Geometry Polygon(IReadOnlyList<Point> points, Point center, bool flipY)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            Point Map(Point p) => new(center.X + p.X, center.Y + (flipY ? -p.Y : p.Y));
            ctx.BeginFigure(Map(points[0]), true, true);
            foreach (var p in points.Skip(1)) ctx.LineTo(Map(p), true, true);
        }
        g.Freeze();
        return g;
    }
}
