using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using Couchtop.App.Views;
using Couchtop.Core.Sports;

namespace Couchtop.App.Sports;

/// <summary>Baseball at "Couchtop Park", seen from behind home plate.</summary>
public sealed class BaseballView : SportView
{
    private static readonly (double X, double Z)[] BasePoints = { (19.4, 19.4), (0, 38.8), (-19.4, 19.4) };
    private static readonly (double X, double Z)[] FieldSpots = { (0, 18.44), (0, -1.05), (17, 22), (8, 33), (-9, 32), (-17, 22), (-42, 72), (0, 84), (42, 72) };

    private readonly Model3DGroup[] _fielding = { new(), new() };
    private readonly Model3DGroup[] _batting = { new(), new() };
    private readonly PalModel[][] _fielders = new PalModel[2][];
    private readonly PalModel[] _batters = new PalModel[2];
    private readonly PalModel[][] _runners = new PalModel[2][];
    private readonly Mover _ball;
    private readonly Mover _shadow;
    private readonly Border _meterCard;
    private readonly TranslateTransform _meterMarker = new();
    private readonly TextBlock[] _pitchNames = new TextBlock[3];
    private int _shownBatter = -1;

    public BaseballView(AppHost host, MainWindow window, SportSetup setup, bool demo)
        : base(host, window, setup, demo, Color.FromRgb(104, 184, 240), Color.FromRgb(222, 240, 252))
    {
        BuildPark();
        var mitt = Mat.Solid(Color.FromRgb(150, 92, 50), 0.3);
        for (var team = 0; team < 2; team++)
        {
            var look = SportsSession.LookFor(host, setup, team);
            _fielders[team] = new PalModel[FieldSpots.Length];
            for (var k = 0; k < FieldSpots.Length; k++)
            {
                var pal = new PalModel(look, scale: 1.3);
                if (k > 0) pal.SetItem(Scene.Model(Mesh3D.Sphere(1, 12, 8), mitt, Scene.At(0, 0, 0, 0, 0.1, 0.1, 0.07)));
                _fielders[team][k] = pal;
                _fielding[team].Children.Add(pal.Root);
            }
            _batters[team] = new PalModel(look, scale: 1.3);
            _batters[team].SetItem(Props.Bat());
            _batting[team].Children.Add(_batters[team].Root);
            _runners[team] = new PalModel[3];
            for (var k = 0; k < 3; k++)
            {
                _runners[team][k] = new PalModel(look, scale: 1.3);
                _batting[team].Children.Add(_runners[team][k].Root);
            }
        }

        _ball = new Mover(Scene.Model(Mesh3D.Sphere(1, 14, 10), Mat.Solid(Color.FromRgb(250, 250, 246), 0.5))) { Size = 0.075 };
        _shadow = new Mover(Scene.Model(Mesh3D.Disc(1, 16), Mat.Shadow(0.3))) { Size = 0.1 };
        Dynamic.Children.Add(_ball.Model);
        Dynamic.Children.Add(_shadow.Model);

        // Pitch picker for human pitchers.
        var stack = new StackPanel();
        var names = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        string[] labels = { "◀ Curveball", "Fastball", "Changeup ▶" };
        for (var i = 0; i < 3; i++)
        {
            _pitchNames[i] = ViewKit.Text(labels[i], 30, FontWeights.Bold, "SubtleTextBrush", wrap: false);
            _pitchNames[i].Margin = new Thickness(26, 0, 26, 10);
            names.Children.Add(_pitchNames[i]);
        }
        stack.Children.Add(names);
        var track = new Grid { Width = 620, Height = 34 };
        var back = new Rectangle { RadiusX = 17, RadiusY = 17 };
        back.SetResourceReference(Shape.FillProperty, "TrackBrush");
        track.Children.Add(back);
        var sweet = new Rectangle { Width = 90, RadiusX = 10, RadiusY = 10, Opacity = 0.85 };
        sweet.SetResourceReference(Shape.FillProperty, "SuccessBrush");
        track.Children.Add(sweet);
        var marker = new Rectangle { Width = 16, Height = 52, RadiusX = 8, RadiusY = 8, RenderTransform = _meterMarker, Margin = new Thickness(0, -9, 0, -9) };
        marker.SetResourceReference(Shape.FillProperty, "AccentDeepBrush");
        track.Children.Add(marker);
        stack.Children.Add(track);
        _meterCard = HudCard(stack, new Thickness(40, 18, 40, 26));
        _meterCard.HorizontalAlignment = HorizontalAlignment.Center;
        _meterCard.VerticalAlignment = VerticalAlignment.Bottom;
        _meterCard.Margin = new Thickness(0, 0, 0, 130);
        _meterCard.Visibility = Visibility.Collapsed;
        Hud.Children.Add(_meterCard);
    }

    private void BuildPark()
    {
        var grass = Scene.Paint(64, 64, dc =>
        {
            dc.DrawRectangle(Mat.Brush(Color.FromRgb(86, 170, 74)), null, new Rect(0, 0, 32, 64));
            dc.DrawRectangle(Mat.Brush(Color.FromRgb(98, 184, 84)), null, new Rect(32, 0, 32, 64));
        });
        Static.Children.Add(Scene.Model(Mesh3D.Plane(420, 420, 30, 30), Mat.Texture(grass, tile: true), At(0, 0, 110)));
        var dirt = Mat.Matte(Color.FromRgb(200, 146, 96));
        Static.Children.Add(Scene.Model(Mesh3D.Box(34, 0.01, 34), dirt, At(0, 0.004, 19.4, 45)));
        Static.Children.Add(Scene.Model(Mesh3D.Box(23, 0.01, 23), Mat.Matte(Color.FromRgb(104, 186, 88)), At(0, 0.008, 19.4, 45)));
        Static.Children.Add(Scene.Model(Mesh3D.Disc(3.6, 32), dirt, At(0, 0.012, 0)));
        Static.Children.Add(Scene.Model(Mesh3D.Disc(2.8, 32), dirt, At(0, 0.012, 18.44)));
        Static.Children.Add(Scene.Hill(0, 18.44, 2.6, 0.25, Color.FromRgb(196, 142, 92)));
        var white = Mat.Solid(Colors.White, 0.2);
        foreach (var (x, z) in BasePoints) Static.Children.Add(Scene.Model(Mesh3D.Box(0.4, 0.08, 0.4), white, At(x, 0.04, z, 45)));
        Static.Children.Add(Scene.Model(Mesh3D.Box(0.43, 0.03, 0.43), white, At(0, 0.02, 0.2, 45)));
        Static.Children.Add(Scene.Model(Mesh3D.Box(0.6, 0.05, 0.15), white, At(0, 0.3, 18.44)));
        foreach (var s in new[] { -1, 1 })
            Static.Children.Add(Scene.Model(Mesh3D.Box(0.12, 0.012, 150), white, At(s * 53, 0.014, 53, s * 45)));

        var fence = Mat.Solid(Color.FromRgb(34, 90, 70), 0.15);
        var trim = Mat.Solid(Color.FromRgb(250, 200, 40), 0.3);
        for (var a = -50; a <= 50; a += 5)
        {
            var rad = a * Math.PI / 180;
            var (x, z) = (Math.Sin(rad) * 106, Math.Cos(rad) * 106);
            Static.Children.Add(Scene.Model(Mesh3D.Box(9.6, 3, 0.4), fence, At(x, 1.5, z, a)));
            Static.Children.Add(Scene.Model(Mesh3D.Box(9.6, 0.22, 0.46), trim, At(x, 3.05, z, a)));
        }
        for (var a = -40; a <= 40; a += 20)
        {
            var rad = a * Math.PI / 180;
            var wx = -Math.Sin(rad) * 118;
            var wz = Math.Cos(rad) * 118;
            Static.Children.Add(Scene.Stands(wx, wz, 21, Math.Atan2(-wx, -wz) * 180 / Math.PI, 20 + a));
        }

        var board = Scene.Paint(600, 200, dc =>
        {
            dc.DrawRectangle(Mat.Brush(Color.FromRgb(24, 44, 64)), null, new Rect(0, 0, 600, 200));
            dc.DrawRectangle(null, new Pen(Mat.Brush(Color.FromRgb(250, 200, 40)), 10), new Rect(10, 10, 580, 180));
            var text = new FormattedText("COUCHTOP PARK", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Black, FontStretches.Normal), 64, Brushes.White, 1.0);
            dc.DrawText(text, new Point(300 - text.Width / 2, 100 - text.Height / 2));
        });
        Static.Children.Add(Scene.Model(Mesh3D.Box(30, 10, 1), Mat.Texture(board), At(0, 13, 132)));
        Static.Children.Add(Scene.Model(Mesh3D.Box(1, 8, 1), fence, At(-10, 4, 132.5)));
        Static.Children.Add(Scene.Model(Mesh3D.Box(1, 8, 1), fence, At(10, 4, 132.5)));

        var rng = new Random(9);
        for (var i = 0; i < 30; i++)
        {
            var a = (rng.NextDouble() * 150 - 75) * Math.PI / 180;
            var r = 140 + rng.NextDouble() * 40;
            Static.Children.Add(Scene.Tree(Math.Sin(a) * r, Math.Cos(a) * r, 2.5 + rng.NextDouble() * 1.5, Color.FromRgb(70, (byte)rng.Next(150, 180), 76)));
        }
        Static.Children.Add(Scene.Hill(-120, 260, 110, 40, Color.FromRgb(120, 196, 110)));
        Static.Children.Add(Scene.Hill(110, 280, 130, 50, Color.FromRgb(106, 186, 104)));
        Static.Children.Add(Scene.Cloud(-60, 70, 300, 16));
        Static.Children.Add(Scene.Cloud(80, 85, 320, 20));
    }

    protected override void OnRestart() => _shownBatter = -1;

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    protected override void UpdateScene(double dt)
    {
        var b = (BaseballSim)Sim;
        if (_shownBatter != b.Batter)
        {
            Dynamic.Children.Remove(_fielding[0]);
            Dynamic.Children.Remove(_fielding[1]);
            Dynamic.Children.Remove(_batting[0]);
            Dynamic.Children.Remove(_batting[1]);
            Dynamic.Children.Insert(0, _fielding[b.Pitcher]);
            Dynamic.Children.Insert(0, _batting[b.Batter]);
            _shownBatter = b.Batter;
        }

        AnimateBatter(b, _batters[b.Batter]);
        AnimateFielders(b, _fielders[b.Pitcher]);
        for (var k = 0; k < 3; k++)
        {
            var runner = _runners[b.Batter][k];
            if (!b.Bases[k])
            {
                runner.Place(0, -500, 0, 0);
                continue;
            }
            var (x, z) = BasePoints[k];
            var (nx, nz) = k < 2 ? BasePoints[k + 1] : (0.0, 0.0);
            runner.Place(x + (nx - x) * 0.05, 0, z + (nz - z) * 0.05, PalModel.Heading(nx - x, nz - z));
            runner.Feet(0);
            runner.RestHands(SceneTime + k);
            runner.Pose(bob: Math.Abs(Math.Sin(SceneTime * 3 + k)) * 0.015, pitch: 14);
            runner.Blink(SceneTime + k);
        }

        var ball = b.Ball;
        var hidden = false;
        if (b.Phase == BaseballSim.BaseballPhase.Result && b.LastOutcome == BaseballSim.Outcome.Strike) ball = new V3(0.05, 0.8, -0.75);
        if (b.Phase == BaseballSim.BaseballPhase.Ready && b.PhaseTime < 0.1) hidden = true;
        _ball.Visible = !hidden;
        _ball.At(ball);
        _shadow.At(ball.X, 0.02, ball.Z);
        var distance = (W(ball) - Camera.Position).Length;
        _ball.Size = 0.075 + distance * 0.0022;
        _shadow.Size = 0.1 + distance * 0.002;

        var following = b.Phase == BaseballSim.BaseballPhase.Flight ||
                        (b.Phase == BaseballSim.BaseballPhase.Result && b.LastOutcome is not (BaseballSim.Outcome.Strike or BaseballSim.Outcome.None));
        if (following)
            AimCamera(W(b.Ball.X * 0.35, 4.5 + b.Ball.Y * 0.35, b.Ball.Z * 0.6 - 12), W(b.Ball.X, b.Ball.Y * 0.7, b.Ball.Z), dt, 2.6, 50);
        else
            AimCamera(W(1.7, 3.0, -9.0), W(0.4, 0.8, 18.44), dt, 3, 38);

        var humanPitcher = !(b.IsDerby && b.Pitcher == 1) && !Setup.IsCpu(b.Pitcher) && !IsDemo;
        _meterCard.Visibility = humanPitcher && b.Phase == BaseballSim.BaseballPhase.Ready ? Visibility.Visible : Visibility.Collapsed;
        if (_meterCard.Visibility == Visibility.Visible)
        {
            _meterMarker.X = b.PitchMeter * 300;
            for (var i = 0; i < 3; i++)
            {
                var active = (int)b.Pitch == (i switch { 0 => 1, 1 => 0, _ => 2 });
                _pitchNames[i].SetResourceReference(TextBlock.ForegroundProperty, active ? "AccentDeepBrush" : "SubtleTextBrush");
            }
        }
    }

    private void AnimateBatter(BaseballSim b, PalModel batter)
    {
        batter.Place(-0.85, 0, 0.05, 90);
        batter.Blink(SceneTime);
        var e = 0.0;
        if (b.SwingTime >= 0)
        {
            var k = Math.Clamp(b.SwingTime / 0.32, 0, 1);
            e = 1 - Math.Pow(1 - k, 2.5);
        }
        var hx = Lerp(0.2, -0.28, e);
        var hy = Lerp(0.95, 0.78, e) + (e == 0 ? Math.Sin(SceneTime * 3) * 0.015 : 0);
        var hz = 0.08 + 0.32 * Math.Sin(e * Math.PI * 0.9);
        batter.Hands(hx - 0.03, hy, hz, hx + 0.03, hy + 0.04, hz);
        batter.ItemAngles(Lerp(30, 90, Math.Min(1, e * 1.7)), Lerp(-115, 125, e), 0);
        batter.Pose(pitch: 4, headTurn: 70 - 40 * e);
        batter.Feet(0.06 * e);
    }

    private void AnimateFielders(BaseballSim b, PalModel[] team)
    {
        var pitcher = team[0];
        pitcher.Place(0, 0.25, 18.44, 180);
        pitcher.Blink(SceneTime + 2);
        if (b.Phase == BaseballSim.BaseballPhase.Windup)
        {
            var k = Math.Clamp(b.PhaseTime / 0.75, 0, 1);
            pitcher.Feet(0, Math.Sin(k * Math.PI) * 0.18);
            pitcher.Hands(-0.25, 0.8 + 0.2 * k, 0.25, 0.32, 0.9 + 0.3 * k, -0.3 * k);
            pitcher.Pose(pitch: -10 * k);
        }
        else if (b.Phase == BaseballSim.BaseballPhase.Pitch && b.PhaseTime < 0.35)
        {
            var k = b.PhaseTime / 0.35;
            pitcher.Hands(-0.3, 0.7, -0.1, Lerp(0.3, 0.05, k), Lerp(1.2, 0.7, k), Lerp(-0.3, 0.55, k));
            pitcher.Pose(pitch: 25 * Math.Sin(k * Math.PI));
            pitcher.Feet(0.12 * k);
        }
        else
        {
            pitcher.Feet(0);
            pitcher.Hands(-0.12, 0.66, 0.26, 0.12, 0.66, 0.26);
            pitcher.Pose(bob: Math.Abs(Math.Sin(SceneTime * 2.4)) * 0.01);
        }

        var catcher = team[1];
        catcher.Place(0, 0, -1.05, 0);
        catcher.Hands(-0.1, 0.62, 0.38, 0.12, 0.66, 0.42);
        catcher.Pose(squash: 0.72, pitch: 12);
        catcher.Feet(0);
        catcher.Blink(SceneTime + 4);

        var flying = b.Phase == BaseballSim.BaseballPhase.Flight;
        var chaser = -1;
        if (flying || b.Phase == BaseballSim.BaseballPhase.Result)
        {
            var best = double.MaxValue;
            for (var k = 2; k < FieldSpots.Length; k++)
            {
                var d = Math.Pow(FieldSpots[k].X - b.LandingPoint.X, 2) + Math.Pow(FieldSpots[k].Z - b.LandingPoint.Z, 2);
                if (d < best)
                {
                    best = d;
                    chaser = k;
                }
            }
            if (b.LastOutcome == BaseballSim.Outcome.Strike && !flying) chaser = -1;
        }

        for (var k = 2; k < FieldSpots.Length; k++)
        {
            var pal = team[k];
            var (x, z) = FieldSpots[k];
            if (k == chaser)
            {
                var progress = flying ? Math.Min(1, b.PhaseTime / Math.Max(0.5, b.FlightTime)) : 1;
                var tx = x + (b.LandingPoint.X - x) * 0.85 * progress;
                var tz = z + (b.LandingPoint.Z - z) * 0.85 * progress;
                pal.Place(tx, 0, tz, PalModel.Heading(b.LandingPoint.X - x, b.LandingPoint.Z - z));
                if (flying) pal.Run(SceneTime + k, 6);
                else pal.Feet(0);
                pal.Hands(-0.3, 0.9, 0.3, 0.3, 1.1, 0.3);
            }
            else
            {
                pal.Place(x, 0, z, PalModel.Heading(-x, -z));
                pal.Feet(0);
                pal.Pose(bob: Math.Abs(Math.Sin(SceneTime * 2 + k)) * 0.012, pitch: 12, squash: 0.95);
                pal.Hands(-0.28, 0.55, 0.3, 0.28, 0.55, 0.3);
            }
            pal.Blink(SceneTime + k * 0.7);
        }
    }

    protected override void OnEvent(SportEvent e)
    {
        base.OnEvent(e);
        if (e.Kind == SportEventKind.Contact) Shake(0.03 + 0.05 * e.Strength);
        if (e.Kind == SportEventKind.HomeRun) Shake(0.08);
    }
}
