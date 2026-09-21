using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Couchtop.App.Controls;
using Couchtop.App.Views;
using Couchtop.Core.Diagnostics;
using Couchtop.Core.Settings;

namespace Couchtop.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // No global frame-rate cap: animations run at the monitor's refresh rate (a 60 fps cap made 90-144 Hz
        // screens judder). WPF reports its rendering tier in the high word: 2 = full GPU acceleration.
        var tier = RenderCapability.Tier >> 16;
        Anim.LowPowerGraphics = tier < 2;
        Log.Info($"Graphics: render tier {tier}{(tier < 2 ? " (software or limited acceleration; ambient animations capped)" : " (hardware accelerated)")}");

        var host = AppHost.Create(Program.Options);
        ThemeManager.ApplyFor(host.Settings.Current.MenuStyle, host.Settings.Current.Theme);
        // Heals a taskbar left hidden by a run that was killed, and gives the helper somewhere to remember.
        Core.Shell.WindowsTaskbar.Attach(host.Settings);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Core.Shell.WindowsTaskbar.Restore();
        foreach (var issue in host.Settings.LoadIssues.Concat(host.Layout.LoadIssues))
            host.PostMessage("Settings repaired", issue);

        var window = new MainWindow(host);
        MainWindow = window;
        window.Show();
        Log.Info("Main window shown");
    }
}

public static class ThemeManager
{
    /// <summary>App.xaml merges the shared styles and channel art first; the theme is the last dictionary.</summary>
    private const int SharedDictionaryCount = 2;

    public static string Current { get; private set; } = ThemeCatalog.DefaultId;

    public static event EventHandler? Changed;

    /// <summary>
    /// Applies the look for a menu style: the Channels menu wears the chosen theme, while the Dashboard and
    /// Media Bar shells have one fixed look each that covers every screen.
    /// </summary>
    public static void ApplyFor(string menuStyle, string theme) => Apply(menuStyle switch
    {
        MenuStyleCatalog.Dashboard => "ModeDashboard",
        MenuStyleCatalog.MediaBar => "ModeMediaBar",
        _ => theme,
    });

    /// <summary>The shells are dictionaries too, but they are never offered in the theme list.</summary>
    private static bool IsShellLook(string id) => id is "ModeDashboard" or "ModeMediaBar";

    public static void Apply(string theme)
    {
        theme = IsShellLook(theme) ? theme : ThemeCatalog.Normalize(theme);
        ResourceDictionary replacement;
        try
        {
            replacement = new ResourceDictionary { Source = new Uri($"Themes/{theme}.xaml", UriKind.Relative) };
        }
        catch (Exception ex)
        {
            // A broken theme must never take the menu down with it.
            Log.Error($"Theme '{theme}' failed to load; using {ThemeCatalog.DefaultId}", ex);
            theme = ThemeCatalog.DefaultId;
            replacement = new ResourceDictionary { Source = new Uri($"Themes/{theme}.xaml", UriKind.Relative) };
        }

        var dictionaries = Application.Current.Resources.MergedDictionaries;
        while (dictionaries.Count > SharedDictionaryCount + 1) dictionaries.RemoveAt(dictionaries.Count - 1);
        if (dictionaries.Count == SharedDictionaryCount + 1) dictionaries[SharedDictionaryCount] = replacement;
        else dictionaries.Add(replacement);

        var changed = Current != theme;
        Current = theme;
        if (changed) Changed?.Invoke(null, EventArgs.Empty);
    }

    public static double Number(string key, double fallback) =>
        Application.Current.TryFindResource(key) is double d && double.IsFinite(d) ? d : fallback;

    public static Color ColorOf(string key, Color fallback) => Application.Current.TryFindResource(key) switch
    {
        Color c => c,
        SolidColorBrush b => b.Color,
        _ => fallback,
    };

    /// <summary>First and last colors of a theme brush (solid brushes return the same color twice).</summary>
    public static (Color Top, Color Bottom) Ends(string key, Color fallback) => Application.Current.TryFindResource(key) switch
    {
        SolidColorBrush b => (b.Color, b.Color),
        GradientBrush { GradientStops.Count: > 0 } g => (g.GradientStops.OrderBy(s => s.Offset).First().Color, g.GradientStops.OrderBy(s => s.Offset).Last().Color),
        _ => (fallback, fallback),
    };

    public static string Css(Color c) => c.A == 255 ? $"#{c.R:x2}{c.G:x2}{c.B:x2}" : $"rgba({c.R},{c.G},{c.B},{c.A / 255.0:0.###})";
}
