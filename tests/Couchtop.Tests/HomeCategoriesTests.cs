using Couchtop.Core.Channels;
using Couchtop.Core.Settings;

namespace Couchtop.Tests;

/// <summary>
/// The Dashboard and Media Bar menu styles are views over the one channel layout. These tests pin down the
/// grouping so a channel can never go missing from a style or turn up in two places at once.
/// </summary>
public class HomeCategoriesTests
{
    private static ChannelLayout LayoutWith(params Channel[] channels)
    {
        var layout = new ChannelLayout();
        foreach (var channel in channels) LayoutEditor.Place(layout, channel);
        return layout;
    }

    private static Channel App(string title, ChannelKind kind = ChannelKind.App) => new()
    {
        Kind = kind,
        Title = title,
        Launch = new LaunchSpec { Path = @"C:\apps\" + title + ".exe" },
    };

    [Fact]
    public void Every_channel_appears_exactly_once()
    {
        var layout = LayoutWith(
            BuiltInChannels.Create(BuiltInChannels.Settings),
            BuiltInChannels.Create(BuiltInChannels.Photos),
            BuiltInChannels.Create(BuiltInChannels.Browser),
            App("Paint"),
            App("Game", ChannelKind.Steam));

        var placed = HomeCategories.Build(layout)
            .SelectMany(c => c.Items)
            .Where(i => i.Channel is not null)
            .Select(i => i.Channel!.Id)
            .ToList();

        Assert.Equal(layout.Channels.Count, placed.Count);
        Assert.Equal(placed.Count, placed.Distinct().Count());
    }

    [Fact]
    public void Channels_land_in_the_category_their_kind_belongs_to()
    {
        Assert.Equal(HomeCategories.Games, HomeCategories.CategoryOf(App("Game", ChannelKind.Steam)));
        Assert.Equal(HomeCategories.Games, HomeCategories.CategoryOf(App("Epic Game", ChannelKind.Epic)));
        Assert.Equal(HomeCategories.Apps, HomeCategories.CategoryOf(App("Paint")));
        Assert.Equal(HomeCategories.Web, HomeCategories.CategoryOf(App("Site", ChannelKind.Website)));
        Assert.Equal(HomeCategories.Games, HomeCategories.CategoryOf(BuiltInChannels.Create(BuiltInChannels.Sports)));
        Assert.Equal(HomeCategories.Media, HomeCategories.CategoryOf(BuiltInChannels.Create(BuiltInChannels.Files)));
        Assert.Equal(HomeCategories.Pals, HomeCategories.CategoryOf(BuiltInChannels.Create(BuiltInChannels.Pals)));
        Assert.Equal(HomeCategories.System, HomeCategories.CategoryOf(BuiltInChannels.Create(BuiltInChannels.Power)));
    }

    [Fact]
    public void Empty_categories_are_left_out_and_the_rest_keep_their_order()
    {
        var layout = LayoutWith(BuiltInChannels.Create(BuiltInChannels.Settings), App("Paint"));
        var ids = HomeCategories.Build(layout).Select(c => c.Id).ToList();

        Assert.DoesNotContain(HomeCategories.Media, ids);
        Assert.DoesNotContain(HomeCategories.Games, ids);
        Assert.Equal(ids.OrderBy(id => HomeCategories.Order.ToList().IndexOf(id)), ids);
    }

    [Fact]
    public void Shortcuts_without_a_channel_are_offered_too()
    {
        var layout = LayoutWith(BuiltInChannels.Create(BuiltInChannels.Browser));
        var categories = HomeCategories.Build(layout, new[] { new Bookmark("Wikipedia", "https://www.wikipedia.org") });

        var web = categories.Single(c => c.Id == HomeCategories.Web);
        Assert.Contains(web.Items, i => i is { Kind: HomeItemKind.Bookmark, Url: "https://www.wikipedia.org" });
        Assert.Contains(categories.SelectMany(c => c.Items), i => i.Kind == HomeItemKind.Desktop);
        Assert.Contains(categories.SelectMany(c => c.Items), i => i.Kind == HomeItemKind.Board);
    }

    [Fact]
    public void Items_remember_the_slot_they_came_from()
    {
        var settings = BuiltInChannels.Create(BuiltInChannels.Settings);
        var layout = LayoutWith(settings);
        var item = HomeCategories.Build(layout).SelectMany(c => c.Items).Single(i => i.Channel?.Id == settings.Id);

        Assert.Equal(LayoutEditor.SlotOf(layout, settings.Id), item.Slot);
    }

    [Fact]
    public void Console_shells_leave_pals_out_completely()
    {
        var layout = LayoutWith(
            BuiltInChannels.Create(BuiltInChannels.Pals),
            BuiltInChannels.Create(BuiltInChannels.Settings),
            App("Paint"));

        var categories = HomeCategories.Build(layout, null, includePals: false);

        Assert.DoesNotContain(categories, c => c.Id == HomeCategories.Pals);
        Assert.DoesNotContain(categories.SelectMany(c => c.Items), i => i.Channel?.BuiltInId == BuiltInChannels.Pals);
        // The message board is still reachable, it just moves to System.
        Assert.Contains(categories.Single(c => c.Id == HomeCategories.System).Items, i => i.Kind == HomeItemKind.Board);
    }

    [Fact]
    public void The_channels_menu_still_has_its_pals()
    {
        var layout = LayoutWith(BuiltInChannels.Create(BuiltInChannels.Pals));
        var categories = HomeCategories.Build(layout);

        Assert.Contains(categories.SelectMany(c => c.Items), i => i.Channel?.BuiltInId == BuiltInChannels.Pals);
    }

    [Fact]
    public void An_empty_layout_still_offers_something_to_do()
    {
        var categories = HomeCategories.Build(new ChannelLayout());
        Assert.NotEmpty(categories.SelectMany(c => c.Items));
    }

    [Fact]
    public void A_fresh_install_has_not_seen_the_tour()
    {
        var settings = new UserSettings().Normalize();

        Assert.Equal(0, settings.TourVersion);
        Assert.False(settings.WelcomeShown);
    }

    [Fact]
    public void The_tour_version_survives_being_saved_and_loaded()
    {
        var settings = new UserSettings { TourVersion = 2, WelcomeShown = true }.Normalize();

        Assert.Equal(2, settings.TourVersion);
        Assert.True(settings.WelcomeShown);
    }

    [Fact]
    public void The_windows_taskbar_hides_for_the_couchtop_bar_by_default()
    {
        var settings = new UserSettings().Normalize();

        Assert.True(settings.AutoHideWindowsTaskbar);
        // Nothing to put back until Couchtop actually hides it.
        Assert.Null(settings.TaskbarStateBeforeHiding);
    }

    [Fact]
    public void A_taskbar_state_left_behind_by_a_killed_run_survives_to_be_healed()
    {
        var settings = new UserSettings { TaskbarStateBeforeHiding = 2 }.Normalize();

        Assert.Equal(2, settings.TaskbarStateBeforeHiding);
    }

    [Fact]
    public void Menu_style_falls_back_to_the_channel_grid_when_the_setting_is_junk()
    {
        Assert.Equal(MenuStyleCatalog.Channels, MenuStyleCatalog.Normalize(null));
        Assert.Equal(MenuStyleCatalog.Channels, MenuStyleCatalog.Normalize("xbox-360"));
        Assert.Equal(MenuStyleCatalog.Dashboard, MenuStyleCatalog.Normalize(MenuStyleCatalog.Dashboard));
        Assert.Equal(MenuStyleCatalog.MediaBar, new UserSettings { MenuStyle = MenuStyleCatalog.MediaBar }.Normalize().MenuStyle);
        Assert.Equal(MenuStyleCatalog.Channels, new UserSettings { MenuStyle = "nope" }.Normalize().MenuStyle);
        Assert.Equal(3, MenuStyleCatalog.All.Count);
        Assert.Equal(MenuStyleCatalog.All.Count, MenuStyleCatalog.All.Select(s => s.Id).Distinct().Count());
    }
}
