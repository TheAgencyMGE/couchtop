using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using Couchtop.App.Services;
using Couchtop.Core.Audio;
using Couchtop.Core.Channels;
using Couchtop.Core.Diagnostics;
using Couchtop.Core.Discovery;
using Couchtop.Core.Platform;
using Couchtop.Core.Settings;
using Couchtop.Core.Shell;

namespace Couchtop.App.Views;

public sealed class SettingsView : UserControl, IScreenView
{
    private static readonly string[] AllCategories = { "Display", "Desktop", "Sound", "Controls", "Channels", "Pals", "Shell Mode", "Data", "About" };

    /// <summary>Pals belong to the Channels menu style, so the console shells never offer their settings.</summary>
    private static string[] Categories =>
        ConsoleArt.IsConsoleShell() ? AllCategories.Where(c => c != "Pals").ToArray() : AllCategories;

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
        // More categories than fit at once on some text sizes, so the list scrolls.
        body.Children.Add(ViewKit.Scroll(tabs));

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
            case "Desktop": BuildDesktop(); break;
            case "Sound": BuildSound(); break;
            case "Controls": BuildControls(); break;
            case "Channels": BuildChannels(); break;
            case "Pals": BuildPals(); break;
            case "Shell Mode": BuildShell(); break;
            case "Data": BuildData(); break;
            default: BuildAbout(); break;
        }
    }

    private void Refresh() => SelectCategory(_category);

    /// <summary>Visual regression renders use this to capture the lower half of a long page.</summary>
    internal void SnapshotScrollToEnd() => _scroll.ScrollToEnd();

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
        var slider = new Slider { Minimum = min, Maximum = max, Value = Math.Clamp(get(), min, max), Width = 500, SmallChange = (max - min) / 20, LargeChange = (max - min) / 10 };
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
        var style = MenuStyleCatalog.All.First(m => m.Id == S.MenuStyle);
        Choice("Menu style", style.Description + ". Each style is its own shell: look, icons, sounds and navigation.",
            MenuStyleCatalog.All.Select(m => (m.Name, m.Id)).ToList(), () => S.MenuStyle, v => S.MenuStyle = v, () =>
            {
                _window.ApplyMenuStyle(S.MenuStyle);
                Refresh();
            });

        var current = ThemeCatalog.All.First(t => t.Id == S.Theme);
        if (S.MenuStyle != MenuStyleCatalog.Channels)
            Info("Themes are part of the Channels menu style. The Dashboard and Media Bar have a look of their own.");
        Choice("Theme", current.Description + ". Changes colors, shapes, the pointer and scenery of the Channels menu.", ThemeCatalog.All.Select(t => (t.Name, t.Id)).ToList(), () => S.Theme, v => S.Theme = v, () =>
        {
            _window.ApplyTheme(S.Theme);
            _host.Pals.ReportTheme(S.Theme);
            Refresh();
        });

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
        Toggle("Reduce motion", "Turns off tile animations, scenery, wobble and zoom transitions", () => S.ReduceMotion, v => S.ReduceMotion = v, () => _window.ApplyTheme(S.Theme));
        Choice("Text size", "Makes Couchtop's own text bigger. Pick the High Contrast theme above for maximum readability.",
            new[] { ("Normal", "1"), ("Large", "1.2"), ("Larger", "1.4") },
            () => S.TextScale switch { >= 1.35 => "1.4", >= 1.15 => "1.2", _ => "1" },
            v => S.TextScale = double.Parse(v, System.Globalization.CultureInfo.InvariantCulture),
            () =>
            {
                ViewKit.TextScale = S.TextScale;
                _window.Menu.Rebuild();
                Refresh();
            });
        Buttons("Windows accessibility", "Narrator, Magnifier, contrast themes and pointer size live in Windows",
            ("Open Windows Settings", () =>
            {
                if (!WindowsSettings.Open(WindowsSettings.Accessibility)) _window.ShowToast("Windows Settings could not be opened");
            }));
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

    private void BuildDesktop()
    {
        Header("Desktop");
        Info(_host.IsShellSession
            ? "Couchtop is the desktop for this session: it replaces Explorer's desktop, taskbar and Start menu while Windows keeps running underneath."
            : "Couchtop is in launcher mode, so Explorer's desktop and taskbar are still there. Turn on Shell Mode to make Couchtop the desktop at your next sign-in.");

        Toggle("Hide the Windows taskbar", "While the Couchtop Bar is shown, Explorer's taskbar auto-hides so there are not two",
            () => S.AutoHideWindowsTaskbar, v => S.AutoHideWindowsTaskbar = v, _window.ApplyTaskbarAutoHide);

        Choice("Couchtop Bar", "The bar along the bottom with your open apps, search, the clock and status",
            new[] { ("In shell mode", "shell"), ("Always", "always"), ("Never", "never") },
            () => S.CouchtopBar, v => S.CouchtopBar = v, () =>
            {
                _window.ApplyDesktopBar();
                Refresh();
            });
        Toggle("Keep the bar clear", "Reserves that strip of the screen so maximized windows stop above it", () => S.BarReservesSpace, v => S.BarReservesSpace = v, _window.RefreshDesktopBar);
        Choice("Switch app shortcut", "Shows every open window, like Alt + Tab",
            new[] { ("Ctrl + Alt + W", "Ctrl+Alt+W"), ("Ctrl + Alt + S", "Ctrl+Alt+S"), ("Ctrl + Alt + Q", "Ctrl+Alt+Q") },
            () => S.TaskSwitcherHotkey, v => S.TaskSwitcherHotkey = v, () =>
            {
                _window.RegisterDesktopHotkeys();
                Refresh();
            });
        Choice("Search shortcut", "Finds apps, files, settings and open windows",
            new[] { ("Ctrl + Alt + Space", "Ctrl+Alt+Space"), ("Ctrl + Alt + F", "Ctrl+Alt+F"), ("Ctrl + Shift + Space", "Ctrl+Shift+Space") },
            () => S.CommandPaletteHotkey, v => S.CommandPaletteHotkey = v, () =>
            {
                _window.RegisterDesktopHotkeys();
                Refresh();
            });
        Toggle("Continue where I left off", "Reopens the Couchtop channel you had open (Files comes back in the same folder)", () => S.RestoreLastScreen, v => S.RestoreLastScreen = v);
        Buttons("Try them", null,
            ("Open Search", _window.OpenCommandPalette),
            ("Switch App", _window.OpenTaskSwitcher),
            ("Minimize All", () => _host.Desktop.MinimizeAll()));
        Header("Windows settings");
        Info("Couchtop handles the desktop; Windows still owns the hardware. These open the Windows panels, and they work in shell mode too.");
        Buttons("Network", "Wi-Fi, Bluetooth and other connections",
            ("Wi-Fi", () => OpenWindows(WindowsSettings.Wifi)),
            ("Bluetooth", () => OpenWindows(WindowsSettings.Bluetooth)),
            ("All network", () => OpenWindows(WindowsSettings.Network)));
        Buttons("Hardware", "Screens, sound devices and battery",
            ("Display", () => OpenWindows(WindowsSettings.Display)),
            ("Sound", () => OpenWindows(WindowsSettings.Sound)),
            ("Power & sleep", () => OpenWindows(WindowsSettings.Power)));
        Buttons("System", "Installed programs, updates and everything else",
            ("Apps", () => OpenWindows(WindowsSettings.Apps)),
            ("Windows Update", () => OpenWindows(WindowsSettings.Update)),
            ("All Windows settings", () => OpenWindows(WindowsSettings.Home)));

        Info($"Show the Couchtop menu and clear the screen: {MainWindow.ShowDesktopHotkey}.\n" +
             "On the bar: click an app to show or minimize it, middle-click to close it, right-click for snapping, moving to another screen and closing.\n" +
             "The Quick Menu (and a controller's Guide or HOME button) works on top of full-screen apps.");
    }

    private void OpenWindows(string page)
    {
        if (!WindowsSettings.Open(page)) _window.ShowToast("Windows Settings could not be opened");
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

        Header("Your Music & Sounds");
        var music = S.CustomMusic;
        var musicButtons = new List<(string, Action)> { ("Choose File…", () => _ = ChooseCustomAudioAsync(CustomAudio.MusicSlot)) };
        if (music is not null)
        {
            musicButtons.Add(("Preview", () => _ = PreviewMusicAsync(music)));
            musicButtons.Add(("Use Built-in", () => _ = ResetCustomAudioAsync(CustomAudio.MusicSlot)));
        }
        Buttons("Menu music", music is null ? "Built-in music box. Pick your own song to loop on the menu instead." : $"Playing your song \"{music.Name}\" on a loop while the menu is in front", musicButtons.ToArray());

        foreach (var slot in CustomSoundSlots.All)
        {
            S.CustomSounds.TryGetValue(slot.Id, out var file);
            var effect = Enum.Parse<SoundEffect>(slot.Id);
            var id = slot.Id;
            var buttons = new List<(string, Action)> { ("Choose…", () => _ = ChooseCustomAudioAsync(id)), ("Play", () => _host.Audio.Play(effect)) };
            if (file is not null) buttons.Add(("Reset", () => _ = ResetCustomAudioAsync(id)));
            Buttons(slot.Name + " sound", $"{slot.Description}  ·  {(file is null ? "Built-in" : "Yours: " + file.Name)}", buttons.ToArray());
        }
        if (S.CustomSounds.Count > 0) Buttons("Reset all sounds", "Go back to Couchtop's own sounds", ("Reset All", () => _ = ResetAllSoundsAsync()));
        Info("Use MP3, WAV, M4A, AAC, WMA or FLAC files. Sounds longer than 5 seconds are cut short. Couchtop keeps its own copy of every file you pick, so moving or deleting the original is fine. The built-in sounds and music are synthesized by Couchtop itself.");
    }

    private async Task ChooseCustomAudioAsync(string slot)
    {
        var isMusic = slot == CustomAudio.MusicSlot;
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = isMusic ? "Choose your menu music" : "Choose a sound",
            Filter = CustomAudio.FileFilter,
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(_window) != true) return;
        var source = dialog.FileName;

        CustomAudioFile imported;
        try
        {
            imported = await Task.Run(() =>
            {
                CustomAudio.EnsureAcceptable(source, slot);
                CustomAudioLoader.LoadClip(source, 1); // proves Windows can decode it before we copy it
                return _host.AudioLibrary.Import(source, slot);
            });
        }
        catch (Exception ex)
        {
            Log.Warn("Custom audio file rejected: " + source, ex);
            _host.Audio.Play(SoundEffect.Error);
            await _window.ShowDialogAsync("Couchtop couldn't use that file.\n" + CustomAudioLoader.Explain(ex), "OK");
            return;
        }

        CustomAudioFile? old;
        if (isMusic)
        {
            old = S.CustomMusic;
            S.CustomMusic = imported;
        }
        else
        {
            S.CustomSounds.TryGetValue(slot, out old);
            S.CustomSounds[slot] = imported;
        }
        _host.SaveSettings();
        await _host.Audio.ReloadCustomAudioAsync();
        _host.AudioLibrary.Delete(old);
        Refresh();
        if (isMusic) _ = PreviewMusicAsync(imported);
        else if (Enum.TryParse<SoundEffect>(slot, out var effect)) _host.Audio.Play(effect);
    }

    private async Task PreviewMusicAsync(CustomAudioFile file)
    {
        if (_host.AudioLibrary.PathFor(file) is not { } path) return;
        try
        {
            var clip = await Task.Run(() => CustomAudioLoader.LoadClip(path, 12));
            _host.Audio.PlayPreview(clip);
        }
        catch (Exception ex)
        {
            Log.Warn("Could not preview custom music", ex);
        }
    }

    private async Task ResetCustomAudioAsync(string slot)
    {
        CustomAudioFile? old;
        if (slot == CustomAudio.MusicSlot)
        {
            old = S.CustomMusic;
            S.CustomMusic = null;
        }
        else if (!S.CustomSounds.Remove(slot, out old))
        {
            return;
        }
        _host.SaveSettings();
        await _host.Audio.ReloadCustomAudioAsync();
        _host.AudioLibrary.Delete(old);
        _host.Audio.Play(SoundEffect.Back);
        Refresh();
    }

    private async Task ResetAllSoundsAsync()
    {
        if (await _window.ShowDialogAsync("Go back to Couchtop's own sounds?\nYour menu music stays.", "Reset", "Cancel") != "Reset") return;
        var old = S.CustomSounds.Values.ToList();
        S.CustomSounds.Clear();
        _host.SaveSettings();
        await _host.Audio.ReloadCustomAudioAsync();
        foreach (var file in old) _host.AudioLibrary.Delete(file);
        Refresh();
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

    private void BuildPals()
    {
        Header("Pals");
        var pals = _host.Pals;
        void OpenStudio() => _window.OpenBuiltInView(new Pals.PalStudioView(_host, _window));
        if (pals.Profile is not { } profile)
        {
            Info("Make a Pal: a little 3D character of your own that hangs out on the home screen and reacts to what you're doing.");
            Buttons("Your Pal", null, ("Make my Pal", OpenStudio));
            return;
        }
        Buttons(profile.Name, "Change their look, name and personality in Pal Studio", ("Open Pal Studio", OpenStudio));
        var prefs = pals.Preferences;
        Toggle("Hang out in Couchtop", "On the home screen, by the Start button and peeking in on other screens", () => prefs.ShowOnHome, v => prefs.ShowOnHome = v, pals.SavePreferences);
        Toggle("Walk around", "Stroll along the bottom of the home screen", () => prefs.Wander, v => prefs.Wander = v, pals.SavePreferences);
        Toggle("Visit the desktop", "Comes out onto the Windows desktop while you use other apps. Walks on the taskbar, drag it anywhere, right-click it for options. Hides for full-screen games and videos.",
            () => prefs.DesktopVisits, v => prefs.DesktopVisits = v, pals.SavePreferences);
        Toggle("Climb on windows", "On the desktop, hop up onto app windows (and ride along when you move them)", () => prefs.ClimbWindows, v => prefs.ClimbWindows = v, pals.SavePreferences);
        Toggle("Speak up in the Couchtop Bar", "While you use other apps. Never over full-screen games or videos.", () => prefs.BarReactions, v => prefs.BarReactions = v, pals.SavePreferences);
        Choice("How often they talk", "Being poked always gets an answer, unless they never talk",
            new[] { ("Chatty", "Chatty"), ("Now and then", "Normal"), ("Quiet", "Quiet"), ("Never", "Silent") },
            () => prefs.Chattiness.ToString(), v => prefs.Chattiness = Enum.Parse<Core.Pals.PalChattiness>(v), pals.SavePreferences);
        Buttons("Start over", "Say goodbye to this Pal. You can make a new one any time.", ("Remove Pal…", async () =>
        {
            if (await _window.ShowDialogAsync($"Say goodbye to {profile.Name}?\nYou can make a new Pal any time.", "Remove", "Cancel") != "Remove") return;
            pals.RemovePal();
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
            _window.ApplyTheme(S.Theme);
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
        Info("Fonts: M PLUS Rounded 1c (SIL Open Font License 1.1). All artwork, the pointer and every sound are original and made for this project.\nCouchtop is an independent project and is not affiliated with or endorsed by Nintendo, Microsoft or Sony.");
        Info($"{(_host.Install.IsPortable ? "Portable folder" : "Install folder")}: {_host.Install.Directory}\nData folder: {_host.Paths.DataRoot}");
    }
}
