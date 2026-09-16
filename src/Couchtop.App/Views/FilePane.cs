using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Couchtop.Core.Diagnostics;
using Couchtop.Core.Files;

namespace Couchtop.App.Views;

/// <summary>One item in a folder listing.</summary>
public sealed record FileItem(string Name, string FullPath, bool IsFolder, long Size, DateTime Modified, bool IsImage, bool IsHidden)
{
    public string SizeText => IsFolder ? "" : FileFormat.Size(Size);
    public string ModifiedText => FileFormat.When(Modified);
}

public enum FileViewMode
{
    Grid,
    Details,
}

/// <summary>
/// One folder view: history, listing, selection, sorting and searching. Files shows one of these, or two
/// side by side when it is split, and every toolbar action works on whichever pane was last used.
/// </summary>
public sealed class FilePane : UserControl
{
    private const int PageSize = 300;
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp", ".tif", ".tiff", ".heic", ".jfif" };

    private readonly AppHost _host;
    private readonly ListBox _list = new() { SelectionMode = SelectionMode.Extended, Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
    private readonly TextBlock _status;
    private readonly TextBlock _empty;
    private readonly Border _frame;
    private readonly List<string> _back = new();
    private readonly List<string> _forward = new();
    private List<FileItem> _items = new();
    private string? _searchResultsFor;
    private int _shown;
    private int _version;
    private CancellationTokenSource? _search;

    public FilePane(AppHost host, string? startPath)
    {
        _host = host;

        _list.ItemsPanel = ViewKit.WrapPanelTemplate();
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
        _list.SelectionChanged += (_, _) => SelectionChanged?.Invoke();
        _list.PreviewMouseLeftButtonDown += (_, _) => Activated?.Invoke(this);
        _list.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (ItemsControl.ContainerFromElement(_list, e.OriginalSource as DependencyObject) is not ListBoxItem { Content: FrameworkElement { Tag: FileItem item } }) return;
            if (Keyboard.Modifiers is not (ModifierKeys.Control or ModifierKeys.Shift)) Open(item);
            e.Handled = Keyboard.Modifiers is ModifierKeys.None;
        };
        _list.KeyDown += OnKeyDown;

        _status = ViewKit.Text("", 20, FontWeights.Normal, "SubtleTextBrush", wrap: false);
        _empty = ViewKit.Text("", 28, FontWeights.Bold, "SubtleTextBrush", align: TextAlignment.Center);
        _empty.HorizontalAlignment = HorizontalAlignment.Center;
        _empty.VerticalAlignment = VerticalAlignment.Center;
        _empty.Visibility = Visibility.Collapsed;

        var body = new Grid();
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.Children.Add(_list);
        body.Children.Add(_empty);
        var statusBar = new Border { Child = _status, Padding = new Thickness(14, 6, 14, 6), BorderThickness = new Thickness(0, 2, 0, 0) };
        statusBar.SetResourceReference(Border.BorderBrushProperty, "PanelBorderBrush");
        Grid.SetRow(statusBar, 1);
        body.Children.Add(statusBar);

        _frame = new Border { Child = body, BorderThickness = new Thickness(4), CornerRadius = new CornerRadius(20), Padding = new Thickness(6) };
        _frame.SetResourceReference(Border.BorderBrushProperty, "PanelBorderBrush");
        Content = _frame;
        GotFocus += (_, _) => Activated?.Invoke(this);

        Load(startPath, remember: false);
    }

    public string? Path { get; private set; }
    public FileViewMode ViewMode { get; private set; } = FileViewMode.Grid;
    public bool ShowHidden { get; private set; }
    public string SortBy { get; private set; } = "name";
    public bool IsSearchResults => _searchResultsFor is not null;

    public event Action<FilePane>? Activated;
    public event Action? PathChanged;
    public event Action? SelectionChanged;

    public IReadOnlyList<FileItem> Selection => _list.SelectedItems.Cast<FrameworkElement>()
        .Select(e => e.Tag).OfType<FileItem>().ToList();

    public bool CanGoBack => _back.Count > 0;
    public bool CanGoForward => _forward.Count > 0;
    public bool CanGoUp => Path is not null;

    /// <summary>Marks this pane as the one the toolbar acts on.</summary>
    public void SetActive(bool active) =>
        _frame.SetResourceReference(Border.BorderBrushProperty, active ? "AccentBrush" : "PanelBorderBrush");

    // ---------------------------------------------------------------- navigation

    public void Load(string? path, bool remember = true)
    {
        if (remember && !string.Equals(path, Path, StringComparison.OrdinalIgnoreCase) && Path is not null)
        {
            _back.Add(Path);
            _forward.Clear();
        }
        _searchResultsFor = null;
        Path = path;
        PathChanged?.Invoke();
        _ = RefreshAsync();
    }

    public void Back()
    {
        if (_back.Count == 0) return;
        var target = _back[^1];
        _back.RemoveAt(_back.Count - 1);
        if (Path is not null) _forward.Add(Path);
        Load(target, remember: false);
    }

    public void Forward()
    {
        if (_forward.Count == 0) return;
        var target = _forward[^1];
        _forward.RemoveAt(_forward.Count - 1);
        if (Path is not null) _back.Add(Path);
        Load(target, remember: false);
    }

    public void Up()
    {
        if (Path is null) return;
        Load(Directory.GetParent(Path)?.FullName);
    }

    public void Open(FileItem item)
    {
        if (item.IsFolder)
        {
            Load(item.FullPath);
            return;
        }
        try
        {
            using var _ = Process.Start(new ProcessStartInfo(item.FullPath) { UseShellExecute = true });
            OpenedFile?.Invoke(item);
        }
        catch (Exception ex)
        {
            Log.Warn("Could not open " + item.FullPath, ex);
            OpenFailed?.Invoke(item, ex);
        }
    }

    public event Action<FileItem>? OpenedFile;
    public event Action<FileItem, Exception>? OpenFailed;

    // ---------------------------------------------------------------- listing

    public async Task RefreshAsync()
    {
        var version = ++_version;
        _list.Items.Clear();
        _empty.Visibility = Visibility.Collapsed;
        _status.Text = "Loading…";

        List<FileItem> items;
        try
        {
            var path = Path;
            var hidden = ShowHidden;
            items = await Task.Run(() => Enumerate(path, hidden));
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            if (version != _version) return;
            ShowEmpty("This folder can't be opened.");
            return;
        }
        if (version != _version) return;

        _items = Sort(items);
        _shown = 0;
        ShowMore();
        if (_items.Count == 0) ShowEmpty(Path is null ? "No drives found." : "This folder is empty.");
        UpdateStatus();
    }

    private void ShowEmpty(string text)
    {
        _empty.Text = text;
        _empty.Visibility = Visibility.Visible;
        _status.Text = "";
    }

    private static List<FileItem> Enumerate(string? path, bool showHidden)
    {
        if (path is null)
        {
            return Places.Drives()
                .Select(d => new FileItem(d.Name, d.Path!, true, 0, Directory.GetLastWriteTime(d.Path!), false, false))
                .ToList();
        }

        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            AttributesToSkip = showHidden ? FileAttributes.System : FileAttributes.Hidden | FileAttributes.System,
        };
        var dir = new DirectoryInfo(path);
        var items = new List<FileItem>();
        foreach (var folder in dir.EnumerateDirectories("*", options))
            items.Add(new FileItem(folder.Name, folder.FullName, true, 0, folder.LastWriteTime, false, folder.Attributes.HasFlag(FileAttributes.Hidden)));
        foreach (var file in dir.EnumerateFiles("*", options))
            items.Add(new FileItem(file.Name, file.FullName, false, file.Length, file.LastWriteTime, ImageExtensions.Contains(file.Extension), file.Attributes.HasFlag(FileAttributes.Hidden)));
        return items.Take(30000).ToList();
    }

    private List<FileItem> Sort(List<FileItem> items)
    {
        IEnumerable<FileItem> sorted = SortBy switch
        {
            "size" => items.OrderByDescending(i => i.IsFolder).ThenByDescending(i => i.Size),
            "modified" => items.OrderByDescending(i => i.IsFolder).ThenByDescending(i => i.Modified),
            "type" => items.OrderByDescending(i => i.IsFolder).ThenBy(i => System.IO.Path.GetExtension(i.Name), StringComparer.CurrentCultureIgnoreCase).ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => items.OrderByDescending(i => i.IsFolder).ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase),
        };
        return sorted.ToList();
    }

    public void SetSort(string sortBy)
    {
        SortBy = sortBy;
        _items = Sort(_items);
        _shown = 0;
        _list.Items.Clear();
        ShowMore();
    }

    public void SetViewMode(FileViewMode mode)
    {
        ViewMode = mode;
        _list.ItemsPanel = mode == FileViewMode.Grid ? ViewKit.WrapPanelTemplate() : new ItemsPanelTemplate(new FrameworkElementFactory(typeof(StackPanel)));
        _shown = 0;
        _list.Items.Clear();
        ShowMore();
    }

    public void SetShowHidden(bool show)
    {
        ShowHidden = show;
        _ = RefreshAsync();
    }

    private void ShowMore()
    {
        if (_list.Items.Count > 0 && _list.Items[^1] is Button) _list.Items.RemoveAt(_list.Items.Count - 1);
        foreach (var item in _items.Skip(_shown).Take(PageSize)) _list.Items.Add(BuildItem(item));
        _shown = Math.Min(_items.Count, _shown + PageSize);
        if (_shown < _items.Count) _list.Items.Add(ViewKit.Pill($"Show more ({_items.Count - _shown} left)", ShowMore, 360, "SmallPill"));
    }

    private FrameworkElement BuildItem(FileItem item)
    {
        if (ViewMode == FileViewMode.Details)
        {
            var row = new Grid { Tag = item, Height = 46, Margin = new Thickness(2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });

            var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var smallIcon = ViewKit.IconImage(30);
            smallIcon.Margin = new Thickness(6, 0, 14, 0);
            left.Children.Add(smallIcon);
            var rowName = ViewKit.Text(item.Name, 22, FontWeights.SemiBold, wrap: false);
            rowName.TextTrimming = TextTrimming.CharacterEllipsis;
            rowName.VerticalAlignment = VerticalAlignment.Center;
            if (item.IsHidden) rowName.Opacity = 0.55;
            left.Children.Add(rowName);
            row.Children.Add(left);

            var size = ViewKit.Text(item.SizeText, 20, FontWeights.Normal, "SubtleTextBrush", wrap: false, align: TextAlignment.Right);
            size.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(size, 1);
            row.Children.Add(size);

            var modified = ViewKit.Text(item.ModifiedText, 20, FontWeights.Normal, "SubtleTextBrush", wrap: false, align: TextAlignment.Right);
            modified.VerticalAlignment = VerticalAlignment.Center;
            modified.Margin = new Thickness(0, 0, 14, 0);
            Grid.SetColumn(modified, 2);
            row.Children.Add(modified);
            _ = LoadIconAsync(smallIcon, item, 32);
            return row;
        }

        var stack = new StackPanel { Width = 186, Tag = item, Margin = new Thickness(2) };
        var image = ViewKit.IconImage(104);
        image.Margin = new Thickness(0, 8, 0, 8);
        image.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(image);
        var name = ViewKit.Text(item.Name, 21, FontWeights.SemiBold, align: TextAlignment.Center);
        name.MaxHeight = 56;
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        if (item.IsHidden) name.Opacity = 0.55;
        stack.Children.Add(name);
        if (IsSearchResults)
        {
            var folder = ViewKit.Text(System.IO.Path.GetDirectoryName(item.FullPath) ?? "", 16, FontWeights.Normal, "SubtleTextBrush", wrap: false, align: TextAlignment.Center);
            folder.TextTrimming = TextTrimming.CharacterEllipsis;
            stack.Children.Add(folder);
        }
        _ = LoadIconAsync(image, item, 128);
        return stack;
    }

    private async Task LoadIconAsync(Image image, FileItem item, int size)
    {
        var result = await _host.Icons.LoadUncachedAsync(item.FullPath, size, iconOnly: !item.IsImage);
        if (result is not null) image.Source = result.Image;
    }

    private void UpdateStatus()
    {
        var folders = _items.Count(i => i.IsFolder);
        var files = _items.Count - folders;
        var selected = Selection.Count;
        var size = Selection.Where(s => !s.IsFolder).Sum(s => s.Size);
        _status.Text = _searchResultsFor is { } query
            ? $"{_items.Count} results for \"{query}\""
            : selected > 0
                ? $"{selected} selected{(size > 0 ? "  ·  " + FileFormat.Size(size) : "")}  ·  {folders} folders, {files} files"
                : $"{folders} folders, {files} files";
    }

    public void RefreshStatus() => UpdateStatus();

    public void SelectAll() => _list.SelectAll();

    // ---------------------------------------------------------------- search

    /// <summary>Searches the current folder and everything under it, filling the pane with the matches.</summary>
    public async Task SearchAsync(string query)
    {
        _search?.Cancel();
        if (string.IsNullOrWhiteSpace(query) || Path is null)
        {
            await RefreshAsync();
            return;
        }

        var cts = new CancellationTokenSource();
        _search = cts;
        var version = ++_version;
        _list.Items.Clear();
        _empty.Visibility = Visibility.Collapsed;
        _status.Text = $"Searching for \"{query}\"…";
        var root = Path;

        try
        {
            var results = await Task.Run(() => Find(root, query, cts.Token), cts.Token);
            if (version != _version) return;
            _searchResultsFor = query;
            _items = results;
            _shown = 0;
            ShowMore();
            if (_items.Count == 0) ShowEmpty($"Nothing matching \"{query}\".");
            UpdateStatus();
        }
        catch (OperationCanceledException)
        {
            // Replaced by a newer search.
        }
    }

    private static List<FileItem> Find(string root, string query, CancellationToken token)
    {
        var options = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true, AttributesToSkip = FileAttributes.System };
        var results = new List<FileItem>();
        foreach (var path in Directory.EnumerateFileSystemEntries(root, "*", options))
        {
            token.ThrowIfCancellationRequested();
            if (results.Count >= 600) break;
            var name = System.IO.Path.GetFileName(path);
            if (Core.Search.Fuzzy.Score(name, query) < 0) continue;
            try
            {
                var isFolder = Directory.Exists(path);
                var info = new FileInfo(path);
                results.Add(new FileItem(name, path, isFolder, isFolder ? 0 : info.Length, info.LastWriteTime,
                    ImageExtensions.Contains(info.Extension), info.Attributes.HasFlag(FileAttributes.Hidden)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The file went away mid-search.
            }
        }
        return results
            .OrderByDescending(r => Core.Search.Fuzzy.Score(r.Name, query))
            .ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Selection.Count == 1)
        {
            Open(Selection[0]);
            e.Handled = true;
        }
        else if (e.Key == Key.Back)
        {
            Up();
            e.Handled = true;
        }
    }
}
