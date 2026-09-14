using System.Windows;
using System.Windows.Media.Animation;
using Couchtop.App.Views;
using Couchtop.Core.Diagnostics;

namespace Couchtop.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Timeline.DesiredFrameRateProperty.OverrideMetadata(typeof(Timeline), new FrameworkPropertyMetadata(60));

        var host = AppHost.Create(Program.Options);
        ThemeManager.Apply(host.Settings.Current.Theme);
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
    public static void Apply(string theme)
    {
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var source = new Uri(theme == "Night" ? "Themes/Night.xaml" : "Themes/Classic.xaml", UriKind.Relative);
        var replacement = new ResourceDictionary { Source = source };
        if (dictionaries.Count > 0) dictionaries[0] = replacement;
        else dictionaries.Insert(0, replacement);
    }
}
