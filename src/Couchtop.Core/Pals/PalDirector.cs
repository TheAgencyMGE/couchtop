namespace Couchtop.Core.Pals;

/// <summary>How often a Pal talks on its own. Being poked always gets an answer, unless it's Silent.</summary>
public enum PalChattiness
{
    Chatty,
    Normal,
    Quiet,
    Silent,
}

public enum PalEventKind
{
    Startup,
    CouchtopShown,
    AppLaunched,
    AppOpened,
    AppClosed,
    AppFocused,
    LongSession,
    MenuIdle,
    Poked,
    PickedUp,
    Dropped,
    ThemeChanged,
    ChannelOpened,
    TileHovered,
    CustomizeOpened,
    Battery,
    ProfileCreated,
    ProfileEdited,
    PowerOpened,
    WokeUp,
}

/// <summary>Something that happened that a Pal might react to.</summary>
public sealed record PalEvent(PalEventKind Kind)
{
    public string? App { get; init; }
    public string? AppKey { get; init; }
    public AppCategory Category { get; init; }
    public string? Channel { get; init; }
    public string? Theme { get; init; }
    public TimeSpan Duration { get; init; }
    public int Battery { get; init; } = -1;
    public bool Charging { get; init; }
}

/// <summary>What the Pal does: always a gesture and face, and sometimes something to say.</summary>
public sealed record PalReaction(PalGesture Gesture, PalMood Mood, string? Text = null, string? LineId = null, bool Direct = false);

/// <summary>What a Pal remembers between sessions, so it doesn't repeat itself or nag.</summary>
public sealed class PalMemory
{
    /// <summary>Line id -> the "turn" it was last said on. Lower means said longer ago.</summary>
    public Dictionary<string, long> LineTurns { get; set; } = new();
    public long Turn { get; set; }

    /// <summary>Cooldown key -> when it last fired (e.g. "launch:steam:440", "topic:Bored").</summary>
    public Dictionary<string, DateTimeOffset> Cooldowns { get; set; } = new();

    /// <summary>When the Pal last spoke unprompted, for the per-hour cap.</summary>
    public List<DateTimeOffset> Spoke { get; set; } = new();

    public DateTimeOffset? LastGreeting { get; set; }
    public DateTimeOffset? LastSeen { get; set; }
    public int TotalPokes { get; set; }
    public int Launches { get; set; }

    public PalMemory Normalize()
    {
        LineTurns ??= new();
        Cooldowns ??= new();
        Spoke ??= new();
        if (LineTurns.Count > 500) LineTurns = LineTurns.OrderByDescending(kv => kv.Value).Take(400).ToDictionary(kv => kv.Key, kv => kv.Value);
        if (Cooldowns.Count > 400) Cooldowns = Cooldowns.OrderByDescending(kv => kv.Value).Take(300).ToDictionary(kv => kv.Key, kv => kv.Value);
        if (Spoke.Count > 40) Spoke = Spoke.OrderByDescending(t => t).Take(40).ToList();
        return this;
    }
}

/// <summary>
/// The Pal's judgment: given something that happened, decide whether to react, how, and with which line.
/// Fully deterministic (the same memory, clock and event always give the same answer) and deliberately
/// restrained, so the Pal feels like company rather than a notification feed.
/// </summary>
public sealed class PalDirector
{
    private readonly PalMemory _memory;
    private readonly Func<DateTimeOffset> _clock;
    private readonly List<DateTimeOffset> _pokes = new();
    private readonly List<(DateTimeOffset At, string Key)> _focus = new();
    private DateTimeOffset _lastGesture = DateTimeOffset.MinValue;

    public PalDirector(PalMemory memory, Func<DateTimeOffset>? clock = null)
    {
        _memory = memory.Normalize();
        _clock = clock ?? (() => DateTimeOffset.Now);
    }

    public PalChattiness Chattiness { get; set; } = PalChattiness.Normal;
    public string Personality { get; set; } = "cheerful";
    public string PalName { get; set; } = "Pal";
    public DateTimeOffset? PalCreated { get; set; }
    public PalMemory Memory => _memory;

    /// <summary>Minimum quiet time between unprompted comments.</summary>
    public TimeSpan Gap => Chattiness switch
    {
        PalChattiness.Chatty => TimeSpan.FromSeconds(50),
        PalChattiness.Quiet => TimeSpan.FromMinutes(12),
        PalChattiness.Silent => TimeSpan.MaxValue,
        _ => TimeSpan.FromMinutes(3),
    };

    /// <summary>Most unprompted comments in any hour.</summary>
    public int HourlyCap => Chattiness switch
    {
        PalChattiness.Chatty => 20,
        PalChattiness.Quiet => 3,
        PalChattiness.Silent => 0,
        _ => 8,
    };

    public PalReaction? Handle(PalEvent e)
    {
        var now = _clock();
        var context = new PalContext
        {
            Now = now,
            Personality = Personality,
            PalName = PalName,
            App = e.App,
            Category = e.Category,
            Channel = e.Channel,
            Theme = e.Theme,
            Duration = e.Duration,
            Battery = e.Battery,
            PalCreated = PalCreated,
        };

        switch (e.Kind)
        {
            // ------------------------------------------------ things done to the Pal: always answered
            case PalEventKind.Poked:
            {
                _memory.TotalPokes++;
                _pokes.Add(now);
                _pokes.RemoveAll(t => now - t > TimeSpan.FromSeconds(20));
                var count = _pokes.Count;
                var topic = count >= 5 ? PalTopic.PokedLots : PalTopic.Poked;
                // A burst of pokes gets a spoken answer on the first, fifth and every fifth after; the rest are just gestures.
                var speak = count == 1 || count % 5 == 0;
                return Direct(topic, context with { Count = count }, speak);
            }
            case PalEventKind.PickedUp:
                return Direct(PalTopic.PickedUp, context, speak: Due("direct:pickup", now, TimeSpan.FromSeconds(40)));
            case PalEventKind.Dropped:
                return Direct(PalTopic.Dropped, context, speak: Due("direct:drop", now, TimeSpan.FromSeconds(40)));
            case PalEventKind.ProfileCreated:
                return Direct(PalTopic.FirstMeet, context, speak: true);
            case PalEventKind.ProfileEdited:
                return Direct(PalTopic.NewLook, context, speak: true);
            case PalEventKind.PowerOpened:
                return Direct(PalTopic.Goodbye, context, speak: Due("direct:bye", now, TimeSpan.FromMinutes(2)));
            case PalEventKind.WokeUp:
                return Direct(PalTopic.WakeUp, context, speak: Due("topic:wake", now, TimeSpan.FromMinutes(5)));
            case PalEventKind.ThemeChanged:
                // The user just did something on purpose, so an answer is welcome, but not for every click while browsing themes.
                return Direct(PalTopic.ThemeChanged, context, speak: Due("topic:theme", now, TimeSpan.FromSeconds(8)));

            // ------------------------------------------------ the world around the Pal: answered sparingly
            case PalEventKind.Startup:
            {
                _memory.LastSeen = now;
                if (_memory.LastGreeting is { } last && now - last < TimeSpan.FromHours(3) && last.Date == now.Date)
                    return Gesture(PalGesture.Wave, PalMood.Happy, now);
                _memory.LastGreeting = now;
                return Ambient(PalTopic.Greeting, context, now, bypassGap: true) ?? Gesture(PalGesture.Wave, PalMood.Happy, now);
            }
            case PalEventKind.CouchtopShown:
            {
                // A new day (or a long break) deserves a proper greeting instead of "welcome back".
                if (_memory.LastGreeting is not { } greeted || greeted.Date != now.Date || now - greeted > TimeSpan.FromHours(6))
                {
                    _memory.LastGreeting = now;
                    return Ambient(PalTopic.Greeting, context, now, bypassGap: true) ?? Gesture(PalGesture.Wave, PalMood.Happy, now);
                }
                if (e.Duration < TimeSpan.FromMinutes(2)) return Gesture(PalGesture.Wave, PalMood.Happy, now);
                var topic = e.Duration > TimeSpan.FromHours(2) ? PalTopic.WelcomeBackLong
                    : e.App is not null ? PalTopic.WelcomeBackFromApp
                    : PalTopic.WelcomeBack;
                if (!Due("topic:" + topic, now, TimeSpan.FromMinutes(20), consume: false)) return Gesture(PalGesture.Wave, PalMood.Happy, now);
                var reaction = Ambient(topic, context, now);
                if (reaction is not null) Mark("topic:" + topic, now);
                return reaction ?? Gesture(PalGesture.Wave, PalMood.Happy, now);
            }
            case PalEventKind.AppLaunched:
            case PalEventKind.AppOpened:
            {
                _memory.Launches++;
                var key = "launch:" + (e.AppKey ?? e.App ?? "?").ToLowerInvariant();
                // The same app is only commented on every few hours, and the same kind of app every ten minutes.
                if (Due(key, now, TimeSpan.FromHours(3), consume: false) && Due("launchcat:" + e.Category, now, TimeSpan.FromMinutes(10), consume: false))
                {
                    var reaction = Ambient(PalTopic.AppLaunch, context, now);
                    if (reaction is not null)
                    {
                        Mark(key, now);
                        Mark("launchcat:" + e.Category, now);
                        return reaction;
                    }
                }
                return Gesture(e.Category == AppCategory.Game ? PalGesture.Cheer : PalGesture.Wave, PalMood.Happy, now);
            }
            case PalEventKind.AppClosed:
            {
                if (e.Duration < TimeSpan.FromSeconds(45) && e.Duration > TimeSpan.Zero)
                    return Once("topic:closed-quick", TimeSpan.FromMinutes(30), PalTopic.AppClosedQuick, context, now);
                if (e.Duration > TimeSpan.FromMinutes(75))
                    return Once("closed-long:" + (e.AppKey ?? e.App), TimeSpan.FromHours(6), PalTopic.AppClosedLong, context, now);
                return null;
            }
            case PalEventKind.AppFocused:
            {
                _focus.Add((now, e.AppKey ?? e.App ?? ""));
                _focus.RemoveAll(f => now - f.At > TimeSpan.FromSeconds(25));
                var distinct = _focus.Select(f => f.Key).Distinct().Count();
                if (_focus.Count >= 7 && distinct >= 3)
                {
                    _focus.Clear();
                    return Once("topic:switching", TimeSpan.FromMinutes(45), PalTopic.RapidSwitching, context, now);
                }
                return null;
            }
            case PalEventKind.LongSession:
                return Once("session:" + (e.AppKey ?? e.App), TimeSpan.FromHours(2), PalTopic.LongSession, context, now);
            case PalEventKind.MenuIdle:
                if (e.Duration >= TimeSpan.FromMinutes(6))
                    return Once("topic:sleep", TimeSpan.FromMinutes(30), PalTopic.FallingAsleep, context, now, bypassGap: true)
                        ?? Gesture(PalGesture.Sleep, PalMood.Sleepy, now, force: true);
                if (e.Duration >= TimeSpan.FromMinutes(2))
                    return Once("topic:bored", TimeSpan.FromMinutes(15), PalTopic.Bored, context, now);
                return null;
            case PalEventKind.ChannelOpened:
                return Once("channel:" + e.Channel, TimeSpan.FromHours(4), PalTopic.ChannelOpened, context, now)
                    ?? Gesture(PalGesture.Wave, PalMood.Happy, now);
            case PalEventKind.TileHovered:
                return Once("hover:" + (e.AppKey ?? e.App), TimeSpan.FromHours(2), PalTopic.TileHover, context, now);
            case PalEventKind.CustomizeOpened:
                return Once("topic:customize", TimeSpan.FromMinutes(40), PalTopic.Customize, context, now, bypassGap: true);
            case PalEventKind.Battery:
                if (e.Charging) return Once("topic:charging", TimeSpan.FromHours(3), PalTopic.Charging, context, now);
                if (e.Battery is >= 0 and <= 15) return Once("topic:battery", TimeSpan.FromMinutes(25), PalTopic.BatteryLow, context, now, bypassGap: true);
                return null;
        }
        return null;
    }

    // ---------------------------------------------------------------- decisions

    private PalReaction? Direct(PalTopic topic, PalContext context, bool speak)
    {
        var line = speak && Chattiness != PalChattiness.Silent ? Pick(topic, context) : null;
        if (line is null)
        {
            var any = PalLines.All.Where(l => l.Topic == topic && l.Fits(context)).OrderByDescending(l => l.Priority).FirstOrDefault();
            return new PalReaction(any?.Gesture ?? PalGesture.Wave, any?.Mood ?? PalMood.Happy, Direct: true);
        }
        Use(line);
        return new PalReaction(line.Gesture, line.Mood, line.Render(context), line.Id, Direct: true);
    }

    /// <summary>A comment nobody asked for: needs the quiet gap to have passed and the hourly cap to have room.</summary>
    private PalReaction? Ambient(PalTopic topic, PalContext context, DateTimeOffset now, bool bypassGap = false)
    {
        if (Chattiness == PalChattiness.Silent) return null;
        _memory.Spoke.RemoveAll(t => now - t > TimeSpan.FromHours(1));
        if (_memory.Spoke.Count >= HourlyCap) return null;
        if (!bypassGap && _memory.Spoke.Count > 0 && now - _memory.Spoke.Max() < Gap) return null;
        var line = Pick(topic, context);
        if (line is null) return null;
        Use(line);
        _memory.Spoke.Add(now);
        return new PalReaction(line.Gesture, line.Mood, line.Render(context), line.Id);
    }

    private PalReaction? Once(string key, TimeSpan cooldown, PalTopic topic, PalContext context, DateTimeOffset now, bool bypassGap = false)
    {
        if (!Due(key, now, cooldown, consume: false)) return null;
        var reaction = Ambient(topic, context, now, bypassGap);
        if (reaction is not null) Mark(key, now);
        return reaction;
    }

    /// <summary>Body language on its own, a few seconds apart at most, so the Pal stays lively without talking.</summary>
    private PalReaction? Gesture(PalGesture gesture, PalMood mood, DateTimeOffset now, bool force = false)
    {
        if (!force && now - _lastGesture < TimeSpan.FromSeconds(6)) return null;
        _lastGesture = now;
        return new PalReaction(gesture, mood);
    }

    /// <summary>
    /// The line to say: the highest-priority fitting line, and among those the one said longest ago (never-said
    /// lines first, then script order). Personality lines win ties so each Pal sounds like itself.
    /// </summary>
    public PalLine? Pick(PalTopic topic, PalContext context)
    {
        var candidates = PalLines.All.Where(l => l.Topic == topic && l.Fits(context)).ToList();
        if (candidates.Count == 0) return null;
        var top = candidates.Max(l => l.Priority);
        return candidates
            .Where(l => l.Priority == top)
            .Select((line, index) => (line, index))
            .OrderBy(x => _memory.LineTurns.TryGetValue(x.line.Id, out var turn) ? turn : -1)
            .ThenBy(x => x.line.Personality is null ? 1 : 0)
            .ThenBy(x => x.index)
            .First().line;
    }

    private void Use(PalLine line) => _memory.LineTurns[line.Id] = ++_memory.Turn;

    private bool Due(string key, DateTimeOffset now, TimeSpan cooldown, bool consume = true)
    {
        if (_memory.Cooldowns.TryGetValue(key, out var last) && now - last < cooldown && now >= last) return false;
        if (consume) _memory.Cooldowns[key] = now;
        return true;
    }

    private void Mark(string key, DateTimeOffset now) => _memory.Cooldowns[key] = now;
}
