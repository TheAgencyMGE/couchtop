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

namespace Couchtop.App.Views;

public partial class MainWindow : Window
{
    private const int EmergencyHotkeyId = 1;
    private const int HomeHotkeyId = 2;
    private static bool _classHandlersRegistered;

    private readonly AppHost _host;
    private readonly List<FrameworkElement> _stack = new();
    private readonly List<BackdropWindow> _backdrops = new();
    private readonly DispatcherTimer _displayDebounce;
    private readonly DispatcherTimer _toastTimer;
    private IntPtr _hwnd;
    private HwndSource? _source;
    private HomeMenuWindow? _home;
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

        Menu = new MenuView(host, this);
        ViewLayer.Children.Add(Menu);
        _stack.Add(Menu);

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

    public MenuView Menu { get; }
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
    }

    private void OnContentRendered(object? sender, EventArgs e)
    {
        UpdateScales();
        if (_host.Options.IsSnapshot)
        {
            _ = RunSnapshotsAsync();
            return;
        }

        _host.SignalReady();
        _host.RefreshChannelsInBackground();
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

    private async void AfterIntro()
    {
        UpdateActivity();
        Menu.Focus();
        if (_host.Settings.Current.WelcomeShown) return;
        _host.Settings.Current.WelcomeShown = true;
        _host.SaveSettings();
        await ShowDialogAsync(
            "Welcome to Couchtop!\n\nPoint at a channel and click to start it. Press " + _host.Settings.Current.HomeMenuHotkey +
            " at any time (or Guide / Home on a controller) for the Quick Menu.\n\nIf anything ever goes wrong, Ctrl + Alt + Shift + F12 takes you straight back to the normal Windows desktop.",
            "OK");
    }

    private void UpdateActivity()
    {
        var active = IsActive && WindowState != WindowState.Minimized && !_sessionLocked && !_splashActive && !_host.IsExiting;
        Menu.SetActive(active && CurrentView == Menu);
        _host.Audio.SetAmbience(active && CurrentView is IScreenView { PlaysAmbience: true });
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
        ThemeManager.Apply(theme);
        Menu.RebuildPages();
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
        _useSystemCursor = _host.Settings.Current.UseSystemCursor || (_stack.Count > 0 && CurrentView is IScreenView { WantsSystemCursor: true });
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
        else if (e.ChangedButton == MouseButton.Right && CurrentView is not BrowserView && !IsInsideTextBox(e.OriginalSource as DependencyObject))
        {
            GoBack();
            e.Handled = true;
        }
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
            Menu.Visibility = Visibility.Visible;
            AnimateIn(Menu, null, fromSmall: false);
            AfterViewChanged(Menu);
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
        Menu.BeginAnimation(OpacityProperty, null);
        Menu.Opacity = 1;
        Menu.RenderTransform = Transform.Identity;
        Menu.IsHitTestVisible = true;
        Menu.Visibility = Visibility.Visible;
        AfterViewChanged(Menu);
    }

    private void AfterViewChanged(FrameworkElement view)
    {
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

            _ = ShowDialogAsync("Remove \"Example\" from your channels?\nThe app itself stays installed.", "Remove", "Cancel");
            await Task.Delay(900);
            Save(dir, "10-dialog");
            CloseDialog(null);
            await Task.Delay(500);

            await HomeMenuWindow.RenderSnapshotAsync(_host, this, Path.Combine(dir, "11-home-menu.png"));

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

                await HomeMenuWindow.RenderSnapshotAsync(_host, this, Path.Combine(dir, $"theme-{theme.Id}-5-quick.png"));
            }
        }
        catch (Exception ex)
        {
            Log.Error("Snapshot rendering failed", ex);
        }
        _host.Exit(0);
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
