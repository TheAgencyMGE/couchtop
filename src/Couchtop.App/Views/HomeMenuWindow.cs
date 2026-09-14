using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Couchtop.App.Controls;
using Couchtop.App.Services;
using Couchtop.Core.Native;
using Couchtop.Core.Platform;
using Couchtop.Core.Safety;

namespace Couchtop.App.Views;

/// <summary>
/// The Quick Menu: a translucent overlay that can be summoned on top of any running app to get back to the
/// channel menu, close the app, switch windows, change volume, or reach Power and Settings.
/// </summary>
public sealed class HomeMenuWindow : Window
{
    private static readonly IntPtr HwndTopmost = new(-1);

    private readonly AppHost _host;
    private readonly MainWindow _main;
    private readonly IntPtr _target;
    private readonly bool _snapshot;
    private readonly TranslateTransform _topShift = new(0, -200);
    private readonly TranslateTransform _bottomShift = new(0, 280);
    private readonly ScaleTransform _centerScale = new(0.9, 0.9);
    private readonly Rectangle _scrim;
    private readonly StackPanel _center;
    private readonly StackPanel _windowStrip = new() { Orientation = Orientation.Horizontal };
    private readonly TextBlock _clock;
    private readonly TextBlock _controllers;
    private bool _closing;

    public HomeMenuWindow(AppHost host, MainWindow main, IntPtr target, bool snapshot = false)
    {
        _host = host;
        _main = main;
        _target = target;
        _snapshot = snapshot;

        Title = "Couchtop Quick Menu";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        SetResourceReference(FontFamilyProperty, "AppFont");
        if (snapshot)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = -32000;
            Top = -32000;
            Width = host.Options.SnapshotWidth;
            Height = host.Options.SnapshotHeight;
            ShowActivated = false;
        }

        var root = new Grid { ClipToBounds = true };
        _scrim = new Rectangle { Fill = new SolidColorBrush(Color.FromArgb(0xA8, 0x08, 0x10, 0x16)), Opacity = 0 };
        root.Children.Add(_scrim);
        var stage = new Grid { Width = 1920, Height = 1080 };

        // Top bar
        var top = new Canvas { Height = 160, VerticalAlignment = VerticalAlignment.Top, RenderTransform = _topShift };
        var topFill = new Rectangle { Width = 4400, Height = 160, Fill = new LinearGradientBrush(Color.FromArgb(0xF2, 0x3B, 0xB4, 0xEA), Color.FromArgb(0xF2, 0x1C, 0x7E, 0xBC), 90) };
        Canvas.SetLeft(topFill, -1240);
        top.Children.Add(topFill);
        var topLine = new Rectangle { Width = 4400, Height = 6, Fill = new SolidColorBrush(Color.FromArgb(0xE0, 0xFF, 0xFF, 0xFF)) };
        Canvas.SetLeft(topLine, -1240);
        Canvas.SetTop(topLine, 160);
        top.Children.Add(topLine);
        var title = new TextBlock { Text = "Quick Menu", FontSize = 68, FontWeight = FontWeights.ExtraBold, Foreground = Brushes.White };
        Canvas.SetLeft(title, 80);
        Canvas.SetTop(title, 36);
        top.Children.Add(title);
        var close = ViewKit.Pill("Close", CloseMenu, 230);
        Canvas.SetLeft(close, 1630);
        Canvas.SetTop(close, 30);
        top.Children.Add(close);
        stage.Children.Add(top);

        // Center actions
        _center = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 236, 0, 0), RenderTransform = _centerScale, RenderTransformOrigin = new Point(0.5, 0.5), Opacity = 0 };
        var row1 = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        row1.Children.Add(BigButton("Menu", GoToMenu));
        var closeApp = BigButton("Close App", CloseApp);
        closeApp.IsEnabled = target != IntPtr.Zero;
        row1.Children.Add(closeApp);
        _center.Children.Add(row1);
        if (target != IntPtr.Zero)
        {
            var current = ViewKit.Text("Current app: " + NativeMethods.GetWindowTitle(target), 26, FontWeights.Bold, "InverseTextBrush", wrap: false, align: TextAlignment.Center);
            current.HorizontalAlignment = HorizontalAlignment.Center;
            current.MaxWidth = 1200;
            current.Opacity = 0.85;
            _center.Children.Add(current);
        }
        var row2 = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 16, 0, 0) };
        if (host.IsShellSession) row2.Children.Add(ViewKit.Pill("Windows Desktop", OpenWindowsDesktop, 380));
        row2.Children.Add(ViewKit.Pill("Settings", () => OpenInMain(() => new SettingsView(_host, _main)), 300));
        row2.Children.Add(ViewKit.Pill("Power", () => OpenInMain(() => new PowerView(_host, _main)), 300));
        _center.Children.Add(row2);
        var switchLabel = ViewKit.Text("Switch to", 34, FontWeights.ExtraBold, "InverseTextBrush", wrap: false);
        switchLabel.Margin = new Thickness(10, 40, 0, 10);
        _center.Children.Add(switchLabel);
        _center.Children.Add(new ScrollViewer
        {
            Content = _windowStrip,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Width = 1700,
            Height = 230,
            Focusable = false,
        });
        stage.Children.Add(_center);

        // Bottom bar
        var bottom = new Canvas { Height = 210, VerticalAlignment = VerticalAlignment.Bottom, RenderTransform = _bottomShift };
        var bottomFill = new Rectangle { Width = 4400, Height = 600, Fill = new SolidColorBrush(Color.FromArgb(0xEB, 0x1A, 0x22, 0x28)) };
        Canvas.SetLeft(bottomFill, -1240);
        bottom.Children.Add(bottomFill);
        var bottomLine = new Rectangle { Width = 4400, Height = 5 };
        bottomLine.SetResourceReference(Shape.FillProperty, "AccentBrush");
        Canvas.SetLeft(bottomLine, -1240);
        bottom.Children.Add(bottomLine);
        var volumeLabel = new TextBlock { Text = "Volume", FontSize = 34, FontWeight = FontWeights.Bold, Foreground = Brushes.White };
        Canvas.SetLeft(volumeLabel, 110);
        Canvas.SetTop(volumeLabel, 36);
        bottom.Children.Add(volumeLabel);
        var systemVolume = AudioService.GetSystemVolume();
        var volume = new Slider { Width = 560, Minimum = 0, Maximum = 1, Value = Math.Max(0, systemVolume), IsEnabled = systemVolume >= 0 };
        volume.ValueChanged += (_, e) => AudioService.SetSystemVolume((float)e.NewValue);
        Canvas.SetLeft(volume, 100);
        Canvas.SetTop(volume, 96);
        bottom.Children.Add(volume);
        _controllers = new TextBlock { FontSize = 30, FontWeight = FontWeights.Bold, Foreground = Brushes.White, Opacity = 0.9 };
        Canvas.SetLeft(_controllers, 800);
        Canvas.SetTop(_controllers, 70);
        bottom.Children.Add(_controllers);
        _clock = new TextBlock { FontSize = 72, FontWeight = FontWeights.Medium, Foreground = Brushes.White, TextAlignment = TextAlignment.Right, Width = 520 };
        Canvas.SetLeft(_clock, 1920 - 90 - 520);
        Canvas.SetTop(_clock, 48);
        bottom.Children.Add(_clock);
        stage.Children.Add(bottom);

        root.Children.Add(new Viewbox { Stretch = Stretch.Uniform, Child = stage });
        Content = root;

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key is Key.Escape or Key.Home or Key.BrowserHome)
            {
                CloseMenu();
                e.Handled = true;
            }
        };
        PreviewMouseRightButtonDown += (_, e) =>
        {
            CloseMenu();
            e.Handled = true;
        };
        Deactivated += (_, _) =>
        {
            if (!_snapshot) CloseMenu();
        };
    }

    private static Button BigButton(string text, Action action)
    {
        var button = ViewKit.Pill(text, action, 600);
        button.Height = 150;
        button.FontSize = 52;
        button.Margin = new Thickness(30, 0, 30, 10);
        return button;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        if (_snapshot) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        var monitor = Monitors.ForWindow(_target != IntPtr.Zero ? _target : _main.Handle);
        if (monitor is not null)
            NativeMethods.SetWindowPos(hwnd, HwndTopmost, monitor.X, monitor.Y, monitor.Width, monitor.Height, NativeMethods.SWP_SHOWWINDOW);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _host.Audio.Play(SoundEffect.HomeOpen);
        Anim.To(_scrim, OpacityProperty, 1, 240);
        Anim.To(_topShift, TranslateTransform.YProperty, 0, 420, Anim.EaseOut);
        Anim.To(_bottomShift, TranslateTransform.YProperty, 0, 420, Anim.EaseOut);
        Anim.To(_center, OpacityProperty, 1, 300, delayMs: 80);
        Anim.To(_centerScale, ScaleTransform.ScaleXProperty, 1, 420, Anim.Springy, 80);
        Anim.To(_centerScale, ScaleTransform.ScaleYProperty, 1, 420, Anim.Springy, 80);

        _clock.Text = DateTime.Now.ToString("t");
        var input = _host.Input;
        var pads = input?.ConnectedControllers ?? 0;
        var battery = pads > 0 ? InputService.BatteryLevel(0) : null;
        _controllers.Text = $"Controllers: {pads}{(battery is { } b ? $" (battery {new string('▮', b + 1)}{new string('▯', 3 - b)})" : "")}    Wii Remote: {(input?.WiimoteConnected == true ? "connected" : "—")}";

        if (!_snapshot)
        {
            ForceForeground();
            Focus();
        }
        _ = LoadWindowsAsync();
    }

    /// <summary>Windows only lets the foreground process activate windows; a harmless Alt tap lifts that lock for controller-triggered opens.</summary>
    private void ForceForeground()
    {
        if (IsActive) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        const ushort VK_MENU = 0x12;
        var inputs = new[]
        {
            new NativeMethods.INPUT { type = NativeMethods.INPUT_KEYBOARD, U = new NativeMethods.InputUnion { ki = new NativeMethods.KEYBDINPUT { wVk = VK_MENU } } },
            new NativeMethods.INPUT { type = NativeMethods.INPUT_KEYBOARD, U = new NativeMethods.InputUnion { ki = new NativeMethods.KEYBDINPUT { wVk = VK_MENU, dwFlags = NativeMethods.KEYEVENTF_KEYUP } } },
        };
        NativeMethods.SendInput(2, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
        NativeMethods.SetForegroundWindow(hwnd);
        Activate();
    }

    private async Task LoadWindowsAsync()
    {
        if (_snapshot)
        {
            // Snapshot renders must never capture the user's real window titles.
            _windowStrip.Children.Add(ViewKit.Text("Your open windows appear here.", 30, FontWeights.Normal, "InverseTextBrush", wrap: false));
            return;
        }
        var windows = await Task.Run(() => WindowEnumerator.GetSwitchableWindows(Environment.ProcessId)
            .Take(16)
            .Select(w => (Window: w, Icon: ToBitmap(WindowEnumerator.GetIcon(w.Handle, 64))))
            .ToList());
        _windowStrip.Children.Clear();
        if (windows.Count == 0)
        {
            _windowStrip.Children.Add(ViewKit.Text("No other windows are open.", 30, FontWeights.Normal, "InverseTextBrush", wrap: false));
            return;
        }
        foreach (var (window, icon) in windows)
        {
            var stack = new StackPanel { Width = 250 };
            var image = ViewKit.IconImage(64);
            image.Source = icon;
            image.Margin = new Thickness(0, 4, 0, 8);
            stack.Children.Add(image);
            var label = ViewKit.Text(window.Title, 22, FontWeights.Bold, "ButtonTextBrush", align: TextAlignment.Center);
            label.MaxHeight = 58;
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            stack.Children.Add(label);
            var card = new Button { Content = stack, Width = 290, Height = 190, MinWidth = 0, Padding = new Thickness(10), Margin = new Thickness(10) };
            if (window.Handle == _target) card.SetResourceReference(BorderBrushProperty, "AccentBrush");
            var handle = window.Handle;
            card.Click += (_, _) =>
            {
                CloseMenu();
                WindowEnumerator.Activate(handle);
            };
            _windowStrip.Children.Add(card);
        }
    }

    private static BitmapSource? ToBitmap(RawImage? raw)
    {
        if (raw is null) return null;
        var bitmap = BitmapSource.Create(raw.Width, raw.Height, 96, 96, PixelFormats.Bgra32, null, raw.Pixels, raw.Width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    public void CloseMenu()
    {
        if (_closing) return;
        _closing = true;
        _host.Audio.Play(SoundEffect.HomeClose);
        Anim.To(_topShift, TranslateTransform.YProperty, -200, 220, Anim.EaseInOut);
        Anim.To(_bottomShift, TranslateTransform.YProperty, 280, 220, Anim.EaseInOut);
        Anim.To(_center, OpacityProperty, 0, 160);
        Anim.To(_scrim, OpacityProperty, 0, 240, completed: Close);
    }

    private void GoToMenu()
    {
        CloseMenu();
        _main.BringToFront();
        _main.GoHome();
    }

    private void CloseApp()
    {
        if (_target != IntPtr.Zero) WindowEnumerator.Close(_target);
        CloseMenu();
    }

    private void OpenWindowsDesktop()
    {
        try
        {
            if (!NativeMethods.IsExplorerShellRunning()) new ExplorerController().StartExplorer();
        }
        catch (Exception ex)
        {
            Core.Diagnostics.Log.Warn("Could not start Explorer", ex);
        }
        CloseMenu();
    }

    private void OpenInMain(Func<FrameworkElement> view)
    {
        CloseMenu();
        _main.BringToFront();
        _main.GoHome();
        _main.Navigate(view());
    }

    public static async Task RenderSnapshotAsync(AppHost host, MainWindow main, string path)
    {
        var window = new HomeMenuWindow(host, main, IntPtr.Zero, snapshot: true);
        window.Show();
        await Task.Delay(1400);
        var width = host.Options.SnapshotWidth;
        var height = host.Options.SnapshotHeight;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new VisualBrush(main.RootGrid), null, new Rect(0, 0, width, height));
            dc.DrawRectangle(new VisualBrush((Visual)window.Content), null, new Rect(0, 0, width, height));
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        await using (var fs = File.Create(path)) encoder.Save(fs);
        window._closing = true;
        window.Close();
    }
}
