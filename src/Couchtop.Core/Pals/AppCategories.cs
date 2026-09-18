namespace Couchtop.Core.Pals;

public enum AppCategory
{
    Unknown,
    Game,
    GameStore,
    Browser,
    Music,
    Video,
    Code,
    Office,
    Chat,
    Art,
    Notes,
    Calculator,
    Mail,
    Terminal,
    Files,
    Settings,
}

/// <summary>
/// Guesses what kind of program something is from its file name, folder and title, so a Pal can say something
/// that fits. Only a hint for small talk: a wrong guess just means a more generic line.
/// </summary>
public static class AppCategories
{
    private static readonly (AppCategory Category, string[] Words)[] Table =
    {
        (AppCategory.GameStore, new[] { "steam", "epicgameslauncher", "epic games launcher", "battle.net", "galaxyclient", "gog galaxy", "eadesktop", "ea app", "origin", "ubisoftconnect", "upc", "xboxapp", "xbox", "itch", "playnite", "heroic" }),
        (AppCategory.Browser, new[] { "chrome", "firefox", "msedge", "edge", "brave", "opera", "vivaldi", "arc", "librewolf", "waterfox", "iexplore", "browser" }),
        (AppCategory.Music, new[] { "spotify", "itunes", "applemusic", "apple music", "foobar", "winamp", "musicbee", "aimp", "tidal", "deezer", "amazon music", "groove", "audacious" }),
        (AppCategory.Video, new[] { "vlc", "mpc-hc", "mpc-be", "mpv", "potplayer", "netflix", "plex", "kodi", "jellyfin", "disney", "prime video", "hulu", "twitch", "youtube", "media player", "movies", "wmplayer", "stremio" }),
        (AppCategory.Code, new[] { "devenv", "visual studio", "code", "vscodium", "cursor", "rider", "idea", "pycharm", "webstorm", "clion", "goland", "android studio", "sublime", "notepad++", "atom", "unity", "godot", "unrealeditor", "zed" }),
        (AppCategory.Office, new[] { "winword", "word", "excel", "powerpnt", "powerpoint", "onenote", "libreoffice", "soffice", "wps", "acrobat", "acrord32", "sumatrapdf", "notion", "obsidian", "docs", "sheets" }),
        (AppCategory.Chat, new[] { "discord", "slack", "teams", "whatsapp", "telegram", "signal", "zoom", "skype", "messenger", "element", "mumble", "teamspeak" }),
        (AppCategory.Art, new[] { "mspaint", "paint", "photoshop", "krita", "gimp", "blender", "clipstudio", "clip studio", "aseprite", "figma", "illustrator", "inkscape", "affinity", "procreate", "medibang", "paint.net", "sai" }),
        (AppCategory.Notes, new[] { "notepad", "sticky notes", "stikynot", "wordpad" }),
        (AppCategory.Calculator, new[] { "calc", "calculator" }),
        (AppCategory.Mail, new[] { "outlook", "thunderbird", "mail", "hxoutlook" }),
        (AppCategory.Terminal, new[] { "windowsterminal", "terminal", "wt", "cmd", "powershell", "pwsh", "conhost", "wsl", "git bash", "mintty" }),
        (AppCategory.Files, new[] { "explorer", "file explorer", "files", "totalcmd", "directory opus", "dopus" }),
        (AppCategory.Settings, new[] { "systemsettings", "settings", "control panel", "control" }),
    };

    /// <summary>Folder names that almost always mean a game.</summary>
    private static readonly string[] GameFolders =
    {
        @"\steamapps\common\", @"\epic games\", @"\gog galaxy\games\", @"\gog games\", @"\ubisoft game launcher\games\",
        @"\ea games\", @"\riot games\", @"\xboxgames\", @"\games\", @"\itch\apps\", @"\battle.net\",
    };

    private static readonly string[] GameNames =
    {
        "minecraft", "roblox", "fortnite", "valorant", "league of legends", "leagueclient", "overwatch", "rocket league", "genshin",
        "apex", "counter-strike", "cs2", "dota", "terraria", "stardew", "among us", "fall guys", "rainbow six", "hades", "celeste",
        "hollow knight", "elden ring", "cyberpunk", "witcher", "palworld", "helldivers", "baldur", "sims", "forza", "halo",
        "destiny", "warframe", "pubg", "osu", "geometry dash", "portal", "half-life", "the finals", "marvel rivals", "dead by daylight",
    };

    public static AppCategory Classify(string? processPath, string? title, bool knownGame = false)
    {
        if (knownGame) return AppCategory.Game;
        var path = (processPath ?? "").ToLowerInvariant();
        var file = Path.GetFileNameWithoutExtension(path);
        var text = (title ?? "").ToLowerInvariant();

        if (GameNames.Any(n => file.Contains(n) || text.Contains(n))) return AppCategory.Game;
        if (GameFolders.Any(path.Contains)) return AppCategory.Game;

        // Whole-word matches on the program's file name first, then on its title.
        foreach (var (category, words) in Table)
            if (words.Any(w => file == w || file.Replace(" ", "") == w.Replace(" ", ""))) return category;
        foreach (var (category, words) in Table)
            if (words.Any(w => w.Length >= 4 && (file.Contains(w.Replace(" ", "")) || ContainsWord(text, w)))) return category;
        return AppCategory.Unknown;
    }

    private static bool ContainsWord(string text, string word)
    {
        var at = text.IndexOf(word, StringComparison.Ordinal);
        while (at >= 0)
        {
            var before = at == 0 || !char.IsLetterOrDigit(text[at - 1]);
            var end = at + word.Length;
            var after = end >= text.Length || !char.IsLetterOrDigit(text[end]);
            if (before && after) return true;
            at = text.IndexOf(word, at + 1, StringComparison.Ordinal);
        }
        return false;
    }
}
