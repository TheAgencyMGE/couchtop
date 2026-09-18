using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Couchtop.Core.Pals;

namespace Couchtop.App.Pals;

public enum AvatarFraming
{
    FullBody,
    Bust,
    Face,
}

/// <summary>
/// A lit 3D stage showing one animated Pal. Frames itself for any size, runs only while it is on screen,
/// and exposes the animator so hosts can make the Pal walk, react and talk.
/// </summary>
public sealed class AvatarView : Border
{
    private readonly Viewport3D _viewport = new() { IsHitTestVisible = false, ClipToBounds = false };
    private readonly PerspectiveCamera _camera = new() { FieldOfView = 24, NearPlaneDistance = 0.05, FarPlaneDistance = 60 };
    private readonly ModelVisual3D _visual = new();
    private readonly Model3DGroup _scene = new();
    private readonly Model3DGroup _extras = new();
    private double _centerY, _height, _targetCenterY, _targetHeight;
    private double _facing, _targetFacing;
    private bool _registered;

    public AvatarView()
    {
        Child = _viewport;
        _viewport.Camera = _camera;
        _visual.Content = _scene;
        _viewport.Children.Add(_visual);
        _scene.Children.Add(Lights());
        _scene.Children.Add(_extras);
        SetFraming(AvatarFraming.FullBody, instant: true);
        IsVisibleChanged += (_, _) => UpdateRegistration();
        Loaded += (_, _) => UpdateRegistration();
        Unloaded += (_, _) => UpdateRegistration();
        SizeChanged += (_, _) => UpdateCamera();
    }

    public AvatarModel? Model { get; private set; }
    public AvatarAnimator? Animator { get; private set; }
    public PalProfile? Profile { get; private set; }

    /// <summary>
    /// When true only the Pal's actual body takes clicks (3D hit testing), so the empty space around it never
    /// blocks tiles underneath.
    /// </summary>
    public bool HitTestBody
    {
        get => _viewport.IsHitTestVisible;
        set
        {
            _viewport.IsHitTestVisible = value;
            if (value) Background = null;
        }
    }

    /// <summary>Most frames per second; a Pal standing around doesn't need the full refresh rate.</summary>
    public int MaxFps { get; set; } = 60;

    /// <summary>Raised every frame before the Pal is posed, with the seconds since the last frame.</summary>
    public event Action<double>? Tick;

    /// <summary>Scenery that belongs with the Pal (a podium, a controller in hand).</summary>
    public Model3DGroup Extras => _extras;

    public double Facing
    {
        get => _targetFacing;
        set => _targetFacing = value;
    }

    public void SnapFacing(double degrees) => _facing = _targetFacing = degrees;

    public void SetProfile(PalProfile profile)
    {
        var old = Animator;
        Profile = profile.Clone();
        var model = new AvatarModel(Profile);
        if (Model is not null) _scene.Children.Remove(Model.Root);
        Model = model;
        _scene.Children.Add(model.Root);
        Animator = new AvatarAnimator(model, Profile.Personality)
        {
            Fidgets = old?.Fidgets ?? true,
            RestingMood = old?.RestingMood ?? PalMood.Happy,
            Base = old?.Base ?? AvatarBase.Idle,
        };
        Animator.Update(0.001);
        UpdateCamera();
    }

    public void SetFraming(AvatarFraming framing, bool instant = false)
    {
        var scale = Model?.Scale ?? 1;
        (_targetCenterY, _targetHeight) = framing switch
        {
            AvatarFraming.Face => ((Model?.HeadCenterY ?? 0.7) + 0.03, 0.64 * scale),
            AvatarFraming.Bust => (0.62 * scale, 0.96 * scale),
            _ => (0.48 * scale, 1.14 * scale),
        };
        if (instant)
        {
            _centerY = _targetCenterY;
            _height = _targetHeight;
            UpdateCamera();
        }
    }

    /// <summary>Frames an exact slice: <paramref name="centerY"/> in the middle, <paramref name="height"/> units tall.</summary>
    public void SetFraming(double centerY, double height, bool instant = false)
    {
        _targetCenterY = centerY;
        _targetHeight = height;
        if (!instant) return;
        _centerY = centerY;
        _height = height;
        UpdateCamera();
    }

    /// <summary>Advances time by hand (snapshot renders and portraits, where no frames are running).</summary>
    public void Step(double seconds, int frames = 1)
    {
        for (var i = 0; i < frames; i++) Advance(seconds / frames);
    }

    /// <summary>Screen position (in this element's coordinates) of a point on the Pal, e.g. above its head.</summary>
    public Point Project(Point3D world)
    {
        var w = Math.Max(1, ActualWidth);
        var h = Math.Max(1, ActualHeight);
        var look = _camera.LookDirection;
        look.Normalize();
        var right = Vector3D.CrossProduct(look, _camera.UpDirection);
        right.Normalize();
        var up = Vector3D.CrossProduct(right, look);
        var rel = world - _camera.Position;
        var depth = Vector3D.DotProduct(rel, look);
        if (depth <= 0.01) return new Point(w / 2, h / 2);
        var halfWidth = Math.Tan(_camera.FieldOfView * Math.PI / 360) * depth;
        var x = Vector3D.DotProduct(rel, right) / halfWidth;
        var y = Vector3D.DotProduct(rel, up) / halfWidth * (w / h);
        return new Point((x + 1) / 2 * w, (1 - y) / 2 * h);
    }

    // ---------------------------------------------------------------- frames

    private void UpdateRegistration()
    {
        var want = IsLoaded && IsVisible;
        if (want == _registered) return;
        _registered = want;
        if (want) FrameClock.Add(this);
        else FrameClock.Remove(this);
    }

    internal void Advance(double dt)
    {
        Tick?.Invoke(dt);
        var k = 1 - Math.Exp(-dt * 6);
        _centerY += (_targetCenterY - _centerY) * k;
        _height += (_targetHeight - _height) * k;
        var turn = ((_targetFacing - _facing + 540) % 360) - 180;
        _facing += turn * (1 - Math.Exp(-dt * 8));
        Model?.Place(0, 0, _facing);
        Animator?.Update(dt);
        UpdateCamera();
    }

    private void UpdateCamera()
    {
        var w = Math.Max(1, ActualWidth);
        var h = Math.Max(1, ActualHeight);
        var tanHalfH = Math.Tan(_camera.FieldOfView * Math.PI / 360);
        var tanHalfV = tanHalfH * h / w;
        var distance = _height / 2 / tanHalfV;
        // A touch above eye level looks down slightly, the flattering angle for a chunky character.
        _camera.Position = new Point3D(0, _centerY + distance * 0.1, distance);
        _camera.LookDirection = new Vector3D(0, -distance * 0.1, -distance);
        _camera.UpDirection = new Vector3D(0, 1, 0);
    }

    private static Model3DGroup Lights()
    {
        var group = new Model3DGroup();
        group.Children.Add(new AmbientLight(Color.FromRgb(118, 116, 126)));
        group.Children.Add(new DirectionalLight(Color.FromRgb(246, 238, 224), new Vector3D(-0.45, -0.7, -0.95)));
        group.Children.Add(new DirectionalLight(Color.FromRgb(96, 112, 142), new Vector3D(0.8, -0.15, -0.5)));
        group.Children.Add(new DirectionalLight(Color.FromRgb(120, 120, 128), new Vector3D(0.1, -0.35, 1)));
        return group;
    }

    /// <summary>One shared per-frame callback for every visible Pal, honoring each view's frame cap.</summary>
    private static class FrameClock
    {
        private static readonly List<AvatarView> Views = new();
        private static readonly Dictionary<AvatarView, TimeSpan> Last = new();
        private static bool _hooked;

        public static void Add(AvatarView view)
        {
            if (Views.Contains(view)) return;
            Views.Add(view);
            if (_hooked) return;
            _hooked = true;
            CompositionTarget.Rendering += OnRendering;
        }

        public static void Remove(AvatarView view)
        {
            Views.Remove(view);
            Last.Remove(view);
            if (Views.Count > 0 || !_hooked) return;
            _hooked = false;
            CompositionTarget.Rendering -= OnRendering;
        }

        private static void OnRendering(object? sender, EventArgs e)
        {
            if (e is not RenderingEventArgs args) return;
            foreach (var view in Views.ToArray())
            {
                if (!Last.TryGetValue(view, out var last))
                {
                    Last[view] = args.RenderingTime;
                    continue;
                }
                var dt = (args.RenderingTime - last).TotalSeconds;
                var minimum = 1.0 / Math.Max(10, view.MaxFps) * 0.9;
                if (dt < minimum) continue;
                Last[view] = args.RenderingTime;
                try
                {
                    view.Advance(dt);
                }
                catch (Exception ex)
                {
                    Core.Diagnostics.Log.Warn("Pal frame failed", ex);
                    Remove(view);
                }
            }
        }
    }
}
