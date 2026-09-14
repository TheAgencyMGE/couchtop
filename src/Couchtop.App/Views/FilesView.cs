using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Couchtop.Core.Channels;
using Couchtop.Core.Diagnostics;

namespace Couchtop.App.Views;

/// <summary>The Files: a big, pointer-friendly file browser that works without Explorer.</summary>
public sealed class FilesView : UserControl, IScreenView
{
    private const int PageSize = 240;
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp", ".tif", ".tiff", ".heic", ".jfif" };

    private readonly AppHost _host;
    private readonly MainWindow _window;
    private readonly ListBox _list;
    private readonly TextBlock _subtitle;
    private readonly TextBlock _empty;
    private readonly Stack<string?> _history = new();
    private List<FileEntry> _entries = new();
    private string? _path;
    private int _shown;
    private int _loadVersion;

    private sealed record FileEntry(string Name, string FullPath, bool IsFolder, bool IsImage);

    public FilesView(AppHost host, MainWindow window, string? startPath = null)
    {
        _host = host;
        _window = window;

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var places = new StackPanel();
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var (label, path) in new (string, string?)[]
                 {
                     ("This PC", null),
                     ("Desktop", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)),
                     ("Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
                     ("Downloads", Path.Combine(profile, "Downloads")),
                     ("Pictures", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)),
                     ("Music", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)),
                     ("Videos", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)),
                 })
        {
            if (path is not null && !Directory.Exists(path)) continue;
            var captured = path;
            var button = ViewKit.Pill(label, () => Load(captured), 320);
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.Margin = new Thickness(0, 0, 0, 14);
            places.Children.Add(button);
        }
        body.Children.Add(ViewKit.Scroll(places));

        var right = new Grid();
        Grid.SetColumn(right, 2);
        _list = new ListBox { ItemsPanel = ViewKit.WrapPanelTemplate() };
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
        _list.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (ItemsControl.ContainerFromElement(_list, e.OriginalSource as DependencyObject) is ListBoxItem { Content: FrameworkElement { Tag: FileEntry entry } })
            {
                Open(entry);
                e.Handled = true;
            }
        };
        _list.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && _list.SelectedItem is FrameworkElement { Tag: FileEntry entry })
            {
                Open(entry);
                e.Handled = true;
            }
            else if (e.Key == Key.Back)
            {
                Up();
                e.Handled = true;
            }
        };
        right.Children.Add(_list);
        _empty = ViewKit.Text("", 34, FontWeights.Bold, "SubtleTextBrush", align: TextAlignment.Center);
        _empty.HorizontalAlignment = HorizontalAlignment.Center;
        _empty.VerticalAlignment = VerticalAlignment.Center;
        _empty.Visibility = Visibility.Collapsed;
        right.Children.Add(_empty);
        body.Children.Add(right);

        Content = ViewKit.Scaffold("Files", "This PC", body, window.ReturnToMenu, out _subtitle,
            ViewKit.Pill("Up", Up, 140),
            ViewKit.Pill("Open in Explorer", OpenInExplorer, 300),
            ViewKit.Pill("Add as Channel", AddAsChannel, 280));

        Load(startPath, push: false);
    }

    public void OnShown() { }
    public void OnHidden() { }

    public bool HandleBack()
    {
        if (_history.Count == 0) return false;
        Load(_history.Pop(), push: false);
        return true;
    }

    private async void Load(string? path, bool push = true)
    {
        if (push && !string.Equals(path, _path, StringComparison.OrdinalIgnoreCase)) _history.Push(_path);
        _path = path;
        var version = ++_loadVersion;
        _subtitle.Text = path ?? "This PC";
        _subtitle.Visibility = Visibility.Visible;
        _list.Items.Clear();
        _empty.Visibility = Visibility.Collapsed;

        try
        {
            _entries = await Task.Run(() => Enumerate(path));
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            if (version != _loadVersion) return;
            _empty.Text = "This folder can't be opened.";
            _empty.Visibility = Visibility.Visible;
            return;
        }
        if (version != _loadVersion) return;
        _shown = 0;
        ShowMore();
        if (_entries.Count == 0)
        {
            _empty.Text = "This folder is empty.";
            _empty.Visibility = Visibility.Visible;
        }
    }

    private static List<FileEntry> Enumerate(string? path)
    {
        if (path is null)
        {
            return DriveInfo.GetDrives().Where(d => d.IsReady).Select(d =>
            {
                string label;
                try { label = string.IsNullOrWhiteSpace(d.VolumeLabel) ? $"Drive ({d.Name.TrimEnd('\\')})" : $"{d.VolumeLabel} ({d.Name.TrimEnd('\\')})"; }
                catch (IOException) { label = d.Name; }
                return new FileEntry(label, d.RootDirectory.FullName, true, false);
            }).ToList();
        }

        var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System };
        var dir = new DirectoryInfo(path);
        var folders = dir.EnumerateDirectories("*", options).OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(d => new FileEntry(d.Name, d.FullName, true, false));
        var files = dir.EnumerateFiles("*", options).OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(f => new FileEntry(f.Name, f.FullName, false, ImageExtensions.Contains(f.Extension)));
        return folders.Concat(files).Take(20000).ToList();
    }

    private void ShowMore()
    {
        if (_list.Items.Count > 0 && _list.Items[^1] is Button) _list.Items.RemoveAt(_list.Items.Count - 1);
        foreach (var entry in _entries.Skip(_shown).Take(PageSize)) _list.Items.Add(BuildItem(entry));
        _shown = Math.Min(_entries.Count, _shown + PageSize);
        if (_shown < _entries.Count) _list.Items.Add(ViewKit.Pill($"Show more ({_entries.Count - _shown} left)", ShowMore, 400, "SmallPill"));
    }

    private FrameworkElement BuildItem(FileEntry entry)
    {
        var stack = new StackPanel { Width = 196, Tag = entry };
        var image = ViewKit.IconImage(112);
        image.Margin = new Thickness(0, 8, 0, 8);
        image.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(image);
        var name = ViewKit.Text(entry.Name, 22, FontWeights.SemiBold, align: TextAlignment.Center);
        name.MaxHeight = 60;
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        stack.Children.Add(name);
        _ = LoadIconAsync(image, entry);
        return stack;
    }

    private async Task LoadIconAsync(Image image, FileEntry entry)
    {
        var result = await _host.Icons.LoadUncachedAsync(entry.FullPath, 128, iconOnly: !entry.IsImage);
        if (result is not null) image.Source = result.Image;
    }

    private async void Open(FileEntry entry)
    {
        if (entry.IsFolder)
        {
            Load(entry.FullPath);
            return;
        }
        try
        {
            using var _ = Process.Start(new ProcessStartInfo(entry.FullPath) { UseShellExecute = true });
            _window.ShowToast($"Opening {entry.Name}…");
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not open {entry.FullPath}", ex);
            await _window.ShowDialogAsync($"\"{entry.Name}\" couldn't be opened.\n{ex.Message}", "OK");
        }
    }

    private void Up()
    {
        if (_path is null) return;
        Load(Directory.GetParent(_path)?.FullName);
    }

    private void OpenInExplorer()
    {
        try
        {
            var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            using var _ = Process.Start(new ProcessStartInfo(explorer, _path is null ? "shell:MyComputerFolder" : $"\"{_path}\"") { UseShellExecute = false });
        }
        catch (Exception ex)
        {
            Log.Warn("Could not open Explorer window", ex);
        }
    }

    private void AddAsChannel()
    {
        if (_path is null)
        {
            _window.ShowToast("Open a folder first");
            return;
        }
        var name = new DirectoryInfo(_path).Name;
        var channel = new Channel
        {
            Kind = ChannelKind.Folder,
            Title = string.IsNullOrWhiteSpace(name) ? _path : name,
            Launch = new LaunchSpec { Path = _path },
            IconSource = _path,
            SourceKey = "folder:" + _path.ToLowerInvariant(),
        };
        LayoutEditor.Place(_host.Layout.Layout, channel);
        _host.SaveLayout();
        _window.ShowToast($"\"{channel.Title}\" was added to your channels");
    }
}
