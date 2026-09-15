using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace Couchtop.App.Sports;

/// <summary>Procedural meshes for the stylized sports scenes (everything is generated in code).</summary>
public static class Mesh3D
{
    /// <summary>Unit-style sphere around the origin. <paramref name="fromLatitude"/> 0 gives the upper hemisphere.</summary>
    public static MeshGeometry3D Sphere(double radius, int slices = 24, int stacks = 14, double fromLatitude = -90)
    {
        var mesh = new MeshGeometry3D();
        var minLat = fromLatitude * Math.PI / 180;
        for (var s = 0; s <= stacks; s++)
        {
            var lat = minLat + (Math.PI / 2 - minLat) * s / stacks;
            var y = Math.Sin(lat);
            var r = Math.Cos(lat);
            for (var k = 0; k <= slices; k++)
            {
                var lon = 2 * Math.PI * k / slices;
                var n = new Vector3D(r * Math.Sin(lon), y, r * Math.Cos(lon));
                mesh.Positions.Add(new Point3D(n.X * radius, n.Y * radius, n.Z * radius));
                mesh.Normals.Add(n);
                mesh.TextureCoordinates.Add(new Point((double)k / slices, 1 - (double)s / stacks));
            }
        }
        for (var s = 0; s < stacks; s++)
        {
            for (var k = 0; k < slices; k++)
            {
                var a = s * (slices + 1) + k;
                var b = a + slices + 1;
                mesh.TriangleIndices.Add(a);
                mesh.TriangleIndices.Add(a + 1);
                mesh.TriangleIndices.Add(b);
                mesh.TriangleIndices.Add(b);
                mesh.TriangleIndices.Add(a + 1);
                mesh.TriangleIndices.Add(b + 1);
            }
        }
        mesh.Freeze();
        return mesh;
    }

    /// <summary>Upright cylinder or cone from y = 0 to y = height.</summary>
    public static MeshGeometry3D Cylinder(double bottomRadius, double topRadius, double height, int slices = 20, bool caps = true)
    {
        var mesh = new MeshGeometry3D();
        var slope = (bottomRadius - topRadius) / Math.Max(1e-6, height);
        for (var k = 0; k <= slices; k++)
        {
            var angle = 2 * Math.PI * k / slices;
            var sin = Math.Sin(angle);
            var cos = Math.Cos(angle);
            var normal = new Vector3D(sin, slope, cos);
            normal.Normalize();
            mesh.Positions.Add(new Point3D(bottomRadius * sin, 0, bottomRadius * cos));
            mesh.Positions.Add(new Point3D(topRadius * sin, height, topRadius * cos));
            mesh.Normals.Add(normal);
            mesh.Normals.Add(normal);
            mesh.TextureCoordinates.Add(new Point((double)k / slices, 1));
            mesh.TextureCoordinates.Add(new Point((double)k / slices, 0));
        }
        for (var k = 0; k < slices; k++)
        {
            var b = 2 * k;
            mesh.TriangleIndices.Add(b);
            mesh.TriangleIndices.Add(b + 2);
            mesh.TriangleIndices.Add(b + 1);
            mesh.TriangleIndices.Add(b + 1);
            mesh.TriangleIndices.Add(b + 2);
            mesh.TriangleIndices.Add(b + 3);
        }
        if (caps)
        {
            AddCap(mesh, topRadius, height, slices, up: true);
            AddCap(mesh, bottomRadius, 0, slices, up: false);
        }
        mesh.Freeze();
        return mesh;
    }

    private static void AddCap(MeshGeometry3D mesh, double radius, double y, int slices, bool up)
    {
        if (radius <= 0) return;
        var normal = new Vector3D(0, up ? 1 : -1, 0);
        var center = mesh.Positions.Count;
        mesh.Positions.Add(new Point3D(0, y, 0));
        mesh.Normals.Add(normal);
        mesh.TextureCoordinates.Add(new Point(0.5, 0.5));
        for (var k = 0; k <= slices; k++)
        {
            var angle = 2 * Math.PI * k / slices;
            mesh.Positions.Add(new Point3D(radius * Math.Sin(angle), y, radius * Math.Cos(angle)));
            mesh.Normals.Add(normal);
            mesh.TextureCoordinates.Add(new Point(0.5 + 0.5 * Math.Sin(angle), 0.5 + 0.5 * Math.Cos(angle)));
        }
        for (var k = 0; k < slices; k++)
        {
            mesh.TriangleIndices.Add(center);
            mesh.TriangleIndices.Add(center + 1 + (up ? k : k + 1));
            mesh.TriangleIndices.Add(center + 1 + (up ? k + 1 : k));
        }
    }

    /// <summary>Axis-aligned box centered on the origin.</summary>
    public static MeshGeometry3D Box(double sx, double sy, double sz)
    {
        var mesh = new MeshGeometry3D();
        var hx = sx / 2;
        var hy = sy / 2;
        var hz = sz / 2;
        void Face(Vector3D n, Point3D a, Point3D b, Point3D c, Point3D d)
        {
            var i = mesh.Positions.Count;
            foreach (var p in new[] { a, b, c, d })
            {
                mesh.Positions.Add(p);
                mesh.Normals.Add(n);
            }
            mesh.TextureCoordinates.Add(new Point(0, 1));
            mesh.TextureCoordinates.Add(new Point(1, 1));
            mesh.TextureCoordinates.Add(new Point(1, 0));
            mesh.TextureCoordinates.Add(new Point(0, 0));
            mesh.TriangleIndices.Add(i);
            mesh.TriangleIndices.Add(i + 1);
            mesh.TriangleIndices.Add(i + 2);
            mesh.TriangleIndices.Add(i);
            mesh.TriangleIndices.Add(i + 2);
            mesh.TriangleIndices.Add(i + 3);
        }
        Face(new Vector3D(0, 0, 1), new(-hx, -hy, hz), new(hx, -hy, hz), new(hx, hy, hz), new(-hx, hy, hz));
        Face(new Vector3D(0, 0, -1), new(hx, -hy, -hz), new(-hx, -hy, -hz), new(-hx, hy, -hz), new(hx, hy, -hz));
        Face(new Vector3D(1, 0, 0), new(hx, -hy, hz), new(hx, -hy, -hz), new(hx, hy, -hz), new(hx, hy, hz));
        Face(new Vector3D(-1, 0, 0), new(-hx, -hy, -hz), new(-hx, -hy, hz), new(-hx, hy, hz), new(-hx, hy, -hz));
        Face(new Vector3D(0, 1, 0), new(-hx, hy, hz), new(hx, hy, hz), new(hx, hy, -hz), new(-hx, hy, -hz));
        Face(new Vector3D(0, -1, 0), new(-hx, -hy, -hz), new(hx, -hy, -hz), new(hx, -hy, hz), new(-hx, -hy, hz));
        mesh.Freeze();
        return mesh;
    }

    /// <summary>Flat ground rectangle on y = 0 facing up. The texture's top edge is at +z, left edge at -x.</summary>
    public static MeshGeometry3D Plane(double width, double depth, double tileU = 1, double tileV = 1)
    {
        var mesh = new MeshGeometry3D();
        var hw = width / 2;
        var hd = depth / 2;
        mesh.Positions.Add(new Point3D(-hw, 0, -hd));
        mesh.Positions.Add(new Point3D(hw, 0, -hd));
        mesh.Positions.Add(new Point3D(hw, 0, hd));
        mesh.Positions.Add(new Point3D(-hw, 0, hd));
        for (var i = 0; i < 4; i++) mesh.Normals.Add(new Vector3D(0, 1, 0));
        mesh.TextureCoordinates.Add(new Point(0, tileV));
        mesh.TextureCoordinates.Add(new Point(tileU, tileV));
        mesh.TextureCoordinates.Add(new Point(tileU, 0));
        mesh.TextureCoordinates.Add(new Point(0, 0));
        foreach (var index in new[] { 0, 2, 1, 0, 3, 2 }) mesh.TriangleIndices.Add(index);
        mesh.Freeze();
        return mesh;
    }

    /// <summary>Flat disc on y = 0 facing up.</summary>
    public static MeshGeometry3D Disc(double radius, int slices = 32)
    {
        var mesh = new MeshGeometry3D();
        AddCap(mesh, radius, 0, slices, up: true);
        mesh.Freeze();
        return mesh;
    }

    /// <summary>Surface of revolution around the Y axis from a (radius, y) profile listed bottom to top.</summary>
    public static MeshGeometry3D Lathe(IReadOnlyList<(double R, double Y)> profile, int slices = 18)
    {
        var mesh = new MeshGeometry3D();
        for (var i = 0; i < profile.Count; i++)
        {
            var prev = profile[Math.Max(0, i - 1)];
            var next = profile[Math.Min(profile.Count - 1, i + 1)];
            var dy = next.Y - prev.Y;
            var dr = next.R - prev.R;
            for (var k = 0; k <= slices; k++)
            {
                var angle = 2 * Math.PI * k / slices;
                var sin = Math.Sin(angle);
                var cos = Math.Cos(angle);
                var normal = new Vector3D(dy * sin, -dr, dy * cos);
                if (normal.LengthSquared < 1e-9) normal = new Vector3D(0, dr > 0 ? -1 : 1, 0);
                normal.Normalize();
                mesh.Positions.Add(new Point3D(profile[i].R * sin, profile[i].Y, profile[i].R * cos));
                mesh.Normals.Add(normal);
                mesh.TextureCoordinates.Add(new Point((double)k / slices, 1 - (double)i / (profile.Count - 1)));
            }
        }
        for (var i = 0; i < profile.Count - 1; i++)
        {
            for (var k = 0; k < slices; k++)
            {
                var a = i * (slices + 1) + k;
                var b = a + slices + 1;
                mesh.TriangleIndices.Add(a);
                mesh.TriangleIndices.Add(a + 1);
                mesh.TriangleIndices.Add(b);
                mesh.TriangleIndices.Add(b);
                mesh.TriangleIndices.Add(a + 1);
                mesh.TriangleIndices.Add(b + 1);
            }
        }
        mesh.Freeze();
        return mesh;
    }

    /// <summary>Ring in the XY plane (standing up, facing ±z). Pair with a double-sided material.</summary>
    public static MeshGeometry3D Torus(double major, double minor, int segments = 28, int sides = 10)
    {
        var mesh = new MeshGeometry3D();
        for (var i = 0; i <= segments; i++)
        {
            var u = 2 * Math.PI * i / segments;
            var center = new Vector3D(major * Math.Cos(u), major * Math.Sin(u), 0);
            for (var j = 0; j <= sides; j++)
            {
                var v = 2 * Math.PI * j / sides;
                var normal = new Vector3D(Math.Cos(v) * Math.Cos(u), Math.Cos(v) * Math.Sin(u), Math.Sin(v));
                var p = center + normal * minor;
                mesh.Positions.Add(new Point3D(p.X, p.Y, p.Z));
                mesh.Normals.Add(normal);
            }
        }
        for (var i = 0; i < segments; i++)
        {
            for (var j = 0; j < sides; j++)
            {
                var a = i * (sides + 1) + j;
                var b = a + sides + 1;
                mesh.TriangleIndices.Add(a);
                mesh.TriangleIndices.Add(b);
                mesh.TriangleIndices.Add(a + 1);
                mesh.TriangleIndices.Add(a + 1);
                mesh.TriangleIndices.Add(b);
                mesh.TriangleIndices.Add(b + 1);
            }
        }
        mesh.Freeze();
        return mesh;
    }
}

public static class Mat
{
    public static SolidColorBrush Brush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    /// <summary>Soft plastic look: diffuse color with a gentle highlight.</summary>
    public static Material Solid(Color color, double gloss = 0.25, double power = 30)
    {
        var group = new MaterialGroup();
        group.Children.Add(new DiffuseMaterial(Brush(color)));
        if (gloss > 0) group.Children.Add(new SpecularMaterial(Brush(Color.FromArgb((byte)(Math.Clamp(gloss, 0, 1) * 255), 255, 255, 255)), power));
        group.Freeze();
        return group;
    }

    public static Material Matte(Color color)
    {
        var material = new DiffuseMaterial(Brush(color));
        material.Freeze();
        return material;
    }

    /// <summary>Self-lit color (targets, glints, glowing HUD objects).</summary>
    public static Material Glow(Color color)
    {
        var group = new MaterialGroup();
        group.Children.Add(new DiffuseMaterial(Brush(Color.FromRgb((byte)(color.R * 0.35), (byte)(color.G * 0.35), (byte)(color.B * 0.35)))));
        group.Children.Add(new EmissiveMaterial(Brush(color)));
        group.Freeze();
        return group;
    }

    public static Material Texture(ImageSource image, bool tile = false, double gloss = 0)
    {
        var brush = new ImageBrush(image) { Stretch = Stretch.Fill };
        if (tile)
        {
            brush.TileMode = TileMode.Tile;
            brush.ViewportUnits = BrushMappingMode.Absolute;
            brush.Viewport = new Rect(0, 0, 1, 1);
        }
        brush.Freeze();
        var group = new MaterialGroup();
        group.Children.Add(new DiffuseMaterial(brush));
        if (gloss > 0) group.Children.Add(new SpecularMaterial(Brush(Color.FromArgb((byte)(gloss * 255), 255, 255, 255)), 25));
        group.Freeze();
        return group;
    }

    public static Material Shadow(double alpha = 0.28) => Matte(Color.FromArgb((byte)(alpha * 255), 10, 20, 30));
}

public static class Scene
{
    public static GeometryModel3D Model(MeshGeometry3D mesh, Material material, Transform3D? transform = null, bool doubleSided = false)
    {
        var model = new GeometryModel3D(mesh, material);
        if (doubleSided) model.BackMaterial = material;
        if (transform is not null) model.Transform = transform;
        return model;
    }

    /// <summary>Scale, then turn around Y, then move. Frozen, for static scenery.</summary>
    public static Transform3D At(double x, double y, double z, double yawDegrees = 0, double sx = 1, double sy = 1, double sz = 1, double pitchDegrees = 0)
    {
        var group = new Transform3DGroup();
        if (sx != 1 || sy != 1 || sz != 1) group.Children.Add(new ScaleTransform3D(sx, sy, sz));
        if (pitchDegrees != 0) group.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), pitchDegrees)));
        if (yawDegrees != 0) group.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), yawDegrees)));
        group.Children.Add(new TranslateTransform3D(x, y, z));
        group.Freeze();
        return group;
    }

    public static Model3DGroup Lights()
    {
        var group = new Model3DGroup();
        group.Children.Add(new AmbientLight(Color.FromRgb(112, 112, 120)));
        group.Children.Add(new DirectionalLight(Color.FromRgb(236, 232, 218), new Vector3D(-0.35, -1, -0.55)));
        group.Children.Add(new DirectionalLight(Color.FromRgb(72, 82, 104), new Vector3D(0.6, -0.3, 0.9)));
        return group;
    }

    /// <summary>Draws into a frozen bitmap (court lines, lane wood, crowd, course maps).</summary>
    public static BitmapSource Paint(int width, int height, Action<DrawingContext> draw)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen()) draw(dc);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static readonly MeshGeometry3D UnitSphere = Mesh3D.Sphere(1, 16, 10);
    private static readonly MeshGeometry3D UnitHemisphere = Mesh3D.Sphere(1, 20, 6, fromLatitude: 0);
    private static readonly MeshGeometry3D Trunk = Mesh3D.Cylinder(0.16, 0.11, 1.5, 8);

    public static Model3DGroup Tree(double x, double z, double scale, Color leaves)
    {
        var group = new Model3DGroup();
        group.Children.Add(Model(Trunk, Mat.Matte(Color.FromRgb(120, 82, 52)), At(x, 0, z, 0, scale, scale, scale)));
        group.Children.Add(Model(UnitSphere, Mat.Solid(leaves, 0.08), At(x, 2.1 * scale, z, 0, 1.05 * scale, 0.95 * scale, 1.05 * scale)));
        group.Children.Add(Model(UnitSphere, Mat.Solid(Lighten(leaves, 0.12), 0.08), At(x + 0.25 * scale, 2.75 * scale, z - 0.1 * scale, 0, 0.7 * scale, 0.65 * scale, 0.7 * scale)));
        return group;
    }

    public static GeometryModel3D Hill(double x, double z, double radius, double height, Color color) =>
        Model(UnitHemisphere, Mat.Solid(color, 0.05), At(x, 0, z, 0, radius, height, radius));

    public static Model3DGroup Cloud(double x, double y, double z, double scale)
    {
        var group = new Model3DGroup();
        var white = Mat.Glow(Color.FromRgb(236, 244, 250));
        foreach (var (dx, dy, r) in new[] { (0.0, 0.0, 1.0), (1.2, -0.2, 0.8), (-1.1, -0.25, 0.75), (0.4, 0.45, 0.7) })
            group.Children.Add(Model(UnitSphere, white, At(x + dx * scale, y + dy * scale, z, 0, r * scale, r * scale * 0.8, r * scale * 0.7)));
        return group;
    }

    public static Color Lighten(Color c, double amount) => Color.FromRgb(
        (byte)(c.R + (255 - c.R) * amount),
        (byte)(c.G + (255 - c.G) * amount),
        (byte)(c.B + (255 - c.B) * amount));

    public static Color Darken(Color c, double amount) => Color.FromRgb(
        (byte)(c.R * (1 - amount)),
        (byte)(c.G * (1 - amount)),
        (byte)(c.B * (1 - amount)));

    /// <summary>Tiered stands with a painted crowd of simple colored heads.</summary>
    public static Model3DGroup Stands(double x, double z, double width, double yawDegrees, int seed)
    {
        var crowd = Paint(512, 128, dc =>
        {
            dc.DrawRectangle(Mat.Brush(Color.FromRgb(70, 84, 104)), null, new Rect(0, 0, 512, 128));
            var rng = new Random(seed);
            Color[] shirts = { Color.FromRgb(232, 69, 60), Color.FromRgb(47, 125, 225), Color.FromRgb(54, 178, 74), Color.FromRgb(242, 163, 27), Color.FromRgb(155, 89, 208), Colors.White, Color.FromRgb(31, 181, 176) };
            for (var row = 0; row < 4; row++)
            {
                for (var i = 0; i < 34; i++)
                {
                    var cx = 8 + i * 15 + rng.Next(-3, 4) + (row % 2) * 7;
                    var cy = 22 + row * 28;
                    dc.DrawEllipse(Mat.Brush(shirts[rng.Next(shirts.Length)]), null, new Point(cx, cy + 9), 6.5, 6);
                    dc.DrawEllipse(Mat.Brush(Color.FromRgb(240, 205, 175)), null, new Point(cx, cy), 4.5, 4.5);
                }
            }
        });
        var group = new Model3DGroup();
        group.Children.Add(Model(Mesh3D.Box(width, 2.4, 3.2), Mat.Matte(Color.FromRgb(96, 108, 126)), At(0, 1.2, -1.6)));
        var front = new MeshGeometry3D
        {
            Positions = { new(-width / 2, 0.3, 0.01), new(width / 2, 0.3, 0.01), new(width / 2, 2.35, -2.6), new(-width / 2, 2.35, -2.6) },
            Normals = { new(0, 0.8, 0.6), new(0, 0.8, 0.6), new(0, 0.8, 0.6), new(0, 0.8, 0.6) },
            TextureCoordinates = { new(0, 1), new(Math.Round(width / 8), 1), new(Math.Round(width / 8), 0), new(0, 0) },
            TriangleIndices = { 0, 1, 2, 0, 2, 3 },
        };
        front.Freeze();
        group.Children.Add(Model(front, Mat.Texture(crowd, tile: true)));
        group.Transform = At(x, 0, z, yawDegrees);
        return group;
    }
}
