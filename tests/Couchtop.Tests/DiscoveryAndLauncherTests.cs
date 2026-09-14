using System.ComponentModel;
using System.Diagnostics;
using Couchtop.Core.Channels;
using Couchtop.Core.Discovery;
using Couchtop.Core.Launching;
using Couchtop.Core.Native;

namespace Couchtop.Tests;

public class VdfTests
{
    [Fact]
    public void Parses_library_folders_new_format()
    {
        const string vdf = """
        "libraryfolders"
        {
            "0"
            {
                "path"		"C:\\Program Files (x86)\\Steam"
                "apps" { "228980" "1234" "440" "5678" }
            }
            "1"
            {
                "path"		"D:\\SteamLibrary"   // second drive
            }
        }
        """;
        var root = VdfParser.Parse(vdf);
        var folders = root.GetNode("libraryfolders")!;
        Assert.Equal(@"C:\Program Files (x86)\Steam", folders.GetNode("0")!.GetString("path"));
        Assert.Equal(@"D:\SteamLibrary", folders.GetNode("1")!.GetString("path"));
        Assert.Equal("1234", folders.GetNode("0")!.GetNode("apps")!.GetString("228980"));
    }

    [Fact]
    public void Handles_escaped_quotes_and_unquoted_tokens()
    {
        var root = VdfParser.Parse("AppState { name \"Say \\\"Hi\\\"\" appid 440 }");
        Assert.Equal("Say \"Hi\"", root.GetNode("AppState")!.GetString("name"));
        Assert.Equal("440", root.GetNode("AppState")!.GetString("appid"));
    }

    [Theory]
    [InlineData("\"a\" {")]
    [InlineData("}")]
    [InlineData("\"unterminated")]
    public void Malformed_input_throws_format_exception(string text)
    {
        Assert.Throws<FormatException>(() => VdfParser.Parse(text));
    }
}

public class DiscoverySourceTests
{
    [Fact]
    public void Steam_source_reads_all_libraries_and_skips_tools()
    {
        using var tmp = new TempDir();
        var steam = Path.Combine(tmp.Path, "Steam");
        var lib2 = Path.Combine(tmp.Path, "Lib2");
        tmp.File(@"Steam\steamapps\libraryfolders.vdf", $$"""
            "libraryfolders" { "0" { "path" "{{steam.Replace("\\", "\\\\")}}" } "1" { "path" "{{lib2.Replace("\\", "\\\\")}}" } }
            """);
        tmp.File(@"Steam\steamapps\appmanifest_440.acf", "\"AppState\" { \"appid\" \"440\" \"name\" \"Team Fortress 2\" \"installdir\" \"TF2\" }");
        Directory.CreateDirectory(Path.Combine(steam, "steamapps", "common", "TF2"));
        tmp.File(@"Steam\steamapps\appmanifest_228980.acf", "\"AppState\" { \"appid\" \"228980\" \"name\" \"Steamworks Common Redistributables\" \"installdir\" \"R\" }");
        tmp.File(@"Lib2\steamapps\appmanifest_620.acf", "\"AppState\" { \"appid\" \"620\" \"name\" \"Portal 2\" \"installdir\" \"Portal 2\" }");
        Directory.CreateDirectory(Path.Combine(lib2, "steamapps", "common", "Portal 2"));
        tmp.File(@"Lib2\steamapps\appmanifest_999.acf", "\"AppState\" { \"appid\" \"999\" \"name\" \"Uninstalled\" \"installdir\" \"Gone\" }");
        tmp.File(@"Lib2\steamapps\appmanifest_998.acf", "{{{ broken");
        tmp.File(@"Steam\appcache\librarycache\620\header.jpg", "x");

        var apps = new SteamSource(steam).Discover(CancellationToken.None);
        Assert.Equal(new[] { "Portal 2", "Team Fortress 2" }, apps.Select(a => a.Title).OrderBy(t => t));
        var portal = apps.Single(a => a.SourceKey == "steam:620");
        Assert.Equal("steam://rungameid/620", portal.Launch.Uri);
        Assert.EndsWith("header.jpg", portal.BannerImage);
    }

    [Fact]
    public void Epic_source_reads_game_manifests_only()
    {
        using var tmp = new TempDir();
        var install = Path.Combine(tmp.Path, "Games", "Fortnite");
        Directory.CreateDirectory(install);
        tmp.File(@"m\a.item", $$"""{ "AppName": "Fortnite", "DisplayName": "Fortnite", "CatalogNamespace": "fn", "CatalogItemId": "abc", "InstallLocation": "{{install.Replace("\\", "\\\\")}}", "LaunchExecutable": "f.exe", "AppCategories": ["public","games"] }""");
        tmp.File(@"m\b.item", $$"""{ "AppName": "UE", "DisplayName": "Unreal Engine", "InstallLocation": "{{install.Replace("\\", "\\\\")}}", "AppCategories": ["engines"] }""");
        tmp.File(@"m\c.item", "not json");

        var apps = new EpicSource(Path.Combine(tmp.Path, "m")).Discover(CancellationToken.None);
        var app = Assert.Single(apps);
        Assert.Equal("epic:fortnite", app.SourceKey);
        Assert.Equal("Fortnite", EpicSource.AppNameFromUri(app.Launch.Uri!));
    }

    [Fact]
    public void Start_menu_source_filters_noise_and_broken_shortcuts()
    {
        using var tmp = new TempDir();
        var root = Path.Combine(tmp.Path, "Programs");
        var exe = tmp.File(@"bin\cool.exe", "MZ");
        ShellLink.Create(Path.Combine(root, "Games", "Cool Game.lnk"), exe, "--fullscreen");
        ShellLink.Create(Path.Combine(root, "Games", "Uninstall Cool Game.lnk"), exe);
        ShellLink.Create(Path.Combine(root, "Broken.lnk"), Path.Combine(tmp.Path, "missing.exe"));
        ShellLink.Create(Path.Combine(root, "Windows Tools", "Registry Thing.lnk"), exe, "--admin");
        tmp.File(@"Programs\Garbage.lnk", "this is not a shortcut");
        tmp.File(@"Programs\Steam\Portal.url", "[InternetShortcut]\r\nURL=steam://rungameid/620\r\n");
        tmp.File(@"Programs\Vendor Website.url", "[InternetShortcut]\r\nURL=https://example.com\r\n");
        var readme = tmp.File(@"bin\readme.txt", "hi");
        ShellLink.Create(Path.Combine(root, "Cool Notes.lnk"), readme);

        var apps = new StartMenuSource(new[] { root }).Discover(CancellationToken.None);

        var cool = apps.Single(a => a.Title == "Cool Game");
        Assert.Equal("lnk:games/cool game", cool.SourceKey);
        Assert.Equal(exe, cool.Launch.Path);
        Assert.Equal("--fullscreen", cool.Launch.Arguments);
        Assert.Contains(apps, a => a.SourceKey == "steam:620" && a.Kind == ChannelKind.Steam);
        Assert.True(apps.Single(a => a.Title == "Registry Thing").ExcludeFromAutoSeed);
        Assert.DoesNotContain(apps, a => a.Title.Contains("Uninstall") || a.Title == "Broken" || a.Title == "Garbage" || a.Title.Contains("Website") || a.Title == "Cool Notes");
    }

    [Theory]
    [InlineData("Uninstall Foo", null, true)]
    [InlineData("Foo Help", null, true)]
    [InlineData("Helper Tool", null, false)]
    [InlineData("Report a problem with Unity", null, true)]
    [InlineData("Foo", @"C:\foo\unins000.exe", true)]
    [InlineData("Foo Docs", @"C:\foo\manual.pdf", true)]
    [InlineData("Spotify", @"C:\spotify.exe", false)]
    public void Noise_detection(string title, string? target, bool expected)
    {
        Assert.Equal(expected, AppClassifier.IsNoise(title, target));
    }

    [Fact]
    public void Classifier_prioritizes_known_categories()
    {
        Assert.Equal(AppCategory.Browser, AppClassifier.Classify("Google Chrome", null, null).Category);
        Assert.Equal(AppCategory.Media, AppClassifier.Classify("Spotify", null, null).Category);
        Assert.True(AppClassifier.Classify("Event Viewer", null, "Windows Tools").ExcludeFromAutoSeed);
        Assert.Equal(AppCategory.Utility, AppClassifier.Classify("Some App", null, null).Category);
    }

    [Fact]
    public void Merge_prefers_richer_sources_and_dedupes_titles_and_targets()
    {
        var apps = new[]
        {
            new DiscoveredApp { SourceKey = "steam:620", Title = "Portal 2", Kind = ChannelKind.Steam, Source = "Start Menu", Launch = new LaunchSpec { Uri = "steam://rungameid/620" } },
            new DiscoveredApp { SourceKey = "steam:620", Title = "Portal 2", Kind = ChannelKind.Steam, Source = "Steam", BannerImage = "banner.jpg", Launch = new LaunchSpec { Uri = "steam://rungameid/620" } },
            new DiscoveredApp { SourceKey = "lnk:a", Title = "Chrome", Kind = ChannelKind.App, Launch = new LaunchSpec { Path = @"C:\chrome.exe" } },
            new DiscoveredApp { SourceKey = "lnk:b", Title = "Google Chrome (2)", Kind = ChannelKind.App, Launch = new LaunchSpec { Path = @"c:\CHROME.exe" } },
            new DiscoveredApp { SourceKey = "aumid:x", Title = "chrome", Kind = ChannelKind.StoreApp, Launch = new LaunchSpec { Aumid = "x!App" } },
            new DiscoveredApp { SourceKey = "lnk:empty", Title = "Empty", Kind = ChannelKind.App, Launch = new LaunchSpec() },
        };
        var merged = DiscoveryService.Merge(apps);
        Assert.Equal(2, merged.Count);
        Assert.Equal("banner.jpg", merged.Single(a => a.SourceKey == "steam:620").BannerImage);
        Assert.Contains(merged, a => a.SourceKey == "lnk:a");
    }

    [Fact]
    public void Couchtop_never_discovers_itself()
    {
        var apps = new[]
        {
            new DiscoveredApp { SourceKey = "lnk:couchtop/couchtop", Title = "Couchtop", Kind = ChannelKind.App, Launch = new LaunchSpec { ShortcutPath = @"C:\SM\Couchtop\Couchtop.lnk" } },
            new DiscoveredApp { SourceKey = "lnk:tools/rescue", Title = "Rescue", Kind = ChannelKind.App, Launch = new LaunchSpec { Path = @"D:\Apps\Couchtop\Couchtop.Recovery.exe" } },
            new DiscoveredApp { SourceKey = "lnk:couch potato", Title = "Couch Potato Game", Kind = ChannelKind.App, Launch = new LaunchSpec { Path = @"C:\Games\couchpotato.exe" } },
        };
        var merged = DiscoveryService.Merge(apps);
        Assert.Equal("Couch Potato Game", Assert.Single(merged).Title);
    }

    [Fact]
    public void Discovery_service_isolates_failing_sources_and_caches()
    {
        using var tmp = new TempDir();
        var cache = Path.Combine(tmp.Path, "cache.json");
        var service = new DiscoveryService(new IAppSource[] { new ThrowingSource(), new FixedSource() }, cache);
        var result = service.Discover();
        Assert.Single(result.Apps);
        Assert.Single(result.Errors);

        var fromCache = new DiscoveryService(Array.Empty<IAppSource>(), cache).LoadCache();
        Assert.NotNull(fromCache);
        Assert.Equal("Fixed", fromCache!.Apps.Single().Title);
    }

    [Fact]
    public void System_tools_are_available_but_not_auto_seeded()
    {
        var apps = new SystemToolsSource().Discover(CancellationToken.None);
        Assert.Contains(apps, a => a.SourceKey == "sys:taskmgr");
        Assert.All(apps, a => Assert.True(a.ExcludeFromAutoSeed));
    }

    private sealed class ThrowingSource : IAppSource
    {
        public string Name => "Broken";
        public IReadOnlyList<DiscoveredApp> Discover(CancellationToken cancellationToken) => throw new InvalidOperationException("boom");
    }

    private sealed class FixedSource : IAppSource
    {
        public string Name => "Fixed";
        public IReadOnlyList<DiscoveredApp> Discover(CancellationToken cancellationToken) =>
            new[] { new DiscoveredApp { SourceKey = "k", Title = "Fixed", Launch = new LaunchSpec { Path = @"C:\f.exe" } } };
    }
}

public class LauncherTests
{
    private sealed class FakeStarter : IProcessStarter
    {
        public List<ProcessStartInfo> Started { get; } = new();
        public List<string> Activated { get; } = new();
        public Exception? Throw { get; set; }

        public int? Start(ProcessStartInfo startInfo)
        {
            if (Throw is not null) throw Throw;
            Started.Add(startInfo);
            return 42;
        }

        public int ActivatePackagedApp(string aumid, string? arguments)
        {
            if (Throw is not null) throw Throw;
            Activated.Add(aumid);
            return 7;
        }
    }

    private sealed class FakeResolver : IChannelResolver
    {
        public LaunchSpec? Result { get; set; }
        public int Calls { get; private set; }

        public LaunchSpec? Resolve(Channel channel)
        {
            Calls++;
            return Result;
        }
    }

    private static Channel Exe(string path, string? args = null) => new()
    {
        Kind = ChannelKind.App,
        Title = "Test App",
        SourceKey = "lnk:test",
        Launch = new LaunchSpec { Path = path, Arguments = args },
    };

    [Fact]
    public void Starts_existing_executable_with_arguments_and_working_directory()
    {
        var starter = new FakeStarter();
        var launcher = new AppLauncher(starter, exists: p => p == @"C:\Apps\app.exe" || p == @"C:\Apps");
        var result = launcher.Launch(Exe(@"C:\Apps\app.exe", "--fast"));
        Assert.True(result.Succeeded);
        var psi = Assert.Single(starter.Started);
        Assert.Equal("--fast", psi.Arguments);
        Assert.True(psi.UseShellExecute);
    }

    [Fact]
    public void Missing_app_without_resolver_reports_not_found()
    {
        var launcher = new AppLauncher(new FakeStarter(), exists: _ => false);
        var result = launcher.Launch(Exe(@"C:\Gone\app.exe"));
        Assert.Equal(LaunchStatus.NotFound, result.Status);
    }

    [Fact]
    public void Updated_app_is_re_resolved_and_spec_is_updated()
    {
        var starter = new FakeStarter();
        var resolver = new FakeResolver { Result = new LaunchSpec { Path = @"C:\Apps\app-2.0\app.exe" } };
        var launcher = new AppLauncher(starter, resolver, p => p.Contains("app-2.0"));
        var channel = Exe(@"C:\Apps\app-1.0\app.exe");
        var result = launcher.Launch(channel);
        Assert.True(result.Succeeded);
        Assert.True(result.LaunchSpecUpdated);
        Assert.Equal(@"C:\Apps\app-2.0\app.exe", channel.Launch!.Path);
        Assert.Equal(1, resolver.Calls);
    }

    [Fact]
    public void Resolver_returning_another_missing_target_is_still_not_found()
    {
        var resolver = new FakeResolver { Result = new LaunchSpec { Path = @"C:\Nope\app.exe" } };
        var launcher = new AppLauncher(new FakeStarter(), resolver, _ => false);
        Assert.Equal(LaunchStatus.NotFound, launcher.Launch(Exe(@"C:\Old\app.exe")).Status);
    }

    [Fact]
    public void Uac_cancel_and_file_not_found_are_classified()
    {
        var starter = new FakeStarter { Throw = new Win32Exception(1223) };
        var launcher = new AppLauncher(starter, exists: _ => true);
        Assert.Equal(LaunchStatus.Cancelled, launcher.Launch(Exe(@"C:\a.exe")).Status);
        starter.Throw = new Win32Exception(2);
        Assert.Equal(LaunchStatus.NotFound, launcher.Launch(Exe(@"C:\a.exe")).Status);
        starter.Throw = new InvalidOperationException("weird");
        Assert.Equal(LaunchStatus.Failed, launcher.Launch(Exe(@"C:\a.exe")).Status);
    }

    [Fact]
    public void Store_apps_uri_channels_and_shortcuts_use_the_right_path()
    {
        var starter = new FakeStarter();
        var launcher = new AppLauncher(starter, exists: p => p.EndsWith(".lnk"));

        Assert.True(launcher.Launch(new Channel { Kind = ChannelKind.StoreApp, Title = "Calc", Launch = new LaunchSpec { Aumid = "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App" } }).Succeeded);
        Assert.Single(starter.Activated);

        Assert.True(launcher.Launch(new Channel { Kind = ChannelKind.Steam, Title = "Portal", Launch = new LaunchSpec { Uri = "steam://rungameid/620" } }).Succeeded);
        Assert.Equal("steam://rungameid/620", starter.Started[^1].FileName);

        Assert.True(launcher.Launch(new Channel { Kind = ChannelKind.App, Title = "Lnk", Launch = new LaunchSpec { ShortcutPath = @"C:\s\app.lnk", Path = @"C:\missing.exe" } }).Succeeded);
        Assert.Equal(@"C:\s\app.lnk", starter.Started[^1].FileName);

        Assert.Equal(LaunchStatus.Failed, launcher.Launch(new Channel { Kind = ChannelKind.Website, Title = "Evil", Launch = new LaunchSpec { Uri = "javascript:alert(1)" } }).Status);
        Assert.Equal(LaunchStatus.Failed, launcher.Launch(BuiltInChannels.Create(BuiltInChannels.Files)).Status);
    }

    [Fact]
    public void Real_launch_through_shortcut_and_executable()
    {
        Assert.True(File.Exists(RepoPaths.FakeApp), "FakeShellApp must be built: " + RepoPaths.FakeApp);
        using var tmp = new TempDir();
        var launcher = new AppLauncher(new SystemProcessStarter());

        var marker1 = Path.Combine(tmp.Path, "exe.txt");
        var r1 = launcher.Launch(Exe(RepoPaths.FakeApp, $"--marker \"{marker1}\""));
        Assert.True(r1.Succeeded, r1.Message);

        var lnk = Path.Combine(tmp.Path, "Fake App.lnk");
        var marker2 = Path.Combine(tmp.Path, "lnk.txt");
        ShellLink.Create(lnk, RepoPaths.FakeApp, $"--marker \"{marker2}\"");
        var r2 = launcher.Launch(new Channel { Kind = ChannelKind.App, Title = "Fake", Launch = new LaunchSpec { ShortcutPath = lnk } });
        Assert.True(r2.Succeeded, r2.Message);

        Assert.True(SpinWait.SpinUntil(() => File.Exists(marker1) && File.Exists(marker2), TimeSpan.FromSeconds(20)));
    }
}
