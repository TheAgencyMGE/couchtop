using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Couchtop.App.Views;
using Couchtop.Core.Sports;

namespace Couchtop.App.Sports;

/// <summary>Tennis on a blue hard court in a small open-air stadium.</summary>
public sealed class TennisView : SportView
{
    private readonly PalModel?[] _pals = new PalModel?[2];
    private readonly Mover _ball;
    private readonly Mover _shadow;

    public TennisView(AppHost host, MainWindow window, SportSetup setup, bool demo)
        : base(host, window, setup, demo, Color.FromRgb(96, 178, 238), Color.FromRgb(210, 236, 250))
    {
        BuildCourt(setup.Mode == SportMode.Training);
        Color[] frames = { Color.FromRgb(40, 120, 220), Color.FromRgb(230, 80, 60) };
        for (var i = 0; i < 2; i++)
        {
            if (i == 1 && setup.Mode == SportMode.Training) continue;
            var pal = new PalModel(SportsSession.LookFor(host, setup, i), scale: 1.45);
            pal.SetItem(Props.Racket(frames[i]));
            Dynamic.Children.Add(pal.Root);
            _pals[i] = pal;
        }
        _ball = new Mover(Scene.Model(Mesh3D.Sphere(1, 14, 10), Mat.Solid(Color.FromRgb(226, 244, 70), 0.45))) { Size = 0.075 };
        _shadow = new Mover(Scene.Model(Mesh3D.Disc(1, 18), Mat.Shadow(0.3)));
        Dynamic.Children.Add(_ball.Model);
        Dynamic.Children.Add(_shadow.Model);
    }

    private TennisSim T => (TennisSim)Sim;

    private void BuildCourt(bool training)
    {
        const double hw = TennisSim.HalfWidth, hl = TennisSim.HalfLength, doubles = 5.485, ppm = 40;
        double pw = 2 * doubles + 6, pd = 2 * hl + 10;
        int w = (int)(pw * ppm), h = (int)(pd * ppm);
        var court = Scene.Paint(w, h, dc =>
        {
            double X(double x) => (x + pw / 2) * ppm;
            double Y(double z) => (pd / 2 - z) * ppm;
            dc.DrawRectangle(Mat.Brush(Color.FromRgb(64, 152, 122)), null, new Rect(0, 0, w, h));
            dc.DrawRectangle(Mat.Brush(Color.FromRgb(58, 118, 202)), null, new Rect(new Point(X(-doubles), Y(hl)), new Point(X(doubles), Y(-hl))));
            var pen = new Pen(Brushes.White, 0.075 * ppm);
            void Line(double x1, double z1, double x2, double z2) => dc.DrawLine(pen, new Point(X(x1), Y(z1)), new Point(X(x2), Y(z2)));
            foreach (var s in new[] { -1, 1 })
            {
                Line(-doubles, s * hl, doubles, s * hl);
                Line(s * doubles, -hl, s * doubles, hl);
                Line(s * hw, -hl, s * hw, hl);
                Line(-hw, s * TennisSim.ServiceLine, hw, s * TennisSim.ServiceLine);
                Line(0, s * hl, 0, s * (hl - 0.25));
            }
            Line(0, -TennisSim.ServiceLine, 0, TennisSim.ServiceLine);
        });
        Static.Children.Add(Scene.Model(Mesh3D.Plane(220, 240), Mat.Matte(Color.FromRgb(112, 192, 96)), At(0, -0.02, 20)));
        Static.Children.Add(Scene.Model(Mesh3D.Plane(pw, pd), Mat.Texture(court), At(0, 0, 0)));

        // Net, posts and tape.
        var post = Mat.Solid(Color.FromRgb(44, 52, 60), 0.4);
        foreach (var s in new[] { -1, 1 })
            Static.Children.Add(Scene.Model(Mesh3D.Cylinder(0.05, 0.05, 1.07, 10), post, At(s * (doubles + 0.4), 0, 0)));
        Static.Children.Add(Scene.Model(Mesh3D.Box(2 * doubles + 0.8, 0.07, 0.03), Mat.Solid(Colors.White, 0.2), At(0, 0.93, 0)));
        Overlay3D.Children.Add(Scene.Model(Mesh3D.Box(2 * doubles + 0.8, 0.86, 0.012), Mat.Matte(Color.FromArgb(135, 22, 26, 34)), At(0, 0.46, 0), doubleSided: true));

        // Low walls, stands, trees and hills.
        var wall = Mat.Solid(Color.FromRgb(40, 96, 80), 0.1);
        Static.Children.Add(Scene.Model(Mesh3D.Box(26, 1.4, 0.3), wall, At(0, 0.7, hl + 6.2)));
        Static.Children.Add(Scene.Model(Mesh3D.Box(26, 1.4, 0.3), wall, At(0, 0.7, -hl - 6.2)));
        foreach (var s in new[] { -1, 1 })
            Static.Children.Add(Scene.Model(Mesh3D.Box(0.3, 1.4, 2 * hl + 12.4), wall, At(s * 13, 0.7, 0)));
        Static.Children.Add(Scene.Stands(0, hl + 7, 30, 180, 11));
        Static.Children.Add(Scene.Stands(-14.5, 0, 34, 90, 12));
        Static.Children.Add(Scene.Stands(14.5, 0, 34, -90, 13));
        var rng = new Random(5);
        for (var i = 0; i < 26; i++)
        {
            var side = i % 2 == 0 ? -1 : 1;
            Static.Children.Add(Scene.Tree(side * rng.Next(22, 60), rng.Next(-10, 80), 1.4 + rng.NextDouble() * 1.2,
                Color.FromRgb((byte)rng.Next(60, 90), (byte)rng.Next(150, 180), (byte)rng.Next(60, 90))));
        }
        Static.Children.Add(Scene.Hill(-70, 150, 60, 22, Color.FromRgb(120, 196, 110)));
        Static.Children.Add(Scene.Hill(60, 170, 75, 30, Color.FromRgb(104, 184, 104)));
        Static.Children.Add(Scene.Cloud(-40, 40, 160, 9));
        Static.Children.Add(Scene.Cloud(50, 52, 190, 12));

        if (!training) return;
        var machine = Mat.Solid(Color.FromRgb(90, 104, 124), 0.4);
        Static.Children.Add(Scene.Model(Mesh3D.Box(0.9, 0.9, 0.9), machine, At(0, 0.45, hl + 0.8)));
        Static.Children.Add(Scene.Model(Mesh3D.Cylinder(0.3, 0.45, 0.5, 16), Mat.Solid(Color.FromRgb(226, 244, 70), 0.3), At(0, 0.9, hl + 0.8)));
        foreach (var (x, z, r) in TennisSim.Targets)
        {
            Overlay3D.Children.Add(Scene.Model(Mesh3D.Disc(r, 36), Mat.Glow(Color.FromArgb(120, 255, 200, 40)), At(x, 0.015, z)));
            Overlay3D.Children.Add(Scene.Model(Mesh3D.Disc(r * 0.45, 28), Mat.Glow(Color.FromArgb(190, 255, 110, 60)), At(x, 0.02, z)));
        }
    }

    protected override void UpdateScene(double dt)
    {
        var t = T;
        for (var i = 0; i < 2; i++)
        {
            if (_pals[i] is { } pal) AnimatePlayer(pal, t, i);
        }

        _ball.At(t.Ball.X, Math.Max(TennisSim.BallRadius, t.Ball.Y), t.Ball.Z);
        _shadow.At(t.Ball.X, 0.012, t.Ball.Z);
        _shadow.Size = 0.1 * (1 - Math.Min(0.6, t.Ball.Y / 5));

        var near = t.Players[0].Position;
        AimCamera(W(near.X * 0.45, 6.2, -TennisSim.HalfLength - 11), W(near.X * 0.25 + t.Ball.X * 0.12, 0.2, 2.5), dt, 3.2, 46);
    }

    private void AnimatePlayer(PalModel pal, TennisSim t, int i)
    {
        var p = t.Players[i];
        pal.Place(p.Position.X, 0, p.Position.Z, TennisSim.Forward(i) > 0 ? 0 : 180);
        pal.Blink(SceneTime + i * 1.3);
        if (p.Speed > 0.1) pal.Run(SceneTime + i, p.Speed);
        else
        {
            pal.Feet(0);
            pal.Pose(bob: Math.Abs(Math.Sin(SceneTime * 3 + i)) * 0.012, pitch: 4);
        }

        var serving = !t.IsTraining && i == t.Server && t.Phase is TennisSim.TennisPhase.Serve or TennisSim.TennisPhase.Toss;
        if (p.SwingTime >= 0)
        {
            var k = Math.Clamp(p.SwingTime / 0.4, 0, 1);
            var e = 1 - Math.Pow(1 - k, 3);
            var side = p.SwingSide;
            var phi = (20 + 190 * e) * Math.PI / 180;
            const double r = 0.62;
            pal.Hands(-side * 0.28, 0.62, 0.22, side * Math.Sin(phi) * r, 0.7 + 0.12 * Math.Sin(e * Math.PI), -Math.Cos(phi) * r);
            pal.ItemAngles(0, (phi * 180 / Math.PI - 90) * side, 75 * side);
        }
        else if (serving && t.Phase == TennisSim.TennisPhase.Toss)
        {
            pal.Hands(-0.1, 1.25, 0.3, 0.32, 1.2, -0.2);
            pal.ItemAngles(-50, 0, -20);
        }
        else if (serving)
        {
            pal.Hands(-0.12, 0.75, 0.32, 0.3, 0.62, 0.2);
            pal.ItemAngles(20, 0, 30);
        }
        else
        {
            pal.Hands(-0.3, 0.62, 0.25, 0.36, 0.64, 0.3);
            pal.ItemAngles(25, 0, 35);
        }
    }

    protected override void OnEvent(SportEvent e)
    {
        base.OnEvent(e);
        if (e.Kind == SportEventKind.PowerHit) Shake(0.05);
    }
}
