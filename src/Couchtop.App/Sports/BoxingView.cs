using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using Couchtop.App.Services;
using Couchtop.App.Views;
using Couchtop.Core.Sports;

namespace Couchtop.App.Sports;

/// <summary>Boxing under the lights, seen over your (see-through) shoulder.</summary>
public sealed class BoxingView : SportView
{
    private const double RingTop = 0.62;
    private readonly PalModel[] _pals = new PalModel[2];
    private readonly Mover[] _gloves = new Mover[2];
    private readonly GeometryModel3D[] _mitts = new GeometryModel3D[2];
    private readonly Material _mittLit = Mat.Glow(Color.FromRgb(255, 210, 40));
    private readonly Material _mittDark = Mat.Solid(Color.FromRgb(170, 60, 50), 0.4);
    private readonly Rectangle[] _health = new Rectangle[2];
    private readonly TextBlock[] _knockdowns = new TextBlock[2];
    private readonly Border[] _healthCards = new Border[2];

    public BoxingView(AppHost host, MainWindow window, SportSetup setup, bool demo)
        : base(host, window, setup, demo, Color.FromRgb(16, 18, 30), Color.FromRgb(52, 42, 76))
    {
        BuildArena();
        var training = setup.Mode == SportMode.Training;
        _pals[1] = new PalModel(SportsSession.LookFor(host, setup, 1), gloves: !training, scale: 1.6);
        Dynamic.Children.Add(_pals[1].Root);
        // You see the fight through your own eyes: your body is animated but not drawn, only your gloves are.
        _pals[0] = new PalModel(SportsSession.LookFor(host, setup, 0), gloves: true, scale: 1.6);
        var glove = Mat.Solid(Color.FromRgb(214, 38, 48), 0.55, 40);
        for (var i = 0; i < 2; i++)
        {
            _gloves[i] = new Mover(Scene.Model(Mesh3D.Sphere(1, 20, 14), glove, Scene.At(0, 0, 0, 0, 0.19, 0.19, 0.23)));
            Dynamic.Children.Add(_gloves[i].Model);
        }

        if (training)
        {
            for (var i = 0; i < 2; i++)
            {
                var side = i == 0 ? -1 : 1;
                _mitts[i] = Scene.Model(Mesh3D.Cylinder(0.17, 0.17, 0.07, 20), _mittDark, Scene.At(-side * 0.25, RingTop + 1.58, 0.72, 0, 1, 1, 1, pitchDegrees: 90));
                Dynamic.Children.Add(_mitts[i]);
            }
        }

        for (var i = 0; i < 2; i++)
        {
            var stack = new StackPanel();
            var row = new DockPanel();
            _knockdowns[i] = ViewKit.Text("", 28, FontWeights.Bold, "DangerBrush", wrap: false);
            DockPanel.SetDock(_knockdowns[i], Dock.Right);
            row.Children.Add(_knockdowns[i]);
            row.Children.Add(ViewKit.Text(SportsSession.PlayerName(setup, i), 34, FontWeights.ExtraBold, wrap: false));
            stack.Children.Add(row);
            var track = new Grid { Width = 520, Height = 30, Margin = new Thickness(0, 8, 0, 0) };
            var back = new Rectangle { RadiusX = 15, RadiusY = 15 };
            back.SetResourceReference(Shape.FillProperty, "TrackBrush");
            track.Children.Add(back);
            _health[i] = new Rectangle { RadiusX = 15, RadiusY = 15, HorizontalAlignment = i == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right, Width = 520 };
            track.Children.Add(_health[i]);
            stack.Children.Add(track);
            _healthCards[i] = HudCard(stack, new Thickness(32, 16, 32, 22));
            _healthCards[i].HorizontalAlignment = i == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            _healthCards[i].VerticalAlignment = VerticalAlignment.Top;
            _healthCards[i].Margin = new Thickness(40, 120, 40, 0);
            if (training) _healthCards[i].Visibility = Visibility.Collapsed;
            Hud.Children.Add(_healthCards[i]);
        }
    }

    protected override Model3DGroup CreateLights()
    {
        var group = new Model3DGroup();
        group.Children.Add(new AmbientLight(Color.FromRgb(92, 90, 104)));
        group.Children.Add(new DirectionalLight(Color.FromRgb(250, 244, 230), new Vector3D(0.2, -1, 0.35)));
        group.Children.Add(new DirectionalLight(Color.FromRgb(96, 104, 150), new Vector3D(-0.4, -0.2, -0.8)));
        return group;
    }

    private void BuildArena()
    {
        Static.Children.Add(Scene.Model(Mesh3D.Plane(80, 80), Mat.Matte(Color.FromRgb(34, 34, 44)), At(0, 0, 0)));
        Static.Children.Add(Scene.Model(Mesh3D.Box(6.8, RingTop, 6.8), Mat.Solid(Color.FromRgb(30, 60, 140), 0.2), At(0, RingTop / 2, 0)));
        var canvas = Scene.Paint(512, 512, dc =>
        {
            dc.DrawRectangle(Mat.Brush(Color.FromRgb(222, 230, 240)), null, new Rect(0, 0, 512, 512));
            dc.DrawEllipse(null, new Pen(Mat.Brush(Color.FromRgb(60, 130, 220)), 18), new Point(256, 256), 150, 150);
            var star = Geometry.Parse("M 256,150 L 283,222 L 360,224 L 300,270 L 322,344 L 256,300 L 190,344 L 212,270 L 152,224 L 229,222 Z");
            dc.DrawGeometry(Mat.Brush(Color.FromRgb(240, 90, 90)), null, star);
        });
        Static.Children.Add(Scene.Model(Mesh3D.Plane(6.6, 6.6), Mat.Texture(canvas), At(0, RingTop + 0.005, 0)));

        var postMat = Mat.Solid(Color.FromRgb(200, 204, 212), 0.6);
        foreach (var sx in new[] { -3.2, 3.2 })
            foreach (var sz in new[] { -3.2, 3.2 })
            {
                Static.Children.Add(Scene.Model(Mesh3D.Cylinder(0.07, 0.07, 1.6, 12), postMat, At(sx, RingTop, sz)));
                Static.Children.Add(Scene.Model(Mesh3D.Box(0.22, 1.2, 0.22), Mat.Solid(sx * sz > 0 ? Color.FromRgb(220, 60, 60) : Color.FromRgb(60, 110, 220), 0.3), At(sx, RingTop + 0.95, sz)));
            }
        Color[] ropes = { Colors.White, Color.FromRgb(220, 60, 60), Colors.White };
        for (var r = 0; r < 3; r++)
        {
            var y = RingTop + 0.5 + r * 0.38;
            var mat = Mat.Solid(ropes[r], 0.5);
            Static.Children.Add(Scene.Model(Mesh3D.Box(6.4, 0.05, 0.05), mat, At(0, y, 3.2)));
            Static.Children.Add(Scene.Model(Mesh3D.Box(6.4, 0.05, 0.05), mat, At(0, y, -3.2)));
            Static.Children.Add(Scene.Model(Mesh3D.Box(0.05, 0.05, 6.4), mat, At(3.2, y, 0)));
            Static.Children.Add(Scene.Model(Mesh3D.Box(0.05, 0.05, 6.4), mat, At(-3.2, y, 0)));
        }

        Static.Children.Add(Scene.Stands(0, 10, 30, 180, 31));
        Static.Children.Add(Scene.Stands(-10, 0, 26, 90, 32));
        Static.Children.Add(Scene.Stands(10, 0, 26, -90, 33));
        var lamp = Mat.Glow(Color.FromRgb(255, 250, 220));
        foreach (var sx in new[] { -2.5, 0, 2.5 })
            Static.Children.Add(Scene.Model(Mesh3D.Box(0.8, 0.15, 0.8), lamp, At(sx, 7.5, 1.5)));
        Static.Children.Add(Scene.Model(Mesh3D.Box(8, 0.3, 0.3), Mat.Solid(Color.FromRgb(40, 40, 50)), At(0, 7.7, 1.5)));
    }

    protected override void UpdateScene(double dt)
    {
        var b = (BoxingSim)Sim;
        for (var i = 0; i < 2; i++)
        {
            if (i == 1 && b.IsTraining) AnimateTrainer(b, _pals[1]);
            else AnimateFighter(b.Fighters[i], _pals[i], i);

            if (!b.IsTraining)
            {
                var health = Math.Clamp(b.Fighters[i].Health, 0, 100) / 100;
                _health[i].Width = Math.Max(0, 520 * health);
                _health[i].Fill = Mat.Brush(health > 0.5 ? Color.FromRgb(90, 196, 90) : health > 0.25 ? Color.FromRgb(236, 170, 40) : Color.FromRgb(226, 70, 70));
                _knockdowns[i].Text = new string('●', b.Fighters[i].KnockDowns);
            }
        }

        if (b.IsTraining)
        {
            _mitts[0].Material = b.MittSide == -1 ? _mittLit : _mittDark;
            _mitts[1].Material = b.MittSide == 1 ? _mittLit : _mittDark;
        }

        var me = b.Fighters[0];
        var eyeX = me.Lean * 0.42;
        if (me.Down) AimCamera(W(eyeX, 1.3, -1.4), W(eyeX * 0.5, 2.6, 1.0), dt, 3, 60);
        else AimCamera(W(eyeX, 2.6, -2.7), W(eyeX * 0.4, 1.85, 0.8), dt, 6, 50);
    }

    private void AnimateFighter(BoxingSim.Fighter f, PalModel pal, int i)
    {
        var mirror = i == 0 ? 1 : -1;
        var x = f.Lean * 0.42 * mirror;
        var z = i == 0 ? -0.62 : 0.62;
        pal.Place(x, RingTop, z, i == 0 ? 0 : 180);
        pal.Blink(SceneTime + i);
        pal.Flash(f.Flash);

        double bob = Math.Sin(SceneTime * 5 + i) * 0.015, pitch = 4, roll = -f.Lean * 12, squash = 1;
        double lx = -0.2, ly = 0.92, lz = 0.32, rx = 0.2, ry = 0.92, rz = 0.32;

        if (f.Down)
        {
            pal.Pose(bob: -0.2, pitch: -80);
            pal.Hands(-0.45, 0.4, 0.1, 0.45, 0.4, 0.1);
            pal.Feet(0.1);
            if (i == 0) PlaceGloves(x, z, -0.45, 0.5, 0.3, 0.45, 0.5, 0.3);
            return;
        }

        if (f.Blocking)
        {
            (lx, ly, lz, rx, ry, rz) = (-0.09, 1.06, 0.34, 0.09, 1.06, 0.34);
            pitch = -4;
            squash = 0.97;
        }
        else if (f.PunchTime >= 0)
        {
            var e = f.PunchTime < BoxingSim.ImpactTime ? f.PunchTime / BoxingSim.ImpactTime : Math.Max(0, 1 - (f.PunchTime - BoxingSim.ImpactTime) / (BoxingSim.PunchDuration - BoxingSim.ImpactTime));
            var ext = 1 - (1 - e) * (1 - e);
            if (f.PunchSide < 0)
            {
                lx += (0.06 - lx) * ext * 0.9;
                ly += (1.0 - ly) * ext;
                lz += (0.82 - lz) * ext;
            }
            else
            {
                rx += (-0.06 - rx) * ext * 0.9;
                ry += (1.0 - ry) * ext;
                rz += (0.82 - rz) * ext;
            }
            pitch += 10 * ext;
            roll += f.PunchSide * 7 * ext;
        }
        else if (f.WindUp >= 0)
        {
            var k = Math.Min(1, f.WindUp / 0.35);
            pitch -= 9 * k;
            lz -= 0.16 * k;
            rz -= 0.16 * k;
            bob -= 0.03 * k;
        }

        if (f.Stun > 0) roll += Math.Sin(SceneTime * 22) * 8 * Math.Min(1, f.Stun * 3);
        pal.Pose(bob: bob, pitch: pitch, roll: roll, squash: squash);
        pal.Hands(lx, ly, lz, rx, ry, rz);
        pal.Feet(Math.Sin(SceneTime * 5 + i) * 0.03);
        if (i == 0) PlaceGloves(x, z, lx, ly + bob, lz, rx, ry + bob, rz);
    }

    /// <summary>Player one faces +z, so the Pal's local right is +x in sport coordinates.</summary>
    private void PlaceGloves(double x, double z, double lx, double ly, double lz, double rx, double ry, double rz)
    {
        const double scale = 1.6;
        _gloves[0].At(x + lx * scale, RingTop + ly * scale, z + lz * scale);
        _gloves[1].At(x + rx * scale, RingTop + ry * scale, z + rz * scale);
    }

    private void AnimateTrainer(BoxingSim b, PalModel pal)
    {
        pal.Place(0, RingTop, 1.1, 180);
        pal.Blink(SceneTime);
        var bounce = Math.Sin(SceneTime * 4) * 0.012;
        pal.Pose(bob: bounce, pitch: 6);
        pal.Hands(-0.16, 0.99, 0.28, 0.16, 0.99, 0.28);
        pal.Feet(0);
    }

    protected override void OnEvent(SportEvent e)
    {
        base.OnEvent(e);
        switch (e.Kind)
        {
            case SportEventKind.PunchHit:
                Shake(e.Player == 1 ? 0.09 : 0.04);
                break;
            case SportEventKind.KnockDown:
                Shake(0.15);
                break;
            case SportEventKind.Target:
                Play(SoundEffect.MittPop);
                Shake(0.03);
                break;
        }
    }
}
