using Couchtop.Core.Channels;
using Couchtop.Core.Discovery;
using Couchtop.Core.Safety;
using Couchtop.Core.Settings;
using Couchtop.Core.Storage;

namespace Couchtop.Tests;

public class StorageTests
{
    [Fact]
    public void Portable_marker_keeps_data_beside_the_exe()
    {
        using var tmp = new TempDir();
        var layout = new InstallLayout(tmp.Path);
        Assert.False(layout.IsPortable);
        Assert.NotEqual(Path.Combine(tmp.Path, AppPaths.PortableDataFolderName), AppPaths.Resolve(null, layout).DataRoot);

        File.WriteAllText(layout.PortableMarkerPath, "portable");
        Assert.True(layout.IsPortable);
        Assert.Equal(Path.Combine(tmp.Path, AppPaths.PortableDataFolderName), AppPaths.Resolve(null, layout).DataRoot);

        var custom = Path.Combine(tmp.Path, "custom");
        Assert.Equal(custom, AppPaths.Resolve(custom, layout).DataRoot);
    }

    [Fact]
    public void Settings_round_trip_persists_values()
    {
        using var tmp = new TempDir();
        var paths = new AppPaths(tmp.Path);
        var service = new SettingsService(paths);
        service.Current.Theme = "Night";
        service.Current.AmbienceVolume = 0.12;
        service.Current.TargetMonitor = @"\\.\DISPLAY2";
        service.Save();

        var reloaded = new SettingsService(paths);
        Assert.Equal("Night", reloaded.Current.Theme);
        Assert.Equal(0.12, reloaded.Current.AmbienceVolume, 3);
        Assert.Equal(@"\\.\DISPLAY2", reloaded.Current.TargetMonitor);
        Assert.Equal(LoadSource.Primary, reloaded.LoadSource);
    }

    [Fact]
    public void Corrupt_settings_fall_back_to_backup_and_quarantine_bad_file()
    {
        using var tmp = new TempDir();
        var paths = new AppPaths(tmp.Path);
        var service = new SettingsService(paths);
        service.Current.Theme = "Night";
        service.Save();
        service.Current.EffectsVolume = 0.25;
        service.Save(); // .bak now holds the first save

        File.WriteAllText(paths.SettingsFile, "{ this is not json");
        var reloaded = new SettingsService(paths);
        Assert.Equal(LoadSource.Backup, reloaded.LoadSource);
        Assert.Equal("Night", reloaded.Current.Theme);
        Assert.NotEmpty(Directory.GetFiles(tmp.Path, "settings.json.corrupt-*"));
        Assert.True(File.Exists(paths.SettingsFile));
    }

    [Fact]
    public void Corrupt_primary_and_backup_yield_defaults()
    {
        using var tmp = new TempDir();
        var paths = new AppPaths(tmp.Path);
        File.WriteAllText(paths.SettingsFile, "\0\0\0\0");
        File.WriteAllText(paths.SettingsFile + ".bak", "[1,2,");
        var service = new SettingsService(paths);
        Assert.Equal(LoadSource.Default, service.LoadSource);
        Assert.Equal("Classic", service.Current.Theme);
        Assert.NotEmpty(service.LoadIssues);
    }

    [Fact]
    public void Out_of_range_values_are_clamped()
    {
        using var tmp = new TempDir();
        var paths = new AppPaths(tmp.Path);
        File.WriteAllText(paths.SettingsFile, """{ "SchemaVersion": 1, "EffectsVolume": 7, "PointerSpeed": -3, "Theme": "Neon", "SearchUrl": "nope", "UnknownField": true }""");
        var s = new SettingsService(paths).Current;
        Assert.Equal(1, s.EffectsVolume);
        Assert.Equal(0.25, s.PointerSpeed);
        Assert.Equal("Classic", s.Theme);
        Assert.Contains("{0}", s.SearchUrl);
        Assert.NotEmpty(s.Bookmarks);
    }

    [Fact]
    public void Atomic_write_leaves_no_temp_file()
    {
        using var tmp = new TempDir();
        var file = Path.Combine(tmp.Path, "a.json");
        AtomicFile.WriteAllText(file, "1");
        AtomicFile.WriteAllText(file, "2");
        Assert.Equal("2", File.ReadAllText(file));
        Assert.Equal("1", File.ReadAllText(file + ".bak"));
        Assert.False(File.Exists(file + ".tmp"));
    }

    [Fact]
    public void Session_sentinel_detects_unclean_exit()
    {
        using var tmp = new TempDir();
        var file = Path.Combine(tmp.Path, "session.json");
        var first = new SessionSentinel(file);
        first.Begin();
        Assert.False(first.PreviousSessionCrashed);

        var second = new SessionSentinel(file); // previous never called End => crash
        second.Begin();
        Assert.True(second.PreviousSessionCrashed);
        second.End(clean: true);

        var third = new SessionSentinel(file);
        third.Begin();
        Assert.False(third.PreviousSessionCrashed);
        Assert.Equal(1, third.Record.CleanExits);
        Assert.Equal(1, third.Record.UncleanExits);
    }
}

public class LayoutTests
{
    private static Channel App(string title, string key) => new()
    {
        Kind = ChannelKind.App,
        Title = title,
        SourceKey = key,
        Launch = new LaunchSpec { Path = @"C:\x\" + title + ".exe" },
    };

    [Fact]
    public void Place_fills_first_empty_slot()
    {
        var layout = new ChannelLayout();
        var a = App("A", "a");
        var b = App("B", "b");
        Assert.Equal(0, LayoutEditor.Place(layout, a));
        Assert.Equal(1, LayoutEditor.Place(layout, b));
        LayoutEditor.Remove(layout, a.Id);
        Assert.Equal(0, LayoutEditor.Place(layout, App("C", "c")));
    }

    [Fact]
    public void Move_to_occupied_slot_swaps_across_pages()
    {
        var layout = new ChannelLayout();
        var a = App("A", "a");
        var b = App("B", "b");
        LayoutEditor.Place(layout, a);
        LayoutEditor.Place(layout, b, 13);
        Assert.Equal(2, LayoutEditor.PageCount(layout));

        Assert.True(LayoutEditor.Move(layout, 0, 13));
        Assert.Equal(b.Id, layout.Slots[0]);
        Assert.Equal(a.Id, layout.Slots[13]);

        Assert.True(LayoutEditor.Move(layout, 13, 30));
        Assert.Equal(3, LayoutEditor.PageCount(layout));
        Assert.Null(layout.Slots[13]);
        Assert.False(LayoutEditor.Move(layout, 5, 6)); // empty source
    }

    [Fact]
    public void Remove_dismisses_source_and_trims_pages()
    {
        var layout = new ChannelLayout();
        var a = App("A", "a");
        LayoutEditor.Place(layout, a, 30);
        Assert.Equal(3, LayoutEditor.PageCount(layout));
        LayoutEditor.Remove(layout, a.Id);
        Assert.Equal(1, LayoutEditor.PageCount(layout));
        Assert.Contains("a", layout.DismissedSourceKeys);
        Assert.Equal(2, LayoutEditor.PageCount(layout, includeSparePage: true));
    }

    [Fact]
    public void Normalize_repairs_damaged_layout()
    {
        var good = App("Good", "g");
        var noLaunch = new Channel { Kind = ChannelKind.App, Title = "Bad" };
        var dup = App("Dup", "d");
        var unknownBuiltIn = new Channel { Kind = ChannelKind.BuiltIn, BuiltInId = "nope", Title = "X" };
        var unplaced = App("Unplaced", "u");
        var layout = new ChannelLayout
        {
            Channels = new List<Channel> { good, noLaunch, dup, new() { Id = dup.Id, Kind = ChannelKind.App, Title = "Dup2", Launch = new LaunchSpec { Path = "x" } }, unknownBuiltIn, unplaced },
            Slots = new List<string?> { good.Id, "ghost", good.Id, dup.Id, noLaunch.Id },
        };

        var fixes = LayoutEditor.Normalize(layout);
        Assert.True(fixes >= 4);
        Assert.Equal(3, layout.Channels.Count);
        Assert.Equal(good.Id, layout.Slots[0]);
        Assert.Equal(1, layout.Slots.Count(s => s == good.Id));
        Assert.Contains(unplaced.Id, layout.Slots);
        Assert.Equal(ChannelLayout.SlotsPerPage, layout.Slots.Count);
    }

    [Fact]
    public void Layout_service_survives_corrupt_file()
    {
        using var tmp = new TempDir();
        var paths = new AppPaths(tmp.Path);
        File.WriteAllText(paths.LayoutFile, "garbage");
        var service = new LayoutService(paths);
        Assert.Empty(service.Layout.Channels);
        Assert.False(service.Layout.Seeded);
    }

    private static DiscoveredApp Disc(string title, string key, int priority, bool exclude = false) => new()
    {
        Title = title,
        SourceKey = key,
        Priority = priority,
        ExcludeFromAutoSeed = exclude,
        Kind = ChannelKind.App,
        Launch = new LaunchSpec { Path = @"C:\apps\" + key + ".exe" },
    };

    [Fact]
    public void First_run_seeds_builtins_then_highest_priority_apps()
    {
        var layout = new ChannelLayout();
        var apps = new List<DiscoveredApp>
        {
            Disc("Low", "low", 10),
            Disc("Game", "steam:1", 88),
            Disc("Tool", "sys:x", 5, exclude: true),
            Disc("Browser", "b", 86),
        };
        var result = ChannelSeeder.Apply(layout, apps, autoAddNewApps: true);

        Assert.True(result.FirstRun);
        Assert.Equal(3, result.Added);
        Assert.Equal(BuiltInChannels.All.Count + 3, layout.Channels.Count);
        Assert.Equal("builtin-files", layout.Slots[0]);
        var firstApp = LayoutEditor.At(layout, BuiltInChannels.All.Count)!;
        Assert.Equal("Game", firstApp.Title);
        Assert.DoesNotContain(layout.Channels, c => c.SourceKey == "sys:x");
        Assert.Equal(4, layout.KnownSourceKeys.Count);
    }

    [Fact]
    public void Builtins_placed_before_first_discovery_do_not_cause_an_app_flood()
    {
        var layout = new ChannelLayout();
        foreach (var id in BuiltInChannels.All) LayoutEditor.Place(layout, BuiltInChannels.Create(id));
        Assert.False(layout.Seeded);

        var apps = Enumerable.Range(0, 120).Select(i => Disc("App " + i, "key" + i, 50)).ToList();
        var result = ChannelSeeder.Apply(layout, apps, autoAddNewApps: true);

        Assert.True(result.FirstRun);
        Assert.Equal(ChannelSeeder.InitialAppLimit, result.Added);
        Assert.Equal(BuiltInChannels.All.Count + ChannelSeeder.InitialAppLimit, layout.Channels.Count);
        Assert.Equal(1, layout.Channels.Count(c => c.BuiltInId == BuiltInChannels.Files));
        Assert.Equal(120, layout.KnownSourceKeys.Count);

        var again = ChannelSeeder.Apply(layout, apps, autoAddNewApps: true);
        Assert.Equal(0, again.Added);
    }

    [Fact]
    public void Later_runs_only_add_new_non_dismissed_apps()
    {
        var layout = new ChannelLayout();
        ChannelSeeder.Apply(layout, new[] { Disc("A", "a", 50) }, true);
        var a = layout.Channels.Single(c => c.SourceKey == "a");
        LayoutEditor.Remove(layout, a.Id);

        var result = ChannelSeeder.Apply(layout, new[] { Disc("A", "a", 50), Disc("New", "new", 40), Disc("Hidden", "hid", 40, true) }, true);
        Assert.False(result.FirstRun);
        Assert.Equal(1, result.Added);
        Assert.Contains(layout.Channels, c => c.SourceKey == "new");
        Assert.DoesNotContain(layout.Channels, c => c.SourceKey == "a");

        var none = ChannelSeeder.Apply(layout, new[] { Disc("Another", "another", 40) }, autoAddNewApps: false);
        Assert.Equal(0, none.Added);
        Assert.Contains("another", layout.KnownSourceKeys);
    }
}
