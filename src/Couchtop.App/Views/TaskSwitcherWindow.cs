using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Couchtop.App.Services;
using Couchtop.Core.Platform;
using Couchtop.Core.Shell;

namespace Couchtop.App.Views;

/// <summary>
/// The task switcher: a console-style overlay of every open window. Tab or the shortcut key steps forward,
/// Shift+Tab steps back, arrows and a controller's stick move the highlight, Enter or A switches.
/// </summary>
public sealed class TaskSwitcherWindow : Window
{
    private readonly AppHost _host;
    private readonly MainWindow _main;
    private readonly WrapPanel _items = new() { HorizontalAlignment = HorizontalAlignment.Center };
    private readonly List<(Border Card, ManagedWindow Window)> _cards = new();
    private readonly TextBlock _title = new() { FontSize = 30, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 18, 0, 0) };
    private int _index;

    public TaskSwitcherWindow(AppHost host, MainWindow main)
    {
        _host = host;
        _main = main;

        Title = "Couchtop Task Switcher";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        SetResourceReference(FontFamilyProperty, "AppFont");

        var scrim = new Rectangle();
        scrim.SetResourceReference(Shape.FillProperty, "QuickScrimBrush");
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var heading = ViewKit.Text("Switch app", 44, FontWeights.ExtraBold, "InverseTextBrush", wrap: false, align: TextAlignment.Center);
        heading.Margin = new Thickness(0, 0, 0, 22);
        panel.Children.Add(heading);
        panel.Children.Add(new Border { Child = _items, MaxWidth = 1500 });
        _title.SetResourceReference(TextBlock.ForegroundProperty, "QuickTextBrush");
        panel.Children.Add(_title);
        var hint = ViewKit.Text("Tab next  ·  Shift+Tab back  ·  Enter switch  ·  Esc cancel", 24, FontWeights.Normal, "QuickTextBrush", wrap: false, align: TextAlignment.Center);
        hint.Opacity = 0.75;
        hint.Margin = new Thickness(0, 14, 0, 0);
        panel.Children.Add(hint);

        var root = new Grid();
        root.Children.Add(scrim);
        root.Children.Add(panel);
        Content = root;

        PreviewKeyDown += OnKeyDown;
        PreviewKeyUp += OnKeyUp;
        Deactivated += (_, _) => Cancel();
        MouseDown += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Right) Cancel();
        };
    }

    /// <summary>Shows the switcher on the Couchtop screen with the previously used window already highlighted.</summary>
    public bool ShowSwitcher()
    {
        _host.Desktop.Poll();
        var windows = _host.Desktop.Windows.Windows;
        if (windows.Count == 0) return false;

        var monitor = Monitors.Choose(Monitors.Enumerate(), _host.Settings.Current.TargetMonitor);
        if (monitor is not null)
        {
            var scale = monitor.Scale <= 0 ? 1 : monitor.Scale;
            Left = monitor.X / scale;
            Top = monitor.Y / scale;
            Width = monitor.Width / scale;
            Height = monitor.Height / scale;
        }

        Build(windows);
        _index = windows.Count > 1 ? 1 : 0;
        Highlight();
        Show();
        Activate();
        return true;
    }

    /// <summary>Fills the switcher in for a visual regression render, without showing it.</summary>
    internal void SnapshotPrepare()
    {
        _host.Desktop.Poll();
        Build(_host.Desktop.Windows.Windows);
        _index = _cards.Count > 1 ? 1 : 0;
        Highlight();
    }

    private void Build(IReadOnlyList<ManagedWindow> windows)
    {
        _items.Children.Clear();
        _cards.Clear();
        foreach (var window in windows)
        {
            var stack = new StackPanel { Width = 230, Margin = new Thickness(10) };
            var icon = _host.Desktop.IconFor(window);
            var image = new Image { Width = 56, Height = 56, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 12), Source = icon };
            stack.Children.Add(image);
            var name = ViewKit.Text(window.AppName, 24, FontWeights.ExtraBold, "TextBrush", wrap: false, align: TextAlignment.Center);
            stack.Children.Add(name);
            var caption = ViewKit.Text(window.Title, 19, FontWeights.Normal, "SubtleTextBrush", wrap: false, align: TextAlignment.Center);
            caption.TextTrimming = TextTrimming.CharacterEllipsis;
            caption.Margin = new Thickness(6, 2, 6, 12);
            stack.Children.Add(caption);

            var card = new Border
            {
                Child = stack,
                CornerRadius = new CornerRadius(24),
                BorderThickness = new Thickness(5),
                Margin = new Thickness(6),
                Cursor = Cursors.Hand,
            };
            card.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
            card.SetResourceReference(Border.BorderBrushProperty, "PanelBorderBrush");
            var captured = window;
            card.MouseLeftButtonUp += (_, _) => Choose(captured);
            card.MouseEnter += (_, _) =>
            {
                _index = _cards.FindIndex(c => c.Window.Handle == captured.Handle);
                Highlight();
            };
            _cards.Add((card, window));
            _items.Children.Add(card);
        }
    }

    private void Highlight()
    {
        if (_cards.Count == 0) return;
        _index = ((_index % _cards.Count) + _cards.Count) % _cards.Count;
        for (var i = 0; i < _cards.Count; i++)
        {
            var (card, _) = _cards[i];
            card.SetResourceReference(Border.BorderBrushProperty, i == _index ? "AccentBrush" : "PanelBorderBrush");
            card.RenderTransformOrigin = new Point(0.5, 0.5);
            card.RenderTransform = new ScaleTransform(i == _index ? 1.06 : 1, i == _index ? 1.06 : 1);
        }
        _title.Text = _cards[_index].Window.Title;
    }

    /// <summary>Steps the highlight; used by the hotkey (press again to keep moving) and the arrow keys.</summary>
    public void Step(int delta)
    {
        _index += delta;
        Highlight();
        _host.Audio.Play(SoundEffect.Hover);
    }

    private void Choose(ManagedWindow window)
    {
        Close();
        _host.Desktop.Activate(window);
    }

    private void Cancel() => Close();

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Cancel();
                break;
            case Key.Tab:
            case Key.Right:
            case Key.Down:
                Step(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && e.Key == Key.Tab ? -1 : 1);
                break;
            case Key.Left:
            case Key.Up:
                Step(-1);
                break;
            case Key.Enter:
            case Key.Space:
                if (_cards.Count > 0) Choose(_cards[_index].Window);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private void OnKeyUp(object sender, KeyEventArgs e)
    {
        // Releasing the modifier confirms the choice, the way Alt+Tab does.
        if (e.Key is not (Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.System)) return;
        if (Keyboard.Modifiers is ModifierKeys.None && _cards.Count > 0) Choose(_cards[_index].Window);
    }
}
