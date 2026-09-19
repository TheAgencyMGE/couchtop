using System.Diagnostics;
using System.IO;
using Couchtop.App.Views;
using Couchtop.Core.Channels;
using Couchtop.Core.Diagnostics;
using Couchtop.Core.Platform;
using Couchtop.Core.Search;

namespace Couchtop.App.Services;

/// <summary>One row in the command palette: what to show, how well it matched, and what happens on Enter.</summary>
public sealed record PaletteItem(SearchKind Kind, string Title, string? Subtitle, int Score, Action Run);

/// <summary>
/// Searches everything Couchtop can reach: channels, installed apps, open windows, settings, actions and the
/// user's files. Cheap sources run on every keystroke; the file index is built in the background and reused.
/// </summary>
public sealed class SearchService
{
    private static readonly TimeSpan IndexLife = TimeSpan.FromSeconds(90);
    private readonly AppHost _host;
    private readonly MainWindow _window;
    private readonly object _gate = new();
    private List<FileEntry>? _files;
    private DateTime _indexedAt;

    private readonly record struct FileEntry(string Path, string Name, bool IsFolder);

    public SearchService(AppHost host, MainWindow window)
    {
        _host = host;
        _window = window;
    }

    public async Task<IReadOnlyList<PaletteItem>> QueryAsync(string query, CancellationToken token)
    {
        query = query.Trim();
        var items = new List<PaletteItem>();
        items.AddRange(Channels(query));
        items.AddRange(InstalledApps(query));
        items.AddRange(OpenWindows(query));
        items.AddRange(SettingsPages(query));
        items.AddRange(Actions(query));
        if (query.Length >= 2 && !_host.Options.IsSnapshot) items.AddRange(await FilesAsync(query, token));

        return items
            .Where(i => i.Score >= 0)
            .OrderByDescending(i => i.Score + Bias(i.Kind))
            .ThenBy(i => i.Title.Length)
            .Take(40)
            .ToList();
    }

    /// <summary>What you meant is usually a channel, app or setting, so those outrank a stray file with a similar name.</summary>
    private static int Bias(SearchKind kind) => kind switch
    {
        SearchKind.Channel => 420,
        SearchKind.App => 360,
        SearchKind.Window => 320,
        SearchKind.Setting => 260,
        SearchKind.Action => 220,
        SearchKind.Folder => 60,
        _ => 40,
    };

    // ---------------------------------------------------------------- sources

    private IEnumerable<PaletteItem> Channels(string query)
    {
        foreach (var channel in _host.Layout.Layout.Channels.ToList())
        {
            var score = Fuzzy.ScoreAny(query, channel.Title, channel.BuiltInId);
            if (score < 0) continue;
            var captured = channel;
            yield return new PaletteItem(SearchKind.Channel, channel.Title,
                channel.Kind == ChannelKind.BuiltIn ? "Couchtop channel" : "Your channel", score, () => OpenChannel(captured));
        }
    }

    private IEnumerable<PaletteItem> InstalledApps(string query)
    {
        if (query.Length == 0) yield break;
        var known = new HashSet<string>(_host.Layout.Layout.Channels.Select(c => c.SourceKey ?? "").Where(k => k.Length > 0), StringComparer.OrdinalIgnoreCase);
        var cached = _host.Discovery.LoadCache();
        if (cached is null) yield break;
        foreach (var app in cached.Apps)
        {
            if (known.Contains(app.SourceKey)) continue;
            var score = Fuzzy.Score(app.Title, query);
            if (score < 0) continue;
            var captured = app;
            yield return new PaletteItem(SearchKind.App, app.Title, "Installed app  ·  " + captured.Source, score, () =>
            {
                var channel = new Channel
                {
                    Kind = captured.Kind,
                    Title = captured.Title,
                    Launch = captured.Launch,
                    SourceKey = captured.SourceKey,
                    IconSource = captured.IconSource,
                };
                _ = ChannelActions.LaunchAsync(_window, _host, channel);
            });
        }
    }

    private IEnumerable<PaletteItem> OpenWindows(string query)
    {
        _host.Desktop.Poll();
        foreach (var window in _host.Desktop.Windows.Windows)
        {
            var score = Fuzzy.ScoreAny(query, window.Title, window.AppName);
            if (score < 0) continue;
            var captured = window;
            yield return new PaletteItem(SearchKind.Window, window.Title, "Open window  ·  " + window.AppName, score, () => _host.Desktop.Activate(captured));
        }
    }

    private static readonly (string Title, string Category, string Hint)[] SettingsEntries =
    {
        ("Theme", "Display", "Classic, Night, Sky Resort, Neon City, Midnight, Sakura, Sunset"),
        ("Menu screen and backdrops", "Display", "Which monitor shows Couchtop"),
        ("Pointer and motion", "Display", "Hand pointer, reduce motion, clock format"),
        ("Menu music and sounds", "Sound", "Use your own music and sound effects"),
        ("Volume", "Sound", "Effects, music and Windows volume"),
        ("Controllers and Wii Remotes", "Controls", "Pairing, pointer speed, shortcuts"),
        ("Couchtop Bar and shortcuts", "Controls", "Taskbar, task switcher, command palette"),
        ("Channels and discovery", "Channels", "Steam, Epic, Store apps, auto-add"),
        ("Your Pal", "Pals", "Avatar, character, how often they talk, home screen, bar"),
        ("Shell Mode", "Shell Mode", "Replace the Windows desktop with Couchtop"),
        ("Data and reset", "Data", "Where Couchtop keeps your settings"),
        ("About Couchtop", "About", "Version and licenses"),
    };

    private IEnumerable<PaletteItem> SettingsPages(string query)
    {
        foreach (var (title, category, hint) in SettingsEntries)
        {
            var score = Fuzzy.ScoreAny(query, title, category, hint);
            if (score < 0) continue;
            var captured = category;
            yield return new PaletteItem(SearchKind.Setting, title, "Settings  ·  " + category, score, () =>
            {
                var view = new SettingsView(_host, _window);
                _window.OpenBuiltInView(view);
                view.SelectCategory(captured);
            });
        }
    }

    private IEnumerable<PaletteItem> Actions(string query)
    {
        var actions = new (string Title, string Subtitle, Action Run)[]
        {
            ("Couchtop menu", "Back to the channel menu", () => { _window.GoHome(); _window.BringToFront(); }),
            ("Windows desktop", "Show the normal Windows desktop", _window.ShowWindowsDesktop),
            ("Quick Menu", "Volume, controllers and switching", _window.ToggleHomeMenu),
            ("Switch app", "Show the task switcher", () => _window.OpenTaskSwitcher()),
            ("Customize channels", "Move, rename and add channels", () => { _window.GoHome(); _window.BringToFront(); _window.Menu.SetEditMode(true); }),
            ("Pal Studio", "Change how your Pal looks and acts (avatar, character)", () => { _window.BringToFront(); _window.OpenBuiltIn(BuiltInChannels.Pals); }),
            (_host.Pals.Preferences.DesktopVisits ? "Keep my Pal in Couchtop" : "Take my Pal to the desktop", "Let your Pal roam the Windows desktop and your apps",
                () => { _host.Pals.Preferences.DesktopVisits = !_host.Pals.Preferences.DesktopVisits; _host.Pals.SavePreferences(); }),
            ("Message board", "Couchtop notices", () => _window.OpenBuiltInView(new MessageBoardView(_host, _window))),
            ("Find new apps", "Scan for newly installed programs", () => _host.RefreshChannelsInBackground()),
            ("Minimize all windows", "Clear the screen", () => _host.Desktop.MinimizeAll()),
            ("Lock the PC", "Windows lock screen", PowerActions.Lock),
            ("Sleep", "Rest the PC", PowerActions.Sleep),
            ("Sign out", "End the Windows session", () => _window.OpenBuiltIn(BuiltInChannels.Power)),
            ("Restart", "Restart the PC", () => _window.OpenBuiltIn(BuiltInChannels.Power)),
            ("Shut down", "Turn the PC off", () => _window.OpenBuiltIn(BuiltInChannels.Power)),
            ("Exit Couchtop", "Back to the Windows desktop", () => _window.OpenBuiltIn(BuiltInChannels.Power)),
        };
        foreach (var (title, subtitle, run) in actions)
        {
            var score = Fuzzy.ScoreAny(query, title, subtitle);
            if (score < 0) continue;
            yield return new PaletteItem(SearchKind.Action, title, subtitle, score, run);
        }
    }

    // ---------------------------------------------------------------- files

    private async Task<IReadOnlyList<PaletteItem>> FilesAsync(string query, CancellationToken token)
    {
        List<FileEntry> index;
        try
        {
            index = await Task.Run(BuildIndex, token);
        }
        catch (OperationCanceledException)
        {
            return Array.Empty<PaletteItem>();
        }

        var hits = new List<PaletteItem>();
        foreach (var entry in index)
        {
            if (token.IsCancellationRequested) break;
            var score = Fuzzy.Score(entry.Name, query);
            if (score < 0) continue;
            var captured = entry;
            hits.Add(new PaletteItem(captured.IsFolder ? SearchKind.Folder : SearchKind.File, captured.Name,
                Path.GetDirectoryName(captured.Path), score, () => Open(captured)));
        }
        return hits.OrderByDescending(h => h.Score).Take(14).ToList();
    }

    private void Open(FileEntry entry)
    {
        if (entry.IsFolder)
        {
            _window.OpenBuiltInView(new FilesView(_host, _window, entry.Path));
            return;
        }
        try
        {
            using var _ = Process.Start(new ProcessStartInfo(entry.Path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn("Could not open " + entry.Path, ex);
            _window.ShowToast("Couldn't open that file");
        }
    }

    /// <summary>A shallow index of the user's own folders: enough for "find my file" without a full disk crawl.</summary>
    private List<FileEntry> BuildIndex()
    {
        lock (_gate)
        {
            if (_files is not null && DateTime.UtcNow - _indexedAt < IndexLife) return _files;

            var roots = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
                Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
            };

            var entries = new List<FileEntry>();
            foreach (var root in roots.Where(r => !string.IsNullOrEmpty(r) && Directory.Exists(r)))
            {
                Walk(root, 0, entries);
                if (entries.Count >= 9000) break;
            }
            _files = entries;
            _indexedAt = DateTime.UtcNow;
            return entries;
        }
    }

    /// <summary>Folders full of machinery rather than the user's own documents.</summary>
    private static readonly HashSet<string> SkipFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", "obj", "bin", ".git", ".vs", "AppData", "$RECYCLE.BIN", "Temp", "__pycache__", "venv", ".venv", "packages", "dist", "build",
    };

    private static void Walk(string folder, int depth, List<FileEntry> into)
    {
        if (depth > 3 || into.Count >= 9000) return;
        try
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(folder))
            {
                if (into.Count >= 9000) return;
                var name = Path.GetFileName(path);
                if (name.StartsWith('.') || SkipFolders.Contains(name) || name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                var attributes = File.GetAttributes(path);
                if (attributes.HasFlag(FileAttributes.Hidden) || attributes.HasFlag(FileAttributes.System)) continue;
                var isFolder = attributes.HasFlag(FileAttributes.Directory);
                into.Add(new FileEntry(path, name, isFolder));
                if (isFolder) Walk(path, depth + 1, into);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // Skip folders we cannot read.
        }
    }

    private void OpenChannel(Channel channel)
    {
        _window.BringToFront();
        if (channel.Kind == ChannelKind.BuiltIn && channel.BuiltInId is { } id)
        {
            _window.OpenBuiltIn(id);
            return;
        }
        if (channel.Kind == ChannelKind.Folder && channel.Launch?.Path is { } folder)
        {
            _window.OpenBuiltInView(new FilesView(_host, _window, folder));
            return;
        }
        _ = ChannelActions.LaunchAsync(_window, _host, channel);
    }
}
