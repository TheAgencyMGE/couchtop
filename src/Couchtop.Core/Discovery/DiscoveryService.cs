using System.Diagnostics;
using Couchtop.Core.Channels;
using Couchtop.Core.Diagnostics;
using Couchtop.Core.Settings;
using Couchtop.Core.Storage;

namespace Couchtop.Core.Discovery;

public sealed record DiscoveryResult(IReadOnlyList<DiscoveredApp> Apps, IReadOnlyList<string> Errors, DateTimeOffset CompletedAt, TimeSpan Duration);

public sealed class DiscoveryCache
{
    public List<DiscoveredApp> Apps { get; set; } = new();
    public DateTimeOffset CompletedAt { get; set; }
}

public sealed class DiscoveryService
{
    private readonly IReadOnlyList<IAppSource> _sources;
    private readonly string? _cacheFile;
    private readonly TimeSpan _sourceTimeout;

    public DiscoveryService(IEnumerable<IAppSource> sources, string? cacheFile, TimeSpan? sourceTimeout = null)
    {
        _sources = sources.ToList();
        _cacheFile = cacheFile;
        _sourceTimeout = sourceTimeout ?? TimeSpan.FromSeconds(45);
    }

    public DiscoveryResult? Latest { get; private set; }

    public static DiscoveryService CreateDefault(AppPaths paths, UserSettings settings)
    {
        var sources = new List<IAppSource>();
        if (settings.DiscoverStartMenu) sources.Add(new StartMenuSource());
        if (settings.DiscoverStoreApps) sources.Add(new AppsFolderSource());
        if (settings.DiscoverSteam) sources.Add(new SteamSource());
        if (settings.DiscoverEpic) sources.Add(new EpicSource());
        sources.Add(new SystemToolsSource());
        return new DiscoveryService(sources, paths.DiscoveryCacheFile);
    }

    public DiscoveryResult? LoadCache()
    {
        if (_cacheFile is null || !File.Exists(_cacheFile)) return null;
        var result = JsonStore.Load(_cacheFile, () => new DiscoveryCache());
        if (result.Source == LoadSource.Default) return null;
        var apps = result.Value.Apps.Where(a => !string.IsNullOrWhiteSpace(a.SourceKey) && a.Launch is { IsEmpty: false }).ToList();
        Latest ??= new DiscoveryResult(apps, Array.Empty<string>(), result.Value.CompletedAt, TimeSpan.Zero);
        return new DiscoveryResult(apps, Array.Empty<string>(), result.Value.CompletedAt, TimeSpan.Zero);
    }

    public DiscoveryResult Discover(CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var errors = new List<string>();
        var tasks = _sources.Select(source => (source, task: Task.Run(() => source.Discover(cancellationToken), cancellationToken))).ToList();
        var collected = new List<DiscoveredApp>();

        foreach (var (source, task) in tasks)
        {
            try
            {
                if (task.Wait(_sourceTimeout, cancellationToken))
                {
                    collected.AddRange(task.Result);
                }
                else
                {
                    errors.Add($"{source.Name}: timed out");
                    Log.Warn($"Discovery source '{source.Name}' timed out");
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                var inner = ex is AggregateException agg ? agg.Flatten().InnerException ?? ex : ex;
                if (inner is OperationCanceledException) throw inner;
                errors.Add($"{source.Name}: {inner.Message}");
                Log.Warn($"Discovery source '{source.Name}' failed", inner);
            }
        }

        var merged = Merge(collected);
        var result = new DiscoveryResult(merged, errors, DateTimeOffset.Now, sw.Elapsed);
        Latest = result;
        Log.Info($"Discovery found {merged.Count} apps in {sw.ElapsedMilliseconds} ms ({errors.Count} source errors)");

        if (_cacheFile is not null)
        {
            try
            {
                JsonStore.Save(_cacheFile, new DiscoveryCache { Apps = merged.ToList(), CompletedAt = result.CompletedAt });
            }
            catch (Exception ex)
            {
                Log.Warn("Could not write discovery cache", ex);
            }
        }
        return result;
    }

    private static int Rank(DiscoveredApp app) => (app.Kind, app.Source) switch
    {
        (ChannelKind.Steam, "Steam") => 5,
        (ChannelKind.Epic, "Epic Games") => 5,
        (ChannelKind.Steam, _) or (ChannelKind.Epic, _) => 4,
        (ChannelKind.App, _) => 3,
        (ChannelKind.StoreApp, _) => 2,
        _ => 1,
    };

    public static IReadOnlyList<DiscoveredApp> Merge(IEnumerable<DiscoveredApp> apps)
    {
        var ordered = apps.Where(a => !string.IsNullOrWhiteSpace(a.SourceKey) && !string.IsNullOrWhiteSpace(a.Title) && a.Launch is { IsEmpty: false } && !AppClassifier.IsSelf(a))
            .OrderByDescending(Rank)
            .ToList();

        var byKey = new Dictionary<string, DiscoveredApp>(StringComparer.OrdinalIgnoreCase);
        var byTarget = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var byTitle = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<DiscoveredApp>();

        foreach (var app in ordered)
        {
            if (byKey.ContainsKey(app.SourceKey)) continue;
            var target = string.IsNullOrWhiteSpace(app.Launch.Path) ? null : app.Launch.Path + "|" + (app.Launch.Arguments ?? "");
            if (target is not null && byTarget.Contains(target)) continue;
            var title = NormalizeTitle(app.Title) + "|" + (app.Kind == ChannelKind.System ? "sys" : "app");
            if (byTitle.Contains(title)) continue;

            byKey[app.SourceKey] = app;
            if (target is not null) byTarget.Add(target);
            byTitle.Add(title);
            result.Add(app);
        }

        return result.OrderByDescending(a => a.Priority).ThenBy(a => a.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    internal static string NormalizeTitle(string title) =>
        new(title.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
