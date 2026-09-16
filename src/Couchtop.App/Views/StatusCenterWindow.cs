using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Couchtop.App.Services;
using Couchtop.Core.Platform;

namespace Couchtop.App.Views;

/// <summary>
/// The status center: volume, battery, Wi-Fi and the Windows panels people need every day, plus the latest
/// Couchtop notifications. Opens from the Couchtop Bar and closes as soon as it loses focus.
/// </summary>
public sealed class StatusCenterWindow : Window
{
    private readonly AppHost _host;
    private readonly MainWindow _main;
    private readonly TextBlock _wifi;
    private readonly TextBlock _battery;
    private readonly StackPanel _notifications = new();
    private readonly Slider _volume;
    private bool _settingVolume;

    public StatusCenterWindow(AppHost host, MainWindow main)
    {
        _host = host;
        _main = main;

        Title = "Couchtop Status";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        SizeToContent = SizeToContent.Height;
        Width = 520;
        SetResourceReference(FontFamilyProperty, "AppFont");

        var stack = new StackPanel();
        stack.Children.Add(ViewKit.Text("Status", 34, FontWeights.ExtraBold, "AccentDeepBrush", wrap: false));

        // Volume
        var volumeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 4) };
        volumeRow.Children.Add(ViewKit.Text("Volume", 24, FontWeights.Bold, wrap: false));
        stack.Children.Add(volumeRow);
        _volume = new Slider { Minimum = 0, Maximum = 1, Width = 440, HorizontalAlignment = HorizontalAlignment.Left };
        var current = AudioService.GetSystemVolume();
        _volume.Value = current >= 0 ? current : 0.5;
        _volume.ValueChanged += (_, e) =>
        {
            if (_settingVolume) return;
            AudioService.SetSystemVolume((float)e.NewValue);
        };
        _volume.IsEnabled = current >= 0;
        stack.Children.Add(_volume);

        _battery = ViewKit.Text("", 22, FontWeights.Normal, "SubtleTextBrush", wrap: false);
        _battery.Margin = new Thickness(0, 10, 0, 0);
        stack.Children.Add(_battery);
        _wifi = ViewKit.Text("Checking Wi-Fi…", 22, FontWeights.Normal, "SubtleTextBrush", wrap: false);
        stack.Children.Add(_wifi);

        // Windows panels people still need.
        var grid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 14, 0, 0) };
        grid.Children.Add(Tile("Wi-Fi", WindowsSettings.Wifi));
        grid.Children.Add(Tile("Bluetooth", WindowsSettings.Bluetooth));
        grid.Children.Add(Tile("Display", WindowsSettings.Display));
        grid.Children.Add(Tile("Sound", WindowsSettings.Sound));
        grid.Children.Add(Tile("Power & sleep", WindowsSettings.Power));
        grid.Children.Add(Tile("Accessibility", WindowsSettings.Accessibility));
        stack.Children.Add(grid);

        var couchtop = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        couchtop.Children.Add(ViewKit.Pill("Couchtop Settings", () =>
        {
            Close();
            _main.OpenBuiltIn(Core.Channels.BuiltInChannels.Settings);
        }, 230, "SmallPill"));
        couchtop.Children.Add(ViewKit.Pill("Quick Menu", () =>
        {
            Close();
            _main.ToggleHomeMenu();
        }, 160, "SmallPill"));
        stack.Children.Add(couchtop);

        // Notifications
        var header = new DockPanel { Margin = new Thickness(0, 18, 0, 6) };
        var clear = ViewKit.Pill("Clear", () =>
        {
            _host.Messages.Clear();
            _host.UnreadMessages = 0;
            _host.PostMessagesChanged();
            BuildNotifications();
        }, 120, "SmallPill");
        DockPanel.SetDock(clear, Dock.Right);
        header.Children.Add(clear);
        header.Children.Add(ViewKit.Text("Notifications", 26, FontWeights.ExtraBold, wrap: false));
        stack.Children.Add(header);
        stack.Children.Add(_notifications);

        var panel = new Border { Child = stack, Padding = new Thickness(26, 22, 26, 22) };
        panel.SetResourceReference(FrameworkElement.StyleProperty, "AppPanel");
        Content = panel;

        Deactivated += (_, _) => Close();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            Close();
            e.Handled = true;
        };
    }

    private Button Tile(string text, string settingsPage)
    {
        var button = ViewKit.Pill(text, () =>
        {
            Close();
            if (!WindowsSettings.Open(settingsPage)) _main.ShowToast("Windows Settings could not be opened");
        }, 0, "SmallPill");
        button.MinWidth = 0;
        button.Margin = new Thickness(4);
        return button;
    }

    private void BuildNotifications()
    {
        _notifications.Children.Clear();
        if (_host.Messages.Count == 0)
        {
            _notifications.Children.Add(ViewKit.Text("Nothing new", 22, FontWeights.Normal, "SubtleTextBrush", wrap: false));
            return;
        }
        foreach (var message in _host.Messages.Take(4))
        {
            var item = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            item.Children.Add(ViewKit.Text(message.Title, 23, FontWeights.Bold, wrap: false));
            var body = ViewKit.Text(message.Body, 20, FontWeights.Normal, "SubtleTextBrush");
            body.MaxHeight = 60;
            item.Children.Add(body);
            _notifications.Children.Add(item);
        }
        if (_host.Messages.Count > 4)
        {
            _notifications.Children.Add(ViewKit.Pill($"All {_host.Messages.Count} messages", () =>
            {
                Close();
                _main.OpenBuiltInView(new MessageBoardView(_host, _main));
            }, 260, "SmallPill"));
        }
    }

    /// <summary>Shows the panel above the Couchtop Bar in the bottom-right corner.</summary>
    public void ShowPanel()
    {
        BuildNotifications();
        _battery.Text = PowerStatus.Read().Describe();
        _host.UnreadMessages = 0;
        _host.PostMessagesChanged();

        Show();
        var monitor = Monitors.Choose(Monitors.Enumerate(), _host.Settings.Current.TargetMonitor);
        if (monitor is not null)
        {
            var scale = monitor.Scale <= 0 ? 1 : monitor.Scale;
            Left = monitor.X / scale + monitor.Width / scale - Width - 16;
            Top = monitor.Y / scale + monitor.Height / scale - ActualHeight - CouchtopBar.BarHeight - 12;
        }
        Activate();
        _ = RefreshWifiAsync();
    }

    private async Task RefreshWifiAsync()
    {
        var status = await Wifi.ReadAsync();
        if (!IsLoaded) return;
        _wifi.Text = "Wi-Fi: " + status.Describe();
    }

    /// <summary>Fills the panel in for a visual regression render, without showing it.</summary>
    internal void SnapshotPrepare()
    {
        BuildNotifications();
        _battery.Text = "76%  ·  plugged in";
        _wifi.Text = "Wi-Fi: Home network  ·  82%";
        _settingVolume = true;
        _volume.Value = 0.65;
        _volume.IsEnabled = true;
        _settingVolume = false;
    }
}
