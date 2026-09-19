using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Couchtop.App.Views;
using Couchtop.Core.Channels;
using Couchtop.Core.Diagnostics;
using Couchtop.Core.Pals;
using Couchtop.Core.Platform;
using Couchtop.Core.Shell;

namespace Couchtop.App.Pals;

/// <summary>Where a reaction should play.</summary>
public enum PalStage
{
    /// <summary>The Couchtop screen in front shows the Pal itself (home, a channel's start screen, Power).</summary>
    Screen,

    /// <summary>Another Couchtop screen is in front: the Pal peeks in from the corner.</summary>
    Peek,

    /// <summary>Another app is in front: the Pal speaks up above the Couchtop Bar.</summary>
    Bar,

    /// <summary>Another app is in front and the Pal is out on the desktop: it reacts right there.</summary>
    Desktop,
}

/// <summary>
/// Connects the user's Pal to the rest of Couchtop: keeps it saved, watches what the user does (launching,
/// closing and switching apps, themes, battery, coming back to Couchtop) and hands the director's reactions
/// to whichever part of Couchtop is on screen.
/// </summary>
public sealed class PalService
{
    private readonly AppHost _host;
    private readonly PalStore _store;
    private readonly PalDirector _director;
    private readonly Dictionary<string, (DateTimeOffset Since, string Name, AppCategory Category)> _open = new();
    private readonly HashSet<string> _gameKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _saveTimer;
    private readonly DispatcherTimer _batteryTimer;
    private MainWindow? _window;
    private bool _primed;
    private bool _started;
    private bool _listening;
    private DateTimeOffset _lastLaunch = DateTimeOffset.MinValue;
    private bool _lastLaunchWasGame;
    private string? _focusKey;
    private DateTimeOffset _focusSince;
    private bool _longSessionReported;
    private DateTimeOffset? _awaySince;
    private (string Name, AppCategory Category)? _awayApp;
    private int _lastBattery = -1;
    private bool _lastCharging;

    public PalService(AppHost host)
    {
        _host = host;
        _store = new PalStore(host.Paths.PalsFile);
        _director = new PalDirector(_store.State.Memory);
        ApplyPreferences();
        _saveTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(4) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            if (!_host.Options.IsSnapshot) _store.Save();
        };
        _batteryTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(90) };
        _batteryTimer.Tick += (_, _) => CheckBattery();
    }

    public PalState State => _store.State;
    public PalProfile? Profile => State.Profile;
    public bool HasPal => State.Profile is not null;
    public PalPreferences Preferences => State.Preferences;

    /// <summary>True while the Pal is out on the Windows desktop (set by the desktop Pal).</summary>
    public bool DesktopShowing { get; set; }

    public PalDesktopBuddy? DesktopBuddy { get; private set; }

    /// <summary>The look changed (or the Pal was made for the first time).</summary>
    public event Action? ProfileChanged;

    /// <summary>The Pal should do something. Hosts check the stage and ignore reactions meant for elsewhere.</summary>
    public event Action<PalReaction, PalStage>? Reacted;

    public event Action? PreferencesChanged;

    // ---------------------------------------------------------------- the Pal itself

    public void SaveProfile(PalProfile profile)
    {
        var first = !HasPal;
        State.Profile = profile.Clone().Normalize();
        if (first) State.CreatedAt = DateTimeOffset.Now;
        ApplyPreferences();
        _store.Save();
        _pendingAnnounce = first ? PalEventKind.ProfileCreated : PalEventKind.ProfileEdited;
        ProfileChanged?.Invoke();
        UpdateListening();
    }

    private PalEventKind? _pendingAnnounce;

    /// <summary>
    /// The Pal says hello (or shows off a new look) once it is back on screen, rather than while the editor is
    /// still closing.
    /// </summary>
    public void AnnounceIfPending()
    {
        if (_pendingAnnounce is not { } kind) return;
        _pendingAnnounce = null;
        Report(new PalEvent(kind));
    }

    /// <summary>Removes the Pal (its memory goes too, so a new one starts fresh).</summary>
    public void RemovePal()
    {
        State.Profile = null;
        State.CreatedAt = null;
        // The director holds this same memory object, so it is cleared in place.
        var memory = State.Memory;
        memory.LineTurns.Clear();
        memory.Cooldowns.Clear();
        memory.Spoke.Clear();
        memory.Turn = 0;
        memory.LastGreeting = null;
        memory.TotalPokes = 0;
        memory.Launches = 0;
        _pendingAnnounce = null;
        _store.Save();
        ProfileChanged?.Invoke();
        UpdateListening();
    }

    public void SavePreferences()
    {
        ApplyPreferences();
        _store.Save();
        UpdateListening();
        PreferencesChanged?.Invoke();
    }

    private void ApplyPreferences()
    {
        _director.Chattiness = Preferences.Chattiness;
        _director.Personality = Profile?.Personality ?? "cheerful";
        _director.PalName = Profile?.Name ?? "Pal";
        _director.PalCreated = State.CreatedAt;
    }

    // ---------------------------------------------------------------- events from Couchtop

    /// <summary>Called once the menu is on screen for the first time.</summary>
    public void Start(MainWindow window)
    {
        if (_started) return;
        _started = true;
        _window = window;
        window.Activated += (_, _) => OnCouchtopActivated();
        window.Deactivated += (_, _) => _awaySince ??= DateTimeOffset.Now;
        if (!_host.Options.IsSnapshot)
        {
            DesktopBuddy = new PalDesktopBuddy(_host, window);
            DesktopBuddy.Update();
        }
        UpdateListening();
        if (!_host.Options.IsSnapshot) _batteryTimer.Start();
        if (HasPal) Report(new PalEvent(PalEventKind.Startup));
    }

    public void Report(PalEvent e)
    {
        if (!HasPal || _host.Options.IsSnapshot && e.Kind != PalEventKind.ProfileCreated) return;
        PalReaction? reaction;
        try
        {
            reaction = _director.Handle(e);
        }
        catch (Exception ex)
        {
            Log.Warn("Pal reaction failed for " + e.Kind, ex);
            return;
        }
        ScheduleSave();
        if (reaction is null) return;
        var stage = StageFor(reaction);
        if (stage is { } s) Reacted?.Invoke(reaction, s);
    }

    /// <summary>A channel was started from the menu.</summary>
    public void ReportLaunch(Channel channel)
    {
        if (channel.Kind == ChannelKind.BuiltIn) return;
        var game = channel.Kind is ChannelKind.Steam or ChannelKind.Epic;
        _lastLaunch = DateTimeOffset.Now;
        _lastLaunchWasGame = game;
        var path = channel.Launch?.Path ?? channel.Launch?.ShortcutPath;
        Report(new PalEvent(PalEventKind.AppLaunched)
        {
            App = channel.Title,
            AppKey = channel.SourceKey ?? channel.Id,
            Category = AppCategories.Classify(path, channel.Title, knownGame: game),
        });
    }

    public void ReportBuiltIn(string builtInId)
    {
        switch (builtInId)
        {
            case BuiltInChannels.Pals:
                return;
            case BuiltInChannels.Customize:
                Report(new PalEvent(PalEventKind.CustomizeOpened));
                return;
            case BuiltInChannels.Power:
                Report(new PalEvent(PalEventKind.PowerOpened));
                return;
            default:
                Report(new PalEvent(PalEventKind.ChannelOpened) { Channel = builtInId });
                return;
        }
    }

    public void ReportTheme(string theme)
    {
        if (!_started) return;
        Report(new PalEvent(PalEventKind.ThemeChanged) { Theme = theme });
    }

    public void ReportHover(Channel channel)
    {
        if (channel.Kind == ChannelKind.BuiltIn) return;
        Report(new PalEvent(PalEventKind.TileHovered)
        {
            App = channel.Title,
            AppKey = channel.SourceKey ?? channel.Id,
            Category = AppCategories.Classify(channel.Launch?.Path ?? channel.Launch?.ShortcutPath, channel.Title, channel.Kind is ChannelKind.Steam or ChannelKind.Epic),
        });
    }

    // ---------------------------------------------------------------- where reactions go

    private PalStage? StageFor(PalReaction reaction)
    {
        if (_window is null) return PalStage.Screen;
        if (_window.IsActive && _window.WindowState != WindowState.Minimized)
        {
            if (!Preferences.ShowOnHome) return null;
            if (_window.CurrentView is IPalHost { ShowsPal: true }) return PalStage.Screen;
            // A peek is a small interruption, so only for something worth saying.
            return reaction.Text is null ? null : PalStage.Peek;
        }
        // Out on the desktop the Pal reacts in person (it's already hidden for full-screen apps).
        if (DesktopShowing) return PalStage.Desktop;
        // Over other apps the Pal only ever speaks up in the bar, never over a full-screen game or video.
        if (reaction.Text is null || !Preferences.BarReactions || _host.Desktop.ForegroundIsFullScreen) return null;
        return PalStage.Bar;
    }

    private void OnCouchtopActivated()
    {
        var away = _awaySince;
        _awaySince = null;
        var app = _awayApp;
        _awayApp = null;
        // Our own popups (search, status) steal focus briefly; only a real trip to another app counts.
        if (away is null || app is null) return;
        var duration = DateTimeOffset.Now - away.Value;
        if (duration < TimeSpan.FromSeconds(8)) return;
        _window?.Dispatcher.BeginInvoke(() => Report(new PalEvent(PalEventKind.CouchtopShown)
        {
            Duration = duration,
            App = app.Value.Name,
            Category = app.Value.Category,
        }), DispatcherPriority.Background);
    }

    // ---------------------------------------------------------------- watching apps

    private void UpdateListening()
    {
        var want = _started && HasPal && !_host.Options.IsSnapshot;
        if (want == _listening) return;
        _listening = want;
        if (want)
        {
            _host.Desktop.Changed += OnDesktopChanged;
            _host.Desktop.AddListener();
        }
        else
        {
            _host.Desktop.Changed -= OnDesktopChanged;
            _host.Desktop.RemoveListener();
            _primed = false;
            _open.Clear();
        }
    }

    private void OnDesktopChanged()
    {
        var now = DateTimeOffset.Now;
        var windows = _host.Desktop.Windows.Windows;
        var groups = windows.GroupBy(w => w.AppKey).ToDictionary(g => g.Key, g => g.First());

        if (!_primed)
        {
            // Whatever was already open when Couchtop started isn't news.
            foreach (var (key, w) in groups) _open[key] = (now, FriendlyName(w), Classify(w));
            _primed = true;
        }
        else
        {
            foreach (var (key, w) in groups)
            {
                if (_open.ContainsKey(key)) continue;
                var recentLaunch = now - _lastLaunch < TimeSpan.FromSeconds(40);
                if (recentLaunch && _lastLaunchWasGame) _gameKeys.Add(key);
                var entry = (now, FriendlyName(w), Classify(w));
                _open[key] = entry;
                // Apps started from a channel were already greeted when they launched.
                if (!recentLaunch)
                    Report(new PalEvent(PalEventKind.AppOpened) { App = entry.Item2, AppKey = key, Category = entry.Item3 });
            }
            foreach (var key in _open.Keys.Where(k => !groups.ContainsKey(k)).ToList())
            {
                var (since, name, category) = _open[key];
                _open.Remove(key);
                Report(new PalEvent(PalEventKind.AppClosed) { App = name, AppKey = key, Category = category, Duration = now - since });
            }
        }

        // What's in front: feeds "welcome back from X", rapid switching and long sessions.
        var foreground = windows.FirstOrDefault(w => w.Handle == _host.Desktop.Windows.Foreground);
        if (foreground is null) return;
        var fkey = foreground.AppKey;
        if (_awaySince is not null) _awayApp = (FriendlyName(foreground), Classify(foreground));
        if (fkey != _focusKey)
        {
            _focusKey = fkey;
            _focusSince = now;
            _longSessionReported = false;
            Report(new PalEvent(PalEventKind.AppFocused) { App = FriendlyName(foreground), AppKey = fkey, Category = Classify(foreground) });
        }
        else if (!_longSessionReported)
        {
            var category = Classify(foreground);
            var limit = category == AppCategory.Game ? TimeSpan.FromMinutes(110) : TimeSpan.FromMinutes(130);
            if (now - _focusSince > limit)
            {
                _longSessionReported = true;
                Report(new PalEvent(PalEventKind.LongSession) { App = FriendlyName(foreground), AppKey = fkey, Category = category, Duration = now - _focusSince });
            }
        }
    }

    private AppCategory Classify(ManagedWindow w) => AppCategories.Classify(w.ProcessPath, w.Title, _gameKeys.Contains(w.AppKey));

    private static readonly Dictionary<string, string?> Descriptions = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>What people call the app: "Paint", not "mspaint".</summary>
    internal static string FriendlyName(ManagedWindow w)
    {
        if (string.IsNullOrEmpty(w.ProcessPath)) return w.Title;
        var file = Path.GetFileNameWithoutExtension(w.ProcessPath);
        if (file.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase)) return w.Title;
        if (!Descriptions.TryGetValue(w.ProcessPath, out var description))
        {
            try
            {
                description = FileVersionInfo.GetVersionInfo(w.ProcessPath).FileDescription?.Trim();
                if (string.IsNullOrEmpty(description) || description.Length > 32 || description.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) description = null;
            }
            catch (Exception ex) when (ex is FileNotFoundException or UnauthorizedAccessException or IOException)
            {
                description = null;
            }
            Descriptions[w.ProcessPath] = description;
        }
        if (description is not null) return description;
        var dash = w.Title.LastIndexOf(" - ", StringComparison.Ordinal);
        var suffix = dash >= 0 ? w.Title[(dash + 3)..].Trim() : "";
        return suffix.Length is > 0 and <= 28 ? suffix : char.ToUpperInvariant(file[0]) + file[1..];
    }

    // ---------------------------------------------------------------- battery

    private void CheckBattery()
    {
        if (!HasPal) return;
        BatteryStatus status;
        try
        {
            status = PowerStatus.Read();
        }
        catch (Exception ex)
        {
            Log.Warn("Battery check failed", ex);
            return;
        }
        if (status.Percent is not { } percent) return;
        if (status.Charging && !_lastCharging && percent < 40)
            Report(new PalEvent(PalEventKind.Battery) { Battery = percent, Charging = true });
        else if (!status.OnMains && percent <= 15 && (_lastBattery < 0 || percent < _lastBattery))
            Report(new PalEvent(PalEventKind.Battery) { Battery = percent });
        _lastBattery = percent;
        _lastCharging = status.Charging;
    }

    private void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>Writes any pending memory straight away (on exit).</summary>
    public void Flush()
    {
        if (_host.Options.IsSnapshot) return;
        _saveTimer.Stop();
        _store.Save();
    }
}
