using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Couchtop.App.Controls;
using Couchtop.App.Views;
using Couchtop.Core.Diagnostics;
using Couchtop.Core.Native;
using Couchtop.Core.Pals;
using Couchtop.Core.Platform;

namespace Couchtop.App.Pals;

/// <summary>
/// The Pal out on the real Windows desktop. It lives in a small see-through, always-on-top window that follows
/// it around: it walks along the taskbar and the tops of app windows, hops between them, rides along when you
/// drag a window, falls when the window under it goes away, and can be poked, picked up and dropped. Only the
/// Pal itself takes clicks; everything around it clicks straight through to your apps. It steps aside while
/// Couchtop is in front (the home screen has its own Pal) and for full-screen games and videos.
/// </summary>
public sealed class PalDesktopBuddy
{
    private const double WindowWidth = 560, WindowHeight = 600;
    private const double FeetInset = 38;
    private const double Gravity = 3200;

    private enum Mode { Standing, Falling, Jumping, Held }

    private readonly AppHost _host;
    private readonly MainWindow _main;
    private readonly DispatcherTimer _poll;
    private readonly Random _random = new();
    private Window? _window;
    private PalActor? _actor;
    private IntPtr _hwnd;
    private bool _showing;
    private bool _listening;
    private List<Surface> _surfaces = new();
    private List<ScreenRect> _workAreas = new();
    private Surface? _surface;
    private ScreenRect? _ownerBounds;
    private Mode _mode = Mode.Falling;
    private double _x, _feetY, _vy, _targetX;
    private double _nextDecision;
    private double _clock;
    private (double X0, double Y0, double X1, double Y1, double Start, double Length, Surface Target)? _jump;
    private Point? _pressAt;
    private double _lastRideReport = -100;
    private DateTime _hiddenAt = DateTime.MinValue;

    public PalDesktopBuddy(AppHost host, MainWindow main)
    {
        _host = host;
        _main = main;
        _poll = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(400) };
        _poll.Tick += (_, _) => Poll();
        main.Activated += (_, _) => Update();
        main.Deactivated += (_, _) => main.Dispatcher.BeginInvoke(Update, DispatcherPriority.Background);
        main.StateChanged += (_, _) => Update();
        host.Pals.PreferencesChanged += Update;
        host.Pals.ProfileChanged += Update;
        host.Pals.Reacted += OnReacted;
    }

    public bool Showing => _showing;

    /// <summary>Shows or hides the desktop Pal to match the settings and what's in front.</summary>
    public void Update()
    {
        var prefs = _host.Pals.Preferences;
        var couchtopInFront = _main.IsActive && _main.WindowState != WindowState.Minimized;
        var want = prefs.DesktopVisits && _host.Pals.HasPal && !couchtopInFront && !_host.Desktop.ForegroundIsFullScreen && !_host.IsExiting;
        SetListening(prefs.DesktopVisits && _host.Pals.HasPal && !_host.IsExiting);
        if (want == _showing) return;
        if (want) Show();
        else Hide();
    }

    private void SetListening(bool on)
    {
        if (on == _listening) return;
        _listening = on;
        // While visits are on, keep the window list fresh so full-screen apps are noticed even while hidden.
        if (on)
        {
            _host.Desktop.AddListener();
            _host.Desktop.Changed += Update;
            _poll.Start();
        }
        else
        {
            _host.Desktop.RemoveListener();
            _host.Desktop.Changed -= Update;
            _poll.Stop();
        }
    }

    // ---------------------------------------------------------------- window

    private void EnsureWindow()
    {
        if (_window is not null) return;
        _actor = new PalActor(_host, 240, 270)
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Pokable = false,
        };
        _actor.View.SetFraming(0.5, 1.28, instant: true);
        _actor.View.HitTestBody = true;
        _actor.View.Tick += OnTick;
        _actor.View.MouseLeftButtonDown += OnPress;
        _actor.View.MouseMove += OnDrag;
        _actor.View.MouseLeftButtonUp += OnRelease;
        _actor.View.MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            ShowMenu();
        };

        _window = new Window
        {
            Title = "Couchtop Pal",
            Width = WindowWidth,
            Height = WindowHeight,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            Content = new Grid { Children = { _actor } },
        };
        _window.SetResourceReference(Window.FontFamilyProperty, "AppFont");
        _window.SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(_window).Handle;
            var style = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
            // Never takes focus from the app you're using, and stays out of Alt+Tab.
            NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(style | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE));
        };
    }

    private void Show()
    {
        try
        {
            EnsureWindow();
            _showing = true;
            _host.Pals.DesktopShowing = true;
            Poll();
            // Back from a long break (or the first visit): drop in from the top of the screen. After a quick
            // switch to Couchtop and back, carry on where it was.
            var fresh = DateTime.Now - _hiddenAt > TimeSpan.FromMinutes(2) || _surface is null;
            if (fresh)
            {
                var area = _workAreas.FirstOrDefault(a => a.Contains(_x, _feetY));
                if (area == default) area = _workAreas.FirstOrDefault();
                _x = area == default ? 600 : area.Left + area.Width * (0.35 + _random.NextDouble() * 0.3);
                _feetY = (area == default ? 0 : area.Top) - 40;
                _vy = 0;
                _mode = Mode.Falling;
                _surface = null;
                if (_actor?.Animator is { } animator) animator.Base = AvatarBase.Held;
            }
            _window!.Show();
            Place();
            Log.Info("Desktop Pal out on the desktop");
            if (fresh) _pendingArrival = true;
        }
        catch (Exception ex)
        {
            Log.Warn("Could not show the desktop Pal", ex);
            _showing = false;
            _host.Pals.DesktopShowing = false;
        }
    }

    private bool _pendingArrival;

    private void Hide()
    {
        _showing = false;
        _host.Pals.DesktopShowing = false;
        _hiddenAt = DateTime.Now;
        if (_mode == Mode.Held) Release();
        _actor?.HideBubble();
        _window?.Hide();
    }

    private void Place()
    {
        if (_window is null || _hwnd == IntPtr.Zero) return;
        var scale = Scale();
        var left = (int)Math.Round(_x - WindowWidth / 2 * scale);
        var top = (int)Math.Round(_feetY - (WindowHeight - FeetInset) * scale);
        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, left, top, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    private double Scale() => _window is null ? 1 : VisualTreeHelper.GetDpi(_window).DpiScaleX;

    // ---------------------------------------------------------------- the world

    /// <summary>Re-reads the windows and screens the Pal can walk on.</summary>
    private void Poll()
    {
        if (!_showing) return;
        try
        {
            _workAreas = Monitors.Enumerate().Select(m => new ScreenRect(m.WorkLeft, m.WorkTop, m.WorkLeft + m.UsableWidth, m.WorkTop + m.UsableHeight)).ToList();
            var windows = new List<(IntPtr, ScreenRect)>();
            if (_host.Pals.Preferences.ClimbWindows)
            {
                foreach (var w in WindowEnumerator.GetSwitchableWindows(Environment.ProcessId))
                {
                    if (NativeMethods.IsIconic(w.Handle)) continue;
                    if (Bounds(w.Handle) is { } b) windows.Add((w.Handle, b));
                }
            }
            _surfaces = DesktopSurfaces.Build(windows, _workAreas, headroom: 170 * Scale());
            if (_mode == Mode.Standing && _surface is { } current)
            {
                // Still there? (The same edge might have been cut shorter by a window moving in front.)
                var match = _surfaces.FirstOrDefault(s => s.Owner == current.Owner && Math.Abs(s.Y - current.Y) < 4 && s.Spans(_x));
                if (match is null)
                {
                    if (!current.IsFloor && !Exists(current.Owner)) _host.Pals.Report(new PalEvent(PalEventKind.WindowVanished));
                    StartFalling();
                }
                else
                {
                    _surface = match;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Desktop Pal could not read the windows", ex);
        }
    }

    private static ScreenRect? Bounds(IntPtr hwnd)
    {
        // The visible frame, without the invisible resize border Windows adds around most windows.
        if (NativeMethods.DwmGetWindowRect(hwnd, NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS, out var r, System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.RECT>()) != 0
            && !NativeMethods.GetWindowRect(hwnd, out r)) return null;
        return new ScreenRect(r.Left, r.Top, r.Right, r.Bottom);
    }

    private static bool Exists(IntPtr hwnd) => NativeMethods.IsWindowVisible(hwnd) && !NativeMethods.IsIconic(hwnd);

    // ---------------------------------------------------------------- per frame

    private void OnTick(double dt)
    {
        if (!_showing || _actor?.Animator is not { } animator) return;
        _clock += dt;
        switch (_mode)
        {
            case Mode.Held:
            {
                if (!NativeMethods.GetCursorPos(out var cursor)) break;
                var previous = _x;
                _x = cursor.X;
                // Held by the top of the head.
                _feetY = cursor.Y + 205 * Scale();
                animator.HeldSway = Math.Clamp(-(_x - previous) / Math.Max(dt, 0.001) * 0.02, -30, 30);
                break;
            }
            case Mode.Falling:
            {
                var before = _feetY;
                _vy = Math.Min(_vy + Gravity * dt, 2600);
                _feetY += _vy * dt;
                var landing = DesktopSurfaces.Below(_surfaces, _x, before);
                if (landing is not null && _feetY >= landing.Y) Land(landing);
                else if (landing is null && _workAreas.Count > 0 && _feetY > _workAreas.Max(a => a.Bottom))
                    Land(DesktopSurfaces.Below(_surfaces, _x, 0) ?? _surfaces.First(s => s.IsFloor));
                break;
            }
            case Mode.Jumping when _jump is { } j:
            {
                var t = Math.Clamp((_clock - j.Start) / j.Length, 0, 1);
                _x = j.X0 + (j.X1 - j.X0) * t;
                var arc = Math.Max(90, j.Y0 - j.Y1 + 90) * Scale();
                _feetY = j.Y0 + (j.Y1 - j.Y0) * t - 4 * arc * t * (1 - t);
                if (t >= 1)
                {
                    _jump = null;
                    Land(j.Target);
                    if (!j.Target.IsFloor && j.Target.Y < j.Y0 - 40) _host.Pals.Report(new PalEvent(PalEventKind.Climbed));
                }
                break;
            }
            case Mode.Standing:
                Stand(dt, animator);
                break;
        }

        var busy = _mode != Mode.Standing || animator.Base == AvatarBase.Walk || animator.IsGesturing || animator.IsTalking;
        _actor.View.MaxFps = Anim.LowPowerGraphics ? (busy ? 30 : 15) : (busy ? 60 : 30);
        Place();
    }

    private void Stand(double dt, AvatarAnimator animator)
    {
        if (_surface is not { } surface) { StartFalling(); return; }

        // Ride along with the window underneath when it moves.
        if (!surface.IsFloor)
        {
            if (Bounds(surface.Owner) is not { } now || !Exists(surface.Owner))
            {
                _host.Pals.Report(new PalEvent(PalEventKind.WindowVanished));
                StartFalling();
                return;
            }
            if (_ownerBounds is { } before && (Math.Abs(now.Left - before.Left) > 0.5 || Math.Abs(now.Top - before.Top) > 0.5))
            {
                var dx = now.Left - before.Left;
                var dy = now.Top - before.Top;
                _x += dx;
                _targetX += dx;
                _feetY = now.Top;
                _surface = surface = new Surface(now.Top, surface.Left + dx, surface.Right + dx, surface.Owner);
                if (Math.Abs(dx) + Math.Abs(dy) > 6 && _clock - _lastRideReport > 3)
                {
                    _lastRideReport = _clock;
                    _host.Pals.Report(new PalEvent(PalEventKind.RidingWindow));
                }
            }
            _ownerBounds = now;
        }
        else
        {
            _feetY = surface.Y;
        }

        var margin = 50 * Scale();
        var min = surface.Left + margin;
        var max = surface.Right - margin;
        if (_x < surface.Left - 4 || _x > surface.Right + 4) { StartFalling(); return; }

        var calm = animator.IsTalking || animator.IsGesturing || animator.Base is AvatarBase.Sit or AvatarBase.Sleep;
        if (!calm && _clock > _nextDecision && Math.Abs(_targetX - _x) < 1) Decide(surface, min, max, animator);

        var distance = _targetX - _x;
        if (Math.Abs(distance) > 2 && !calm)
        {
            var speed = (animator.Personality == "sporty" ? 170 : animator.Personality == "chill" ? 95 : 130) * Scale();
            _x += Math.Sign(distance) * Math.Min(Math.Abs(distance), speed * dt);
            animator.WalkSpeed = speed / (130 * Scale());
            animator.Base = AvatarBase.Walk;
            _actor!.View.Facing = Math.Sign(distance) * 72;
            // Walking off the end of a window means dropping to whatever is below.
            if (_x < surface.Left || _x > surface.Right) StartFalling();
        }
        else if (animator.Base == AvatarBase.Walk)
        {
            _targetX = _x;
            animator.Base = AvatarBase.Idle;
            _actor!.View.Facing = 0;
        }
    }

    /// <summary>What to do next: stroll, rest, hop to another window, or wander off an edge.</summary>
    private void Decide(Surface surface, double min, double max, AvatarAnimator animator)
    {
        _nextDecision = _clock + 5 + _random.NextDouble() * 9 * (animator.Personality == "chill" ? 1.8 : animator.Personality == "sporty" ? 0.6 : 1);
        var roll = _random.NextDouble();
        if (_host.Pals.Preferences.ClimbWindows && roll < 0.28)
        {
            var options = DesktopSurfaces.Reachable(_surfaces, surface, _x, 560 * Scale(), 380 * Scale()).ToList();
            if (options.Count > 0)
            {
                var target = options[_random.Next(options.Count)];
                var landX = Math.Clamp(_x, target.Left + 60 * Scale(), target.Right - 60 * Scale());
                if (Math.Abs(landX - _x) < 20) landX = Math.Clamp(landX + (_random.NextDouble() - 0.5) * 200 * Scale(), target.Left + 60 * Scale(), target.Right - 60 * Scale());
                Jump(target, landX);
                return;
            }
        }
        if (roll < 0.36 && !surface.IsFloor)
        {
            // Wander off the nearest end and drop down.
            _targetX = _x - surface.Left < surface.Right - _x ? surface.Left - 30 * Scale() : surface.Right + 30 * Scale();
            return;
        }
        if (roll > 0.9 && animator.Personality is "chill" or "cheerful" or "curious")
        {
            animator.Base = AvatarBase.Sit;
            _nextDecision = _clock + 20 + _random.NextDouble() * 20;
            return;
        }
        if (max > min) _targetX = min + _random.NextDouble() * (max - min);
    }

    private void Jump(Surface target, double landX)
    {
        if (_actor?.Animator is not { } animator) return;
        animator.Base = AvatarBase.Idle;
        animator.Play(PalGesture.Jump, PalMood.Excited);
        _actor.View.Facing = Math.Sign(landX - _x) * 50;
        var length = 0.65 + Math.Min(0.4, Math.Abs(landX - _x) / (1400 * Scale()));
        _jump = (_x, _feetY, landX, target.Y, _clock + 0.18, length, target);
        _mode = Mode.Jumping;
        _surface = null;
    }

    private void StartFalling()
    {
        _mode = Mode.Falling;
        _vy = 0;
        _surface = null;
        _ownerBounds = null;
        if (_actor?.Animator is { } animator)
        {
            animator.Base = AvatarBase.Held;
            animator.Play(PalGesture.Surprised, PalMood.Surprised);
        }
    }

    private void Land(Surface surface)
    {
        _mode = Mode.Standing;
        _surface = surface;
        _feetY = surface.Y;
        _vy = 0;
        _targetX = _x = Math.Clamp(_x, surface.Left + 4, surface.Right - 4);
        _ownerBounds = surface.IsFloor ? null : Bounds(surface.Owner);
        _nextDecision = _clock + 2.5;
        if (_actor?.Animator is { } animator)
        {
            animator.Base = AvatarBase.Idle;
            _actor.View.Facing = 0;
        }
        Log.Info($"Desktop Pal landed at ({_x:0},{_feetY:0}) on {(surface.IsFloor ? "the floor" : "a window")} spanning {surface.Left:0}-{surface.Right:0}");
        DebugSnapshot();
        if (_pendingArrival)
        {
            _pendingArrival = false;
            _dropReport = false;
            _host.Pals.Report(new PalEvent(PalEventKind.DesktopArrived));
        }
        else if (_dropReport)
        {
            _dropReport = false;
            _host.Pals.Report(new PalEvent(PalEventKind.Dropped));
        }
    }

    /// <summary>Developer check (COUCHTOP_PAL_DEBUG=folder): saves what the desktop Pal's window is drawing.</summary>
    private void DebugSnapshot()
    {
        var folder = Environment.GetEnvironmentVariable("COUCHTOP_PAL_DEBUG");
        if (string.IsNullOrEmpty(folder) || _window?.Content is not FrameworkElement content) return;
        try
        {
            System.IO.Directory.CreateDirectory(folder);
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(content);
            PalPortrait.Save(bitmap, System.IO.Path.Combine(folder, $"desktop-pal-{DateTime.Now:HHmmss}.png"));
        }
        catch (Exception ex)
        {
            Log.Warn("Desktop Pal debug snapshot failed", ex);
        }
    }

    // ---------------------------------------------------------------- hands on

    private void OnPress(object sender, MouseButtonEventArgs e)
    {
        _pressAt = e.GetPosition(_window);
        _actor!.View.CaptureMouse();
        e.Handled = true;
    }

    private void OnDrag(object sender, MouseEventArgs e)
    {
        if (_pressAt is not { } press || _mode == Mode.Held) return;
        if ((e.GetPosition(_window) - press).Length < 8) return;
        _mode = Mode.Held;
        _surface = null;
        _jump = null;
        _actor!.HideBubble();
        if (_actor.Animator is { } animator) animator.Base = AvatarBase.Held;
        _actor.View.Facing = 0;
        _host.Pals.Report(new PalEvent(PalEventKind.PickedUp));
    }

    private void OnRelease(object sender, MouseButtonEventArgs e)
    {
        _actor!.View.ReleaseMouseCapture();
        e.Handled = true;
        var wasHeld = _mode == Mode.Held;
        _pressAt = null;
        if (wasHeld)
        {
            Release();
            return;
        }
        if (_actor.Animator?.Base is AvatarBase.Sit or AvatarBase.Sleep) _actor.Animator.Base = AvatarBase.Idle;
        _host.Pals.Report(new PalEvent(PalEventKind.Poked));
    }

    private void Release()
    {
        _pressAt = null;
        _mode = Mode.Falling;
        _vy = 0;
        _dropReport = true;
    }

    private bool _dropReport;

    private void ShowMenu()
    {
        var menu = new ContextMenu();
        void Item(string text, Action action)
        {
            var item = new MenuItem { Header = text };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }
        Item("Open Couchtop", () => _main.BringToFront());
        Item("Edit in Pal Studio", () =>
        {
            _main.BringToFront();
            _main.OpenBuiltIn(Core.Channels.BuiltInChannels.Pals);
        });
        Item(_host.Pals.Preferences.ClimbWindows ? "Stay on the taskbar" : "Climb on windows", () =>
        {
            _host.Pals.Preferences.ClimbWindows = !_host.Pals.Preferences.ClimbWindows;
            _host.Pals.SavePreferences();
            if (_surface is { IsFloor: false }) StartFalling();
        });
        menu.Items.Add(new Separator());
        Item("Go back to Couchtop", () =>
        {
            _host.Pals.Preferences.DesktopVisits = false;
            _host.Pals.SavePreferences();
        });
        menu.PlacementTarget = _actor;
        menu.IsOpen = true;
    }

    // ---------------------------------------------------------------- reactions

    private void OnReacted(PalReaction reaction, PalStage stage)
    {
        if (stage != PalStage.Desktop || !_showing || _actor is null) return;
        if (_mode == Mode.Held && reaction.Gesture is not (PalGesture.None or PalGesture.Surprised)) reaction = reaction with { Gesture = PalGesture.None };
        if (_mode != Mode.Standing && reaction.Gesture is not (PalGesture.None or PalGesture.Surprised)) reaction = reaction with { Gesture = PalGesture.None };
        if (_actor.Animator?.Base is AvatarBase.Sit or AvatarBase.Sleep) _actor.Animator.Base = AvatarBase.Idle;
        if (reaction.Text is not null && _mode == Mode.Standing)
        {
            _targetX = _x;
            _actor.View.Facing = 0;
        }
        // Keep the bubble on screen near the edges of the monitor.
        var area = _workAreas.FirstOrDefault(a => a.Left <= _x && _x <= a.Right);
        var scale = Scale();
        _actor.BubbleShift = area == default ? 0
            : _x - area.Left < 280 * scale ? (280 * scale - (_x - area.Left)) / scale * 0.5
            : area.Right - _x < 280 * scale ? -(280 * scale - (area.Right - _x)) / scale * 0.5
            : 0;
        _actor.React(reaction);
    }
}
