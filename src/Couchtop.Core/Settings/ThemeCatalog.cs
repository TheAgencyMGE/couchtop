namespace Couchtop.Core.Settings;

public sealed record ThemeInfo(string Id, string Name, string Description);

/// <summary>The built-in looks. Ids are stored in settings.json, so never rename one; add new ids instead.</summary>
public static class ThemeCatalog
{
    public const string DefaultId = "Classic";

    public static IReadOnlyList<ThemeInfo> All { get; } = new ThemeInfo[]
    {
        new("Classic", "Classic", "Bright white console menu with sky-blue accents"),
        new("Night", "Night", "The dim slate look for dark rooms"),
        new("SkyResort", "Sky Resort", "Glossy glass, open skies, sea and bubbles"),
        new("NeonCity", "Neon City", "Black steel, hazard yellow and cyan neon"),
        new("Midnight", "Midnight", "Sleek, flat and dark"),
        new("Sakura", "Sakura", "Soft pink petals and warm paper"),
        new("Sunset", "Sunset", "Golden hour over the water"),
    };

    public static bool IsKnown(string? id) => id is not null && All.Any(t => t.Id == id);

    public static string Normalize(string? id) => IsKnown(id) ? id! : DefaultId;
}
