using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Couchtop.App.Controls;
using Couchtop.App.Views;
using Couchtop.Core.Channels;
using Couchtop.Core.Diagnostics;
using Couchtop.Core.Discovery;
using Couchtop.Core.Launching;

namespace Couchtop.App.Services;

public static class ChannelActions
{
    public static async Task<LaunchStatus> LaunchAsync(MainWindow window, AppHost host, Channel channel)
    {
        host.Audio.Play(SoundEffect.Launch);
        window.Flash();
        LaunchResult result;
        try
        {
            result = await Task.Run(() => host.Launcher.Launch(channel));
        }
        catch (Exception ex)
        {
            result = new LaunchResult(LaunchStatus.Failed, ex.Message);
        }
        if (result.LaunchSpecUpdated) host.SaveLayout(raiseChanged: false);

        switch (result.Status)
        {
            case LaunchStatus.Started:
                window.ShowToast($"Starting {channel.Title}…");
                host.Pals.ReportLaunch(channel);
                if (host.Settings.Current.HideMenuWhenAppLaunches && !host.IsShellSession) window.WindowState = WindowState.Minimized;
                break;
            case LaunchStatus.Cancelled:
                break;
            case LaunchStatus.NotFound:
                host.Audio.Play(SoundEffect.Error);
                await HandleMissingAsync(window, host, channel, result.Message);
                break;
            default:
                host.Audio.Play(SoundEffect.Error);
                await window.ShowDialogAsync(result.Message, "OK");
                break;
        }
        return result.Status;
    }

    private static async Task HandleMissingAsync(MainWindow window, AppHost host, Channel channel, string message)
    {
        var choice = await window.ShowDialogAsync(message + "\n\nYou can show Couchtop where the app is now, or remove this channel.", "Locate…", "Remove Channel", "Cancel");
        if (choice == "Locate…")
        {
            var dialog = new OpenFileDialog
            {
                Title = $"Locate {channel.Title}",
                Filter = "Programs and shortcuts|*.exe;*.lnk;*.url;*.bat;*.cmd;*.appref-ms|All files|*.*",
                CheckFileExists = true,
            };
            if (dialog.ShowDialog(window) != true) return;
            channel.Launch = SpecForFile(dialog.FileName);
            channel.IconSource = dialog.FileName;
            host.SaveLayout();
            await LaunchAsync(window, host, channel);
        }
        else if (choice == "Remove Channel")
        {
            LayoutEditor.Remove(host.Layout.Layout, channel.Id);
            host.SaveLayout();
        }
    }

    public static LaunchSpec SpecForFile(string file) =>
        Path.GetExtension(file).ToLowerInvariant() is ".lnk" or ".url" or ".appref-ms"
            ? new LaunchSpec { ShortcutPath = file }
            : new LaunchSpec { Path = file, WorkingDirectory = Path.GetDirectoryName(file) };
}

public static class ChannelDialogs
{
    private sealed record WebsiteRequest(string Title, string Url);

    private static readonly string[] Swatches = { "#8FD3F4", "#7CCB5B", "#FFD166", "#FF9DB5", "#B39DDB", "#FFB26B", "#9AA4AA", "#5ED3C4" };

    public static async Task AddChannelAsync(MainWindow window, AppHost host, int? slot)
    {
        var apps = host.Discovery.Latest?.Apps ?? host.Discovery.LoadCache()?.Apps ?? (IReadOnlyList<DiscoveredApp>)Array.Empty<DiscoveredApp>();
        var result = await window.ShowCustomDialogAsync(close => BuildAddPanel(host, apps, close));
        var layout = host.Layout.Layout;
        Channel? channel = null;

        switch (result)
        {
            case DiscoveredApp app:
                channel = ChannelSeeder.ToChannel(app);
                layout.DismissedSourceKeys.RemoveAll(k => string.Equals(k, app.SourceKey, StringComparison.OrdinalIgnoreCase));
                break;
            case WebsiteRequest site:
                channel = new Channel { Kind = ChannelKind.Website, Title = site.Title, Launch = new LaunchSpec { Uri = site.Url }, SourceKey = "web:" + site.Url.ToLowerInvariant() };
                break;
            case "file":
                var file = new OpenFileDialog { Title = "Choose a program or shortcut", Filter = "Programs and shortcuts|*.exe;*.lnk;*.url;*.bat;*.cmd;*.appref-ms|All files|*.*", CheckFileExists = true };
                if (file.ShowDialog(window) == true)
                {
                    channel = new Channel
                    {
                        Kind = ChannelKind.App,
                        Title = Path.GetFileNameWithoutExtension(file.FileName),
                        Launch = ChannelActions.SpecForFile(file.FileName),
                        IconSource = file.FileName,
                        SourceKey = "custom:" + file.FileName.ToLowerInvariant(),
                    };
                }
                break;
            case "folder":
                var folder = new OpenFolderDialog { Title = "Choose a folder" };
                if (folder.ShowDialog(window) == true)
                {
                    var name = new DirectoryInfo(folder.FolderName).Name;
                    channel = new Channel
                    {
                        Kind = ChannelKind.Folder,
                        Title = string.IsNullOrWhiteSpace(name) ? folder.FolderName : name,
                        Launch = new LaunchSpec { Path = folder.FolderName },
                        IconSource = folder.FolderName,
                        SourceKey = "folder:" + folder.FolderName.ToLowerInvariant(),
                    };
                }
                break;
            case string s when s.StartsWith("builtin:", StringComparison.Ordinal):
                channel = BuiltInChannels.Create(s["builtin:".Length..]);
                break;
        }

        if (channel is null) return;
        try
        {
            LayoutEditor.Place(layout, channel, slot ?? 0);
            host.SaveLayout();
            window.ShowToast($"\"{channel.Title}\" was added");
        }
        catch (InvalidOperationException ex)
        {
            await window.ShowDialogAsync(ex.Message, "OK");
        }
    }

    private static FrameworkElement BuildAddPanel(AppHost host, IReadOnlyList<DiscoveredApp> apps, Action<object?> close)
    {
        var layout = host.Layout.Layout;
        var root = new Grid { Height = 860 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        root.Children.Add(ViewKit.Text("Add a Channel", 52, FontWeights.ExtraBold));

        var tabs = new WrapPanel { Margin = new Thickness(0, 16, 0, 10) };
        Grid.SetRow(tabs, 1);
        root.Children.Add(tabs);

        var contentHost = new ContentControl { Focusable = false };
        Grid.SetRow(contentHost, 2);
        root.Children.Add(contentHost);

        // Installed apps
        var appsPanel = new Grid();
        appsPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        appsPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        appsPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var search = new TextBox { Margin = new Thickness(0, 0, 0, 8) };
        appsPanel.Children.Add(search);

        var missingBuiltIns = BuiltInChannels.All.Where(id => LayoutEditor.Find(layout, "builtin-" + id) is null).ToList();
        var builtInRow = new WrapPanel();
        foreach (var id in missingBuiltIns)
        {
            var captured = id;
            builtInRow.Children.Add(ViewKit.Pill(BuiltInChannels.DefaultTitle(id), () => close("builtin:" + captured), 200, "SmallPill"));
        }
        Grid.SetRow(builtInRow, 1);
        appsPanel.Children.Add(builtInRow);

        var list = new ListBox();
        Grid.SetRow(list, 2);
        appsPanel.Children.Add(list);
        var available = apps.Where(a => !LayoutEditor.ContainsSource(layout, a.SourceKey)).ToList();

        void Fill(string filter)
        {
            list.Items.Clear();
            var matches = available.Where(a => filter.Length == 0 || a.Title.Contains(filter, StringComparison.CurrentCultureIgnoreCase)).Take(300).ToList();
            foreach (var app in matches)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Tag = app };
                var icon = ViewKit.IconImage(64);
                icon.Margin = new Thickness(6, 0, 22, 0);
                row.Children.Add(icon);
                var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                text.Children.Add(ViewKit.Text(app.Title, 30, FontWeights.Bold, wrap: false));
                text.Children.Add(ViewKit.Text(app.Source + (layout.DismissedSourceKeys.Contains(app.SourceKey) ? " · previously removed" : ""), 22, FontWeights.Normal, "SubtleTextBrush", wrap: false));
                row.Children.Add(text);
                list.Items.Add(row);
                _ = LoadIconAsync(host, icon, app.IconSource ?? app.Launch.ShortcutPath ?? app.Launch.Path);
            }
            if (matches.Count == 0) list.Items.Add(ViewKit.Text(available.Count == 0 ? "All discovered apps are already channels. Try \"Program or Shortcut\"." : "No apps match your search.", 28, FontWeights.Normal, "SubtleTextBrush"));
        }

        search.TextChanged += (_, _) => Fill(search.Text.Trim());
        list.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (ItemsControl.ContainerFromElement(list, e.OriginalSource as DependencyObject) is ListBoxItem { Content: FrameworkElement { Tag: DiscoveredApp app } }) close(app);
        };
        list.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && list.SelectedItem is FrameworkElement { Tag: DiscoveredApp app }) close(app);
        };
        Fill("");

        // Website
        var sitePanel = new StackPanel();
        sitePanel.Children.Add(ViewKit.Text("Name", 30, FontWeights.Bold));
        var siteName = new TextBox { Margin = new Thickness(0, 6, 0, 20) };
        sitePanel.Children.Add(siteName);
        sitePanel.Children.Add(ViewKit.Text("Address", 30, FontWeights.Bold));
        var siteUrl = new TextBox { Margin = new Thickness(0, 6, 0, 20), Text = "https://" };
        sitePanel.Children.Add(siteUrl);
        var siteError = ViewKit.Text("", 26, FontWeights.Normal, "DangerBrush");
        sitePanel.Children.Add(siteError);
        sitePanel.Children.Add(ViewKit.Pill("Add Website", () =>
        {
            var url = siteUrl.Text.Trim();
            if (!url.Contains("://", StringComparison.Ordinal)) url = "https://" + url;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || string.IsNullOrEmpty(uri.Host))
            {
                siteError.Text = "Please enter a web address like https://example.com";
                return;
            }
            var title = string.IsNullOrWhiteSpace(siteName.Text) ? uri.Host : siteName.Text.Trim();
            close(new WebsiteRequest(title, uri.ToString()));
        }, 320));

        var tabButtons = new List<ToggleButton>();
        void Select(int index)
        {
            for (var i = 0; i < tabButtons.Count; i++) tabButtons[i].IsChecked = i == index;
            contentHost.Content = index == 0 ? appsPanel : sitePanel;
        }
        tabButtons.Add(ViewKit.Option("Installed Apps", true, () => Select(0)));
        tabButtons.Add(ViewKit.Option("Website", false, () => Select(1)));
        foreach (var t in tabButtons) tabs.Children.Add(t);
        tabs.Children.Add(ViewKit.Pill("Program or Shortcut…", () => close("file"), 200, "SmallPill"));
        tabs.Children.Add(ViewKit.Pill("Folder…", () => close("folder"), 160, "SmallPill"));
        Select(0);

        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        footer.Children.Add(ViewKit.Pill("Cancel", () => close(null), 260));
        Grid.SetRow(footer, 3);
        root.Children.Add(footer);
        return ViewKit.Panel(root, 1560, new Thickness(60, 44, 60, 30));
    }

    private static async Task LoadIconAsync(AppHost host, Image image, string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return;
        var result = await host.Icons.GetAsync(source, 256);
        if (result is not null) image.Source = result.Image;
    }

    public static async Task EditChannelAsync(MainWindow window, AppHost host, Channel channel)
    {
        var name = channel.Title;
        var accent = channel.AccentColor;
        var pendingArt = channel.CustomArt;
        var runAsAdmin = channel.Launch?.RunAsAdministrator == true;

        var result = await window.ShowCustomDialogAsync(close =>
        {
            var root = new Grid();
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(470) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var left = new StackPanel { Margin = new Thickness(0, 0, 50, 0) };
            left.Children.Add(ViewKit.Text("Edit Channel", 48, FontWeights.ExtraBold));
            var preview = new ChannelTile { Width = MenuView.TileW, Height = MenuView.TileH, Margin = new Thickness(0, 30, 0, 24), HorizontalAlignment = HorizontalAlignment.Left };
            void Refresh() => preview.SetChannel(PreviewCopy(channel, name, accent, pendingArt), host, editMode: false);
            Refresh();
            left.Children.Add(preview);
            if (channel.Kind != ChannelKind.BuiltIn)
            {
                left.Children.Add(ViewKit.Pill("Change Picture…", () =>
                {
                    var dialog = new OpenFileDialog { Title = "Choose a picture", Filter = "Pictures|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|All files|*.*" };
                    if (dialog.ShowDialog(window) != true) return;
                    pendingArt = dialog.FileName;
                    Refresh();
                }, 380, "SmallPill"));
                left.Children.Add(ViewKit.Pill("Use App Icon", () =>
                {
                    pendingArt = null;
                    Refresh();
                }, 380, "SmallPill"));
            }
            root.Children.Add(left);

            var right = new StackPanel();
            Grid.SetColumn(right, 1);
            right.Children.Add(ViewKit.Text("Name", 30, FontWeights.Bold));
            var nameBox = new TextBox { Text = name, Margin = new Thickness(0, 8, 0, 24) };
            nameBox.TextChanged += (_, _) =>
            {
                name = nameBox.Text;
                Refresh();
            };
            right.Children.Add(nameBox);

            if (channel.Kind != ChannelKind.BuiltIn)
            {
                right.Children.Add(ViewKit.Text("Tile color", 30, FontWeights.Bold));
                var swatches = new WrapPanel { Margin = new Thickness(0, 8, 0, 20) };
                var auto = ViewKit.Pill("Auto", () =>
                {
                    accent = null;
                    Refresh();
                }, 120, "SmallPill");
                swatches.Children.Add(auto);
                foreach (var hex in Swatches)
                {
                    var color = (Color)ColorConverter.ConvertFromString(hex);
                    var swatch = new Button { Width = 64, Height = 64, MinWidth = 0, MinHeight = 0, Padding = new Thickness(0), Background = new SolidColorBrush(color), Content = "" };
                    swatch.SetResourceReference(FrameworkElement.StyleProperty, "RoundButton");
                    swatch.Margin = new Thickness(8);
                    var captured = hex;
                    swatch.Click += (_, _) =>
                    {
                        accent = captured;
                        Refresh();
                    };
                    swatches.Children.Add(swatch);
                }
                right.Children.Add(swatches);

                if (channel.Kind is ChannelKind.App or ChannelKind.System)
                {
                    right.Children.Add(ViewKit.Text("Run as administrator", 30, FontWeights.Bold));
                    var toggles = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 20) };
                    ToggleButton on = null!, off = null!;
                    on = ViewKit.Option("On", runAsAdmin, () => { runAsAdmin = true; on.IsChecked = true; off.IsChecked = false; });
                    off = ViewKit.Option("Off", !runAsAdmin, () => { runAsAdmin = false; on.IsChecked = false; off.IsChecked = true; });
                    toggles.Children.Add(on);
                    toggles.Children.Add(off);
                    right.Children.Add(toggles);
                }
                var target = channel.Launch?.ShortcutPath ?? channel.Launch?.Path ?? channel.Launch?.Uri ?? channel.Launch?.Aumid ?? "";
                right.Children.Add(ViewKit.Text("Starts: " + target, 22, FontWeights.Normal, "SubtleTextBrush"));
            }

            var buttons = new WrapPanel { Margin = new Thickness(0, 30, 0, 0) };
            buttons.Children.Add(ViewKit.Pill("Save", () => close("save"), 220));
            buttons.Children.Add(ViewKit.Pill("Remove Channel", () => close("remove"), 300));
            buttons.Children.Add(ViewKit.Pill("Cancel", () => close(null), 220));
            right.Children.Add(buttons);
            root.Children.Add(right);
            return ViewKit.Panel(root, 1580);
        });

        switch (result as string)
        {
            case "save":
                channel.Title = string.IsNullOrWhiteSpace(name) ? channel.Title : name.Trim();
                channel.AccentColor = accent;
                if (channel.Launch is not null) channel.Launch.RunAsAdministrator = runAsAdmin;
                if (!string.Equals(pendingArt, channel.CustomArt, StringComparison.OrdinalIgnoreCase))
                {
                    channel.CustomArt = pendingArt is null ? null : CopyArt(host, channel, pendingArt);
                }
                host.SaveLayout();
                break;
            case "remove":
                await ConfirmRemoveAsync(window, host, channel);
                break;
        }
    }

    private static string? CopyArt(AppHost host, Channel channel, string source)
    {
        try
        {
            Directory.CreateDirectory(host.Paths.ArtDirectory);
            var destination = Path.Combine(host.Paths.ArtDirectory, channel.Id + "-" + DateTime.Now.Ticks + Path.GetExtension(source));
            File.Copy(source, destination, true);
            if (channel.CustomArt is { } old && old.StartsWith(host.Paths.ArtDirectory, StringComparison.OrdinalIgnoreCase) && File.Exists(old)) File.Delete(old);
            return destination;
        }
        catch (Exception ex)
        {
            Log.Warn("Could not copy channel picture", ex);
            return channel.CustomArt;
        }
    }

    private static Channel PreviewCopy(Channel c, string name, string? accent, string? art) => new()
    {
        Id = c.Id,
        Kind = c.Kind,
        BuiltInId = c.BuiltInId,
        Title = name,
        Launch = c.Launch,
        SourceKey = c.SourceKey,
        IconSource = c.IconSource,
        BannerImage = c.BannerImage,
        CustomArt = art,
        AccentColor = accent,
    };

    public static async Task ConfirmRemoveAsync(MainWindow window, AppHost host, Channel channel)
    {
        var note = channel.Kind == ChannelKind.BuiltIn ? "You can add it back later from Add Channel." : "The app itself stays installed.";
        var choice = await window.ShowDialogAsync($"Remove \"{channel.Title}\" from your channels?\n{note}", "Remove", "Cancel");
        if (choice != "Remove") return;
        LayoutEditor.Remove(host.Layout.Layout, channel.Id);
        if (channel.CustomArt is { } art && art.StartsWith(host.Paths.ArtDirectory, StringComparison.OrdinalIgnoreCase))
        {
            try { File.Delete(art); } catch (IOException) { }
        }
        host.SaveLayout();
    }
}
