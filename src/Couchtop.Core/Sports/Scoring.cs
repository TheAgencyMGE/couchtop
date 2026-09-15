namespace Couchtop.Core.Sports;

/// <summary>Ten-pin bowling score sheet with strikes, spares and the tenth-frame bonus balls.</summary>
public sealed class BowlingScorer
{
    private readonly List<int> _rolls = new();

    public IReadOnlyList<int> Rolls => _rolls;

    /// <summary>Current frame (0..9), the roll within it, and the pins that should be standing for it.</summary>
    public (int Frame, int Roll, int Standing) Position
    {
        get
        {
            var (frame, roll, standing, _) = State();
            return (frame, roll, standing);
        }
    }

    public bool IsComplete => State().Complete;

    public void Roll(int pins)
    {
        var (_, _, standing, complete) = State();
        if (complete) throw new InvalidOperationException("The game is already complete.");
        if (pins < 0 || pins > standing) throw new ArgumentOutOfRangeException(nameof(pins), $"Only {standing} pins are standing.");
        _rolls.Add(pins);
    }

    private (int Frame, int Roll, int Standing, bool Complete) State()
    {
        var i = 0;
        for (var f = 0; f < 9; f++)
        {
            if (i >= _rolls.Count) return (f, 0, 10, false);
            if (_rolls[i] == 10)
            {
                i++;
                continue;
            }
            if (i + 1 >= _rolls.Count) return (f, 1, 10 - _rolls[i], false);
            i += 2;
        }

        var tenth = _rolls.Skip(i).ToList();
        switch (tenth.Count)
        {
            case 0: return (9, 0, 10, false);
            case 1: return (9, 1, tenth[0] == 10 ? 10 : 10 - tenth[0], false);
            case 2:
                if (tenth[0] == 10) return (9, 2, tenth[1] == 10 ? 10 : 10 - tenth[1], false);
                if (tenth[0] + tenth[1] == 10) return (9, 2, 10, false);
                return (9, 2, 0, true);
            default: return (9, 3, 0, true);
        }
    }

    /// <summary>Running total after each frame, null while a frame's bonus is still unknown.</summary>
    public int?[] FrameTotals()
    {
        var totals = new int?[10];
        var i = 0;
        var running = 0;
        for (var f = 0; f < 10 && i < _rolls.Count; f++)
        {
            int? frameScore;
            if (f == 9)
            {
                var rest = _rolls.Skip(i).ToList();
                var needed = rest.Count >= 2 && (rest[0] == 10 || rest[0] + rest[1] == 10) ? 3 : 2;
                frameScore = rest.Count >= needed ? rest.Take(needed).Sum() : null;
                i = _rolls.Count;
            }
            else if (_rolls[i] == 10)
            {
                frameScore = i + 2 < _rolls.Count ? 10 + _rolls[i + 1] + _rolls[i + 2] : null;
                i += 1;
            }
            else if (i + 1 < _rolls.Count)
            {
                var two = _rolls[i] + _rolls[i + 1];
                frameScore = two == 10 ? (i + 2 < _rolls.Count ? 10 + _rolls[i + 2] : null) : two;
                i += 2;
            }
            else
            {
                break;
            }

            if (frameScore is null) break;
            running += frameScore.Value;
            totals[f] = running;
        }
        return totals;
    }

    public int Total => FrameTotals().LastOrDefault(t => t is not null) ?? 0;

    /// <summary>Marks shown on the score sheet for a frame, e.g. "X", "7 /", "9 -".</summary>
    public string Marks(int frame)
    {
        var i = 0;
        for (var f = 0; f < frame && i < _rolls.Count; f++) i += f < 9 && _rolls[i] == 10 ? 1 : 2;
        if (i >= _rolls.Count) return "";
        var rolls = frame == 9 ? _rolls.Skip(i).ToList() : _rolls.Skip(i).Take(_rolls[i] == 10 ? 1 : 2).ToList();
        var marks = new List<string>();
        var frameStart = 0;
        for (var r = 0; r < rolls.Count; r++)
        {
            var pins = rolls[r];
            var isFresh = r == frameStart;
            if (isFresh && pins == 10)
            {
                marks.Add("X");
                frameStart = r + 1;
            }
            else if (!isFresh && rolls[frameStart] + pins == 10)
            {
                marks.Add("/");
                frameStart = r + 1;
            }
            else
            {
                marks.Add(pins == 0 ? "-" : pins.ToString());
                if (!isFresh) frameStart = r + 1;
            }
        }
        return string.Join(" ", marks);
    }
}

/// <summary>Tennis game scoring (love, 15, 30, 40, deuce, advantage) for a short match of games.</summary>
public sealed class TennisScorer
{
    private readonly int[] _points = new int[2];
    private readonly int[] _games = new int[2];

    public TennisScorer(int gamesToWin = 2)
    {
        GamesToWin = Math.Max(1, gamesToWin);
    }

    public int GamesToWin { get; }
    public int Server { get; private set; }
    public int? Winner { get; private set; }
    public int Points(int player) => _points[player];
    public int Games(int player) => _games[player];

    /// <summary>Awards a point; returns true when it ended a game.</summary>
    public bool PointTo(int player)
    {
        if (Winner is not null) return false;
        var other = 1 - player;
        _points[player]++;
        if (_points[player] >= 4 && _points[player] - _points[other] >= 2)
        {
            _games[player]++;
            _points[0] = _points[1] = 0;
            Server = 1 - Server;
            if (_games[player] >= GamesToWin) Winner = player;
            return true;
        }
        return false;
    }

    private static string Name(int points) => points switch { 0 => "Love", 1 => "15", 2 => "30", _ => "40" };

    /// <summary>The umpire's call for the current game, server's score first.</summary>
    public string Call(Func<int, string>? playerName = null)
    {
        playerName ??= p => $"P{p + 1}";
        var s = _points[Server];
        var r = _points[1 - Server];
        if (s >= 3 && r >= 3)
        {
            if (s == r) return "Deuce";
            return "Advantage " + playerName(s > r ? Server : 1 - Server);
        }
        if (s == r) return Name(s) + " All";
        return $"{Name(s)} - {Name(r)}";
    }
}

/// <summary>Stroke play across a few holes.</summary>
public sealed class GolfScorer
{
    private readonly int?[,] _strokes;

    public GolfScorer(int players, IReadOnlyList<int> pars)
    {
        Players = players;
        Pars = pars;
        _strokes = new int?[players, pars.Count];
    }

    public int Players { get; }
    public IReadOnlyList<int> Pars { get; }

    public void Record(int player, int hole, int strokes) => _strokes[player, hole] = strokes;
    public int? Strokes(int player, int hole) => _strokes[player, hole];

    public int Total(int player)
    {
        var total = 0;
        for (var h = 0; h < Pars.Count; h++) total += _strokes[player, h] ?? 0;
        return total;
    }

    /// <summary>Strokes relative to par over the holes this player has finished.</summary>
    public int ToPar(int player)
    {
        var diff = 0;
        for (var h = 0; h < Pars.Count; h++)
            if (_strokes[player, h] is { } s) diff += s - Pars[h];
        return diff;
    }

    public static string ToParText(int diff) => diff == 0 ? "E" : diff > 0 ? "+" + diff : diff.ToString();

    public static string Term(int strokes, int par)
    {
        if (strokes == 1) return "Hole in One!";
        return (strokes - par) switch
        {
            <= -3 => "Albatross!",
            -2 => "Eagle!",
            -1 => "Birdie!",
            0 => "Par",
            1 => "Bogey",
            2 => "Double Bogey",
            var over => $"+{over}",
        };
    }
}

public static class BaseRunners
{
    /// <summary>
    /// Moves runners for a hit worth <paramref name="basesGained"/> bases (1 single .. 4 home run) and returns runs scored.
    /// Every runner advances by the same number of bases as the batter.
    /// </summary>
    public static int Advance(bool[] bases, int basesGained)
    {
        if (bases.Length != 3) throw new ArgumentException("Three bases expected.", nameof(bases));
        basesGained = Math.Clamp(basesGained, 1, 4);
        var runs = 0;
        for (var b = 2; b >= 0; b--)
        {
            if (!bases[b]) continue;
            bases[b] = false;
            var target = b + basesGained;
            if (target >= 3) runs++;
            else bases[target] = true;
        }
        if (basesGained >= 4) runs++;
        else bases[basesGained - 1] = true;
        return runs;
    }
}
