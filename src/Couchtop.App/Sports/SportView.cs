using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using Couchtop.App.Controls;
using Couchtop.App.Services;
using Couchtop.App.Views;
using Couchtop.Core.Sports;

namespace Couchtop.App.Sports;

/// <summary>Shared state for Couchtop Sports: records, Pal looks and view creation.</summary>
public static class SportsSession
{
    private static SportsRecordsService? _records;

    public static SportsRecordsService Records(AppHost host) =>
        _records ??= new SportsRecordsService(System.IO.Path.Combine(host.Paths.DataRoot, "sports.json"));

    public static readonly string[] DifficultyNames = { "Easy", "Normal", "Hard", "Expert" };

    public static SportView CreateView(AppHost host, MainWindow window, SportSetup setup, bool demo = false) => setup.Sport switch
    {
        SportKind.Tennis => new TennisView(host, window, setup, demo),
        SportKind.Baseball => new BaseballView(host, window, setup, demo),
        SportKind.Bowling => new BowlingView(host, window, setup, demo),
        SportKind.Golf => new GolfView(host, window, setup, demo),
        _ => new BoxingView(host, window, setup, demo),
    };

    /// <summary>People use their saved Pal; computer players get a look that contrasts with player one.</summary>
    public static PalLook LookFor(AppHost host, SportSetup setup, int player)
    {
        var records = Records(host).Current;
        if (!setup.IsCpu(player)) return records.Pal(player);
        var first = records.Pal(0);
        return new PalLook
        {
            Shirt = (first.Shirt + 3 + player * 2) % PalModel.Shirts.Length,
            Skin = (first.Skin + player + 1) % PalModel.Skins.Length,
            Hair = (first.Hair + player + 2) % PalModel.Hair.Length,
        };
    }

    public static string PlayerName(SportSetup setup, int player) =>
        setup.Humans == 1 && setup.Players > 1 ? (player == 0 ? "You" : setup.Players > 2 ? $"CPU {player}" : "CPU") : $"P{player + 1}";
}

/// <summary>Moves, turns and hides a model every frame. Positions are in sport coordinates (x right, z forward).</summary>
public sealed class Mover
{
    private readonly ScaleTransform3D _scale = new(1, 1, 1);
    private readonly AxisAngleRotation3D _spin = new(new Vector3D(1, 0, 0), 0);
    private readonly AxisAngleRotation3D _yaw = new(new Vector3D(0, 1, 0), 0);
    private readonly TranslateTransform3D _move = new();
    private double _size = 1;
    private bool _visible = true;

    public Mover(Model3D model)
    {
        Model = new Model3DGroup();
        Model.Children.Add(model);
        var group = new Transform3DGroup();
        group.Children.Add(_scale);
        group.Children.Add(new RotateTransform3D(_spin));
        group.Children.Add(new RotateTransform3D(_yaw));
        group.Children.Add(_move);
        Model.Transform = group;
    }

    public Model3DGroup Model { get; }

    public void At(double x, double y, double z)
    {
        _move.OffsetX = -x;
        _move.OffsetY = y;
        _move.OffsetZ = z;
    }

    public void At(V3 p) => At(p.X, p.Y, p.Z);

    public void Heading(double degrees) => _yaw.Angle = -degrees;

    public void Spin(Vector3D axis, double degrees)
    {
        _spin.Axis = axis;
        _spin.Angle = degrees;
    }

    public double Size
    {
        set
        {
            _size = value;
            Apply();
        }
    }

    public bool Visible
    {
        set
        {
            _visible = value;
            Apply();
        }
    }

    private void Apply()
    {
        var s = _visible ? _size : 1e-4;
        _scale.ScaleX = _scale.ScaleY = _scale.ScaleZ = s;
    }
}

/// <summary>
/// Base screen for one game of Couchtop Sports: a 3D viewport, a theme-styled HUD, the frame loop that feeds input
/// into the simulation, sounds and popups for its events, a pause menu, and the results card that saves records.
/// </summary>
public abstract class SportView : UserControl, IScreenView
{
    private enum Overlay { None, Paused, Results }

    private readonly Stopwatch _clock = new();
    private readonly Random _fx = new();
    private readonly List<(ButtonBase Button, Action Action)> _menu = new();
    private readonly Border _scoreBar;
    private readonly TextBlock _scoreText;
    private readonly Border _promptBar;
    private readonly TextBlock _promptText;
    private readonly TextBlock _popup;
    private readonly ScaleTransform _popupScale = new(1, 1);
    private readonly DropShadowEffect _popupGlow = new() { ShadowDepth = 0, BlurRadius = 22, Opacity = 1 };
    private readonly Grid _overlayHost = new() { Visibility = Visibility.Collapsed };
    private readonly StackPanel _overlayBody = new();
    private readonly Border _intro;
    private readonly TextBlock _introTitle;
    private readonly TextBlock _introMode;
    private readonly TextBlock _introControls;
    private SportsInputHub? _input;
    private Overlay _overlay;
    private bool _attached;
    private bool _leaving;
    private double _lastTime;
    private double _finishedFor = -1;
    private double _introLeft;
    private double _overlayAge;
    private double _popupAge = 99;
    private int _menuIndex;
    private double _shake;
    private Point3D _camPos;
    private Point3D _camLook;
    private bool _camReady;

    protected SportView(AppHost host, MainWindow window, SportSetup setup, bool demo, Color skyTop, Color skyBottom)
    {
        Host = host;
        Window = window;
        Setup = setup;
        IsDemo = demo;
        Sim = SportSim.Create(setup);
        Focusable = true;
        FocusVisualStyle = null;

        var root = new Grid { Background = new LinearGradientBrush(skyTop, skyBottom, 90), ClipToBounds = true };
        Viewport.Camera = Camera;
        var world = new Model3DGroup();
        world.Children.Add(CreateLights());
        world.Children.Add(Static);
        world.Children.Add(Dynamic);
        world.Children.Add(Overlay3D);
        Viewport.Children.Add(new ModelVisual3D { Content = world });
        root.Children.Add(Viewport);
        root.Children.Add(new Viewbox { Stretch = Stretch.Uniform, Child = Hud });

        // Score line (top) and prompt (bottom), styled by the current theme.
        _scoreText = ViewKit.Text("", 34, FontWeights.Bold, wrap: false, align: TextAlignment.Center);
        _scoreBar = HudCard(_scoreText, new Thickness(44, 12, 44, 14));
        _scoreBar.VerticalAlignment = VerticalAlignment.Top;
        _scoreBar.HorizontalAlignment = HorizontalAlignment.Center;
        _scoreBar.Margin = new Thickness(0, 30, 0, 0);
        Hud.Children.Add(_scoreBar);

        _promptText = ViewKit.Text("", 28, FontWeights.SemiBold, "SubtleTextBrush", wrap: false, align: TextAlignment.Center);
        _promptBar = HudCard(_promptText, new Thickness(40, 10, 40, 12));
        _promptBar.VerticalAlignment = VerticalAlignment.Bottom;
        _promptBar.HorizontalAlignment = HorizontalAlignment.Center;
        _promptBar.Margin = new Thickness(0, 0, 0, 34);
        Hud.Children.Add(_promptBar);

        _popup = new TextBlock
        {
            FontSize = 118,
            FontWeight = FontWeights.ExtraBold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, 260),
            RenderTransform = _popupScale,
            RenderTransformOrigin = new Point(0.5, 0.5),
            Effect = _popupGlow,
            Opacity = 0,
            IsHitTestVisible = false,
        };
        _popup.SetResourceReference(TextBlock.FontFamilyProperty, "AppFont");
        Hud.Children.Add(_popup);

        // Intro banner.
        var introStack = new StackPanel();
        _introTitle = ViewKit.Text(SportSim.DisplayName(setup.Sport), 84, FontWeights.ExtraBold, "AccentDeepBrush", wrap: false, align: TextAlignment.Center);
        _introMode = ViewKit.Text("", 36, FontWeights.Bold, wrap: false, align: TextAlignment.Center);
        _introControls = ViewKit.Text("", 26, FontWeights.Normal, "SubtleTextBrush", align: TextAlignment.Center);
        _introControls.Margin = new Thickness(0, 14, 0, 0);
        introStack.Children.Add(_introTitle);
        introStack.Children.Add(_introMode);
        introStack.Children.Add(_introControls);
        _intro = HudCard(introStack, new Thickness(80, 36, 80, 40));
        _intro.HorizontalAlignment = HorizontalAlignment.Center;
        _intro.VerticalAlignment = VerticalAlignment.Center;
        _intro.MaxWidth = 1300;
        _intro.Visibility = Visibility.Collapsed;
        Hud.Children.Add(_intro);

        // Pause / results overlay.
        var scrim = new Rectangle();
        scrim.SetResourceReference(Shape.FillProperty, "ScrimBrush");
        _overlayHost.Children.Add(scrim);
        var panel = new Border { Child = _overlayBody, Width = 1080, Padding = new Thickness(70, 52, 70, 44), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        panel.SetResourceReference(FrameworkElement.StyleProperty, "AppPanel");
        _overlayHost.Children.Add(panel);
        Hud.Children.Add(_overlayHost);

        Content = root;
    }

    protected AppHost Host { get; }
    protected MainWindow Window { get; }
    protected SportSetup Setup { get; }
    protected bool IsDemo { get; }
    protected SportSim Sim { get; private set; }
    protected double SceneTime { get; private set; }
    protected Viewport3D Viewport { get; } = new() { ClipToBounds = true };
    protected PerspectiveCamera Camera { get; } = new() { FieldOfView = 50, NearPlaneDistance = 0.05, FarPlaneDistance = 3000 };

    /// <summary>Scenery that never moves.</summary>
    protected Model3DGroup Static { get; } = new();

    /// <summary>Players, balls and everything that moves.</summary>
    protected Model3DGroup Dynamic { get; } = new();

    /// <summary>Drawn last: see-through things (aim guides, the boxing player).</summary>
    protected Model3DGroup Overlay3D { get; } = new();

    /// <summary>1920x1080 HUD layer that scales with the window.</summary>
    protected Grid Hud { get; } = new() { Width = 1920, Height = 1080 };

    public bool CapturesMouseButtons => true;
    public bool PlaysAmbience => false;

    protected virtual Model3DGroup CreateLights() => Scene.Lights();

    protected abstract void UpdateScene(double dt);

    /// <summary>Called after Play Again created a fresh simulation.</summary>
    protected virtual void OnRestart() { }

    protected static Point3D W(double x, double y, double z) => new(-x, y, z);
    protected static Point3D W(V3 p) => new(-p.X, p.Y, p.Z);

    /// <summary>A static transform placed in sport coordinates.</summary>
    protected static Transform3D At(double x, double y, double z, double headingDegrees = 0, double sx = 1, double sy = 1, double sz = 1) =>
        Scene.At(-x, y, z, -headingDegrees, sx, sy, sz);

    protected static Border HudCard(UIElement child, Thickness padding)
    {
        var border = new Border { Child = child, Padding = padding };
        border.SetResourceReference(Border.CornerRadiusProperty, "CardCornerRadius");
        border.SetResourceReference(Border.BorderThicknessProperty, "PanelBorderThickness");
        border.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "PanelBorderBrush");
        border.Effect = new DropShadowEffect { ShadowDepth = 3, BlurRadius = 18, Opacity = 0.25 };
        return border;
    }

    protected void Play(SoundEffect effect)
    {
        if (!IsDemo) Host.Audio.Play(effect);
    }

    protected void Shake(double amount) => _shake = Math.Max(_shake, Anim.Reduced ? 0 : amount);

    protected void Popup(string text, Color glow)
    {
        _popup.Text = text;
        _popup.FontSize = text.Length > 18 ? 84 : 118;
        _popupGlow.Color = glow;
        _popupAge = 0;
    }

    /// <summary>Eases the camera toward a target (both points in WPF coordinates, see <see cref="W(double,double,double)"/>).</summary>
    protected void AimCamera(Point3D position, Point3D lookAt, double dt, double sharpness = 5, double fieldOfView = 50)
    {
        var k = _camReady ? 1 - Math.Exp(-sharpness * dt) : 1;
        _camPos += (position - _camPos) * k;
        _camLook += (lookAt - _camLook) * k;
        _camReady = true;
        var jitter = new Vector3D();
        if (_shake > 0.001)
        {
            jitter = new Vector3D(_fx.NextDouble() - 0.5, _fx.NextDouble() - 0.5, 0) * _shake;
            _shake *= Math.Exp(-9 * dt);
        }
        Camera.Position = _camPos + jitter;
        var look = _camLook - _camPos;
        if (look.LengthSquared < 1e-6) look = new Vector3D(0, 0, 1);
        Camera.LookDirection = look;
        Camera.UpDirection = new Vector3D(0, 1, 0);
        Camera.FieldOfView += (fieldOfView - Camera.FieldOfView) * Math.Min(1, k * 1.5 + (_camReady ? 0 : 1));
    }

    protected void SnapCamera() => _camReady = false;

    // ---------------------------------------------------------------- lifecycle

    public void OnShown()
    {
        if (!_attached)
        {
            CompositionTarget.Rendering += OnRendering;
            _attached = true;
        }
        _clock.Restart();
        _lastTime = 0;
        _input ??= new SportsInputHub(Host);
        _input.SetCapture(true);
        if (_overlay == Overlay.None && Sim.Time == 0) BeginIntro();
        Dispatcher.BeginInvoke(() => Keyboard.Focus(this), System.Windows.Threading.DispatcherPriority.Input);
    }

    public void OnHidden()
    {
        if (_attached)
        {
            CompositionTarget.Rendering -= OnRendering;
            _attached = false;
        }
        _input?.Dispose();
        _input = null;
        if (!_leaving && _overlay == Overlay.None && !Sim.IsFinished) Pause();
    }

    public bool HandleBack()
    {
        if (_leaving) return false;
        switch (_overlay)
        {
            case Overlay.Results:
                _leaving = true;
                return false;
            case Overlay.Paused:
                Resume();
                return true;
            default:
                Pause();
                return true;
        }
    }

    public bool HandleKey(KeyEventArgs e) => e.Key is Key.Up or Key.Down or Key.Left or Key.Right or Key.Space or Key.Enter or Key.Tab;

    private void BeginIntro()
    {
        if (IsDemo) return;
        _introLeft = 2.3;
        _introMode.Text = ModeLine();
        _introControls.Text = (_input?.Describe(Setup.Humans, SportSim.IsTurnBased(Setup.Sport)) ?? "") + "\nPause any time with Start, + or Esc.";
        _intro.Visibility = Visibility.Visible;
        _intro.Opacity = 1;
    }

    private string ModeLine()
    {
        if (Setup.Mode == SportMode.Training) return "Training  ·  " + TrainingGoals.For(Setup.Sport).Name;
        if (Setup.Humans == 1 && Setup.Players > 1) return $"Match vs Computer  ·  {SportsSession.DifficultyNames[Math.Clamp(Setup.Difficulty, 0, 3)]}";
        return Setup.Players == 1 ? "Solo match" : $"{Setup.Players} player match";
    }

    // ---------------------------------------------------------------- frame loop

    private void OnRendering(object? sender, EventArgs e)
    {
        var now = _clock.Elapsed.TotalSeconds;
        var dt = Math.Min(0.05, now - _lastTime);
        _lastTime = now;
        if (dt <= 0) return;
        try
        {
            Tick(dt);
        }
        catch (Exception ex)
        {
            Core.Diagnostics.Log.Error("Sports frame failed", ex);
            CompositionTarget.Rendering -= OnRendering;
            _attached = false;
        }
    }

    private void Tick(double dt)
    {
        PlayerCommand[] commands;
        MenuInput menu = default;
        if (_input is not null && !IsDemo)
            commands = _input.Poll(Setup.Players, Setup.Humans, SportSim.IsTurnBased(Setup.Sport), Sim.ActivePlayer, out menu);
        else
            commands = Array.Empty<PlayerCommand>();

        if (_overlay == Overlay.None && !IsDemo && !Host.Options.IsSnapshot && !Window.IsActive && !Sim.IsFinished) Pause();

        if (_overlay != Overlay.None)
        {
            _overlayAge += dt;
            if (_overlayAge > 0.35) HandleMenu(menu);
            if (_overlay == Overlay.Paused)
            {
                AnimatePopup(dt);
                return;
            }
        }
        else if (menu.Pause && !Sim.IsFinished)
        {
            Pause();
            return;
        }

        if (_introLeft > 0)
        {
            _introLeft -= dt;
            if (_introLeft < 0.4) _intro.Opacity = Math.Max(0, _introLeft / 0.4);
            if (_introLeft <= 0)
            {
                _intro.Visibility = Visibility.Collapsed;
                Play(SoundEffect.CountGo);
            }
        }
        else
        {
            Sim.Update(dt, commands);
        }

        foreach (var ev in Sim.DrainEvents()) OnEvent(ev);
        SceneTime += dt;
        UpdateScene(dt);
        AnimatePopup(dt);

        _scoreText.Text = Sim.Scoreline;
        var prompt = _introLeft > 0 || IsDemo ? "" : Sim.Prompt;
        _promptText.Text = prompt;
        _promptBar.Visibility = string.IsNullOrEmpty(prompt) ? Visibility.Hidden : Visibility.Visible;

        if (Sim.IsFinished && _overlay == Overlay.None)
        {
            _finishedFor = _finishedFor < 0 ? 0 : _finishedFor + dt;
            if (_finishedFor > 1.9 && !IsDemo) ShowResults();
        }
    }

    private void AnimatePopup(double dt)
    {
        _popupAge += dt;
        const double pop = 0.22, hold = 1.05, fade = 0.35;
        if (_popupAge > pop + hold + fade)
        {
            _popup.Opacity = 0;
            return;
        }
        if (_popupAge < pop)
        {
            var t = _popupAge / pop;
            var s = Anim.Reduced ? 1 : 0.55 + 0.45 * (1 + 2.2 * Math.Pow(t - 1, 3) + 1.2 * Math.Pow(t - 1, 2));
            _popupScale.ScaleX = _popupScale.ScaleY = s;
            _popup.Opacity = Math.Min(1, t * 2);
        }
        else
        {
            _popupScale.ScaleX = _popupScale.ScaleY = 1;
            _popup.Opacity = _popupAge < pop + hold ? 1 : 1 - (_popupAge - pop - hold) / fade;
        }
    }

    // ---------------------------------------------------------------- events

    protected static readonly Color Good = Color.FromRgb(255, 170, 20);
    protected static readonly Color Great = Color.FromRgb(235, 60, 160);
    protected static readonly Color Bad = Color.FromRgb(60, 90, 140);
    protected static readonly Color Neutral = Color.FromRgb(30, 140, 210);

    protected virtual void OnEvent(SportEvent e)
    {
        SoundEffect? sound = e.Kind switch
        {
            SportEventKind.Swing or SportEventKind.Punch or SportEventKind.Pitch => SoundEffect.SportSwing,
            SportEventKind.Hit => SoundEffect.SportHit,
            SportEventKind.PowerHit => SoundEffect.SportPowerHit,
            SportEventKind.Bounce => e.Strength > 0.08 ? SoundEffect.SportBounce : null,
            SportEventKind.Net => SoundEffect.SportNet,
            SportEventKind.Out or SportEventKind.Fault or SportEventKind.Gutter or SportEventKind.StrikeOut => SoundEffect.CrowdGroan,
            SportEventKind.Point or SportEventKind.Target => SoundEffect.Point,
            SportEventKind.Game or SportEventKind.Spare => SoundEffect.Applause,
            SportEventKind.Contact => SoundEffect.BatCrack,
            SportEventKind.Catch => SoundEffect.MittPop,
            SportEventKind.Strike or SportEventKind.Foul => SoundEffect.Tick,
            SportEventKind.HomeRun or SportEventKind.BowlingStrike or SportEventKind.Holed => SoundEffect.CrowdCheer,
            SportEventKind.Single or SportEventKind.Double or SportEventKind.Triple or SportEventKind.Run => SoundEffect.Applause,
            SportEventKind.Roll => SoundEffect.BowlRoll,
            SportEventKind.PinHit => SoundEffect.PinCrash,
            SportEventKind.PinsFall => SoundEffect.PinTap,
            SportEventKind.GolfSwing => SoundEffect.GolfClick,
            SportEventKind.Land => SoundEffect.SportBounce,
            SportEventKind.Putt => SoundEffect.Putt,
            SportEventKind.Splash => SoundEffect.Splash,
            SportEventKind.Bunker => SoundEffect.SandThud,
            SportEventKind.PunchHit => e.Strength > 0.8 ? SoundEffect.PunchHeavy : SoundEffect.Punch,
            SportEventKind.Block => SoundEffect.Block,
            SportEventKind.Dodge => SoundEffect.SportSwing,
            SportEventKind.KnockDown => SoundEffect.PunchHeavy,
            SportEventKind.GetUp => SoundEffect.Applause,
            SportEventKind.Bell => SoundEffect.BoxBell,
            SportEventKind.Cheer => SoundEffect.CrowdCheer,
            SportEventKind.Miss => SoundEffect.Error,
            SportEventKind.Announce when e.Text is { Length: <= 2 } => SoundEffect.CountBeep,
            _ => null,
        };
        if (sound is { } s) Play(s);

        if (e.Text is { Length: > 0 } text && e.Kind is not (SportEventKind.Pitch or SportEventKind.Serve))
        {
            var glow = e.Kind switch
            {
                SportEventKind.HomeRun or SportEventKind.BowlingStrike or SportEventKind.KnockDown or SportEventKind.Game => Great,
                SportEventKind.Holed or SportEventKind.Spare or SportEventKind.Target or SportEventKind.Run or SportEventKind.Triple
                    or SportEventKind.Double or SportEventKind.Single or SportEventKind.Hit or SportEventKind.PowerHit or SportEventKind.PunchHit
                    or SportEventKind.GetUp or SportEventKind.Contact => Good,
                SportEventKind.Out or SportEventKind.Fault or SportEventKind.Miss or SportEventKind.Gutter or SportEventKind.Splash
                    or SportEventKind.StrikeOut or SportEventKind.Net or SportEventKind.Bunker => Bad,
                _ => Neutral,
            };
            Popup(text, glow);
        }
    }

    // ---------------------------------------------------------------- pause & results

    private void Pause()
    {
        if (_overlay != Overlay.None || _leaving) return;
        _overlay = Overlay.Paused;
        Play(SoundEffect.HomeOpen);
        var hint = _input?.Describe(Setup.Humans, SportSim.IsTurnBased(Setup.Sport)) ?? "";
        ShowOverlay("Paused", null, new[] { ViewKit.Text(ModeLine(), 32, FontWeights.Bold, align: TextAlignment.Center), ViewKit.Text(hint, 26, FontWeights.Normal, "SubtleTextBrush", align: TextAlignment.Center) },
            ("Resume", Resume), ("Restart", Restart), ("Quit", Leave));
    }

    private void Resume()
    {
        if (_overlay != Overlay.Paused) return;
        _overlay = Overlay.None;
        _overlayHost.Visibility = Visibility.Collapsed;
        Play(SoundEffect.HomeClose);
        _clock.Restart();
        _lastTime = 0;
        Keyboard.Focus(this);
    }

    private void Restart()
    {
        Play(SoundEffect.Select);
        Sim = SportSim.Create(Setup with { Seed = 0 });
        _overlay = Overlay.None;
        _overlayHost.Visibility = Visibility.Collapsed;
        _finishedFor = -1;
        _popupAge = 99;
        SnapCamera();
        OnRestart();
        BeginIntro();
        Keyboard.Focus(this);
    }

    private void Leave()
    {
        _leaving = true;
        _overlay = Overlay.None;
        _overlayHost.Visibility = Visibility.Collapsed;
        Window.GoBack();
    }

    private void ShowResults()
    {
        var result = Sim.Result!;
        _overlay = Overlay.Results;
        RecordUpdate? update = null;
        if (!IsDemo && Setup.Humans >= 1) update = SportsSession.Records(Host).Record(result, Setup);
        var stats = SportsSession.Records(Host).Current.For(Setup.Sport);

        var lines = new List<UIElement> { ViewKit.Text(result.Detail, 34, FontWeights.SemiBold, align: TextAlignment.Center) };
        UIElement? badge = null;
        string headline;
        SoundEffect jingle;

        if (result.Mode == SportMode.Training)
        {
            var goal = TrainingGoals.For(Setup.Sport);
            var medal = TrainingGoals.MedalFor(Setup.Sport, result.TrainingScore ?? 0);
            headline = result.Headline;
            badge = MedalBadge(medal, 120);
            lines.Add(ViewKit.Text(medal == TrainingMedal.None
                ? $"Bronze needs {goal.Bronze} {goal.Unit}. So close!"
                : $"{medal} medal!{(update?.MedalImproved == true ? "  New medal unlocked." : "")}", 36, FontWeights.ExtraBold, medal == TrainingMedal.None ? "SubtleTextBrush" : "AccentDeepBrush", align: TextAlignment.Center));
            lines.Add(ViewKit.Text(update?.NewBest == true ? "New personal best!" : $"Personal best: {stats.BestTraining} {goal.Unit}", 28, FontWeights.Normal, "SubtleTextBrush", align: TextAlignment.Center));
            lines.Add(ViewKit.Text($"Bronze {goal.Bronze}  ·  Silver {goal.Silver}  ·  Gold {goal.Gold}", 26, FontWeights.Normal, "SubtleTextBrush", align: TextAlignment.Center));
            jingle = medal == TrainingMedal.None ? SoundEffect.Defeat : SoundEffect.Medal;
        }
        else
        {
            var vsCpu = Setup.Humans == 1 && Setup.Players > 1;
            headline = vsCpu && result.Winner is { } w ? (w == 0 ? "You win!" : "The computer wins") : result.Headline;
            if (vsCpu && result.Headline.StartsWith("K.O.", StringComparison.Ordinal)) headline = "K.O.!  " + headline;
            jingle = vsCpu && result.Winner != 0 ? SoundEffect.Defeat : SoundEffect.Victory;
            if (vsCpu && update is not null)
            {
                var delta = update.SkillAfter - update.SkillBefore;
                lines.Add(ViewKit.Text($"Skill {update.SkillAfter} ({(delta >= 0 ? "+" : "")}{delta})  ·  {SkillRating.Level(update.SkillAfter)}", 32, FontWeights.Bold, "AccentDeepBrush", align: TextAlignment.Center));
            }
            if (update?.NewBest == true && Setup.Humans >= 1) lines.Add(ViewKit.Text("New personal best!", 28, FontWeights.Normal, "SubtleTextBrush", align: TextAlignment.Center));
        }

        Play(jingle);
        ShowOverlay(headline, badge, lines, ("Play Again", Restart), ("Sports Menu", Leave));
    }

    /// <summary>Renders the results card without playing a whole game (visual regression snapshots).</summary>
    internal void ShowSnapshotResults(SportResult result)
    {
        _overlay = Overlay.Results;
        var medal = TrainingGoals.MedalFor(result.Sport, result.TrainingScore ?? 0);
        ShowOverlay(result.Headline, result.Mode == SportMode.Training ? MedalBadge(medal, 120) : null,
            new UIElement[]
            {
                ViewKit.Text(result.Detail, 34, FontWeights.SemiBold, align: TextAlignment.Center),
                ViewKit.Text($"{medal} medal!", 36, FontWeights.ExtraBold, "AccentDeepBrush", align: TextAlignment.Center),
                ViewKit.Text("New personal best!", 28, FontWeights.Normal, "SubtleTextBrush", align: TextAlignment.Center),
            },
            ("Play Again", () => { }), ("Sports Menu", () => { }));
    }

    public static FrameworkElement MedalBadge(TrainingMedal medal, double size)
    {
        var (light, dark) = medal switch
        {
            TrainingMedal.Gold => (Color.FromRgb(255, 226, 110), Color.FromRgb(214, 150, 20)),
            TrainingMedal.Silver => (Color.FromRgb(240, 244, 248), Color.FromRgb(150, 162, 176)),
            TrainingMedal.Bronze => (Color.FromRgb(242, 184, 130), Color.FromRgb(168, 96, 44)),
            _ => (Color.FromRgb(226, 230, 234), Color.FromRgb(196, 202, 208)),
        };
        var grid = new Grid { Width = size, Height = size };
        grid.Children.Add(new Ellipse { Fill = new RadialGradientBrush(light, dark) { GradientOrigin = new Point(0.35, 0.3) }, Stroke = new SolidColorBrush(dark), StrokeThickness = size * 0.05 });
        grid.Children.Add(new Ellipse { Margin = new Thickness(size * 0.16), Stroke = new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)), StrokeThickness = size * 0.03 });
        grid.Children.Add(new Path
        {
            Data = Geometry.Parse("M 50,18 L 59,39 L 82,41 L 64,56 L 70,79 L 50,67 L 30,79 L 36,56 L 18,41 L 41,39 Z"),
            Fill = medal == TrainingMedal.None ? new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)) : Brushes.White,
            Stretch = Stretch.Uniform,
            Margin = new Thickness(size * 0.26),
        });
        return grid;
    }

    private void ShowOverlay(string title, UIElement? badge, IEnumerable<UIElement> lines, params (string Label, Action Action)[] buttons)
    {
        _overlayBody.Children.Clear();
        _menu.Clear();
        if (badge is FrameworkElement fe)
        {
            fe.HorizontalAlignment = HorizontalAlignment.Center;
            fe.Margin = new Thickness(0, 0, 0, 12);
            _overlayBody.Children.Add(fe);
        }
        var heading = ViewKit.Text(title, 72, FontWeights.ExtraBold, "AccentDeepBrush", align: TextAlignment.Center);
        heading.Margin = new Thickness(0, 0, 0, 12);
        _overlayBody.Children.Add(heading);
        foreach (var line in lines)
        {
            if (line is FrameworkElement l) l.Margin = new Thickness(0, 4, 0, 4);
            _overlayBody.Children.Add(line);
        }
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 30, 0, 0) };
        foreach (var (label, action) in buttons)
        {
            var captured = action;
            var button = ViewKit.Pill(label, () => captured(), buttons.Length > 2 ? 250 : 320);
            button.Height = 96;
            row.Children.Add(button);
            _menu.Add((button, captured));
        }
        _overlayBody.Children.Add(row);
        _overlayHost.Visibility = Visibility.Visible;
        _overlayAge = 0;
        _menuIndex = 0;
        Dispatcher.BeginInvoke(() => _menu.FirstOrDefault().Button?.Focus(), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void HandleMenu(MenuInput menu)
    {
        if (_menu.Count == 0) return;
        var move = (menu.Left || menu.Up ? -1 : 0) + (menu.Right || menu.Down ? 1 : 0);
        if (move != 0)
        {
            var focused = _menu.FindIndex(m => m.Button.IsKeyboardFocused);
            if (focused >= 0) _menuIndex = focused;
            _menuIndex = (_menuIndex + move + _menu.Count) % _menu.Count;
            _menu[_menuIndex].Button.Focus();
            Play(SoundEffect.Hover);
        }
        if (menu.Confirm)
        {
            var focused = _menu.FindIndex(m => m.Button.IsKeyboardFocused);
            _menu[focused >= 0 ? focused : _menuIndex].Action();
        }
        else if (menu.Pause || menu.Back)
        {
            if (_overlay == Overlay.Paused) Resume();
            else if (menu.Back) Leave();
        }
    }
}
