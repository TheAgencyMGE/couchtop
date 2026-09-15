using Couchtop.Core.Diagnostics;
using Couchtop.Core.Storage;

namespace Couchtop.Core.Sports;

public enum TrainingMedal
{
    None,
    Bronze,
    Silver,
    Gold,
}

public sealed class SportStats
{
    /// <summary>0..1000; grows when you beat the computer and picks the computer's difficulty.</summary>
    public int Skill { get; set; }
    public int Played { get; set; }
    public int Wins { get; set; }

    /// <summary>Best match result in the sport's own units (pins, strokes to par, runs, games, knockdowns).</summary>
    public int? BestMatch { get; set; }

    public int? BestTraining { get; set; }
    public TrainingMedal Medal { get; set; }
}

/// <summary>How a player's Pal looks. Indices into the palettes the presentation layer defines.</summary>
public sealed class PalLook
{
    public int Shirt { get; set; }
    public int Skin { get; set; }
    public int Hair { get; set; }
}

public sealed class SportsRecords
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public Dictionary<SportKind, SportStats> Stats { get; set; } = new();
    public List<PalLook> Pals { get; set; } = new();

    public SportStats For(SportKind sport)
    {
        if (!Stats.TryGetValue(sport, out var stats))
        {
            stats = new SportStats();
            Stats[sport] = stats;
        }
        return stats;
    }

    public PalLook Pal(int player)
    {
        while (Pals.Count <= player) Pals.Add(new PalLook { Shirt = Pals.Count, Skin = Pals.Count % 3, Hair = Pals.Count % 4 });
        return Pals[player];
    }

    public SportsRecords Normalize()
    {
        Stats ??= new();
        Pals ??= new();
        foreach (var stats in Stats.Values)
        {
            stats.Skill = Math.Clamp(stats.Skill, 0, SkillRating.Max);
            stats.Played = Math.Max(0, stats.Played);
            stats.Wins = Math.Clamp(stats.Wins, 0, stats.Played);
            if (!Enum.IsDefined(stats.Medal)) stats.Medal = TrainingMedal.None;
        }
        SchemaVersion = CurrentSchemaVersion;
        return this;
    }
}

public static class SkillRating
{
    public const int Max = 1000;

    public static int Update(int skill, bool won, int margin, int difficulty)
    {
        var change = won
            ? 30 + Math.Min(margin, 5) * 6 + difficulty * 8
            : -(18 + Math.Min(margin, 5) * 3) + difficulty * 3;
        return Math.Clamp(skill + change, 0, Max);
    }

    public static string Level(int skill) => skill switch
    {
        < 150 => "Rookie",
        < 350 => "Amateur",
        < 600 => "Pro",
        < 850 => "Star",
        _ => "Legend",
    };

    /// <summary>Computer opponent difficulty (0..3) that suits a player's skill.</summary>
    public static int DifficultyFor(int skill) => Math.Clamp(skill / 250, 0, 3);
}

public sealed record TrainingGoal(string Name, string Description, string Unit, int Bronze, int Silver, int Gold);

public static class TrainingGoals
{
    public static TrainingGoal For(SportKind sport) => sport switch
    {
        SportKind.Tennis => new("Target Rally", "Return 15 balls from the ball machine and land them on the glowing targets.", "points", 10, 20, 30),
        SportKind.Baseball => new("Home Run Derby", "Ten pitches. Knock as many as you can over the fence.", "home runs", 2, 4, 7),
        SportKind.Bowling => new("Spare Master", "Eight tricky pin setups, one ball each. Clear them all.", "cleared", 3, 5, 7),
        SportKind.Golf => new("Nearest the Pin", "Five tee shots at a short hole. Land them close to the flag.", "points", 20, 32, 42),
        SportKind.Boxing => new("Mitt Drill", "Punch the lit mitt as fast as you can for 30 seconds.", "hits", 15, 24, 32),
        _ => throw new ArgumentOutOfRangeException(nameof(sport)),
    };

    public static TrainingMedal MedalFor(SportKind sport, int score)
    {
        var goal = For(sport);
        return score >= goal.Gold ? TrainingMedal.Gold
            : score >= goal.Silver ? TrainingMedal.Silver
            : score >= goal.Bronze ? TrainingMedal.Bronze
            : TrainingMedal.None;
    }
}

public sealed record RecordUpdate(bool NewBest, TrainingMedal Medal, bool MedalImproved, int SkillBefore, int SkillAfter);

/// <summary>Loads and saves sports progress (sports.json in the Couchtop data folder).</summary>
public sealed class SportsRecordsService
{
    private readonly string _path;

    public SportsRecordsService(string path)
    {
        _path = path;
        Current = JsonStore.Load(path, () => new SportsRecords(), r => r.SchemaVersion >= 1).Value.Normalize();
    }

    public SportsRecords Current { get; }

    /// <summary>Applies a finished game to player one's records. Only games player one took part in as a human count.</summary>
    public RecordUpdate Record(SportResult result, SportSetup setup)
    {
        var stats = Current.For(result.Sport);
        var before = stats.Skill;
        var newBest = false;
        var medal = TrainingMedal.None;
        var improved = false;

        if (result.Mode == SportMode.Training && result.TrainingScore is { } score)
        {
            newBest = stats.BestTraining is null || score > stats.BestTraining;
            if (newBest) stats.BestTraining = score;
            medal = TrainingGoals.MedalFor(result.Sport, score);
            if (medal > stats.Medal)
            {
                stats.Medal = medal;
                improved = true;
            }
        }
        else if (result.Mode == SportMode.Match && setup.Humans >= 1)
        {
            stats.Played++;
            var p1 = result.Scores.Count > 0 ? result.Scores[0] : 0;
            var lowerIsBetter = result.Sport == SportKind.Golf;
            newBest = stats.BestMatch is null || (lowerIsBetter ? p1 < stats.BestMatch : p1 > stats.BestMatch);
            if (newBest) stats.BestMatch = p1;

            if (result.Winner == 0) stats.Wins++;
            // Skill only moves against the computer, so friends can't farm it off each other.
            if (setup.Humans == 1 && setup.Players > 1)
                stats.Skill = SkillRating.Update(stats.Skill, result.Winner == 0, Math.Abs(result.Margin), setup.Difficulty);
        }

        Save();
        return new RecordUpdate(newBest, medal, improved, before, stats.Skill);
    }

    public void Save()
    {
        try
        {
            JsonStore.Save(_path, Current.Normalize());
        }
        catch (Exception ex)
        {
            Log.Warn("Could not save sports records", ex);
        }
    }
}
