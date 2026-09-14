using Couchtop.Core.Channels;

namespace Couchtop.Core.Discovery;

public enum AppCategory
{
    Game,
    GameLauncher,
    Browser,
    Media,
    Communication,
    Productivity,
    Creative,
    Development,
    Utility,
    System,
}

public sealed class DiscoveredApp
{
    public string SourceKey { get; set; } = "";
    public string Title { get; set; } = "";
    public ChannelKind Kind { get; set; }
    public LaunchSpec Launch { get; set; } = new();
    public string? IconSource { get; set; }
    public string? BannerImage { get; set; }
    public AppCategory Category { get; set; } = AppCategory.Utility;
    public int Priority { get; set; }
    public bool ExcludeFromAutoSeed { get; set; }
    public string Source { get; set; } = "";
}

public interface IAppSource
{
    string Name { get; }
    IReadOnlyList<DiscoveredApp> Discover(CancellationToken cancellationToken);
}

public static class AppClassifier
{
    private static readonly string[] NoisePhrases =
    {
        "uninstall", "uninstaller", "readme", "read me", "help", "documentation", "manual", "release notes",
        "license", "licence", "website", "web site", "what's new", "whats new", "changelog", "user guide",
        "faq", "eula", "report a bug", "report a problem", "send feedback", "repair", "safe mode", "online support", "migration assistant",
    };

    private static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".pdf", ".chm", ".htm", ".html", ".rtf", ".md", ".hlp", ".log", ".ini", ".xml", ".doc", ".docx", ".nfo",
    };

    private static readonly (AppCategory Category, int Priority, string[] Keywords)[] Rules =
    {
        (AppCategory.GameLauncher, 90, new[] { "steam", "epic games", "battle.net", "gog galaxy", "ea app", "ubisoft connect", "xbox", "itch", "playnite", "retroarch", "dolphin", "heroic" }),
        (AppCategory.Browser, 86, new[] { "chrome", "firefox", "microsoft edge", "brave", "opera", "vivaldi", "librewolf", "arc", "zen browser" }),
        (AppCategory.Media, 80, new[] { "spotify", "vlc", "netflix", "media player", "music", "movies", "plex", "jellyfin", "kodi", "youtube", "prime video", "disney", "twitch", "audacious", "foobar", "itunes", "photos" }),
        (AppCategory.Communication, 72, new[] { "discord", "teams", "zoom", "slack", "whatsapp", "telegram", "signal", "outlook", "mail", "thunderbird", "skype" }),
        (AppCategory.Creative, 60, new[] { "photoshop", "obs studio", "blender", "paint", "gimp", "krita", "inkscape", "davinci", "premiere", "clipchamp", "snipping", "camera" }),
        (AppCategory.Productivity, 58, new[] { "word", "excel", "powerpoint", "onenote", "libreoffice", "notion", "obsidian", "calculator", "notepad", "calendar", "clock", "to do", "sticky notes" }),
        (AppCategory.Development, 45, new[] { "visual studio", "code", "terminal", "git", "android studio", "rider", "intellij", "pycharm", "unity", "unreal" }),
    };

    private static readonly string[] SystemFolders =
    {
        "windows tools", "administrative tools", "accessibility", "system tools", "windows powershell", "maintenance",
        "windows administrative tools", "windows kits", "microsoft sdks", "startup",
    };

    /// <summary>True for Couchtop's own shortcuts and executables, which must never become channels.</summary>
    public static bool IsSelf(DiscoveredApp app)
    {
        if (app.SourceKey.StartsWith("lnk:couchtop/", StringComparison.OrdinalIgnoreCase)) return true;
        foreach (var path in new[] { app.Launch.Path, app.Launch.ShortcutPath })
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            var file = System.IO.Path.GetFileName(path);
            if (file.StartsWith("Couchtop.", StringComparison.OrdinalIgnoreCase) && file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    public static bool IsNoise(string title, string? targetPath = null)
    {
        if (string.IsNullOrWhiteSpace(title)) return true;
        var t = title.ToLowerInvariant();
        foreach (var phrase in NoisePhrases)
            if (ContainsWord(t, phrase)) return true;

        if (!string.IsNullOrWhiteSpace(targetPath))
        {
            var file = System.IO.Path.GetFileName(targetPath).ToLowerInvariant();
            if (file.StartsWith("unins") || file.StartsWith("uninst") || file.Contains("uninstall")) return true;
            if (DocumentExtensions.Contains(System.IO.Path.GetExtension(file))) return true;
        }
        return false;
    }

    public static (AppCategory Category, int Priority, bool ExcludeFromAutoSeed) Classify(string title, string? targetPath, string? folder)
    {
        var haystack = (title + " " + System.IO.Path.GetFileNameWithoutExtension(targetPath ?? "")).ToLowerInvariant();
        var folderLower = (folder ?? "").ToLowerInvariant();
        if (SystemFolders.Any(f => folderLower.Contains(f))) return (AppCategory.System, 10, true);

        foreach (var (category, priority, keywords) in Rules)
            if (keywords.Any(k => ContainsWord(haystack, k))) return (category, priority, false);

        return (AppCategory.Utility, 30, false);
    }

    internal static bool ContainsWord(string haystack, string word)
    {
        var index = 0;
        while ((index = haystack.IndexOf(word, index, StringComparison.Ordinal)) >= 0)
        {
            var before = index == 0 || !char.IsLetterOrDigit(haystack[index - 1]);
            var afterIndex = index + word.Length;
            var after = afterIndex >= haystack.Length || !char.IsLetterOrDigit(haystack[afterIndex]);
            if (before && after) return true;
            index = afterIndex;
        }
        return false;
    }
}
