using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Couchtop.Core.Channels;
using Couchtop.Core.Diagnostics;
using Couchtop.Core.Native;

namespace Couchtop.Core.Discovery;

/// <summary>Start Menu shortcuts (.lnk, .url, .appref-ms) from the per-user and all-users Programs folders.</summary>
public sealed class StartMenuSource : IAppSource
{
    private static readonly Regex SteamUrl = new(@"^steam://rungameid/(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private readonly IReadOnlyList<string> _roots;
    private readonly Func<string, ShellLinkInfo?> _readLink;
    private readonly Func<string, bool> _fileExists;

    public StartMenuSource(IReadOnlyList<string>? roots = null, Func<string, ShellLinkInfo?>? readLink = null, Func<string, bool>? fileExists = null)
    {
        _roots = roots ?? DefaultRoots();
        _readLink = readLink ?? ShellLink.Read;
        _fileExists = fileExists ?? (p => File.Exists(p) || Directory.Exists(p));
    }

    public string Name => "Start Menu";

    public static IReadOnlyList<string> DefaultRoots() => new[]
    {
        Environment.GetFolderPath(Environment.SpecialFolder.Programs),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
    };

    public IReadOnlyList<DiscoveredApp> Discover(CancellationToken cancellationToken)
    {
        var apps = new List<DiscoveredApp>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MaxRecursionDepth = 6 };

        foreach (var root in _roots.Where(r => !string.IsNullOrWhiteSpace(r) && Directory.Exists(r)))
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", options))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext is not (".lnk" or ".url" or ".appref-ms")) continue;
                try
                {
                    var app = ext switch
                    {
                        ".lnk" => FromShortcut(root, file),
                        ".url" => FromInternetShortcut(root, file),
                        _ => FromClickOnce(root, file),
                    };
                    if (app is not null && seen.Add(app.SourceKey)) apps.Add(app);
                }
                catch (Exception ex)
                {
                    Log.Warn($"Skipping unreadable shortcut {file}", ex);
                }
            }
        }
        return apps;
    }

    private static string KeyFor(string root, string file) =>
        Path.ChangeExtension(Path.GetRelativePath(root, file), null)!.Replace('\\', '/').ToLowerInvariant();

    private DiscoveredApp? FromShortcut(string root, string file)
    {
        var title = Path.GetFileNameWithoutExtension(file);
        var info = _readLink(file);
        if (info is null) return null; // corrupt/broken shortcut file
        var target = info.TargetPath;
        if (AppClassifier.IsNoise(title, target)) return null;
        if (!string.IsNullOrWhiteSpace(target) && Path.IsPathRooted(target) && !_fileExists(target)) return null; // target was uninstalled

        var folder = Path.GetDirectoryName(Path.GetRelativePath(root, file));
        var (category, priority, exclude) = AppClassifier.Classify(title, target, folder);
        var isExplorer = target.EndsWith("\\explorer.exe", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(info.Arguments);
        return new DiscoveredApp
        {
            SourceKey = "lnk:" + KeyFor(root, file),
            Title = title,
            Kind = ChannelKind.App,
            Launch = new LaunchSpec
            {
                ShortcutPath = file,
                Path = string.IsNullOrWhiteSpace(target) ? null : target,
                Arguments = string.IsNullOrWhiteSpace(info.Arguments) ? null : info.Arguments,
                WorkingDirectory = string.IsNullOrWhiteSpace(info.WorkingDirectory) ? null : info.WorkingDirectory,
            },
            IconSource = file,
            Category = category,
            Priority = priority,
            ExcludeFromAutoSeed = exclude || isExplorer,
            Source = Name,
        };
    }

    private DiscoveredApp? FromInternetShortcut(string root, string file)
    {
        var title = Path.GetFileNameWithoutExtension(file);
        string? url = null, iconFile = null;
        foreach (var line in File.ReadLines(file))
        {
            if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase)) url = line[4..].Trim();
            else if (line.StartsWith("IconFile=", StringComparison.OrdinalIgnoreCase)) iconFile = line[9..].Trim();
        }
        if (string.IsNullOrWhiteSpace(url) || AppClassifier.IsNoise(title)) return null;
        if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase) || url.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) return null;

        var icon = !string.IsNullOrWhiteSpace(iconFile) && _fileExists(iconFile) ? iconFile : file;
        var steam = SteamUrl.Match(url);
        if (steam.Success)
        {
            return new DiscoveredApp
            {
                SourceKey = "steam:" + steam.Groups[1].Value,
                Title = title,
                Kind = ChannelKind.Steam,
                Launch = new LaunchSpec { Uri = url },
                IconSource = icon,
                Category = AppCategory.Game,
                Priority = 88,
                Source = Name,
            };
        }

        var epicName = EpicSource.AppNameFromUri(url);
        if (epicName is not null)
        {
            return new DiscoveredApp
            {
                SourceKey = "epic:" + epicName.ToLowerInvariant(),
                Title = title,
                Kind = ChannelKind.Epic,
                Launch = new LaunchSpec { Uri = url },
                IconSource = icon,
                Category = AppCategory.Game,
                Priority = 88,
                Source = Name,
            };
        }

        var (category, priority, exclude) = AppClassifier.Classify(title, null, Path.GetDirectoryName(Path.GetRelativePath(root, file)));
        return new DiscoveredApp
        {
            SourceKey = "url:" + KeyFor(root, file),
            Title = title,
            Kind = ChannelKind.App,
            Launch = new LaunchSpec { ShortcutPath = file, Uri = url },
            IconSource = icon,
            Category = category,
            Priority = priority,
            ExcludeFromAutoSeed = exclude,
            Source = Name,
        };
    }

    private DiscoveredApp? FromClickOnce(string root, string file)
    {
        var title = Path.GetFileNameWithoutExtension(file);
        if (AppClassifier.IsNoise(title)) return null;
        var (category, priority, exclude) = AppClassifier.Classify(title, null, null);
        return new DiscoveredApp
        {
            SourceKey = "clickonce:" + KeyFor(root, file),
            Title = title,
            Kind = ChannelKind.App,
            Launch = new LaunchSpec { ShortcutPath = file },
            IconSource = file,
            Category = category,
            Priority = priority,
            ExcludeFromAutoSeed = exclude,
            Source = Name,
        };
    }
}

/// <summary>Packaged apps (Microsoft Store / MSIX) enumerated from shell:AppsFolder.</summary>
public sealed class AppsFolderSource : IAppSource
{
    private static readonly string[] ExcludedPrefixes =
    {
        "Microsoft.Windows.ShellExperienceHost", "Microsoft.AAD.BrokerPlugin", "Microsoft.Windows.CloudExperienceHost",
        "Microsoft.Windows.StartMenuExperienceHost", "Microsoft.LockApp", "Microsoft.Win32WebViewHost", "MicrosoftWindows.Client",
        "Microsoft.Windows.PeopleExperienceHost", "Microsoft.XboxGameCallableUI", "Microsoft.CredDialogHost", "Microsoft.ECApp",
        "Microsoft.Windows.ParentalControls", "Microsoft.Windows.XGpuEjectDialog", "Microsoft.Windows.CapturePicker",
        "Microsoft.Windows.PinningConfirmationDialog", "Microsoft.Windows.NarratorQuickStart", "Microsoft.Windows.OOBENetwork",
        "MicrosoftWindows.UndockedDevKit", "Microsoft.Windows.Apprep.ChxApp", "Microsoft.Windows.AssignedAccessLockApp",
        "Microsoft.AsyncTextService", "Microsoft.BioEnrollment", "Windows.PrintDialog", "Windows.CBSPreview", "NcsiUwpApp",
        "Microsoft.Windows.CallingShellApp", "Microsoft.WindowsAppRuntime", "Microsoft.MicrosoftEdgeDevToolsClient",
        "c5e2524a-ea46-4f67-841f-6a9465d9d515", "1527c705-839a-4832-9118-54d4Bd6a0c89", "E2A4F912-2574-4A75-9BB0-0D023378592B",
        "F46D4000-FD22-4DB4-AC8E-4E1DDDE828FE", "Microsoft.Windows.SecureAssessmentBrowser", "Microsoft.Windows.ContentDeliveryManager",
        "MicrosoftWindows.LKG", "Microsoft.WidgetsPlatformRuntime", "MicrosoftWindows.CrossDevice", "Microsoft.Windows.DevHome",
    };

    public string Name => "Store apps";

    public IReadOnlyList<DiscoveredApp> Discover(CancellationToken cancellationToken)
    {
        var items = StaRunner.Run(() => ShellItems.EnumerateFolder("shell:AppsFolder"), TimeSpan.FromSeconds(40));
        var apps = new List<DiscoveredApp>();
        foreach (var (name, parsing) in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!parsing.Contains('!') || parsing.Contains('\\') || parsing.Contains('/')) continue;
            if (ExcludedPrefixes.Any(p => parsing.StartsWith(p, StringComparison.OrdinalIgnoreCase))) continue;
            if (AppClassifier.IsNoise(name)) continue;
            var (category, priority, exclude) = AppClassifier.Classify(name, null, null);
            apps.Add(new DiscoveredApp
            {
                SourceKey = "aumid:" + parsing.ToLowerInvariant(),
                Title = name,
                Kind = ChannelKind.StoreApp,
                Launch = new LaunchSpec { Aumid = parsing },
                IconSource = "shell:AppsFolder\\" + parsing,
                Category = category,
                Priority = Math.Max(0, priority - 4),
                ExcludeFromAutoSeed = exclude || priority <= 30,
                Source = Name,
            });
        }
        return apps;
    }
}

/// <summary>Installed Steam games from every Steam library folder.</summary>
public sealed class SteamSource : IAppSource
{
    private static readonly HashSet<string> ExcludedAppIds = new() { "228980", "1070560", "1391110", "1628350", "1493710", "2180100", "961940", "1113280", "1245040", "250820" };
    private readonly string? _steamRoot;

    public SteamSource(string? steamRoot = null)
    {
        _steamRoot = steamRoot;
    }

    public string Name => "Steam";

    public static string? FindSteamRoot()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            if (key?.GetValue("SteamPath") is string p && Directory.Exists(p)) return Path.GetFullPath(p.Replace('/', '\\'));
        }
        catch (Exception ex)
        {
            Log.Warn("Could not read Steam registry key", ex);
        }
        var fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
        return Directory.Exists(fallback) ? fallback : null;
    }

    public IReadOnlyList<DiscoveredApp> Discover(CancellationToken cancellationToken)
    {
        var root = _steamRoot ?? FindSteamRoot();
        if (root is null || !Directory.Exists(root)) return Array.Empty<DiscoveredApp>();

        var libraries = new List<string> { root };
        var vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdf))
        {
            try
            {
                var node = VdfParser.Parse(File.ReadAllText(vdf));
                var folders = node.GetNode("libraryfolders") ?? node.GetNode("LibraryFolders");
                if (folders is not null)
                {
                    foreach (var (key, value) in folders.Values)
                    {
                        if (!key.All(char.IsDigit)) continue;
                        var path = value is VdfNode lib ? lib.GetString("path") : value as string;
                        if (!string.IsNullOrWhiteSpace(path)) libraries.Add(path);
                    }
                }
            }
            catch (FormatException ex)
            {
                Log.Warn("libraryfolders.vdf is malformed", ex);
            }
        }

        var apps = new List<DiscoveredApp>();
        var seen = new HashSet<string>();
        foreach (var library in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var steamapps = Path.Combine(library, "steamapps");
            if (!Directory.Exists(steamapps)) continue;
            foreach (var manifest in Directory.EnumerateFiles(steamapps, "appmanifest_*.acf"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var state = VdfParser.Parse(File.ReadAllText(manifest)).GetNode("AppState");
                    var appId = state?.GetString("appid");
                    var name = state?.GetString("name");
                    var installDir = state?.GetString("installdir");
                    if (appId is null || string.IsNullOrWhiteSpace(name) || ExcludedAppIds.Contains(appId) || !seen.Add(appId)) continue;
                    if (name.Contains("Redistributable", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Proton", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Steam Linux Runtime", StringComparison.OrdinalIgnoreCase) || name.Contains("Steamworks", StringComparison.OrdinalIgnoreCase)) continue;
                    var fullInstall = installDir is null ? null : Path.Combine(steamapps, "common", installDir);
                    if (fullInstall is not null && !Directory.Exists(fullInstall)) continue;

                    apps.Add(new DiscoveredApp
                    {
                        SourceKey = "steam:" + appId,
                        Title = name,
                        Kind = ChannelKind.Steam,
                        Launch = new LaunchSpec { Uri = "steam://rungameid/" + appId },
                        BannerImage = FindBanner(root, appId),
                        IconSource = FindIcon(root, appId),
                        Category = AppCategory.Game,
                        Priority = 88,
                        Source = Name,
                    });
                }
                catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
                {
                    Log.Warn($"Skipping Steam manifest {manifest}", ex);
                }
            }
        }
        return apps;
    }

    internal static string? FindBanner(string steamRoot, string appId)
    {
        var cache = Path.Combine(steamRoot, "appcache", "librarycache");
        var candidates = new[]
        {
            Path.Combine(cache, appId, "header.jpg"),
            Path.Combine(cache, appId + "_header.jpg"),
            Path.Combine(cache, appId, "library_hero.jpg"),
            Path.Combine(cache, appId + "_library_hero.jpg"),
        };
        var found = candidates.FirstOrDefault(File.Exists);
        if (found is not null) return found;
        var folder = Path.Combine(cache, appId);
        if (Directory.Exists(folder))
            return Directory.EnumerateFiles(folder, "*header*.jpg", SearchOption.AllDirectories).FirstOrDefault();
        return null;
    }

    private static string? FindIcon(string steamRoot, string appId)
    {
        var cache = Path.Combine(steamRoot, "appcache", "librarycache");
        var icon = Path.Combine(cache, appId + "_icon.jpg");
        return File.Exists(icon) ? icon : null;
    }
}

/// <summary>Installed Epic Games Store titles from the launcher's manifest files.</summary>
public sealed class EpicSource : IAppSource
{
    private readonly string _manifestDirectory;

    public EpicSource(string? manifestDirectory = null)
    {
        _manifestDirectory = manifestDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests");
    }

    public string Name => "Epic Games";

    public static string? AppNameFromUri(string uri)
    {
        const string prefix = "com.epicgames.launcher://apps/";
        if (!uri.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        var rest = uri[prefix.Length..];
        var q = rest.IndexOf('?');
        if (q >= 0) rest = rest[..q];
        var decoded = Uri.UnescapeDataString(rest);
        var parts = decoded.Split(':', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? null : parts[^1];
    }

    public IReadOnlyList<DiscoveredApp> Discover(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_manifestDirectory)) return Array.Empty<DiscoveredApp>();
        var apps = new List<DiscoveredApp>();
        foreach (var file in Directory.EnumerateFiles(_manifestDirectory, "*.item"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var root = doc.RootElement;
                string? Str(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                if (root.TryGetProperty("bIsIncompleteInstall", out var incomplete) && incomplete.ValueKind == JsonValueKind.True) continue;
                if (root.TryGetProperty("AppCategories", out var cats) && cats.ValueKind == JsonValueKind.Array)
                {
                    var list = cats.EnumerateArray().Select(c => c.GetString() ?? "").ToList();
                    if (!list.Contains("games", StringComparer.OrdinalIgnoreCase)) continue;
                }
                var appName = Str("AppName");
                var display = Str("DisplayName");
                var ns = Str("CatalogNamespace");
                var item = Str("CatalogItemId");
                var location = Str("InstallLocation");
                var exe = Str("LaunchExecutable");
                if (appName is null || string.IsNullOrWhiteSpace(display) || location is null || !Directory.Exists(location)) continue;

                var exePath = exe is null ? null : Path.Combine(location, exe);
                apps.Add(new DiscoveredApp
                {
                    SourceKey = "epic:" + appName.ToLowerInvariant(),
                    Title = display,
                    Kind = ChannelKind.Epic,
                    Launch = new LaunchSpec
                    {
                        Uri = $"com.epicgames.launcher://apps/{Uri.EscapeDataString($"{ns}:{item}:{appName}")}?action=launch&silent=true",
                    },
                    IconSource = exePath is not null && File.Exists(exePath) ? exePath : null,
                    Category = AppCategory.Game,
                    Priority = 88,
                    Source = Name,
                });
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                Log.Warn($"Skipping Epic manifest {file}", ex);
            }
        }
        return apps;
    }
}

/// <summary>Always-available Windows tools, useful in shell mode where there is no taskbar or Start menu.</summary>
public sealed class SystemToolsSource : IAppSource
{
    private readonly string _windows;

    public SystemToolsSource(string? windowsDirectory = null)
    {
        _windows = windowsDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    }

    public string Name => "Windows";

    public IReadOnlyList<DiscoveredApp> Discover(CancellationToken cancellationToken)
    {
        var sys = Path.Combine(_windows, "System32");
        var list = new List<DiscoveredApp>();

        void AddExe(string key, string title, string path, string? args = null, int priority = 12)
        {
            if (!File.Exists(path)) return;
            list.Add(new DiscoveredApp
            {
                SourceKey = "sys:" + key,
                Title = title,
                Kind = ChannelKind.System,
                Launch = new LaunchSpec { Path = path, Arguments = args },
                IconSource = path,
                Category = AppCategory.System,
                Priority = priority,
                ExcludeFromAutoSeed = true,
                Source = Name,
            });
        }

        AddExe("taskmgr", "Task Manager", Path.Combine(sys, "Taskmgr.exe"));
        AddExe("cmd", "Command Prompt", Path.Combine(sys, "cmd.exe"));
        AddExe("powershell", "Windows PowerShell", Path.Combine(sys, "WindowsPowerShell", "v1.0", "powershell.exe"));
        AddExe("control", "Control Panel", Path.Combine(sys, "control.exe"));
        AddExe("explorer", "Windows Desktop", Path.Combine(_windows, "explorer.exe"), null, 5);
        list.Add(new DiscoveredApp
        {
            SourceKey = "sys:settings",
            Title = "Windows Settings",
            Kind = ChannelKind.System,
            Launch = new LaunchSpec { Uri = "ms-settings:" },
            IconSource = "shell:AppsFolder\\windows.immersivecontrolpanel_cw5n1h2txyewy!microsoft.windows.immersivecontrolpanel",
            Category = AppCategory.System,
            Priority = 14,
            ExcludeFromAutoSeed = true,
            Source = Name,
        });
        return list;
    }
}
