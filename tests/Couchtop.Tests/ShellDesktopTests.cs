using Couchtop.Core.Settings;
using Couchtop.Core.Platform;
using Couchtop.Core.Search;
using Couchtop.Core.Shell;
using Xunit;

namespace Couchtop.Tests;

public sealed class WindowTrackingTests
{
    private sealed class FakeWindows : IWindowSource
    {
        public List<ManagedWindow> Items { get; } = new();
        public IntPtr Foreground { get; set; }
        public IReadOnlyList<ManagedWindow> List() => Items;
    }

    private static ManagedWindow Window(int handle, string title, string? path = null, bool minimized = false) =>
        new(new IntPtr(handle), title, 100 + handle, path ?? $@"C:\apps\app{handle}.exe", minimized, false);

    [Fact]
    public void Most_recently_used_window_comes_first()
    {
        var source = new FakeWindows();
        source.Items.AddRange(new[] { Window(1, "One"), Window(2, "Two"), Window(3, "Three") });
        var tracker = new WindowTracker(source);
        tracker.Refresh();

        source.Foreground = new IntPtr(3);
        tracker.Refresh();
        Assert.Equal(new IntPtr(3), tracker.Windows[0].Handle);

        source.Foreground = new IntPtr(2);
        tracker.Refresh();
        Assert.Equal(new[] { 2, 3, 1 }, tracker.Windows.Select(w => (int)w.Handle));
    }

    [Fact]
    public void New_windows_join_at_the_back_and_closed_ones_disappear()
    {
        var source = new FakeWindows();
        source.Items.AddRange(new[] { Window(1, "One"), Window(2, "Two") });
        source.Foreground = new IntPtr(1);
        var tracker = new WindowTracker(source);
        tracker.Refresh();

        source.Items.Add(Window(3, "Three"));
        tracker.Refresh();
        Assert.Equal(new[] { 1, 2, 3 }, tracker.Windows.Select(w => (int)w.Handle));

        source.Items.RemoveAll(w => w.Handle == new IntPtr(2));
        tracker.Refresh();
        Assert.Equal(new[] { 1, 3 }, tracker.Windows.Select(w => (int)w.Handle));
    }

    [Fact]
    public void Refresh_reports_changes_only_when_something_moved()
    {
        var source = new FakeWindows();
        source.Items.Add(Window(1, "One"));
        var tracker = new WindowTracker(source);
        Assert.True(tracker.Refresh());
        Assert.False(tracker.Refresh());

        source.Items[0] = Window(1, "One - edited");
        Assert.True(tracker.Refresh());
    }

    [Fact]
    public void Windows_of_one_program_are_grouped_together()
    {
        var source = new FakeWindows();
        source.Items.AddRange(new[]
        {
            Window(1, "Doc 1", @"C:\apps\writer.exe"),
            Window(2, "Notes", @"C:\apps\notes.exe"),
            Window(3, "Doc 2", @"C:\APPS\WRITER.EXE"),
        });
        var tracker = new WindowTracker(source);
        tracker.Refresh();

        Assert.Equal(2, tracker.Groups.Count);
        var writer = tracker.Groups.First(g => g.AppName.Equals("writer", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2, writer.Windows.Count);
        Assert.True(writer.AnyVisible);
    }

    [Fact]
    public void Alt_tab_steps_through_the_used_order_and_wraps()
    {
        var source = new FakeWindows();
        source.Items.AddRange(new[] { Window(1, "One"), Window(2, "Two"), Window(3, "Three") });
        var tracker = new WindowTracker(source);
        tracker.Refresh();

        Assert.Equal(new IntPtr(2), tracker.Step(1)!.Handle);
        Assert.Equal(new IntPtr(3), tracker.Step(2)!.Handle);
        Assert.Equal(new IntPtr(1), tracker.Step(3)!.Handle);
        Assert.Equal(new IntPtr(3), tracker.Step(-1)!.Handle);
    }

    [Fact]
    public void Tracking_survives_a_source_that_throws()
    {
        var tracker = new WindowTracker(new ThrowingSource());
        Assert.False(tracker.Refresh());
        Assert.Empty(tracker.Windows);
    }

    private sealed class ThrowingSource : IWindowSource
    {
        public IntPtr Foreground => throw new InvalidOperationException("no");
        public IReadOnlyList<ManagedWindow> List() => throw new InvalidOperationException("no");
    }

    [Fact]
    public void Windows_without_a_program_path_still_group_apart()
    {
        var a = new ManagedWindow(new IntPtr(1), "A", 10, null, false, false);
        var b = new ManagedWindow(new IntPtr(2), "B", 11, "", false, false);
        Assert.NotEqual(a.AppKey, b.AppKey);
        Assert.Equal("A", a.AppName);
    }
}

public sealed class TrayHostTests
{
    /// <summary>Builds the bytes a program sends to the shell when it adds or changes a tray icon.</summary>
    private static byte[] Build(TrayMessage message, long owner, uint id, uint flags, uint callback, long icon, string tooltip, bool wide, uint state = 0, uint stateMask = 0)
    {
        var pointer = wide ? 8 : 4;
        var size = 8 + 4 + (wide ? 4 : 0) + pointer + 4 + 4 + 4 + (wide ? 4 : 0) + pointer + 256 + 8;
        var data = new byte[size];
        var at = 0;
        void U32(uint value)
        {
            BitConverter.GetBytes(value).CopyTo(data, at);
            at += 4;
        }
        void Ptr(long value)
        {
            if (wide) BitConverter.GetBytes(value).CopyTo(data, at);
            else BitConverter.GetBytes((int)value).CopyTo(data, at);
            at += pointer;
        }

        U32(0);                       // dwHz
        U32((uint)message);
        U32((uint)size);              // cbSize
        if (wide) at += 4;
        Ptr(owner);
        U32(id);
        U32(flags);
        U32(callback);
        if (wide) at += 4;
        Ptr(icon);
        System.Text.Encoding.Unicode.GetBytes(tooltip).CopyTo(data, at);
        at += 256;
        U32(state);
        U32(stateMask);
        return data;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void An_icon_request_is_decoded_from_either_program_layout(bool wide)
    {
        var flags = (uint)(TrayDataReader.NifMessage | TrayDataReader.NifIcon | TrayDataReader.NifTip);
        var data = Build(TrayMessage.Add, 0x4321, 7, flags, 0x8001, 0x99, "Sync client", wide);

        var request = TrayDataReader.Read(data, wide);
        Assert.NotNull(request);
        Assert.Equal(TrayMessage.Add, request!.Message);
        Assert.Equal(new IntPtr(0x4321), request.Item.Owner);
        Assert.Equal(7u, request.Item.Id);
        Assert.Equal(0x8001u, request.Item.CallbackMessage);
        Assert.Equal(new IntPtr(0x99), request.Item.Icon);
        Assert.Equal("Sync client", request.Item.Tooltip);
        Assert.False(request.Item.Hidden);
    }

    [Fact]
    public void Hidden_icons_are_marked_hidden()
    {
        var flags = (uint)(TrayDataReader.NifTip | TrayDataReader.NifState);
        var data = Build(TrayMessage.Modify, 0x10, 1, flags, 0, 0, "Quiet app", wide: true, state: 1, stateMask: 1);
        Assert.True(TrayDataReader.Read(data, true)!.Item.Hidden);
    }

    [Fact]
    public void Garbage_and_stray_messages_are_ignored()
    {
        Assert.Null(TrayDataReader.Read(new byte[4], true));
        var unknown = Build(TrayMessage.Add, 1, 1, 0, 0, 0, "x", wide: true);
        BitConverter.GetBytes(99u).CopyTo(unknown, 4);
        Assert.Null(TrayDataReader.Read(unknown, true));
    }

    [Fact]
    public void Adding_modifying_and_removing_keeps_one_entry_per_icon()
    {
        var items = new List<TrayItem>();
        var add = new TrayRequest(TrayMessage.Add, new TrayItem(new IntPtr(5), 1, "Chat", new IntPtr(70), 0x400, false));
        items = TrayDataReader.Apply(items, add);
        Assert.Single(items);

        // A modify that only carries a new tooltip keeps the icon and callback.
        var modify = new TrayRequest(TrayMessage.Modify, new TrayItem(new IntPtr(5), 1, "Chat - 3 new", IntPtr.Zero, 0, false));
        items = TrayDataReader.Apply(items, modify);
        Assert.Single(items);
        Assert.Equal("Chat - 3 new", items[0].Tooltip);
        Assert.Equal(new IntPtr(70), items[0].Icon);
        Assert.Equal(0x400u, items[0].CallbackMessage);

        items = TrayDataReader.Apply(items, new TrayRequest(TrayMessage.Delete, items[0]));
        Assert.Empty(items);
    }

    [Fact]
    public void Two_programs_can_use_the_same_icon_id()
    {
        var items = new List<TrayItem>();
        items = TrayDataReader.Apply(items, new TrayRequest(TrayMessage.Add, new TrayItem(new IntPtr(1), 1, "A", IntPtr.Zero, 1, false)));
        items = TrayDataReader.Apply(items, new TrayRequest(TrayMessage.Add, new TrayItem(new IntPtr(2), 1, "B", IntPtr.Zero, 1, false)));
        Assert.Equal(2, items.Count);
        Assert.NotEqual(items[0].Key, items[1].Key);
    }
}

public sealed class FullScreenTests
{
    private static readonly MonitorDescriptor Screen = new("\\\\.\\DISPLAY1", 0, 0, 1920, 1080, true, 1.0, 0, 0, 1920, 1040);

    [Theory]
    [InlineData(0, 0, 1920, 1080, true)]      // exactly the screen
    [InlineData(-1, -1, 1921, 1081, true)]    // games often overhang by a pixel
    [InlineData(0, 0, 1920, 1040, false)]     // maximized, taskbar still visible
    [InlineData(100, 100, 900, 700, false)]   // an ordinary window
    public void Only_windows_covering_the_whole_screen_count(int left, int top, int right, int bottom, bool expected) =>
        Assert.Equal(expected, FullScreenCheck.Covers(left, top, right, bottom, Screen));

    [Fact]
    public void A_window_on_a_second_screen_is_measured_against_that_screen()
    {
        var second = new MonitorDescriptor("\\\\.\\DISPLAY2", 1920, 0, 1280, 1024, false, 1.0);
        Assert.True(FullScreenCheck.Covers(1920, 0, 3200, 1024, second));
        Assert.False(FullScreenCheck.Covers(1920, 0, 3200, 1024, Screen));
    }
}

public sealed class DesktopSettingsTests
{
    [Theory]
    [InlineData("always", "always")]
    [InlineData("NEVER", "never")]
    [InlineData("nonsense", "shell")]
    [InlineData(null, "shell")]
    public void The_bar_setting_only_accepts_known_values(string? stored, string expected) =>
        Assert.Equal(expected, new UserSettings { CouchtopBar = stored! }.Normalize().CouchtopBar);

    [Theory]
    [InlineData(1.0, 1.0)]
    [InlineData(1.2, 1.2)]
    [InlineData(9.0, 1.4)]
    [InlineData(0.1, 1.0)]
    [InlineData(double.NaN, 1.0)]
    public void Text_scale_stays_in_a_usable_range(double stored, double expected) =>
        Assert.Equal(expected, new UserSettings { TextScale = stored }.Normalize().TextScale);

    [Theory]
    [InlineData("Ctrl+Alt+Tab", "Ctrl+Alt+Tab")]
    [InlineData("nonsense", "Ctrl+Alt+Tab")]
    [InlineData("", "Ctrl+Alt+Tab")]
    [InlineData("Ctrl+Alt+W", "Ctrl+Alt+W")]
    public void A_broken_shortcut_falls_back_to_the_default(string stored, string expected) =>
        Assert.Equal(expected, new UserSettings { TaskSwitcherHotkey = stored }.Normalize().TaskSwitcherHotkey);

    [Fact]
    public void Blank_restore_values_become_nothing()
    {
        var settings = new UserSettings { LastScreen = "  ", LastFolder = "" }.Normalize();
        Assert.Null(settings.LastScreen);
        Assert.Null(settings.LastFolder);
    }

    [Fact]
    public void High_contrast_is_a_real_theme()
    {
        Assert.True(ThemeCatalog.IsKnown("HighContrast"));
        Assert.Equal("HighContrast", new UserSettings { Theme = "HighContrast" }.Normalize().Theme);
    }
}

public sealed class SystemStatusTests
{
    private const string Connected = """
        There is 1 interface on the system:

            Name                   : Wi-Fi
            Description            : Demo Wireless Adapter
            GUID                   : 1a2b
            State                  : connected
            SSID                   : Home Network
            BSSID                  : 00:11:22:33:44:55
            Signal                 : 82%
            Profile                : Home Network
        """;

    [Fact]
    public void A_connected_adapter_reports_its_network()
    {
        var status = Couchtop.Core.Platform.Wifi.Parse(Connected);
        Assert.True(status.RadioPresent);
        Assert.True(status.Connected);
        Assert.Equal("Home Network", status.Network);
        Assert.Equal(82, status.SignalPercent);
        Assert.Contains("Home Network", status.Describe());
    }

    [Fact]
    public void A_disconnected_adapter_says_so()
    {
        var status = Couchtop.Core.Platform.Wifi.Parse("    Name : Wi-Fi\r\n    State : disconnected\r\n");
        Assert.True(status.RadioPresent);
        Assert.False(status.Connected);
        Assert.Null(status.Network);
        Assert.Equal("Not connected", status.Describe());
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("There is no wireless interface on the system.")]
    public void A_machine_without_wifi_is_not_an_error(string? output)
    {
        var status = Couchtop.Core.Platform.Wifi.Parse(output);
        Assert.False(status.RadioPresent);
        Assert.Equal("No Wi-Fi adapter", status.Describe());
    }

    [Fact]
    public void Battery_text_reads_naturally()
    {
        Assert.Equal("80%  ·  charging", new BatteryStatus(80, true, true, null).Describe());
        Assert.Equal("55%  ·  2 h 5 min left", new BatteryStatus(55, false, false, TimeSpan.FromMinutes(125)).Describe());
        Assert.Equal("Plugged in", new BatteryStatus(null, true, false, null).Describe());
    }
}

public sealed class FuzzySearchTests
{
    [Theory]
    [InlineData("Settings", "settings")]
    [InlineData("Settings", "set")]
    [InlineData("PowerShell", "psh")]
    [InlineData("Couchtop Sports", "sports")]
    [InlineData("Visual Studio Code", "vsc")]
    [InlineData("holiday-photos.png", "photo")]
    public void Sensible_queries_match(string candidate, string query) => Assert.True(Fuzzy.IsMatch(candidate, query));

    [Theory]
    [InlineData("Settings", "xyz")]
    [InlineData("Notepad", "notepadd")]
    [InlineData("", "a")]
    public void Nonsense_queries_do_not_match(string candidate, string query) => Assert.False(Fuzzy.IsMatch(candidate, query));

    [Fact]
    public void Exact_beats_prefix_beats_scattered()
    {
        var exact = Fuzzy.Score("Set", "Set");
        var prefix = Fuzzy.Score("Settings", "Set");
        var scattered = Fuzzy.Score("Sunset", "Set");
        Assert.True(exact > prefix, $"exact {exact} vs prefix {prefix}");
        Assert.True(prefix > scattered, $"prefix {prefix} vs scattered {scattered}");
        Assert.True(scattered >= 0);
    }

    [Fact]
    public void Shorter_names_win_ties()
    {
        Assert.True(Fuzzy.Score("Photos", "photos") > Fuzzy.Score("Photos From Last Summer", "photos"));
    }

    [Fact]
    public void Word_starts_score_above_mid_word_letters()
    {
        Assert.True(Fuzzy.Score("Quick Menu", "qm") > Fuzzy.Score("Aquarium", "qm"));
    }

    [Fact]
    public void An_empty_query_matches_everything()
    {
        Assert.Equal(0, Fuzzy.Score("anything", ""));
        Assert.True(Fuzzy.IsMatch("anything", ""));
    }

    [Fact]
    public void Later_fields_count_for_less_than_the_title()
    {
        var title = Fuzzy.ScoreAny("report", "Report", @"C:\docs\other.txt");
        var subtitle = Fuzzy.ScoreAny("report", "Other", @"C:\docs\report.txt");
        Assert.True(title > subtitle);
        Assert.True(subtitle > 0);
        Assert.Equal(Fuzzy.NoMatch, Fuzzy.ScoreAny("zzz", "Other", @"C:\docs\report.txt"));
    }

    [Fact]
    public void Hits_break_ties_by_kind()
    {
        var channel = new SearchHit(SearchKind.Channel, "Photos", null, "photos", 100);
        var file = new SearchHit(SearchKind.File, "Photos", null, @"C:\photos", 100);
        Assert.True(channel.Rank > file.Rank);
    }
}
