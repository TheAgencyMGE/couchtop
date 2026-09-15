using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using Couchtop.App.Services;
using Couchtop.App.Views;
using Couchtop.Core.Sports;

namespace Couchtop.App.Sports;

/// <summary>Golf on a three-hole meadow course, painted from the same terrain map the simulation uses.</summary>
public sealed class GolfView : SportView
{
    private const double GolferScale = 1.35;
    private readonly Model3DGroup _hole = new();
    private readonly PalModel[] _golfers;
    private readonly bool[] _holdsPutter;
    private readonly bool[] _itemSet;
    private readonly Mover[] _balls;
    private readonly Mover _shadow;
    private readonly Mover _landing;
    private readonly Mover _puttLine;
    private readonly TextBlock _clubText;
    private readonly TextBlock _distanceText;
    private readonly TextBlock _lieText;
    private readonly Border _meterCard;
    private readonly Rectangle _meterFill;
    private readonly Border _scoreCard;
    private readonly TextBlock _scoreText;
    private int _builtHole = -1;
    private int _anchorPlayer = -1;
    private (double X, double Z, double Heading, bool Putter) _anchor;
    private double _swingAt = -10;
    private double _swingFrom;
    private double _beta;
    private Vector _camDir = new(0, 1);

    public GolfView(AppHost host, MainWindow window, SportSetup setup, bool demo)
        : base(host, window, setup, demo, Color.FromRgb(92, 172, 238), Color.FromRgb(214, 238, 252))
    {
        Static.Children.Add(Scene.Model(Mesh3D.Plane(3000, 3000), Mat.Matte(Color.FromRgb(56, 122, 60)), At(0, -0.08, 300)));
        Static.Children.Add(Scene.Hill(-300, 700, 320, 120, Color.FromRgb(110, 176, 120)));
        Static.Children.Add(Scene.Hill(350, 760, 380, 150, Color.FromRgb(96, 164, 112)));
        Static.Children.Add(Scene.Cloud(-120, 160, 700, 40));
        Static.Children.Add(Scene.Cloud(200, 190, 760, 50));
        Static.Children.Add(_hole);

        var players = setup.Mode == SportMode.Training ? 1 : Math.Max(1, setup.Players);
        _golfers = new PalModel[players];
        _holdsPutter = new bool[players];
        _itemSet = new bool[players];
        _balls = new Mover[players];
        var ballMaterial = Mat.Solid(Colors.White, 0.5);
        for (var i = 0; i < players; i++)
        {
            _golfers[i] = new PalModel(SportsSession.LookFor(host, setup, i), scale: GolferScale);
            Dynamic.Children.Add(_golfers[i].Root);
            _balls[i] = new Mover(Scene.Model(Mesh3D.Sphere(1, 12, 8), ballMaterial));
            Dynamic.Children.Add(_balls[i].Model);
        }
        _shadow = new Mover(Scene.Model(Mesh3D.Disc(1, 16), Mat.Shadow(0.3)));
        Dynamic.Children.Add(_shadow.Model);

        var ring = new Model3DGroup();
        ring.Children.Add(Scene.Model(Mesh3D.Disc(1, 40), Mat.Glow(Color.FromArgb(90, 255, 255, 255))));
        ring.Children.Add(Scene.Model(Mesh3D.Disc(0.35, 24), Mat.Glow(Color.FromArgb(170, 255, 220, 60)), Scene.At(0, 0.01, 0)));
        _landing = new Mover(ring);
        Overlay3D.Children.Add(_landing.Model);
        var line = new Model3DGroup();
        line.Children.Add(Scene.Model(Mesh3D.Box(0.035, 0.004, 3), Mat.Glow(Color.FromArgb(170, 255, 255, 255)), Scene.At(0, 0, 1.6)));
        _puttLine = new Mover(line);
        Overlay3D.Children.Add(_puttLine.Model);

        var info = new StackPanel();
        _clubText = ViewKit.Text("", 46, FontWeights.ExtraBold, "AccentDeepBrush", wrap: false);
        _distanceText = ViewKit.Text("", 30, FontWeights.Bold, wrap: false);
        _lieText = ViewKit.Text("", 26, FontWeights.Normal, "SubtleTextBrush", wrap: false);
        info.Children.Add(_clubText);
        info.Children.Add(_distanceText);
        info.Children.Add(_lieText);
        var infoCard = HudCard(info, new Thickness(36, 16, 44, 20));
        infoCard.HorizontalAlignment = HorizontalAlignment.Left;
        infoCard.VerticalAlignment = VerticalAlignment.Bottom;
        infoCard.Margin = new Thickness(40, 0, 0, 40);
        Hud.Children.Add(infoCard);

        var track = new Grid { Width = 760, Height = 44 };
        var back = new Rectangle { RadiusX = 22, RadiusY = 22 };
        back.SetResourceReference(Shape.FillProperty, "TrackBrush");
        track.Children.Add(back);
        track.Children.Add(new Rectangle { Width = 760 - 760 / 1.15, HorizontalAlignment = HorizontalAlignment.Right, Fill = Mat.Brush(Color.FromArgb(90, 230, 70, 70)), RadiusX = 22, RadiusY = 22 });
        _meterFill = new Rectangle { HorizontalAlignment = HorizontalAlignment.Left, RadiusX = 22, RadiusY = 22, Width = 0 };
        _meterFill.SetResourceReference(Shape.FillProperty, "AccentBrush");
        track.Children.Add(_meterFill);
        track.Children.Add(new Rectangle { Width = 6, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(760 / 1.15 - 3, -6, 0, -6), Fill = Brushes.White });
        _meterCard = HudCard(track, new Thickness(26, 18, 26, 18));
        _meterCard.HorizontalAlignment = HorizontalAlignment.Center;
        _meterCard.VerticalAlignment = VerticalAlignment.Bottom;
        _meterCard.Margin = new Thickness(0, 0, 0, 130);
        _meterCard.Visibility = Visibility.Collapsed;
        Hud.Children.Add(_meterCard);

        _scoreText = ViewKit.Text("", 28, FontWeights.Bold, wrap: false);
        _scoreCard = HudCard(_scoreText, new Thickness(30, 14, 30, 16));
        _scoreCard.HorizontalAlignment = HorizontalAlignment.Right;
        _scoreCard.VerticalAlignment = VerticalAlignment.Top;
        _scoreCard.Margin = new Thickness(0, 120, 40, 0);
        if (setup.Mode == SportMode.Training) _scoreCard.Visibility = Visibility.Collapsed;
        Hud.Children.Add(_scoreCard);
    }

    private static int Rank(GolfSim.Terrain t) => t switch
    {
        GolfSim.Terrain.Water => 5,
        GolfSim.Terrain.Sand => 4,
        GolfSim.Terrain.Green => 3,
        GolfSim.Terrain.Tee => 2,
        GolfSim.Terrain.Fairway => 1,
        _ => 0,
    };

    private void BuildHole(GolfSim.Hole hole, int index)
    {
        _hole.Children.Clear();
        double minX = hole.MinX - 30, maxX = hole.MaxX + 30, minZ = -45, maxZ = hole.MaxZ + 30;
        double pw = maxX - minX, pd = maxZ - minZ, cx = (minX + maxX) / 2, cz = (minZ + maxZ) / 2;
        var ppm = 1700 / Math.Max(pw, pd);
        int w = (int)(pw * ppm), h = (int)(pd * ppm);
        var ground = Scene.Paint(w, h, dc =>
        {
            double X(double x) => (cx + pw / 2 - x) * ppm;
            double Y(double z) => (cz + pd / 2 - z) * ppm;
            dc.DrawRectangle(Mat.Brush(Color.FromRgb(56, 122, 60)), null, new Rect(0, 0, w, h));
            dc.DrawRectangle(Mat.Brush(Color.FromRgb(78, 154, 66)), null, new Rect(new Point(X(hole.MaxX), Y(hole.MaxZ)), new Point(X(hole.MinX), Y(-15))));
            foreach (var area in hole.Areas.OrderBy(a => Rank(a.Terrain)))
            {
                var geometry = new EllipseGeometry(new Point(X(area.X), Y(area.Z)), area.RadiusX * ppm, area.RadiusZ * ppm);
                var (fill, stripe, band) = area.Terrain switch
                {
                    GolfSim.Terrain.Fairway => (Color.FromRgb(106, 186, 76), Color.FromRgb(118, 198, 86), 7.0),
                    GolfSim.Terrain.Tee => (Color.FromRgb(120, 200, 88), Color.FromRgb(130, 210, 96), 1.5),
                    GolfSim.Terrain.Green => (Color.FromRgb(124, 214, 96), Color.FromRgb(134, 222, 106), 2.0),
                    GolfSim.Terrain.Sand => (Color.FromRgb(236, 218, 164), Color.FromRgb(236, 218, 164), 0.0),
                    _ => (Color.FromRgb(70, 156, 222), Color.FromRgb(70, 156, 222), 0.0),
                };
                dc.DrawGeometry(Mat.Brush(fill), null, geometry);
                if (band > 0)
                {
                    dc.PushClip(geometry);
                    for (var z = area.Z - area.RadiusZ; z < area.Z + area.RadiusZ; z += band * 2)
                        dc.DrawRectangle(Mat.Brush(stripe), null, new Rect(new Point(0, Y(z + band)), new Point(w, Y(z))));
                    dc.Pop();
                }
                if (area.Terrain == GolfSim.Terrain.Water) dc.DrawGeometry(null, new Pen(Mat.Brush(Color.FromRgb(180, 226, 250)), 0.9 * ppm), geometry);
                if (area.Terrain == GolfSim.Terrain.Sand) dc.DrawGeometry(null, new Pen(Mat.Brush(Color.FromRgb(210, 190, 136)), 0.5 * ppm), geometry);
            }
        });
        _hole.Children.Add(Scene.Model(Mesh3D.Plane(pw, pd), Mat.Texture(ground), At(cx, 0, cz)));

        var rng = new Random(40 + index);
        for (var z = -10.0; z < hole.MaxZ + 20; z += 14 + rng.NextDouble() * 8)
        {
            foreach (var edge in new[] { hole.MinX - 8 - rng.NextDouble() * 16, hole.MaxX + 8 + rng.NextDouble() * 16 })
                _hole.Children.Add(Scene.Tree(-edge, z, 2.4 + rng.NextDouble() * 1.4, Color.FromRgb((byte)rng.Next(50, 80), (byte)rng.Next(130, 170), (byte)rng.Next(60, 90))));
        }
        for (var x = hole.MinX; x < hole.MaxX; x += 12 + rng.NextDouble() * 8)
            _hole.Children.Add(Scene.Tree(-x, hole.MaxZ + 10 + rng.NextDouble() * 14, 2.6 + rng.NextDouble() * 1.4, Color.FromRgb(60, (byte)rng.Next(130, 170), 76)));

        var pin = hole.Pin;
        _hole.Children.Add(Scene.Model(Mesh3D.Disc(GolfSim.CaptureRadius, 20), Mat.Matte(Color.FromRgb(20, 26, 20)), At(pin.X, 0.006, pin.Z)));
        _hole.Children.Add(Scene.Model(Mesh3D.Cylinder(0.03, 0.03, 2.6, 8), Mat.Solid(Colors.White, 0.4), At(pin.X, 0, pin.Z)));
        _hole.Children.Add(Scene.Model(Mesh3D.Box(0.9, 0.55, 0.03), Mat.Solid(Color.FromRgb(236, 60, 60), 0.2), At(pin.X + 0.45, 2.3, pin.Z), doubleSided: true));
        var tee = Mat.Solid(Color.FromRgb(240, 80, 80), 0.4);
        _hole.Children.Add(Scene.Model(Mesh3D.Sphere(0.12, 10, 8), tee, At(hole.Tee.X - 2, 0.1, hole.Tee.Z + 0.5)));
        _hole.Children.Add(Scene.Model(Mesh3D.Sphere(0.12, 10, 8), tee, At(hole.Tee.X + 2, 0.1, hole.Tee.Z + 0.5)));
    }

    protected override void OnRestart()
    {
        _builtHole = -1;
        _anchorPlayer = -1;
    }

    protected override void UpdateScene(double dt)
    {
        var g = (GolfSim)Sim;
        if (_builtHole != g.HoleIndex)
        {
            BuildHole(g.CurrentHole, g.HoleIndex);
            _builtHole = g.HoleIndex;
            SnapCamera();
        }

        var active = Math.Clamp(g.ActivePlayer, 0, _golfers.Length - 1);
        var ball = g.ActiveBall;
        var human = !Setup.IsCpu(active) && !IsDemo;
        var aiming = g.Phase is GolfSim.GolfPhase.Aim or GolfSim.GolfPhase.Backswing;
        var putter = g.CurrentClub.IsPutter;
        var yaw = g.AimYaw;
        var (dx, dz) = (Math.Sin(yaw), Math.Cos(yaw));

        if (aiming || _anchorPlayer != active)
        {
            var heading = yaw * 180 / Math.PI + 90;
            var hr = heading * Math.PI / 180;
            var stand = putter ? 0.75 : 0.95;
            _anchor = (ball.Position.X - Math.Sin(hr) * stand, ball.Position.Z - Math.Cos(hr) * stand, heading, putter);
            _anchorPlayer = active;
        }

        for (var i = 0; i < _golfers.Length; i++)
        {
            var b = g.Balls[i];
            var visible = b.Visible && !b.Holed;
            _balls[i].Visible = visible;
            var distance = (W(b.Position) - Camera.Position).Length;
            var size = Math.Max(0.03, distance * 0.0045);
            _balls[i].Size = size;
            _balls[i].At(b.Position.X, b.Position.Y + size, b.Position.Z);
            if (i != active) _golfers[i].Place(0, -500, 0, 0);
        }
        var shadowDistance = (W(ball.Position) - Camera.Position).Length;
        _shadow.Visible = !ball.Holed;
        _shadow.At(ball.Position.X, 0.03, ball.Position.Z);
        _shadow.Size = Math.Max(0.05, shadowDistance * 0.006) * (1 - Math.Min(0.6, ball.Position.Y / 30));

        AnimateGolfer(g, active);

        var carry = g.CurrentClub.MaxCarry * g.TerrainUnder(ball.Position) switch
        {
            GolfSim.Terrain.Rough => 0.85,
            GolfSim.Terrain.Sand => g.CurrentClub.Name == "Wedge" ? 0.8 : 0.55,
            _ => 1,
        };
        var preview = g.Phase == GolfSim.GolfPhase.Backswing ? Math.Min(g.Meter, 1.15) : 1;
        _landing.Visible = human && aiming && !putter;
        _landing.Size = 2.5 + carry * preview * 0.02;
        _landing.At(ball.Position.X + dx * carry * preview, 0.05, ball.Position.Z + dz * carry * preview);
        _puttLine.Visible = human && aiming && putter;
        _puttLine.At(ball.Position.X, 0.02, ball.Position.Z);
        _puttLine.Heading(yaw * 180 / Math.PI);

        UpdateCamera(g, dt, putter, dx, dz);

        _clubText.Text = g.CurrentClub.Name;
        _distanceText.Text = g.IsTraining ? $"Pin {g.DistanceToPin:0} m" : $"Pin {g.DistanceToPin:0} m  ·  {SportsSession.PlayerName(Setup, active)}";
        _lieText.Text = "Lie: " + g.TerrainUnder(ball.Position);
        var showMeter = human && (g.Phase == GolfSim.GolfPhase.Backswing || SceneTime - _swingAt < 0.9);
        _meterCard.Visibility = showMeter ? Visibility.Visible : Visibility.Collapsed;
        _meterFill.Width = Math.Clamp(g.Meter / 1.15, 0, 1) * 760;

        if (!g.IsTraining)
        {
            var sb = new StringBuilder();
            for (var i = 0; i < _golfers.Length; i++)
            {
                if (i > 0) sb.Append('\n');
                sb.Append($"{SportsSession.PlayerName(Setup, i),-4}  {GolfScorer.ToParText(g.Scorer.ToPar(i)),3}   ({g.Scorer.Total(i)})");
            }
            _scoreText.Text = sb.ToString();
        }
    }

    private void AnimateGolfer(GolfSim g, int active)
    {
        var golfer = _golfers[active];
        var putter = _anchor.Putter;
        if (!_itemSet[active] || _holdsPutter[active] != putter)
        {
            golfer.SetItem(Props.Club(putter));
            _holdsPutter[active] = putter;
            _itemSet[active] = true;
        }

        golfer.Place(_anchor.X, 0, _anchor.Z, _anchor.Heading);
        golfer.Blink(SceneTime);
        var max = putter ? 0.7 : 2.6;
        switch (g.Phase)
        {
            case GolfSim.GolfPhase.Aim:
                _beta = Math.Sin(SceneTime * 2) * 0.04;
                break;
            case GolfSim.GolfPhase.Backswing:
                _beta = Math.Min(1.15, g.Meter) / 1.15 * max;
                break;
            default:
                var t = Math.Clamp((SceneTime - _swingAt) / 0.35, 0, 1);
                var e = 1 - Math.Pow(1 - t, 3);
                _beta = _swingFrom + (-(putter ? 0.6 : 2.3) - _swingFrom) * e;
                break;
        }
        var hx = Math.Sin(_beta) * 0.42;
        var hy = 0.72 + (1 - Math.Cos(_beta)) * 0.34;
        golfer.Hands(hx - 0.03, hy, 0.3, hx + 0.03, hy + 0.03, 0.3);
        golfer.ItemAngles(-30, 0, -_beta * 57.3 * 1.1);
        golfer.Pose(pitch: 14, headTurn: _beta < 0 ? 70 * Math.Min(1, -_beta / 2) : 0);
        golfer.Feet(0);
    }

    private void UpdateCamera(GolfSim g, double dt, bool putter, double dx, double dz)
    {
        var p = g.ActiveBall.Position;
        switch (g.Phase)
        {
            case GolfSim.GolfPhase.Aim:
            case GolfSim.GolfPhase.Backswing:
                _camDir = new Vector(dx, dz);
                if (putter) AimCamera(W(p.X - dx * 3.4, 1.5, p.Z - dz * 3.4), W(p.X + dx * 4, 0, p.Z + dz * 4), dt, 3, 50);
                else AimCamera(W(p.X - dx * 6.5, 2.7, p.Z - dz * 6.5), W(p.X + dx * 40, 0, p.Z + dz * 40), dt, 3, 52);
                break;
            case GolfSim.GolfPhase.Flight:
            case GolfSim.GolfPhase.Rolling:
            {
                var v = new Vector(g.ActiveBall.Velocity.X, g.ActiveBall.Velocity.Z);
                if (v.Length > 0.5)
                {
                    v.Normalize();
                    _camDir += (v - _camDir) * Math.Min(1, dt * 2);
                }
                var back = g.Phase == GolfSim.GolfPhase.Flight ? 12 : 7;
                AimCamera(W(p.X - _camDir.X * back, Math.Max(3, p.Y * 0.6 + 4), p.Z - _camDir.Y * back), W(p), dt, 2.4, 50);
                break;
            }
            default:
                AimCamera(W(p.X - _camDir.X * 8, 4, p.Z - _camDir.Y * 8), W(p), dt, 1.5, 50);
                break;
        }
    }

    protected override void OnEvent(SportEvent e)
    {
        base.OnEvent(e);
        switch (e.Kind)
        {
            case SportEventKind.GolfSwing:
                Play(SoundEffect.GolfSwing);
                _swingAt = SceneTime;
                _swingFrom = _beta;
                break;
            case SportEventKind.Putt:
                _swingAt = SceneTime;
                _swingFrom = _beta;
                break;
            case SportEventKind.Splash:
                Shake(0.04);
                break;
        }
    }
}
