using System.Windows;
using Couchtop.App.Services;
using Couchtop.Core.Channels;
using Couchtop.Core.Settings;

namespace Couchtop.App.Views;

/// <summary>
/// A Couchtop home screen. Three of them exist (Channels, Dashboard, Media Bar); they are interchangeable
/// presentations of the same channels, and <see cref="MainWindow"/> swaps them live from Settings › Display.
/// </summary>
public interface IHomeScreen : IScreenView
{
    /// <summary>The screen itself, for the view stack and animations.</summary>
    FrameworkElement Element { get; }

    /// <summary>Menu style id this screen implements (see <see cref="MenuStyleCatalog"/>).</summary>
    string StyleId { get; }

    /// <summary>The menu is in front and the window is active: run idle animations and the clock.</summary>
    void SetActive(bool active);

    /// <summary>Opening flourish after the splash or when coming back from a screen.</summary>
    void PlayIntro();

    /// <summary>Channels, theme, text size or Pal changed: build the screen again.</summary>
    void Rebuild();

    /// <summary>Starts (or leaves) channel editing, however this style presents it.</summary>
    void SetEditMode(bool on);

    bool IsEditMode { get; }

    /// <summary>Opens a built-in channel by id, as if the user had picked its tile.</summary>
    void OpenBuiltIn(string id, Point? origin);

    void RefreshClock();

    /// <summary>Screen point a channel opens from, so the zoom starts at the right place.</summary>
    Point SlotCenter(int slot);

    /// <summary>Snapshot rendering: highlight a tile (or -1 for none) without a pointer.</summary>
    void SnapshotHover(int slot);
}

/// <summary>
/// What happens when an item is picked. Shared by all three home screens so a channel behaves identically
/// whichever menu style opened it.
/// </summary>
public static class HomeActions
{
    /// <summary>Opens an item. <paramref name="onCustomize"/> lets each style present channel editing its own way.</summary>
    public static void Open(MainWindow window, AppHost host, HomeItem item, Point? origin, Action onCustomize, Action? onChannelOpened = null)
    {
        switch (item.Kind)
        {
            case HomeItemKind.Desktop:
                window.ShowWindowsDesktop();
                return;
            case HomeItemKind.Board:
                window.Navigate(new MessageBoardView(host, window), origin);
                return;
            case HomeItemKind.Bookmark:
                window.Navigate(new BrowserView(host, window, item.Url), origin);
                return;
        }

        if (item.Channel is not { } channel) return;
        window.ShowBubble(null);
        switch (channel.Kind)
        {
            case ChannelKind.BuiltIn:
                OpenBuiltIn(window, host, channel.BuiltInId!, origin, onCustomize, onChannelOpened);
                break;
            case ChannelKind.Folder when channel.Launch?.Path is { } folder:
                window.Navigate(new FilesView(host, window, folder), origin);
                onChannelOpened?.Invoke();
                break;
            default:
                if (host.Settings.Current.QuickLaunch) _ = ChannelActions.LaunchAsync(window, host, channel);
                else window.Navigate(new ChannelPreviewView(host, window, channel), origin);
                onChannelOpened?.Invoke();
                break;
        }
    }

    /// <summary>
    /// Channel tools for the styles that have no drag-and-drop grid: the same add, find, edit and remove
    /// actions the Channels menu offers, in one dialog.
    /// </summary>
    public static async Task ShowChannelToolsAsync(MainWindow window, AppHost host, Channel? selected, Action onClosed)
    {
        var result = await window.ShowCustomDialogAsync(close =>
        {
            var stack = new System.Windows.Controls.StackPanel();
            stack.Children.Add(ViewKit.Text("Channels", 52, System.Windows.FontWeights.ExtraBold, align: System.Windows.TextAlignment.Center));
            stack.Children.Add(ViewKit.Text("Dragging tiles around lives in the Channels menu style. Everything else works here.",
                24, System.Windows.FontWeights.Normal, "SubtleTextBrush", align: System.Windows.TextAlignment.Center));
            var actions = new List<(string Label, string Key)> { ("Add Channel", "add"), ("Find New Apps", "rescan") };
            if (selected is not null)
            {
                actions.Add(($"Edit \"{selected.Title}\"", "edit"));
                actions.Add(($"Remove \"{selected.Title}\"", "remove"));
            }
            actions.Add(("Done", ""));
            foreach (var (label, key) in actions)
            {
                var button = ViewKit.Pill(label, () => close(key), 620);
                button.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
                button.Margin = new Thickness(0, 14, 0, 0);
                stack.Children.Add(button);
            }
            return ViewKit.Panel(stack, 880);
        });

        onClosed();
        switch (result as string)
        {
            case "add": await ChannelDialogs.AddChannelAsync(window, host, null); break;
            case "rescan":
                host.RefreshChannelsInBackground();
                window.ShowToast("Looking for newly installed apps…");
                break;
            case "edit" when selected is not null: await ChannelDialogs.EditChannelAsync(window, host, selected); break;
            case "remove" when selected is not null: await ChannelDialogs.ConfirmRemoveAsync(window, host, selected); break;
        }
    }

    public static void OpenBuiltIn(MainWindow window, AppHost host, string id, Point? origin, Action onCustomize, Action? onChannelOpened = null)
    {
        // Remembered for "continue where I left off"; written to disk when Couchtop closes.
        host.Settings.Current.LastScreen = id;
        switch (id)
        {
            case BuiltInChannels.Files: window.Navigate(new FilesView(host, window, host.Settings.Current.RestoreLastScreen ? host.Settings.Current.LastFolder : null), origin); break;
            case BuiltInChannels.Photos: window.Navigate(new PhotosView(host, window), origin); break;
            case BuiltInChannels.Browser: window.Navigate(new BrowserView(host, window), origin); break;
            case BuiltInChannels.Sports: window.Navigate(Sports.SportsAccess.CreateEntryView(host, window), origin); break;
            case BuiltInChannels.Settings: window.Navigate(new SettingsView(host, window), origin); break;
            case BuiltInChannels.Power: window.Navigate(new PowerView(host, window), origin); break;
            // Pal Studio only exists in the Channels menu style.
            case BuiltInChannels.Pals when host.Pals.Suspended: window.ShowToast("Pals live in the Channels menu style"); break;
            case BuiltInChannels.Pals: window.Navigate(new Pals.PalStudioView(host, window), origin); break;
            case BuiltInChannels.Customize: onCustomize(); break;
        }
        if (id != BuiltInChannels.Customize) onChannelOpened?.Invoke();
        host.Pals.ReportBuiltIn(id);
    }
}
