namespace Couchtop.Core.Settings;

public sealed record MenuStyleInfo(string Id, string Name, string Description);

/// <summary>
/// A whole shell: its own look for every screen, its own icons, sounds, pointer and navigation. Your apps,
/// channels, files and settings are shared between them; nothing else is. Pals and themes belong to the
/// Channels style alone. Ids are stored in settings.json, so never rename one.
/// </summary>
public static class MenuStyleCatalog
{
    /// <summary>The original Couchtop grid of channel tiles.</summary>
    public const string Channels = "channels";

    /// <summary>A rail of sections over a mosaic of flat tiles, in the style of late-2000s console dashboards.</summary>
    public const string Dashboard = "dashboard";

    /// <summary>A cross of categories and items, in the style of late-2000s media-bar consoles.</summary>
    public const string MediaBar = "mediabar";

    public const string DefaultId = Channels;

    public static IReadOnlyList<MenuStyleInfo> All { get; } = new MenuStyleInfo[]
    {
        new(Channels, "Channels", "The Couchtop grid: pages of channel tiles, themes, the hand pointer and Pals"),
        new(Dashboard, "Dashboard", "A green tile shell: lowercase sections, a mosaic of flat tiles, dry clicks"),
        new(MediaBar, "Media Bar", "A blue cross shell: thin icons on a bar, sine blips and a quiet background, 2006 style"),
    };

    public static bool IsKnown(string? id) => id is not null && All.Any(s => s.Id == id);

    public static string Normalize(string? id) => IsKnown(id) ? id! : DefaultId;
}
