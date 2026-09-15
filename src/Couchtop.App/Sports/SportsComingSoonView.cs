using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Couchtop.App.Views;
using Couchtop.Core.Sports;

namespace Couchtop.App.Sports;

public static class SportsAccess
{
    /// <summary>
    /// Couchtop Sports is a work in progress. Released builds always show the coming-soon screen; only a developer
    /// (Debug) build started with COUCHTOP_SPORTS_PREVIEW=1 opens the unfinished games.
    /// </summary>
    public static bool Playable =>
#if DEBUG
        Environment.GetEnvironmentVariable("COUCHTOP_SPORTS_PREVIEW") == "1";
#else
        false;
#endif

    public static FrameworkElement CreateEntryView(AppHost host, MainWindow window) =>
        Playable ? new SportsHubView(host, window) : new SportsComingSoonView(host, window);
}

/// <summary>What people see when they open the Sports channel before it's finished.</summary>
public sealed class SportsComingSoonView : UserControl, IScreenView
{
    private readonly Button _back;

    public SportsComingSoonView(AppHost host, MainWindow window)
    {
        var emblems = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 30) };
        foreach (var sport in Enum.GetValues<SportKind>())
        {
            var item = new StackPanel { Margin = new Thickness(28, 0, 28, 0) };
            var emblem = SportsHubView.Emblem(sport, 170);
            emblem.Opacity = 0.92;
            item.Children.Add(emblem);
            var name = ViewKit.Text(SportSim.DisplayName(sport), 34, FontWeights.ExtraBold, "ButtonTextBrush", wrap: false, align: TextAlignment.Center);
            name.Margin = new Thickness(0, 12, 0, 0);
            item.Children.Add(name);
            emblems.Children.Add(item);
        }

        var body = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 20, 0, 0) };
        body.Children.Add(emblems);
        var card = new StackPanel { Width = 1240 };
        var badge = new Border { HorizontalAlignment = HorizontalAlignment.Center, CornerRadius = new CornerRadius(30), Padding = new Thickness(34, 8, 34, 10), Margin = new Thickness(0, 0, 0, 14) };
        badge.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        badge.Child = ViewKit.Text("Work in progress", 28, FontWeights.ExtraBold, "InverseTextBrush", wrap: false);
        card.Children.Add(badge);
        card.Children.Add(ViewKit.Text("Coming soon", 76, FontWeights.ExtraBold, "AccentDeepBrush", align: TextAlignment.Center));
        var text = ViewKit.Text("Tennis, baseball, bowling, golf and boxing, built right into Couchtop. Quick matches with easy controls, training challenges with medals, and games with friends, playable with controllers, Wii Remotes, keyboard and mouse.", 32, FontWeights.Normal, align: TextAlignment.Center);
        text.Margin = new Thickness(0, 10, 0, 10);
        card.Children.Add(text);
        card.Children.Add(ViewKit.Text("Couchtop Sports is still being made and isn't playable yet. Keep an eye on the changelog.", 28, FontWeights.Normal, "SubtleTextBrush", align: TextAlignment.Center));
        _back = ViewKit.Pill("Back to Menu", window.ReturnToMenu, 420);
        _back.Height = 100;
        _back.HorizontalAlignment = HorizontalAlignment.Center;
        _back.Margin = new Thickness(0, 30, 0, 0);
        card.Children.Add(_back);
        body.Children.Add(ViewKit.Card(card, new Thickness(60, 40, 60, 40), new Thickness(0)));

        Content = ViewKit.Scaffold("Couchtop Sports", "Coming soon", body, window.ReturnToMenu);
    }

    public bool PlaysAmbience => true;

    public void OnShown() => Dispatcher.BeginInvoke(() => _back.Focus(), System.Windows.Threading.DispatcherPriority.Input);

    public void OnHidden() { }

    public bool HandleBack() => false;

    public bool HandleKey(KeyEventArgs e) => MenuKeys.MoveFocus(e);
}
