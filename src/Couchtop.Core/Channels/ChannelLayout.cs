using Couchtop.Core.Diagnostics;
using Couchtop.Core.Discovery;
using Couchtop.Core.Storage;

namespace Couchtop.Core.Channels;

public sealed class ChannelLayout
{
    public const int Columns = 4;
    public const int Rows = 3;
    public const int SlotsPerPage = Columns * Rows;
    public const int MaxPages = 60;

    public int SchemaVersion { get; set; } = 1;
    public List<Channel> Channels { get; set; } = new();

    /// <summary>Slot index -> channel id (null = empty slot). Page = slot / 12.</summary>
    public List<string?> Slots { get; set; } = new();

    public List<string> DismissedSourceKeys { get; set; } = new();
    public List<string> KnownSourceKeys { get; set; } = new();

    /// <summary>Built-in channels this layout has already been offered, so new ones appear once and removed ones stay removed.</summary>
    public List<string> KnownBuiltIns { get; set; } = new();

    public bool Seeded { get; set; }
}

/// <summary>Pure layout operations (no I/O) so they are easy to test.</summary>
public static class LayoutEditor
{
    /// <summary>
    /// Adds built-in channels introduced after this layout was created (once each). Built-ins the user has
    /// removed are never re-added. Returns how many channels were placed.
    /// </summary>
    public static int OfferNewBuiltIns(ChannelLayout layout)
    {
        if (!layout.Seeded) return 0;
        layout.KnownBuiltIns ??= new();
        if (layout.KnownBuiltIns.Count == 0) layout.KnownBuiltIns.AddRange(BuiltInChannels.Original);
        var added = 0;
        foreach (var id in BuiltInChannels.All)
        {
            if (layout.KnownBuiltIns.Contains(id)) continue;
            layout.KnownBuiltIns.Add(id);
            var channel = BuiltInChannels.Create(id);
            if (Find(layout, channel.Id) is not null) continue;
            try
            {
                Place(layout, channel);
                added++;
            }
            catch (InvalidOperationException ex)
            {
                Log.Warn("Could not add new built-in channel " + id, ex);
            }
        }
        return added;
    }

    public static Channel? Find(ChannelLayout layout, string? id) =>
        id is null ? null : layout.Channels.FirstOrDefault(c => c.Id == id);

    public static Channel? At(ChannelLayout layout, int slot) =>
        slot >= 0 && slot < layout.Slots.Count ? Find(layout, layout.Slots[slot]) : null;

    public static int SlotOf(ChannelLayout layout, string id) => layout.Slots.IndexOf(id);

    public static int LastOccupiedSlot(ChannelLayout layout)
    {
        for (var i = layout.Slots.Count - 1; i >= 0; i--)
            if (layout.Slots[i] is not null) return i;
        return -1;
    }

    public static int PageCount(ChannelLayout layout, bool includeSparePage = false)
    {
        var used = (LastOccupiedSlot(layout) / ChannelLayout.SlotsPerPage) + 1;
        var pages = Math.Max(1, used);
        if (includeSparePage) pages++;
        return Math.Min(pages, ChannelLayout.MaxPages);
    }

    public static IEnumerable<(int Slot, Channel Channel)> Occupied(ChannelLayout layout)
    {
        for (var i = 0; i < layout.Slots.Count; i++)
        {
            var ch = Find(layout, layout.Slots[i]);
            if (ch is not null) yield return (i, ch);
        }
    }

    public static void EnsureCapacity(ChannelLayout layout, int slotIndex)
    {
        while (layout.Slots.Count <= slotIndex) layout.Slots.Add(null);
    }

    /// <summary>Adds the channel (if new) into the first empty slot at or after <paramref name="preferredSlot"/>.</summary>
    public static int Place(ChannelLayout layout, Channel channel, int preferredSlot = 0)
    {
        if (layout.Channels.All(c => c.Id != channel.Id)) layout.Channels.Add(channel);
        var existing = SlotOf(layout, channel.Id);
        if (existing >= 0) return existing;

        var max = ChannelLayout.SlotsPerPage * ChannelLayout.MaxPages;
        for (var i = Math.Max(0, preferredSlot); i < max; i++)
        {
            EnsureCapacity(layout, i);
            if (layout.Slots[i] is null)
            {
                layout.Slots[i] = channel.Id;
                return i;
            }
        }
        throw new InvalidOperationException("The channel menu is full.");
    }

    /// <summary>Moves a channel; if the target is occupied the two channels swap, like the Menu.</summary>
    public static bool Move(ChannelLayout layout, int from, int to)
    {
        var max = ChannelLayout.SlotsPerPage * ChannelLayout.MaxPages;
        if (from < 0 || to < 0 || from >= max || to >= max || from == to) return false;
        if (from >= layout.Slots.Count || layout.Slots[from] is null) return false;
        EnsureCapacity(layout, to);
        (layout.Slots[from], layout.Slots[to]) = (layout.Slots[to], layout.Slots[from]);
        TrimTrailing(layout);
        return true;
    }

    public static bool Remove(ChannelLayout layout, string id, bool dismissSource = true)
    {
        var channel = Find(layout, id);
        if (channel is null) return false;
        for (var i = 0; i < layout.Slots.Count; i++)
            if (layout.Slots[i] == id) layout.Slots[i] = null;
        layout.Channels.Remove(channel);
        if (dismissSource && !string.IsNullOrEmpty(channel.SourceKey) && !layout.DismissedSourceKeys.Contains(channel.SourceKey))
            layout.DismissedSourceKeys.Add(channel.SourceKey);
        TrimTrailing(layout);
        return true;
    }

    public static bool ContainsSource(ChannelLayout layout, string sourceKey) =>
        layout.Channels.Any(c => string.Equals(c.SourceKey, sourceKey, StringComparison.OrdinalIgnoreCase));

    /// <summary>Repairs a layout loaded from disk. Returns the number of problems fixed.</summary>
    public static int Normalize(ChannelLayout layout)
    {
        var fixes = 0;
        layout.Channels ??= new();
        layout.Slots ??= new();
        layout.DismissedSourceKeys ??= new();
        layout.KnownSourceKeys ??= new();

        var seen = new HashSet<string>();
        var valid = new List<Channel>();
        foreach (var ch in layout.Channels)
        {
            if (ch is null || !ch.IsValid() || !seen.Add(ch.Id))
            {
                fixes++;
                continue;
            }
            if (string.IsNullOrWhiteSpace(ch.Title))
            {
                ch.Title = ch.Kind == ChannelKind.BuiltIn ? BuiltInChannels.DefaultTitle(ch.BuiltInId!) : "Channel";
                fixes++;
            }
            valid.Add(ch);
        }
        layout.Channels = valid;

        var placed = new HashSet<string>();
        var maxSlots = ChannelLayout.SlotsPerPage * ChannelLayout.MaxPages;
        if (layout.Slots.Count > maxSlots)
        {
            layout.Slots.RemoveRange(maxSlots, layout.Slots.Count - maxSlots);
            fixes++;
        }
        for (var i = 0; i < layout.Slots.Count; i++)
        {
            var id = layout.Slots[i];
            if (id is null) continue;
            if (!seen.Contains(id) || !placed.Add(id))
            {
                layout.Slots[i] = null;
                fixes++;
            }
        }

        foreach (var ch in layout.Channels.Where(c => !placed.Contains(c.Id)).ToList())
        {
            try
            {
                Place(layout, ch);
                fixes++;
            }
            catch (InvalidOperationException)
            {
                layout.Channels.Remove(ch);
            }
        }

        layout.DismissedSourceKeys = layout.DismissedSourceKeys.Where(k => !string.IsNullOrWhiteSpace(k)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        layout.KnownSourceKeys = layout.KnownSourceKeys.Where(k => !string.IsNullOrWhiteSpace(k)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        TrimTrailing(layout);
        return fixes;
    }

    private static void TrimTrailing(ChannelLayout layout)
    {
        var last = LastOccupiedSlot(layout);
        var keep = ((last / ChannelLayout.SlotsPerPage) + 1) * ChannelLayout.SlotsPerPage;
        if (last < 0) keep = ChannelLayout.SlotsPerPage;
        if (layout.Slots.Count > keep) layout.Slots.RemoveRange(keep, layout.Slots.Count - keep);
        EnsureCapacity(layout, keep - 1);
    }
}

/// <param name="Added">Newly installed apps placed on the menu.</param>
/// <param name="BuiltInsAdded">New built-in channels (such as Sports) placed for an existing layout.</param>
public sealed record SeedResult(int Added, int NewlyKnown, bool FirstRun, int BuiltInsAdded = 0);

public static class ChannelSeeder
{
    public const int InitialAppLimit = 29;

    public static SeedResult Apply(ChannelLayout layout, IReadOnlyList<DiscoveredApp> apps, bool autoAddNewApps)
    {
        var known = new HashSet<string>(layout.KnownSourceKeys, StringComparer.OrdinalIgnoreCase);
        var dismissed = new HashSet<string>(layout.DismissedSourceKeys, StringComparer.OrdinalIgnoreCase);
        var added = 0;
        var newlyKnown = 0;
        var firstRun = !layout.Seeded;
        var builtInsAdded = 0;

        if (firstRun)
        {
            foreach (var id in BuiltInChannels.All)
            {
                var ch = BuiltInChannels.Create(id);
                if (Find(layout, ch.Id) is null) LayoutEditor.Place(layout, ch);
            }
            layout.KnownBuiltIns = BuiltInChannels.All.ToList();

            var picks = apps
                .Where(a => !a.ExcludeFromAutoSeed && !dismissed.Contains(a.SourceKey) && !LayoutEditor.ContainsSource(layout, a.SourceKey))
                .OrderByDescending(a => a.Priority)
                .ThenBy(a => a.Title, StringComparer.CurrentCultureIgnoreCase)
                .Take(InitialAppLimit);
            foreach (var app in picks)
            {
                LayoutEditor.Place(layout, ToChannel(app));
                added++;
            }
            layout.Seeded = true;
        }
        else
        {
            builtInsAdded = LayoutEditor.OfferNewBuiltIns(layout);
            foreach (var app in apps)
            {
                if (known.Contains(app.SourceKey)) continue;
                if (autoAddNewApps && !app.ExcludeFromAutoSeed && !dismissed.Contains(app.SourceKey) && !LayoutEditor.ContainsSource(layout, app.SourceKey))
                {
                    try
                    {
                        LayoutEditor.Place(layout, ToChannel(app));
                        added++;
                    }
                    catch (InvalidOperationException ex)
                    {
                        Log.Warn("Could not auto-add channel", ex);
                    }
                }
            }
        }

        foreach (var app in apps)
        {
            if (known.Add(app.SourceKey))
            {
                layout.KnownSourceKeys.Add(app.SourceKey);
                newlyKnown++;
            }
        }
        return new SeedResult(added, newlyKnown, firstRun, builtInsAdded);
    }

    private static Channel? Find(ChannelLayout layout, string id) => LayoutEditor.Find(layout, id);

    public static Channel ToChannel(DiscoveredApp app) => new()
    {
        Kind = app.Kind,
        Title = app.Title,
        Launch = app.Launch.Clone(),
        SourceKey = app.SourceKey,
        IconSource = app.IconSource,
        BannerImage = app.BannerImage,
    };
}

public sealed class LayoutService
{
    private readonly string _path;
    private readonly object _gate = new();

    public LayoutService(AppPaths paths)
    {
        _path = paths.LayoutFile;
        var result = JsonStore.Load(_path, () => new ChannelLayout(), l => l.SchemaVersion >= 1);
        Layout = result.Value;
        LoadIssues = result.Issues;
        var fixes = LayoutEditor.Normalize(Layout);
        if (fixes > 0) Log.Warn($"Channel layout repaired ({fixes} fixes).");
    }

    public ChannelLayout Layout { get; }
    public IReadOnlyList<string> LoadIssues { get; }

    public event EventHandler? Changed;

    public void Save(bool raiseChanged = true)
    {
        lock (_gate)
        {
            JsonStore.Save(_path, Layout);
        }
        if (raiseChanged) Changed?.Invoke(this, EventArgs.Empty);
    }
}
