using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using Couchtop.App.Services;
using Couchtop.App.Views;
using Couchtop.Core.Sports;

namespace Couchtop.App.Sports;

/// <summary>The Couchtop Sports title screen: pick a sport, see your progress, dress your Pal.</summary>
public sealed class SportsHubView : UserControl, IScreenView
{
    private readonly AppHost _host;
    private readonly MainWindow _window;
    private readonly UniformGrid _cards = new() { Columns = 5, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly StackPanel _palColors = new() { Orientation = Orientation.Horizontal };
    private readonly StackPanel _palSkins = new() { Orientation = Orientation.Horizontal };
    private readonly Viewport3D _preview = new() { Width = 230, Height = 250 };
    private readonly Model3DGroup _previewRoot = new();

    public SportsHubView(AppHost host, MainWindow window)
    {
        _host = host;
        _window = window;

        _preview.Camera = new PerspectiveCamera(new Point3D(0, 1.0, 3.3), new Vector3D(0, -0.18, -1), new Vector3D(0, 1, 0), 30);
        var world = new Model3DGroup();
        world.Children.Add(Scene.Lights());
        world.Children.Add(_previewRoot);
        _preview.Children.Add(new ModelVisual3D { Content = world });

        var palText = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 30, 0) };
        palText.Children.Add(ViewKit.Text("Your Pal", 40, FontWeights.ExtraBold, "AccentDeepBrush", wrap: false));
        palText.Children.Add(ViewKit.Text("Shirt", 26, FontWeights.Bold, "SubtleTextBrush", wrap: false));
        palText.Children.Add(_palColors);
        palText.Children.Add(ViewKit.Text("Skin", 26, FontWeights.Bold, "SubtleTextBrush", wrap: false));
        palText.Children.Add(_palSkins);
        var controls = ViewKit.Text("Works with controllers, Wii Remotes (swing to play), keyboard and mouse. Two people can share a keyboard: WASD + Space and arrows + Enter.", 26, FontWeights.Normal, "SubtleTextBrush");
        controls.VerticalAlignment = VerticalAlignment.Center;
        controls.Width = 700;

        var palRow = new StackPanel { Orientation = Orientation.Horizontal };
        palRow.Children.Add(_preview);
        palRow.Children.Add(palText);
        palRow.Children.Add(controls);

        var body = new StackPanel();
        body.Children.Add(_cards);
        body.Children.Add(ViewKit.Card(palRow, new Thickness(30, 10, 40, 10), new Thickness(12, 22, 12, 0)));
        Content = ViewKit.Scaffold("Couchtop Sports", "Pick a sport", body, window.ReturnToMenu);
        Build();
    }

    public bool PlaysAmbience => true;

    private void Build()
    {
        var records = SportsSession.Records(_host).Current;
        _cards.Children.Clear();
        foreach (var sport in Enum.GetValues<SportKind>())
        {
            var stats = records.For(sport);
            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            var emblem = Emblem(sport, 200);
            emblem.Margin = new Thickness(0, 6, 0, 14);
            stack.Children.Add(emblem);
            stack.Children.Add(ViewKit.Text(SportSim.DisplayName(sport), 44, FontWeights.ExtraBold, "ButtonTextBrush", wrap: false, align: TextAlignment.Center));
            stack.Children.Add(ViewKit.Text($"{SkillRating.Level(stats.Skill)}  ·  {stats.Skill}", 26, FontWeights.Bold, "SubtleTextBrush", wrap: false, align: TextAlignment.Center));
            var medal = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) };
            medal.Children.Add(SportView.MedalBadge(stats.Medal, 40));
            var medalText = ViewKit.Text(stats.Medal == TrainingMedal.None ? "No medal" : stats.Medal.ToString(), 24, FontWeights.SemiBold, "SubtleTextBrush", wrap: false);
            medalText.VerticalAlignment = VerticalAlignment.Center;
            medalText.Margin = new Thickness(10, 0, 0, 0);
            medal.Children.Add(medalText);
            stack.Children.Add(medal);
            var card = new Button { Content = stack, Width = 322, Height = 470, Margin = new Thickness(12) };
            var chosen = sport;
            card.Click += (_, _) => _window.Navigate(new SportMenuView(_host, _window, chosen));
            _cards.Children.Add(card);
        }

        var look = records.Pal(0);
        _palColors.Children.Clear();
        for (var i = 0; i < PalModel.Shirts.Length; i++)
        {
            var index = i;
            _palColors.Children.Add(Swatch(PalModel.Shirts[i], look.Shirt == i, () =>
            {
                look.Shirt = index;
                SportsSession.Records(_host).Save();
                Build();
            }));
        }
        _palSkins.Children.Clear();
        for (var i = 0; i < PalModel.Skins.Length; i++)
        {
            var index = i;
            _palSkins.Children.Add(Swatch(PalModel.Skins[i], look.Skin == i, () =>
            {
                look.Skin = index;
                look.Hair = (index + 1) % PalModel.Hair.Length;
                SportsSession.Records(_host).Save();
                Build();
            }));
        }

        _previewRoot.Children.Clear();
        var pal = new PalModel(look);
        pal.Place(0, 0, 0, 20);
        pal.Hands(-0.36, 0.95, 0.1, 0.36, 0.52, 0.12);
        _previewRoot.Children.Add(pal.Root);
    }

    private static ToggleButton Swatch(Color color, bool selected, Action pick)
    {
        var button = ViewKit.Option("", selected, pick);
        button.MinWidth = 0;
        button.MinHeight = 0;
        button.Width = 72;
        button.Height = 64;
        button.Padding = new Thickness(0);
        button.Margin = new Thickness(4);
        button.Content = new Ellipse { Width = 36, Height = 36, Fill = new SolidColorBrush(color), Stroke = new SolidColorBrush(Color.FromArgb(60, 0, 0, 0)), StrokeThickness = 2 };
        return button;
    }

    /// <summary>Original vector emblem for each sport.</summary>
    public static FrameworkElement Emblem(SportKind sport, double size)
    {
        var canvas = new Canvas { Width = 200, Height = 200 };
        var background = sport switch
        {
            SportKind.Tennis => (Color.FromRgb(96, 186, 250), Color.FromRgb(40, 120, 210)),
            SportKind.Baseball => (Color.FromRgb(120, 210, 120), Color.FromRgb(50, 150, 80)),
            SportKind.Bowling => (Color.FromRgb(186, 140, 250), Color.FromRgb(110, 70, 190)),
            SportKind.Golf => (Color.FromRgb(140, 220, 190), Color.FromRgb(40, 160, 120)),
            _ => (Color.FromRgb(255, 160, 130), Color.FromRgb(220, 70, 60)),
        };
        canvas.Children.Add(new Ellipse { Width = 200, Height = 200, Fill = new RadialGradientBrush(background.Item1, background.Item2) { GradientOrigin = new Point(0.35, 0.25) } });
        canvas.Children.Add(new Ellipse { Width = 150, Height = 70, Fill = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)), Margin = new Thickness(25, 12, 0, 0) });

        static Path P(string data, Brush? fill, Brush? stroke = null, double thickness = 0) => new()
        {
            Data = Geometry.Parse(data),
            Fill = fill,
            Stroke = stroke,
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
        };

        switch (sport)
        {
            case SportKind.Tennis:
                canvas.Children.Add(new Ellipse { Width = 116, Height = 116, Margin = new Thickness(42), Fill = new RadialGradientBrush(Color.FromRgb(246, 255, 140), Color.FromRgb(196, 222, 40)) { GradientOrigin = new Point(0.35, 0.3) } });
                canvas.Children.Add(P("M 56,66 Q 100,100 56,136 M 144,64 Q 100,100 144,138", null, Brushes.White, 7));
                break;
            case SportKind.Baseball:
                canvas.Children.Add(new Ellipse { Width = 120, Height = 120, Margin = new Thickness(40), Fill = new RadialGradientBrush(Colors.White, Color.FromRgb(220, 224, 228)) { GradientOrigin = new Point(0.35, 0.3) } });
                var red = new SolidColorBrush(Color.FromRgb(220, 50, 60));
                canvas.Children.Add(P("M 72,52 Q 100,100 72,148 M 128,52 Q 100,100 128,148", null, red, 5));
                canvas.Children.Add(P("M 76,68 L 88,64 M 82,84 L 94,82 M 84,100 L 96,100 M 82,116 L 94,118 M 76,132 L 88,136 M 124,68 L 112,64 M 118,84 L 106,82 M 116,100 L 104,100 M 118,116 L 106,118 M 124,132 L 112,136", null, red, 3));
                break;
            case SportKind.Bowling:
                canvas.Children.Add(P("M 84,34 C 98,34 100,50 96,62 C 93,70 92,76 96,86 C 110,112 116,136 108,160 C 104,172 96,176 84,176 C 72,176 64,172 60,160 C 52,136 58,112 72,86 C 76,76 75,70 72,62 C 68,50 70,34 84,34 Z", Brushes.White));
                canvas.Children.Add(P("M 73,70 L 95,70 M 72,79 L 96,79", null, new SolidColorBrush(Color.FromRgb(220, 50, 60)), 5));
                canvas.Children.Add(new Ellipse { Width = 74, Height = 74, Margin = new Thickness(100, 98, 0, 0), Fill = new RadialGradientBrush(Color.FromRgb(90, 150, 255), Color.FromRgb(30, 60, 170)) { GradientOrigin = new Point(0.35, 0.3) } });
                foreach (var (x, y) in new[] { (124.0, 116.0), (142.0, 116.0), (133.0, 132.0) })
                    canvas.Children.Add(new Ellipse { Width = 10, Height = 10, Margin = new Thickness(x, y, 0, 0), Fill = new SolidColorBrush(Color.FromRgb(20, 26, 60)) });
                break;
            case SportKind.Golf:
                canvas.Children.Add(new Ellipse { Width = 140, Height = 46, Margin = new Thickness(30, 130, 0, 0), Fill = new SolidColorBrush(Color.FromRgb(150, 230, 120)) });
                canvas.Children.Add(new Ellipse { Width = 18, Height = 7, Margin = new Thickness(91, 150, 0, 0), Fill = new SolidColorBrush(Color.FromRgb(30, 60, 40)) });
                canvas.Children.Add(P("M 100,154 L 100,40", null, Brushes.White, 5));
                canvas.Children.Add(P("M 102,40 L 150,58 L 102,76 Z", new SolidColorBrush(Color.FromRgb(240, 70, 70))));
                canvas.Children.Add(new Ellipse { Width = 22, Height = 22, Margin = new Thickness(126, 140, 0, 0), Fill = Brushes.White, Stroke = new SolidColorBrush(Color.FromRgb(200, 205, 210)), StrokeThickness = 2 });
                break;
            default:
                canvas.Children.Add(P("M 64,150 L 62,104 C 50,90 52,48 90,40 C 130,32 156,56 154,96 C 153,124 146,142 132,150 Z", new RadialGradientBrush(Color.FromRgb(255, 110, 100), Color.FromRgb(200, 30, 40)) { GradientOrigin = new Point(0.35, 0.3) }));
                canvas.Children.Add(P("M 62,104 C 44,100 40,78 56,70 C 66,66 76,74 78,86", new SolidColorBrush(Color.FromRgb(214, 44, 52)), new SolidColorBrush(Color.FromRgb(170, 20, 30)), 3));
                canvas.Children.Add(P("M 62,150 L 134,150 L 134,176 L 62,176 Z", Brushes.White));
                break;
        }
        return new Viewbox { Width = size, Height = size, Child = canvas };
    }

    public void OnShown()
    {
        Build();
        Dispatcher.BeginInvoke(() => (_cards.Children.Count > 0 ? _cards.Children[0] as UIElement : null)?.Focus(), System.Windows.Threading.DispatcherPriority.Input);
    }

    public void OnHidden() { }

    public bool HandleBack() => false;

    public bool HandleKey(KeyEventArgs e) => MenuKeys.MoveFocus(e);
}

internal static class MenuKeys
{
    public static bool MoveFocus(KeyEventArgs e)
    {
        var direction = e.Key switch
        {
            Key.Left => FocusNavigationDirection.Left,
            Key.Right => FocusNavigationDirection.Right,
            Key.Up => FocusNavigationDirection.Up,
            Key.Down => FocusNavigationDirection.Down,
            _ => (FocusNavigationDirection?)null,
        };
        if (direction is null || Keyboard.FocusedElement is not UIElement focused) return false;
        focused.MoveFocus(new TraversalRequest(direction.Value));
        return true;
    }
}

/// <summary>One sport's menu: match against the computer, local multiplayer, training, and records.</summary>
public sealed class SportMenuView : UserControl, IScreenView
{
    private readonly AppHost _host;
    private readonly MainWindow _window;
    private readonly SportKind _sport;
    private readonly StackPanel _records = new();
    private readonly WrapPanel _difficultyRow = new() { Width = 820 };
    private readonly Button _play;
    private int _difficulty = -1;

    public SportMenuView(AppHost host, MainWindow window, SportKind sport)
    {
        _host = host;
        _window = window;
        _sport = sport;
        var goal = TrainingGoals.For(sport);

        var left = new StackPanel { Width = 820 };
        _play = ViewKit.Pill("Play vs Computer", () => Launch(new SportSetup(sport, SportMode.Match, 2, 1, EffectiveDifficulty())), 760);
        _play.Height = 120;
        _play.FontSize = 42;
        _play.HorizontalAlignment = HorizontalAlignment.Left;
        left.Children.Add(_play);
        var skillLabel = ViewKit.Text("Computer skill", 26, FontWeights.Bold, "SubtleTextBrush", wrap: false);
        skillLabel.Margin = new Thickness(20, 6, 0, 0);
        left.Children.Add(skillLabel);
        left.Children.Add(_difficultyRow);

        var multiLabel = ViewKit.Text(SportSim.IsTurnBased(sport) ? "Take turns (pass the controller or use one each)" : "Two players", 26, FontWeights.Bold, "SubtleTextBrush", wrap: false);
        multiLabel.Margin = new Thickness(20, 22, 0, 0);
        left.Children.Add(multiLabel);
        var multi = new WrapPanel();
        var (_, max) = SportSim.PlayerRange(sport);
        if (SportSim.IsTurnBased(sport)) multi.Children.Add(Small("Solo", () => Launch(new SportSetup(sport, SportMode.Match, 1, 1))));
        for (var n = 2; n <= max; n++)
        {
            var count = n;
            multi.Children.Add(Small($"{n} Players", () => Launch(new SportSetup(sport, SportMode.Match, count, count))));
        }
        left.Children.Add(multi);

        var trainingLabel = ViewKit.Text("Training", 26, FontWeights.Bold, "SubtleTextBrush", wrap: false);
        trainingLabel.Margin = new Thickness(20, 22, 0, 0);
        left.Children.Add(trainingLabel);
        var training = ViewKit.Pill(goal.Name, () => Launch(new SportSetup(sport, SportMode.Training, SportSim.IsTurnBased(sport) ? 1 : 2, 1)), 760);
        training.Height = 100;
        training.HorizontalAlignment = HorizontalAlignment.Left;
        left.Children.Add(training);
        var trainingHelp = ViewKit.Text(goal.Description, 26, FontWeights.Normal, "SubtleTextBrush");
        trainingHelp.Margin = new Thickness(20, 0, 0, 0);
        left.Children.Add(trainingHelp);

        var right = new StackPanel { Margin = new Thickness(40, 0, 0, 0) };
        right.Children.Add(ViewKit.Card(_records, new Thickness(40, 26, 40, 28)));
        var how = new StackPanel();
        how.Children.Add(ViewKit.Text("How to play", 34, FontWeights.ExtraBold, "AccentDeepBrush", wrap: false));
        how.Children.Add(ViewKit.Text(HowToPlay(sport), 27));
        right.Children.Add(ViewKit.Card(how, new Thickness(40, 24, 40, 28)));

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(left);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);

        var emblem = SportsHubView.Emblem(sport, 110);
        emblem.Margin = new Thickness(0, 0, 20, 0);
        Content = ViewKit.Scaffold(SportSim.DisplayName(sport), "Couchtop Sports", ViewKit.Scroll(grid), window.GoBack, emblem);
    }

    private static ButtonBase Small(string text, Action action)
    {
        var button = ViewKit.Pill(text, action, 240);
        button.Height = 88;
        return button;
    }

    private int EffectiveDifficulty() =>
        _difficulty >= 0 ? _difficulty : SkillRating.DifficultyFor(SportsSession.Records(_host).Current.For(_sport).Skill);

    private void Launch(SportSetup setup)
    {
        _host.Audio.Play(SoundEffect.Launch);
        _window.Navigate(SportsSession.CreateView(_host, _window, setup));
    }

    public static string HowToPlay(SportKind sport) => sport switch
    {
        SportKind.Tennis => "Players run to the ball for you. Swing as the ball arrives: A, Space, left click or a Wii Remote swing. Early swings pull the ball across, late swings push it; the stick aims. First to two games wins.",
        SportKind.Baseball => "Batting: swing as the ball reaches the plate. Perfect timing sends it over the fence. Pitching: hold left or right to pick a curveball or changeup, then press A when the meter is centered. Three innings.",
        SportKind.Bowling => "Left and right move you, up and down angle the throw. Hold A to wind up and let go at full power, or swing a Wii Remote. Steer while the ball rolls to curve it into the pocket.",
        SportKind.Golf => "The stick aims and the right club is picked for you (B changes it). Hold A to swing back and let go at the power you want; past the white line is an overswing. Wii Remote swings work too.",
        _ => "Punch with the bumpers, Q and E, the mouse buttons or Wii Remote swings (A alternates). Hold B or Shift to block, lean with the stick to dodge, then counter while they're open.",
    };

    private void BuildRecords()
    {
        var stats = SportsSession.Records(_host).Current.For(_sport);
        var goal = TrainingGoals.For(_sport);
        _records.Children.Clear();
        _records.Children.Add(ViewKit.Text($"{SkillRating.Level(stats.Skill)}", 44, FontWeights.ExtraBold, "AccentDeepBrush", wrap: false));
        var bar = new Grid { Height = 26, Margin = new Thickness(0, 8, 0, 6) };
        var track = new Rectangle { RadiusX = 13, RadiusY = 13 };
        track.SetResourceReference(Shape.FillProperty, "TrackBrush");
        bar.Children.Add(track);
        var fill = new Rectangle { RadiusX = 13, RadiusY = 13, HorizontalAlignment = HorizontalAlignment.Left, Width = Math.Max(26, 700.0 * stats.Skill / SkillRating.Max) };
        fill.SetResourceReference(Shape.FillProperty, "AccentBrush");
        bar.Children.Add(fill);
        bar.Width = 700;
        bar.HorizontalAlignment = HorizontalAlignment.Left;
        _records.Children.Add(bar);
        _records.Children.Add(ViewKit.Text($"Skill {stats.Skill} of {SkillRating.Max}  ·  Played {stats.Played}  ·  Wins {stats.Wins}", 28, FontWeights.SemiBold, "SubtleTextBrush", wrap: false));
        if (stats.BestMatch is { } best)
        {
            var text = _sport switch
            {
                SportKind.Tennis => $"Most games in a match: {best}",
                SportKind.Baseball => $"Most runs in a game: {best}",
                SportKind.Bowling => $"High score: {best}",
                SportKind.Golf => $"Best round: {best} strokes",
                _ => $"Most knockdowns in a fight: {best}",
            };
            _records.Children.Add(ViewKit.Text(text, 28, FontWeights.SemiBold, wrap: false));
        }
        var medalRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 18, 0, 0) };
        medalRow.Children.Add(SportView.MedalBadge(stats.Medal, 86));
        var medalText = new StackPanel { Margin = new Thickness(20, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        medalText.Children.Add(ViewKit.Text(stats.Medal == TrainingMedal.None ? $"{goal.Name}: no medal yet" : $"{goal.Name}: {stats.Medal}", 30, FontWeights.Bold, wrap: false));
        medalText.Children.Add(ViewKit.Text($"{(stats.BestTraining is { } bestTraining ? $"Best {bestTraining} {goal.Unit}" : "Not played yet")}  ·  Bronze {goal.Bronze}, Silver {goal.Silver}, Gold {goal.Gold}", 24, FontWeights.Normal, "SubtleTextBrush", wrap: false));
        medalRow.Children.Add(medalText);
        _records.Children.Add(medalRow);

        _difficultyRow.Children.Clear();
        var auto = SkillRating.DifficultyFor(stats.Skill);
        var options = new[] { ($"Auto ({SportsSession.DifficultyNames[auto]})", -1), ("Easy", 0), ("Normal", 1), ("Hard", 2), ("Expert", 3) };
        foreach (var (label, value) in options)
        {
            var captured = value;
            var option = ViewKit.Option(label, _difficulty == value, () =>
            {
                _difficulty = captured;
                BuildRecords();
            });
            option.MinWidth = 0;
            option.Padding = new Thickness(24, 4, 24, 4);
            option.Margin = new Thickness(6, 8, 6, 8);
            _difficultyRow.Children.Add(option);
        }
    }

    public void OnShown()
    {
        BuildRecords();
        Dispatcher.BeginInvoke(() => _play.Focus(), System.Windows.Threading.DispatcherPriority.Input);
    }

    public void OnHidden() { }

    public bool HandleBack() => false;

    public bool PlaysAmbience => true;

    public bool HandleKey(KeyEventArgs e) => MenuKeys.MoveFocus(e);
}
