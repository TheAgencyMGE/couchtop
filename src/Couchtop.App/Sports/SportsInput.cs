using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using Couchtop.Core.Input;
using Couchtop.Core.Sports;

namespace Couchtop.App.Sports;

/// <summary>Edge-triggered navigation for pause and result panels, merged from every device.</summary>
public struct MenuInput
{
    public bool Up, Down, Left, Right, Confirm, Back, Pause;
}

/// <summary>
/// Reads the keyboard, mouse, Xbox-style controllers and Wii Remotes directly while a game is running and turns
/// them into one <see cref="PlayerCommand"/> per player. With one player (or taking turns) every device
/// controls the game; for two players at once, devices are split sensibly.
/// </summary>
public sealed class SportsInputHub : IDisposable
{
    private enum Source { Keyboard, KeyboardLeft, KeyboardRight, Mouse, Wiimote, Pad0, Pad1, Pad2, Pad3 }

    private struct Raw
    {
        public double MoveX, MoveY;
        public bool Action, Alt, LeftPunch, RightPunch, Pause, Confirm, Back, Up, Down, Left, Right;
        public float Swing;
        public int SwingSide;
    }

    private struct Latch
    {
        public bool Action, Alt, LeftPunch, RightPunch, Pause, Confirm, Back, Up, Down, Left, Right;
    }

    private readonly AppHost _host;
    private readonly SwingDetector _swingDetector = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly object _gate = new();
    private readonly Dictionary<string, Latch> _latches = new();
    private readonly bool[] _padConnected = new bool[4];
    private double _nextProbe;
    private WiimoteButtons _wiimoteButtons;
    private float _pendingSwing;
    private int _pendingSide;

    public SportsInputHub(AppHost host)
    {
        _host = host;
        if (host.Input is { } input) input.WiimoteRaw += OnWiimote;
    }

    public bool WiimoteConnected => _host.Input?.WiimoteConnected == true;

    public int ConnectedPads
    {
        get
        {
            RefreshPads(force: true);
            return _padConnected.Count(c => c);
        }
    }

    public void SetCapture(bool on)
    {
        if (_host.Input is { } input) input.GameCapture = on;
        lock (_gate) _pendingSwing = 0;
    }

    private void OnWiimote(WiimoteInput input)
    {
        lock (_gate)
        {
            _wiimoteButtons = input.Buttons;
            if (input.Accel is not { } a) return;
            if (_swingDetector.Feed(a.X, a.Y, a.Z, _clock.Elapsed.TotalSeconds) is { } swing && swing.Strength > _pendingSwing)
            {
                _pendingSwing = swing.Strength;
                _pendingSide = swing.Side;
            }
        }
    }

    private void RefreshPads(bool force = false)
    {
        var now = _clock.Elapsed.TotalSeconds;
        if (!force && now < _nextProbe) return;
        _nextProbe = now + 1;
        for (var i = 0; i < 4; i++) _padConnected[i] = XInput.GetState(i).Connected;
    }

    private List<int> Pads()
    {
        RefreshPads();
        return Enumerable.Range(0, 4).Where(i => _padConnected[i]).ToList();
    }

    private Source[][] Assign(int players)
    {
        var pads = Pads().Select(i => Source.Pad0 + i).ToList();
        if (players <= 1)
            return new[] { new[] { Source.Keyboard, Source.Mouse, Source.Wiimote }.Concat(pads).ToArray() };
        if (pads.Count >= 2)
            return new[] { new[] { Source.Keyboard, Source.Mouse, Source.Wiimote, pads[0] }, new[] { pads[1] } };
        if (pads.Count == 1)
            return new[] { new[] { Source.Keyboard, Source.Mouse, Source.Wiimote }, new[] { pads[0] } };
        if (WiimoteConnected)
            return new[] { new[] { Source.Wiimote }, new[] { Source.Keyboard, Source.Mouse } };
        return new[] { new[] { Source.KeyboardLeft, Source.Mouse }, new[] { Source.KeyboardRight } };
    }

    /// <summary>Human-readable device assignment, shown before a game starts.</summary>
    public string Describe(int humans, bool turnBased)
    {
        if (humans <= 1 || turnBased) return "Keyboard, mouse, controllers and Wii Remotes all work. Take turns passing the controls.";
        var groups = Assign(humans);
        return string.Join("     ", groups.Select((g, i) => $"P{i + 1}: {string.Join(" + ", g.Select(Name).Distinct())}"));

        static string Name(Source s) => s switch
        {
            Source.Keyboard => "keyboard",
            Source.KeyboardLeft => "keyboard (WASD, Space, Q/E)",
            Source.KeyboardRight => "keyboard (arrows, Enter, K/L)",
            Source.Mouse => "mouse",
            Source.Wiimote => "Wii Remote",
            _ => $"controller {s - Source.Pad0 + 1}",
        };
    }

    /// <summary>Reads all devices once. Call every frame on the UI thread.</summary>
    public PlayerCommand[] Poll(int players, int humans, bool turnBased, int activePlayer, out MenuInput menu)
    {
        var commands = new PlayerCommand[Math.Max(1, players)];
        float swing;
        int side;
        WiimoteButtons wiimote;
        lock (_gate)
        {
            swing = _pendingSwing;
            side = _pendingSide;
            _pendingSwing = 0;
            wiimote = _wiimoteButtons;
        }

        var windowActive = Application.Current?.MainWindow?.IsActive == true;
        var groups = turnBased || humans <= 1 ? Assign(1) : Assign(Math.Min(2, humans));
        var everything = default(Raw);
        for (var g = 0; g < groups.Length; g++)
        {
            var raw = default(Raw);
            foreach (var source in groups[g]) Merge(ref raw, Read(source, windowActive, wiimote, swing, side));
            Merge(ref everything, raw);
            var slot = turnBased ? Math.Clamp(activePlayer, 0, commands.Length - 1) : g;
            if (slot < commands.Length && slot < Math.Max(1, humans)) commands[slot] = ToCommand("player" + g + (turnBased ? "t" : ""), raw);
        }
        menu = ToMenu(everything);
        return commands;
    }

    private static void Merge(ref Raw into, Raw from)
    {
        into.MoveX = Math.Clamp(into.MoveX + from.MoveX, -1, 1);
        into.MoveY = Math.Clamp(into.MoveY + from.MoveY, -1, 1);
        into.Action |= from.Action;
        into.Alt |= from.Alt;
        into.LeftPunch |= from.LeftPunch;
        into.RightPunch |= from.RightPunch;
        into.Pause |= from.Pause;
        into.Confirm |= from.Confirm;
        into.Back |= from.Back;
        into.Up |= from.Up;
        into.Down |= from.Down;
        into.Left |= from.Left;
        into.Right |= from.Right;
        if (from.Swing > into.Swing)
        {
            into.Swing = from.Swing;
            into.SwingSide = from.SwingSide;
        }
    }

    private Raw Read(Source source, bool windowActive, WiimoteButtons wiimote, float swing, int side)
    {
        var raw = default(Raw);
        switch (source)
        {
            case Source.Keyboard or Source.KeyboardLeft or Source.KeyboardRight when windowActive:
            {
                var arrows = source != Source.KeyboardLeft;
                var wasd = source != Source.KeyboardRight;
                bool Down(Key k) => Keyboard.IsKeyDown(k);
                var left = (arrows && Down(Key.Left)) || (wasd && Down(Key.A));
                var right = (arrows && Down(Key.Right)) || (wasd && Down(Key.D));
                var up = (arrows && Down(Key.Up)) || (wasd && Down(Key.W));
                var down = (arrows && Down(Key.Down)) || (wasd && Down(Key.S));
                raw.MoveX = (right ? 1 : 0) - (left ? 1 : 0);
                raw.MoveY = (up ? 1 : 0) - (down ? 1 : 0);
                raw.Up = up;
                raw.Down = down;
                raw.Left = left;
                raw.Right = right;
                switch (source)
                {
                    case Source.KeyboardLeft:
                        raw.Action = Down(Key.Space);
                        raw.Alt = Down(Key.LeftShift);
                        raw.LeftPunch = Down(Key.Q);
                        raw.RightPunch = Down(Key.E);
                        break;
                    case Source.KeyboardRight:
                        raw.Action = Down(Key.Enter) || Down(Key.RightCtrl) || Down(Key.NumPad0);
                        raw.Alt = Down(Key.RightShift);
                        raw.LeftPunch = Down(Key.K);
                        raw.RightPunch = Down(Key.L);
                        break;
                    default:
                        raw.Action = Down(Key.Space) || Down(Key.Enter);
                        raw.Alt = Down(Key.LeftShift) || Down(Key.RightShift) || Down(Key.Tab);
                        raw.LeftPunch = Down(Key.Q) || Down(Key.Z);
                        raw.RightPunch = Down(Key.E) || Down(Key.X);
                        break;
                }
                raw.Confirm = raw.Action;
                raw.Pause = Down(Key.P);
                raw.Back = Down(Key.Back);
                break;
            }
            case Source.Mouse when windowActive:
                raw.Action = Mouse.LeftButton == MouseButtonState.Pressed;
                raw.LeftPunch = raw.Action;
                raw.RightPunch = Mouse.RightButton == MouseButtonState.Pressed;
                raw.Alt = Mouse.MiddleButton == MouseButtonState.Pressed;
                raw.Confirm = raw.Action;
                break;
            case Source.Wiimote:
                raw.MoveX = ((wiimote & WiimoteButtons.Right) != 0 ? 1 : 0) - ((wiimote & WiimoteButtons.Left) != 0 ? 1 : 0);
                raw.MoveY = ((wiimote & WiimoteButtons.Up) != 0 ? 1 : 0) - ((wiimote & WiimoteButtons.Down) != 0 ? 1 : 0);
                raw.Up = (wiimote & WiimoteButtons.Up) != 0;
                raw.Down = (wiimote & WiimoteButtons.Down) != 0;
                raw.Left = (wiimote & WiimoteButtons.Left) != 0;
                raw.Right = (wiimote & WiimoteButtons.Right) != 0;
                raw.Action = (wiimote & WiimoteButtons.A) != 0;
                raw.Alt = (wiimote & WiimoteButtons.B) != 0;
                raw.LeftPunch = (wiimote & WiimoteButtons.One) != 0;
                raw.RightPunch = (wiimote & WiimoteButtons.Two) != 0;
                raw.Pause = (wiimote & WiimoteButtons.Plus) != 0;
                raw.Confirm = raw.Action;
                raw.Back = (wiimote & WiimoteButtons.Minus) != 0;
                raw.Swing = swing;
                raw.SwingSide = side;
                break;
            case >= Source.Pad0 and <= Source.Pad3:
            {
                var state = XInput.GetState(source - Source.Pad0);
                if (!state.Connected) break;
                var b = state.Buttons;
                bool Has(GamepadButtons button) => (b & button) != 0;
                raw.MoveX = Math.Clamp(state.LeftX + (Has(GamepadButtons.DPadRight) ? 1 : 0) - (Has(GamepadButtons.DPadLeft) ? 1 : 0), -1, 1);
                raw.MoveY = Math.Clamp(state.LeftY + (Has(GamepadButtons.DPadUp) ? 1 : 0) - (Has(GamepadButtons.DPadDown) ? 1 : 0), -1, 1);
                raw.Up = Has(GamepadButtons.DPadUp) || state.LeftY > 0.6;
                raw.Down = Has(GamepadButtons.DPadDown) || state.LeftY < -0.6;
                raw.Left = Has(GamepadButtons.DPadLeft) || state.LeftX < -0.6;
                raw.Right = Has(GamepadButtons.DPadRight) || state.LeftX > 0.6;
                raw.Action = Has(GamepadButtons.A) || Has(GamepadButtons.X);
                raw.Alt = Has(GamepadButtons.B) || Has(GamepadButtons.Y);
                raw.LeftPunch = Has(GamepadButtons.LeftShoulder) || state.LeftTrigger > 0.5;
                raw.RightPunch = Has(GamepadButtons.RightShoulder) || state.RightTrigger > 0.5;
                raw.Pause = Has(GamepadButtons.Start);
                raw.Confirm = Has(GamepadButtons.A);
                raw.Back = Has(GamepadButtons.B);
                break;
            }
        }
        return raw;
    }

    private PlayerCommand ToCommand(string key, Raw raw)
    {
        var last = _latches.GetValueOrDefault(key);
        var command = new PlayerCommand
        {
            MoveX = (float)raw.MoveX,
            MoveY = (float)raw.MoveY,
            Action = raw.Action,
            ActionPressed = raw.Action && !last.Action,
            ActionReleased = !raw.Action && last.Action,
            Alt = raw.Alt,
            AltPressed = raw.Alt && !last.Alt,
            LeftPunch = raw.LeftPunch && !last.LeftPunch,
            RightPunch = raw.RightPunch && !last.RightPunch,
            Swing = raw.Swing,
            SwingSide = raw.SwingSide,
        };
        _latches[key] = new Latch { Action = raw.Action, Alt = raw.Alt, LeftPunch = raw.LeftPunch, RightPunch = raw.RightPunch };
        return command;
    }

    private MenuInput ToMenu(Raw raw)
    {
        var last = _latches.GetValueOrDefault("menu");
        var menu = new MenuInput
        {
            Up = raw.Up && !last.Up,
            Down = raw.Down && !last.Down,
            Left = raw.Left && !last.Left,
            Right = raw.Right && !last.Right,
            Confirm = raw.Confirm && !last.Confirm,
            Back = raw.Back && !last.Back,
            Pause = raw.Pause && !last.Pause,
        };
        _latches["menu"] = new Latch { Up = raw.Up, Down = raw.Down, Left = raw.Left, Right = raw.Right, Confirm = raw.Confirm, Back = raw.Back, Pause = raw.Pause };
        return menu;
    }

    public void Dispose()
    {
        if (_host.Input is { } input)
        {
            input.WiimoteRaw -= OnWiimote;
            input.GameCapture = false;
        }
    }
}
