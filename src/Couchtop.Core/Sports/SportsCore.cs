namespace Couchtop.Core.Sports;

public enum SportKind
{
    Tennis,
    Baseball,
    Bowling,
    Golf,
    Boxing,
}

public enum SportMode
{
    Match,
    Training,
}

/// <summary>
/// One frame of input for one player, already merged from keyboard, mouse, controller and Wii Remote.
/// Every sport can be played with just <see cref="MoveX"/>/<see cref="MoveY"/> and <see cref="ActionPressed"/>.
/// </summary>
public struct PlayerCommand
{
    /// <summary>Stick / arrow keys, -1..1. Positive Y is up/forward.</summary>
    public float MoveX, MoveY;

    /// <summary>A button, Space or left click.</summary>
    public bool Action, ActionPressed, ActionReleased;

    /// <summary>B button or Shift.</summary>
    public bool Alt, AltPressed;

    /// <summary>Boxing punches (bumpers, Q/E, mouse buttons, Wii Remote swings).</summary>
    public bool LeftPunch, RightPunch;

    /// <summary>Strength (0..1) of a motion swing detected this frame, 0 when there was none.</summary>
    public float Swing;

    /// <summary>Direction of the motion swing: -1 left, +1 right, 0 unknown.</summary>
    public int SwingSide;

    /// <summary>True when the player asked for a swing/shot this frame, by button or by motion.</summary>
    public readonly bool SwingNow => ActionPressed || Swing > 0;
}

public enum SportEventKind
{
    Swing,
    Hit,
    PowerHit,
    Bounce,
    Net,
    Out,
    Fault,
    Point,
    Game,
    Serve,
    Pitch,
    Contact,
    Catch,
    Foul,
    Strike,
    StrikeOut,
    Single,
    Double,
    Triple,
    HomeRun,
    Run,
    Roll,
    PinHit,
    PinsFall,
    BowlingStrike,
    Spare,
    Gutter,
    GolfSwing,
    Land,
    Putt,
    Holed,
    Splash,
    Bunker,
    Punch,
    PunchHit,
    Block,
    Dodge,
    KnockDown,
    GetUp,
    Bell,
    Target,
    Miss,
    Cheer,
    Announce,
    Finish,
}

/// <summary>Something the presentation layer should show or play (a sound, a popup, a crowd reaction).</summary>
public readonly record struct SportEvent(SportEventKind Kind, int Player = -1, string? Text = null, float Strength = 1);

/// <param name="Players">Total number of competitors, humans first.</param>
/// <param name="Humans">How many of the players are people; the rest are computer-controlled.</param>
/// <param name="Difficulty">Computer skill, 0 (easy) to 3 (expert).</param>
/// <param name="Seed">0 picks a random seed.</param>
public sealed record SportSetup(SportKind Sport, SportMode Mode = SportMode.Match, int Players = 2, int Humans = 1, int Difficulty = 1, int Seed = 0)
{
    public bool IsCpu(int player) => player >= Humans;
}

/// <param name="Scores">Final score per player (meaning depends on the sport).</param>
/// <param name="Winner">Index of the winning player, or null for a draw / training.</param>
public sealed record SportResult(
    SportKind Sport,
    SportMode Mode,
    IReadOnlyList<int> Scores,
    int? Winner,
    string Headline,
    string Detail,
    int? TrainingScore = null,
    int Margin = 0);

/// <summary>
/// Base class for a sport's game state. Pure simulation: no rendering, no I/O, deterministic for a given seed,
/// so full matches can be played computer-vs-computer in tests.
/// </summary>
public abstract class SportSim
{
    private const int MaxQueuedEvents = 256;
    private readonly List<SportEvent> _events = new();

    protected SportSim(SportSetup setup)
    {
        Setup = setup;
        Rng = new Random(setup.Seed == 0 ? Environment.TickCount : setup.Seed);
    }

    public SportSetup Setup { get; }
    protected Random Rng { get; }

    public double Time { get; private set; }
    public SportResult? Result { get; protected set; }
    public bool IsFinished => Result is not null;

    /// <summary>A short hint for the player whose input matters right now ("Press A to serve").</summary>
    public string Prompt { get; protected set; } = "";

    /// <summary>For turn-based sports, the player whose turn it is; -1 when everyone plays at once.</summary>
    public virtual int ActivePlayer => -1;

    /// <summary>A line for the top of the screen (score, frame, hole, round).</summary>
    public abstract string Scoreline { get; }

    public void Update(double dt, IReadOnlyList<PlayerCommand> commands)
    {
        if (IsFinished || double.IsNaN(dt)) return;
        dt = Math.Clamp(dt, 0, 1.0 / 20);
        Time += dt;
        Step(dt, commands);
    }

    protected abstract void Step(double dt, IReadOnlyList<PlayerCommand> commands);

    protected static PlayerCommand CommandFor(IReadOnlyList<PlayerCommand> commands, int player) =>
        player >= 0 && player < commands.Count ? commands[player] : default;

    public IReadOnlyList<SportEvent> DrainEvents()
    {
        var copy = _events.ToArray();
        _events.Clear();
        return copy;
    }

    protected void Emit(SportEventKind kind, int player = -1, string? text = null, float strength = 1)
    {
        if (_events.Count >= MaxQueuedEvents) _events.RemoveAt(0);
        _events.Add(new SportEvent(kind, player, text, strength));
    }

    protected static double Lerp(double a, double b, double t) => a + (b - a) * Math.Clamp(t, 0, 1);

    /// <summary>Uniform random number in [min, max).</summary>
    protected double Range(double min, double max) => min + Rng.NextDouble() * (max - min);

    /// <summary>Computer reaction noise: bigger for easier opponents.</summary>
    protected double CpuError(double scale) => (Rng.NextDouble() * 2 - 1) * scale * (1.35 - Setup.Difficulty * 0.3);

    public static SportSim Create(SportSetup setup) => setup.Sport switch
    {
        SportKind.Tennis => new TennisSim(setup),
        SportKind.Baseball => new BaseballSim(setup),
        SportKind.Bowling => new BowlingSim(setup),
        SportKind.Golf => new GolfSim(setup),
        SportKind.Boxing => new BoxingSim(setup),
        _ => throw new ArgumentOutOfRangeException(nameof(setup)),
    };

    public static string DisplayName(SportKind sport) => sport.ToString();

    /// <summary>Players a sport supports (inclusive).</summary>
    public static (int Min, int Max) PlayerRange(SportKind sport) => sport switch
    {
        SportKind.Bowling or SportKind.Golf => (1, 4),
        _ => (1, 2),
    };

    public static bool IsTurnBased(SportKind sport) => sport is SportKind.Bowling or SportKind.Golf;
}

/// <summary>Simple 3D vector in meters (x = right, y = up, z = forward), kept tiny and allocation-free.</summary>
public struct V3
{
    public double X, Y, Z;

    public V3(double x, double y, double z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public static V3 operator +(V3 a, V3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static V3 operator -(V3 a, V3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static V3 operator *(V3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);
    public readonly double Length => Math.Sqrt(X * X + Y * Y + Z * Z);
    public readonly double FlatLength => Math.Sqrt(X * X + Z * Z);
    public readonly bool IsFinite => double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Z);
    public override readonly string ToString() => $"({X:0.00}, {Y:0.00}, {Z:0.00})";
}

public static class Ballistics
{
    public const double Gravity = 9.81;

    /// <summary>Launch velocity that carries a ball from <paramref name="from"/> to <paramref name="to"/> in exactly <paramref name="seconds"/> (no drag).</summary>
    public static V3 LaunchVelocity(V3 from, V3 to, double seconds)
    {
        seconds = Math.Max(0.05, seconds);
        return new V3(
            (to.X - from.X) / seconds,
            (to.Y - from.Y + 0.5 * Gravity * seconds * seconds) / seconds,
            (to.Z - from.Z) / seconds);
    }

    /// <summary>Height of a ball launched with <paramref name="velocity"/> when it has travelled to depth <paramref name="z"/>.</summary>
    public static double HeightAtZ(V3 from, V3 velocity, double z)
    {
        if (Math.Abs(velocity.Z) < 1e-6) return from.Y;
        var t = (z - from.Z) / velocity.Z;
        if (t < 0) return from.Y;
        return from.Y + velocity.Y * t - 0.5 * Gravity * t * t;
    }

    /// <summary>Seconds until a ball at height y with vertical speed vy reaches height <paramref name="target"/> on the way down.</summary>
    public static double TimeToHeight(double y, double vy, double target)
    {
        var a = -0.5 * Gravity;
        var b = vy;
        var c = y - target;
        var disc = b * b - 4 * a * c;
        if (disc < 0) return double.NaN;
        var sq = Math.Sqrt(disc);
        return Math.Max((-b + sq) / (2 * a), (-b - sq) / (2 * a));
    }
}
