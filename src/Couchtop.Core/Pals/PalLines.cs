namespace Couchtop.Core.Pals;

/// <summary>What a Pal can talk about. Every line belongs to exactly one topic.</summary>
public enum PalTopic
{
    Greeting,
    FirstMeet,
    NewLook,
    WelcomeBack,
    WelcomeBackFromApp,
    WelcomeBackLong,
    AppLaunch,
    AppClosedQuick,
    AppClosedLong,
    LongSession,
    RapidSwitching,
    Bored,
    FallingAsleep,
    WakeUp,
    Poked,
    PokedLots,
    PickedUp,
    Dropped,
    ThemeChanged,
    Customize,
    TileHover,
    BatteryLow,
    Charging,
    Goodbye,
    ChannelOpened,
}

/// <summary>Body language that goes with a reaction. The app turns these into animations.</summary>
public enum PalGesture
{
    None,
    Wave,
    Cheer,
    Clap,
    Jump,
    Nod,
    ShakeHead,
    Think,
    Shrug,
    Point,
    Surprised,
    Laugh,
    Bow,
    Dance,
    Stretch,
    Yawn,
    LookAround,
    ThumbsUp,
    Sleep,
    Sit,
    Spin,
}

/// <summary>The face a Pal pulls while reacting.</summary>
public enum PalMood
{
    Neutral,
    Happy,
    Excited,
    Surprised,
    Sleepy,
    Thinking,
    Worried,
    Cheeky,
}

/// <summary>Everything a line's condition or text can refer to.</summary>
public sealed record PalContext
{
    public DateTimeOffset Now { get; init; }
    public string Personality { get; init; } = "cheerful";
    public string PalName { get; init; } = "Pal";
    public string? App { get; init; }
    public AppCategory Category { get; init; }
    public string? Channel { get; init; }
    public string? Theme { get; init; }
    public TimeSpan Duration { get; init; }
    public int Count { get; init; }
    public int Battery { get; init; } = -1;
    public DateTimeOffset? PalCreated { get; init; }

    public int Hour => Now.Hour;
    public bool Weekend => Now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
}

/// <summary>One handcrafted line: when it fits, what it says, and how the Pal acts while saying it.</summary>
public sealed record PalLine(string Id, PalTopic Topic, string Text, PalGesture Gesture = PalGesture.None, PalMood Mood = PalMood.Happy,
    Func<PalContext, bool>? When = null, string? Personality = null, int Priority = 0)
{
    public bool Fits(PalContext context) =>
        (Personality is null || Personality == context.Personality) && (When is null || When(context));

    /// <summary>Fills in {name}, {app}, {theme}, {hours}, {minutes}, {battery}, {day}, {count}, {time}.</summary>
    public string Render(PalContext context) => Text
        .Replace("{name}", context.PalName)
        .Replace("{app}", context.App ?? "that")
        .Replace("{theme}", context.Theme ?? "this look")
        .Replace("{hours}", Math.Max(1, (int)Math.Round(context.Duration.TotalHours, MidpointRounding.AwayFromZero)).ToString())
        .Replace("{minutes}", Math.Max(1, (int)Math.Round(context.Duration.TotalMinutes, MidpointRounding.AwayFromZero)).ToString())
        .Replace("{battery}", Math.Max(0, context.Battery).ToString())
        .Replace("{day}", context.Now.DayOfWeek.ToString())
        .Replace("{count}", context.Count.ToString())
        .Replace("{time}", context.Now.ToString("h:mm"));
}

/// <summary>
/// The full script. Every line here was written by hand; nothing is generated. Lines with a higher priority win
/// when they fit (holidays beat plain greetings), otherwise the least recently said line is picked.
/// </summary>
public static class PalLines
{
    private static bool Morning(PalContext c) => c.Hour is >= 5 and < 12;
    private static bool Afternoon(PalContext c) => c.Hour is >= 12 and < 17;
    private static bool Evening(PalContext c) => c.Hour is >= 17 and < 22;
    private static bool Night(PalContext c) => c.Hour is >= 22 or < 1;
    private static bool LateNight(PalContext c) => c.Hour is >= 1 and < 5;
    private static bool On(PalContext c, int month, int day) => c.Now.Month == month && c.Now.Day == day;
    private static bool Is(PalContext c, AppCategory category) => c.Category == category;

    private static bool Anniversary(PalContext c) =>
        c.PalCreated is { } created && c.Now.Month == created.Month && c.Now.Day == created.Day && c.Now.Year > created.Year;

    public static readonly IReadOnlyList<PalLine> All = new PalLine[]
    {
        // ---------------------------------------------------------------- greetings
        new("g-morning-1", PalTopic.Greeting, "Morning! Ready when you are.", PalGesture.Wave, When: Morning),
        new("g-morning-2", PalTopic.Greeting, "Good morning! I saved you a spot on the couch.", PalGesture.Wave, When: Morning),
        new("g-morning-3", PalTopic.Greeting, "Rise and shine! What are we starting with?", PalGesture.Cheer, When: Morning, Personality: "cheerful"),
        new("g-morning-4", PalTopic.Greeting, "Mmh... morning. Coffee first?", PalGesture.Yawn, PalMood.Sleepy, Morning, "chill"),
        new("g-morning-5", PalTopic.Greeting, "Morning! I already did my stretches. Twice.", PalGesture.Stretch, When: Morning, Personality: "sporty"),
        new("g-morning-6", PalTopic.Greeting, "Good morning! So, what's the plan today?", PalGesture.Wave, When: Morning, Personality: "curious"),
        new("g-morning-7", PalTopic.Greeting, "Oh, you're up! Took you long enough.", PalGesture.Wave, PalMood.Cheeky, Morning, "cheeky"),
        new("g-monday", PalTopic.Greeting, "Monday again! We've got this.", PalGesture.ThumbsUp, When: c => Morning(c) && c.Now.DayOfWeek == DayOfWeek.Monday, Priority: 1),
        new("g-afternoon-1", PalTopic.Greeting, "Good afternoon! How's the day going?", PalGesture.Wave, When: Afternoon),
        new("g-afternoon-2", PalTopic.Greeting, "Hey there! Perfect time for a little break.", PalGesture.Wave, When: Afternoon),
        new("g-afternoon-3", PalTopic.Greeting, "Afternoon! I was just rearranging the channels in my head.", PalGesture.Think, PalMood.Thinking, Afternoon),
        new("g-afternoon-4", PalTopic.Greeting, "Oh, hi! Busy afternoon?", PalGesture.Wave, When: Afternoon, Personality: "curious"),
        new("g-evening-1", PalTopic.Greeting, "Good evening! Time to kick back?", PalGesture.Wave, When: Evening),
        new("g-evening-2", PalTopic.Greeting, "Evening! Couch mode: on.", PalGesture.Cheer, When: Evening),
        new("g-evening-3", PalTopic.Greeting, "Hey! Long day? Let's make the evening a good one.", PalGesture.Wave, When: Evening),
        new("g-evening-4", PalTopic.Greeting, "Evening already? Where did the day go?", PalGesture.Shrug, PalMood.Surprised, Evening),
        new("g-friday", PalTopic.Greeting, "Friday night! Anything fun planned?", PalGesture.Dance, PalMood.Excited, c => Evening(c) && c.Now.DayOfWeek == DayOfWeek.Friday, Priority: 1),
        new("g-night-1", PalTopic.Greeting, "Night owl mode, huh?", PalGesture.Wave, PalMood.Cheeky, Night),
        new("g-night-2", PalTopic.Greeting, "Getting late! One more thing, then bed?", PalGesture.Yawn, PalMood.Sleepy, Night),
        new("g-night-3", PalTopic.Greeting, "The couch is extra comfy this time of night.", PalGesture.Wave, When: Night),
        new("g-late-1", PalTopic.Greeting, "Whoa, it's really late. Don't forget to sleep!", PalGesture.Surprised, PalMood.Worried, LateNight),
        new("g-late-2", PalTopic.Greeting, "It's {time} in the morning... I won't tell anyone.", PalGesture.Shrug, PalMood.Cheeky, LateNight),
        new("g-late-3", PalTopic.Greeting, "Late-night session? I'll keep you company.", PalGesture.Yawn, PalMood.Sleepy, LateNight),
        new("g-weekend-1", PalTopic.Greeting, "It's the weekend! No rush today.", PalGesture.Cheer, PalMood.Excited, c => c.Weekend && c.Hour is >= 8 and < 20),
        new("g-weekend-2", PalTopic.Greeting, "Happy {day}! The best kind of day for games.", PalGesture.Wave, PalMood.Excited, c => c.Weekend && c.Hour is >= 8 and < 20),
        new("g-newyear", PalTopic.Greeting, "Happy New Year! New year, same comfy couch.", PalGesture.Cheer, PalMood.Excited, c => On(c, 1, 1), Priority: 5),
        new("g-valentine", PalTopic.Greeting, "Happy Valentine's Day! I made you a heart. It's invisible.", PalGesture.Bow, When: c => On(c, 2, 14), Priority: 5),
        new("g-halloween", PalTopic.Greeting, "Happy Halloween! I'm dressed as... me. Spooky, right?", PalGesture.Spin, PalMood.Cheeky, c => On(c, 10, 31), Priority: 5),
        new("g-holidays", PalTopic.Greeting, "Happy holidays! I hope today is extra cozy.", PalGesture.Cheer, When: c => c.Now.Month == 12 && c.Now.Day is 24 or 25 or 26, Priority: 5),
        new("g-yearend", PalTopic.Greeting, "Last day of the year! Let's finish strong.", PalGesture.Cheer, PalMood.Excited, c => On(c, 12, 31), Priority: 5),
        new("g-anniversary", PalTopic.Greeting, "Guess what? We've been pals for a whole year today!", PalGesture.Cheer, PalMood.Excited, Anniversary, Priority: 9),

        // ---------------------------------------------------------------- meeting and looks
        new("meet-1", PalTopic.FirstMeet, "Hi! I'm {name}. So this is Couchtop? It's lovely in here!", PalGesture.Wave, PalMood.Excited),
        new("meet-2", PalTopic.FirstMeet, "Nice to meet you! I'll hang out down here. Click me any time.", PalGesture.Bow),
        new("look-1", PalTopic.NewLook, "New look! How do I look?", PalGesture.Spin, PalMood.Excited),
        new("look-2", PalTopic.NewLook, "Ooh, fresh. I feel like a whole new {name}.", PalGesture.Cheer),
        new("look-3", PalTopic.NewLook, "Thanks for the makeover!", PalGesture.Bow),
        new("look-4", PalTopic.NewLook, "Is it me, or do I look extra good today?", PalGesture.ThumbsUp, PalMood.Cheeky),

        // ---------------------------------------------------------------- coming back
        new("back-1", PalTopic.WelcomeBack, "Welcome back!", PalGesture.Wave),
        new("back-2", PalTopic.WelcomeBack, "Oh, you're back! Did I miss anything?", PalGesture.Wave, PalMood.Surprised),
        new("back-3", PalTopic.WelcomeBack, "Hey again!", PalGesture.Wave),
        new("back-4", PalTopic.WelcomeBack, "Back already? I was just getting comfy.", PalGesture.Stretch, PalMood.Sleepy, Personality: "chill"),
        new("back-app-1", PalTopic.WelcomeBackFromApp, "How was {app}?", PalGesture.Wave, PalMood.Happy),
        new("back-app-2", PalTopic.WelcomeBackFromApp, "Back from {app}? Welcome home.", PalGesture.Wave),
        new("back-app-3", PalTopic.WelcomeBackFromApp, "{app} all done? What's next?", PalGesture.Point, PalMood.Thinking),
        new("back-app-game-1", PalTopic.WelcomeBackFromApp, "Did you win? Tell me you won.", PalGesture.Jump, PalMood.Excited, c => Is(c, AppCategory.Game)),
        new("back-app-game-2", PalTopic.WelcomeBackFromApp, "Good game! I could hear the button mashing from here.", PalGesture.Clap, When: c => Is(c, AppCategory.Game)),
        new("back-app-curious", PalTopic.WelcomeBackFromApp, "So? What happened in {app}? I want details!", PalGesture.Jump, PalMood.Excited, Personality: "curious"),
        new("back-long-1", PalTopic.WelcomeBackLong, "There you are! It's been a while.", PalGesture.Cheer, PalMood.Excited),
        new("back-long-2", PalTopic.WelcomeBackLong, "You were gone ages! I counted the tiles. Still the same number.", PalGesture.Shrug),
        new("back-long-3", PalTopic.WelcomeBackLong, "Welcome back! I kept the couch warm.", PalGesture.Wave),
        new("back-long-4", PalTopic.WelcomeBackLong, "Oh! Hi! I definitely wasn't napping.", PalGesture.Surprised, PalMood.Sleepy, Personality: "chill"),

        // ---------------------------------------------------------------- launching things
        new("launch-game-1", PalTopic.AppLaunch, "Game time! Good luck!", PalGesture.Cheer, PalMood.Excited, c => Is(c, AppCategory.Game)),
        new("launch-game-2", PalTopic.AppLaunch, "Ooh, {app}! Show them what you've got.", PalGesture.ThumbsUp, PalMood.Excited, c => Is(c, AppCategory.Game)),
        new("launch-game-3", PalTopic.AppLaunch, "Have fun! I'll be right here cheering.", PalGesture.Cheer, When: c => Is(c, AppCategory.Game)),
        new("launch-game-4", PalTopic.AppLaunch, "Snacks ready? Okay, go!", PalGesture.Point, PalMood.Cheeky, c => Is(c, AppCategory.Game), "cheeky"),
        new("launch-game-5", PalTopic.AppLaunch, "Warm up those thumbs!", PalGesture.Stretch, PalMood.Excited, c => Is(c, AppCategory.Game), "sporty"),
        new("launch-game-6", PalTopic.AppLaunch, "{app}? Tell me everything afterwards.", PalGesture.Jump, When: c => Is(c, AppCategory.Game), Personality: "curious"),
        new("launch-game-7", PalTopic.AppLaunch, "Take it easy and have fun.", PalGesture.Wave, When: c => Is(c, AppCategory.Game), Personality: "chill"),
        new("launch-game-late", PalTopic.AppLaunch, "A game at this hour? Just one round, right?", PalGesture.Shrug, PalMood.Cheeky, c => Is(c, AppCategory.Game) && LateNight(c), Priority: 1),
        new("launch-store-1", PalTopic.AppLaunch, "Browsing for games? Nothing too pricey, okay?", PalGesture.Think, PalMood.Cheeky, c => Is(c, AppCategory.GameStore)),
        new("launch-store-2", PalTopic.AppLaunch, "Library time! What are we playing?", PalGesture.Jump, PalMood.Excited, c => Is(c, AppCategory.GameStore)),
        new("launch-store-3", PalTopic.AppLaunch, "Checking for new games? I love new games.", PalGesture.Clap, When: c => Is(c, AppCategory.GameStore)),
        new("launch-web-1", PalTopic.AppLaunch, "Surfing the web? Watch out for rabbit holes.", PalGesture.Wave, When: c => Is(c, AppCategory.Browser)),
        new("launch-web-2", PalTopic.AppLaunch, "To the internet!", PalGesture.Point, When: c => Is(c, AppCategory.Browser)),
        new("launch-web-3", PalTopic.AppLaunch, "Try not to open forty tabs this time.", PalGesture.Shrug, PalMood.Cheeky, c => Is(c, AppCategory.Browser), "cheeky"),
        new("launch-web-4", PalTopic.AppLaunch, "Looking something up? I love learning stuff.", PalGesture.Think, PalMood.Thinking, c => Is(c, AppCategory.Browser), "curious"),
        new("launch-music-1", PalTopic.AppLaunch, "Music! I'll tap my foot along.", PalGesture.Dance, When: c => Is(c, AppCategory.Music)),
        new("launch-music-2", PalTopic.AppLaunch, "Ooh, what are we listening to?", PalGesture.Dance, PalMood.Excited, c => Is(c, AppCategory.Music)),
        new("launch-music-3", PalTopic.AppLaunch, "Turn it up! Well... not too loud.", PalGesture.Dance, PalMood.Cheeky, c => Is(c, AppCategory.Music)),
        new("launch-video-1", PalTopic.AppLaunch, "Movie time? I'll grab the imaginary popcorn.", PalGesture.Sit, When: c => Is(c, AppCategory.Video)),
        new("launch-video-2", PalTopic.AppLaunch, "Watching something? No spoilers, please!", PalGesture.ShakeHead, When: c => Is(c, AppCategory.Video)),
        new("launch-video-3", PalTopic.AppLaunch, "Show time! Lights down.", PalGesture.Clap, When: c => Is(c, AppCategory.Video)),
        new("launch-code-1", PalTopic.AppLaunch, "Coding? You've got this. Mind the semicolons.", PalGesture.ThumbsUp, When: c => Is(c, AppCategory.Code)),
        new("launch-code-2", PalTopic.AppLaunch, "Building something cool? I believe in you.", PalGesture.Cheer, When: c => Is(c, AppCategory.Code)),
        new("launch-code-3", PalTopic.AppLaunch, "May all your builds be green.", PalGesture.Bow, When: c => Is(c, AppCategory.Code)),
        new("launch-office-1", PalTopic.AppLaunch, "Work mode. I'll be quiet... mostly.", PalGesture.Nod, PalMood.Neutral, c => Is(c, AppCategory.Office)),
        new("launch-office-2", PalTopic.AppLaunch, "Documents! You're so productive today.", PalGesture.ThumbsUp, When: c => Is(c, AppCategory.Office)),
        new("launch-office-3", PalTopic.AppLaunch, "Spreadsheets! My second favorite grid, after the channel menu.", PalGesture.Point, PalMood.Cheeky, c => Is(c, AppCategory.Office) && (c.App ?? "").Contains("xcel", StringComparison.OrdinalIgnoreCase)),
        new("launch-chat-1", PalTopic.AppLaunch, "Say hi from me!", PalGesture.Wave, When: c => Is(c, AppCategory.Chat)),
        new("launch-chat-2", PalTopic.AppLaunch, "Catching up with friends? Nice.", PalGesture.Nod, When: c => Is(c, AppCategory.Chat)),
        new("launch-art-1", PalTopic.AppLaunch, "Making art? Can I be in it?", PalGesture.Jump, PalMood.Excited, c => Is(c, AppCategory.Art)),
        new("launch-art-2", PalTopic.AppLaunch, "Ooh, painting! Happy little pixels.", PalGesture.Clap, When: c => Is(c, AppCategory.Art)),
        new("launch-notes", PalTopic.AppLaunch, "Taking notes? Good thinking.", PalGesture.Nod, PalMood.Thinking, c => Is(c, AppCategory.Notes)),
        new("launch-calc-1", PalTopic.AppLaunch, "Math time! Don't forget to carry the one.", PalGesture.Think, PalMood.Thinking, c => Is(c, AppCategory.Calculator)),
        new("launch-calc-2", PalTopic.AppLaunch, "Quick maths!", PalGesture.Point, When: c => Is(c, AppCategory.Calculator)),
        new("launch-mail", PalTopic.AppLaunch, "Checking mail? I hope it's good news.", PalGesture.Nod, When: c => Is(c, AppCategory.Mail)),
        new("launch-terminal", PalTopic.AppLaunch, "The command line! Very hacker of you.", PalGesture.ThumbsUp, PalMood.Cheeky, c => Is(c, AppCategory.Terminal)),
        new("launch-any-1", PalTopic.AppLaunch, "Opening {app}! Have fun.", PalGesture.Wave),
        new("launch-any-2", PalTopic.AppLaunch, "{app}. Nice choice.", PalGesture.ThumbsUp),
        new("launch-any-3", PalTopic.AppLaunch, "Off you go! I'll be here.", PalGesture.Wave),
        new("launch-any-4", PalTopic.AppLaunch, "Ooh, {app}? What's that for?", PalGesture.Think, PalMood.Thinking, Personality: "curious"),

        // ---------------------------------------------------------------- Couchtop's own channels
        new("ch-files", PalTopic.ChannelOpened, "Looking for something? Files is on it.", PalGesture.Point, When: c => c.Channel == "files"),
        new("ch-photos", PalTopic.ChannelOpened, "Photos! I love a good memory.", PalGesture.Clap, When: c => c.Channel == "photos"),
        new("ch-web", PalTopic.ChannelOpened, "To the web! I'll wait here.", PalGesture.Wave, When: c => c.Channel == "browser"),
        new("ch-settings", PalTopic.ChannelOpened, "Tweaking things? Make it cozy.", PalGesture.Nod, When: c => c.Channel == "settings"),
        new("ch-sports", PalTopic.ChannelOpened, "Sports? I've been practicing! It's coming soon.", PalGesture.Stretch, PalMood.Excited, c => c.Channel == "sports"),

        // ---------------------------------------------------------------- closing things and sessions
        new("closed-quick-1", PalTopic.AppClosedQuick, "That was quick! Wrong app?", PalGesture.Shrug, PalMood.Surprised),
        new("closed-quick-2", PalTopic.AppClosedQuick, "In and out! Found what you needed?", PalGesture.ThumbsUp),
        new("closed-quick-3", PalTopic.AppClosedQuick, "Blink and you'd miss it!", PalGesture.Laugh, PalMood.Cheeky, Personality: "cheeky"),
        new("closed-long-1", PalTopic.AppClosedLong, "{hours} hours of {app}! That's dedication.", PalGesture.Clap, PalMood.Excited),
        new("closed-long-2", PalTopic.AppClosedLong, "Good session? You were in there a while!", PalGesture.Wave),
        new("closed-long-game", PalTopic.AppClosedLong, "What a marathon! Your controller deserves a nap.", PalGesture.Yawn, PalMood.Sleepy, c => Is(c, AppCategory.Game)),
        new("session-game-1", PalTopic.LongSession, "You've been playing a while. Maybe stretch your legs?", PalGesture.Stretch, PalMood.Neutral, c => Is(c, AppCategory.Game)),
        new("session-game-2", PalTopic.LongSession, "Quick water break? I can't pause your game, but I believe in you.", PalGesture.ThumbsUp, When: c => Is(c, AppCategory.Game)),
        new("session-work-1", PalTopic.LongSession, "You've been at it for {hours} hours. Time for a little break?", PalGesture.Stretch, PalMood.Worried, c => !Is(c, AppCategory.Game)),
        new("session-work-2", PalTopic.LongSession, "Hard work! Don't forget to blink. I blink a lot.", PalGesture.Nod, When: c => !Is(c, AppCategory.Game)),
        new("session-sporty", PalTopic.LongSession, "Break time! Ten jumping jacks. I'll count.", PalGesture.Jump, PalMood.Excited, Personality: "sporty", Priority: 1),
        new("switching-1", PalTopic.RapidSwitching, "Juggling a lot of windows, huh?", PalGesture.LookAround, PalMood.Surprised),
        new("switching-2", PalTopic.RapidSwitching, "So much switching! I'm getting dizzy.", PalGesture.Spin, PalMood.Worried),
        new("switching-3", PalTopic.RapidSwitching, "Busy busy! You're doing great.", PalGesture.ThumbsUp),

        // ---------------------------------------------------------------- hanging out on the menu
        new("bored-1", PalTopic.Bored, "Just hanging out? Me too.", PalGesture.Sit, PalMood.Neutral),
        new("bored-2", PalTopic.Bored, "Should I rearrange the channels? Kidding.", PalGesture.Laugh, PalMood.Cheeky),
        new("bored-3", PalTopic.Bored, "I could stare at these tiles all day. Apparently.", PalGesture.LookAround, PalMood.Neutral),
        new("bored-4", PalTopic.Bored, "Pick something, anything! I'm curious.", PalGesture.Point, PalMood.Excited, Personality: "curious"),
        new("bored-5", PalTopic.Bored, "Hmm hm hmm... just humming.", PalGesture.Dance, PalMood.Happy, Personality: "chill"),
        new("bored-6", PalTopic.Bored, "Nobody's watching? Dance break!", PalGesture.Dance, PalMood.Excited, Personality: "cheeky"),
        new("bored-7", PalTopic.Bored, "Stretch break! Reach for the sky!", PalGesture.Stretch, PalMood.Happy, Personality: "sporty"),
        new("bored-8", PalTopic.Bored, "It's a nice day to click a channel.", PalGesture.Wave, Personality: "cheerful"),
        new("sleep-1", PalTopic.FallingAsleep, "Zzz...", PalGesture.Sleep, PalMood.Sleepy),
        new("sleep-2", PalTopic.FallingAsleep, "Just resting my eyes...", PalGesture.Sleep, PalMood.Sleepy),
        new("wake-1", PalTopic.WakeUp, "Huh? Oh! I'm awake. Totally awake.", PalGesture.Surprised, PalMood.Surprised),
        new("wake-2", PalTopic.WakeUp, "Wha-? Hi! I wasn't asleep.", PalGesture.Surprised, PalMood.Surprised),
        new("wake-3", PalTopic.WakeUp, "Five more minutes...", PalGesture.Yawn, PalMood.Sleepy, Personality: "chill"),

        // ---------------------------------------------------------------- being poked and picked up
        new("poke-1", PalTopic.Poked, "Hi!", PalGesture.Wave),
        new("poke-2", PalTopic.Poked, "That tickles!", PalGesture.Laugh, PalMood.Excited),
        new("poke-3", PalTopic.Poked, "You rang?", PalGesture.Bow),
        new("poke-4", PalTopic.Poked, "Hey! What's up?", PalGesture.Wave),
        new("poke-5", PalTopic.Poked, "Boop!", PalGesture.Jump, PalMood.Excited),
        new("poke-6", PalTopic.Poked, "Need something? I'm all ears.", PalGesture.Nod, PalMood.Thinking, Personality: "curious"),
        new("poke-7", PalTopic.Poked, "Poke me again, I dare you.", PalGesture.Point, PalMood.Cheeky, Personality: "cheeky"),
        new("poke-8", PalTopic.Poked, "Ready for action!", PalGesture.Cheer, PalMood.Excited, Personality: "sporty"),
        new("poke-9", PalTopic.Poked, "Heyyy.", PalGesture.Wave, PalMood.Sleepy, Personality: "chill"),
        new("poke-10", PalTopic.Poked, "Right here!", PalGesture.Spin),
        new("pokes-1", PalTopic.PokedLots, "Okay, okay, I'm here!", PalGesture.ShakeHead, PalMood.Surprised),
        new("pokes-2", PalTopic.PokedLots, "You're very clicky today.", PalGesture.Shrug, PalMood.Cheeky),
        new("pokes-3", PalTopic.PokedLots, "I'm going to start charging for pokes.", PalGesture.Point, PalMood.Cheeky),
        new("pokes-4", PalTopic.PokedLots, "Fine, you win the poking contest.", PalGesture.Bow, PalMood.Happy, c => c.Count >= 10, Priority: 1),
        new("up-1", PalTopic.PickedUp, "Whoa! Put me down!", PalGesture.Surprised, PalMood.Surprised),
        new("up-2", PalTopic.PickedUp, "Wheee!", PalGesture.None, PalMood.Excited),
        new("up-3", PalTopic.PickedUp, "I can see my tile from up here!", PalGesture.None, PalMood.Excited),
        new("down-1", PalTopic.Dropped, "Nice landing.", PalGesture.ThumbsUp),
        new("down-2", PalTopic.Dropped, "Ta-da!", PalGesture.Cheer, PalMood.Excited),
        new("down-3", PalTopic.Dropped, "Ooh, new spot. I like it.", PalGesture.LookAround),

        // ---------------------------------------------------------------- themes and tidying
        new("theme-classic", PalTopic.ThemeChanged, "Classic! Clean and comfy.", PalGesture.Nod, When: c => c.Theme == "Classic"),
        new("theme-night", PalTopic.ThemeChanged, "Ooh, dark mode. Very cozy.", PalGesture.Yawn, PalMood.Sleepy, c => c.Theme == "Night"),
        new("theme-sky", PalTopic.ThemeChanged, "Bubbles! I love it up here.", PalGesture.Jump, PalMood.Excited, c => c.Theme == "SkyResort"),
        new("theme-neon", PalTopic.ThemeChanged, "Neon lights! I feel so cool right now.", PalGesture.Dance, PalMood.Cheeky, c => c.Theme == "NeonCity"),
        new("theme-midnight", PalTopic.ThemeChanged, "Midnight. Sleek. Mysterious. Just like me.", PalGesture.ThumbsUp, PalMood.Cheeky, c => c.Theme == "Midnight"),
        new("theme-sakura", PalTopic.ThemeChanged, "Cherry blossoms! It's so pretty.", PalGesture.Spin, PalMood.Excited, c => c.Theme == "Sakura"),
        new("theme-sunset", PalTopic.ThemeChanged, "Golden hour! Perfect.", PalGesture.Stretch, When: c => c.Theme == "Sunset"),
        new("theme-contrast", PalTopic.ThemeChanged, "Nice and clear! I can see everything.", PalGesture.Nod, When: c => c.Theme == "HighContrast"),
        new("theme-any", PalTopic.ThemeChanged, "New look for Couchtop! I like it.", PalGesture.Clap),
        new("custom-1", PalTopic.Customize, "Rearranging? Put your favorites up front!", PalGesture.Point),
        new("custom-2", PalTopic.Customize, "Tidying up? I'll supervise.", PalGesture.Nod, PalMood.Cheeky),
        new("custom-3", PalTopic.Customize, "Ooh, redecorating!", PalGesture.Clap, PalMood.Excited),
        new("hover-1", PalTopic.TileHover, "{app}? Good pick.", PalGesture.ThumbsUp),
        new("hover-2", PalTopic.TileHover, "Ooh, what's {app} like?", PalGesture.Think, PalMood.Thinking, Personality: "curious"),
        new("hover-3", PalTopic.TileHover, "That one's a classic.", PalGesture.Nod),
        new("hover-game", PalTopic.TileHover, "Is it game time? It feels like game time.", PalGesture.Jump, PalMood.Excited, c => Is(c, AppCategory.Game)),

        // ---------------------------------------------------------------- power
        new("battery-1", PalTopic.BatteryLow, "Battery's at {battery}%. Maybe plug in soon?", PalGesture.Point, PalMood.Worried),
        new("battery-2", PalTopic.BatteryLow, "Only {battery}% left! Charger, anyone?", PalGesture.LookAround, PalMood.Worried),
        new("battery-3", PalTopic.BatteryLow, "Running low on power. I know the feeling.", PalGesture.Yawn, PalMood.Sleepy),
        new("charge-1", PalTopic.Charging, "Charging up! Me too, sort of.", PalGesture.Cheer),
        new("charge-2", PalTopic.Charging, "Ahh, power. Much better.", PalGesture.Stretch),
        new("bye-1", PalTopic.Goodbye, "Heading off? See you soon!", PalGesture.Wave),
        new("bye-2", PalTopic.Goodbye, "Bye for now! I'll be right here.", PalGesture.Wave),
        new("bye-3", PalTopic.Goodbye, "Good night! Sleep well.", PalGesture.Wave, PalMood.Sleepy, c => Night(c) || LateNight(c), Priority: 1),
        new("bye-4", PalTopic.Goodbye, "Leaving already? Okay, go have fun!", PalGesture.Wave, PalMood.Cheeky, Personality: "cheeky"),
    };
}
