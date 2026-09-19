using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Couchtop.App.Services;
using Couchtop.Core.Diagnostics;
using Couchtop.Core.Native;
using Couchtop.Core.Platform;
using Couchtop.Core.Shell;

namespace Couchtop.App.Views;

/// <summary>
/// The Couchtop Bar: the always-there strip along the bottom of the screen with the Couchtop button, search,
/// the running apps, and the clock and status buttons. It registers as a desktop toolbar so maximized apps
/// stay clear of it, exactly like the Windows taskbar.
/// </summary>
public sealed class CouchtopBar : Window
{
    public const int BarHeight = 62;
    private const uint CallbackMessage = NativeMethods.WM_USER + 30;

    private readonly AppHost _host;
    private readonly MainWindow _main;
    private readonly StackPanel _apps = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _tray = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
    private readonly DispatcherTimer _trayPrune = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(20) };
    private readonly TextBlock _clock = new() { FontSize = 26, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _date = new() { FontSize = 17, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 0, 0) };
    private readonly TextBlock _badge = new() { FontSize = 16, FontWeight = FontWeights.ExtraBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _badgeHost;
    private readonly DispatcherTimer _clockTimer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(10) };
    private readonly Image _palFace = new() { Width = 40, Height = 40, Clip = new EllipseGeometry(new Point(20, 20), 19, 19) };
    private readonly Button _palButton;
    private Pals.PalBarBubble? _palBubble;
    private AppBarDock? _dock;
    private IntPtr _hwnd;
    private bool _closing;

    public CouchtopBar(AppHost host, MainWindow main)
    {
        _host = host;
        _main = main;

        Title = "Couchtop Bar";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        SetResourceReference(FontFamilyProperty, "AppFont");
        SetResourceReference(BackgroundProperty, "BarBrush");

        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var top = new Border { BorderThickness = new Thickness(0, 3, 0, 0), VerticalAlignment = VerticalAlignment.Top, Height = 3 };
        top.SetResourceReference(Border.BorderBrushProperty, "BarLineBrush");
        Grid.SetColumnSpan(top, 3);
        root.Children.Add(top);

        // Left: Couchtop home and search.
        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 6, 0) };
        left.Children.Add(BarButton("Couchtop", "Show the Couchtop menu (or press the Home hotkey)", GoHome, accent: true));
        left.Children.Add(IconButton(
            "M 26,26 L 38,38 M 4,17 A 13,13 0 1 0 30,17 A 13,13 0 1 0 4,17",
            "Search apps, files and settings", () => _main.OpenCommandPalette()));
        root.Children.Add(left);

        // Middle: running apps.
        var scroller = new ScrollViewer
        {
            Content = _apps,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false,
            VerticalAlignment = VerticalAlignment.Center,
        };
        scroller.PreviewMouseWheel += (_, e) =>
        {
            scroller.ScrollToHorizontalOffset(scroller.HorizontalOffset - e.Delta);
            e.Handled = true;
        };
        Grid.SetColumn(scroller, 1);
        root.Children.Add(scroller);

        // Right: tray icons, status, notifications, clock, power.
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 12, 0) };
        right.Children.Add(_tray);

        // The user's Pal: opens Pal Studio, and speaks up from here while other apps are in front.
        _palButton = new Button { Content = _palFace, Width = 52, Height = 46, Padding = new Thickness(0), ToolTip = "Your Pal" };
        Style(_palButton);
        _palButton.Click += (_, _) =>
        {
            _main.BringToFront();
            _main.OpenBuiltIn(Core.Channels.BuiltInChannels.Pals);
        };
        _palButton.MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            var prefs = _host.Pals.Preferences;
            var menu = new ContextMenu();
            var visit = new MenuItem { Header = prefs.DesktopVisits ? "Keep my Pal in Couchtop" : "Let my Pal out on the desktop" };
            visit.Click += (_, _) =>
            {
                prefs.DesktopVisits = !prefs.DesktopVisits;
                _host.Pals.SavePreferences();
            };
            menu.Items.Add(visit);
            menu.PlacementTarget = _palButton;
            menu.IsOpen = true;
        };
        right.Children.Add(_palButton);
        UpdatePal();
        _host.Pals.ProfileChanged += UpdatePal;
        _host.Pals.Reacted += OnPalReacted;

        right.Children.Add(IconButton(
            "M 4,22 L 12,22 L 24,10 L 24,42 L 12,30 L 4,30 Z M 31,16 A 14,14 0 0 1 31,36 M 37,9 A 22,22 0 0 1 37,43",
            "Volume, battery, Wi-Fi and notifications", () => _main.OpenStatusCenter()));

        var bell = new Grid();
        bell.Children.Add(IconButton(
            "M 24,6 A 12,12 0 0 1 36,18 L 36,28 L 41,36 L 7,36 L 12,28 L 12,18 A 12,12 0 0 1 24,6 Z M 19,40 A 5,5 0 0 0 29,40",
            "Notifications and messages", OpenNotifications));
        _badgeHost = new Border
        {
            MinWidth = 22,
            Height = 22,
            CornerRadius = new CornerRadius(11),
            Padding = new Thickness(5, 0, 5, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 2, 0),
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
            Child = _badge,
        };
        _badgeHost.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        _badge.SetResourceReference(TextBlock.ForegroundProperty, "InverseTextBrush");
        bell.Children.Add(_badgeHost);
        right.Children.Add(bell);

        var clockStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0) };
        _clock.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        _date.SetResourceReference(TextBlock.ForegroundProperty, "SubtleTextBrush");
        clockStack.Children.Add(_clock);
        clockStack.Children.Add(_date);
        var clockButton = new Button { Content = clockStack, Padding = new Thickness(6, 0, 6, 0), ToolTip = "Clock and calendar" };
        Style(clockButton);
        clockButton.Click += (_, _) => _main.OpenCalendar();
        right.Children.Add(clockButton);

        right.Children.Add(IconButton(
            "M 16,10 A 18,18 0 1 0 32,10 M 24,4 L 24,24",
            "Power", () => _main.OpenBuiltIn(Core.Channels.BuiltInChannels.Power)));
        Grid.SetColumn(right, 2);
        root.Children.Add(right);

        Content = root;
        _clockTimer.Tick += (_, _) => UpdateClock();
        UpdateClock();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        var style = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE,
            new IntPtr(style | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE));

        _dock = new AppBarDock(_hwnd, CallbackMessage);
        if (_host.Settings.Current.BarReservesSpace) _dock.Register();
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
        Reposition();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Windows tells desktop toolbars when the screen layout or full-screen state changed.
        if (msg == CallbackMessage) Dispatcher.BeginInvoke(Reposition, DispatcherPriority.Background);
        return IntPtr.Zero;
    }

    /// <summary>Puts the bar along the bottom of the Couchtop monitor and reserves that strip.</summary>
    public void Reposition()
    {
        if (_closing || _hwnd == IntPtr.Zero) return;
        var monitors = Monitors.Enumerate();
        var monitor = Monitors.Choose(monitors, _host.Settings.Current.TargetMonitor);
        if (monitor is null) return;

        var height = (int)Math.Round(BarHeight * monitor.Scale);
        var rect = new NativeMethods.RECT { Left = monitor.X, Top = monitor.Y + monitor.Height - height, Right = monitor.X + monitor.Width, Bottom = monitor.Y + monitor.Height };
        if (_dock is { IsRegistered: true } dock && _host.Settings.Current.BarReservesSpace)
            rect = dock.Reserve(DockEdge.Bottom, rect.Left, rect.Top, rect.Right, rect.Bottom);

        NativeMethods.SetWindowPos(_hwnd, IntPtr.Zero, rect.Left, rect.Top, rect.Width, rect.Height,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
    }

    /// <summary>Fills the bar in for a visual regression render, without showing it or claiming screen space.</summary>
    internal void SnapshotPrepare()
    {
        _host.Desktop.Poll();
        Rebuild();
        UpdateClock();
        UpdateBadge();
    }

    public void Start()
    {
        _host.Desktop.Changed += Rebuild;
        _host.MessagesChanged += OnMessagesChanged;
        _host.Desktop.AddListener();
        _clockTimer.Start();
        if (_host.Tray is { } tray)
        {
            tray.Changed += RebuildTray;
            _trayPrune.Tick += (_, _) => tray.PruneDeadOwners();
            _trayPrune.Start();
            RebuildTray();
        }
        Rebuild();
        UpdateBadge();
    }

    /// <summary>Shows the background apps' notification icons (shell mode only; Explorer owns them otherwise).</summary>
    private void RebuildTray()
    {
        _tray.Children.Clear();
        if (_host.Tray is not { } tray) return;
        foreach (var item in tray.Items.Where(i => !i.Hidden))
        {
            var image = new Image
            {
                Width = 20,
                Height = 20,
                Source = ViewKit.ToBitmap(Core.Native.ShellImageLoader.FromHIcon(item.Icon, 20)),
                VerticalAlignment = VerticalAlignment.Center,
            };
            var button = new Button { Content = image, Width = 34, Height = 40, Padding = new Thickness(0), ToolTip = string.IsNullOrEmpty(item.Tooltip) ? "Background app" : item.Tooltip };
            Style(button);
            var captured = item;
            button.Click += (_, _) => tray.Click(captured, rightButton: false);
            button.MouseRightButtonUp += (_, _) => tray.Click(captured, rightButton: true);
            _tray.Children.Add(button);
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _closing = true;
        _clockTimer.Stop();
        _trayPrune.Stop();
        if (_host.Tray is { } tray) tray.Changed -= RebuildTray;
        _host.Desktop.Changed -= Rebuild;
        _host.MessagesChanged -= OnMessagesChanged;
        _host.Pals.ProfileChanged -= UpdatePal;
        _host.Pals.Reacted -= OnPalReacted;
        _palBubble?.Close();
        _host.Desktop.RemoveListener();
        _dock?.Dispose();
        _dock = null;
        base.OnClosing(e);
    }

    private void OnMessagesChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(UpdateBadge);

    private void UpdateBadge()
    {
        var unread = _host.UnreadMessages;
        _badge.Text = unread > 9 ? "9+" : unread.ToString();
        _badgeHost.Visibility = unread > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        _clock.Text = now.ToString(_host.Settings.Current.Clock24Hour ? "HH:mm" : "h:mm tt");
        _date.Text = now.ToString("ddd d MMM");
    }

    // ---------------------------------------------------------------- running apps

    private void Rebuild()
    {
        // Step aside for full-screen games and videos, the way a taskbar does.
        var hide = _host.Desktop.ForegroundIsFullScreen;
        if (hide == IsVisible)
        {
            if (hide) Hide();
            else Show();
        }

        _apps.Children.Clear();
        foreach (var group in _host.Desktop.Windows.Groups)
        {
            var window = group.Primary;
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            if (_host.Desktop.IconFor(window) is { } icon)
            {
                content.Children.Add(new Image
                {
                    Source = icon,
                    Width = 26,
                    Height = 26,
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                });
            }
            var label = new TextBlock
            {
                Text = group.Windows.Count > 1 ? $"{Shorten(window.Title)}  ({group.Windows.Count})" : Shorten(window.Title),
                FontSize = 19,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 190,
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, "ButtonTextBrush");
            content.Children.Add(label);

            var button = new ToggleButton
            {
                Content = content,
                IsChecked = window.Handle == _host.Desktop.Windows.Foreground,
                Padding = new Thickness(12, 0, 14, 0),
                Height = 46,
                Margin = new Thickness(3, 0, 3, 0),
                ToolTip = window.Title,
            };
            Style(button);
            var captured = group;
            button.Click += (_, _) => _host.Desktop.Toggle(captured.Primary);
            button.MouseDown += (_, e) =>
            {
                if (e.ChangedButton != MouseButton.Middle) return;
                _host.Desktop.Close(captured.Primary);
                e.Handled = true;
            };
            button.ContextMenu = BuildMenu(captured);
            _apps.Children.Add(button);
        }

        if (_apps.Children.Count != 0) return;
        var empty = ViewKit.Text("No apps open", 19, FontWeights.Normal, "SubtleTextBrush", wrap: false);
        empty.VerticalAlignment = VerticalAlignment.Center;
        empty.Margin = new Thickness(14, 0, 0, 0);
        _apps.Children.Add(empty);
    }

    private ContextMenu BuildMenu(WindowGroup group)
    {
        var menu = new ContextMenu();
        var window = group.Primary;
        menu.Items.Add(MenuItem("Bring to front", () => _host.Desktop.Activate(window)));
        menu.Items.Add(MenuItem("Minimize", () => _host.Desktop.Minimize(window)));
        menu.Items.Add(MenuItem(window.Maximized ? "Restore" : "Maximize", () => _host.Desktop.Maximize(window)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem("Snap left", () => _host.Desktop.Snap(window, WindowSnap.Left)));
        menu.Items.Add(MenuItem("Snap right", () => _host.Desktop.Snap(window, WindowSnap.Right)));
        if (Monitors.Enumerate().Count > 1) menu.Items.Add(MenuItem("Move to next screen", () => _host.Desktop.SendToNextMonitor(window)));
        if (group.Windows.Count > 1)
        {
            menu.Items.Add(new Separator());
            foreach (var other in group.Windows)
            {
                var captured = other;
                menu.Items.Add(MenuItem(Shorten(other.Title), () => _host.Desktop.Activate(captured)));
            }
        }
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem(group.Windows.Count > 1 ? "Close all" : "Close", () =>
        {
            WindowActions.CloseGroup(group);
            _host.Desktop.Poll();
        }));
        return menu;
    }

    private static MenuItem MenuItem(string text, Action action)
    {
        var item = new MenuItem { Header = text, FontSize = 20 };
        item.Click += (_, _) => action();
        return item;
    }

    private static string Shorten(string title) => title.Length <= 34 ? title : title[..32].TrimEnd() + "…";

    // ---------------------------------------------------------------- buttons

    private void GoHome()
    {
        _main.GoHome();
        _main.BringToFront();
    }

    private void OpenNotifications()
    {
        _main.BringToFront();
        _main.OpenBuiltInView(new MessageBoardView(_host, _main));
    }

    private Button BarButton(string text, string tooltip, Action action, bool accent = false)
    {
        var label = new TextBlock { Text = text, FontSize = 21, FontWeight = FontWeights.ExtraBold, VerticalAlignment = VerticalAlignment.Center };
        label.SetResourceReference(TextBlock.ForegroundProperty, accent ? "AccentDeepBrush" : "ButtonTextBrush");
        var button = new Button { Content = label, Height = 46, Padding = new Thickness(16, 0, 16, 0), ToolTip = tooltip };
        Style(button);
        button.Click += (_, _) => action();
        return button;
    }

    private Button IconButton(string geometry, string tooltip, Action action)
    {
        var path = new Path
        {
            Data = Geometry.Parse(geometry),
            Width = 24,
            Height = 24,
            Stretch = Stretch.Uniform,
            StrokeThickness = 4,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        };
        path.SetResourceReference(Shape.StrokeProperty, "AccentBrush");
        var button = new Button { Content = path, Width = 48, Height = 46, ToolTip = tooltip, Padding = new Thickness(0) };
        Style(button);
        button.Click += (_, _) => action();
        return button;
    }

    private void UpdatePal()
    {
        if (_host.Pals.Profile is not { } profile)
        {
            _palButton.Visibility = Visibility.Collapsed;
            return;
        }
        _palButton.Visibility = Visibility.Visible;
        _palButton.ToolTip = profile.Name + " (edit in Pals)";
        _palFace.Source = Pals.PalPortrait.Render(profile, 40, 40, Pals.AvatarFraming.Face);
    }

    private void OnPalReacted(Core.Pals.PalReaction reaction, Pals.PalStage stage)
    {
        if (stage != Pals.PalStage.Bar || _closing || !IsVisible || reaction.Text is null) return;
        _palBubble ??= new Pals.PalBarBubble(_host);
        // Anchor the bubble's corner just above the Pal button, in screen units.
        var corner = _palButton.PointToScreen(new Point(_palButton.ActualWidth + 40, 0));
        if (PresentationSource.FromVisual(this)?.CompositionTarget is { } target) corner = target.TransformFromDevice.Transform(corner);
        _palBubble.ShowLine(reaction, corner);
    }

    /// <summary>Bar buttons are flat and compact rather than the big console pills used inside Couchtop.</summary>
    private static void Style(ButtonBase button)
    {
        button.SetResourceReference(FrameworkElement.StyleProperty, "BarButton");
        button.Focusable = false;
    }
}
