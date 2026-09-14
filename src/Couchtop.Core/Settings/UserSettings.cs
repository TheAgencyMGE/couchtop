using Couchtop.Core.Storage;

namespace Couchtop.Core.Settings;

public enum ShellBootstrapMode
{
    /// <summary>cmd.exe guard that falls back to Explorer if Couchtop files are missing.</summary>
    Resilient,
    /// <summary>Starts the Guardian directly (slightly faster, no console flash).</summary>
    Direct,
}

public sealed class UserSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    // Look & feel
    public string Theme { get; set; } = ThemeCatalog.DefaultId;
    public bool UseSystemCursor { get; set; }
    public bool ReduceMotion { get; set; }
    public bool Clock24Hour { get; set; }
    public bool ShowStartupSplash { get; set; } = true;
    public bool QuickLaunch { get; set; }
    public bool HideMenuWhenAppLaunches { get; set; }

    // Sound
    public bool SoundEffects { get; set; } = true;
    public double EffectsVolume { get; set; } = 0.7;
    public bool Ambience { get; set; } = true;
    public double AmbienceVolume { get; set; } = 0.3;

    // Displays
    public string? TargetMonitor { get; set; }
    public bool BackdropOnOtherMonitors { get; set; } = true;

    // Channels & discovery
    public bool AutoAddNewApps { get; set; } = true;
    public bool DiscoverStartMenu { get; set; } = true;
    public bool DiscoverStoreApps { get; set; } = true;
    public bool DiscoverSteam { get; set; } = true;
    public bool DiscoverEpic { get; set; } = true;

    // Input
    public bool ControllerEnabled { get; set; } = true;
    public bool WiimoteEnabled { get; set; } = true;
    public double PointerSpeed { get; set; } = 1.0;
    public string HomeMenuHotkey { get; set; } = "Ctrl+Alt+Home";

    // Built-in channels
    public string BrowserHome { get; set; } = "";
    public string SearchUrl { get; set; } = "https://duckduckgo.com/?q={0}";
    public List<Bookmark> Bookmarks { get; set; } = new();
    public string PhotosFolder { get; set; } = "";

    // Startup / shell
    public bool StartWithWindows { get; set; }
    public bool RunStartupAppsInShell { get; set; } = true;
    public ShellBootstrapMode ShellBootstrap { get; set; } = ShellBootstrapMode.Resilient;
    public bool WelcomeShown { get; set; }
    public bool DesktopHintShown { get; set; }

    /// <summary>Clamps out-of-range values so a hand-edited or damaged file cannot break the UI.</summary>
    public UserSettings Normalize()
    {
        EffectsVolume = Clamp01(EffectsVolume);
        AmbienceVolume = Clamp01(AmbienceVolume);
        PointerSpeed = double.IsFinite(PointerSpeed) ? Math.Clamp(PointerSpeed, 0.25, 3.0) : 1.0;
        Theme = ThemeCatalog.Normalize(Theme);
        if (!Enum.IsDefined(ShellBootstrap)) ShellBootstrap = ShellBootstrapMode.Resilient;
        HomeMenuHotkey = string.IsNullOrWhiteSpace(HomeMenuHotkey) ? "Ctrl+Alt+Home" : HomeMenuHotkey.Trim();
        SearchUrl = string.IsNullOrWhiteSpace(SearchUrl) || !SearchUrl.Contains("{0}") ? "https://duckduckgo.com/?q={0}" : SearchUrl;
        BrowserHome ??= "";
        PhotosFolder ??= "";
        Bookmarks = (Bookmarks ?? new()).Where(b => b is not null && !string.IsNullOrWhiteSpace(b.Url)).Take(48).ToList();
        if (Bookmarks.Count == 0) Bookmarks = DefaultBookmarks();
        SchemaVersion = CurrentSchemaVersion;
        return this;
    }

    public static List<Bookmark> DefaultBookmarks() => new()
    {
        new Bookmark("Wikipedia", "https://www.wikipedia.org"),
        new Bookmark("YouTube", "https://www.youtube.com"),
        new Bookmark("Weather", "https://weather.com"),
        new Bookmark("News", "https://news.google.com"),
        new Bookmark("Maps", "https://www.openstreetmap.org"),
        new Bookmark("Internet Archive", "https://archive.org"),
    };

    private static double Clamp01(double v) => double.IsFinite(v) ? Math.Clamp(v, 0, 1) : 0.5;
}

public sealed record Bookmark(string Title, string Url);

public sealed class SettingsService
{
    private readonly string _path;
    private readonly object _gate = new();

    public SettingsService(AppPaths paths)
    {
        _path = paths.SettingsFile;
        var result = JsonStore.Load(_path, () => new UserSettings(), s => s.SchemaVersion >= 1);
        Current = result.Value.Normalize();
        LoadIssues = result.Issues;
        LoadSource = result.Source;
    }

    public UserSettings Current { get; }
    public IReadOnlyList<string> LoadIssues { get; }
    public LoadSource LoadSource { get; }

    public event EventHandler? Changed;

    public void Save()
    {
        lock (_gate)
        {
            Current.Normalize();
            JsonStore.Save(_path, Current);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
