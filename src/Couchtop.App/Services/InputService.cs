using System.Diagnostics;
using System.Runtime.InteropServices;
using Couchtop.Core.Diagnostics;
using Couchtop.Core.Input;
using Couchtop.Core.Native;
using Couchtop.Core.Platform;
using Couchtop.Core.Settings;

namespace Couchtop.App.Services;

/// <summary>
/// Turns Xbox-style controllers and real Wii Remotes into pointer movement, clicks and key presses.
/// Only the HOME combination works while another app is in front; everything else is ignored so
/// Couchtop never interferes with games or apps that read the controller themselves.
/// </summary>
public sealed class InputService : IDisposable
{
    private const ushort VK_RETURN = 0x0D, VK_ESCAPE = 0x1B, VK_PRIOR = 0x21, VK_NEXT = 0x22, VK_LEFT = 0x25, VK_UP = 0x26, VK_RIGHT = 0x27, VK_DOWN = 0x28;

    private readonly UserSettings _settings;
    private readonly Func<IntPtr> _mainWindow;
    private readonly GamepadButtons[] _previous = new GamepadButtons[4];
    private readonly bool[] _connected = new bool[4];
    private readonly Dictionary<(int, GamepadButtons), DateTime> _repeat = new();
    private Thread? _thread;
    private volatile bool _running;
    private WiimoteManager? _wiimote;
    private WiimoteButtons _wiimotePrevious;
    private (double X, double Y)? _wiimoteSmoothed;
    private DateTime _nextProbe;
    private DateTime _lastWheel;
    private double _fracX, _fracY;
    private MonitorDescriptor? _monitor;
    private DateTime _monitorCheckedAt;

    public InputService(UserSettings settings, Func<IntPtr> mainWindow)
    {
        _settings = settings;
        _mainWindow = mainWindow;
    }

    public event Action? HomeRequested;

    public int ConnectedControllers => _connected.Count(c => c);
    public bool WiimoteConnected => _wiimote?.IsConnected == true;

    public void Start()
    {
        if (_settings.ControllerEnabled && _thread is null)
        {
            _running = true;
            _thread = new Thread(PollLoop) { IsBackground = true, Name = "Controller input", Priority = ThreadPriority.AboveNormal };
            _thread.Start();
        }
        if (_settings.WiimoteEnabled && _wiimote is null)
        {
            _wiimote = new WiimoteManager();
            _wiimote.InputReceived += OnWiimoteInput;
            _wiimote.Start();
        }
    }

    public void Stop()
    {
        _running = false;
        _thread?.Join(500);
        _thread = null;
        if (_wiimote is not null)
        {
            _wiimote.InputReceived -= OnWiimoteInput;
            _wiimote.Dispose();
            _wiimote = null;
        }
    }

    public void Restart()
    {
        Stop();
        Start();
    }

    public static int? BatteryLevel(int index) => XInput.BatteryLevel(index);

    private static bool IsOurWindowInFront()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero) return false;
        NativeMethods.GetWindowThreadProcessId(foreground, out var pid);
        return pid == Environment.ProcessId;
    }

    private void PollLoop()
    {
        var clock = Stopwatch.StartNew();
        var last = 0.0;
        while (_running)
        {
            var now = clock.Elapsed.TotalSeconds;
            var dt = Math.Clamp(now - last, 0, 0.1);
            last = now;
            var probe = DateTime.UtcNow >= _nextProbe;
            var any = false;
            try
            {
                for (var i = 0; i < 4; i++)
                {
                    if (!_connected[i] && !probe) continue;
                    var state = XInput.GetState(i);
                    _connected[i] = state.Connected;
                    if (!state.Connected)
                    {
                        _previous[i] = GamepadButtons.None;
                        continue;
                    }
                    any = true;
                    HandleGamepad(i, state, dt);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Controller polling error", ex);
            }
            if (probe) _nextProbe = DateTime.UtcNow.AddSeconds(2);
            Thread.Sleep(any ? (IsOurWindowInFront() ? 8 : 30) : 200);
        }
    }

    private void HandleGamepad(int index, GamepadState state, double dt)
    {
        var buttons = state.Buttons;
        var pressed = buttons & ~_previous[index];
        var released = _previous[index] & ~buttons;
        _previous[index] = buttons;

        const GamepadButtons combo = GamepadButtons.Back | GamepadButtons.Start;
        if ((pressed & GamepadButtons.Guide) != 0 || ((buttons & combo) == combo && (pressed & combo) != 0))
        {
            HomeRequested?.Invoke();
            return;
        }
        if (!IsOurWindowInFront()) return;

        if (state.LeftX != 0 || state.LeftY != 0) MoveCursor(StickMath.Curve(state.LeftX), -StickMath.Curve(state.LeftY), dt);
        if ((pressed & GamepadButtons.A) != 0) Mouse(NativeMethods.MOUSEEVENTF_LEFTDOWN);
        if ((released & GamepadButtons.A) != 0) Mouse(NativeMethods.MOUSEEVENTF_LEFTUP);
        if ((pressed & GamepadButtons.B) != 0) Key(VK_ESCAPE);
        if ((pressed & GamepadButtons.X) != 0) Key(VK_RETURN);
        if ((pressed & GamepadButtons.Start) != 0 && (buttons & GamepadButtons.Back) == 0) Key(VK_RETURN);
        if ((pressed & GamepadButtons.LeftShoulder) != 0) Key(VK_PRIOR);
        if ((pressed & GamepadButtons.RightShoulder) != 0) Key(VK_NEXT);
        Repeat(index, buttons, pressed, GamepadButtons.DPadUp, VK_UP);
        Repeat(index, buttons, pressed, GamepadButtons.DPadDown, VK_DOWN);
        Repeat(index, buttons, pressed, GamepadButtons.DPadLeft, VK_LEFT);
        Repeat(index, buttons, pressed, GamepadButtons.DPadRight, VK_RIGHT);

        if (Math.Abs(state.RightY) > 0.35 && DateTime.UtcNow - _lastWheel > TimeSpan.FromMilliseconds(110))
        {
            _lastWheel = DateTime.UtcNow;
            Wheel(state.RightY > 0 ? 120 : -120);
        }
    }

    private void Repeat(int index, GamepadButtons buttons, GamepadButtons pressed, GamepadButtons button, ushort vk)
    {
        var key = (index, button);
        if ((buttons & button) == 0)
        {
            _repeat.Remove(key);
            return;
        }
        var now = DateTime.UtcNow;
        if ((pressed & button) != 0)
        {
            Key(vk);
            _repeat[key] = now.AddMilliseconds(420);
        }
        else if (_repeat.TryGetValue(key, out var next) && now >= next)
        {
            Key(vk);
            _repeat[key] = now.AddMilliseconds(110);
        }
    }

    private MonitorDescriptor? CurrentMonitor()
    {
        if (_monitor is null || DateTime.UtcNow - _monitorCheckedAt > TimeSpan.FromSeconds(2))
        {
            _monitor = Monitors.ForWindow(_mainWindow());
            _monitorCheckedAt = DateTime.UtcNow;
        }
        return _monitor;
    }

    private void MoveCursor(double dx, double dy, double dt)
    {
        var scale = CurrentMonitor()?.Scale ?? 1;
        var speed = 1500 * _settings.PointerSpeed * scale;
        _fracX += dx * speed * dt;
        _fracY += dy * speed * dt;
        var moveX = (int)_fracX;
        var moveY = (int)_fracY;
        _fracX -= moveX;
        _fracY -= moveY;
        if (moveX == 0 && moveY == 0) return;
        if (NativeMethods.GetCursorPos(out var p)) NativeMethods.SetCursorPos(p.X + moveX, p.Y + moveY);
    }

    private void OnWiimoteInput(WiimoteInput input)
    {
        var pressed = input.Buttons & ~_wiimotePrevious;
        var released = _wiimotePrevious & ~input.Buttons;
        _wiimotePrevious = input.Buttons;

        if ((pressed & WiimoteButtons.Home) != 0)
        {
            HomeRequested?.Invoke();
            return;
        }
        if (!IsOurWindowInFront()) return;

        var point = WiimotePointerMapper.Map(input.Dots);
        var monitor = CurrentMonitor();
        if (point is { } p && monitor is not null)
        {
            _wiimoteSmoothed = _wiimoteSmoothed is { } s ? (s.X + (p.X - s.X) * 0.4, s.Y + (p.Y - s.Y) * 0.4) : p;
            NativeMethods.SetCursorPos(monitor.X + (int)(_wiimoteSmoothed.Value.X * monitor.Width), monitor.Y + (int)(_wiimoteSmoothed.Value.Y * monitor.Height));
        }
        else
        {
            _wiimoteSmoothed = null;
            double dx = 0, dy = 0;
            if ((input.Buttons & WiimoteButtons.Left) != 0) dx -= 1;
            if ((input.Buttons & WiimoteButtons.Right) != 0) dx += 1;
            if ((input.Buttons & WiimoteButtons.Up) != 0) dy -= 1;
            if ((input.Buttons & WiimoteButtons.Down) != 0) dy += 1;
            if (dx != 0 || dy != 0) MoveCursor(dx * 0.7, dy * 0.7, 0.01);
        }

        if ((pressed & WiimoteButtons.A) != 0) Mouse(NativeMethods.MOUSEEVENTF_LEFTDOWN);
        if ((released & WiimoteButtons.A) != 0) Mouse(NativeMethods.MOUSEEVENTF_LEFTUP);
        if ((pressed & (WiimoteButtons.B | WiimoteButtons.Two)) != 0) Key(VK_ESCAPE);
        if ((pressed & WiimoteButtons.One) != 0) Key(VK_RETURN);
        if ((pressed & WiimoteButtons.Plus) != 0) Key(VK_NEXT);
        if ((pressed & WiimoteButtons.Minus) != 0) Key(VK_PRIOR);
    }

    private static void Key(ushort vk)
    {
        var inputs = new[]
        {
            new NativeMethods.INPUT { type = NativeMethods.INPUT_KEYBOARD, U = new NativeMethods.InputUnion { ki = new NativeMethods.KEYBDINPUT { wVk = vk } } },
            new NativeMethods.INPUT { type = NativeMethods.INPUT_KEYBOARD, U = new NativeMethods.InputUnion { ki = new NativeMethods.KEYBDINPUT { wVk = vk, dwFlags = NativeMethods.KEYEVENTF_KEYUP } } },
        };
        NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    private static void Mouse(uint flags)
    {
        var inputs = new[] { new NativeMethods.INPUT { type = NativeMethods.INPUT_MOUSE, U = new NativeMethods.InputUnion { mi = new NativeMethods.MOUSEINPUT { dwFlags = flags } } } };
        NativeMethods.SendInput(1, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    private static void Wheel(int delta)
    {
        var inputs = new[] { new NativeMethods.INPUT { type = NativeMethods.INPUT_MOUSE, U = new NativeMethods.InputUnion { mi = new NativeMethods.MOUSEINPUT { dwFlags = NativeMethods.MOUSEEVENTF_WHEEL, mouseData = delta } } } };
        NativeMethods.SendInput(1, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    public void Dispose() => Stop();
}
