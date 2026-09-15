using System.Text.Json.Serialization;

namespace Couchtop.Core.Channels;

public enum ChannelKind
{
    BuiltIn,
    App,
    StoreApp,
    Steam,
    Epic,
    Website,
    Folder,
    System,
}

/// <summary>Everything needed to start a channel's real application.</summary>
public sealed class LaunchSpec
{
    public string? Path { get; set; }
    public string? Arguments { get; set; }
    public string? WorkingDirectory { get; set; }
    public string? ShortcutPath { get; set; }
    public string? Aumid { get; set; }
    public string? Uri { get; set; }
    public bool RunAsAdministrator { get; set; }

    [JsonIgnore]
    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Path) && string.IsNullOrWhiteSpace(ShortcutPath) &&
        string.IsNullOrWhiteSpace(Aumid) && string.IsNullOrWhiteSpace(Uri);

    public LaunchSpec Clone() => (LaunchSpec)MemberwiseClone();
}

public sealed class Channel
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public ChannelKind Kind { get; set; }
    public string Title { get; set; } = "";
    public string? BuiltInId { get; set; }
    public LaunchSpec? Launch { get; set; }

    /// <summary>Stable identity from discovery (e.g. "steam:440"), used to re-resolve apps after updates.</summary>
    public string? SourceKey { get; set; }

    /// <summary>Shell parsing name (file path or shell:AppsFolder\AUMID) used to fetch the icon.</summary>
    public string? IconSource { get; set; }

    /// <summary>Wide image used as full-tile artwork (e.g. Steam header art).</summary>
    public string? BannerImage { get; set; }

    /// <summary>User-selected artwork copied into the channel-art folder.</summary>
    public string? CustomArt { get; set; }

    /// <summary>Optional #RRGGBB tile tint.</summary>
    public string? AccentColor { get; set; }

    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.Now;

    public bool IsValid()
    {
        if (string.IsNullOrWhiteSpace(Id)) return false;
        if (Kind == ChannelKind.BuiltIn) return BuiltInChannels.IsKnown(BuiltInId);
        return Launch is { IsEmpty: false };
    }
}

public static class BuiltInChannels
{
    public const string Files = "files";
    public const string Photos = "photos";
    public const string Browser = "browser";
    public const string Settings = "settings";
    public const string Power = "power";
    public const string Customize = "customize";
    public const string Sports = "sports";

    public static readonly IReadOnlyList<string> All = new[] { Files, Photos, Browser, Sports, Customize, Settings, Power };

    /// <summary>The built-ins that existed before per-channel tracking; layouts from then already know them.</summary>
    public static readonly IReadOnlyList<string> Original = new[] { Files, Photos, Browser, Customize, Settings, Power };

    public static bool IsKnown(string? id) => id is not null && All.Contains(id);

    public static string DefaultTitle(string id) => id switch
    {
        Files => "Files",
        Photos => "Photos",
        Browser => "Web",
        Settings => "Settings",
        Power => "Power",
        Customize => "Customize",
        Sports => "Sports",
        _ => id,
    };

    public static Channel Create(string id) => new()
    {
        Id = "builtin-" + id,
        Kind = ChannelKind.BuiltIn,
        BuiltInId = id,
        Title = DefaultTitle(id),
        SourceKey = "builtin:" + id,
    };
}
