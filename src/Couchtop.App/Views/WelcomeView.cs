using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Couchtop.App.Controls;
using Couchtop.App.Services;
using Couchtop.Core.Channels;
using Couchtop.Core.Settings;

namespace Couchtop.App.Views;

/// <summary>
/// The tour new installs get: what Couchtop is, the three shells (picking one switches straight away), what is
/// already set up, how to drive it, and how to get back to Windows. It follows whichever shell is on, so the
/// screen the person is reading is itself an example of the choice they just made.
/// </summary>
public sealed class WelcomeView : UserControl, IScreenView
{
    /// <summary>Bumped when the tour gains something worth showing people who already have Couchtop.</summary>
    public const int CurrentVersion = 2;

    private readonly AppHost _host;
    private readonly MainWindow _window;
    private readonly Grid _stage = new() { Width = 1920, Height = 1080 };
    private readonly ContentControl _body = new() { Focusable = false };
    private readonly TextBlock _stepText, _title, _lead;
    private readonly StackPanel _dots = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 74) };
    private readonly Button _back, _next, _skip;
    private readonly TranslateTransform _bodyShift = new();

    private readonly List<Step> _steps;
    private int _index;

    private sealed record Step(string Title, string Lead, Func<UIElement> Content, string NextLabel = "Next");

    public WelcomeView(AppHost host, MainWindow window)
    {
        _host = host;
        _window = window;
        Focusable = true;
        FocusVisualStyle = null;

        _steps = new List<Step>
        {
            new("Welcome to Couchtop", "Your PC, from the couch. Everything you already have, on a screen built for a controller and a comfortable distance.", BuildIntro),
            new("Pick how it looks", "Three shells. Each one changes every screen, its icons, its sounds and the way you move around. Try one now — it switches instantly.", BuildShells),
            new("Your apps are already here", "Couchtop went looking while you read this.", BuildApps),
            new("Built in", "A few things Couchtop brings of its own.", BuildBuiltIns),
            new("How to drive it", "Mouse, keyboard, a controller, or a Wii Remote over Bluetooth.", BuildControls),
            new("Getting back to Windows", "Couchtop is a window until you tell it otherwise. Nothing is replaced, and there is always a way out.", BuildSafety),
            new("That's the tour", "Everything here can be changed later in Settings.", BuildFinish, "Start using Couchtop"),
        };

        var backdrop = new Grid();
        backdrop.SetResourceReference(Panel.BackgroundProperty, "BackgroundBrush");
        _stage.Children.Add(backdrop);
        var scrim = new Rectangle { Opacity = 0.5 };
        scrim.SetResourceReference(Shape.FillProperty, "ScrimBrush");
        _stage.Children.Add(scrim);

        _stepText = ViewKit.Text("", 26, FontWeights.Bold, "SubtleTextBrush", wrap: false);
        _stepText.Margin = new Thickness(150, 92, 0, 0);
        _stepText.HorizontalAlignment = HorizontalAlignment.Left;
        _stepText.VerticalAlignment = VerticalAlignment.Top;
        _stage.Children.Add(_stepText);

        _title = ViewKit.Text("", 84, FontWeights.ExtraBold, wrap: false);
        _title.Margin = new Thickness(150, 132, 150, 0);
        _title.VerticalAlignment = VerticalAlignment.Top;
        _stage.Children.Add(_title);

        _lead = ViewKit.Text("", 34, FontWeights.Normal, "SubtleTextBrush");
        _lead.Margin = new Thickness(150, 246, 420, 0);
        _lead.VerticalAlignment = VerticalAlignment.Top;
        _stage.Children.Add(_lead);

        _body.Margin = new Thickness(150, 348, 150, 186);
        _body.RenderTransform = _bodyShift;
        _stage.Children.Add(_body);

        _stage.Children.Add(_dots);

        _skip = ViewKit.Pill("Skip", Finish, 200, "SmallPill");
        _skip.HorizontalAlignment = HorizontalAlignment.Right;
        _skip.VerticalAlignment = VerticalAlignment.Top;
        _skip.Margin = new Thickness(0, 92, 150, 0);
        _stage.Children.Add(_skip);

        _back = ViewKit.Pill("Back", () => Go(_index - 1), 240);
        _next = ViewKit.Pill("Next", () => Go(_index + 1), 420);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 150, 62) };
        _back.Margin = new Thickness(0, 0, 20, 0);
        buttons.Children.Add(_back);
        buttons.Children.Add(_next);
        _stage.Children.Add(buttons);

        Content = new Viewbox { Stretch = Stretch.Uniform, Child = _stage };
        Show(0, 0);
    }

    // ---------------------------------------------------------------- steps

    private void Go(int index)
    {
        if (index >= _steps.Count)
        {
            Finish();
            return;
        }
        if (index < 0 || index == _index) return;
        var direction = index > _index ? 1 : -1;
        _host.Audio.Play(direction > 0 ? SoundEffect.Select : SoundEffect.Back);
        Show(index, direction);
    }

    private void Show(int index, int direction)
    {
        _index = index;
        var step = _steps[index];
        _title.Text = step.Title;
        _lead.Text = step.Lead;
        _stepText.Text = $"STEP {index + 1} OF {_steps.Count}";
        _body.Content = step.Content();
        _next.Content = step.NextLabel;
        _back.Visibility = index == 0 ? Visibility.Hidden : Visibility.Visible;
        _skip.Visibility = index == _steps.Count - 1 ? Visibility.Hidden : Visibility.Visible;
        BuildDots();

        if (direction != 0 && !Anim.Reduced)
        {
            Anim.To(_bodyShift, TranslateTransform.XProperty, 0, 320, Anim.EaseOut, from: 90 * direction);
            Anim.To(_body, OpacityProperty, 1, 260, Anim.EaseOut, from: 0);
        }
    }

    private void BuildDots()
    {
        _dots.Children.Clear();
        for (var i = 0; i < _steps.Count; i++)
        {
            var dot = new Border { Width = i == _index ? 40 : 14, Height = 14, CornerRadius = new CornerRadius(7), Margin = new Thickness(6, 0, 6, 0) };
            dot.SetResourceReference(Border.BackgroundProperty, i == _index ? "AccentBrush" : "TrackBrush");
            _dots.Children.Add(dot);
        }
    }

    private void Finish()
    {
        _host.Settings.Current.WelcomeShown = true;
        _host.Settings.Current.TourVersion = CurrentVersion;
        _host.SaveSettings();
        _window.ReturnToMenu();
    }

    // ---------------------------------------------------------------- step content

    private UIElement BuildIntro()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Card("Everything you own", "Your programs, Steam and Epic games, Store apps and folders, all on one screen you can read from across the room.", 520));
        row.Children.Add(Card("Nothing is replaced", "Couchtop runs as a normal window. Your desktop, taskbar and files are exactly where you left them.", 520));
        row.Children.Add(Card("Made for a controller", "Big targets, a pointer that leans as you move it, and sounds for every step. A mouse works just as well.", 520));
        return row;
    }

    /// <summary>The live one: picking a shell applies it, and the tour itself is restyled underneath.</summary>
    private UIElement BuildShells()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var style in MenuStyleCatalog.All)
        {
            var current = _host.Settings.Current.MenuStyle == style.Id;
            var card = new Border
            {
                Width = 520,
                Margin = new Thickness(0, 0, 30, 0),
                Padding = new Thickness(0, 0, 0, 22),
                BorderThickness = new Thickness(current ? 5 : 2),
            };
            card.SetResourceReference(Border.CornerRadiusProperty, "CardCornerRadius");
            card.SetResourceReference(Border.BackgroundProperty, "CardBrush");
            card.SetResourceReference(Border.BorderBrushProperty, current ? "AccentBrush" : "PanelBorderBrush");

            var stack = new StackPanel();
            stack.Children.Add(ShellPreview(style.Id));
            var name = ViewKit.Text(style.Name, 36, FontWeights.ExtraBold, wrap: false, align: TextAlignment.Center);
            name.Margin = new Thickness(0, 16, 0, 4);
            stack.Children.Add(name);
            var description = ViewKit.Text(style.Description, 22, FontWeights.Normal, "SubtleTextBrush", align: TextAlignment.Center);
            description.Margin = new Thickness(24, 0, 24, 14);
            stack.Children.Add(description);
            var pick = ViewKit.Pill(current ? "Using this" : "Try it", () =>
            {
                _window.ApplyMenuStyle(style.Id);
                _host.SaveSettings();
                Show(_index, 0);
            }, 260, "SmallPill");
            pick.HorizontalAlignment = HorizontalAlignment.Center;
            pick.IsEnabled = !current;
            stack.Children.Add(pick);

            card.Child = stack;
            row.Children.Add(card);
        }
        return row;
    }

    /// <summary>A small drawing of each shell's shape: a grid of tiles, a row of tiles, a cross.</summary>
    private static UIElement ShellPreview(string styleId)
    {
        var canvas = new Canvas { Width = 452, Height = 186, Margin = new Thickness(26, 22, 26, 0), ClipToBounds = true };
        var back = new Rectangle { Width = 452, Height = 186, RadiusX = 6, RadiusY = 6 };
        back.Fill = new SolidColorBrush(styleId switch
        {
            MenuStyleCatalog.Dashboard => Color.FromRgb(0x0C, 0x11, 0x0E),
            MenuStyleCatalog.MediaBar => Color.FromRgb(0x10, 0x18, 0x30),
            _ => Color.FromRgb(0xEC, 0xF3, 0xF7),
        });
        canvas.Children.Add(back);

        var ink = new SolidColorBrush(styleId switch
        {
            MenuStyleCatalog.Dashboard => Color.FromRgb(0x7B, 0xC6, 0x18),
            MenuStyleCatalog.MediaBar => Color.FromRgb(0xCF, 0xE3, 0xFF),
            _ => Color.FromRgb(0x35, 0xB4, 0xE5),
        });
        var soft = new SolidColorBrush(styleId switch
        {
            MenuStyleCatalog.Dashboard => Color.FromRgb(0x25, 0x2B, 0x2D),
            MenuStyleCatalog.MediaBar => Color.FromRgb(0x22, 0x2E, 0x4A),
            _ => Color.FromRgb(0xC8, 0xDD, 0xE8),
        });

        void At(UIElement element, double x, double y)
        {
            Canvas.SetLeft(element, x);
            Canvas.SetTop(element, y);
            canvas.Children.Add(element);
        }

        switch (styleId)
        {
            case MenuStyleCatalog.Dashboard:
                for (var i = 0; i < 5; i++)
                    At(new Rectangle { Width = 52, Height = 16, RadiusX = 2, RadiusY = 2, Fill = i == 1 ? ink : soft }, 24 + i * 60, 20);
                for (var i = 0; i < 4; i++)
                    At(new Rectangle { Width = 88, Height = 78, Fill = soft, Stroke = i == 0 ? ink : null, StrokeThickness = 3 }, 24 + i * 100, 62);
                At(new Rectangle { Width = 140, Height = 12, RadiusX = 2, RadiusY = 2, Fill = soft }, 24, 154);
                break;
            case MenuStyleCatalog.MediaBar:
                for (var i = 0; i < 5; i++)
                    At(new Rectangle { Width = 28, Height = 28, RadiusX = 3, RadiusY = 3, Fill = i == 1 ? ink : soft, Opacity = i == 1 ? 1 : 0.6 }, 38 + i * 74, 42);
                for (var i = 0; i < 3; i++)
                    At(new Rectangle { Width = 24, Height = 24, RadiusX = 3, RadiusY = 3, Fill = soft, Opacity = i == 0 ? 1 : 0.45 }, 114, 86 + i * 36);
                At(new Rectangle { Width = 116, Height = 14, RadiusX = 3, RadiusY = 3, Fill = ink, Opacity = 0.9 }, 152, 91);
                break;
            default:
                for (var i = 0; i < 8; i++)
                    At(new Rectangle { Width = 92, Height = 52, RadiusX = 9, RadiusY = 9, Fill = i == 0 ? ink : soft }, 28 + i % 4 * 102, 28 + i / 4 * 62);
                At(new Rectangle { Width = 452, Height = 28, Fill = soft }, 0, 158);
                break;
        }
        return canvas;
    }

    private UIElement BuildApps()
    {
        var layout = _host.Layout.Layout;
        var apps = layout.Channels.Count(c => c.Kind != ChannelKind.BuiltIn);
        var games = layout.Channels.Count(c => c.Kind is ChannelKind.Steam or ChannelKind.Epic);

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Card(apps == 0 ? "Looking for your apps" : $"{apps} apps found",
            games > 0
                ? $"Including {games} from Steam and Epic, with their own artwork. New apps you install turn up on their own."
                : "From your Start menu and the Microsoft Store. New apps you install turn up on their own.", 520));
        row.Children.Add(Card("Arrange them", "Open Customize to drag things around, rename them, pick your own picture, or add a website or folder as an entry of its own.", 520));
        row.Children.Add(Card("Start them", "Pick one and Couchtop shows a start screen. Prefer it immediate? Settings › Display › Quick launch starts apps on the first click.", 520));
        return row;
    }

    private UIElement BuildBuiltIns()
    {
        var grid = new UniformGrid { Columns = 3, Rows = 2 };
        void Add(string title, string text) => grid.Children.Add(Card(title, text, 0, new Thickness(0, 0, 24, 20)));

        Add("Files", "Two panes, copy and move, and open anything in Explorer.");
        Add("Photos", "Your pictures, with a slideshow.");
        Add("Web", "A browser with bookmarks, built for a controller.");
        Add("Pals", _host.Settings.Current.MenuStyle == MenuStyleCatalog.Channels
            ? "A little 3D character that lives on the menu, reacts to what you do, and can walk onto your desktop."
            : "A 3D character that lives on the menu. Part of the Channels shell — switch to it to meet them.");
        Add("Couchtop Bar", "Couchtop's own taskbar: search, the clock and your open windows. Settings › Desktop.");
        Add("Search", $"Press {_host.Settings.Current.CommandPaletteHotkey} to find an app, a file or a setting from anywhere.");
        return grid;
    }

    private UIElement BuildControls()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var left = new StackPanel { Margin = new Thickness(0, 0, 24, 0) };
        left.Children.Add(Row("Move", "Mouse, arrow keys, left stick, or point a Wii Remote at a sensor bar"));
        left.Children.Add(Row("Select", "Click, Enter, or A"));
        left.Children.Add(Row("Back", "Right-click, Esc, or B"));
        left.Children.Add(Row("Quick Menu", $"{_host.Settings.Current.HomeMenuHotkey}, Guide, or HOME — over any game"));
        grid.Children.Add(left);

        var right = new StackPanel();
        right.Children.Add(Card("Over any app", "The Quick Menu drops in on top of whatever is running: switch windows, change the volume, close the app, or come back here.", 0, new Thickness(0, 0, 0, 20)));
        right.Children.Add(Card("Pair a Wii Remote", "Windows Settings › Bluetooth › Add device, then press 1+2 on the remote. Leave the PIN empty. Pointing needs an IR sensor bar.", 0));
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        return grid;
    }

    private UIElement BuildSafety()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Card("Leave any time", "The Desktop button on the menu hands you straight back to Windows, with Couchtop still running in the tray.", 520));
        row.Children.Add(Card("The emergency exit", "Ctrl + Alt + Shift + F12 always returns you to the normal Windows desktop, whatever is on screen.", 520));
        row.Children.Add(Card("Shell mode is opt-in", "Couchtop can replace Explorer as your desktop, watched by a guardian with a recovery tool on hand. Off until you turn it on in Settings › Shell Mode, and the beta asks you to try that in a virtual machine first.", 520));
        return row;
    }

    private UIElement BuildFinish()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Card("Change your mind", "Settings › Display switches shells whenever you like. Your apps, files and settings follow you between them.", 520));
        row.Children.Add(Card("Make it yours", "Themes, text size, reduced motion, your own music and sounds, and which screen Couchtop opens on.", 520));

        var last = new StackPanel { Width = 520 };
        last.Children.Add(Card("See this again", "Settings › About › Take the tour, any time.", 0, new Thickness(0, 0, 0, 20)));
        var settings = ViewKit.Pill("Open Settings", () =>
        {
            Finish();
            _window.Navigate(new SettingsView(_host, _window));
        }, 360);
        settings.HorizontalAlignment = HorizontalAlignment.Left;
        last.Children.Add(settings);
        row.Children.Add(last);
        return row;
    }

    // ---------------------------------------------------------------- pieces

    private static Border Card(string title, string text, double width, Thickness? margin = null)
    {
        var stack = new StackPanel();
        var heading = ViewKit.Text(title, 31, FontWeights.ExtraBold, wrap: false);
        heading.Margin = new Thickness(0, 0, 0, 10);
        stack.Children.Add(heading);
        stack.Children.Add(ViewKit.Text(text, 23, FontWeights.Normal, "SubtleTextBrush"));

        var card = ViewKit.Card(stack, new Thickness(28, 22, 28, 24), margin ?? new Thickness(0, 0, 26, 0));
        if (width > 0) card.Width = width;
        card.VerticalAlignment = VerticalAlignment.Top;
        return card;
    }

    private static UIElement Row(string action, string how)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 22) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var label = ViewKit.Text(action, 32, FontWeights.ExtraBold, "AccentDeepBrush", wrap: false);
        grid.Children.Add(label);
        var value = ViewKit.Text(how, 26, FontWeights.Normal, "SubtleTextBrush");
        Grid.SetColumn(value, 1);
        grid.Children.Add(value);
        return grid;
    }

    /// <summary>Snapshot rendering: jump straight to a step.</summary>
    internal void SnapshotStep(int index) => Show(Math.Clamp(index, 0, _steps.Count - 1), 0);

    // ---------------------------------------------------------------- screen

    public void OnShown() => Focus();

    public void OnHidden()
    {
    }

    /// <summary>Back leaves the tour, and counts as having seen it.</summary>
    public bool HandleBack()
    {
        if (_index == 0) return false;
        Go(_index - 1);
        return true;
    }

    public bool HandleKey(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Right or Key.Down or Key.PageDown: Go(_index + 1); return true;
            case Key.Left or Key.Up or Key.PageUp: Go(_index - 1); return true;
            case Key.Enter or Key.Space: Go(_index + 1); return true;
        }
        return false;
    }
}
