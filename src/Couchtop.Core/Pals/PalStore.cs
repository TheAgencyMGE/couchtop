using Couchtop.Core.Diagnostics;
using Couchtop.Core.Storage;

namespace Couchtop.Core.Pals;

/// <summary>How the Pal fits into Couchtop. All optional; the Pal is company, never in the way.</summary>
public sealed class PalPreferences
{
    public bool ShowOnHome { get; set; } = true;
    public bool Wander { get; set; } = true;
    public bool BarReactions { get; set; } = true;

    /// <summary>The Pal leaves Couchtop and roams the Windows desktop while you use other apps. Off until asked for.</summary>
    public bool DesktopVisits { get; set; }

    /// <summary>On the desktop, the Pal climbs onto app windows rather than only walking along the taskbar.</summary>
    public bool ClimbWindows { get; set; } = true;
    public PalChattiness Chattiness { get; set; } = PalChattiness.Normal;
}

/// <summary>Everything saved in pals.json: the look, the preferences and the Pal's memory.</summary>
public sealed class PalState
{
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Null until the user has made their Pal in Pal Studio.</summary>
    public PalProfile? Profile { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public PalPreferences Preferences { get; set; } = new();
    public PalMemory Memory { get; set; } = new();

    public PalState Normalize()
    {
        Profile = Profile?.Normalize();
        Preferences ??= new();
        if (!Enum.IsDefined(Preferences.Chattiness)) Preferences.Chattiness = PalChattiness.Normal;
        Memory = (Memory ?? new()).Normalize();
        if (Profile is not null && CreatedAt is null) CreatedAt = DateTimeOffset.Now;
        return this;
    }
}

public sealed class PalStore
{
    private readonly string _path;
    private readonly object _gate = new();

    public PalStore(string path)
    {
        _path = path;
        State = JsonStore.Load(path, () => new PalState()).Value.Normalize();
    }

    public PalState State { get; private set; }

    public void Save()
    {
        lock (_gate)
        {
            try
            {
                JsonStore.Save(_path, State);
            }
            catch (Exception ex)
            {
                Log.Warn("Could not save the Pal", ex);
            }
        }
    }
}
