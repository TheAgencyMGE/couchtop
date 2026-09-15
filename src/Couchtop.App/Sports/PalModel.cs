using System.Windows.Media;
using System.Windows.Media.Media3D;
using Couchtop.Core.Sports;

namespace Couchtop.App.Sports;

/// <summary>
/// A Couchtop "Pal": an original, rounded bean-shaped character with a big round head, dot eyes, rosy cheeks,
/// floating hands and little shoes. Built from spheres; every part has its own transform for animation.
/// Faces +Z when its yaw is 0; feet rest on y = 0.
/// </summary>
public sealed class PalModel
{
    public static readonly Color[] Shirts =
    {
        Color.FromRgb(232, 69, 60), Color.FromRgb(47, 125, 225), Color.FromRgb(54, 178, 74), Color.FromRgb(242, 163, 27),
        Color.FromRgb(155, 89, 208), Color.FromRgb(31, 181, 176), Color.FromRgb(240, 106, 164), Color.FromRgb(74, 79, 92),
    };

    public static readonly string[] ShirtNames = { "Red", "Blue", "Green", "Orange", "Purple", "Teal", "Pink", "Charcoal" };

    public static readonly Color[] Skins =
    {
        Color.FromRgb(246, 210, 180), Color.FromRgb(227, 169, 126), Color.FromRgb(185, 122, 84), Color.FromRgb(123, 79, 53),
    };

    public static readonly Color[] Hair =
    {
        Color.FromRgb(59, 42, 32), Color.FromRgb(138, 90, 43), Color.FromRgb(224, 185, 91), Color.FromRgb(30, 30, 36), Color.FromRgb(192, 70, 58),
    };

    private static readonly MeshGeometry3D Ball = Mesh3D.Sphere(1, 22, 14);
    private static readonly MeshGeometry3D Cap = Mesh3D.Sphere(1, 22, 8, fromLatitude: -5);

    private readonly TranslateTransform3D _position = new();
    private readonly AxisAngleRotation3D _yaw = new(new Vector3D(0, 1, 0), 0);
    private readonly TranslateTransform3D _bob = new();
    private readonly AxisAngleRotation3D _pitch = new(new Vector3D(1, 0, 0), 0);
    private readonly AxisAngleRotation3D _roll = new(new Vector3D(0, 0, 1), 0);
    private readonly AxisAngleRotation3D _headTurn = new(new Vector3D(0, 1, 0), 0);
    private readonly ScaleTransform3D _squash = new(1, 1, 1);
    // WPF's X axis points to the viewer's left when looking down +Z, so "right" limbs sit at negative X.
    private readonly TranslateTransform3D _leftHand = new(0.36, 0.52, 0.12);
    private readonly TranslateTransform3D _rightHand = new(-0.36, 0.52, 0.12);
    private readonly TranslateTransform3D _leftFoot = new(0.13, 0.05, 0.02);
    private readonly TranslateTransform3D _rightFoot = new(-0.13, 0.05, 0.02);
    private readonly ScaleTransform3D _eyeBlink = new(1, 1, 1, 0, 1.05, 0.23);
    private readonly AxisAngleRotation3D _itemPitch = new(new Vector3D(1, 0, 0), 0);
    private readonly AxisAngleRotation3D _itemYaw = new(new Vector3D(0, 1, 0), 0);
    private readonly AxisAngleRotation3D _itemRoll = new(new Vector3D(0, 0, 1), 0);
    private readonly SolidColorBrush _flash = new(Color.FromArgb(0, 255, 90, 90));
    private readonly Model3DGroup _item = new();

    /// <param name="alpha">Below 255 draws a see-through Pal (the boxing player seen from behind).</param>
    public PalModel(PalLook look, bool gloves = false, double scale = 1, byte alpha = 255)
    {
        ShirtColor = Shirts[Math.Abs(look.Shirt) % Shirts.Length];
        Color Fade(Color c) => Color.FromArgb(alpha, c.R, c.G, c.B);
        var shirt = Fade(ShirtColor);
        var skin = Fade(Skins[Math.Abs(look.Skin) % Skins.Length]);
        var hair = Fade(Hair[Math.Abs(look.Hair) % Hair.Length]);

        var body = new Model3DGroup();
        body.Children.Add(Part(Flashing(Mat.Solid(shirt, 0.3)), 0, 0.5, 0, 0.28, 0.34, 0.26));
        body.Children.Add(Part(Mat.Solid(Scene.Lighten(shirt, 0.35), 0.2), 0, 0.72, 0.0, 0.2, 0.08, 0.2));

        var head = new Model3DGroup();
        head.Children.Add(Part(Flashing(Mat.Solid(skin, 0.18)), 0, 1.02, 0, 0.25, 0.245, 0.24));
        head.Children.Add(Scene.Model(Cap, Mat.Solid(hair, 0.35), Transform(
            new ScaleTransform3D(0.262, 0.2, 0.26),
            new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), -22)),
            new TranslateTransform3D(0, 1.07, -0.035))));
        var eyes = new Model3DGroup { Transform = _eyeBlink };
        foreach (var side in new[] { -1, 1 })
        {
            eyes.Children.Add(Part(Mat.Solid(Color.FromRgb(34, 30, 40), 0.6, 60), side * 0.085, 1.05, 0.215, 0.034, 0.052, 0.03));
            eyes.Children.Add(Part(Mat.Glow(Colors.White), side * 0.075, 1.07, 0.243, 0.011, 0.013, 0.008));
            head.Children.Add(Part(Mat.Matte(Color.FromRgb(247, 150, 158)), side * 0.155, 0.965, 0.19, 0.045, 0.024, 0.02));
        }
        head.Children.Add(eyes);
        head.Children.Add(Part(Mat.Matte(Color.FromRgb(110, 52, 52)), 0, 0.93, 0.238, 0.03, 0.011, 0.012));
        head.Transform = new RotateTransform3D(_headTurn, 0, 1.0, 0);
        body.Children.Add(head);
        body.Transform = Transform(_squash, new RotateTransform3D(_pitch, 0, 0.2, 0), new RotateTransform3D(_roll, 0, 0.2, 0), _bob);
        Root.Children.Add(body);

        var handMaterial = gloves ? Mat.Solid(Fade(Color.FromRgb(214, 38, 48)), 0.55, 40) : Mat.Solid(skin, 0.18);
        var handSize = gloves ? 0.125 : 0.085;
        Root.Children.Add(Scene.Model(Ball, handMaterial, Transform(new ScaleTransform3D(handSize, handSize, handSize * (gloves ? 1.15 : 1)), _leftHand)));
        var right = new Model3DGroup();
        right.Children.Add(Scene.Model(Ball, handMaterial, new ScaleTransform3D(handSize, handSize, handSize * (gloves ? 1.15 : 1))));
        _item.Transform = Transform(new RotateTransform3D(_itemPitch), new RotateTransform3D(_itemYaw), new RotateTransform3D(_itemRoll));
        right.Children.Add(_item);
        right.Transform = _rightHand;
        Root.Children.Add(right);

        var shoe = Mat.Solid(Fade(Color.FromRgb(58, 62, 74)), 0.35);
        Root.Children.Add(Scene.Model(Ball, shoe, Transform(new ScaleTransform3D(0.1, 0.06, 0.14), _leftFoot)));
        Root.Children.Add(Scene.Model(Ball, shoe, Transform(new ScaleTransform3D(0.1, 0.06, 0.14), _rightFoot)));

        Root.Transform = Transform(new ScaleTransform3D(scale, scale, scale), new RotateTransform3D(_yaw), _position);
    }

    public Model3DGroup Root { get; } = new();
    public Color ShirtColor { get; }

    private Material Flashing(Material baseMaterial)
    {
        var group = new MaterialGroup();
        group.Children.Add(baseMaterial);
        group.Children.Add(new EmissiveMaterial(_flash));
        return group;
    }

    private static GeometryModel3D Part(Material material, double x, double y, double z, double sx, double sy, double sz) =>
        Scene.Model(Ball, material, Scene.At(x, y, z, 0, sx, sy, sz));

    private static Transform3DGroup Transform(params Transform3D[] parts)
    {
        var group = new Transform3DGroup();
        foreach (var part in parts) group.Children.Add(part);
        return group;
    }

    /// <summary>Moves the Pal to a point in sport coordinates (x right, z forward) facing <paramref name="headingDegrees"/> (0 = +z, 90 = +x).</summary>
    public void Place(double x, double y, double z, double headingDegrees)
    {
        _position.OffsetX = -x;
        _position.OffsetY = y;
        _position.OffsetZ = z;
        _yaw.Angle = -headingDegrees;
    }

    public static double Heading(double dx, double dz) => Math.Atan2(dx, dz) * 180 / Math.PI;

    /// <summary>Body pose: bounce height, lean forward (+) / back, lean sideways, and squash (1 = normal).</summary>
    public void Pose(double bob = 0, double pitch = 0, double roll = 0, double squash = 1, double headTurn = 0)
    {
        _bob.OffsetY = bob;
        _pitch.Angle = pitch;
        _roll.Angle = roll;
        _squash.ScaleY = squash;
        _squash.ScaleX = _squash.ScaleZ = 1 + (1 - squash) * 0.5;
        _headTurn.Angle = headTurn;
    }

    /// <summary>Hand positions relative to the Pal (x right, y up, z forward).</summary>
    public void Hands(double lx, double ly, double lz, double rx, double ry, double rz)
    {
        _leftHand.OffsetX = -lx;
        _leftHand.OffsetY = ly;
        _leftHand.OffsetZ = lz;
        _rightHand.OffsetX = -rx;
        _rightHand.OffsetY = ry;
        _rightHand.OffsetZ = rz;
    }

    public void RestHands(double time = 0)
    {
        var sway = Math.Sin(time * 2.2) * 0.02;
        Hands(-0.36, 0.52 + sway, 0.12, 0.36, 0.52 - sway, 0.12);
    }

    public void Feet(double stride, double lift = 0)
    {
        _leftFoot.OffsetZ = 0.02 + stride;
        _rightFoot.OffsetZ = 0.02 - stride;
        _leftFoot.OffsetY = 0.05 + Math.Max(0, lift);
        _rightFoot.OffsetY = 0.05 + Math.Max(0, -lift);
    }

    /// <summary>Running animation: stride, bounce and a little lean, scaled by speed.</summary>
    public void Run(double time, double speed)
    {
        var amount = Math.Min(1, speed / 4);
        var phase = time * 13;
        Feet(Math.Sin(phase) * 0.1 * amount, Math.Cos(phase) * 0.04 * amount);
        _bob.OffsetY = Math.Abs(Math.Sin(phase)) * 0.04 * amount;
        _pitch.Angle = 8 * amount;
    }

    public void Blink(double time)
    {
        var t = time % 3.7;
        _eyeBlink.ScaleY = t < 0.12 ? 0.15 : 1;
    }

    public void SetItem(Model3D? item)
    {
        _item.Children.Clear();
        if (item is not null) _item.Children.Add(item);
    }

    public void ItemAngles(double pitch, double yaw, double roll)
    {
        _itemPitch.Angle = pitch;
        _itemYaw.Angle = yaw;
        _itemRoll.Angle = roll;
    }

    /// <summary>Red hit flash, 0..1.</summary>
    public void Flash(double amount) => _flash.Color = Color.FromArgb((byte)(Math.Clamp(amount, 0, 1) * 170), 255, 90, 90);
}

/// <summary>Sports equipment models. Items attach to a Pal's right hand, pointing along +Y from the grip.</summary>
public static class Props
{
    private static readonly MeshGeometry3D Ball = Mesh3D.Sphere(1, 18, 12);

    public static Model3DGroup Racket(Color frame)
    {
        var group = new Model3DGroup();
        group.Children.Add(Scene.Model(Mesh3D.Cylinder(0.02, 0.018, 0.3, 10), Mat.Solid(Color.FromRgb(40, 40, 48), 0.3), Scene.At(0, -0.08, 0)));
        group.Children.Add(Scene.Model(Mesh3D.Cylinder(0.012, 0.012, 0.12, 8), Mat.Solid(frame, 0.5), Scene.At(0, 0.2, 0)));
        group.Children.Add(Scene.Model(Mesh3D.Torus(0.14, 0.016, 30, 8), Mat.Solid(frame, 0.6, 50), Scene.At(0, 0.46, 0, 0, 1, 1.2, 1), doubleSided: true));
        group.Children.Add(Scene.Model(Ball, Mat.Matte(Color.FromArgb(150, 245, 245, 235)), Scene.At(0, 0.46, 0, 0, 0.135, 0.162, 0.004)));
        return group;
    }

    public static Model3DGroup Bat()
    {
        var group = new Model3DGroup();
        var wood = Mat.Solid(Color.FromRgb(214, 158, 96), 0.35);
        group.Children.Add(Scene.Model(Mesh3D.Cylinder(0.018, 0.043, 0.86, 14), wood, Scene.At(0, -0.1, 0)));
        group.Children.Add(Scene.Model(Ball, wood, Scene.At(0, 0.76, 0, 0, 0.043, 0.03, 0.043)));
        group.Children.Add(Scene.Model(Ball, Mat.Solid(Color.FromRgb(40, 40, 48)), Scene.At(0, -0.11, 0, 0, 0.03, 0.015, 0.03)));
        return group;
    }

    public static Model3DGroup Club(bool putter)
    {
        var group = new Model3DGroup();
        group.Children.Add(Scene.Model(Mesh3D.Cylinder(0.02, 0.018, 0.22, 10), Mat.Solid(Color.FromRgb(36, 36, 42)), Scene.At(0, -0.1, 0)));
        group.Children.Add(Scene.Model(Mesh3D.Cylinder(0.009, 0.007, 0.9, 8), Mat.Solid(Color.FromRgb(190, 196, 204), 0.7, 60), Scene.At(0, -0.95, 0)));
        var head = putter ? Mesh3D.Box(0.12, 0.035, 0.035) : Mesh3D.Box(0.09, 0.05, 0.03);
        group.Children.Add(Scene.Model(head, Mat.Solid(putter ? Color.FromRgb(70, 74, 86) : Color.FromRgb(205, 210, 218), 0.8, 70), Scene.At(0.04, -0.96, 0.01)));
        return group;
    }

    public static Model3DGroup BowlingBall(Color color)
    {
        var group = new Model3DGroup();
        group.Children.Add(Scene.Model(Ball, Mat.Solid(color, 0.8, 80), Scene.At(0, 0, 0, 0, 0.109, 0.109, 0.109)));
        foreach (var (x, y) in new[] { (-0.03, 0.09), (0.03, 0.09), (0.0, 0.04) })
            group.Children.Add(Scene.Model(Ball, Mat.Matte(Color.FromRgb(20, 20, 26)), Scene.At(x, y, 0.07, 0, 0.016, 0.016, 0.01)));
        return group;
    }
}
