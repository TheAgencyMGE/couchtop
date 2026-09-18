using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Couchtop.Core.Pals;

namespace Couchtop.App.Pals;

/// <summary>The drawn part of a face at one moment: mood, eyelids, mouth and where the eyes look.</summary>
public readonly record struct FaceState(PalMood Mood, bool Blink, bool Talk, int Look, int LookUp);

/// <summary>
/// Paints Pal faces. The texture covers the front of the head from 75° left to 75° right and from 60° below
/// to 50° above the equator. Drawing happens in those degrees (x = longitude + 75, y = 50 - latitude) and a
/// scale turns them into pixels, so a circle drawn here is a circle on the round head.
/// </summary>
public static class AvatarFace
{
    public const double LongitudeSpan = 75;
    public const double LatitudeTop = 50;
    public const double LatitudeBottom = -60;
    private const int Size = 512;

    private static readonly Color Ink = Color.FromRgb(43, 34, 48);
    private static readonly Color Sclera = Color.FromRgb(255, 253, 248);
    private static readonly Color MouthInside = Color.FromRgb(138, 46, 59);
    private static readonly Color Tongue = Color.FromRgb(240, 122, 134);

    public static ImageSource Paint(PalProfile p, FaceState state)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(Size / (LongitudeSpan * 2), Size / (LatitudeTop - LatitudeBottom)));
            Draw(dc, p, state);
            dc.Pop();
        }
        var bitmap = new RenderTargetBitmap(Size, Size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>Converts a spot on the head (longitude right of centre, latitude up) into drawing units.</summary>
    private static Point At(double longitude, double latitude) => new(longitude + LongitudeSpan, LatitudeTop - latitude);

    private static void Draw(DrawingContext dc, PalProfile p, FaceState s)
    {
        var skin = AvatarMaterials.Parse(p.Skin);
        var hair = AvatarMaterials.Parse(p.HairColor);
        var eyeColor = AvatarMaterials.Parse(p.EyeColor);
        var spacing = 19 + p.EyeSpacing * 9;
        var eyeLat = 1 + (p.EyeHeight - 0.5) * 10;
        var eyeSize = 7.6 * (0.8 + 0.45 * p.EyeSize);

        DrawCheeks(dc, p, skin, spacing, eyeLat);
        DrawFacialHair(dc, p, hair);

        foreach (var side in new[] { -1, 1 })
        {
            var center = At(side * spacing, eyeLat);
            // A wink closes the Pal's left eye (the one on the viewer's right).
            var closed = s.Blink || (s.Mood == PalMood.Cheeky && side == 1);
            DrawEye(dc, p, s, center, eyeSize, side, eyeColor, closed);
            DrawBrow(dc, p, s, center, eyeSize, side, p.HairStyle == "bald" ? AvatarMaterials.Darken(skin, 0.45) : AvatarMaterials.Darken(hair, 0.15));
        }

        if (p.NoseStyle == "small")
            dc.DrawEllipse(Brush(Color.FromArgb(110, 160, 80, 70)), null, At(0, -9), 1.3, 0.9);

        DrawMouth(dc, p, s, skin);
    }

    // ---------------------------------------------------------------- eyes

    private static void DrawEye(DrawingContext dc, PalProfile p, FaceState s, Point c, double r, int side, Color iris, bool closed)
    {
        var style = p.EyeStyle;
        if (s.Mood == PalMood.Surprised) r *= 1.12;
        if (s.Mood == PalMood.Excited && style != "happy") r *= 1.05;

        if (closed || (style == "happy" && s.Mood != PalMood.Surprised))
        {
            // Closed eyes: a happy arch for smiles and winks, a sleepy line for blinks.
            var arch = !s.Blink || style == "happy";
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(c.X - r * 0.95, c.Y + (arch ? r * 0.25 : -r * 0.05)), false, false);
                ctx.QuadraticBezierTo(new Point(c.X, c.Y + (arch ? -r * 0.95 : r * 0.55)), new Point(c.X + r * 0.95, c.Y + (arch ? r * 0.25 : -r * 0.05)), true, true);
            }
            dc.DrawGeometry(null, Pen(Ink, r * 0.3), g);
            if (style == "lashes") DrawLashes(dc, c, r, side, closedEye: true);
            return;
        }

        var lookX = s.Look * r * 0.24;
        var lookY = s.LookUp * -r * 0.22;
        if (s.Mood == PalMood.Thinking)
        {
            lookX = r * 0.22;
            lookY = -r * 0.25;
        }

        // Upper lid position, as a share of the eye covered from the top.
        var lid = style switch
        {
            "sleepy" => 0.42,
            "focused" => 0.22,
            _ => 0.0,
        };
        if (s.Mood == PalMood.Sleepy) lid = Math.Max(lid, 0.5);
        if (s.Mood == PalMood.Happy) lid = Math.Max(lid, 0.05);
        var squint = s.Mood is PalMood.Happy or PalMood.Excited ? 0.18 : 0.0;

        var height = style switch
        {
            "almond" => r * 0.8,
            "dot" => r * 0.62,
            "sparkle" => r * 1.28,
            _ => r * 1.12,
        };
        var width = style switch
        {
            "almond" => r * 1.12,
            "dot" => r * 0.44,
            "sparkle" => r * 0.95,
            _ => r * 0.9,
        };

        var top = c.Y - height + lid * height * 2;
        var bottom = c.Y + height - squint * height * 2;
        dc.PushClip(new RectangleGeometry(new Rect(c.X - width * 1.6, top, width * 3.2, Math.Max(0.01, bottom - top))));

        Geometry outline = style == "almond" ? Almond(c, width, height, side) : new EllipseGeometry(c, width, height);
        if (style == "dot")
        {
            dc.DrawGeometry(Brush(Ink), null, outline);
        }
        else
        {
            if (style != "sparkle") dc.DrawGeometry(Brush(Sclera), null, outline);
            dc.PushClip(outline);
            var irisR = style == "sparkle" ? width * 1.02 : width * 0.78;
            var irisCenter = new Point(c.X + lookX, c.Y + lookY + (style == "sparkle" ? 0 : height * 0.08));
            var gradient = new RadialGradientBrush(AvatarMaterials.Lighten(iris, 0.35), AvatarMaterials.Darken(iris, 0.25))
            {
                GradientOrigin = new Point(0.5, 0.85),
                Center = new Point(0.5, 0.7),
                RadiusX = 0.75,
                RadiusY = 0.75,
            };
            gradient.Freeze();
            dc.DrawEllipse(gradient, null, irisCenter, irisR, style == "sparkle" ? height * 1.02 : irisR * 1.05);
            var pupil = s.Mood == PalMood.Surprised ? 0.34 : 0.5;
            dc.DrawEllipse(Brush(Ink), null, irisCenter, irisR * pupil, irisR * pupil * 1.1);
            dc.Pop();
            if (style != "sparkle") dc.DrawGeometry(null, Pen(Ink, r * 0.1), outline);
        }

        // Catch-lights: what makes eyes look alive.
        var shine = Brush(Color.FromArgb(245, 255, 255, 255));
        var hx = c.X + lookX - width * 0.32;
        var hy = c.Y + lookY - height * 0.42;
        if (style == "sparkle" || s.Mood == PalMood.Excited)
        {
            DrawSparkle(dc, new Point(hx, hy), r * 0.42);
            dc.DrawEllipse(shine, null, new Point(c.X + lookX + width * 0.3, c.Y + lookY + height * 0.3), r * 0.12, r * 0.12);
        }
        else
        {
            dc.DrawEllipse(shine, null, new Point(hx, hy), r * (style == "dot" ? 0.14 : 0.24), r * (style == "dot" ? 0.16 : 0.26));
            if (style != "dot") dc.DrawEllipse(Brush(Color.FromArgb(200, 255, 255, 255)), null, new Point(c.X + lookX + width * 0.28, c.Y + lookY + height * 0.3), r * 0.1, r * 0.1);
        }
        dc.Pop();

        // Lid line.
        if (style != "dot")
        {
            var lidGeometry = new StreamGeometry();
            using (var ctx = lidGeometry.Open())
            {
                ctx.BeginFigure(new Point(c.X - width * 1.02, lid > 0 ? top : c.Y - height * 0.35), false, false);
                if (lid > 0) ctx.LineTo(new Point(c.X + width * 1.02, top), true, true);
                else ctx.QuadraticBezierTo(new Point(c.X, c.Y - height * 1.3), new Point(c.X + width * 1.02, c.Y - height * 0.35), true, true);
            }
            dc.DrawGeometry(null, Pen(Ink, r * (style == "lashes" ? 0.26 : 0.2)), lidGeometry);
            if (style == "almond") dc.DrawLine(Pen(Ink, r * 0.18), new Point(c.X + side * width * 1.0, c.Y - height * 0.2), new Point(c.X + side * width * 1.35, c.Y - height * 0.55));
            if (style == "lashes") DrawLashes(dc, c, r, side, closedEye: false);
        }
    }

    private static Geometry Almond(Point c, double w, double h, int side)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(new Point(c.X - w * 1.05, c.Y), true, true);
            ctx.BezierTo(new Point(c.X - w * 0.6, c.Y - h * 1.35), new Point(c.X + w * 0.6, c.Y - h * 1.35), new Point(c.X + w * 1.05, c.Y - h * 0.1), true, true);
            ctx.BezierTo(new Point(c.X + w * 0.6, c.Y + h * 1.2), new Point(c.X - w * 0.6, c.Y + h * 1.2), new Point(c.X - w * 1.05, c.Y), true, true);
        }
        g.Freeze();
        return g;
    }

    private static void DrawLashes(DrawingContext dc, Point c, double r, int side, bool closedEye)
    {
        var pen = Pen(Ink, r * 0.16);
        for (var k = 0; k < 3; k++)
        {
            var a = (-20 - k * 22) * Math.PI / 180;
            var start = new Point(c.X + side * r * (0.55 + k * 0.12), c.Y - r * (closedEye ? 0.45 : 0.85) + k * r * 0.18);
            var end = new Point(start.X + side * Math.Cos(a) * r * 0.45, start.Y + Math.Sin(a) * r * 0.45);
            dc.DrawLine(pen, start, end);
        }
    }

    private static void DrawSparkle(DrawingContext dc, Point c, double r)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(new Point(c.X, c.Y - r), true, true);
            ctx.QuadraticBezierTo(c, new Point(c.X + r, c.Y), true, true);
            ctx.QuadraticBezierTo(c, new Point(c.X, c.Y + r), true, true);
            ctx.QuadraticBezierTo(c, new Point(c.X - r, c.Y), true, true);
            ctx.QuadraticBezierTo(c, new Point(c.X, c.Y - r), true, true);
        }
        dc.DrawGeometry(Brush(Colors.White), null, g);
    }

    // ---------------------------------------------------------------- brows

    private static void DrawBrow(DrawingContext dc, PalProfile p, FaceState s, Point eye, double r, int side, Color color)
    {
        var thickness = p.BrowStyle switch
        {
            "thick" => 2.9,
            "thin" => 1.1,
            "straight" => 2.0,
            _ => 1.8,
        } * (0.8 + 0.2 * r / 6.2);
        // Angle: positive tilts the inner end down (determined), negative lifts it (worried).
        var tilt = p.BrowStyle switch
        {
            "determined" => 12.0,
            "worried" => -12.0,
            _ => 0.0,
        };
        var lift = 0.0;
        var arch = p.BrowStyle switch { "arched" => 1.8, "straight" => 0.2, "thin" => 1.0, _ => 1.1 };
        switch (s.Mood)
        {
            case PalMood.Surprised: lift = 2.2; arch += 0.5; break;
            case PalMood.Worried: tilt = -14; lift = 0.8; break;
            case PalMood.Thinking: if (side == -1) lift = 1.6; break;
            case PalMood.Cheeky: if (side == -1) lift = 1.4; tilt = side == 1 ? 6 : tilt; break;
            case PalMood.Excited: lift = 1.0; break;
            case PalMood.Sleepy: lift = -0.6; break;
        }

        var half = r * 1.05;
        var y = eye.Y - r * 1.65 - lift;
        // Inner end is toward the middle of the face.
        var inner = new Point(eye.X - side * half, y + tilt * 0.12);
        var outer = new Point(eye.X + side * half, y - tilt * 0.06 + 0.6);
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(inner, false, false);
            ctx.QuadraticBezierTo(new Point(eye.X + side * half * 0.1, y - arch), outer, true, true);
        }
        dc.DrawGeometry(null, Pen(color, thickness), g);
    }

    // ---------------------------------------------------------------- mouth

    private static void DrawMouth(DrawingContext dc, PalProfile p, FaceState s, Color skin)
    {
        var center = At(0, -24);
        dc.PushTransform(new ScaleTransform(1.3, 1.3, center.X, center.Y));
        DrawMouthShape(dc, p, s, skin, center);
        dc.Pop();
    }

    private static void DrawMouthShape(DrawingContext dc, PalProfile p, FaceState s, Color skin, Point c)
    {
        var line = AvatarMaterials.Mix(Ink, AvatarMaterials.Darken(skin, 0.5), 0.35);
        var pen = Pen(line, 1.1);
        var style = p.MouthStyle;

        // Talking and big feelings open the mouth whatever its resting style.
        if (s.Talk || s.Mood is PalMood.Excited || style == "open" && s.Mood != PalMood.Sleepy)
        {
            var wide = s.Mood == PalMood.Excited ? 6.5 : style == "grin" ? 6.2 : 4.6;
            var deep = s.Talk ? (s.Mood == PalMood.Excited ? 5.0 : 3.6) : s.Mood == PalMood.Excited ? 5.0 : 3.8;
            OpenMouth(dc, c, wide, deep, line, teeth: style == "grin" || s.Mood == PalMood.Excited);
            return;
        }
        switch (s.Mood)
        {
            case PalMood.Surprised:
                dc.DrawEllipse(Brush(MouthInside), Pen(line, 0.9), new Point(c.X, c.Y + 0.6), 2.2, 2.8);
                return;
            case PalMood.Worried:
                Wave(dc, c, 4.2, pen);
                return;
            case PalMood.Thinking:
                dc.DrawLine(pen, new Point(c.X - 1.5, c.Y + 0.6), new Point(c.X + 3.2, c.Y - 0.4));
                return;
            case PalMood.Sleepy:
                dc.DrawEllipse(Brush(MouthInside), null, new Point(c.X, c.Y + 0.4), 1.4, 1.1);
                return;
            case PalMood.Cheeky:
                Smirk(dc, c, pen);
                dc.DrawEllipse(Brush(Tongue), null, new Point(c.X + 1.6, c.Y + 1.6), 1.3, 1.1);
                return;
            case PalMood.Happy when style is not ("cat" or "smirk"):
                OpenMouth(dc, c, style == "small" ? 3.2 : 4.4, 2.6, line, teeth: style == "grin");
                return;
        }

        switch (style)
        {
            case "grin": OpenMouth(dc, c, 5.6, 3.0, line, teeth: true); break;
            case "small": Smile(dc, c, 2.2, 0.9, pen); break;
            case "smirk": Smirk(dc, c, pen); break;
            case "cat":
            {
                var g = new StreamGeometry();
                using (var ctx = g.Open())
                {
                    ctx.BeginFigure(new Point(c.X - 3.6, c.Y - 0.6), false, false);
                    ctx.QuadraticBezierTo(new Point(c.X - 1.8, c.Y + 2.4), new Point(c.X, c.Y - 0.2), true, true);
                    ctx.QuadraticBezierTo(new Point(c.X + 1.8, c.Y + 2.4), new Point(c.X + 3.6, c.Y - 0.6), true, true);
                }
                dc.DrawGeometry(null, pen, g);
                break;
            }
            case "calm": Smile(dc, c, 3.0, 0.5, pen); break;
            default: Smile(dc, c, 3.8, 1.6, pen); break;
        }
    }

    private static void Smile(DrawingContext dc, Point c, double half, double depth, Pen pen)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(new Point(c.X - half, c.Y - depth * 0.3), false, false);
            ctx.QuadraticBezierTo(new Point(c.X, c.Y + depth * 1.4), new Point(c.X + half, c.Y - depth * 0.3), true, true);
        }
        dc.DrawGeometry(null, pen, g);
    }

    private static void Smirk(DrawingContext dc, Point c, Pen pen)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(new Point(c.X - 3.0, c.Y + 0.4), false, false);
            ctx.QuadraticBezierTo(new Point(c.X + 0.8, c.Y + 1.8), new Point(c.X + 3.6, c.Y - 1.2), true, true);
        }
        dc.DrawGeometry(null, pen, g);
    }

    private static void Wave(DrawingContext dc, Point c, double half, Pen pen)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(new Point(c.X - half, c.Y + 0.6), false, false);
            ctx.BezierTo(new Point(c.X - half / 2, c.Y - 1.2), new Point(c.X - half / 4, c.Y - 1.2), c, true, true);
            ctx.BezierTo(new Point(c.X + half / 4, c.Y + 1.2), new Point(c.X + half / 2, c.Y + 1.2), new Point(c.X + half, c.Y - 0.6), true, true);
        }
        dc.DrawGeometry(null, pen, g);
    }

    private static void OpenMouth(DrawingContext dc, Point c, double half, double depth, Color line, bool teeth)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(new Point(c.X - half, c.Y - depth * 0.35), true, true);
            ctx.QuadraticBezierTo(new Point(c.X, c.Y - depth * 0.1 - 0.6), new Point(c.X + half, c.Y - depth * 0.35), true, true);
            ctx.BezierTo(new Point(c.X + half * 0.8, c.Y + depth * 1.1), new Point(c.X - half * 0.8, c.Y + depth * 1.1), new Point(c.X - half, c.Y - depth * 0.35), true, true);
        }
        g.Freeze();
        dc.DrawGeometry(Brush(MouthInside), null, g);
        dc.PushClip(g);
        dc.DrawEllipse(Brush(Tongue), null, new Point(c.X, c.Y + depth * 0.95), half * 0.62, depth * 0.55);
        if (teeth) dc.DrawRectangle(Brush(Colors.White), null, new Rect(c.X - half, c.Y - depth, half * 2, depth * 0.95));
        dc.Pop();
        dc.DrawGeometry(null, Pen(line, 0.9), g);
    }

    // ---------------------------------------------------------------- cheeks and facial hair

    private static void DrawCheeks(DrawingContext dc, PalProfile p, Color skin, double spacing, double eyeLat)
    {
        if (p.Cheeks is "blush" or "both")
        {
            foreach (var side in new[] { -1, 1 })
            {
                var blush = new RadialGradientBrush(Color.FromArgb(120, 255, 120, 130), Color.FromArgb(0, 255, 120, 130));
                blush.Freeze();
                dc.DrawEllipse(blush, null, At(side * (spacing + 9), eyeLat - 13), 8, 6);
            }
        }
        if (p.Cheeks is "freckles" or "both")
        {
            var freckle = Brush(Color.FromArgb(150, (byte)(skin.R * 0.62), (byte)(skin.G * 0.5), (byte)(skin.B * 0.42)));
            foreach (var side in new[] { -1, 1 })
            {
                foreach (var (dx, dy) in new[] { (7.0, -9.0), (11.0, -7.5), (9.5, -12.0), (14.0, -10.5) })
                    dc.DrawEllipse(freckle, null, At(side * (spacing - 6 + dx), eyeLat + dy), 0.75, 0.75);
            }
        }
        if (p.Cheeks == "mole")
            dc.DrawEllipse(Brush(Color.FromArgb(200, 80, 50, 45)), null, At(9, -22), 0.8, 0.8);
    }

    private static void DrawFacialHair(DrawingContext dc, PalProfile p, Color hair)
    {
        switch (p.FacialHair)
        {
            case "stubble":
            {
                var dot = Brush(Color.FromArgb(70, hair.R, hair.G, hair.B));
                var rng = new Random(11);
                for (var k = 0; k < 420; k++)
                {
                    var lon = rng.NextDouble() * 76 - 38;
                    var lat = -18 - rng.NextDouble() * 34;
                    // Keep the lips clear and follow the jaw.
                    if (Math.Abs(lon) < 6 && lat > -27) continue;
                    if (Math.Abs(lon) > 38 - (lat + 52) * 0.2) continue;
                    dc.DrawEllipse(dot, null, At(lon, lat), 0.45, 0.45);
                }
                break;
            }
            case "mustache":
                Mustache(dc, hair);
                break;
            case "goatee":
            case "beard":
                // The chin hair itself is modelled in 3D; only the mustache is painted.
                Mustache(dc, hair);
                break;
        }
    }

    private static void Mustache(DrawingContext dc, Color hair)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(At(0, -17.5), true, true);
            ctx.BezierTo(At(4, -15), At(9, -17), At(10.5, -22.5), true, true);
            ctx.QuadraticBezierTo(At(5, -20.5), At(0, -21), true, true);
            ctx.QuadraticBezierTo(At(-5, -20.5), At(-10.5, -22.5), true, true);
            ctx.BezierTo(At(-9, -17), At(-4, -15), At(0, -17.5), true, true);
        }
        dc.DrawGeometry(Brush(hair), null, g);
    }

    private static SolidColorBrush Brush(Color c) => AvatarMaterials.Brush(c);

    private static Pen Pen(Color c, double thickness)
    {
        var pen = new Pen(Brush(c), thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        pen.Freeze();
        return pen;
    }
}
