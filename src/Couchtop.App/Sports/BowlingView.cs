using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using Couchtop.App.Views;
using Couchtop.Core.Sports;

namespace Couchtop.App.Sports;

/// <summary>Bowling at "Couchtop Lanes": wooden lanes, glowing ceiling lights and a live score sheet.</summary>
public sealed class BowlingView : SportView
{
    private const double BowlerScale = 1.35;
    private static readonly Color[] BallColors = { Color.FromRgb(40, 110, 220), Color.FromRgb(220, 60, 90), Color.FromRgb(60, 170, 90), Color.FromRgb(240, 160, 30) };
    private static readonly MeshGeometry3D PinMesh = Mesh3D.Lathe(new (double, double)[]
    {
        (0, 0), (0.026, 0), (0.033, 0.02), (0.052, 0.08), (0.06, 0.13), (0.052, 0.19), (0.032, 0.245), (0.023, 0.275),
        (0.026, 0.31), (0.03, 0.34), (0.024, 0.366), (0.01, 0.38), (0, 0.381),
    }, 16);
    private static readonly MeshGeometry3D StripeMesh = Mesh3D.Cylinder(0.0265, 0.0245, 0.012, 16, caps: false);

    private readonly (AxisAngleRotation3D Tilt, TranslateTransform3D Move)[] _pins = new (AxisAngleRotation3D, TranslateTransform3D)[10];
    private readonly PalModel[] _bowlers;
    private readonly Mover[] _balls;
    private readonly Mover _aim;
    private readonly Border _powerCard;
    private readonly Rectangle _powerFill;
    private readonly Border _sheet;
    private readonly Grid _sheetGrid = new();
    private readonly TextBlock[,] _marks;
    private readonly TextBlock[,] _totals;
    private readonly TextBlock[] _names;
    private double _cheerAt = -10;
    private double _bowlerX;
    private double _releaseAt = -10;

    public BowlingView(AppHost host, MainWindow window, SportSetup setup, bool demo)
        : base(host, window, setup, demo, Color.FromRgb(28, 24, 58), Color.FromRgb(74, 52, 112))
    {
        BuildHall();
        var players = setup.Mode == SportMode.Training ? 1 : Math.Max(1, setup.Players);
        _bowlers = new PalModel[players];
        _balls = new Mover[players];
        for (var i = 0; i < players; i++)
        {
            _bowlers[i] = new PalModel(SportsSession.LookFor(host, setup, i), scale: BowlerScale);
            Dynamic.Children.Add(_bowlers[i].Root);
            _balls[i] = new Mover(Props.BowlingBall(BallColors[i % BallColors.Length]));
            Dynamic.Children.Add(_balls[i].Model);
        }

        var white = Mat.Solid(Color.FromRgb(250, 250, 248), 0.7, 60);
        var red = Mat.Solid(Color.FromRgb(220, 40, 50), 0.5);
        for (var k = 0; k < 10; k++)
        {
            var model = new Model3DGroup();
            model.Children.Add(Scene.Model(PinMesh, white));
            model.Children.Add(Scene.Model(StripeMesh, red, Scene.At(0, 0.25, 0)));
            model.Children.Add(Scene.Model(StripeMesh, red, Scene.At(0, 0.268, 0)));
            var tilt = new AxisAngleRotation3D(new Vector3D(1, 0, 0), 0);
            var move = new TranslateTransform3D();
            var group = new Transform3DGroup();
            group.Children.Add(new RotateTransform3D(tilt));
            group.Children.Add(move);
            model.Transform = group;
            Dynamic.Children.Add(model);
            _pins[k] = (tilt, move);
        }

        var guide = new Model3DGroup();
        guide.Children.Add(Scene.Model(Mesh3D.Box(0.03, 0.004, 5), Mat.Glow(Color.FromArgb(170, 90, 200, 255)), Scene.At(0, 0, 2.5)));
        guide.Children.Add(Scene.Model(Mesh3D.Box(0.09, 0.004, 0.09), Mat.Glow(Color.FromArgb(220, 120, 220, 255)), Scene.At(0, 0, 5, 45)));
        _aim = new Mover(guide);
        Overlay3D.Children.Add(_aim.Model);

        // Power meter.
        var meter = new Grid { Width = 56, Height = 420 };
        var back = new Rectangle { RadiusX = 28, RadiusY = 28 };
        back.SetResourceReference(Shape.FillProperty, "TrackBrush");
        meter.Children.Add(back);
        _powerFill = new Rectangle { RadiusX = 28, RadiusY = 28, VerticalAlignment = VerticalAlignment.Bottom, Height = 0 };
        _powerFill.Fill = new LinearGradientBrush(Color.FromRgb(90, 200, 90), Color.FromRgb(240, 80, 70), 90);
        meter.Children.Add(_powerFill);
        var meterStack = new StackPanel();
        meterStack.Children.Add(ViewKit.Text("Power", 26, FontWeights.Bold, wrap: false, align: TextAlignment.Center));
        meter.Margin = new Thickness(0, 10, 0, 0);
        meterStack.Children.Add(meter);
        _powerCard = HudCard(meterStack, new Thickness(24, 14, 24, 22));
        _powerCard.HorizontalAlignment = HorizontalAlignment.Right;
        _powerCard.VerticalAlignment = VerticalAlignment.Center;
        _powerCard.Margin = new Thickness(0, 0, 90, 0);
        _powerCard.Visibility = Visibility.Collapsed;
        Hud.Children.Add(_powerCard);

        // Score sheet.
        _marks = new TextBlock[players, 10];
        _totals = new TextBlock[players, 10];
        _names = new TextBlock[players];
        _sheetGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        for (var f = 0; f < 10; f++) _sheetGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(f == 9 ? 130 : 104) });
        for (var p = 0; p < players; p++)
        {
            _sheetGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(78) });
            _names[p] = ViewKit.Text(SportsSession.PlayerName(setup, p), 30, FontWeights.ExtraBold, wrap: false);
            _names[p].VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRow(_names[p], p);
            _sheetGrid.Children.Add(_names[p]);
            for (var f = 0; f < 10; f++)
            {
                var cell = new Border { BorderThickness = new Thickness(2, 0, 0, p < players - 1 ? 2 : 0) };
                cell.SetResourceReference(Border.BorderBrushProperty, "PanelBorderBrush");
                var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                _marks[p, f] = ViewKit.Text("", 22, FontWeights.Bold, "SubtleTextBrush", wrap: false, align: TextAlignment.Center);
                _totals[p, f] = ViewKit.Text("", 28, FontWeights.ExtraBold, wrap: false, align: TextAlignment.Center);
                stack.Children.Add(_marks[p, f]);
                stack.Children.Add(_totals[p, f]);
                cell.Child = stack;
                Grid.SetRow(cell, p);
                Grid.SetColumn(cell, f + 1);
                _sheetGrid.Children.Add(cell);
            }
        }
        _sheet = HudCard(_sheetGrid, new Thickness(28, 8, 20, 8));
        _sheet.HorizontalAlignment = HorizontalAlignment.Center;
        _sheet.VerticalAlignment = VerticalAlignment.Top;
        _sheet.Margin = new Thickness(0, 118, 0, 0);
        if (setup.Mode == SportMode.Training) _sheet.Visibility = Visibility.Collapsed;
        Hud.Children.Add(_sheet);
    }

    private void BuildHall()
    {
        var lane = Scene.Paint(160, 2400, dc =>
        {
            var rng = new Random(3);
            for (var b = 0; b < 39; b++)
            {
                var tone = rng.Next(-10, 10);
                dc.DrawRectangle(Mat.Brush(Color.FromRgb((byte)(222 + tone), (byte)(180 + tone), (byte)(124 + tone))), null, new Rect(b * 160 / 39.0, 0, 160 / 39.0 + 0.5, 2400));
            }
            dc.DrawRectangle(Mat.Brush(Color.FromArgb(60, 120, 70, 30)), null, new Rect(0, 0, 160, 120));
            var arrow = Mat.Brush(Color.FromRgb(60, 90, 170));
            foreach (var board in new[] { 5, 10, 15, 20, 25, 30, 35 })
            {
                var x = board * 160 / 39.0;
                var y = board is 5 or 35 ? 1850 : board is 10 or 30 ? 1810 : board is 15 or 25 ? 1770 : 1730;
                dc.DrawGeometry(arrow, null, new PathGeometry(new[] { new PathFigure(new Point(x, y - 26), new[] { new LineSegment(new Point(x + 4, y), true), new LineSegment(new Point(x - 4, y), true) }, true) }));
            }
            foreach (var board in new[] { 3, 5, 8, 11, 14, 25, 28, 31, 34, 36 }) dc.DrawEllipse(arrow, null, new Point(board * 160 / 39.0, 2140), 2, 2);
            dc.DrawRectangle(Mat.Brush(Color.FromRgb(40, 40, 50)), null, new Rect(0, 2392, 160, 8));
        });

        Static.Children.Add(Scene.Model(Mesh3D.Plane(60, 60), Mat.Matte(Color.FromRgb(38, 70, 96)), At(0, -0.06, -10)));
        var sign = Scene.Paint(1400, 300, dc =>
        {
            dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(46, 30, 90), Color.FromRgb(20, 16, 44), 90), null, new Rect(0, 0, 1400, 300));
            var rng = new Random(8);
            for (var i = 0; i < 40; i++) dc.DrawEllipse(Mat.Brush(Color.FromArgb((byte)rng.Next(80, 220), 255, 255, 255)), null, new Point(rng.Next(1400), rng.Next(300)), 2.5, 2.5);
            var text = new FormattedText("COUCHTOP LANES", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Italic, FontWeights.Black, FontStretches.Normal), 120, Mat.Brush(Color.FromRgb(255, 220, 90)), 1.0);
            dc.DrawText(text, new Point(700 - text.Width / 2, 150 - text.Height / 2));
        });

        foreach (var offset in new[] { 0.0, -1.75, 1.75, -3.5, 3.5 })
        {
            var main = offset == 0;
            Static.Children.Add(Scene.Model(Mesh3D.Plane(1.066, 19.25), Mat.Texture(lane, gloss: 0.35), At(offset, 0, 9.625)));
            Static.Children.Add(Scene.Model(Mesh3D.Plane(1.5, 5.5), Mat.Solid(Color.FromRgb(214, 172, 118), 0.25), At(offset, 0, -2.75)));
            var gutter = Mat.Solid(Color.FromRgb(70, 74, 86), 0.4);
            foreach (var s in new[] { -1, 1 })
            {
                Static.Children.Add(Scene.Model(Mesh3D.Box(0.14, 0.02, 19.25), gutter, At(offset + s * 0.61, -0.04, 9.625)));
                Static.Children.Add(Scene.Model(Mesh3D.Box(0.05, 0.07, 19.25), Mat.Solid(Color.FromRgb(200, 204, 214), 0.5), At(offset + s * 0.705, 0.0, 9.625)));
                Static.Children.Add(Scene.Model(Mesh3D.Box(0.06, 0.75, 2.6), Mat.Solid(Color.FromRgb(60, 44, 90), 0.2), At(offset + s * 0.78, 0.37, 19.1)));
            }
            Static.Children.Add(Scene.Model(Mesh3D.Box(1.6, 0.05, 1.4), Mat.Matte(Color.FromRgb(14, 12, 20)), At(offset, -0.12, 20.1)));
            if (!main)
            {
                var white = Mat.Solid(Color.FromRgb(250, 250, 248), 0.6);
                for (int row = 0, n = 0; row < 4; row++)
                    for (var k = 0; k <= row; k++, n++)
                        Static.Children.Add(Scene.Model(PinMesh, white, At(offset + (k - row / 2.0) * BowlingSim.PinSpacing, 0, BowlingSim.LaneLength + row * BowlingSim.RowDepth)));
            }
        }
        Static.Children.Add(Scene.Model(Mesh3D.Box(5.6, 1.2, 0.3), Mat.Texture(sign), At(0, 1.45, 20.9)));
        Static.Children.Add(Scene.Model(Mesh3D.Box(12, 0.85, 0.3), Mat.Matte(Color.FromRgb(16, 14, 26)), At(0, 0.42, 20.9)));
        var wall = Mat.Solid(Color.FromRgb(70, 52, 110), 0.1);
        foreach (var s in new[] { -1, 1 }) Static.Children.Add(Scene.Model(Mesh3D.Box(0.3, 6, 36), wall, At(s * 5.4, 3, 6)));
        var lamp = Mat.Glow(Color.FromRgb(255, 246, 220));
        for (var z = 0; z <= 18; z += 6)
            foreach (var offset in new[] { -1.75, 0.0, 1.75 })
                Static.Children.Add(Scene.Model(Mesh3D.Box(0.9, 0.08, 0.35), lamp, At(offset, 3.4, z)));
    }

    protected override void UpdateScene(double dt)
    {
        var s = (BowlingSim)Sim;
        var active = Math.Clamp(s.ActivePlayer, 0, _bowlers.Length - 1);
        var human = !Setup.IsCpu(active) && !IsDemo;

        for (var i = 0; i < _bowlers.Length; i++)
        {
            if (i != active)
            {
                _bowlers[i].Place(0, -500, 0, 0);
                _balls[i].Visible = false;
            }
        }

        var bowler = _bowlers[active];
        var ball = _balls[active];
        bowler.Blink(SceneTime);
        switch (s.Phase)
        {
            case BowlingSim.BowlingPhase.Aim:
            case BowlingSim.BowlingPhase.Charge:
            {
                var p = s.Phase == BowlingSim.BowlingPhase.Charge ? s.Power : 0;
                _bowlerX = s.AimX - 0.22 * BowlerScale;
                var z = -1.3 + 0.25 * p;
                bowler.Place(_bowlerX, 0, z, 0);
                var theta = -p * 100 * Math.PI / 180;
                var hy = 0.95 - 0.5 * Math.Cos(theta);
                var hz = 0.5 * Math.Sin(theta) + 0.1;
                bowler.Hands(-0.28, 0.6, 0.22, 0.22, hy, hz);
                bowler.Pose(pitch: 6 + 10 * p, bob: Math.Abs(Math.Sin(SceneTime * 2)) * 0.01);
                bowler.Feet(0.06 * p);
                ball.Visible = true;
                ball.At(_bowlerX + 0.22 * BowlerScale, hy * BowlerScale, z + hz * BowlerScale);
                ball.Spin(new Vector3D(1, 0, 0), 0);
                break;
            }
            default:
            {
                var since = SceneTime - _releaseAt;
                var lunge = s.Phase == BowlingSim.BowlingPhase.Rolling && since < 0.9 ? 1 - since / 0.9 : 0;
                bowler.Place(_bowlerX, 0, -1.0, 0);
                var cheering = SceneTime - _cheerAt < 1.6;
                if (cheering)
                {
                    var jump = Math.Abs(Math.Sin((SceneTime - _cheerAt) * 9)) * 0.06;
                    bowler.Pose(bob: jump);
                    bowler.Hands(-0.32, 1.25, 0.05, 0.32, 1.25, 0.05);
                }
                else
                {
                    bowler.Pose(pitch: 22 * lunge, squash: 1 - 0.12 * lunge);
                    bowler.Hands(-0.4, 0.62, 0.1, 0.22, 0.62 + 0.3 * lunge, 0.2 + 0.4 * lunge);
                }
                bowler.Feet(0.14 * lunge);
                ball.Visible = s.BallVisible;
                ball.At(s.BallX, s.InGutter ? 0.06 : BowlingSim.BallRadius, s.BallZ);
                ball.Spin(new Vector3D(1, 0, 0), s.BallZ / BowlingSim.BallRadius * 180 / Math.PI);
                break;
            }
        }

        for (var k = 0; k < 10; k++)
        {
            var pin = s.Pins[k];
            var (tilt, move) = _pins[k];
            if (pin.Gone)
            {
                move.OffsetY = -500;
                continue;
            }
            move.OffsetX = -pin.X;
            move.OffsetY = 0;
            move.OffsetZ = pin.Z;
            if (pin.Fall > 0)
            {
                var sx = Math.Sin(pin.FallAngle);
                var sz = Math.Cos(pin.FallAngle);
                tilt.Axis = new Vector3D(sz, 0, sx);
                tilt.Angle = pin.Fall * 86;
            }
            else
            {
                tilt.Angle = 0;
            }
        }

        var aiming = human && s.Phase is BowlingSim.BowlingPhase.Aim or BowlingSim.BowlingPhase.Charge;
        _aim.Visible = aiming;
        _aim.At(s.AimX, 0.012, 0.3);
        _aim.Heading(s.AimAngle * 180 / Math.PI);

        _powerCard.Visibility = human && s.Phase == BowlingSim.BowlingPhase.Charge ? Visibility.Visible : Visibility.Collapsed;
        _powerFill.Height = 420 * s.Power;

        switch (s.Phase)
        {
            case BowlingSim.BowlingPhase.Aim:
            case BowlingSim.BowlingPhase.Charge:
                AimCamera(W(s.AimX * 0.5, 1.4, -3.3), W(s.AimX * 0.3, 0.15, 9), dt, 4, 46);
                break;
            case BowlingSim.BowlingPhase.Rolling:
                AimCamera(W(s.BallX * 0.4, 1.05, Math.Min(s.BallZ - 3.4, 14.8)), W(s.BallX * 0.6, 0.12, Math.Min(s.BallZ + 5, 18.9)), dt, 6, 46);
                break;
            default:
                AimCamera(W(0, 1.3, 15.3), W(0, 0.15, 18.7), dt, 3, 46);
                break;
        }

        if (s.IsTraining) return;
        _sheet.Visibility = s.Phase == BowlingSim.BowlingPhase.Rolling ? Visibility.Hidden : Visibility.Visible;
        for (var p = 0; p < _bowlers.Length && p < s.Scorers.Length; p++)
        {
            var scorer = s.Scorers[p];
            var totals = scorer.FrameTotals();
            for (var f = 0; f < 10; f++)
            {
                _marks[p, f].Text = scorer.Marks(f);
                _totals[p, f].Text = totals[f]?.ToString(CultureInfo.InvariantCulture) ?? "";
            }
            _names[p].SetResourceReference(TextBlock.ForegroundProperty, p == active ? "AccentDeepBrush" : "TextBrush");
        }
    }

    protected override void OnEvent(SportEvent e)
    {
        base.OnEvent(e);
        switch (e.Kind)
        {
            case SportEventKind.Roll:
                _releaseAt = SceneTime;
                break;
            case SportEventKind.PinHit:
                Shake(0.02 + 0.04 * e.Strength);
                break;
            case SportEventKind.BowlingStrike:
            case SportEventKind.Spare:
                _cheerAt = SceneTime + 0.2;
                Shake(0.04);
                break;
        }
    }
}
