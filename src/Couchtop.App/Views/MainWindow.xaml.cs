using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Couchtop.App.Controls;
using Couchtop.App.Services;
using Couchtop.Core.Channels;
using Couchtop.Core.Diagnostics;
using Couchtop.Core.Native;
using Couchtop.Core.Platform;
using Couchtop.Core.Safety;
using Couchtop.Core.Settings;
using Couchtop.Core.Shell;

namespace Couchtop.App.Views;

public partial class MainWindow : Window
{
    private const int EmergencyHotkeyId = 1;
    private const int HomeHotkeyId = 2;
    private const int SwitcherHotkeyId = 3;
    private const int PaletteHotkeyId = 4;
    private const int ShowDesktopHotkeyId = 5;
    public const string ShowDesktopHotkey = "Ctrl+Alt+D";
    private static bool _classHandlersRegistered;

    private readonly AppHost _host;
    private readonly List<FrameworkElement> _stack = new();
    private readonly List<BackdropWindow> _backdrops = new();
    private readonly DispatcherTimer _displayDebounce;
    private readonly DispatcherTimer _toastTimer;
    private IntPtr _hwnd;
    private HwndSource? _source;
    private HomeMenuWindow? _home;
    private CouchtopBar? _bar;
    private CommandPaletteWindow? _palette;
    private TaskSwitcherWindow? _switcher;
    private CalendarWindow? _calendar;
    private StatusCenterWindow? _status;
    private TaskCompletionSource<object?>? _dialog;
    private CancellationTokenSource? _activationCts;
    private bool _splashActive;
    private bool _sessionLocked;
    private bool _useSystemCursor;

    private Point _pointerTarget;
    private Point _pointerLast;
    private double _pointerAngle;
    private bool _renderingHooked;
    private bool _pointerInside;
    private DateTime _lastMove;
    private DateTime _lastFrame;
    private Point? _bubbleAnchor;
    private readonly ScaleTransform PointerScale = new(1, 1);
    private readonly RotateTransform PointerRotate = new();

    public MainWindow(AppHost host)
    {
        InitializeComponent();
        _host = host;
        Pointer.RenderTransform = new TransformGroup { Children = { PointerScale, PointerRotate } };
        RegisterClassHandlers();

        Menu = CreateHomeScreen(host.Settings.Current.MenuStyle);
        ViewLayer.Children.Add(Menu.Element);
        _stack.Add(Menu.Element);

        // The Pal's corner peek sits above every screen but below dialogs and the pointer.
        _palPeek = new Pals.PalPeek(host);
        RootGrid.Children.Insert(RootGrid.Children.IndexOf(DialogLayer), _palPeek);

        _displayDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _displayDebounce.Tick += (_, _) =>
        {
            _displayDebounce.Stop();
            PlaceOnMonitor();
            RebuildBackdrops();
        };
        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.8) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            Anim.To(Toast, OpacityProperty, 0, 350);
        };

        if (host.Options.IsSnapshot)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = -32000;
            Top = -32000;
            Width = host.Options.SnapshotWidth;
            Height = host.Options.SnapshotHeight;
            ShowInTaskbar = false;
            ShowActivated = false;
        }

        SourceInitialized += OnSourceInitialized;
        ContentRendered += OnContentRendered;
        Activated += (_, _) => UpdateActivity();
        Deactivated += (_, _) =>
        {
            ShowBubble(null);
            UpdateActivity();
        };
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Normal) PlaceOnMonitor();
            UpdateActivity();
        };
        Closing += OnClosing;
        Closed += OnClosed;
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewMouseMove += OnPreviewMouseMove;
        PreviewMouseDown += OnPreviewMouseDown;
        MouseLeave += (_, _) =>
        {
            _pointerInside = false;
            UpdatePointerVisibility();
            if (_bubbleAnchor is null) ShowBubble(null);
        };
        SizeChanged += (_, _) => UpdateScales();
        Application.Current.SessionEnding += OnSessionEnding;
        ApplyCursorMode();
    }

    /// <summary>The home screen for the chosen menu style. Swapped live by <see cref="ApplyMenuStyle"/>.</summary>
    public IHomeScreen Menu { get; private set; }
    public bool EmergencyHotkeyRegistered { get; private set; }
    public bool HomeHotkeyRegistered { get; private set; }
    public Func<bool>? EmergencyHotkeyInterceptor { get; set; }
    public bool IsDialogOpen => _dialog is not null;
    public IntPtr Handle => _hwnd;
    public FrameworkElement CurrentView => _stack[^1];

    private void RegisterClassHandlers()
    {
        if (_classHandlersRegistered) return;
        _classHandlersRegistered = true;
        EventManager.RegisterClassHandler(typeof(ButtonBase), MouseEnterEvent, new MouseEventHandler((s, _) =>
        {
            if (s is ButtonBase { IsEnabled: true }) AppHost.CurrentOrNull?.Audio.Play(SoundEffect.Hover);
        }));
        EventManager.RegisterClassHandler(typeof(ButtonBase), GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler((s, _) =>
        {
            if (s is ButtonBase { IsMouseOver: false }) AppHost.CurrentOrNull?.Audio.Play(SoundEffect.Tick);
        }));
        EventManager.RegisterClassHandler(typeof(ButtonBase), ButtonBase.ClickEvent, new RoutedEventHandler((_, _) =>
            AppHost.CurrentOrNull?.Audio.Play(SoundEffect.Select)));
    }

    // ---------------------------------------------------------------- lifecycle

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        _source = HwndSource.FromHwnd(_hwnd);
        _source?.AddHook(WndProc);
        if (_host.Options.IsSnapshot) return;

        PlaceOnMonitor();
        RegisterHotkeys();
        SystemEvents.DisplaySettingsChanged += OnSystemDisplayChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;

        _host.Input = new InputService(_host.Settings.Current, () => _hwnd);
        _host.Input.HomeRequested += () => Dispatcher.BeginInvoke(ToggleHomeMenu);
        _host.Input.Start();
        ListenForActivation();
        RebuildBackdrops();
        _host.StartTrayHost();
        ApplyDesktopBar();
    }

    // ---------------------------------------------------------------- desktop shell

    /// <summary>
    /// Shows or hides the Couchtop Bar. It is on by default only when Couchtop is the Windows shell; with Explorer
    /// running its taskbar is already there, so the bar stays out of the way unless the user asks for it.
    /// </summary>
    public void ApplyDesktopBar()
    {
        if (_host.Options.IsSnapshot) return;
        var mode = _host.Settings.Current.CouchtopBar;
        var wanted = mode == "always" || (mode == "shell" && _host.IsShellSession);
        if (wanted == (_bar is not null))
        {
            _bar?.Reposition();
            ApplyTaskbarAutoHide();
            return;
        }
        if (wanted)
        {
            _bar = new CouchtopBar(_host, this);
            _bar.Show();
            _bar.Start();
            Log.Info("Couchtop Bar shown");
        }
        else
        {
            _bar?.Close();
            _bar = null;
        }
        ApplyTaskbarAutoHide();
    }

    /// <summary>
    /// With the Couchtop Bar on screen there is no need for a second taskbar underneath it, so Explorer's is
    /// set to auto-hide while ours is up, and put back exactly as it was when ours goes away. As the Windows
    /// shell there is no Explorer taskbar, so nothing happens.
    /// </summary>
    public void ApplyTaskbarAutoHide()
    {
        if (_host.Options.IsSnapshot) return;
        var wanted = _bar is not null && _host.Settings.Current.AutoHideWindowsTaskbar && !_host.IsShellSession && !_host.IsExiting;
        if (wanted) WindowsTaskbar.Hide();
        else WindowsTaskbar.Restore();
    }

    /// <summary>Opens the command palette (search everything) over whatever is on screen.</summary>
    public void OpenCommandPalette()
    {
        if (_host.Options.IsSnapshot) return;
        if (_palette is { IsVisible: true })
        {
            _palette.Activate();
            return;
        }
        _palette = new CommandPaletteWindow(_host, this);
        _palette.Closed += (_, _) => _palette = null;
        _palette.ShowPalette();
    }

    /// <summary>Opens the task switcher, or steps it along when it is already up.</summary>
    public void OpenTaskSwitcher()
    {
        if (_host.Options.IsSnapshot) return;
        if (_switcher is { IsVisible: true })
        {
            _switcher.Step(1);
            return;
        }
        var switcher = new TaskSwitcherWindow(_host, this);
        switcher.Closed += (_, _) => _switcher = null;
        if (!switcher.ShowSwitcher())
        {
            ShowToast("No other windows are open");
            return;
        }
        _switcher = switcher;
    }

    /// <summary>Opens the status center (volume, battery, Wi-Fi, notifications) above the bar.</summary>
    public void OpenStatusCenter()
    {
        if (_status is { IsVisible: true })
        {
            _status.Close();
            return;
        }
        _status = new StatusCenterWindow(_host, this);
        _status.Closed += (_, _) => _status = null;
        _status.ShowPanel();
    }

    public void OpenCalendar()
    {
        if (_calendar is { IsVisible: true })
        {
            _calendar.Close();
            return;
        }
        _calendar = new CalendarWindow(_host, this);
        _calendar.Closed += (_, _) => _calendar = null;
        _calendar.ShowPopup();
    }

    /// <summary>Rebuilds the bar so a changed setting (like reserving screen space) takes effect right away.</summary>
    public void RefreshDesktopBar()
    {
        _bar?.Close();
        _bar = null;
        ApplyDesktopBar();
    }

    public void OpenBuiltIn(string builtInId)
    {
        BringToFront();
        Menu.OpenBuiltIn(builtInId, null);
    }

    public void OpenBuiltInView(FrameworkElement view)
    {
        BringToFront();
        Navigate(view);
    }

    private void OnContentRendered(object? sender, EventArgs e)
    {
        UpdateScales();
        if (_host.Options.IsSnapshot)
        {
            _ = RunSnapshotsAsync();
            return;
        }

        ViewKit.TextScale = _host.Settings.Current.TextScale;
        _host.SignalReady();
        _host.RefreshChannelsInBackground();
        // The Pal starts with Couchtop, not with the menu, so it can head out onto the desktop even when
        // Couchtop opens behind other windows.
        _host.Pals.SetSuspended(ConsoleArt.IsConsoleShell(_host));
        Dispatcher.BeginInvoke(() => _host.Pals.Start(this), DispatcherPriority.ApplicationIdle);
        if (_host.Options.SmokeTestSeconds > 0) ScheduleSmokeTestExit();
        if (_host.Settings.Current.ShowStartupSplash && !Anim.Reduced) PlaySplash();
        else
        {
            Menu.PlayIntro();
            AfterIntro();
        }
    }

    private void ScheduleSmokeTestExit()
    {
        using var self = System.Diagnostics.Process.GetCurrentProcess();
        var startupMs = (DateTime.Now - self.StartTime).TotalMilliseconds;
        Log.Info($"SMOKE startup-to-first-frame={startupMs:0} ms, working-set={self.WorkingSet64 / 1048576} MB, " +
                 $"emergency-hotkey={EmergencyHotkeyRegistered}, home-hotkey={HomeHotkeyRegistered}, size={ActualWidth:0}x{ActualHeight:0}, dpi-scale={PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11:0.##}");
        var total = _host.Options.SmokeTestSeconds;
        var midpointCpu = TimeSpan.Zero;
        var midpointAt = DateTime.UtcNow;
        var midpoint = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Max(1, total / 2.0)) };
        midpoint.Tick += (_, _) =>
        {
            midpoint.Stop();
            using var p = System.Diagnostics.Process.GetCurrentProcess();
            midpointCpu = p.TotalProcessorTime;
            midpointAt = DateTime.UtcNow;
        };
        midpoint.Start();

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(total) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            using var current = System.Diagnostics.Process.GetCurrentProcess();
            current.Refresh();
            var window = (DateTime.UtcNow - midpointAt).TotalMilliseconds;
            var idlePercent = window > 0 ? (current.TotalProcessorTime - midpointCpu).TotalMilliseconds / window / Environment.ProcessorCount * 100 : 0;
            Log.Info($"SMOKE before-exit working-set={current.WorkingSet64 / 1048576} MB, cpu-total={current.TotalProcessorTime.TotalMilliseconds:0} ms, " +
                     $"second-half-cpu={idlePercent:0.00}% of all cores (active={IsActive}), channels={_host.Layout.Layout.Channels.Count}");
            _host.Exit(ExitCodes.Success);
        };
        timer.Start();
    }

    private void PlaySplash()
    {
        _splashActive = true;
        SplashLayer.Visibility = Visibility.Visible;
        SplashLayer.Opacity = 1;
        Anim.To(SplashScale, ScaleTransform.ScaleXProperty, 1, 1400, Anim.EaseOut, from: 0.86);
        Anim.To(SplashScale, ScaleTransform.ScaleYProperty, 1, 1400, Anim.EaseOut, from: 0.86);
        Anim.To(SplashGlow, OpacityProperty, 1, 900, Anim.EaseOut);
        Anim.To(SplashHint, OpacityProperty, 1, 500, delayMs: 700);
        var sound = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        sound.Tick += (_, _) =>
        {
            sound.Stop();
            _host.Audio.Play(SoundEffect.Startup);
        };
        sound.Start();
        var end = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1900) };
        end.Tick += (_, _) =>
        {
            end.Stop();
            EndSplash();
        };
        end.Start();
    }

    private void EndSplash()
    {
        if (!_splashActive) return;
        _splashActive = false;
        Anim.To(SplashLayer, OpacityProperty, 0, 450, completed: () => SplashLayer.Visibility = Visibility.Collapsed);
        Menu.PlayIntro();
        AfterIntro();
    }

    private void AfterIntro()
    {
        UpdateActivity();
        Menu.Element.Focus();
        RestoreLastScreen();
        if (_host.Options.IsSnapshot) return;
        // New installs get the tour, and everyone gets it once more when it gains something worth seeing.
        if (_host.Settings.Current.TourVersion >= WelcomeView.CurrentVersion) return;
        Dispatcher.BeginInvoke(ShowTour, DispatcherPriority.Background);
    }

    /// <summary>Opens the welcome tour over the menu.</summary>
    public void ShowTour()
    {
        if (CurrentView is WelcomeView) return;
        GoHome();
        Navigate(new WelcomeView(_host, this));
    }

    /// <summary>
    /// "Continue where I left off": reopens the built-in channel that was on screen last time. Only built-in
    /// screens come back, never an app, so a crashing app can't be relaunched into a loop.
    /// </summary>
    private void RestoreLastScreen()
    {
        var settings = _host.Settings.Current;
        if (!settings.RestoreLastScreen || _host.Options.IsSnapshot) return;
        var id = settings.LastScreen;
        if (id is null || !BuiltInChannels.All.Contains(id) || id == BuiltInChannels.Customize || id == BuiltInChannels.Power) return;
        Log.Info("Restoring last screen: " + id);
        Menu.OpenBuiltIn(id, null);
    }

    private void UpdateActivity()
    {
        var active = IsActive && WindowState != WindowState.Minimized && !_sessionLocked && !_splashActive && !_host.IsExiting;
        Menu.SetActive(active && CurrentView == Menu.Element);
        _host.Audio.SetAmbience(active && !ConsoleArt.IsConsoleShell(_host) && CurrentView is IScreenView { PlaysAmbience: true });
        SetDecorRunning(active && !Anim.Reduced);
    }

    private bool _decorRunning;

    private void SetDecorRunning(bool run)
    {
        if (_decorRunning == run) return;
        _decorRunning = run;
        if (run) IdleAnim.Start(ThemeDecor, 0);
        else IdleAnim.Stop(ThemeDecor);
    }

    /// <summary>Switches the whole app to a theme: styles update live, tiles and scenery are rebuilt for the new look.</summary>
    public void ApplyTheme(string theme)
    {
        SetDecorRunning(false);
        ThemeManager.ApplyFor(_host.Settings.Current.MenuStyle, theme);
        Menu.Rebuild();
        RebuildBackdrops();
        // The scenery template is re-created on the next layout pass; start its animations after that.
        Dispatcher.BeginInvoke(UpdateActivity, DispatcherPriority.Loaded);
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_host.IsExiting) return;
        e.Cancel = true;
        Dispatcher.BeginInvoke(() =>
        {
            if (CurrentView is not PowerView) Navigate(new PowerView(_host, this));
        });
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        SystemEvents.DisplaySettingsChanged -= OnSystemDisplayChanged;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        Application.Current.SessionEnding -= OnSessionEnding;
        _activationCts?.Cancel();
        if (_hwnd != IntPtr.Zero)
        {
            NativeMethods.UnregisterHotKey(_hwnd, EmergencyHotkeyId);
            NativeMethods.UnregisterHotKey(_hwnd, HomeHotkeyId);
        }
        foreach (var b in _backdrops) b.Close();
        _backdrops.Clear();
        WindowsTaskbar.Restore();
    }

    private void OnSessionEnding(object? sender, SessionEndingCancelEventArgs e)
    {
        Log.Info("Windows session is ending: " + e.ReasonSessionEnding);
        _host.Exit(ExitCodes.SignOut);
    }

    private void ListenForActivation()
    {
        _activationCts = new CancellationTokenSource();
        var token = _activationCts.Token;
        new Thread(() =>
        {
            try
            {
                using var signal = InstanceSignals.Activate();
                var handles = new WaitHandle[] { signal, token.WaitHandle };
                while (!token.IsCancellationRequested)
                    if (WaitHandle.WaitAny(handles) == 0) Dispatcher.BeginInvoke(BringToFront);
            }
            catch (Exception ex)
            {
                Log.Warn("Activation listener stopped", ex);
            }
        })
        { IsBackground = true, Name = "Activation listener" }.Start();
    }

    /// <summary>
    /// Jumps to the normal Windows desktop without closing Couchtop. In shell mode Explorer is started first.
    /// Couchtop stays on the taskbar, and the Quick Menu hotkey brings it back.
    /// </summary>
    public async void ShowWindowsDesktop()
    {
        if (_host.Options.IsSnapshot) return;
        var settings = _host.Settings.Current;
        if (!settings.DesktopHintShown)
        {
            settings.DesktopHintShown = true;
            _host.SaveSettings();
            await ShowDialogAsync(
                "Couchtop keeps running in the background.\n\nTo come back, click Couchtop on the taskbar or press " + settings.HomeMenuHotkey + " and choose Menu.",
                "Go to Desktop");
        }

        if (_host.IsShellSession && !NativeMethods.IsExplorerShellRunning())
        {
            try { new ExplorerController().StartExplorer(); }
            catch (Exception ex) { Log.Warn("Could not start Explorer", ex); }
        }

        WindowState = WindowState.Minimized;
        try
        {
            // Minimize everything else too, so the user lands on the actual desktop rather than another app.
            if (NativeMethods.IsExplorerShellRunning() && Type.GetTypeFromProgID("Shell.Application") is { } shellType &&
                Activator.CreateInstance(shellType) is { } shell)
            {
                shellType.InvokeMember("MinimizeAll", System.Reflection.BindingFlags.InvokeMethod, null, shell, null);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Could not minimize other windows", ex);
        }
    }

    public void BringToFront()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show();
        Activate();
        if (_hwnd != IntPtr.Zero) NativeMethods.SetForegroundWindow(_hwnd);
    }

    // ---------------------------------------------------------------- hotkeys & system events

    private void RegisterHotkeys()
    {
        if (HotkeyParser.TryParse(HotkeyParser.EmergencyHotkey, out var emergency))
            EmergencyHotkeyRegistered = NativeMethods.RegisterHotKey(_hwnd, EmergencyHotkeyId, emergency.Modifiers | NativeMethods.MOD_NOREPEAT, emergency.VirtualKey);
        if (!EmergencyHotkeyRegistered)
            Log.Warn("Emergency hotkey not registered here (in shell mode the Guardian owns it).");
        RegisterHomeHotkey();
        RegisterDesktopHotkeys();
    }

    /// <summary>Desktop-wide shortcuts for the task switcher and the command palette.</summary>
    public void RegisterDesktopHotkeys()
    {
        if (_hwnd == IntPtr.Zero) return;
        foreach (var (id, text) in new[]
                 {
                     (SwitcherHotkeyId, _host.Settings.Current.TaskSwitcherHotkey),
                     (PaletteHotkeyId, _host.Settings.Current.CommandPaletteHotkey),
                     (ShowDesktopHotkeyId, ShowDesktopHotkey),
                 })
        {
            NativeMethods.UnregisterHotKey(_hwnd, id);
            if (HotkeyParser.TryParse(text, out var hotkey) && NativeMethods.RegisterHotKey(_hwnd, id, hotkey.Modifiers, hotkey.VirtualKey)) continue;
            Log.Warn($"Shortcut '{text}' could not be registered (another app may own it).");
        }
    }

    public void RegisterHomeHotkey()
    {
        if (_hwnd == IntPtr.Zero) return;
        NativeMethods.UnregisterHotKey(_hwnd, HomeHotkeyId);
        HomeHotkeyRegistered = HotkeyParser.TryParse(_host.Settings.Current.HomeMenuHotkey, out var home) &&
                               NativeMethods.RegisterHotKey(_hwnd, HomeHotkeyId, home.Modifiers | NativeMethods.MOD_NOREPEAT, home.VirtualKey);
        if (!HomeHotkeyRegistered) Log.Warn("Quick Menu hotkey could not be registered: " + _host.Settings.Current.HomeMenuHotkey);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY)
        {
            switch (wParam.ToInt32())
            {
                case EmergencyHotkeyId:
                    handled = true;
                    if (EmergencyHotkeyInterceptor?.Invoke() != true) _host.Emergency("hotkey");
                    break;
                case HomeHotkeyId:
                    handled = true;
                    ToggleHomeMenu();
                    break;
                case SwitcherHotkeyId:
                    handled = true;
                    OpenTaskSwitcher();
                    break;
                case PaletteHotkeyId:
                    handled = true;
                    OpenCommandPalette();
                    break;
                case ShowDesktopHotkeyId:
                    handled = true;
                    _host.Desktop.MinimizeAll();
                    BringToFront();
                    break;
            }
        }
        return IntPtr.Zero;
    }

    private void OnSystemDisplayChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        _displayDebounce.Stop();
        _displayDebounce.Start();
    });

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Dispatcher.BeginInvoke(PlaceOnMonitor, DispatcherPriority.Background);
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume) return;
        Dispatcher.BeginInvoke(() =>
        {
            Log.Info("Resumed from sleep");
            _host.Audio.ResetDevice();
            PlaceOnMonitor();
            RebuildBackdrops();
            _host.Input?.Restart();
            Menu.RefreshClock();
            _host.RefreshChannelsInBackground(announce: false);
            UpdateActivity();
        });
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        _sessionLocked = e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect;
        UpdateActivity();
    });

    // ---------------------------------------------------------------- displays

    public void PlaceOnMonitor()
    {
        if (_host.Options.IsSnapshot || _hwnd == IntPtr.Zero || WindowState == WindowState.Minimized) return;
        var monitors = Monitors.Enumerate();
        var target = Monitors.Choose(monitors, _host.Settings.Current.TargetMonitor);
        if (target is null) return;
        NativeMethods.SetWindowPos(_hwnd, IntPtr.Zero, target.X, target.Y, target.Width, target.Height,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_FRAMECHANGED);
        Log.Info($"Main window on {target.DeviceName} {target.Width}x{target.Height} scale {target.Scale:0.##}");
    }

    public void RebuildBackdrops()
    {
        _bar?.Reposition();
        foreach (var b in _backdrops) b.Close();
        _backdrops.Clear();
        if (_host.Options.IsSnapshot || !_host.Settings.Current.BackdropOnOtherMonitors) return;
        var monitors = Monitors.Enumerate();
        var target = Monitors.Choose(monitors, _host.Settings.Current.TargetMonitor);
        foreach (var monitor in monitors.Where(m => target is null || !string.Equals(m.DeviceName, target.DeviceName, StringComparison.OrdinalIgnoreCase)))
        {
            var window = new BackdropWindow(monitor);
            window.Show();
            _backdrops.Add(window);
        }
    }

    private void UpdateScales()
    {
        var s = Math.Max(0.4, ActualHeight / 1080.0);
        PointerScale.ScaleX = PointerScale.ScaleY = Math.Clamp(s * 1.2, 0.8, 3);
        BubbleScale.ScaleX = BubbleScale.ScaleY = s;
        ToastLayer.LayoutTransform = new ScaleTransform(s, s);
    }

    // ---------------------------------------------------------------- pointer & bubble

    public void ApplyCursorMode()
    {
        _useSystemCursor = _host.Settings.Current.UseSystemCursor || ConsoleArt.IsConsoleShell(_host) ||
                           (_stack.Count > 0 && CurrentView is IScreenView { WantsSystemCursor: true });
        Cursor = _useSystemCursor ? null : Cursors.None;
        UpdatePointerVisibility();
    }

    private void UpdatePointerVisibility()
    {
        Pointer.Visibility = _pointerInside && !_useSystemCursor ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        _pointerTarget = e.GetPosition(RootGrid);
        _lastMove = DateTime.UtcNow;
        if (!_pointerInside)
        {
            _pointerInside = true;
            _pointerLast = _pointerTarget;
            UpdatePointerVisibility();
        }
        if (!_renderingHooked)
        {
            _renderingHooked = true;
            _lastFrame = DateTime.UtcNow;
            CompositionTarget.Rendering += OnRendering;
        }
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        var dt = Math.Max(0.004, (now - _lastFrame).TotalSeconds);
        _lastFrame = now;
        var velocity = (_pointerTarget.X - _pointerLast.X) / dt / Math.Max(0.5, ActualHeight / 1080.0);
        var target = Anim.Reduced ? 0 : Math.Clamp(velocity / 75.0, -18, 18);
        _pointerAngle += (target - _pointerAngle) * Math.Min(1, 0.32 * dt * 60);
        _pointerLast = _pointerTarget;

        Canvas.SetLeft(Pointer, _pointerTarget.X - HandPointer.Hotspot.X);
        Canvas.SetTop(Pointer, _pointerTarget.Y - HandPointer.Hotspot.Y);
        PointerRotate.Angle = _pointerAngle;
        if (_bubbleAnchor is null) PositionBubble();

        if ((now - _lastMove).TotalMilliseconds > 300 && Math.Abs(_pointerAngle) < 0.15)
        {
            CompositionTarget.Rendering -= OnRendering;
            _renderingHooked = false;
            PointerRotate.Angle = 0;
            _pointerAngle = 0;
        }
    }

    /// <summary>Shows the channel-name speech bubble near the pointer, or above <paramref name="anchor"/> for keyboard focus.</summary>
    public void ShowBubble(string? text, Point? anchor = null)
    {
        // The pointer bubble belongs to the Channels menu; the shells name things on screen instead.
        if (ConsoleArt.IsConsoleShell(_host)) text = null;
        if (string.IsNullOrEmpty(text))
        {
            Bubble.Visibility = Visibility.Collapsed;
            _bubbleAnchor = null;
            return;
        }
        BubbleText.Text = text;
        _bubbleAnchor = anchor;
        Bubble.Visibility = Visibility.Visible;
        PositionBubble();
    }

    private void PositionBubble()
    {
        if (Bubble.Visibility != Visibility.Visible) return;
        Bubble.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = Bubble.DesiredSize;
        var s = PointerScale.ScaleX;
        double x, y;
        if (_bubbleAnchor is { } anchor)
        {
            x = anchor.X - size.Width / 2;
            y = anchor.Y - size.Height - 6;
        }
        else
        {
            x = _pointerTarget.X + 30 * s;
            y = _pointerTarget.Y - size.Height - 4 * s;
            if (x + size.Width > ActualWidth - 10) x = _pointerTarget.X - size.Width - 18 * s;
        }
        Canvas.SetLeft(Bubble, Math.Clamp(x, 10, Math.Max(10, ActualWidth - size.Width - 10)));
        Canvas.SetTop(Bubble, Math.Clamp(y, 10, Math.Max(10, ActualHeight - size.Height - 10)));
    }

    public void ShowToast(string text)
    {
        ToastText.Text = text;
        Anim.To(Toast, OpacityProperty, 1, 200);
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    public void Flash()
    {
        if (Anim.Reduced) return;
        var a = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(760) };
        a.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        a.KeyFrames.Add(new EasingDoubleKeyFrame(0.92, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(170)), Anim.EaseOut));
        a.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(760)), Anim.EaseInOut));
        FlashLayer.BeginAnimation(OpacityProperty, a);
    }

    // ---------------------------------------------------------------- input routing

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_splashActive)
        {
            EndSplash();
            e.Handled = true;
            return;
        }
        if (IsDialogOpen)
        {
            if (e.Key == Key.Escape)
            {
                CloseDialog(null);
                e.Handled = true;
            }
            return;
        }
        if (e.Key is Key.Escape or Key.BrowserBack)
        {
            GoBack();
            e.Handled = true;
            return;
        }
        if (CurrentView is IScreenView view && view.HandleKey(e)) e.Handled = true;
    }

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_splashActive)
        {
            EndSplash();
            e.Handled = true;
            return;
        }
        if (e.ChangedButton == MouseButton.XButton1)
        {
            GoBack();
            e.Handled = true;
        }
        else if (e.ChangedButton == MouseButton.Right && CurrentView is not BrowserView && CurrentView is not IScreenView { CapturesMouseButtons: true } &&
                 !IsInsideTextBox(e.OriginalSource as DependencyObject) && !IsInsidePal(e.OriginalSource as DependencyObject))
        {
            GoBack();
            e.Handled = true;
        }
    }

    /// <summary>Right-clicking a Pal opens its menu rather than going back.</summary>
    private static bool IsInsidePal(DependencyObject? d)
    {
        while (d is not null)
        {
            if (d is Pals.AvatarView) return true;
            d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }
        return false;
    }

    private static bool IsInsideTextBox(DependencyObject? d)
    {
        while (d is not null)
        {
            if (d is TextBoxBase) return true;
            d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }
        return false;
    }

    public void ToggleHomeMenu()
    {
        if (_host.Options.IsSnapshot || _host.IsExiting) return;
        if (_home is { IsLoaded: true })
        {
            _home.CloseMenu();
            return;
        }
        var foreground = NativeMethods.GetForegroundWindow();
        NativeMethods.GetWindowThreadProcessId(foreground, out var pid);
        var target = pid == Environment.ProcessId ? IntPtr.Zero : foreground;
        _home = new HomeMenuWindow(_host, this, target);
        _home.Closed += (_, _) => _home = null;
        _home.Show();
        _home.Activate();
    }

    // ---------------------------------------------------------------- navigation

    public void Navigate(FrameworkElement view, Point? origin = null)
    {
        CloseDialog(null);
        var old = CurrentView;
        (old as IScreenView)?.OnHidden();
        AnimateOut(old, remove: false, enlarge: true, origin);

        _stack.Add(view);
        if (!ViewLayer.Children.Contains(view)) ViewLayer.Children.Add(view);
        view.Visibility = Visibility.Visible;
        AnimateIn(view, origin, fromSmall: true);
        ShowBubble(null);
        AfterViewChanged(view);
    }

    public void GoBack()
    {
        if (IsDialogOpen)
        {
            CloseDialog(null);
            return;
        }
        var current = CurrentView;
        if (current is IScreenView v && v.HandleBack()) return;
        if (_stack.Count <= 1) return;

        _stack.RemoveAt(_stack.Count - 1);
        (current as IScreenView)?.OnHidden();
        _host.Audio.Play(SoundEffect.Back);
        AnimateOut(current, remove: true, enlarge: false, null);

        var previous = CurrentView;
        previous.Visibility = Visibility.Visible;
        AnimateIn(previous, null, fromSmall: false);
        AfterViewChanged(previous);
    }

    /// <summary>The "Menu" button: leave the current screen for the channel menu, skipping in-view back handling.</summary>
    public void ReturnToMenu()
    {
        CloseDialog(null);
        if (_stack.Count == 2)
        {
            var current = CurrentView;
            _stack.RemoveAt(1);
            (current as IScreenView)?.OnHidden();
            _host.Audio.Play(SoundEffect.Back);
            AnimateOut(current, remove: true, enlarge: false, null);
            Menu.Element.Visibility = Visibility.Visible;
            AnimateIn(Menu.Element, null, fromSmall: false);
            AfterViewChanged(Menu.Element);
        }
        else if (_stack.Count > 2)
        {
            GoHome();
        }
    }

    public void GoHome()
    {
        CloseDialog(null);
        while (_stack.Count > 1)
        {
            var view = _stack[^1];
            _stack.RemoveAt(_stack.Count - 1);
            (view as IScreenView)?.OnHidden();
            ViewLayer.Children.Remove(view);
            (view as IDisposable)?.Dispose();
        }
        ShowHomeScreen();
    }

    /// <summary>Puts the home screen back on screen, reset from any transition it was left in.</summary>
    private void ShowHomeScreen()
    {
        var element = Menu.Element;
        element.BeginAnimation(OpacityProperty, null);
        element.Opacity = 1;
        element.RenderTransform = Transform.Identity;
        element.IsHitTestVisible = true;
        element.Visibility = Visibility.Visible;
        AfterViewChanged(element);
    }

    private IHomeScreen CreateHomeScreen(string styleId) => MenuStyleCatalog.Normalize(styleId) switch
    {
        MenuStyleCatalog.Dashboard => new DashboardView(_host, this),
        MenuStyleCatalog.MediaBar => new MediaBarView(_host, this),
        _ => new MenuView(_host, this),
    };

    /// <summary>
    /// Switches the home screen between menu styles without restarting: channels, settings, open screens and
    /// everything else stay as they are, only the menu in front of them is replaced.
    /// </summary>
    public void ApplyMenuStyle(string styleId)
    {
        styleId = MenuStyleCatalog.Normalize(styleId);
        if (Menu.StyleId == styleId) return;

        ShowBubble(null);
        _host.Settings.Current.MenuStyle = styleId;
        ThemeManager.ApplyFor(styleId, _host.Settings.Current.Theme);
        _host.Audio.SetShellSounds(styleId);
        var old = Menu;
        var wasInFront = CurrentView == old.Element;
        var next = CreateHomeScreen(styleId);

        Menu = next;
        _stack[0] = next.Element;
        ViewLayer.Children.Insert(Math.Max(0, ViewLayer.Children.IndexOf(old.Element)), next.Element);
        next.Element.Visibility = wasInFront ? Visibility.Visible : Visibility.Hidden;
        next.Element.IsHitTestVisible = wasInFront;

        old.SetActive(false);
        (old as IDisposable)?.Dispose();
        ViewLayer.Children.Remove(old.Element);

        if (wasInFront)
        {
            ShowHomeScreen();
            Anim.To(next.Element, OpacityProperty, 1, 280, Anim.EaseOut, from: 0);
            next.PlayIntro();
        }
        // Pals are part of the Channels menu; the console shells show nothing of them.
        _palPeek.Hide();
        _host.Pals.SetSuspended(ConsoleArt.IsConsoleShell(_host));
        ApplyCursorMode();
        RebuildBackdrops();
        _bar?.RefreshForMenuStyle();
        UpdateActivity();
        Log.Info("Menu style: " + styleId);
    }

    private readonly Pals.PalPeek _palPeek;

    private void AfterViewChanged(FrameworkElement view)
    {
        // A screen with its own Pal doesn't need the corner one too.
        if (ConsoleArt.IsConsoleShell(_host) || view is Pals.IPalHost { ShowsPal: true }) _palPeek.Hide();
        ApplyCursorMode();
        (view as IScreenView)?.OnShown();
        UpdateActivity();
        Dispatcher.BeginInvoke(() =>
        {
            if (CurrentView == view && !view.IsKeyboardFocusWithin && !IsDialogOpen)
            {
                if (view.Focusable) view.Focus();
                else view.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
            }
        }, DispatcherPriority.Input);
    }

    private void AnimateIn(FrameworkElement view, Point? origin, bool fromSmall)
    {
        view.IsHitTestVisible = true;
        var scale = new ScaleTransform(fromSmall ? 0.8 : 1.06, fromSmall ? 0.8 : 1.06);
        view.RenderTransform = scale;
        view.RenderTransformOrigin = RelativeOrigin(origin);
        Anim.To(scale, ScaleTransform.ScaleXProperty, 1, 360, Anim.EaseOut);
        Anim.To(scale, ScaleTransform.ScaleYProperty, 1, 360, Anim.EaseOut);
        Anim.To(view, OpacityProperty, 1, 260, from: 0);
    }

    private void AnimateOut(FrameworkElement view, bool remove, bool enlarge, Point? origin)
    {
        view.IsHitTestVisible = false;
        var scale = new ScaleTransform(1, 1);
        view.RenderTransform = scale;
        view.RenderTransformOrigin = RelativeOrigin(origin);
        var to = enlarge ? 1.12 : 0.86;
        Anim.To(scale, ScaleTransform.ScaleXProperty, to, 280, Anim.EaseInOut);
        Anim.To(scale, ScaleTransform.ScaleYProperty, to, 280, Anim.EaseInOut);
        Anim.To(view, OpacityProperty, 0, 240, completed: () =>
        {
            if (remove)
            {
                ViewLayer.Children.Remove(view);
                (view as IDisposable)?.Dispose();
            }
            else if (_stack.Count > 0 && CurrentView != view)
            {
                view.Visibility = Visibility.Collapsed;
            }
        });
    }

    private Point RelativeOrigin(Point? origin)
    {
        if (origin is not { } o || ActualWidth <= 0 || ActualHeight <= 0) return new Point(0.5, 0.5);
        return new Point(Math.Clamp(o.X / ActualWidth, 0, 1), Math.Clamp(o.Y / ActualHeight, 0, 1));
    }

    // ---------------------------------------------------------------- dialogs

    public async Task<string?> ShowDialogAsync(string message, params string[] buttons)
    {
        var result = await ShowCustomDialogAsync(close =>
        {
            var stack = new StackPanel();
            stack.Children.Add(ViewKit.Text(message, 38, FontWeights.Bold, align: TextAlignment.Center));
            ((TextBlock)stack.Children[0]).Margin = new Thickness(0, 10, 0, 44);
            ((TextBlock)stack.Children[0]).MaxWidth = 1100;
            var row = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };
            foreach (var label in buttons.Length == 0 ? new[] { "OK" } : buttons)
            {
                var captured = label;
                row.Children.Add(ViewKit.Pill(label, () => close(captured), 300));
            }
            stack.Children.Add(row);
            return ViewKit.Panel(stack, 1260);
        });
        return result as string;
    }

    public Task<object?> ShowCustomDialogAsync(Func<Action<object?>, FrameworkElement> build)
    {
        if (_dialog is not null) CloseDialog(null);
        var tcs = new TaskCompletionSource<object?>();
        _dialog = tcs;
        ShowBubble(null);
        DialogHost.Content = build(result =>
        {
            if (ReferenceEquals(_dialog, tcs)) CloseDialog(result);
        });
        DialogLayer.Visibility = Visibility.Visible;
        DialogLayer.IsHitTestVisible = true;
        Anim.To(DialogLayer, OpacityProperty, 1, 180, from: 0);
        Anim.To(DialogScale, ScaleTransform.ScaleXProperty, 1, 320, Anim.Springy, from: 0.85);
        Anim.To(DialogScale, ScaleTransform.ScaleYProperty, 1, 320, Anim.Springy, from: 0.85);
        UpdateActivity();
        Dispatcher.BeginInvoke(() =>
        {
            if (DialogHost.Content is FrameworkElement content) content.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        }, DispatcherPriority.Input);
        return tcs.Task;
    }

    public void CloseDialog(object? result)
    {
        var tcs = _dialog;
        if (tcs is null) return;
        _dialog = null;
        DialogLayer.IsHitTestVisible = false;
        Anim.To(DialogLayer, OpacityProperty, 0, 160, completed: () =>
        {
            if (_dialog is not null) return;
            DialogLayer.Visibility = Visibility.Collapsed;
            DialogHost.Content = null;
        });
        tcs.TrySetResult(result);
        UpdateActivity();
        Dispatcher.BeginInvoke(() =>
        {
            if (_dialog is null && CurrentView is { } view && !view.IsKeyboardFocusWithin)
            {
                if (view.Focusable) view.Focus();
                else view.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
            }
        }, DispatcherPriority.Input);
    }

    // ---------------------------------------------------------------- snapshot mode (visual regression renders)

    private async Task RunSnapshotsAsync()
    {
        var dir = _host.Options.SnapshotDirectory!;
        Directory.CreateDirectory(dir);
        try
        {
            // Developers can render just one area while iterating (e.g. COUCHTOP_SNAPSHOT_ONLY=pals).
            if (Environment.GetEnvironmentVariable("COUCHTOP_SNAPSHOT_ONLY") == "palclips")
            {
                await Task.Delay(2500);
                await SavePalClipsAsync(dir);
                _host.Exit(0);
                return;
            }
            if (Environment.GetEnvironmentVariable("COUCHTOP_SNAPSHOT_ONLY") == "tour")
            {
                await Task.Delay(2500);
                await SaveTourSnapshotsAsync(dir);
                _host.Exit(0);
                return;
            }
            if (Environment.GetEnvironmentVariable("COUCHTOP_SNAPSHOT_ONLY") == "menus")
            {
                await Task.Delay(2500);
                await SaveMenuStyleSnapshotsAsync(dir);
                _host.Exit(0);
                return;
            }
            if (Environment.GetEnvironmentVariable("COUCHTOP_SNAPSHOT_ONLY") == "pals")
            {
                await Task.Delay(2500);
                await SavePalSnapshotsAsync(dir);
                _host.Exit(0);
                return;
            }
            await Task.Delay(4000);
            _pointerInside = true;
            _useSystemCursor = false;
            UpdatePointerVisibility();
            var hover = Math.Min(6, _host.Layout.Layout.Channels.Count - 1);
            var point = Menu.SlotCenter(hover);
            _pointerTarget = new Point(point.X + 40, point.Y + 30);
            Canvas.SetLeft(Pointer, _pointerTarget.X - HandPointer.Hotspot.X);
            Canvas.SetTop(Pointer, _pointerTarget.Y - HandPointer.Hotspot.Y);
            PointerRotate.Angle = -9;
            Menu.SnapshotHover(hover);
            await Task.Delay(900);
            Save(dir, "01-menu");
            _pointerInside = false;
            UpdatePointerVisibility();
            Menu.SnapshotHover(-1);

            Menu.SetEditMode(true);
            await Task.Delay(1200);
            Save(dir, "02-customize");
            Menu.SetEditMode(false);
            await Task.Delay(600);

            var app = _host.Layout.Layout.Channels.FirstOrDefault(c => c.Kind != ChannelKind.BuiltIn);
            if (app is not null)
            {
                Navigate(new ChannelPreviewView(_host, this, app));
                await Task.Delay(1600);
                Save(dir, "03-preview");
                GoHome();
            }

            var settings = new SettingsView(_host, this);
            Navigate(settings);
            await Task.Delay(1000);
            Save(dir, "04-settings");
            settings.SelectCategory("Sound");
            await Task.Delay(1000);
            Save(dir, "04b-sound");
            settings.SnapshotScrollToEnd();
            await Task.Delay(900);
            Save(dir, "04c-sound-custom");
            settings.SelectCategory("Desktop");
            await Task.Delay(900);
            Save(dir, "04d-desktop");
            settings.SelectCategory("Shell Mode");
            await Task.Delay(1000);
            Save(dir, "05-shell-mode");
            GoHome();

            Navigate(new SafetyTestView(_host, this));
            await Task.Delay(1000);
            Save(dir, "06-safety-test");
            GoHome();

            Navigate(new FilesView(_host, this));
            await Task.Delay(2500);
            Save(dir, "07-files");
            GoHome();

            Navigate(new PhotosView(_host, this));
            await Task.Delay(2500);
            Save(dir, "08-photos");
            GoHome();

            Navigate(new PowerView(_host, this));
            await Task.Delay(1000);
            Save(dir, "09-power");
            GoHome();

            await SaveSportsSnapshotsAsync(dir);
            await SaveDesktopSnapshotsAsync(dir);

            _ = ShowDialogAsync("Remove \"Example\" from your channels?\nThe app itself stays installed.", "Remove", "Cancel");
            await Task.Delay(900);
            Save(dir, "10-dialog");
            CloseDialog(null);
            await Task.Delay(500);

            await HomeMenuWindow.RenderSnapshotAsync(_host, this, Path.Combine(dir, "11-home-menu.png"));

            await SaveWebSnapshotAsync(dir, "web");

            ApplyTheme("Night");
            await Task.Delay(1200);
            Save(dir, "12-night");

            // One set of renders per extra theme, for reviewing every look side by side.
            foreach (var theme in ThemeCatalog.All.Where(t => t.Id is not ("Classic" or "Night")))
            {
                _host.Settings.Current.Theme = theme.Id; // in memory only, so Settings shows the right choice
                ApplyTheme(theme.Id);
                await Task.Delay(1500);
                _pointerInside = true;
                _useSystemCursor = false;
                UpdatePointerVisibility();
                Menu.SnapshotHover(hover);
                await Task.Delay(900);
                Save(dir, $"theme-{theme.Id}-1-menu");
                _pointerInside = false;
                UpdatePointerVisibility();
                Menu.SnapshotHover(-1);

                if (app is not null)
                {
                    Navigate(new ChannelPreviewView(_host, this, app));
                    await Task.Delay(1600);
                    Save(dir, $"theme-{theme.Id}-2-preview");
                    GoHome();
                }

                var themedSettings = new SettingsView(_host, this);
                Navigate(themedSettings);
                await Task.Delay(1000);
                Save(dir, $"theme-{theme.Id}-3-settings");
                GoHome();

                Navigate(new PowerView(_host, this));
                await Task.Delay(1000);
                Save(dir, $"theme-{theme.Id}-4-power");
                GoHome();
                await Task.Delay(500);

                Menu.SetEditMode(true);
                await Task.Delay(1200);
                Save(dir, $"theme-{theme.Id}-6-customize");
                Menu.SetEditMode(false);
                await Task.Delay(600);

                Navigate(new FilesView(_host, this));
                await Task.Delay(2000);
                Save(dir, $"theme-{theme.Id}-7-files");
                GoHome();
                await Task.Delay(500);

                Navigate(new Sports.SportsComingSoonView(_host, this));
                await Task.Delay(1200);
                Save(dir, $"theme-{theme.Id}-9-sports");
                GoHome();
                await Task.Delay(500);

                await SaveWebSnapshotAsync(dir, $"theme-{theme.Id}-8-web");

                await HomeMenuWindow.RenderSnapshotAsync(_host, this, Path.Combine(dir, $"theme-{theme.Id}-5-quick.png"));
            }
        }
        catch (Exception ex)
        {
            Log.Error("Snapshot rendering failed", ex);
        }
        _host.Exit(0);
    }

    /// <summary>Renders every step of the welcome tour, in each shell the tour can be read in.</summary>
    private async Task SaveTourSnapshotsAsync(string dir)
    {
        foreach (var style in new[] { Core.Settings.MenuStyleCatalog.Channels, Core.Settings.MenuStyleCatalog.Dashboard })
        {
            ApplyMenuStyle(style);
            await Task.Delay(900);
            var tour = new WelcomeView(_host, this);
            Navigate(tour);
            await Task.Delay(900);
            for (var step = 0; step < 7; step++)
            {
                tour.SnapshotStep(step);
                await Task.Delay(700);
                Save(dir, $"tour-{style}-{step + 1}");
            }
            GoHome();
            await Task.Delay(400);
        }
    }

    /// <summary>Renders every menu style across the screens it touches, for reviewing the shells side by side.</summary>
    private async Task SaveMenuStyleSnapshotsAsync(string dir)
    {
        var app = _host.Layout.Layout.Channels.FirstOrDefault(c => c.Kind != ChannelKind.BuiltIn);
        foreach (var style in Core.Settings.MenuStyleCatalog.All)
        {
            ApplyMenuStyle(style.Id);
            await Task.Delay(1500);
            Save(dir, $"style-{style.Id}-1-home");

            switch (Menu)
            {
                case DashboardView dashboard:
                    dashboard.SnapshotSelect(2, 1);
                    break;
                case MediaBarView bar:
                    bar.SnapshotSelect(2, 2);
                    break;
                default:
                    Menu.SnapshotHover(5);
                    break;
            }
            await Task.Delay(1100);
            Save(dir, $"style-{style.Id}-2-home");

            if (app is not null)
            {
                Navigate(new ChannelPreviewView(_host, this, app));
                await Task.Delay(1500);
                Save(dir, $"style-{style.Id}-3-start");
                GoHome();
                await Task.Delay(500);
            }

            Navigate(new SettingsView(_host, this));
            await Task.Delay(1200);
            Save(dir, $"style-{style.Id}-4-settings");
            GoHome();
            await Task.Delay(400);

            Navigate(new FilesView(_host, this));
            await Task.Delay(2000);
            Save(dir, $"style-{style.Id}-5-files");
            GoHome();
            await Task.Delay(400);

            Navigate(new PowerView(_host, this));
            await Task.Delay(1000);
            Save(dir, $"style-{style.Id}-6-power");
            GoHome();
            await Task.Delay(400);

            await HomeMenuWindow.RenderSnapshotAsync(_host, this, Path.Combine(dir, $"style-{style.Id}-7-quick.png"));
        }
        ApplyMenuStyle(_host.Settings.Current.MenuStyle);
        await Task.Delay(300);
    }

    /// <summary>
    /// Renders the desktop-shell windows (bar, search, switcher) without showing them, so the renders never
    /// disturb the real desktop and never capture the real window titles of whoever runs them.
    /// </summary>
    private async Task SaveDesktopSnapshotsAsync(string dir)
    {
        var bar = new CouchtopBar(_host, this);
        bar.SnapshotPrepare();
        SaveVisual((FrameworkElement)bar.Content, 1920, CouchtopBar.BarHeight * 2, dir, "09e-couchtop-bar");

        var palette = new CommandPaletteWindow(_host, this);
        await palette.SnapshotPrepareAsync("se");
        SaveVisual((FrameworkElement)palette.Content, 980, 660, dir, "09f-search");

        var switcher = new TaskSwitcherWindow(_host, this);
        switcher.SnapshotPrepare();
        SaveVisual((FrameworkElement)switcher.Content, 1600, 900, dir, "09g-task-switcher");

        var status = new StatusCenterWindow(_host, this);
        status.SnapshotPrepare();
        SaveVisual((FrameworkElement)status.Content, 520, 700, dir, "09h-status-center");
        await Task.Delay(200);
    }

    private static void SaveVisual(FrameworkElement content, double width, double height, string dir, string name)
    {
        content.Measure(new Size(width, height));
        content.Arrange(new Rect(0, 0, width, height));
        content.UpdateLayout();
        Log.Info($"snapshot {name}: content {content.ActualWidth:0}x{content.ActualHeight:0}");
        var bitmap = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var fs = File.Create(Path.Combine(dir, name + ".png"));
        encoder.Save(fs);
        Log.Info("Snapshot saved: " + name);
    }

    private async Task SaveSportsSnapshotsAsync(string dir)
    {
        Navigate(new Sports.SportsComingSoonView(_host, this));
        await Task.Delay(1400);
        Save(dir, "09a-sports-coming-soon");
        GoHome();

#if DEBUG
        // The unfinished games only exist in developer builds.
        Navigate(new Sports.SportsHubView(_host, this));
        await Task.Delay(1400);
        Save(dir, "09a-sports-hub");
        GoHome();

        Navigate(new Sports.SportMenuView(_host, this, Couchtop.Core.Sports.SportKind.Bowling));
        await Task.Delay(1200);
        Save(dir, "09b-sports-menu");
        GoHome();

        // Computer-vs-computer demos (no records are written).
        foreach (var sport in Enum.GetValues<Couchtop.Core.Sports.SportKind>())
        {
            var setup = new Couchtop.Core.Sports.SportSetup(sport, Players: 2, Humans: 0, Difficulty: 3, Seed: 7);
            var view = Sports.SportsSession.CreateView(_host, this, setup, demo: true);
            Navigate(view);
            await Task.Delay(sport switch
            {
                Couchtop.Core.Sports.SportKind.Bowling => 3600,
                Couchtop.Core.Sports.SportKind.Golf => 2300,
                Couchtop.Core.Sports.SportKind.Baseball => 3000,
                _ => 5000,
            });
            Save(dir, $"09c-sports-{sport.ToString().ToLowerInvariant()}");
            GoHome();
            await Task.Delay(400);
        }

        var results = Sports.SportsSession.CreateView(_host, this, new Couchtop.Core.Sports.SportSetup(Couchtop.Core.Sports.SportKind.Golf, Couchtop.Core.Sports.SportMode.Training, 1, 0, Seed: 3), demo: true);
        Navigate(results);
        await Task.Delay(1200);
        results.ShowSnapshotResults(new Couchtop.Core.Sports.SportResult(Couchtop.Core.Sports.SportKind.Golf, Couchtop.Core.Sports.SportMode.Training, new[] { 44 }, null, "44 points", "Nearest the Pin", 44));
        await Task.Delay(500);
        Save(dir, "09d-sports-results");
        GoHome();
        await Task.Delay(400);
#endif
    }

    private async Task SavePalSnapshotsAsync(string dir)
    {
        // A lineup of random Pals, each mid-gesture, for judging the look without clicking through the editor.
        var gestures = new[] { Core.Pals.PalGesture.None, Core.Pals.PalGesture.Wave, Core.Pals.PalGesture.Cheer, Core.Pals.PalGesture.Think, Core.Pals.PalGesture.Shrug, Core.Pals.PalGesture.Dance };
        var moods = new[] { Core.Pals.PalMood.Happy, Core.Pals.PalMood.Excited, Core.Pals.PalMood.Surprised, Core.Pals.PalMood.Thinking, Core.Pals.PalMood.Cheeky, Core.Pals.PalMood.Neutral };
        var sheet = new DrawingVisual();
        using (var dc = sheet.RenderOpen())
        {
            dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(232, 244, 250), Color.FromRgb(196, 222, 236), 90), null, new Rect(0, 0, 1920, 1080));
            for (var i = 0; i < 12; i++)
            {
                var profile = Core.Pals.PalProfile.Random(100 + i);
                var picture = Pals.PalPortrait.Render(profile, 300, 500, Pals.AvatarFraming.FullBody, gestures[i % gestures.Length], moods[i % moods.Length], 0.7, i % 3 == 1 ? 25 : 0);
                dc.DrawImage(picture, new Rect(20 + (i % 6) * 315, 20 + (i / 6) * 520, 300, 500));
            }
        }
        var bitmap = new RenderTargetBitmap(1920, 1080, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(sheet);
        Pals.PalPortrait.Save(bitmap, Path.Combine(dir, "pals-lineup.png"));

        var faces = new DrawingVisual();
        using (var dc = faces.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(236, 242, 246)), null, new Rect(0, 0, 1920, 1080));
            var i = 0;
            foreach (var mood in Enum.GetValues<Core.Pals.PalMood>())
            {
                var profile = Core.Pals.PalProfile.Random(200 + i);
                profile.Hat = "none";
                dc.DrawImage(Pals.PalPortrait.Render(profile, 460, 500, Pals.AvatarFraming.Face, mood: mood), new Rect(10 + (i % 4) * 475, 20 + (i / 4) * 520, 460, 500));
                i++;
            }
        }
        bitmap = new RenderTargetBitmap(1920, 1080, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(faces);
        Pals.PalPortrait.Save(bitmap, Path.Combine(dir, "pals-faces.png"));
        var moves = new DrawingVisual();
        using (var dc = moves.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(236, 242, 246)), null, new Rect(0, 0, 1920, 1080));
            var profile = Core.Pals.PalProfile.Random(7);
            var all = Enum.GetValues<Core.Pals.PalGesture>().Where(g => g is not (Core.Pals.PalGesture.Sleep or Core.Pals.PalGesture.Sit)).ToList();
            for (var i = 0; i < all.Count; i++)
            {
                var picture = Pals.PalPortrait.Render(profile, 190, 330, Pals.AvatarFraming.FullBody, all[i], Core.Pals.PalMood.Happy, all[i] == Core.Pals.PalGesture.None ? 0.6 : 0.75);
                dc.DrawImage(picture, new Rect(10 + (i % 10) * 190, 20 + (i / 10) * 360, 190, 330));
                var label = new FormattedText(all[i].ToString(), System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 18, Brushes.Black, 1);
                dc.DrawText(label, new Point(20 + (i % 10) * 190, 340 + (i / 10) * 360));
            }
        }
        bitmap = new RenderTargetBitmap(1920, 1080, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(moves);
        Pals.PalPortrait.Save(bitmap, Path.Combine(dir, "pals-gestures.png"));
        await SavePalScreensAsync(dir);
        Log.Info("Snapshot saved: pals");
        await Task.Delay(100);
    }

    /// <summary>Transparent Pal animation clips and clean backdrops for promo videos (COUCHTOP_SNAPSHOT_ONLY=palclips).</summary>
    private async Task SavePalClipsAsync(string dir)
    {
        Directory.CreateDirectory(dir);
        var pip = new Core.Pals.PalProfile
        {
            Name = "Pip", HairStyle = "swept", HairColor = "#6E4A2F", Skin = "#E8B48C", EyeStyle = "sparkle", EyeColor = "#3C6FA8",
            TopStyle = "hoodie", TopColor = "#35B4E5", TopAccent = "#FFFFFF", TopPattern = "star", BottomStyle = "jeans", BottomColor = "#3E5C8A",
            ShoeStyle = "sneakers", ShoeColor = "#F25C54", Cheeks = "blush", Personality = "cheerful",
        }.Normalize();
        var momo = new Core.Pals.PalProfile
        {
            Name = "Momo", HairStyle = "twin-tails", HairColor = "#E86F9A", HairTips = "#FFD35A", Skin = "#F6D2B6", EyeStyle = "lashes", EyeColor = "#7B5AA6",
            TopStyle = "tee", TopColor = "#FFD35A", TopAccent = "#F25C54", TopPattern = "heart", BottomStyle = "skirt", BottomColor = "#2E3440",
            ShoeStyle = "high-tops", ShoeColor = "#FFFFFF", Hat = "bow", HatColor = "#F25C54", Cheeks = "both", MouthStyle = "smirk", Personality = "cheeky",
        }.Normalize();
        var kai = new Core.Pals.PalProfile
        {
            Name = "Kai", HairStyle = "spiky", HairColor = "#1F1B1C", Skin = "#A86F48", EyeStyle = "focused", EyeColor = "#2A211D", BrowStyle = "determined",
            TopStyle = "jacket", TopColor = "#8BD66A", TopAccent = "#2E3440", BottomStyle = "joggers", BottomColor = "#2E3440", ShoeStyle = "sneakers", ShoeColor = "#35B4E5",
            Hat = "headphones", HatColor = "#F25C54", Personality = "sporty",
        }.Normalize();

        const int w = 540, h = 700;
        var full = Pals.AvatarFraming.FullBody;
        // COUCHTOP_CLIP_FILTER=momo re-renders just the clips whose names start with it.
        var only = Environment.GetEnvironmentVariable("COUCHTOP_CLIP_FILTER");
        bool Wanted(string name) => string.IsNullOrEmpty(only) || name.StartsWith(only, StringComparison.OrdinalIgnoreCase);
        void Clip(string name, Core.Pals.PalProfile p, double seconds, Action<Pals.AvatarView, int, double> script, double facing = 0)
        {
            if (Wanted(name)) Pals.PalClipExporter.Render(dir, name, p, w, h, full, seconds, script, facing);
        }

        Clip("pip-walk", pip, 3, (v, i, _) => { if (i == 0) v.Animator!.Base = Pals.AvatarBase.Walk; }, facing: 72);
        Clip("pip-wave", pip, 3.5, (v, i, _) => { if (i == 0) { v.Animator!.Play(Core.Pals.PalGesture.Wave, Core.Pals.PalMood.Happy); v.Animator.Talk(2.6); } });
        Clip("pip-poke", pip, 2.6, (v, i, _) =>
        {
            if (i == 0) v.Animator!.Play(Core.Pals.PalGesture.Surprised, Core.Pals.PalMood.Surprised);
            if (i == 12) { v.Animator!.Play(Core.Pals.PalGesture.Laugh, Core.Pals.PalMood.Excited); v.Animator.Talk(1.2); }
        });
        Clip("pip-held", pip, 3.5, (v, i, t) =>
        {
            if (i == 0) { v.Animator!.Base = Pals.AvatarBase.Held; v.Animator.RestingMood = Core.Pals.PalMood.Excited; }
            v.Animator!.HeldSway = 26 * Math.Sin(t * 3.4);
            if (i == 8) v.Animator.Talk(1.2);
        });
        Clip("pip-land", pip, 2.2, (v, i, _) =>
        {
            if (i == 0) v.Animator!.Base = Pals.AvatarBase.Held;
            if (i == 3) { v.Animator!.Base = Pals.AvatarBase.Idle; v.Animator.RestingMood = Core.Pals.PalMood.Happy; }
            if (i == 16) { v.Animator!.Play(Core.Pals.PalGesture.ThumbsUp, Core.Pals.PalMood.Happy); v.Animator.Talk(1.0); }
        });
        Clip("pip-cheer", pip, 2.4, (v, i, _) => { if (i == 0) v.Animator!.Play(Core.Pals.PalGesture.Cheer, Core.Pals.PalMood.Excited); });
        Clip("momo-dance", momo, 4, (v, i, _) =>
        {
            if (i == 0) { v.Animator!.RestingMood = Core.Pals.PalMood.Cheeky; v.Animator.Play(Core.Pals.PalGesture.Dance, Core.Pals.PalMood.Cheeky); v.Animator.Talk(2.2); }
            if (i == 96) v.Animator!.Play(Core.Pals.PalGesture.Dance, Core.Pals.PalMood.Cheeky);
        });
        Clip("momo-point", momo, 2.8, (v, i, _) =>
        {
            if (i == 0) { v.Animator!.RestingMood = Core.Pals.PalMood.Cheeky; v.Animator.Play(Core.Pals.PalGesture.Point, Core.Pals.PalMood.Cheeky); v.Animator.Talk(2.0); }
        });
        Clip("momo-spin", momo, 2.2, (v, i, _) =>
        {
            if (i == 0) { v.Animator!.RestingMood = Core.Pals.PalMood.Cheeky; v.Animator.Play(Core.Pals.PalGesture.Spin, Core.Pals.PalMood.Excited); }
            if (i == 40) v.Animator!.Play(Core.Pals.PalGesture.Laugh, Core.Pals.PalMood.Cheeky);
        });
        Clip("kai-sleep", kai, 3, (v, i, _) => { if (i == 0) v.Animator!.Base = Pals.AvatarBase.Sleep; });
        Clip("kai-wake", kai, 2.6, (v, i, _) =>
        {
            if (i == 0) v.Animator!.Base = Pals.AvatarBase.Sleep;
            if (i == 6) { v.Animator!.Base = Pals.AvatarBase.Idle; v.Animator.Play(Core.Pals.PalGesture.Surprised, Core.Pals.PalMood.Surprised); v.Animator.Talk(1.6); }
        });
        Clip("kai-stretch", kai, 3, (v, i, _) => { if (i == 0) { v.Animator!.Play(Core.Pals.PalGesture.Stretch, Core.Pals.PalMood.Happy); v.Animator.Talk(1.8); } });

        // Extra reactions for the second batch of Shorts ("x-" so they can be rendered on their own).
        void React(string name, Core.Pals.PalProfile p, Core.Pals.PalGesture gesture, Core.Pals.PalMood mood, double seconds, double talk = 1.6) =>
            Clip(name, p, seconds, (v, i, _) =>
            {
                if (i != 0) return;
                v.Animator!.RestingMood = mood;
                v.Animator.Play(gesture, mood);
                if (talk > 0) v.Animator.Talk(talk);
            });
        React("x-pip-shake", pip, Core.Pals.PalGesture.ShakeHead, Core.Pals.PalMood.Surprised, 2.4);
        React("x-pip-shrug", pip, Core.Pals.PalGesture.Shrug, Core.Pals.PalMood.Cheeky, 2.4);
        React("x-pip-point", pip, Core.Pals.PalGesture.Point, Core.Pals.PalMood.Cheeky, 2.4);
        React("x-pip-bow", pip, Core.Pals.PalGesture.Bow, Core.Pals.PalMood.Happy, 2.6);
        React("x-pip-thumbs", pip, Core.Pals.PalGesture.ThumbsUp, Core.Pals.PalMood.Happy, 2.4);
        React("x-pip-look", pip, Core.Pals.PalGesture.LookAround, Core.Pals.PalMood.Excited, 3.0, 2.0);
        React("x-pip-jump", pip, Core.Pals.PalGesture.Jump, Core.Pals.PalMood.Excited, 1.4, 0);
        React("x-pip-surprised", pip, Core.Pals.PalGesture.Surprised, Core.Pals.PalMood.Surprised, 2.0);
        React("x-kai-shrug", kai, Core.Pals.PalGesture.Shrug, Core.Pals.PalMood.Cheeky, 2.4);
        React("x-momo-laugh", momo, Core.Pals.PalGesture.Laugh, Core.Pals.PalMood.Cheeky, 2.4);
        Clip("x-kai-sit", kai, 3, (v, i, _) => { if (i == 0) v.Animator!.Base = Pals.AvatarBase.Sit; });
        var extraMoves = new[] { Core.Pals.PalGesture.Dance, Core.Pals.PalGesture.Wave, Core.Pals.PalGesture.Clap, Core.Pals.PalGesture.Cheer, Core.Pals.PalGesture.Spin, Core.Pals.PalGesture.ThumbsUp };
        for (var k = 0; k < extraMoves.Length && Wanted("x-lineup"); k++)
        {
            var move = extraMoves[k];
            Pals.PalClipExporter.Render(dir, $"x-lineup-{k + 6}", Core.Pals.PalProfile.Random(140 + k * 7), 360, 480, full, 2, (v, i, _) =>
            {
                if (i == 0) v.Animator!.Play(move, Core.Pals.PalMood.Excited);
            });
        }

        // Close-up for the hook: Momo winking and chatting.
        if (Wanted("momo-face")) Pals.PalClipExporter.Render(dir, "momo-face", momo, 720, 720, Pals.AvatarFraming.Face, 3, (v, i, _) =>
        {
            if (i == 0) { v.Animator!.RestingMood = Core.Pals.PalMood.Cheeky; v.Animator.Talk(2.6); }
        });

        // A lineup of different Pals, each doing its own thing.
        var moves = new[] { Core.Pals.PalGesture.Wave, Core.Pals.PalGesture.Cheer, Core.Pals.PalGesture.Spin, Core.Pals.PalGesture.ThumbsUp, Core.Pals.PalGesture.Dance, Core.Pals.PalGesture.Jump };
        for (var k = 0; k < moves.Length && Wanted("lineup"); k++)
        {
            var move = moves[k];
            Pals.PalClipExporter.Render(dir, $"lineup-{k}", Core.Pals.PalProfile.Random(100 + k), 360, 480, full, 2, (v, i, _) =>
            {
                if (i == 0) v.Animator!.Play(move, Core.Pals.PalMood.Excited);
            });
        }

        Pals.PalClipExporter.RenderElement(new HandPointer(), 64, 80, Path.Combine(dir, "hand.png"), 3);
        if (!string.IsNullOrEmpty(only)) return;

        // Clean home screens (Pip on the Pals tile, but not walking around) in a few themes.
        _host.Pals.SaveProfile(pip);
        _host.Pals.Preferences.ShowOnHome = false;
        _host.Pals.SavePreferences();
        GoHome();
        foreach (var theme in new[] { "Classic", "NeonCity", "Sakura", "Sunset" })
        {
            ApplyTheme(theme);
            await Task.Delay(1500);
            Save(dir, "backdrop-" + theme.ToLowerInvariant());
        }
        ApplyTheme("Classic");
        await Task.Delay(800);
        var studio = new Pals.PalStudioView(_host, this);
        Navigate(studio);
        await Task.Delay(1200);
        foreach (var category in new[] { "Hair", "Extras" })
        {
            studio.SnapshotPrepare(category);
            await Task.Delay(600);
            Save(dir, "studio-" + category.ToLowerInvariant());
        }
        GoHome();
        Log.Info("Snapshot saved: pal clips");
    }

    /// <summary>The Pal where people meet it: Pal Studio, the home screen, a start screen, Power, the peek and the bar.</summary>
    private async Task SavePalScreensAsync(string dir)
    {
        var studio = new Pals.PalStudioView(_host, this);
        Navigate(studio);
        await Task.Delay(1200);
        foreach (var category in new[] { "Body", "Face", "Hair", "Extras", "Personality" })
        {
            studio.SnapshotPrepare(category);
            await Task.Delay(700);
            Save(dir, "pals-studio-" + category.ToLowerInvariant());
        }
        GoHome();
        await Task.Delay(800);

        var pip = new Core.Pals.PalProfile
        {
            Name = "Pip", HairStyle = "swept", HairColor = "#6E4A2F", Skin = "#E8B48C", EyeStyle = "sparkle", EyeColor = "#3C6FA8",
            TopStyle = "hoodie", TopColor = "#35B4E5", TopAccent = "#FFFFFF", TopPattern = "star", BottomStyle = "jeans", BottomColor = "#3E5C8A",
            ShoeStyle = "sneakers", ShoeColor = "#F25C54", Cheeks = "blush", Personality = "cheerful",
        }.Normalize();
        _host.Pals.SaveProfile(pip);
        await Task.Delay(1500);
        ((MenuView)Menu).SnapshotPal(650, Core.Pals.PalGesture.Wave, "Hi! I'm Pip. So this is Couchtop? It's lovely in here!");
        await Task.Delay(600);
        Save(dir, "pals-home");
        ((MenuView)Menu).SnapshotPal(1290, Core.Pals.PalGesture.None, null);
        await Task.Delay(400);
        Save(dir, "pals-home-right");

        var app = _host.Layout.Layout.Channels.FirstOrDefault(c => c.Kind != ChannelKind.BuiltIn);
        if (app is not null)
        {
            var preview = new ChannelPreviewView(_host, this, app);
            Navigate(preview);
            await Task.Delay(1400);
            preview.SnapshotPal(Core.Pals.PalGesture.Cheer, "Ooh, " + app.Title + "! Show them what you've got.");
            await Task.Delay(500);
            Save(dir, "pals-preview");
            GoHome();
            await Task.Delay(600);
        }

        var power = new PowerView(_host, this);
        Navigate(power);
        await Task.Delay(1200);
        power.SnapshotPal(Core.Pals.PalGesture.Wave, "Heading off? See you soon!");
        await Task.Delay(500);
        Save(dir, "pals-power");
        GoHome();
        await Task.Delay(600);

        var settings = new SettingsView(_host, this);
        Navigate(settings);
        await Task.Delay(1000);
        settings.SelectCategory("Pals");
        await Task.Delay(500);
        _palPeek.SnapshotShow(new Core.Pals.PalReaction(Core.Pals.PalGesture.Dance, Core.Pals.PalMood.Cheeky, "Neon lights! I feel so cool right now."));
        await Task.Delay(600);
        Save(dir, "pals-peek-settings");
        _palPeek.Hide();
        GoHome();
        await Task.Delay(600);

        var bubble = new Pals.PalBarBubble(_host);
        bubble.ShowLine(new Core.Pals.PalReaction(Core.Pals.PalGesture.Wave, Core.Pals.PalMood.Happy, "You've been playing a while. Maybe stretch your legs?"), new Point(-4000, -4000));
        await Task.Delay(400);
        SaveVisual((FrameworkElement)bubble.Content, 640, 180, dir, "pals-bar-bubble");
        bubble.Close();
    }

    private async Task SaveWebSnapshotAsync(string dir, string name)
    {
        var web = new BrowserView(_host, this);
        Navigate(web);
        await Task.Delay(5000);
        try
        {
            await web.SaveSnapshotAsync(Path.Combine(dir, name + ".png"), RootGrid);
            Log.Info("Snapshot saved: " + name);
        }
        catch (Exception ex)
        {
            Log.Warn("Web snapshot failed", ex);
        }
        GoHome();
        await Task.Delay(500);
    }

    private void Save(string dir, string name)
    {
        var width = (int)Math.Round(RootGrid.ActualWidth);
        var height = (int)Math.Round(RootGrid.ActualHeight);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(RootGrid);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var fs = File.Create(Path.Combine(dir, name + ".png"));
        encoder.Save(fs);
        Log.Info("Snapshot saved: " + name);
    }
}
