using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using Couchtop.App.Services;
using Couchtop.Core.Channels;
using Couchtop.Core.Diagnostics;
using Couchtop.Core.Discovery;
using Couchtop.Core.Platform;
using Couchtop.Core.Settings;
using Couchtop.Core.Shell;

namespace Couchtop.App.Views;

public sealed class SettingsView : UserControl, IScreenView
{
    private static readonly string[] Categories = { "Display", "Sound", "Controls", "Channels", "Shell Mode", "Data", "About" };

    private readonly AppHost _host;
    private readonly MainWindow _window;
    private readonly StackPanel _rows = new();
    private readonly ScrollViewer _scroll;
    private readonly List<RadioButton> _tabs = new();
    private string _category = Categories[0];
    private bool _suppressTabs;

    public SettingsView(AppHost host, MainWindow window)
    {
        _host = host;
        _window = window;

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(440) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var tabs = new StackPanel();
        foreach (var category in Categories)
        {
            var captured = category;
            var tab = new RadioButton { Content = category, GroupName = "SettingsTabs" + GetHashCode() };
            tab.SetResourceReference(StyleProperty, "TabPill");
            tab.Checked += (_, _) =>
            {
                if (!_suppressTabs) SelectCategory(captured);
            };
            tabs.Children.Add(tab);
            _tabs.Add(tab);
        }
        body.Children.Add(tabs);

        _scroll = ViewKit.Scroll(_rows);
        Grid.SetColumn(_scroll, 2);
        body.Children.Add(_scroll);

        Content = ViewKit.Scaffold("Settings", "Couchtop System Settings", body, window.ReturnToMenu);
        SelectCategory(Categories[0]);
    }

    private UserSettings S => _host.Settings.Current;

    public void OnShown() { }
    public void OnHidden() { }
    public bool HandleBack() => false;

    public void SelectCategory(string category)
    {
        _category = category;
        _suppressTabs = true;
        foreach (var tab in _tabs) tab.IsChecked = (string)tab.Content == category;
        _suppressTabs = false;
        _rows.Children.Clear();
        _scroll.ScrollToTop();
        switch (category)
        {
            case "Display": BuildDisplay(); break;
            case "Sound": BuildSound(); break;
            case "Controls": BuildControls(); break;
            case "Channels": BuildChannels(); break;
            case "Shell Mode": BuildShell(); break;
            case "Data": BuildData(); break;
            default: BuildAbout(); break;
        }
    }

    private void Refresh() => SelectCategory(_category);

    // ---------------------------------------------------------------- row helpers

    private void Header(string text)
    {
        var header = ViewKit.Text(text, 42, FontWeights.ExtraBold, "AccentDeepBrush");
        header.Margin = new Thickness(8, 4, 0, 16);
        _rows.Children.Add(header);
    }

    private void Row(string label, string? help, UIElement control)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(540) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 24, 0) };
        left.Children.Add(ViewKit.Text(label, 32, FontWeights.Bold));
        if (!string.IsNullOrEmpty(help)) left.Children.Add(ViewKit.Text(help, 23, FontWeights.Normal, "SubtleTextBrush"));
        grid.Children.Add(left);
        if (control is FrameworkElement fe)
        {
            fe.VerticalAlignment = VerticalAlignment.Center;
            fe.HorizontalAlignment = HorizontalAlignment.Left;
        }
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        _rows.Children.Add(ViewKit.Card(grid, margin: new Thickness(0, 0, 24, 16)));
    }

    private void Save(Action? after = null)
    {
        _host.SaveSettings();
        after?.Invoke();
    }

    private void Toggle(string label, string? help, Func<bool> get, Action<bool> set, Action? after = null)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        ToggleButton on = null!, off = null!;
        on = ViewKit.Option("On", get(), () =>
        {
            set(true);
            on.IsChecked = true;
            off.IsChecked = false;
            Save(after);
        });
        off = ViewKit.Option("Off", !get(), () =>
        {
            set(false);
            on.IsChecked = false;
            off.IsChecked = true;
            Save(after);
        });
        panel.Children.Add(on);
        panel.Children.Add(off);
        Row(label, help, panel);
    }

    private void Choice(string label, string? help, IReadOnlyList<(string Text, string Value)> options, Func<string> get, Action<string> set, Action? after = null)
    {
        var wrap = new WrapPanel();
        var buttons = new List<ToggleButton>();
        foreach (var (text, value) in options)
        {
            ToggleButton button = null!;
            button = ViewKit.Option(text, get() == value, () =>
            {
                set(value);
                foreach (var b in buttons) b.IsChecked = ReferenceEquals(b, button);
                Save(after);
            });
            buttons.Add(button);
            wrap.Children.Add(button);
        }
        Row(label, help, wrap);
    }

    private void SliderRow(string label, string? help, double min, double max, Func<double> get, Action<double> set, Func<double, string> format, bool persist = true)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var slider = new Slider { Minimum = min, Maximum = max, Value = Math.Clamp(get(), min, max), Width = 560, SmallChange = (max - min) / 20, LargeChange = (max - min) / 10 };
        var value = ViewKit.Text(format(slider.Value), 30, FontWeights.Bold, wrap: false);
        value.Margin = new Thickness(30, 0, 0, 0);
        value.MinWidth = 120;
        value.VerticalAlignment = VerticalAlignment.Center;
        slider.ValueChanged += (_, e) =>
        {
            set(e.NewValue);
            value.Text = format(e.NewValue);
        };
        if (persist)
        {
            slider.PreviewMouseUp += (_, _) => Save(() => _host.Audio.ApplySettings());
            slider.LostKeyboardFocus += (_, _) => Save(() => _host.Audio.ApplySettings());
        }
        panel.Children.Add(slider);
        panel.Children.Add(value);
        Row(label, help, panel);
    }

    private void Buttons(string label, string? help, params (string Text, Action Action)[] buttons)
    {
        var wrap = new WrapPanel();
        foreach (var (text, action) in buttons) wrap.Children.Add(ViewKit.Pill(text, action, 200, "SmallPill"));
        Row(label, help, wrap);
    }

    private void Info(string text, string brush = "SubtleTextBrush", double size = 27)
    {
        _rows.Children.Add(ViewKit.Card(ViewKit.Text(text, size, FontWeights.Normal, brush), margin: new Thickness(0, 0, 24, 16)));
    }

    private static string Percent(double v) => $"{v * 100:0}%";

    // ---------------------------------------------------------------- categories

    private void BuildDisplay()
    {
        Header("Display");
        Choice("Theme", "Classic white or the dim Night look", new[] { ("Classic", "Classic"), ("Night", "Night") }, () => S.Theme, v => S.Theme = v, () => ThemeManager.Apply(S.Theme));

        var monitors = Monitors.Enumerate();
        var options = new List<(string, string)> { ("Automatic (primary)", "") };
        options.AddRange(monitors.Select(m => ($"{FriendlyDisplay(m.DeviceName)} · {m.Width}×{m.Height}{(m.IsPrimary ? " · primary" : "")}", m.DeviceName)));
        Choice("Menu screen", "Which monitor shows the channel menu", options, () => S.TargetMonitor ?? "", v => S.TargetMonitor = v.Length == 0 ? null : v, () =>
        {
            _window.PlaceOnMonitor();
            _window.RebuildBackdrops();
        });
        Toggle("Backdrop on other screens", "Covers other monitors with a calm Couchtop background", () => S.BackdropOnOtherMonitors, v => S.BackdropOnOtherMonitors = v, _window.RebuildBackdrops);
        Choice("Pointer", "The hand pointer tilts as you move it", new[] { ("Hand", "hand"), ("Windows cursor", "system") }, () => S.UseSystemCursor ? "system" : "hand", v => S.UseSystemCursor = v == "system", _window.ApplyCursorMode);
        Toggle("Reduce motion", "Turns off tile animations, wobble and zoom transitions", () => S.ReduceMotion, v => S.ReduceMotion = v, () => _window.Menu.RebuildPages());
        Choice("Clock", null, new[] { ("12-hour", "12"), ("24-hour", "24") }, () => S.Clock24Hour ? "24" : "12", v => S.Clock24Hour = v == "24", _window.Menu.RefreshClock);
        Toggle("Startup screen", "Shows the Couchtop title for a moment when starting", () => S.ShowStartupSplash, v => S.ShowStartupSplash = v);
        Toggle("Quick launch", "Start apps right away instead of showing the channel start screen", () => S.QuickLaunch, v => S.QuickLaunch = v);
        Toggle("Minimize menu after starting an app", "Only in launcher mode (with Explorer running)", () => S.HideMenuWhenAppLaunches, v => S.HideMenuWhenAppLaunches = v);
    }

    private static string FriendlyDisplay(string device)
    {
        var digits = new string(device.Where(char.IsDigit).ToArray());
        return digits.Length > 0 ? "Display " + digits : device;
    }

    private void BuildSound()
    {
        Header("Sound");
        Toggle("Sound effects", "Soft chimes for pointing, selecting and pages", () => S.SoundEffects, v => S.SoundEffects = v);
        SliderRow("Effects volume", null, 0, 1, () => S.EffectsVolume, v => S.EffectsVolume = v, Percent);
        Toggle("Menu music", "A gentle music-box loop that plays only while the menu is in front", () => S.Ambience, v => S.Ambience = v, _host.Audio.ApplySettings);
        SliderRow("Music volume", null, 0, 1, () => S.AmbienceVolume, v =>
        {
            S.AmbienceVolume = v;
            _host.Audio.ApplySettings();
        }, Percent);
        var systemVolume = AudioService.GetSystemVolume();
        if (systemVolume >= 0)
            SliderRow("Windows volume", "Master volume of the default speakers", 0, 1, () => systemVolume, v => AudioService.SetSystemVolume((float)v), Percent, persist: false);
        Buttons("Test", null, ("Play Chime", () => _host.Audio.Play(SoundEffect.Launch)), ("Play Startup", () => _host.Audio.Play(SoundEffect.Startup)));
        Info("Every sound and the menu music are synthesized by Couchtop itself. No audio files are shipped.");
    }

    private void BuildControls()
    {
        Header("Controls");
        Toggle("Game controllers", "Xbox-compatible controllers: left stick moves the pointer, A selects, B goes back, LB/RB change pages, Guide or Back + Start opens the Quick Menu",
            () => S.ControllerEnabled, v => S.ControllerEnabled = v, () => _host.Input?.Restart());
        Toggle("Wii Remote", "Pair a Wii Remote in Windows Bluetooth settings (press 1 + 2). Point with a sensor bar; A selects, B goes back, +/− change pages, HOME opens the Quick Menu",
            () => S.WiimoteEnabled, v => S.WiimoteEnabled = v, () => _host.Input?.Restart());
        SliderRow("Pointer speed", "For controllers and the Wii Remote D-pad", 0.25, 3, () => S.PointerSpeed, v => S.PointerSpeed = v, v => $"{v:0.0}×");
        Choice("Quick Menu shortcut", "Opens the Quick Menu on top of any app",
            new[] { ("Ctrl + Alt + Home", "Ctrl+Alt+Home"), ("Ctrl + Shift + Home", "Ctrl+Shift+Home"), ("Ctrl + Alt + H", "Ctrl+Alt+H") },
            () => S.HomeMenuHotkey, v => S.HomeMenuHotkey = v, () =>
            {
                _window.RegisterHomeHotkey();
                Refresh();
            });
        Info($"Controllers connected: {_host.Input?.ConnectedControllers ?? 0}      Wii Remote: {(_host.Input?.WiimoteConnected == true ? "connected" : "not connected")}      Quick Menu shortcut: {(_window.HomeHotkeyRegistered ? "ready" : "unavailable (another app uses it)")}");
        Info("Keyboard: arrow keys move between channels, Enter selects, Esc goes back, Page Up/Down change pages.\nEmergency exit: Ctrl + Alt + Shift + F12 immediately returns to the Windows desktop and turns shell mode off.");
    }

    private void BuildChannels()
    {
        var layout = _host.Layout.Layout;
        Header("Channels");
        Buttons("Your channels", $"{layout.Channels.Count} channels on {LayoutEditor.PageCount(layout)} page(s)",
            ("Customize", () =>
            {
                _window.GoHome();
                _window.Menu.SetEditMode(true);
            }),
            ("Add Channel", () => _ = ChannelDialogs.AddChannelAsync(_window, _host, null)),
            ("Find New Apps", () =>
            {
                _host.RefreshChannelsInBackground();
                _window.ShowToast("Looking for newly installed apps…");
            }));
        Toggle("Add new apps automatically", "Newly installed apps and games appear as channels", () => S.AutoAddNewApps, v => S.AutoAddNewApps = v);
        Toggle("Start menu programs", "Takes effect the next time Couchtop starts", () => S.DiscoverStartMenu, v => S.DiscoverStartMenu = v);
        Toggle("Microsoft Store apps", "Takes effect the next time Couchtop starts", () => S.DiscoverStoreApps, v => S.DiscoverStoreApps = v);
        Toggle("Steam games", "Takes effect the next time Couchtop starts", () => S.DiscoverSteam, v => S.DiscoverSteam = v);
        Toggle("Epic Games", "Takes effect the next time Couchtop starts", () => S.DiscoverEpic, v => S.DiscoverEpic = v);
        Buttons("Reset layout", "Rebuilds the menu from your installed apps", ("Reset…", async () =>
        {
            if (await _window.ShowDialogAsync("Rebuild all channels from your installed apps?\nYour current arrangement and custom names will be lost.", "Reset", "Cancel") != "Reset") return;
            layout.Channels.Clear();
            layout.Slots.Clear();
            layout.DismissedSourceKeys.Clear();
            layout.KnownSourceKeys.Clear();
            layout.Seeded = false;
            ChannelSeeder.Apply(layout, _host.Discovery.Latest?.Apps ?? Array.Empty<DiscoveredApp>(), S.AutoAddNewApps);
            _host.SaveLayout();
            Refresh();
        }));
    }

    private void BuildShell()
    {
        var status = _host.Shell.GetStatus();
        var gate = _host.EvaluateSafetyGate();
        var edition = _host.Compatibility.Run().Edition;

        Header("Shell Mode");
        var summary = status.EnabledForCurrentUser
            ? $"ON: Couchtop starts instead of Explorer when {Environment.UserName} signs in."
            : status.ForeignShellConfigured
                ? $"OFF: another per-user shell is configured ({status.CurrentValue})."
                : "OFF: Windows Explorer starts when you sign in.";
        var statusStack = new StackPanel();
        var statusRow = new StackPanel { Orientation = Orientation.Horizontal };
        var dot = new Ellipse { Width = 34, Height = 34, Margin = new Thickness(0, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center };
        dot.SetResourceReference(Shape.FillProperty, status.EnabledForCurrentUser ? "SuccessBrush" : "ButtonBorderBrush");
        statusRow.Children.Add(dot);
        statusRow.Children.Add(ViewKit.Text(summary, 34, FontWeights.ExtraBold));
        statusStack.Children.Add(statusRow);
        statusStack.Children.Add(ViewKit.Text(
            $"{(_host.IsShellSession ? "This session is running Couchtop as the shell." : "This session is in launcher mode (Explorer is available).")}\n{edition.FriendlyName} · {_host.Shell.Mechanism.DisplayName}",
            25, FontWeights.Normal, "SubtleTextBrush"));
        _rows.Children.Add(ViewKit.Card(statusStack, margin: new Thickness(0, 0, 24, 16)));

        Buttons("1. Compatibility check", "Windows edition, policies, install location and recovery files", ("Run Check", ShowCompatibility));
        Buttons("2. Shell Safety Test", gate.Passed ? "Passed. Shell mode can be enabled." : "Required before shell mode can be enabled", ("Start Test", () => _window.Navigate(new SafetyTestView(_host, _window))));
        Buttons("3. Shell Mode", "Takes effect at your next sign-in. Only affects your Windows account.", ("Enable Shell Mode", EnableShell), ("Disable Shell Mode", DisableShell));
        Choice("Shell start method", "Resilient uses a Windows-only guard that falls back to Explorer even if Couchtop's files go missing",
            new[] { ("Resilient (recommended)", "Resilient"), ("Direct", "Direct") }, () => S.ShellBootstrap.ToString(), v => S.ShellBootstrap = Enum.Parse<ShellBootstrapMode>(v));
        Toggle("Run startup apps in shell mode", "Starts the programs Windows normally launches at sign-in (cloud sync, driver tray apps…)", () => S.RunStartupAppsInShell, v => S.RunStartupAppsInShell = v);
        Toggle("Start Couchtop at sign-in", "Launcher mode: opens full screen on top of the normal Explorer desktop", () => S.StartWithWindows, v => S.StartWithWindows = v, _host.ApplyStartWithWindows);
        Buttons("Recovery", "Restores Explorer without opening Couchtop. Also in the Start menu as \"Couchtop Recovery\".", ("Open Recovery Tool", OpenRecovery), ("Show Recovery Script", () => OpenFolder(_host.Install.Directory)));
        Info("How Couchtop keeps you safe in shell mode:\n" +
             "• Ctrl + Alt + Shift + F12 starts Explorer, turns shell mode off and closes Couchtop.\n" +
             "• If Couchtop crashes 3 times in 5 minutes, freezes, or fails on 2 sign-ins in a row, Explorer starts automatically and shell mode is turned off.\n" +
             "• Ctrl + Alt + Del › Task Manager › Run new task › explorer.exe always works too.\n" +
             "• Uninstalling Couchtop restores Explorer before removing any files.");
    }

    private async void ShowCompatibility()
    {
        var report = await Task.Run(() => _host.Compatibility.Run());
        await _window.ShowCustomDialogAsync(close =>
        {
            var stack = new StackPanel();
            stack.Children.Add(ViewKit.Text("Compatibility Check", 50, FontWeights.ExtraBold));
            stack.Children.Add(ViewKit.Text(report.HasBlocking ? "Not ready: fix the red items below before enabling Shell Mode." : "Ready: no blocking problems were found.", 30, FontWeights.Bold, report.HasBlocking ? "DangerBrush" : "SuccessBrush"));
            var list = new StackPanel { Margin = new Thickness(0, 20, 0, 20) };
            foreach (var item in report.Items)
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 14) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var marker = new Ellipse { Width = 26, Height = 26, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 8, 0, 0) };
                marker.SetResourceReference(Shape.FillProperty, item.Severity switch
                {
                    CheckSeverity.Blocking => "DangerBrush",
                    CheckSeverity.Warning => "WarningBrush",
                    CheckSeverity.Info => "AccentBrush",
                    _ => "SuccessBrush",
                });
                row.Children.Add(marker);
                var text = new StackPanel();
                text.Children.Add(ViewKit.Text(item.Title, 28, FontWeights.Bold));
                text.Children.Add(ViewKit.Text(item.Detail, 22, FontWeights.Normal, "SubtleTextBrush"));
                Grid.SetColumn(text, 1);
                row.Children.Add(text);
                list.Children.Add(row);
            }
            stack.Children.Add(new ScrollViewer { Content = list, MaxHeight = 600, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            var ok = ViewKit.Pill("OK", () => close(null), 260);
            ok.HorizontalAlignment = HorizontalAlignment.Right;
            stack.Children.Add(ok);
            return ViewKit.Panel(stack, 1500);
        });
    }

    private async void EnableShell()
    {
        var compat = await Task.Run(() => _host.Compatibility.Run());
        var gate = _host.EvaluateSafetyGate();
        if (compat.HasBlocking || !gate.Passed)
        {
            var reasons = compat.Items.Where(i => i.Severity == CheckSeverity.Blocking).Select(i => $"{i.Title}: {i.Detail}").Concat(gate.Reasons).Take(6);
            var choice = await _window.ShowDialogAsync("Shell Mode can't be turned on yet:\n\n• " + string.Join("\n• ", reasons), "Run Safety Test", "OK");
            if (choice == "Run Safety Test") _window.Navigate(new SafetyTestView(_host, _window));
            return;
        }

        var confirm = await _window.ShowDialogAsync(
            $"Turn on Shell Mode for {Environment.UserName}?\n\nNext time you sign in, Couchtop starts instead of Explorer.\n" +
            "Emergency exit: Ctrl + Alt + Shift + F12\nRecovery: Start menu › Couchtop Recovery", "Turn On", "Cancel");
        if (confirm != "Turn On") return;

        var result = _host.Shell.Enable(S.ShellBootstrap);
        _host.Audio.Play(result.Success ? SoundEffect.Launch : SoundEffect.Error);
        await _window.ShowDialogAsync(result.Message + (result.Reasons.Count > 0 ? "\n\n• " + string.Join("\n• ", result.Reasons) : ""), "OK");
        Refresh();
    }

    private async void DisableShell()
    {
        var result = _host.Shell.Disable();
        await _window.ShowDialogAsync(result.Message, "OK");
        Refresh();
    }

    private async void OpenRecovery()
    {
        if (!File.Exists(_host.Install.RecoveryPath))
        {
            await _window.ShowDialogAsync("The recovery tool was not found next to Couchtop. Reinstall Couchtop to restore it.", "OK");
            return;
        }
        try
        {
            using var _ = Process.Start(new ProcessStartInfo(_host.Install.RecoveryPath) { UseShellExecute = false });
        }
        catch (Exception ex)
        {
            await _window.ShowDialogAsync("The recovery tool could not be started: " + ex.Message, "OK");
        }
    }

    private static void OpenFolder(string path)
    {
        try
        {
            var explorer = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            using var _ = Process.Start(new ProcessStartInfo(explorer, $"\"{path}\"") { UseShellExecute = false });
        }
        catch (Exception ex)
        {
            Log.Warn("Could not open folder", ex);
        }
    }

    private void BuildData()
    {
        var record = _host.Session.Record;
        Header("Data");
        Info($"Clean exits: {record.CleanExits}      Unexpected exits: {record.UncleanExits}" + (record.LastUncleanExitAt is { } at ? $"      Last unexpected exit: {at:g}" : ""), "TextBrush");
        Buttons("Data folder", _host.Paths.DataRoot, ("Open Folder", () => OpenFolder(_host.Paths.DataRoot)), ("Open Logs", () => OpenFolder(_host.Paths.LogDirectory)));
        Buttons("Icon cache", "Clear it if app icons look out of date", ("Clear Cache", () =>
        {
            foreach (var file in Directory.EnumerateFiles(_host.Paths.IconCacheDirectory, "*.png"))
            {
                try { File.Delete(file); } catch (IOException) { }
            }
            _window.ShowToast("Icon cache cleared. Icons refresh next start.");
        }));
        Buttons("Reset settings", "Restores default settings. Channels are kept.", ("Reset…", async () =>
        {
            if (await _window.ShowDialogAsync("Restore all settings to their defaults?", "Reset", "Cancel") != "Reset") return;
            var defaults = new UserSettings { WelcomeShown = true }.Normalize();
            foreach (var property in typeof(UserSettings).GetProperties().Where(p => p.CanWrite))
                property.SetValue(S, property.GetValue(defaults));
            _host.SaveSettings();
            ThemeManager.Apply(S.Theme);
            _host.Audio.ApplySettings();
            _host.ApplyStartWithWindows();
            _window.ApplyCursorMode();
            _window.RegisterHomeHotkey();
            Refresh();
        }));
    }

    private void BuildAbout()
    {
        var version = typeof(SettingsView).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        Header("About");
        Info($"Couchtop {version}\nA playful, console-style desktop for Windows.", "TextBrush", 34);
        Info("Privacy: Couchtop has no telemetry, no accounts and no cloud. Settings and channels stay in your Windows profile. The Web uses Microsoft Edge WebView2, which follows your Windows and Edge privacy settings.");
        Info("Fonts: M PLUS Rounded 1c (SIL Open Font License 1.1). All artwork, the pointer and every sound are original and made for this project.\nCouchtop is an independent fan project and is not affiliated with or endorsed by Nintendo.");
        Info($"{(_host.Install.IsPortable ? "Portable folder" : "Install folder")}: {_host.Install.Directory}\nData folder: {_host.Paths.DataRoot}");
    }
}
