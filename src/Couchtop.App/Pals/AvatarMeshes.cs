using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace Couchtop.App.Pals;

/// <summary>
/// Procedural geometry for Pals. Everything is a parametric surface S(u, v) with u around (0.5 = the front,
/// facing +Z) and v from bottom to top, so textures such as the face and shirt prints land where expected.
/// </summary>
public static class AvatarMeshes
{
    public const double Tau = Math.PI * 2;

    /// <summary>
    /// Builds a grid over (u, v) in [u0, u1] x [v0, v1]. Normals come from the surface itself, so smooth shapes
    /// stay smooth. <paramref name="flipV"/> maps texture y so the top of the surface is the top of the image.
    /// </summary>
    public static MeshGeometry3D Surface(Func<double, double, Point3D> s, int uSteps, int vSteps,
        double u0 = 0, double u1 = 1, double v0 = 0, double v1 = 1, Func<double, double, Vector3D>? fallbackNormal = null)
    {
        var mesh = new MeshGeometry3D();
        var positions = new Point3DCollection((uSteps + 1) * (vSteps + 1));
        var normals = new Vector3DCollection((uSteps + 1) * (vSteps + 1));
        var uvs = new PointCollection((uSteps + 1) * (vSteps + 1));
        const double e = 1e-4;
        for (var j = 0; j <= vSteps; j++)
        {
            var v = v0 + (v1 - v0) * j / vSteps;
            for (var i = 0; i <= uSteps; i++)
            {
                var u = u0 + (u1 - u0) * i / uSteps;
                var p = s(u, v);
                var du = s(u + e, v) - s(u - e, v);
                var dv = s(u, Math.Min(v1, v + e)) - s(u, Math.Max(v0, v - e));
                var n = Vector3D.CrossProduct(du, dv);
                if (n.LengthSquared < 1e-14) n = fallbackNormal?.Invoke(u, v) ?? new Vector3D(p.X, p.Y, p.Z);
                if (n.LengthSquared < 1e-14) n = new Vector3D(0, v > 0.5 ? 1 : -1, 0);
                n.Normalize();
                positions.Add(p);
                normals.Add(n);
                uvs.Add(new Point((u - u0) / (u1 - u0), 1 - (v - v0) / (v1 - v0)));
            }
        }
        var indices = new Int32Collection(uSteps * vSteps * 6);
        for (var j = 0; j < vSteps; j++)
        {
            for (var i = 0; i < uSteps; i++)
            {
                var a = j * (uSteps + 1) + i;
                var b = a + 1;
                var c = a + uSteps + 1;
                var d = c + 1;
                indices.Add(a); indices.Add(b); indices.Add(c);
                indices.Add(c); indices.Add(b); indices.Add(d);
            }
        }
        mesh.Positions = positions;
        mesh.Normals = normals;
        mesh.TextureCoordinates = uvs;
        mesh.TriangleIndices = indices;
        mesh.Freeze();
        return mesh;
    }

    /// <summary>Unit direction for longitude u (0.5 = front) and latitude v (0 = south pole, 1 = north pole).</summary>
    public static Vector3D Direction(double u, double v)
    {
        var theta = Tau * (u - 0.5);
        var phi = Math.PI * (v - 0.5);
        return new Vector3D(Math.Cos(phi) * Math.Sin(theta), Math.Sin(phi), Math.Cos(phi) * Math.Cos(theta));
    }

    public static double LatitudeToV(double degrees) => 0.5 + degrees / 180;
    public static double LongitudeToU(double degrees) => 0.5 + degrees / 360;

    public static MeshGeometry3D Ellipsoid(double rx, double ry, double rz, int slices = 28, int stacks = 18) =>
        Surface((u, v) => Scale(Direction(u, v), rx, ry, rz), slices, stacks);

    public static readonly MeshGeometry3D UnitSphere = Ellipsoid(1, 1, 1, 26, 16);
    public static readonly MeshGeometry3D SmallSphere = Ellipsoid(1, 1, 1, 14, 9);

    /// <summary>A capsule-like limb from y = 0 down to y = -length, radius tapering from top to bottom.</summary>
    public static MeshGeometry3D Limb(double length, double topRadius, double bottomRadius, int slices = 18)
    {
        // v runs bottom (0) to top (1); both ends are closed with rounded caps.
        const double capShare = 0.18;
        return Surface((u, v) =>
        {
            double y, r;
            var theta = Tau * (u - 0.5);
            if (v < capShare)
            {
                var a = v / capShare * Math.PI / 2;
                r = bottomRadius * Math.Sin(a);
                y = -length + bottomRadius * (1 - Math.Cos(a)) * 0.9;
            }
            else if (v > 1 - capShare)
            {
                var a = (1 - v) / capShare * Math.PI / 2;
                r = topRadius * Math.Sin(a);
                y = -topRadius * 0.9 * (1 - Math.Cos(a));
            }
            else
            {
                var t = (v - capShare) / (1 - 2 * capShare);
                r = bottomRadius + (topRadius - bottomRadius) * t;
                var bottomY = -length + bottomRadius * 0.9;
                var topY = -topRadius * 0.9;
                y = bottomY + (topY - bottomY) * t;
            }
            return new Point3D(r * Math.Sin(theta), y, r * Math.Cos(theta));
        }, slices, 18);
    }

    /// <summary>Surface of revolution from a (radius, y) profile listed bottom to top; u = 0.5 faces +Z.</summary>
    public static MeshGeometry3D Lathe(IReadOnlyList<(double R, double Y)> profile, int slices = 32, double scaleX = 1, double scaleZ = 1)
    {
        var count = profile.Count;
        return Surface((u, v) =>
        {
            var f = v * (count - 1);
            var i = Math.Min(count - 2, (int)Math.Floor(f));
            var t = f - i;
            var r = Smooth(profile, i, t, p => p.R);
            var y = Smooth(profile, i, t, p => p.Y);
            var theta = Tau * (u - 0.5);
            return new Point3D(r * Math.Sin(theta) * scaleX, y, r * Math.Cos(theta) * scaleZ);
        }, slices, Math.Max(8, (count - 1) * 4));
    }

    /// <summary>Catmull-Rom through the profile points so lathed shapes have no visible kinks.</summary>
    private static double Smooth(IReadOnlyList<(double R, double Y)> p, int i, double t, Func<(double R, double Y), double> pick)
    {
        var p0 = pick(p[Math.Max(0, i - 1)]);
        var p1 = pick(p[i]);
        var p2 = pick(p[Math.Min(p.Count - 1, i + 1)]);
        var p3 = pick(p[Math.Min(p.Count - 1, i + 2)]);
        var t2 = t * t;
        var t3 = t2 * t;
        return 0.5 * (2 * p1 + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3);
    }

    /// <summary>Ring lying in the XZ plane (around the Y axis), tube radius <paramref name="minor"/>.</summary>
    public static MeshGeometry3D Ring(double major, double minor, int segments = 32, int sides = 10, double arcDegrees = 360, double scaleX = 1, double scaleZ = 1, double phaseDegrees = 0) =>
        Surface((u, v) =>
        {
            var a = ((u - 0.5) * arcDegrees + phaseDegrees) * Math.PI / 180;
            var b = Tau * v;
            var center = new Vector3D(Math.Sin(a) * major * scaleX, 0, Math.Cos(a) * major * scaleZ);
            var outward = new Vector3D(Math.Sin(a), 0, Math.Cos(a));
            var p = center + outward * (Math.Cos(b) * minor) + new Vector3D(0, Math.Sin(b) * minor, 0);
            return new Point3D(p.X, p.Y, p.Z);
        }, segments, sides);

    /// <summary>Cone or cylinder from y = 0 up to y = height with a rounded tip, u = 0.5 facing +Z.</summary>
    public static MeshGeometry3D Cone(double baseRadius, double tipRadius, double height, int slices = 18) =>
        Lathe(new[] { (0.0001, 0.0), (baseRadius * 0.98, 0.0), (baseRadius, height * 0.03), ((baseRadius + tipRadius) / 2, height * 0.5), (tipRadius, height * 0.97), (0.0001, height) }, slices);

    /// <summary>Flat, double-sided-friendly disc facing +Z in the XY plane.</summary>
    public static MeshGeometry3D DiscZ(double rx, double ry, int slices = 36)
    {
        var mesh = new MeshGeometry3D();
        mesh.Positions.Add(new Point3D(0, 0, 0));
        mesh.Normals.Add(new Vector3D(0, 0, 1));
        mesh.TextureCoordinates.Add(new Point(0.5, 0.5));
        for (var k = 0; k <= slices; k++)
        {
            var a = Tau * k / slices;
            mesh.Positions.Add(new Point3D(Math.Cos(a) * rx, Math.Sin(a) * ry, 0));
            mesh.Normals.Add(new Vector3D(0, 0, 1));
            mesh.TextureCoordinates.Add(new Point(0.5 + 0.5 * Math.Cos(a), 0.5 - 0.5 * Math.Sin(a)));
        }
        for (var k = 0; k < slices; k++)
        {
            mesh.TriangleIndices.Add(0);
            mesh.TriangleIndices.Add(k + 1);
            mesh.TriangleIndices.Add(k + 2);
        }
        mesh.Freeze();
        return mesh;
    }

    /// <summary>Flat shape facing +Z from an outline (a star lens, a chest print).</summary>
    public static MeshGeometry3D Polygon(IReadOnlyList<Point> outline)
    {
        var mesh = new MeshGeometry3D();
        mesh.Positions.Add(new Point3D(0, 0, 0));
        mesh.Normals.Add(new Vector3D(0, 0, 1));
        foreach (var p in outline)
        {
            mesh.Positions.Add(new Point3D(p.X, p.Y, 0));
            mesh.Normals.Add(new Vector3D(0, 0, 1));
        }
        for (var k = 0; k < outline.Count; k++)
        {
            mesh.TriangleIndices.Add(0);
            mesh.TriangleIndices.Add(k + 1);
            mesh.TriangleIndices.Add((k + 1) % outline.Count + 1);
        }
        mesh.Freeze();
        return mesh;
    }

    public static IReadOnlyList<Point> Star(double outer, double inner, int points = 5)
    {
        var list = new List<Point>();
        for (var k = 0; k < points * 2; k++)
        {
            var r = k % 2 == 0 ? outer : inner;
            var a = Math.PI / 2 + Math.PI * k / points;
            list.Add(new Point(Math.Cos(a) * r, Math.Sin(a) * r));
        }
        return list;
    }

    /// <summary>A soft rounded box (superellipsoid), good for shoes, backpacks and brims.</summary>
    public static MeshGeometry3D RoundedBox(double sx, double sy, double sz, double roundness = 0.35, int slices = 24, int stacks = 14)
    {
        var e = Math.Clamp(roundness, 0.05, 1);
        return Surface((u, v) =>
        {
            var theta = Tau * (u - 0.5);
            var phi = Math.PI * (v - 0.5);
            double Sp(double w, double m) => Math.Sign(w) * Math.Pow(Math.Abs(w), m);
            var cp = Sp(Math.Cos(phi), e);
            return new Point3D(sx / 2 * cp * Sp(Math.Sin(theta), e), sy / 2 * Sp(Math.Sin(phi), e), sz / 2 * cp * Sp(Math.Cos(theta), e));
        }, slices, stacks);
    }

    public static Point3D Scale(Vector3D d, double rx, double ry, double rz) => new(d.X * rx, d.Y * ry, d.Z * rz);
}

/// <summary>Materials tuned for the Pal look: soft vinyl-toy plastic with a gentle sheen.</summary>
public static class AvatarMaterials
{
    public static Color Parse(string hex)
    {
        var (r, g, b) = Core.Pals.PalColors.Rgb(hex);
        return Color.FromRgb(r, g, b);
    }

    public static Color Mix(Color a, Color b, double t) => Color.FromArgb(
        (byte)(a.A + (b.A - a.A) * t), (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    public static Color Darken(Color c, double amount) => Mix(c, Color.FromRgb(20, 16, 24), amount);
    public static Color Lighten(Color c, double amount) => Mix(c, Colors.White, amount);

    public static SolidColorBrush Brush(Color c)
    {
        var brush = new SolidColorBrush(c);
        brush.Freeze();
        return brush;
    }

    /// <summary>Diffuse color, a hint of self-light so shadows never go muddy, and an optional highlight.</summary>
    public static Material Soft(Color color, double gloss = 0.22, double power = 28)
    {
        var group = new MaterialGroup();
        group.Children.Add(new DiffuseMaterial(Brush(color)));
        group.Children.Add(new EmissiveMaterial(Brush(Color.FromArgb(255, (byte)(color.R * 0.12), (byte)(color.G * 0.12), (byte)(color.B * 0.12)))));
        if (gloss > 0) group.Children.Add(new SpecularMaterial(Brush(Color.FromArgb((byte)(Math.Clamp(gloss, 0, 1) * 255), 255, 255, 255)), power));
        group.Freeze();
        return group;
    }

    public static Material Brushed(Brush brush, double gloss = 0, double power = 28, Color? glow = null)
    {
        var group = new MaterialGroup();
        group.Children.Add(new DiffuseMaterial(brush));
        if (glow is { } g) group.Children.Add(new EmissiveMaterial(Brush(g)));
        if (gloss > 0) group.Children.Add(new SpecularMaterial(Brush(Color.FromArgb((byte)(gloss * 255), 255, 255, 255)), power));
        group.Freeze();
        return group;
    }

    public static Material Shiny(Color color) => Soft(color, 0.75, 70);

    public static Material Glow(Color color)
    {
        var group = new MaterialGroup();
        group.Children.Add(new DiffuseMaterial(Brush(Darken(color, 0.4))));
        group.Children.Add(new EmissiveMaterial(Brush(color)));
        group.Freeze();
        return group;
    }

    public static Material Flat(Brush brush)
    {
        var material = new DiffuseMaterial(brush);
        material.Freeze();
        return material;
    }
}
