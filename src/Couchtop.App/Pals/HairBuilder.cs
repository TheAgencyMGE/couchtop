using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Couchtop.Core.Pals;

namespace Couchtop.App.Pals;

/// <summary>
/// Builds hairstyles in unit head space. Every style starts from a shell that hugs the head, with its own
/// hairline (how far down it reaches at the front, sides and back) and thickness, and a rolled edge so it
/// never looks paper thin. Spikes, curls, tails and buns are added on top.
/// </summary>
internal static class HairBuilder
{
    private readonly record struct Line(double Front, double Side, double Back);

    public static void Build(AvatarModel model, PalProfile p, Model3DGroup group, Color color)
    {
        BuildFacialHair(model, p, group, color);
        var style = p.HairStyle;
        if (style == "bald") return;
        var hatted = p.Hat is "cap" or "beanie" or "bucket" or "wizard";
        // Big-volume styles tuck under a hat as a short cut, so nothing pokes through the crown.
        if (hatted && style is "afro" or "mohawk" or "spiky" or "bun") style = style == "afro" ? "curly" : "short";

        var tips = p.HairTips == "none" ? (Color?)null : AvatarMaterials.Parse(p.HairTips);
        var shellMat = HairMaterial(color, tips, tipStart: 0.72);
        var pieceMat = HairMaterial(color, tips, tipStart: 0.35);
        var solid = AvatarMaterials.Soft(AvatarMaterials.Lighten(color, 0.04), 0.32, 34);

        switch (style)
        {
            case "buzz":
                Shell(model, group, shellMat, new Line(36, 12, -24), (_, _) => 1.028);
                break;
            case "short":
                Shell(model, group, shellMat, new Line(30, 4, -28), (_, lat) => 1.072 + 0.02 * Math.Sin(lat * Math.PI / 180));
                break;
            case "swept":
                Shell(model, group, shellMat, lon => Hairline(lon, 24 + 12 * Math.Sin(lon * Math.PI / 180), 4, -28),
                    (lon, lat) => 1.075 + 0.05 * Front(lon) * Band(lat, 25, 60));
                group.Children.Add(AvatarModel.Model(AvatarMeshes.Ellipsoid(0.62, 0.2, 0.36, 22, 12), solid, AvatarModel.At(0.18, 0.74, 0.5, pitch: -26, roll: -18, yaw: 10)));
                break;
            case "spiky":
                Shell(model, group, shellMat, new Line(30, 4, -26), (_, _) => 1.06);
                foreach (var (lon, lat, length) in new[]
                         {
                             (0.0, 58.0, 0.5), (-38.0, 48.0, 0.44), (38.0, 48.0, 0.44), (0.0, 80.0, 0.52), (-70.0, 42.0, 0.38), (70.0, 42.0, 0.38),
                             (180.0, 60.0, 0.46), (-130.0, 40.0, 0.4), (130.0, 40.0, 0.4), (-20.0, 38.0, 0.34), (20.0, 38.0, 0.34), (180.0, 25.0, 0.34),
                         })
                    Spike(model, group, pieceMat, lon, lat, length, 0.2);
                break;
            case "curly":
                Shell(model, group, shellMat, new Line(30, 2, -30), (_, _) => 1.07);
                Curls(model, group, solid, new Line(34, 6, -24), 1.06, 0.21, 110);
                break;
            case "side-part":
                Shell(model, group, shellMat, lon => Hairline(lon, 28 - 6 * Math.Sin(lon * Math.PI / 180), 4, -28),
                    (lon, lat) => 1.075 + 0.07 * Math.Max(0, Math.Sin(lon * Math.PI / 180)) * Band(lat, 20, 75));
                group.Children.Add(AvatarModel.Model(AvatarMeshes.Ellipsoid(0.5, 0.26, 0.46, 20, 12), solid, AvatarModel.At(0.42, 0.74, 0.22, roll: -22)));
                break;
            case "bob":
                Shell(model, group, shellMat, new Line(22, -40, -44), (_, lat) => 1.085 + 0.12 * Math.Clamp((20 - lat) / 60, 0, 1));
                break;
            case "long":
                Shell(model, group, shellMat, new Line(24, -42, -46), (_, lat) => 1.085 + 0.1 * Math.Clamp((20 - lat) / 60, 0, 1));
                LongBack(group, shellMat);
                foreach (var side in new[] { -1, 1 })
                    group.Children.Add(AvatarModel.Model(AvatarMeshes.Ellipsoid(0.24, 0.62, 0.26, 18, 12), pieceMat, AvatarModel.At(side * 0.93, -0.72, -0.06, roll: side * -6)));
                break;
            case "ponytail":
            {
                Shell(model, group, shellMat, new Line(30, 0, -22), (_, _) => 1.07);
                Tail(model, group, pieceMat, solid, 0, new Point3D(0, 0.58, -0.9), 0);
                break;
            }
            case "twin-tails":
                Shell(model, group, shellMat, new Line(28, -2, -24), (_, _) => 1.07);
                foreach (var side in new[] { -1, 1 })
                    Tail(model, group, pieceMat, solid, side, new Point3D(side * 0.84, 0.44, -0.3), side);
                break;
            case "bun":
                Shell(model, group, shellMat, new Line(30, 2, -22), (_, _) => 1.055);
                group.Children.Add(AvatarModel.Model(AvatarMeshes.Ellipsoid(0.42, 0.4, 0.42, 22, 14), pieceMat, AvatarModel.At(0, 0.98, -0.34)));
                group.Children.Add(AvatarModel.Model(AvatarMeshes.Ring(0.3, 0.06, 24, 8), Tie(p), AvatarModel.At(0, 0.72, -0.26, pitch: -30)));
                break;
            case "afro":
                Shell(model, group, shellMat, new Line(26, -8, -32),
                    (lon, lat) => 1.34 + 0.045 * Math.Sin(lon * Math.PI / 180 * 9) * Math.Sin(lat * Math.PI / 180 * 7), steps: 56);
                break;
            case "mohawk":
                Shell(model, group, shellMat, new Line(36, 12, -22), (_, _) => 1.025);
                for (var k = 0; k < 9; k++)
                {
                    // March the crest from the forehead, over the top, to the back of the head.
                    var along = k / 8.0;
                    var lon = along < 0.5 ? 0 : 180;
                    var lat = along < 0.5 ? 40 + along * 2 * 50 : 90 - (along - 0.5) * 2 * 90;
                    Spike(model, group, pieceMat, lon, lat, 0.34, 0.16, flatten: 0.4);
                }
                break;
        }
    }

    /// <summary>Beards and goatees are real volume on the jaw, following the face shape.</summary>
    private static void BuildFacialHair(AvatarModel model, PalProfile p, Model3DGroup group, Color color)
    {
        if (p.FacialHair is not ("beard" or "goatee")) return;
        var material = AvatarMaterials.Soft(color, 0.18, 24);
        var beard = p.FacialHair == "beard";
        var span = beard ? 88.0 : 20.0;
        var mesh = AvatarMeshes.Surface((u, v) =>
        {
            var lon = (u - 0.5) * 2 * span;
            var edge = Math.Abs(lon) / span;
            // Top edge: under the lip in the middle, up to the sideburns at the sides.
            var top = beard ? -31 + 26 * Math.Pow(edge, 1.6) : -31;
            var bottom = beard ? -74 : -52;
            var lat = bottom + (top - bottom) * v;
            var fullness = beard ? 1.075 - 0.05 * Math.Pow(edge, 3) : 1.06;
            var k = 1 + (fullness - 1) * Math.Sin(Math.Min(1, v * 6) * Math.PI / 2) * Math.Sin(Math.Min(1, (1 - v) * 3 + 0.2) * Math.PI / 2) * (1 - Math.Pow(edge, 8));
            return model.HeadPointAt(lon, lat, k);
        }, 30, 14);
        group.Children.Add(AvatarModel.Model(mesh, material));
    }

    private static Material Tie(PalProfile p) => AvatarMaterials.Soft(AvatarMaterials.Parse(p.TopAccent == "#FFFFFF" ? p.TopColor : p.TopAccent), 0.3);

    /// <summary>Darker at the roots and under the edge, lighter on top, optional dyed tips.</summary>
    private static Material HairMaterial(Color color, Color? tips, double tipStart)
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        brush.GradientStops.Add(new GradientStop(AvatarMaterials.Lighten(color, 0.1), 0));
        brush.GradientStops.Add(new GradientStop(color, 0.45));
        if (tips is { } t)
        {
            brush.GradientStops.Add(new GradientStop(color, tipStart));
            brush.GradientStops.Add(new GradientStop(t, Math.Min(1, tipStart + 0.18)));
            brush.GradientStops.Add(new GradientStop(t, 1));
        }
        else
        {
            brush.GradientStops.Add(new GradientStop(AvatarMaterials.Darken(color, 0.22), 1));
        }
        brush.Freeze();
        return AvatarMaterials.Brushed(brush, 0.34, 36, Color.FromRgb((byte)(color.R * 0.1), (byte)(color.G * 0.1), (byte)(color.B * 0.1)));
    }

    private static double Front(double lon) => Math.Pow(Math.Max(0, Math.Cos(lon * Math.PI / 180)), 2);
    private static double Band(double lat, double from, double to) => lat < from || lat > to ? 0 : Math.Sin((lat - from) / (to - from) * Math.PI);

    /// <summary>Hairline latitude around the head: <paramref name="front"/> at the forehead, blending through the sides to the back.</summary>
    private static double Hairline(double lon, double front, double side, double back)
    {
        var c = Math.Cos(lon * Math.PI / 180);
        return side + (front - side) * Math.Pow(Math.Max(0, c), 2) + (back - side) * Math.Pow(Math.Max(0, -c), 2);
    }

    private static void Shell(AvatarModel model, Model3DGroup group, Material material, Line line, Func<double, double, double> thickness, int steps = 44) =>
        Shell(model, group, material, lon => Hairline(lon, line.Front, line.Side, line.Back), thickness, steps);

    private static void Shell(AvatarModel model, Model3DGroup group, Material material, Func<double, double> hairline, Func<double, double, double> thickness, int steps = 44)
    {
        const double lip = 0.1;
        var mesh = AvatarMeshes.Surface((u, v) =>
        {
            var lon = (u - 0.5) * 360;
            var bottom = hairline(lon);
            if (v < lip)
            {
                // The rolled underside edge, from the scalp out to the full thickness.
                var t = v / lip;
                var k = 1.0 + (thickness(lon, bottom) - 1.0) * Math.Sin(t * Math.PI / 2);
                return model.HeadPointAt(lon, bottom - 2 * (1 - t), k);
            }
            var s = (v - lip) / (1 - lip);
            var lat = bottom + (90 - bottom) * s;
            return model.HeadPointAt(lon, lat, thickness(lon, lat));
        }, steps, 26);
        group.Children.Add(AvatarModel.Model(mesh, material));
    }

    private static void Curls(AvatarModel model, Model3DGroup group, Material material, Line line, double k, double radius, int count)
    {
        // Fibonacci spiral: an even scatter over the scalp with no obvious rows.
        var golden = Math.PI * (3 - Math.Sqrt(5));
        for (var i = 0; i < count * 2; i++)
        {
            var y = 1 - (i + 0.5) / (count * 2) * 2;
            var lat = Math.Asin(y) * 180 / Math.PI;
            var lon = (i * golden * 180 / Math.PI) % 360 - 180;
            if (lat < Hairline(lon, line.Front, line.Side, line.Back) + 4) continue;
            var at = model.HeadPointAt(lon, lat, k);
            var r = radius * (0.85 + 0.3 * ((i * 37) % 10) / 10.0);
            group.Children.Add(AvatarModel.Model(AvatarMeshes.SmallSphere, material, AvatarModel.At(at.X, at.Y, at.Z, sx: r, sy: r, sz: r)));
        }
    }

    private static void Spike(AvatarModel model, Model3DGroup group, Material material, double lon, double lat, double length, double radius, double flatten = 1)
    {
        var at = model.HeadPointAt(lon, lat, 1.02);
        var dir = new Vector3D(at.X, at.Y + 0.35, at.Z - 0.15);
        dir.Normalize();
        var axis = Vector3D.CrossProduct(new Vector3D(0, 1, 0), dir);
        var angle = Math.Acos(Math.Clamp(Vector3D.DotProduct(new Vector3D(0, 1, 0), dir), -1, 1)) * 180 / Math.PI;
        var transform = new Transform3DGroup();
        transform.Children.Add(new ScaleTransform3D(flatten, 1, 1));
        if (axis.LengthSquared > 1e-9) transform.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(axis, angle)));
        transform.Children.Add(new TranslateTransform3D(at.X, at.Y, at.Z));
        transform.Freeze();
        group.Children.Add(AvatarModel.Model(AvatarMeshes.Cone(radius, 0.015, length, 14), material, transform));
    }

    /// <summary>Hair falling down the back, behind the shoulders.</summary>
    private static void LongBack(Model3DGroup group, Material material)
    {
        var mesh = AvatarMeshes.Surface((u, v) =>
        {
            var lon = 110 + 140 * u;
            var a = lon * Math.PI / 180;
            var y = -2.0 + 2.2 * v;
            var r = 1.02 + 0.1 * (1 - v) - 0.18 * Math.Pow(1 - v, 3);
            return new Point3D(Math.Sin(a) * r, y, Math.Cos(a) * r * 0.9 - 0.08);
        }, 24, 16);
        var model = AvatarModel.Model(mesh, material);
        model.BackMaterial = material;
        group.Children.Add(model);
    }

    /// <summary>A ponytail or pigtail on its own joint, so it can swing as the Pal moves.</summary>
    private static void Tail(AvatarModel model, Model3DGroup group, Material material, Material tie, double side, Point3D anchor, double outward)
    {
        var joint = new Joint(group, anchor.X, anchor.Y, anchor.Z);
        group.Children.Add(AvatarModel.Model(AvatarMeshes.Ring(0.13, 0.05, 20, 8), tie, AvatarModel.At(anchor.X, anchor.Y, anchor.Z, pitch: side == 0 ? -40 : 0, roll: side * 70)));
        var steps = new[] { (0.0, -0.05, -0.08, 0.24), (outward * 0.08, -0.3, -0.16, 0.25), (outward * 0.14, -0.62, -0.18, 0.21), (outward * 0.16, -0.92, -0.16, 0.16), (outward * 0.14, -1.14, -0.12, 0.1) };
        foreach (var (x, y, z, r) in steps)
            joint.Group.Children.Add(AvatarModel.Model(AvatarMeshes.Ellipsoid(r, r * 1.35, r, 16, 10), material, new TranslateTransform3D(x, y, z)));
        model.AddTail(joint, side, 1.2);
    }
}
