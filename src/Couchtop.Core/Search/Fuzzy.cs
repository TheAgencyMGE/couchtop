namespace Couchtop.Core.Search;

public enum SearchKind
{
    Channel,
    App,
    Window,
    File,
    Folder,
    Setting,
    Action,
    Web,
}

/// <param name="Target">What the command palette acts on: a path, a channel id, a window handle, a settings key.</param>
public sealed record SearchHit(SearchKind Kind, string Title, string? Subtitle, string Target, int Score)
{
    /// <summary>Ties are broken by kind so channels and apps beat loose file matches at equal score.</summary>
    public int Rank => Score * 100 - (int)Kind;
}

/// <summary>
/// Subsequence matching with the bonuses people expect from a command palette: prefixes, word starts and runs of
/// consecutive letters score highest, and shorter names win ties ("set" finds Settings before Sunset).
/// </summary>
public static class Fuzzy
{
    public const int NoMatch = -1;

    public static bool IsMatch(string candidate, string query) => Score(candidate, query) >= 0;

    /// <summary>Higher is better; <see cref="NoMatch"/> when the query isn't a subsequence of the candidate.</summary>
    public static int Score(string candidate, string query)
    {
        if (string.IsNullOrEmpty(query)) return 0;
        if (string.IsNullOrEmpty(candidate)) return NoMatch;

        if (candidate.Equals(query, StringComparison.OrdinalIgnoreCase)) return 1000 + LengthBonus(candidate);
        if (candidate.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 700 + LengthBonus(candidate);

        var score = 0;
        var candidateIndex = 0;
        var consecutive = 0;
        foreach (var wanted in query)
        {
            if (wanted == ' ') continue;
            var found = -1;
            for (var i = candidateIndex; i < candidate.Length; i++)
            {
                if (char.ToLowerInvariant(candidate[i]) != char.ToLowerInvariant(wanted)) continue;
                found = i;
                break;
            }
            if (found < 0) return NoMatch;

            if (found == 0) score += 25;
            else if (IsWordStart(candidate, found)) score += 15;
            if (found == candidateIndex && candidateIndex > 0)
            {
                consecutive++;
                score += 8 + consecutive * 2;
            }
            else
            {
                consecutive = 0;
                score -= Math.Min(6, found - candidateIndex);
            }
            if (candidate[found] == wanted) score += 1;
            candidateIndex = found + 1;
        }
        return Math.Max(0, score + LengthBonus(candidate));
    }

    /// <summary>Scores against several fields (title, then subtitle/path) and keeps the best, with later fields worth less.</summary>
    public static int ScoreAny(string query, params string?[] fields)
    {
        var best = NoMatch;
        for (var i = 0; i < fields.Length; i++)
        {
            var field = fields[i];
            if (string.IsNullOrEmpty(field)) continue;
            var score = Score(field!, query);
            if (score < 0) continue;
            score = i == 0 ? score : score / (i + 1);
            if (score > best) best = score;
        }
        return best;
    }

    private static int LengthBonus(string candidate) => Math.Max(0, 40 - candidate.Length);

    private static bool IsWordStart(string text, int index)
    {
        if (index <= 0) return true;
        var previous = text[index - 1];
        if (previous is ' ' or '-' or '_' or '.' or '/' or '\\' or '(' or '[') return true;
        return char.IsLower(previous) && char.IsUpper(text[index]);
    }
}
