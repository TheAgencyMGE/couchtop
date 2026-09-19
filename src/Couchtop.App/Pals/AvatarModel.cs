using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Couchtop.Core.Pals;

namespace Couchtop.App.Pals;

/// <summary>A pivot in the rig: rotates its children around its origin, then sits at an offset in its parent.</summary>
public sealed class Joint
{
    private static readonly Vector3D AxisX = new(1, 0, 0);
    private static readonly Vector3D AxisY = new(0, 1, 0);
    private static readonly Vector3D AxisZ = new(0, 0, 1);
    private readonly QuaternionRotation3D _rotation = new();
    private readonly TranslateTransform3D _offset;

    public Joint(Model3DGroup parent, double x, double y, double z)
    {
        _offset = new TranslateTransform3D(x, y, z);
        var transform = new Transform3DGroup();
        transform.Children.Add(new RotateTransform3D(_rotation));
        transform.Children.Add(_offset);
        Group.Transform = transform;
        parent.Children.Add(Group);
    }

    public Model3DGroup Group { get; } = new();

    /// <summary>Pitch about X, then roll about Z, with yaw about Y applied first.</summary>
    public void Rotate(double pitch, double yaw = 0, double roll = 0)
    {
        var q = Quaternion.Identity;
        if (roll != 0) q *= new Quaternion(AxisZ, roll);
        if (pitch != 0) q *= new Quaternion(AxisX, pitch);
        if (yaw != 0) q *= new Quaternion(AxisY, yaw);
        _rotation.Quaternion = q;
    }

    public void Move(double x, double y, double z)
    {
        _offset.OffsetX = x;
        _offset.OffsetY = y;
        _offset.OffsetZ = z;
    }
}

/// <summary>
/// A complete, posable 3D Pal built from a <see cref="PalProfile"/>. Feet rest on y = 0, it faces +Z, and its
/// left side is +X (the viewer's right). About 0.9 units tall at average height.
/// </summary>
public sealed class AvatarModel
{
    private readonly PalProfile _p;
    private readonly Dictionary<FaceState, Material> _faces = new();
    private readonly TranslateTransform3D _position = new();
    private readonly AxisAngleRotation3D _facing = new(new Vector3D(0, 1, 0), 0);
    private readonly ScaleTransform3D _squash = new(1, 1, 1);
    private readonly TranslateTransform3D _lift = new();
    private readonly ScaleTransform3D _shadowScale = new(1, 1, 1);
    private readonly Joint _pelvis, _spine, _neck, _head;
    private readonly Joint _lShoulder, _lElbow, _rShoulder, _rElbow;
    private readonly Joint _lHip, _lKnee, _rHip, _rKnee;
    private readonly List<(Joint Joint, double Side, double Length)> _tails = new();
    private readonly GeometryModel3D _face;
    private readonly double _hipY;
    private FaceState _faceState;
    private double _tailSwing, _tailVelocity, _lastHeadYaw, _lastLift;

    public AvatarModel(PalProfile profile)
    {
        _p = profile.Clone().Normalize();
        Skin = AvatarMaterials.Parse(_p.Skin);
        var hair = AvatarMaterials.Parse(_p.HairColor);
        var top = AvatarMaterials.Parse(_p.TopColor);
        var accent = AvatarMaterials.Parse(_p.TopAccent);
        var bottom = AvatarMaterials.Parse(_p.BottomColor);
        var shoe = AvatarMaterials.Parse(_p.ShoeColor);
        TopColor = top;

        var w = 0.86 + 0.34 * _p.Build;
        var arm = 0.9 + 0.25 * _p.Build;
        HeadRadius = 0.2 * (0.88 + 0.24 * _p.HeadSize);
        var legLength = 0.19 * (0.85 + 0.32 * _p.LegLength);
        const double shoeHeight = 0.05;
        const double torso = 0.25;
        _hipY = shoeHeight + legLength;
        Scale = 0.9 + 0.22 * _p.Height;

        var skinMat = AvatarMaterials.Soft(Skin, 0.12, 22);
        var topMat = AvatarMaterials.Soft(top, 0.16);
        var bottomMat = AvatarMaterials.Soft(bottom, 0.12);
        var dress = _p.TopStyle == "dress";
        var skirt = !dress && _p.BottomStyle == "skirt";
        var longSleeves = _p.TopStyle is "hoodie" or "sweater" or "jacket" or "collared";

        // Ground shadow stays flat on the floor and shrinks as the Pal leaves it.
        var shadowBrush = new RadialGradientBrush(Color.FromArgb(95, 20, 24, 40), Color.FromArgb(0, 20, 24, 40));
        shadowBrush.Freeze();
        var shadow = new GeometryModel3D(AvatarMeshes.DiscZ(0.24, 0.24), AvatarMaterials.Flat(shadowBrush));
        var shadowTransform = new Transform3DGroup();
        shadowTransform.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), -90)));
        shadowTransform.Children.Add(_shadowScale);
        shadowTransform.Children.Add(new TranslateTransform3D(0, 0.002, 0));
        shadow.Transform = shadowTransform;
        Root.Children.Add(shadow);

        var body = new Model3DGroup();
        var bodyTransform = new Transform3DGroup();
        bodyTransform.Children.Add(_squash);
        bodyTransform.Children.Add(_lift);
        body.Transform = bodyTransform;
        Root.Children.Add(body);

        // ------------------------------------------------ pelvis, legs and shoes
        _pelvis = new Joint(body, 0, _hipY, 0);
        var pelvisMat = dress ? topMat : skirt ? bottomMat : bottomMat;
        Add(_pelvis, AvatarMeshes.Ellipsoid(0.1 * w, 0.065, 0.085 * w), pelvisMat, 0, 0.01, 0);

        _lHip = new Joint(_pelvis.Group, 0.052 * w, -0.012, 0);
        _rHip = new Joint(_pelvis.Group, -0.052 * w, -0.012, 0);
        var halfLeg = legLength / 2;
        foreach (var (hip, side) in new[] { (_lHip, 1), (_rHip, -1) })
        {
            var covered = !dress && !skirt;
            var thighMat = covered ? bottomMat : skinMat;
            var shinMat = covered && _p.BottomStyle != "shorts" ? bottomMat : skinMat;
            Add(hip, AvatarMeshes.Limb(halfLeg + 0.018, 0.05 * w, 0.044 * w), thighMat, 0, 0.01, 0);
            if (_p.BottomStyle == "shorts" && covered)
                Add(hip, AvatarMeshes.Limb(halfLeg * 0.72, 0.056 * w, 0.053 * w), bottomMat, 0, 0.012, 0);
            var knee = new Joint(hip.Group, 0, -halfLeg, 0);
            Add(knee, AvatarMeshes.Ellipsoid(0.044 * w, 0.044 * w, 0.044 * w, 14, 10), shinMat, 0, 0, 0);
            Add(knee, AvatarMeshes.Limb(halfLeg - 0.004, 0.044 * w, 0.038 * w), shinMat, 0, 0, 0);
            if (_p.BottomStyle == "joggers" && covered)
                Add(knee, AvatarMeshes.Ring(0.04 * w, 0.011, 20, 8), AvatarMaterials.Soft(AvatarMaterials.Darken(bottom, 0.18), 0.1), 0, -halfLeg + 0.025, 0);
            if (_p.BottomStyle == "jeans" && covered)
                Add(knee, AvatarMeshes.Ring(0.041 * w, 0.008, 20, 8), AvatarMaterials.Soft(AvatarMaterials.Lighten(bottom, 0.18), 0.1), 0, -halfLeg + 0.02, 0);
            BuildShoe(knee.Group, -halfLeg, shoe, w);
            if (side > 0) _lKnee = knee; else _rKnee = knee;
        }
        _lKnee ??= _rKnee!;
        _rKnee ??= _lKnee;

        if (dress) AddSkirt(_pelvis, topMat, AvatarMaterials.Darken(top, 0.3), w, legLength * 0.78, 0.2 * w);
        else if (skirt) AddSkirt(_pelvis, bottomMat, AvatarMaterials.Darken(bottom, 0.3), w, legLength * 0.5, 0.17 * w);

        // ------------------------------------------------ torso, arms and hands
        _spine = new Joint(_pelvis.Group, 0, 0.015, 0);
        var torsoProfile = new (double R, double Y)[]
        {
            (0.0005, -0.018), (0.07 * w, -0.012), (0.1 * w, 0.0), (0.114 * w, 0.03), (0.12 * w, 0.085), (0.117 * w, 0.145),
            (0.11 * w, 0.195), (0.097 * w, 0.228), (0.07 * w, 0.247), (0.04, torso), (0.0005, torso + 0.004),
        };
        var shirt = ShirtTexture.Paint(_p);
        Add(_spine, AvatarMeshes.Lathe(torsoProfile, 40, 1, 0.82), AvatarMaterials.Brushed(shirt, 0.16, 28, Color.FromRgb((byte)(top.R * 0.1), (byte)(top.G * 0.1), (byte)(top.B * 0.1))), 0, 0, 0);
        BuildTopDetails(top, accent, w, torso);
        BuildExtra(w, torso);

        _lShoulder = new Joint(_spine.Group, 0.104 * w, torso - 0.052, 0);
        _rShoulder = new Joint(_spine.Group, -0.104 * w, torso - 0.052, 0);
        var sleeveMat = _p.TopStyle == "tank" ? skinMat : topMat;
        var cuff = AvatarMaterials.Soft(_p.TopStyle is "hoodie" or "sweater" ? AvatarMaterials.Darken(top, 0.15) : accent, 0.12);
        foreach (var (shoulder, side) in new[] { (_lShoulder, 1), (_rShoulder, -1) })
        {
            Add(shoulder, AvatarMeshes.Ellipsoid(0.047 * arm, 0.047 * arm, 0.047 * arm, 16, 10), sleeveMat, 0, 0, 0);
            Add(shoulder, AvatarMeshes.Limb(0.13, 0.043 * arm, 0.038 * arm), longSleeves ? topMat : skinMat, 0, 0, 0);
            if (!longSleeves && _p.TopStyle != "tank")
                Add(shoulder, AvatarMeshes.Limb(0.075, 0.05 * arm, 0.049 * arm), topMat, 0, 0.004, 0);
            var elbow = new Joint(shoulder.Group, 0, -0.125, 0);
            Add(elbow, AvatarMeshes.Ellipsoid(0.038 * arm, 0.038 * arm, 0.038 * arm, 14, 8), longSleeves ? topMat : skinMat, 0, 0, 0);
            Add(elbow, AvatarMeshes.Limb(0.1, 0.038 * arm, 0.035 * arm), longSleeves ? topMat : skinMat, 0, 0, 0);
            if (longSleeves) Add(elbow, AvatarMeshes.Ring(0.036 * arm, 0.011, 20, 8), cuff, 0, -0.09, 0);
            // Mitten hand with a little thumb pointing forward and inward.
            Add(elbow, AvatarMeshes.Ellipsoid(0.042, 0.048, 0.034, 16, 10), skinMat, 0, -0.128, 0.004);
            Add(elbow, AvatarMeshes.Ellipsoid(0.016, 0.022, 0.016, 10, 6), skinMat, -side * 0.018, -0.118, 0.028);
            if (side > 0) _lElbow = elbow; else _rElbow = elbow;
        }
        _lElbow ??= _rElbow!;
        _rElbow ??= _lElbow;

        // ------------------------------------------------ neck and head
        _neck = new Joint(_spine.Group, 0, torso - 0.004, 0);
        Add(_neck, AvatarMeshes.Limb(0.05, 0.034, 0.038), skinMat, 0, 0.042, 0);
        _head = new Joint(_neck.Group, 0, 0.032 + HeadRadius * 0.8, 0);
        var head = new Model3DGroup { Transform = new ScaleTransform3D(HeadRadius, HeadRadius, HeadRadius) };
        _head.Group.Children.Add(head);

        head.Children.Add(Model(AvatarMeshes.Surface((u, v) => HeadPoint(u, v, 1), 40, 26), AvatarMaterials.Soft(Skin, _p.HairStyle == "bald" ? 0.3 : 0.14, 24)));
        foreach (var side in new[] { -1, 1 })
        {
            var ear = HeadPoint(AvatarMeshes.LongitudeToU(side * 86), AvatarMeshes.LatitudeToV(-6), 1);
            head.Children.Add(Model(AvatarMeshes.Ellipsoid(0.15, 0.21, 0.11, 16, 10), skinMat, At(ear.X * 0.97, ear.Y, ear.Z - 0.02, yaw: side * 8)));
            head.Children.Add(Model(AvatarMeshes.Ellipsoid(0.08, 0.12, 0.05, 12, 8), AvatarMaterials.Soft(AvatarMaterials.Darken(Skin, 0.12), 0.05), At(ear.X * 0.97 + side * 0.07, ear.Y, ear.Z - 0.02, yaw: side * 8)));
        }
        BuildNose(head);
        var hairGroup = new Model3DGroup();
        head.Children.Add(hairGroup);
        HairBuilder.Build(this, _p, hairGroup, hair);
        BuildHat(head);
        BuildGlasses(head);
        BuildEarrings(head);

        // The face goes on last so it blends over the skin.
        var faceMesh = AvatarMeshes.Surface((u, v) => HeadPoint(u, v, 1.006), 30, 24,
            AvatarMeshes.LongitudeToU(-AvatarFace.LongitudeSpan), AvatarMeshes.LongitudeToU(AvatarFace.LongitudeSpan),
            AvatarMeshes.LatitudeToV(AvatarFace.LatitudeBottom), AvatarMeshes.LatitudeToV(AvatarFace.LatitudeTop));
        _faceState = new FaceState(PalMood.Happy, false, false, 0, 0);
        _face = new GeometryModel3D(faceMesh, FaceMaterial(_faceState));
        head.Children.Add(_face);

        var rootTransform = new Transform3DGroup();
        rootTransform.Children.Add(new ScaleTransform3D(Scale, Scale, Scale));
        rootTransform.Children.Add(new RotateTransform3D(_facing));
        rootTransform.Children.Add(_position);
        Root.Transform = rootTransform;
        Apply(AvatarPose.Rest, 0);
    }

    public Model3DGroup Root { get; } = new();
    public bool ShadowVisible { get; set; } = true;
    public Color Skin { get; }
    public Color TopColor { get; }
    public double HeadRadius { get; }
    public double Scale { get; }

    /// <summary>Height of the top of the head (without hats) at rest, in world units.</summary>
    public double HeadTop => (_hipY + 0.015 + 0.25 + 0.032 + HeadRadius * 1.85) * Scale;
    public double HeadCenterY => (_hipY + 0.015 + 0.25 + 0.032 + HeadRadius * 0.8) * Scale;

    internal PalProfile Profile => _p;

    // ---------------------------------------------------------------- posing

    public void Place(double x, double z, double facingDegrees)
    {
        _position.OffsetX = x;
        _position.OffsetZ = z;
        _facing.Angle = facingDegrees;
    }

    public void Apply(in AvatarPose pose, double dt)
    {
        _lift.OffsetY = pose.Lift;
        _lift.OffsetX = pose.Shift;
        var squash = pose.Squash <= 0 ? 1 : pose.Squash;
        _squash.ScaleY = squash;
        _squash.ScaleX = _squash.ScaleZ = 1 + (1 - squash) * 0.6;
        // Off the ground (being carried) there is no floor right below the feet, so no shadow either.
        _shadowScale.ScaleX = _shadowScale.ScaleZ = ShadowVisible ? Math.Clamp(1 - pose.Lift * 2.2, 0.5, 1.1) : 0.0001;

        _pelvis.Rotate(0, pose.HipYaw + pose.Turn, pose.HipRoll);
        _spine.Rotate(pose.SpinePitch, pose.SpineYaw, pose.SpineRoll);
        _neck.Rotate(pose.HeadPitch * 0.3, pose.HeadYaw * 0.3, pose.HeadRoll * 0.3);
        _head.Rotate(pose.HeadPitch * 0.7, pose.HeadYaw * 0.7, pose.HeadRoll * 0.7);
        _lShoulder.Rotate(-pose.LArmForward, pose.LArmTwist, pose.LArmOut);
        _rShoulder.Rotate(-pose.RArmForward, pose.RArmTwist, -pose.RArmOut);
        _lElbow.Rotate(-pose.LElbow);
        _rElbow.Rotate(-pose.RElbow);
        _lHip.Rotate(-pose.LLegForward, 0, pose.LLegOut);
        _rHip.Rotate(-pose.RLegForward, 0, -pose.RLegOut);
        _lKnee.Rotate(pose.LKnee);
        _rKnee.Rotate(pose.RKnee);

        // Ponytails and pigtails lag behind head turns and bounces, which sells the motion.
        if (_tails.Count > 0 && dt > 0)
        {
            var headSpeed = (pose.HeadYaw + pose.SpineYaw + pose.Turn - _lastHeadYaw) / dt;
            var liftSpeed = (pose.Lift - _lastLift) / dt;
            _tailVelocity += (-_tailSwing * 90 - _tailVelocity * 9 - headSpeed * 0.9) * dt;
            _tailSwing += _tailVelocity * dt;
            _tailSwing = Math.Clamp(_tailSwing, -35, 35);
            foreach (var (joint, side, _) in _tails)
                joint.Rotate(Math.Clamp(-liftSpeed * 40, -25, 25) + pose.SpinePitch * -0.5, 0, _tailSwing * (side == 0 ? 1 : 0.6) + side * 4);
        }
        _lastHeadYaw = pose.HeadYaw + pose.SpineYaw + pose.Turn;
        _lastLift = pose.Lift;
    }

    public void SetFace(FaceState state)
    {
        if (state == _faceState) return;
        _faceState = state;
        _face.Material = FaceMaterial(state);
    }

    private Material FaceMaterial(FaceState state)
    {
        if (_faces.TryGetValue(state, out var material)) return material;
        if (_faces.Count > 40) _faces.Clear();
        var brush = new ImageBrush(AvatarFace.Paint(_p, state)) { Stretch = Stretch.Fill };
        brush.Freeze();
        var group = new MaterialGroup();
        group.Children.Add(new DiffuseMaterial(brush));
        group.Freeze();
        _faces[state] = group;
        return group;
    }

    /// <summary>Paints every common face ahead of time so the first blink or smile never stutters.</summary>
    public void WarmUpFaces()
    {
        foreach (var mood in Enum.GetValues<PalMood>())
        {
            FaceMaterial(new FaceState(mood, false, false, 0, 0));
            FaceMaterial(new FaceState(mood, true, false, 0, 0));
        }
        FaceMaterial(new FaceState(PalMood.Happy, false, true, 0, 0));
        FaceMaterial(new FaceState(PalMood.Neutral, false, true, 0, 0));
    }

    // ---------------------------------------------------------------- head shape

    /// <summary>A point on the head surface (unit head space) scaled out by <paramref name="k"/>.</summary>
    internal Point3D HeadPoint(double u, double v, double k)
    {
        var d = AvatarMeshes.Direction(u, v);
        double x = d.X, y = d.Y, z = d.Z;
        switch (_p.FaceShape)
        {
            case "oval": x *= 0.93; y *= 1.06; z *= 0.95; break;
            case "wide": x *= 1.08; y *= 0.95; z *= 0.97; break;
            case "soft-square":
                x = Math.Sign(x) * Math.Pow(Math.Abs(x), 0.78) * 0.96;
                z = Math.Sign(z) * Math.Pow(Math.Abs(z), 0.85) * 0.94;
                y *= 0.97;
                break;
            case "heart":
                x *= 1 + 0.07 * y - 0.13 * Math.Max(0, -y);
                z *= 0.96 - 0.04 * Math.Max(0, -y);
                y *= 1.02;
                break;
            default: y *= 0.97; z *= 0.96; break;
        }
        // A softer, rounder chin and a flatter back of the neck read as friendlier.
        if (y < -0.62) y = -0.62 + (y + 0.62) * 0.75;
        if (z < -0.5) z = -0.5 + (z + 0.5) * 0.92;
        return new Point3D(x * k, y * k, z * k);
    }

    internal Point3D HeadPointAt(double longitudeDegrees, double latitudeDegrees, double k) =>
        HeadPoint(AvatarMeshes.LongitudeToU(longitudeDegrees), AvatarMeshes.LatitudeToV(latitudeDegrees), k);

    internal void AddTail(Joint joint, double side, double length) => _tails.Add((joint, side, length));

    // ---------------------------------------------------------------- parts

    private void BuildNose(Model3DGroup head)
    {
        var tip = HeadPointAt(0, -9, 1);
        var mat = AvatarMaterials.Soft(AvatarMaterials.Mix(Skin, Color.FromRgb(240, 150, 140), 0.12), 0.25, 30);
        switch (_p.NoseStyle)
        {
            case "button": head.Children.Add(Model(AvatarMeshes.Ellipsoid(0.1, 0.08, 0.075, 16, 10), mat, At(0, tip.Y, tip.Z - 0.01))); break;
            case "round": head.Children.Add(Model(AvatarMeshes.Ellipsoid(0.13, 0.105, 0.09, 16, 10), mat, At(0, tip.Y - 0.01, tip.Z - 0.01))); break;
            case "pointed": head.Children.Add(Model(AvatarMeshes.Ellipsoid(0.075, 0.1, 0.12, 16, 10), mat, At(0, tip.Y + 0.01, tip.Z, pitch: 12))); break;
        }
    }

    private void BuildShoe(Model3DGroup knee, double ankleY, Color color, double w)
    {
        var upper = AvatarMaterials.Soft(color, 0.3, 36);
        var soleColor = _p.ShoeStyle switch
        {
            "boots" => Color.FromRgb(58, 44, 38),
            "slip-ons" => Color.FromRgb(232, 222, 205),
            _ => Color.FromRgb(250, 250, 248),
        };
        var sole = AvatarMaterials.Soft(soleColor, 0.1);
        var y = ankleY - 0.012;
        knee.Children.Add(Model(AvatarMeshes.RoundedBox(0.092 * w, _p.ShoeStyle == "slip-ons" ? 0.05 : 0.066, 0.15, 0.42), upper, At(0, y + 0.004, 0.026)));
        knee.Children.Add(Model(AvatarMeshes.RoundedBox(0.098 * w, 0.024, 0.158, 0.3), sole, At(0, y - 0.03, 0.027)));
        if (_p.ShoeStyle is "sneakers" or "high-tops")
        {
            // Laces: a bright stripe across the top of the foot.
            knee.Children.Add(Model(AvatarMeshes.RoundedBox(0.05 * w, 0.012, 0.05, 0.4), AvatarMaterials.Soft(Colors.White, 0.1), At(0, y + 0.034, 0.06, pitch: -18)));
        }
        if (_p.ShoeStyle == "high-tops")
            knee.Children.Add(Model(AvatarMeshes.Limb(0.05, 0.048 * w, 0.048 * w), upper, At(0, ankleY + 0.04, 0.004)));
        if (_p.ShoeStyle == "boots")
            knee.Children.Add(Model(AvatarMeshes.Limb(0.075, 0.05 * w, 0.049 * w), upper, At(0, ankleY + 0.07, 0.002)));
    }

    private void AddSkirt(Joint pelvis, Material material, Color inside, double w, double length, double flare)
    {
        var profile = new (double R, double Y)[]
        {
            (flare * 1.02, -length), (flare, -length + 0.008), (flare * 0.82, -length * 0.55), (0.112 * w, -0.01), (0.104 * w, 0.03), (0.098 * w, 0.06),
        };
        var model = Model(AvatarMeshes.Lathe(profile, 40, 1, 0.86), material);
        model.BackMaterial = AvatarMaterials.Soft(inside, 0);
        pelvis.Group.Children.Add(model);
    }

    private void BuildTopDetails(Color top, Color accent, double w, double torso)
    {
        var accentMat = AvatarMaterials.Soft(accent, 0.18);
        var darker = AvatarMaterials.Soft(AvatarMaterials.Darken(top, 0.18), 0.12);
        switch (_p.TopStyle)
        {
            case "hoodie":
            {
                // The hood rests around the back of the neck; drawstrings hang at the front.
                var hood = Model(AvatarMeshes.Ring(0.072 * w, 0.03, 30, 12, 250, 1.05, 0.9), AvatarMaterials.Soft(top, 0.14), At(0, torso - 0.006, -0.012, yaw: 180, pitch: -12));
                _spine.Group.Children.Add(hood);
                foreach (var side in new[] { -1, 1 })
                    _spine.Group.Children.Add(Model(AvatarMeshes.Limb(0.06, 0.006, 0.006), accentMat, At(side * 0.022, torso - 0.018, 0.085 * w * 0.82 + 0.004, pitch: -8)));
                break;
            }
            case "sweater":
                _spine.Group.Children.Add(Model(AvatarMeshes.Ring(0.043, 0.012, 24, 8), darker, At(0, torso - 0.008, 0)));
                break;
            case "jacket":
            case "collared":
            {
                var collar = _p.TopStyle == "jacket" ? AvatarMaterials.Soft(AvatarMaterials.Darken(top, 0.1), 0.16) : accentMat;
                foreach (var side in new[] { -1, 1 })
                    _spine.Group.Children.Add(Model(AvatarMeshes.RoundedBox(0.05, 0.012, 0.045, 0.5), collar, At(side * 0.027, torso - 0.012, 0.036, yaw: side * 28, pitch: 28, roll: -side * 18)));
                break;
            }
        }
    }

    private void BuildExtra(double w, double torso)
    {
        var color = AvatarMaterials.Parse(_p.ExtraColor);
        var mat = AvatarMaterials.Soft(color, 0.2);
        switch (_p.Extra)
        {
            case "scarf":
                _spine.Group.Children.Add(Model(AvatarMeshes.Ring(0.058, 0.026, 28, 12, 360, 1, 0.9), mat, At(0, torso - 0.004, 0)));
                _spine.Group.Children.Add(Model(AvatarMeshes.RoundedBox(0.048, 0.11, 0.02, 0.5), mat, At(0.03, torso - 0.07, 0.088 * w, roll: 6, pitch: -6)));
                break;
            case "backpack":
            {
                _spine.Group.Children.Add(Model(AvatarMeshes.RoundedBox(0.19 * w, 0.2, 0.085, 0.4), mat, At(0, torso * 0.52, -0.13 * w)));
                _spine.Group.Children.Add(Model(AvatarMeshes.RoundedBox(0.16 * w, 0.07, 0.03, 0.5), AvatarMaterials.Soft(AvatarMaterials.Darken(color, 0.15), 0.2), At(0, torso * 0.62, -0.178 * w)));
                var strap = AvatarMaterials.Soft(AvatarMaterials.Darken(color, 0.3), 0.1);
                foreach (var side in new[] { -1, 1 })
                    _spine.Group.Children.Add(Model(AvatarMeshes.RoundedBox(0.018, 0.16, 0.012, 0.6), strap, At(side * 0.062 * w, torso * 0.62, 0.094 * w, pitch: -10)));
                break;
            }
            case "bowtie":
            {
                foreach (var side in new[] { -1, 1 })
                    _spine.Group.Children.Add(Model(AvatarMeshes.Cone(0.024, 0.004, 0.04, 12), mat, At(0, torso - 0.022, 0.066, roll: side * 90)));
                _spine.Group.Children.Add(Model(AvatarMeshes.Ellipsoid(0.013, 0.013, 0.01, 10, 6), mat, At(0, torso - 0.022, 0.07)));
                break;
            }
            case "necklace":
                _spine.Group.Children.Add(Model(AvatarMeshes.Ring(0.062, 0.005, 36, 6), AvatarMaterials.Shiny(Color.FromRgb(242, 196, 80)), At(0, torso - 0.018, 0.012, pitch: 24)));
                _spine.Group.Children.Add(Model(AvatarMeshes.Ellipsoid(0.013, 0.016, 0.008, 10, 6), AvatarMaterials.Glow(color), At(0, torso - 0.05, 0.09 * w)));
                break;
        }
    }

    private void BuildHat(Model3DGroup head)
    {
        var color = AvatarMaterials.Parse(_p.HatColor);
        var mat = AvatarMaterials.Soft(color, 0.22);
        switch (_p.Hat)
        {
            case "cap":
            {
                head.Children.Add(Model(AvatarMeshes.Surface((u, v) => HeadPoint(u, AvatarMeshes.LatitudeToV(10 + 80 * v), 1.13), 36, 14), mat));
                var brim = Model(AvatarMeshes.RoundedBox(1.05, 0.07, 0.72, 0.35), AvatarMaterials.Soft(AvatarMaterials.Darken(color, 0.08), 0.2), At(0, 0.36, 0.86, pitch: 10));
                head.Children.Add(brim);
                head.Children.Add(Model(AvatarMeshes.SmallSphere, AvatarMaterials.Soft(AvatarMaterials.Darken(color, 0.15), 0.3), At(0, 1.12, -0.02, sx: 0.08, sy: 0.05, sz: 0.08)));
                break;
            }
            case "beanie":
            {
                head.Children.Add(Model(AvatarMeshes.Surface((u, v) => HeadPoint(u, AvatarMeshes.LatitudeToV(14 + 76 * v), 1.14 + 0.05 * Math.Pow(v, 3)), 36, 16), mat));
                head.Children.Add(Model(AvatarMeshes.Surface((u, v) => HeadPoint(u, AvatarMeshes.LatitudeToV(4 + 16 * v), 1.18), 36, 4), AvatarMaterials.Soft(AvatarMaterials.Darken(color, 0.14), 0.1)));
                head.Children.Add(Model(AvatarMeshes.UnitSphere, AvatarMaterials.Soft(AvatarMaterials.Lighten(color, 0.55), 0.05), At(0, 1.3, -0.05, sx: 0.24, sy: 0.22, sz: 0.24)));
                break;
            }
            case "bucket":
            {
                head.Children.Add(Model(AvatarMeshes.Lathe(new[] { (1.14, 0.22), (1.14, 0.62), (1.06, 0.92), (0.8, 1.06), (0.0005, 1.1) }, 36), mat));
                var brim = Model(AvatarMeshes.Lathe(new[] { (1.13, 0.27), (1.45, 0.12), (1.55, 0.06), (1.13, 0.2) }, 36), mat);
                brim.BackMaterial = mat;
                head.Children.Add(brim);
                head.Children.Add(Model(AvatarMeshes.Ring(1.15, 0.06, 36, 6), AvatarMaterials.Soft(AvatarMaterials.Parse(_p.TopAccent), 0.1), At(0, 0.32, 0)));
                break;
            }
            case "headphones":
            {
                head.Children.Add(Model(AvatarMeshes.Ring(1.16, 0.07, 30, 10, 180), mat, At(0, 0.05, 0, pitch: -90)));
                var cushion = AvatarMaterials.Soft(Color.FromRgb(46, 48, 58), 0.2);
                foreach (var side in new[] { -1, 1 })
                {
                    head.Children.Add(Model(AvatarMeshes.Lathe(new[] { (0.0005, -0.1), (0.26, -0.1), (0.3, 0.0), (0.26, 0.1), (0.0005, 0.1) }, 24), mat, At(side * 1.1, 0.02, 0, roll: 90)));
                    head.Children.Add(Model(AvatarMeshes.UnitSphere, cushion, At(side * 1.0, 0.02, 0, sx: 0.08, sy: 0.24, sz: 0.24)));
                }
                break;
            }
            case "crown":
            {
                var gold = AvatarMaterials.Shiny(Color.FromRgb(242, 193, 78));
                head.Children.Add(Model(AvatarMeshes.Lathe(new[] { (0.6, 0.72), (0.62, 0.74), (0.64, 0.95), (0.6, 0.97) }, 36), gold));
                for (var k = 0; k < 5; k++)
                {
                    var a = k * 72.0;
                    var x = Math.Sin(a * Math.PI / 180) * 0.61;
                    var z = Math.Cos(a * Math.PI / 180) * 0.61;
                    head.Children.Add(Model(AvatarMeshes.Cone(0.12, 0.02, 0.28, 12), gold, At(x, 0.93, z)));
                    head.Children.Add(Model(AvatarMeshes.SmallSphere, AvatarMaterials.Glow(k % 2 == 0 ? Color.FromRgb(240, 70, 90) : Color.FromRgb(70, 170, 250)), At(x * 1.04, 0.84, z * 1.04, sx: 0.06, sy: 0.06, sz: 0.06)));
                }
                break;
            }
            case "bow":
            {
                foreach (var side in new[] { -1, 1 })
                    head.Children.Add(Model(AvatarMeshes.UnitSphere, mat, At(0.42 + side * 0.2, 0.9, 0.12, roll: side * 30, sx: 0.22, sy: 0.15, sz: 0.09)));
                head.Children.Add(Model(AvatarMeshes.SmallSphere, AvatarMaterials.Soft(AvatarMaterials.Darken(color, 0.12), 0.2), At(0.42, 0.9, 0.16, sx: 0.09, sy: 0.09, sz: 0.07)));
                break;
            }
            case "headband":
                head.Children.Add(Model(AvatarMeshes.Ring(1.0, 0.06, 40, 8, 360, 1.02, 1.04), mat, At(0, 0.52, -0.04, pitch: -18)));
                break;
            case "wizard":
            {
                var brim = Model(AvatarMeshes.Lathe(new[] { (1.0, 0.56), (1.6, 0.48), (1.65, 0.45), (1.0, 0.5) }, 40), mat);
                brim.BackMaterial = mat;
                head.Children.Add(brim);
                head.Children.Add(Model(AvatarMeshes.Lathe(new[] { (1.05, 0.5), (0.95, 0.8), (0.62, 1.3), (0.4, 1.6) }, 32), mat));
                head.Children.Add(Model(AvatarMeshes.Cone(0.4, 0.05, 0.75, 20), mat, At(0, 1.58, -0.02, pitch: -28)));
                head.Children.Add(Model(AvatarMeshes.Ring(1.02, 0.05, 36, 6), AvatarMaterials.Soft(AvatarMaterials.Parse(_p.TopAccent), 0.2), At(0, 0.62, 0)));
                head.Children.Add(Model(AvatarMeshes.Polygon(AvatarMeshes.Star(0.16, 0.07)), AvatarMaterials.Glow(Color.FromRgb(255, 222, 110)), At(0, 1.02, 0.86, pitch: -18)));
                break;
            }
        }
    }

    private void BuildGlasses(Model3DGroup head)
    {
        if (_p.Glasses == "none") return;
        var color = AvatarMaterials.Parse(_p.GlassesColor);
        var frame = AvatarMaterials.Shiny(color);
        var spacing = 19 + _p.EyeSpacing * 9;
        var lat = 1 + (_p.EyeHeight - 0.5) * 10;
        var size = 0.2 * (0.85 + 0.35 * _p.EyeSize);
        var lensColor = _p.Glasses switch
        {
            "shades" => Color.FromArgb(225, 24, 26, 36),
            "star" => Color.FromArgb(200, color.R, color.G, color.B),
            _ => Color.FromArgb(46, 220, 240, 255),
        };
        var lens = AvatarMaterials.Brushed(AvatarMaterials.Brush(lensColor), 0.8, 60);
        var points = new List<Point3D>();
        foreach (var side in new[] { -1, 1 })
        {
            var eye = HeadPointAt(side * spacing, lat, 1);
            var center = new Point3D(eye.X * 1.04, eye.Y, eye.Z + 0.16);
            points.Add(center);
            var yaw = side * spacing * 0.55;
            switch (_p.Glasses)
            {
                case "round":
                    head.Children.Add(Model(AvatarMeshes.Ring(size, 0.03, 30, 8), frame, At(center.X, center.Y, center.Z, yaw: yaw, pitch: 90)));
                    head.Children.Add(Model(AvatarMeshes.DiscZ(size, size), lens, At(center.X, center.Y, center.Z - 0.005, yaw: yaw), doubleSided: true));
                    break;
                case "square":
                case "shades":
                    // Four segments with corners on the diagonals make a square frame.
                    head.Children.Add(Model(AvatarMeshes.Ring(size * 1.3, 0.034, 4, 8, 360, phaseDegrees: 45), frame, At(center.X, center.Y, center.Z, yaw: yaw, pitch: 90, sx: 1.12)));
                    head.Children.Add(Model(AvatarMeshes.RoundedBox(size * 2.05, size * 1.7, 0.01, 0.2), lens, At(center.X, center.Y, center.Z - 0.004, yaw: yaw), doubleSided: true));
                    break;
                case "star":
                    head.Children.Add(Model(AvatarMeshes.Polygon(AvatarMeshes.Star(size * 1.45, size * 0.72)), frame, At(center.X, center.Y, center.Z - 0.01, yaw: yaw), doubleSided: true));
                    head.Children.Add(Model(AvatarMeshes.Polygon(AvatarMeshes.Star(size * 1.2, size * 0.58)), lens, At(center.X, center.Y, center.Z, yaw: yaw), doubleSided: true));
                    break;
            }
            // Arm back to the ear.
            head.Children.Add(Model(AvatarMeshes.RoundedBox(0.035, 0.035, 0.85, 0.5), frame, At(side * (Math.Abs(center.X) + size * 0.9), center.Y + 0.02, center.Z - 0.46, yaw: side * 6)));
        }
        var mid = new Point3D(0, (points[0].Y + points[1].Y) / 2 + 0.03, (points[0].Z + points[1].Z) / 2 + 0.02);
        head.Children.Add(Model(AvatarMeshes.RoundedBox(Math.Abs(points[1].X - points[0].X) - size * 1.9, 0.035, 0.035, 0.5), frame, At(mid.X, mid.Y, mid.Z)));
    }

    private void BuildEarrings(Model3DGroup head)
    {
        if (_p.Earrings == "none") return;
        var metal = AvatarMaterials.Shiny(Color.FromRgb(242, 196, 90));
        foreach (var side in new[] { -1, 1 })
        {
            var ear = HeadPointAt(side * 86, -6, 1);
            var lobe = new Point3D(ear.X * 0.99, ear.Y - 0.19, ear.Z + 0.02);
            if (_p.Earrings == "studs")
                head.Children.Add(Model(AvatarMeshes.SmallSphere, AvatarMaterials.Glow(Color.FromRgb(200, 235, 255)), At(lobe.X + side * 0.03, lobe.Y, lobe.Z, sx: 0.045, sy: 0.045, sz: 0.045)));
            else
                head.Children.Add(Model(AvatarMeshes.Ring(0.1, 0.018, 24, 6), metal, At(lobe.X + side * 0.03, lobe.Y - 0.1, lobe.Z, roll: 90)));
        }
    }

    // ---------------------------------------------------------------- helpers

    private static void Add(Joint joint, MeshGeometry3D mesh, Material material, double x, double y, double z) =>
        joint.Group.Children.Add(Model(mesh, material, x == 0 && y == 0 && z == 0 ? null : new TranslateTransform3D(x, y, z)));

    internal static GeometryModel3D Model(MeshGeometry3D mesh, Material material, Transform3D? transform = null, bool doubleSided = false)
    {
        var model = new GeometryModel3D(mesh, material);
        if (doubleSided) model.BackMaterial = material;
        if (transform is not null) model.Transform = transform;
        return model;
    }

    /// <summary>Scale, roll (Z), pitch (X), yaw (Y), then move. Frozen.</summary>
    internal static Transform3D At(double x, double y, double z, double yaw = 0, double pitch = 0, double roll = 0, double sx = 1, double sy = 1, double sz = 1)
    {
        var group = new Transform3DGroup();
        if (sx != 1 || sy != 1 || sz != 1) group.Children.Add(new ScaleTransform3D(sx, sy, sz));
        if (roll != 0) group.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 0, 1), roll)));
        if (pitch != 0) group.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), pitch)));
        if (yaw != 0) group.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), yaw)));
        group.Children.Add(new TranslateTransform3D(x, y, z));
        group.Freeze();
        return group;
    }
}
