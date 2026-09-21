using Couchtop.Core.Settings;

namespace Couchtop.Core.Channels;

public enum HomeItemKind
{
    Channel,
    Desktop,
    Board,
    Bookmark,
}

/// <summary>One entry on a home screen: usually a channel, sometimes a shortcut that has no tile of its own.</summary>
public sealed record HomeItem(HomeItemKind Kind, string Title, string? Subtitle = null, Channel? Channel = null, string? Url = null)
{
    public static HomeItem For(Channel channel, string? subtitle = null) => new(HomeItemKind.Channel, channel.Title, subtitle, channel);

    /// <summary>Slot this channel sits in, so screens can open it with the same zoom origin as the Channels menu.</summary>
    public int Slot { get; init; } = -1;
}

/// <summary>A column (Media Bar) or blade (Dashboard) of related items.</summary>
public sealed record HomeCategory(string Id, string Title, IReadOnlyList<HomeItem> Items);

/// <summary>
/// Groups the one shared channel layout into categories for the Dashboard and Media Bar styles. Nothing is
/// duplicated or stored: this is a view over <see cref="ChannelLayout"/>, rebuilt whenever channels change.
/// </summary>
public static class HomeCategories
{
    public const string System = "system";
    public const string Media = "media";
    public const string Apps = "apps";
    public const string Games = "games";
    public const string Web = "web";
    public const string Pals = "pals";

    /// <summary>Category order, left to right.</summary>
    public static readonly IReadOnlyList<string> Order = new[] { System, Media, Apps, Games, Web, Pals };

    public static string TitleOf(string id) => id switch
    {
        System => "System",
        Media => "Media",
        Apps => "Apps",
        Games => "Games",
        Web => "Web",
        Pals => "Pals",
        _ => id,
    };

    /// <summary>
    /// Builds the non-empty categories for a layout. Every channel appears exactly once. Pals belong to the
    /// Channels menu only, so the console shells leave that channel and its category out entirely.
    /// </summary>
    public static IReadOnlyList<HomeCategory> Build(ChannelLayout layout, IReadOnlyList<Bookmark>? bookmarks = null, bool includePals = true)
    {
        var buckets = Order.ToDictionary(id => id, _ => new List<HomeItem>());

        foreach (var (slot, channel) in LayoutEditor.Occupied(layout))
        {
            if (!includePals && channel is { Kind: ChannelKind.BuiltIn, BuiltInId: BuiltInChannels.Pals }) continue;
            var item = HomeItem.For(channel, SubtitleOf(channel)) with { Slot = slot };
            buckets[CategoryOf(channel)].Add(item);
        }

        buckets[System].Add(new HomeItem(HomeItemKind.Desktop, "Windows Desktop", "Leave the menu without closing Couchtop"));
        buckets[includePals ? Pals : System].Add(new HomeItem(HomeItemKind.Board, "Message Board", "Notes and Couchtop messages"));
        foreach (var bookmark in bookmarks ?? Array.Empty<Bookmark>())
            buckets[Web].Add(new HomeItem(HomeItemKind.Bookmark, bookmark.Title, bookmark.Url, Url: bookmark.Url));

        return Order
            .Where(id => buckets[id].Count > 0)
            .Select(id => new HomeCategory(id, TitleOf(id), buckets[id]))
            .ToList();
    }

    public static string CategoryOf(Channel channel) => channel switch
    {
        { Kind: ChannelKind.BuiltIn, BuiltInId: BuiltInChannels.Settings or BuiltInChannels.Power or BuiltInChannels.Customize } => System,
        { Kind: ChannelKind.BuiltIn, BuiltInId: BuiltInChannels.Photos or BuiltInChannels.Files } => Media,
        { Kind: ChannelKind.BuiltIn, BuiltInId: BuiltInChannels.Browser } => Web,
        { Kind: ChannelKind.BuiltIn, BuiltInId: BuiltInChannels.Pals } => Pals,
        { Kind: ChannelKind.BuiltIn, BuiltInId: BuiltInChannels.Sports } => Games,
        { Kind: ChannelKind.Steam or ChannelKind.Epic } => Games,
        { Kind: ChannelKind.Website } => Web,
        _ => Apps,
    };

    private static string? SubtitleOf(Channel channel) => channel.Kind switch
    {
        ChannelKind.BuiltIn => "Couchtop",
        ChannelKind.Steam => "Steam",
        ChannelKind.Epic => "Epic Games",
        ChannelKind.StoreApp => "Microsoft Store app",
        ChannelKind.Website => channel.Launch?.Uri,
        ChannelKind.Folder => "Folder",
        _ => "App",
    };
}
