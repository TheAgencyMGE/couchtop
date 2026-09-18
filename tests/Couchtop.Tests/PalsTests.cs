using Couchtop.Core.Channels;
using Couchtop.Core.Pals;

namespace Couchtop.Tests;

public class PalProfileTests
{
    [Fact]
    public void Random_pals_are_repeatable_and_valid()
    {
        var a = PalProfile.Random(42);
        var b = PalProfile.Random(42);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(a), System.Text.Json.JsonSerializer.Serialize(b));
        for (var seed = 0; seed < 200; seed++)
        {
            var pal = PalProfile.Random(seed);
            var json = System.Text.Json.JsonSerializer.Serialize(pal);
            Assert.Equal(json, System.Text.Json.JsonSerializer.Serialize(pal.Clone().Normalize()));
        }
    }

    [Fact]
    public void Damaged_profiles_are_repaired()
    {
        var pal = new PalProfile
        {
            Name = "   ",
            Height = double.NaN,
            Build = 7,
            HairStyle = "made-up",
            TopColor = "blue",
            Skin = "#abc123",
            HairTips = "#zzzzzz",
        }.Normalize();

        Assert.Equal("Pal", pal.Name);
        Assert.Equal(0.5, pal.Height);
        Assert.Equal(1, pal.Build);
        Assert.Equal(PalCatalog.HairStyles[0].Id, pal.HairStyle);
        Assert.Equal("#35B4E5", pal.TopColor);
        Assert.Equal("#ABC123", pal.Skin);
        Assert.Equal("none", pal.HairTips);
    }

    [Fact]
    public void Names_are_trimmed_to_something_that_fits_a_speech_bubble()
    {
        Assert.Equal("Sixteen chars ok", PalProfile.CleanName("  Sixteen chars ok and then some  "));
        Assert.Equal("Tab", PalProfile.CleanName("\tTab\n"));
    }

    [Fact]
    public void Every_catalog_has_unique_ids()
    {
        foreach (var list in new[]
                 {
                     PalCatalog.FaceShapes, PalCatalog.EyeStyles, PalCatalog.BrowStyles, PalCatalog.NoseStyles, PalCatalog.MouthStyles,
                     PalCatalog.CheekStyles, PalCatalog.FacialHairStyles, PalCatalog.HairStyles, PalCatalog.TopStyles, PalCatalog.Patterns,
                     PalCatalog.BottomStyles, PalCatalog.ShoeStyles, PalCatalog.Hats, PalCatalog.GlassesStyles, PalCatalog.Extras,
                     PalCatalog.EarringStyles, PalCatalog.Personalities,
                 })
            Assert.Equal(list.Count, list.Select(o => o.Id).Distinct().Count());
    }
}

public class PalLineTests
{
    [Fact]
    public void There_are_well_over_a_hundred_handwritten_lines_with_unique_ids()
    {
        Assert.True(PalLines.All.Count >= 100, $"only {PalLines.All.Count} lines");
        Assert.Equal(PalLines.All.Count, PalLines.All.Select(l => l.Id).Distinct().Count());
    }

    [Fact]
    public void Every_line_only_uses_known_placeholders_and_fits_a_bubble()
    {
        var known = new[] { "{name}", "{app}", "{theme}", "{hours}", "{minutes}", "{battery}", "{day}", "{count}", "{time}" };
        foreach (var line in PalLines.All)
        {
            var text = known.Aggregate(line.Text, (t, k) => t.Replace(k, ""));
            Assert.DoesNotContain("{", text);
            Assert.DoesNotContain("}", text);
            Assert.True(line.Text.Length <= 72, line.Id + " is too long for a speech bubble");
        }
    }

    [Fact]
    public void Every_topic_has_something_to_say_for_every_personality()
    {
        var context = new PalContext { Now = new DateTimeOffset(2026, 3, 11, 10, 0, 0, TimeSpan.Zero), Theme = "Classic", Channel = "files" };
        foreach (var topic in Enum.GetValues<PalTopic>())
        {
            foreach (var personality in PalCatalog.Personalities)
            {
                var c = context with { Personality = personality.Id };
                Assert.True(PalLines.All.Any(l => l.Topic == topic && l.Fits(c)), $"{topic} has nothing for {personality.Id}");
            }
        }
    }

    [Fact]
    public void Placeholders_are_filled_in()
    {
        var line = new PalLine("x", PalTopic.AppClosedLong, "{hours} hours of {app}, {name}!");
        var text = line.Render(new PalContext { App = "Minecraft", PalName = "Pip", Duration = TimeSpan.FromMinutes(150) });
        Assert.Equal("3 hours of Minecraft, Pip!", text);
    }
}

public class PalDirectorTests
{
    private sealed class Clock
    {
        public DateTimeOffset Now = new(2026, 3, 11, 14, 0, 0, TimeSpan.Zero); // a Wednesday afternoon
        public void Advance(TimeSpan by) => Now += by;
    }

    private static (PalDirector Director, Clock Clock) Create(PalChattiness chattiness = PalChattiness.Normal)
    {
        var clock = new Clock();
        return (new PalDirector(new PalMemory(), () => clock.Now) { Chattiness = chattiness }, clock);
    }

    private static PalEvent Launch(string app, AppCategory category = AppCategory.Unknown) =>
        new(PalEventKind.AppLaunched) { App = app, AppKey = app.ToLowerInvariant(), Category = category };

    [Fact]
    public void Starting_up_says_hello_for_the_time_of_day()
    {
        var (director, _) = Create();
        var reaction = director.Handle(new PalEvent(PalEventKind.Startup));
        Assert.NotNull(reaction?.Text);
        Assert.StartsWith("g-afternoon", reaction!.LineId);
    }

    [Fact]
    public void Holidays_beat_everyday_greetings()
    {
        var (director, clock) = Create();
        clock.Now = new DateTimeOffset(2026, 10, 31, 18, 0, 0, TimeSpan.Zero);
        Assert.Equal("g-halloween", director.Handle(new PalEvent(PalEventKind.Startup))!.LineId);
    }

    [Fact]
    public void Lines_do_not_repeat_until_the_others_have_been_said()
    {
        var (director, clock) = Create(PalChattiness.Chatty);
        var said = new List<string>();
        var apps = new[] { "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight" };
        foreach (var app in apps)
        {
            clock.Advance(TimeSpan.FromMinutes(11));
            var reaction = director.Handle(Launch(app, AppCategory.Game));
            Assert.NotNull(reaction?.LineId);
            said.Add(reaction!.LineId!);
        }
        var gameLines = PalLines.All.Count(l => l.Topic == PalTopic.AppLaunch && l.Fits(new PalContext { Category = AppCategory.Game, Now = clock.Now, Personality = "cheerful" }));
        Assert.Equal(Math.Min(gameLines, said.Count), said.Take(gameLines).Distinct().Count());
    }

    [Fact]
    public void Unprompted_comments_respect_the_quiet_gap()
    {
        var (director, clock) = Create(PalChattiness.Normal);
        Assert.NotNull(director.Handle(Launch("Paint", AppCategory.Art))!.Text);
        clock.Advance(TimeSpan.FromSeconds(30));
        var second = director.Handle(Launch("Calculator", AppCategory.Calculator));
        Assert.Null(second?.Text);
        clock.Advance(TimeSpan.FromMinutes(4));
        Assert.NotNull(director.Handle(Launch("Notepad", AppCategory.Notes))!.Text);
    }

    [Fact]
    public void The_same_app_is_not_commented_on_again_for_hours()
    {
        var (director, clock) = Create(PalChattiness.Chatty);
        Assert.NotNull(director.Handle(Launch("Minecraft", AppCategory.Game))!.Text);
        clock.Advance(TimeSpan.FromMinutes(30));
        Assert.Null(director.Handle(Launch("Minecraft", AppCategory.Game))?.Text);
        clock.Advance(TimeSpan.FromHours(3));
        Assert.NotNull(director.Handle(Launch("Minecraft", AppCategory.Game))!.Text);
    }

    [Fact]
    public void Silent_pals_still_react_but_never_talk()
    {
        var (director, clock) = Create(PalChattiness.Silent);
        Assert.Null(director.Handle(new PalEvent(PalEventKind.Startup))?.Text);
        var poke = director.Handle(new PalEvent(PalEventKind.Poked));
        Assert.NotNull(poke);
        Assert.Null(poke!.Text);
        clock.Advance(TimeSpan.FromHours(1));
        Assert.Null(director.Handle(Launch("Paint", AppCategory.Art))?.Text);
    }

    [Fact]
    public void Quiet_pals_are_capped_per_hour()
    {
        var (director, clock) = Create(PalChattiness.Quiet);
        var spoken = 0;
        for (var i = 0; i < 12; i++)
        {
            clock.Advance(TimeSpan.FromMinutes(5));
            if (director.Handle(Launch("App" + i, (AppCategory)(i % 5 + 1)))?.Text is not null) spoken++;
        }
        Assert.InRange(spoken, 1, 3 + 2); // twelve-minute gap over an hour
    }

    [Fact]
    public void Poking_always_gets_an_answer_and_a_burst_escalates()
    {
        var (director, clock) = Create();
        var first = director.Handle(new PalEvent(PalEventKind.Poked))!;
        Assert.NotNull(first.Text);
        Assert.True(first.Direct);
        string? fifth = null;
        for (var i = 2; i <= 5; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            var r = director.Handle(new PalEvent(PalEventKind.Poked))!;
            if (i < 5) Assert.Null(r.Text);
            else fifth = r.LineId;
        }
        Assert.StartsWith("pokes-", fifth);
    }

    [Fact]
    public void Coming_back_after_an_app_mentions_it()
    {
        var (director, clock) = Create();
        director.Handle(new PalEvent(PalEventKind.Startup));
        clock.Advance(TimeSpan.FromMinutes(40));
        var reaction = director.Handle(new PalEvent(PalEventKind.CouchtopShown) { App = "Minecraft", Duration = TimeSpan.FromMinutes(40), Category = AppCategory.Game });
        Assert.NotNull(reaction?.Text);
        Assert.StartsWith("back-app", reaction!.LineId);
    }

    [Fact]
    public void A_short_hop_away_just_gets_a_wave()
    {
        var (director, clock) = Create();
        director.Handle(new PalEvent(PalEventKind.Startup));
        clock.Advance(TimeSpan.FromMinutes(10));
        var reaction = director.Handle(new PalEvent(PalEventKind.CouchtopShown) { Duration = TimeSpan.FromSeconds(30) });
        Assert.Null(reaction?.Text);
    }

    [Fact]
    public void Low_battery_is_mentioned_but_not_nagged_about()
    {
        var (director, clock) = Create();
        var first = director.Handle(new PalEvent(PalEventKind.Battery) { Battery = 12 });
        Assert.Contains("12%", first!.Text);
        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Null(director.Handle(new PalEvent(PalEventKind.Battery) { Battery = 11 })?.Text);
    }

    [Fact]
    public void Everything_is_deterministic()
    {
        string Run()
        {
            var (director, clock) = Create(PalChattiness.Chatty);
            var log = new List<string?>();
            foreach (var kind in Enum.GetValues<PalEventKind>())
            {
                clock.Advance(TimeSpan.FromMinutes(7));
                log.Add(director.Handle(new PalEvent(kind) { App = "Paint", Category = AppCategory.Art, Theme = "Sakura", Duration = TimeSpan.FromMinutes(80), Battery = 10 })?.LineId);
            }
            return string.Join("|", log);
        }
        Assert.Equal(Run(), Run());
    }
}

public class AppCategoryTests
{
    [Theory]
    [InlineData(@"C:\Program Files\Google\Chrome\Application\chrome.exe", "New Tab - Google Chrome", AppCategory.Browser)]
    [InlineData(@"C:\Program Files (x86)\Steam\steamapps\common\Celeste\Celeste.exe", "Celeste", AppCategory.Game)]
    [InlineData(@"C:\Program Files (x86)\Steam\steam.exe", "Steam", AppCategory.GameStore)]
    [InlineData(@"C:\Users\x\AppData\Local\Programs\Microsoft VS Code\Code.exe", "readme.md - Visual Studio Code", AppCategory.Code)]
    [InlineData(@"C:\Program Files\WindowsApps\Microsoft.Paint\PaintApp\mspaint.exe", "Untitled - Paint", AppCategory.Art)]
    [InlineData(@"C:\Users\x\AppData\Roaming\Spotify\Spotify.exe", "Spotify Premium", AppCategory.Music)]
    [InlineData(@"C:\Windows\System32\notepad.exe", "notes.txt - Notepad", AppCategory.Notes)]
    [InlineData(@"C:\tools\thing.exe", "Something Else", AppCategory.Unknown)]
    public void Programs_are_sorted_into_sensible_kinds(string path, string title, AppCategory expected) =>
        Assert.Equal(expected, AppCategories.Classify(path, title));

    [Fact]
    public void Store_channels_count_as_games() =>
        Assert.Equal(AppCategory.Game, AppCategories.Classify(null, "Anything", knownGame: true));
}

public class PalChannelPlacementTests
{
    [Fact]
    public void Pals_lands_on_a_full_first_page_by_moving_an_app_along()
    {
        var layout = new ChannelLayout { Seeded = true, KnownBuiltIns = BuiltInChannels.All.Where(id => id != BuiltInChannels.Pals).ToList() };
        foreach (var id in BuiltInChannels.All.Where(id => id != BuiltInChannels.Pals)) LayoutEditor.Place(layout, BuiltInChannels.Create(id));
        for (var i = 0; LayoutEditor.LastOccupiedSlot(layout) < ChannelLayout.SlotsPerPage - 1; i++)
            LayoutEditor.Place(layout, new Channel { Kind = ChannelKind.App, Title = "App " + i, Launch = new LaunchSpec { Path = "a.exe" } });
        var lastApp = LayoutEditor.At(layout, ChannelLayout.SlotsPerPage - 1)!;

        Assert.Equal(1, LayoutEditor.OfferNewBuiltIns(layout));

        var slot = LayoutEditor.SlotOf(layout, "builtin-" + BuiltInChannels.Pals);
        Assert.InRange(slot, 0, ChannelLayout.SlotsPerPage - 1);
        Assert.True(LayoutEditor.SlotOf(layout, lastApp.Id) >= ChannelLayout.SlotsPerPage);
        Assert.Equal(0, LayoutEditor.OfferNewBuiltIns(layout));
    }

    [Fact]
    public void Pals_takes_a_free_first_page_slot_when_there_is_one()
    {
        var layout = new ChannelLayout { Seeded = true, KnownBuiltIns = BuiltInChannels.Original.ToList() };
        foreach (var id in BuiltInChannels.Original) LayoutEditor.Place(layout, BuiltInChannels.Create(id));
        LayoutEditor.OfferNewBuiltIns(layout);
        Assert.InRange(LayoutEditor.SlotOf(layout, "builtin-" + BuiltInChannels.Pals), 0, ChannelLayout.SlotsPerPage - 1);
    }
}
