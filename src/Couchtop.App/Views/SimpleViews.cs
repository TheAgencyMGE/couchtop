using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Couchtop.App.Controls;
using Couchtop.App.Services;
using Couchtop.Core.Channels;
using Couchtop.Core.Launching;
using Couchtop.Core.Platform;

namespace Couchtop.App.Views;

/// <summary>The console-style start screen shown before an app channel starts ("Menu" / "Start").</summary>
public sealed class ChannelPreviewView : UserControl, IScreenView
{
    private readonly AppHost _host;
    private readonly MainWindow _window;
    private readonly Channel _channel;
    private readonly Button _start;
    private readonly ScaleTransform _artZoom = new(1, 1);
    private bool _launching;

    public ChannelPreviewView(AppHost host, MainWindow window, Channel channel)
    {
        _host = host;
        _window = window;
        _channel = channel;

        var stage = new Grid { Width = 1920, Height = 1080 };
        var art = ChannelArtFactory.Build(channel, host, large: true);
        art.RenderTransform = _artZoom;
        art.RenderTransformOrigin = new Point(0.5, 0.5);
        stage.Children.Add(new Border { Child = art, Margin = new Thickness(0, 0, 0, 250), ClipToBounds = true });

        var gloss = new Border { Height = 320, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false, Opacity = 0.45 };
        gloss.SetResourceReference(Border.BackgroundProperty, "GlossBrush");
        stage.Children.Add(gloss);

        var bar = new Grid { Height = 262, VerticalAlignment = VerticalAlignment.Bottom };
        var barBack = new Border { BorderThickness = new Thickness(0, 6, 0, 0) };
        barBack.SetResourceReference(Border.BackgroundProperty, "BarBrush");
        barBack.SetResourceReference(Border.BorderBrushProperty, "BarLineBrush");
        bar.Children.Add(barBack);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
        var back = ViewKit.Pill("Menu", window.ReturnToMenu, 540);
        _start = ViewKit.Pill("Start", Start, 540);
        foreach (var b in new[] { back, _start })
        {
            b.Height = 132;
            b.FontSize = 50;
            b.Margin = new Thickness(46, 0, 46, 0);
            buttons.Children.Add(b);
        }
        bar.Children.Add(buttons);
        stage.Children.Add(bar);

        Content = new Viewbox { Stretch = Stretch.Uniform, Child = stage };
    }

    public bool PlaysAmbience => true;

    public void OnShown()
    {
        if (!Anim.Reduced)
        {
            var zoom = new DoubleAnimation(1, 1.05, TimeSpan.FromSeconds(9)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = Anim.EaseInOut };
            _artZoom.BeginAnimation(ScaleTransform.ScaleXProperty, zoom);
            _artZoom.BeginAnimation(ScaleTransform.ScaleYProperty, zoom);
        }
        Dispatcher.BeginInvoke(() => _start.Focus(), System.Windows.Threading.DispatcherPriority.Input);
    }

    public void OnHidden()
    {
        _artZoom.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _artZoom.BeginAnimation(ScaleTransform.ScaleYProperty, null);
    }

    public bool HandleBack() => false;

    public bool HandleKey(KeyEventArgs e)
    {
        if (e.Key is Key.Left or Key.Right)
        {
            (Keyboard.FocusedElement as UIElement ?? _start).MoveFocus(new TraversalRequest(e.Key == Key.Left ? FocusNavigationDirection.Left : FocusNavigationDirection.Right));
            return true;
        }
        return false;
    }

    private async void Start()
    {
        if (_launching) return;
        _launching = true;
        var status = await ChannelActions.LaunchAsync(_window, _host, _channel);
        _launching = false;
        if (status == LaunchStatus.Started && _window.CurrentView == this) _window.ReturnToMenu();
    }
}

public sealed class PowerView : UserControl, IScreenView
{
    private readonly AppHost _host;
    private readonly MainWindow _window;

    public PowerView(AppHost host, MainWindow window)
    {
        _host = host;
        _window = window;

        var grid = new UniformGrid { Columns = 3, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
        grid.Children.Add(Tile("Sleep", "Rest the PC", "M 44,6 A 34,34 0 1 0 84,58 A 27,27 0 1 1 44,6 Z", filled: true, async () =>
        {
            if (await Confirm("Put the PC to sleep?", "Sleep")) PowerActions.Sleep();
        }));
        grid.Children.Add(Tile("Lock", "Lock the screen", "M 20,42 L 70,42 L 70,84 L 20,84 Z M 30,42 L 30,28 A 15,15 0 0 1 60,28 L 60,42", filled: false, () =>
        {
            PowerActions.Lock();
            return Task.CompletedTask;
        }));
        grid.Children.Add(Tile("Sign Out", "End this Windows session", "M 50,12 L 16,12 L 16,80 L 50,80 M 38,46 L 84,46 M 68,30 L 84,46 L 68,62", filled: false, async () =>
        {
            if (await Confirm("Sign out of Windows?\nUnsaved work in other apps may be lost.", "Sign Out")) PowerActions.SignOut();
        }));
        grid.Children.Add(Tile("Restart", "Restart the PC", "M 76,46 A 30,30 0 1 1 66,23 M 68,6 L 68,25 L 49,25", filled: false, async () =>
        {
            if (await Confirm("Restart the PC?\nUnsaved work in other apps may be lost.", "Restart"))
            {
                _host.SaveLayout(raiseChanged: false);
                PowerActions.Restart();
            }
        }));
        grid.Children.Add(Tile("Shut Down", "Turn the PC off", "M 30,22 A 32,32 0 1 0 62,22 M 46,6 L 46,44", filled: false, async () =>
        {
            if (await Confirm("Shut down the PC?\nUnsaved work in other apps may be lost.", "Shut Down"))
            {
                _host.SaveLayout(raiseChanged: false);
                PowerActions.ShutDown();
            }
        }));
        var exitTitle = host.IsShellSession ? "Windows Desktop" : "Exit Couchtop";
        var exitHelp = host.IsShellSession ? "Switch to Explorer for now" : "Return to the Windows desktop";
        grid.Children.Add(Tile(exitTitle, exitHelp, "M 10,16 L 82,16 L 82,76 L 10,76 Z M 10,30 L 82,30 M 30,46 L 62,46 M 30,60 L 52,60", filled: false, async () =>
        {
            var message = host.IsShellSession
                ? "Switch to the Windows desktop for this session?\nCouchtop will start again the next time you sign in (unless you turn Shell Mode off)."
                : "Exit Couchtop and return to the Windows desktop?";
            if (await Confirm(message, exitTitle)) _host.ExitToWindows();
        }));

        var body = new StackPanel();
        body.Children.Add(grid);
        var hint = ViewKit.Text("Emergency exit: Ctrl + Alt + Shift + F12 always returns to the Windows desktop.", 28, FontWeights.Normal, "SubtleTextBrush", align: TextAlignment.Center);
        hint.Margin = new Thickness(0, 40, 0, 0);
        body.Children.Add(hint);
        Content = ViewKit.Scaffold("Power", "What would you like to do?", body, window.ReturnToMenu);
    }

    private async Task<bool> Confirm(string message, string action) => await _window.ShowDialogAsync(message, action, "Cancel") == action;

    private static Button Tile(string title, string subtitle, string glyph, bool filled, Func<Task> action)
    {
        var stack = new StackPanel { Margin = new Thickness(10) };
        var path = new Path { Data = Geometry.Parse(glyph), Width = 96, Height = 96, Stretch = Stretch.Uniform, StrokeThickness = 8, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, Margin = new Thickness(0, 6, 0, 16) };
        if (filled) path.SetResourceReference(Shape.FillProperty, "AccentBrush");
        else path.SetResourceReference(Shape.StrokeProperty, "AccentBrush");
        stack.Children.Add(path);
        stack.Children.Add(ViewKit.Text(title, 42, FontWeights.ExtraBold, "ButtonTextBrush", wrap: false, align: TextAlignment.Center));
        stack.Children.Add(ViewKit.Text(subtitle, 24, FontWeights.Normal, "SubtleTextBrush", wrap: false, align: TextAlignment.Center));
        var button = new Button { Content = stack, Width = 520, Height = 290, Margin = new Thickness(22) };
        button.Click += async (_, _) => await action();
        return button;
    }

    public void OnShown() { }
    public void OnHidden() { }
    public bool HandleBack() => false;
}

public sealed class MessageBoardView : UserControl, IScreenView
{
    private readonly AppHost _host;
    private readonly StackPanel _list = new();

    public MessageBoardView(AppHost host, MainWindow window)
    {
        _host = host;
        var clear = ViewKit.Pill("Clear All", () =>
        {
            _host.Messages.Clear();
            Build();
        }, 240);
        Content = ViewKit.Scaffold("Message Board", "Notes from Couchtop", ViewKit.Scroll(_list), window.ReturnToMenu, clear);
        Build();
    }

    private void Build()
    {
        _list.Children.Clear();
        if (_host.Messages.Count == 0)
        {
            _list.Children.Add(ViewKit.Card(ViewKit.Text("No messages. New channels and important notices will appear here.", 32, FontWeights.Normal, "SubtleTextBrush")));
            return;
        }
        foreach (var message in _host.Messages)
        {
            var stack = new StackPanel();
            var header = new DockPanel();
            var time = ViewKit.Text(message.At.ToString("g"), 24, FontWeights.Normal, "SubtleTextBrush", wrap: false);
            DockPanel.SetDock(time, Dock.Right);
            header.Children.Add(time);
            header.Children.Add(ViewKit.Text(message.Title, 36, FontWeights.ExtraBold, "AccentDeepBrush", wrap: false));
            stack.Children.Add(header);
            stack.Children.Add(ViewKit.Text(message.Body, 28));
            _list.Children.Add(ViewKit.Card(stack, margin: new Thickness(0, 0, 24, 18)));
        }
    }

    public void OnShown()
    {
        _host.UnreadMessages = 0;
        _host.PostMessagesChanged();
    }

    public void OnHidden() { }
    public bool HandleBack() => false;
}
