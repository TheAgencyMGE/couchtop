using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Couchtop.Core.Channels;
using Couchtop.Core.Diagnostics;
using Couchtop.Core.Files;
using Couchtop.Core.Platform;

namespace Couchtop.App.Views;

/// <summary>
/// Files: a full file manager that works without Explorer. Tabs and an optional second pane, the places
/// sidebar with drives and removable media, search, and the usual operations (copy, move, rename, delete to
/// the Recycle Bin, zip and unzip, properties).
/// </summary>
public sealed class FilesView : UserControl, IScreenView
{
    private readonly AppHost _host;
    private readonly MainWindow _window;
    private readonly Grid _panes = new();
    private readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _places = new();
    private readonly WrapPanel _toolbar = new();
    private readonly TextBox _search = new() { Width = 320, FontSize = 24, VerticalContentAlignment = VerticalAlignment.Center, Height = 52, Margin = new Thickness(6) };
    private readonly TextBlock _subtitle;
    private readonly List<string?> _tabPaths = new();
    private readonly List<FilePane> _panesList = new();
    private FilePane _active = null!;
    private FilePane? _second;
    private FileClipboard? _clipboard;
    private int _tabIndex;

    public FilesView(AppHost host, MainWindow window, string? startPath = null)
    {
        _host = host;
        _window = window;

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.Children.Add(ViewKit.Scroll(_places));

        var right = new Grid();
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(right, 2);

        var tabRow = new StackPanel { Orientation = Orientation.Horizontal };
        tabRow.Children.Add(_tabs);
        var addTab = ViewKit.Pill("+", () => AddTab(_active.Path), 70, "SmallPill");
        addTab.ToolTip = "New tab (Ctrl+T)";
        tabRow.Children.Add(addTab);
        right.Children.Add(tabRow);

        Grid.SetRow(_toolbar, 1);
        right.Children.Add(_toolbar);

        Grid.SetRow(_panes, 2);
        right.Children.Add(_panes);
        body.Children.Add(right);

        _active = NewPane(startPath);
        _panesList.Add(_active);
        _panes.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _panes.Children.Add(_active);
        _active.SetActive(true);
        _tabPaths.Add(startPath);

        _search.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                await _active.SearchAsync(_search.Text);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                _search.Text = "";
                await _active.RefreshAsync();
                e.Handled = true;
            }
        };

        BuildPlaces();
        BuildToolbar();
        BuildTabs();

        Content = ViewKit.Scaffold("Files", startPath ?? "This PC", body, window.ReturnToMenu, out _subtitle,
            ViewKit.Pill("Split", ToggleSplit, 150),
            ViewKit.Pill("Open in Explorer", OpenInExplorer, 300),
            ViewKit.Pill("Add as Channel", AddAsChannel, 280));

        PreviewKeyDown += OnKeyDown;
    }

    private FilePane NewPane(string? path)
    {
        var pane = new FilePane(_host, path);
        pane.Activated += p =>
        {
            _active = p;
            foreach (var other in _panesList) other.SetActive(ReferenceEquals(other, p));
            UpdateHeader();
        };
        pane.PathChanged += () =>
        {
            if (ReferenceEquals(pane, _panesList.FirstOrDefault()) && _tabIndex < _tabPaths.Count) _tabPaths[_tabIndex] = pane.Path;
            BuildTabs();
            UpdateHeader();
        };
        pane.SelectionChanged += () =>
        {
            pane.RefreshStatus();
            UpdateToolbarState();
        };
        pane.OpenedFile += item => _window.ShowToast($"Opening {item.Name}…");
        pane.OpenFailed += async (item, ex) => await _window.ShowDialogAsync($"\"{item.Name}\" couldn't be opened.\n{ex.Message}", "OK");
        return pane;
    }

    public void OnShown() { }
    public void OnHidden() { }

    public bool HandleBack()
    {
        if (!_active.CanGoBack) return false;
        _active.Back();
        return true;
    }

    private void UpdateHeader()
    {
        _host.Settings.Current.LastFolder = _active.Path;
        _subtitle.Text = _active.Path ?? "This PC";
        _subtitle.Visibility = Visibility.Visible;
        UpdateToolbarState();
    }

    // ---------------------------------------------------------------- places

    private void BuildPlaces()
    {
        _places.Children.Clear();
        AddPlace("This PC", null);
        foreach (var place in Places.UserFolders()) AddPlace(place.Name, place.Path);

        var drivesLabel = ViewKit.Text("Drives", 22, FontWeights.ExtraBold, "SubtleTextBrush", wrap: false);
        drivesLabel.Margin = new Thickness(12, 18, 0, 8);
        _places.Children.Add(drivesLabel);
        foreach (var drive in Places.Drives()) AddPlace(drive.Name, drive.Path, drive.Detail);

        var otherLabel = ViewKit.Text("Other", 22, FontWeights.ExtraBold, "SubtleTextBrush", wrap: false);
        otherLabel.Margin = new Thickness(12, 18, 0, 8);
        _places.Children.Add(otherLabel);
        AddAction("Recycle Bin", OpenRecycleBin);
        AddAction("Network folder…", ConnectNetworkFolder);
        AddAction("Refresh drives", BuildPlaces);
    }

    private void AddPlace(string label, string? path, string? detail = null)
    {
        var stack = new StackPanel();
        stack.Children.Add(ViewKit.Text(label, 26, FontWeights.Bold, "ButtonTextBrush", wrap: false));
        if (detail is not null) stack.Children.Add(ViewKit.Text(detail, 17, FontWeights.Normal, "SubtleTextBrush", wrap: false));
        var button = new Button { Content = stack, HorizontalContentAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 10), MinHeight = 74, Padding = new Thickness(24, 6, 16, 8) };
        button.SetResourceReference(StyleProperty, "PillButton");
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.Click += (_, _) => _active.Load(path);
        _places.Children.Add(button);
    }

    private void AddAction(string label, Action action)
    {
        var button = ViewKit.Pill(label, action, 300, "SmallPill");
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.HorizontalContentAlignment = HorizontalAlignment.Left;
        button.Margin = new Thickness(0, 0, 0, 8);
        _places.Children.Add(button);
    }

    private void OpenRecycleBin()
    {
        // The Recycle Bin is a shell folder rather than a real directory, so Windows opens it.
        try
        {
            using var _ = Process.Start(new ProcessStartInfo("shell:RecycleBinFolder") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn("Could not open the Recycle Bin", ex);
            _window.ShowToast("The Recycle Bin couldn't be opened");
        }
    }

    private async void ConnectNetworkFolder()
    {
        var path = await AskAsync("Network folder", @"\\server\share", "");
        if (string.IsNullOrWhiteSpace(path)) return;
        if (!Directory.Exists(path))
        {
            await _window.ShowDialogAsync($"\"{path}\" can't be reached.\nCheck the address and that you are signed in to it.", "OK");
            return;
        }
        _active.Load(path);
    }

    // ---------------------------------------------------------------- tabs

    private void BuildTabs()
    {
        _tabs.Children.Clear();
        for (var i = 0; i < _tabPaths.Count; i++)
        {
            var index = i;
            var name = Label(_tabPaths[i]);
            var chip = new ToggleButton { Content = name, IsChecked = i == _tabIndex, MinWidth = 0, Height = 52, Padding = new Thickness(22, 0, 22, 0), Margin = new Thickness(0, 0, 6, 0), FontSize = 22 };
            chip.SetResourceReference(StyleProperty, "OptionPill");
            chip.Click += (_, _) => SelectTab(index);
            if (_tabPaths.Count > 1)
            {
                chip.MouseRightButtonUp += (_, _) => CloseTab(index);
                chip.ToolTip = name + "  (right-click to close)";
            }
            _tabs.Children.Add(chip);
        }
    }

    private static string Label(string? path)
    {
        if (path is null) return "This PC";
        var name = new DirectoryInfo(path).Name;
        return string.IsNullOrWhiteSpace(name) ? path : name;
    }

    private void AddTab(string? path)
    {
        _tabPaths.Add(path);
        SelectTab(_tabPaths.Count - 1);
    }

    private void SelectTab(int index)
    {
        if (index < 0 || index >= _tabPaths.Count) return;
        _tabIndex = index;
        _panesList[0].Load(_tabPaths[index], remember: false);
        _active = _panesList[0];
        foreach (var pane in _panesList) pane.SetActive(ReferenceEquals(pane, _active));
        BuildTabs();
        UpdateHeader();
    }

    private void CloseTab(int index)
    {
        if (_tabPaths.Count <= 1) return;
        _tabPaths.RemoveAt(index);
        SelectTab(Math.Min(_tabIndex, _tabPaths.Count - 1));
    }

    // ---------------------------------------------------------------- split

    private void ToggleSplit()
    {
        if (_second is null)
        {
            _panes.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            _panes.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var splitter = new GridSplitter { Width = 10, HorizontalAlignment = HorizontalAlignment.Stretch, Background = Brushes.Transparent };
            Grid.SetColumn(splitter, 1);
            _panes.Children.Add(splitter);

            _second = NewPane(_active.Path);
            _panesList.Add(_second);
            Grid.SetColumn(_second, 2);
            _panes.Children.Add(_second);
            _window.ShowToast("Split view on: copy between the two sides");
        }
        else
        {
            _panes.Children.Remove(_second);
            _panesList.Remove(_second);
            _second = null;
            while (_panes.ColumnDefinitions.Count > 1) _panes.ColumnDefinitions.RemoveAt(_panes.ColumnDefinitions.Count - 1);
            for (var i = _panes.Children.Count - 1; i >= 0; i--)
                if (_panes.Children[i] is GridSplitter) _panes.Children.RemoveAt(i);
            _active = _panesList[0];
        }
        foreach (var pane in _panesList) pane.SetActive(ReferenceEquals(pane, _active));
    }

    // ---------------------------------------------------------------- toolbar

    private readonly Dictionary<string, ButtonBase> _tools = new();

    private void BuildToolbar()
    {
        _toolbar.Children.Clear();
        _tools.Clear();
        Tool("Back", () => _active.Back());
        Tool("Forward", () => _active.Forward());
        Tool("Up", () => _active.Up());
        Tool("Refresh", () => _ = _active.RefreshAsync());
        Tool("New Folder", NewFolder);
        Tool("Rename", Rename);
        Tool("Copy", () => SetClipboard(ClipboardMode.Copy));
        Tool("Cut", () => SetClipboard(ClipboardMode.Move));
        Tool("Paste", Paste);
        Tool("Delete", Delete);
        Tool("Zip", Compress);
        Tool("Extract", Extract);
        Tool("Properties", ShowProperties);

        var view = ViewKit.Pill("Details view", () =>
        {
            var next = _active.ViewMode == FileViewMode.Grid ? FileViewMode.Details : FileViewMode.Grid;
            foreach (var pane in _panesList) pane.SetViewMode(next);
            BuildToolbar();
        }, 220, "SmallPill");
        view.Content = _active.ViewMode == FileViewMode.Grid ? "Details view" : "Grid view";
        _toolbar.Children.Add(view);

        var hidden = ViewKit.Pill(_active.ShowHidden ? "Hide hidden" : "Show hidden", () =>
        {
            var show = !_active.ShowHidden;
            foreach (var pane in _panesList) pane.SetShowHidden(show);
            BuildToolbar();
        }, 230, "SmallPill");
        _toolbar.Children.Add(hidden);

        var sort = ViewKit.Pill("Sort: " + _active.SortBy, () =>
        {
            var order = new[] { "name", "size", "modified", "type" };
            var next = order[(Array.IndexOf(order, _active.SortBy) + 1) % order.Length];
            foreach (var pane in _panesList) pane.SetSort(next);
            BuildToolbar();
        }, 240, "SmallPill");
        _toolbar.Children.Add(sort);

        var searchLabel = ViewKit.Text("Search", 22, FontWeights.Bold, wrap: false);
        searchLabel.VerticalAlignment = VerticalAlignment.Center;
        searchLabel.Margin = new Thickness(18, 0, 10, 0);
        var searchGroup = new StackPanel { Orientation = Orientation.Horizontal };
        searchGroup.Children.Add(searchLabel);
        // The search box outlives each rebuild, so it has to leave the previous toolbar row first.
        if (_search.Parent is Panel previous) previous.Children.Remove(_search);
        searchGroup.Children.Add(_search);
        _toolbar.Children.Add(searchGroup);
        UpdateToolbarState();
    }

    private void Tool(string label, Action action)
    {
        var button = ViewKit.Pill(label, action, 0, "SmallPill");
        button.MinWidth = 0;
        _tools[label] = button;
        _toolbar.Children.Add(button);
    }

    private void UpdateToolbarState()
    {
        var selection = _active.Selection;
        void Enable(string key, bool on)
        {
            if (_tools.TryGetValue(key, out var button)) button.IsEnabled = on;
        }
        Enable("Back", _active.CanGoBack);
        Enable("Forward", _active.CanGoForward);
        Enable("Up", _active.CanGoUp);
        Enable("Rename", selection.Count == 1 && _active.Path is not null);
        Enable("Copy", selection.Count > 0);
        Enable("Cut", selection.Count > 0 && _active.Path is not null);
        Enable("Paste", _clipboard is not null && _active.Path is not null);
        Enable("Delete", selection.Count > 0 && _active.Path is not null);
        Enable("Zip", selection.Count > 0 && _active.Path is not null);
        Enable("Extract", selection.Count == 1 && Archives.IsArchive(selection[0].FullPath));
        Enable("Properties", selection.Count == 1);
        Enable("New Folder", _active.Path is not null);
    }

    // ---------------------------------------------------------------- operations

    private void SetClipboard(ClipboardMode mode)
    {
        var paths = _active.Selection.Select(s => s.FullPath).ToList();
        if (paths.Count == 0) return;
        _clipboard = new FileClipboard(mode, paths);
        _window.ShowToast($"{paths.Count} item{(paths.Count == 1 ? "" : "s")} ready to {(mode == ClipboardMode.Copy ? "copy" : "move")}");
        UpdateToolbarState();
    }

    private async void Paste()
    {
        if (_clipboard is not { } clipboard || _active.Path is not { } destination) return;
        var moving = clipboard.Mode == ClipboardMode.Move;
        var done = 0;
        var failures = new List<string>();

        foreach (var source in clipboard.Paths)
        {
            var isFolder = Directory.Exists(source);
            if (isFolder && FileOperations.IsInsideItself(source, destination))
            {
                failures.Add($"{Path.GetFileName(source)}: a folder can't be copied into itself");
                continue;
            }
            var name = FileOperations.UniqueName(Path.GetFileName(source), candidate => File.Exists(Path.Combine(destination, candidate)) || Directory.Exists(Path.Combine(destination, candidate)));
            var target = Path.Combine(destination, name);
            try
            {
                await Task.Run(() =>
                {
                    if (isFolder)
                    {
                        if (moving && SameVolume(source, target)) Directory.Move(source, target);
                        else
                        {
                            FileOperations.CopyDirectory(source, target, CancellationToken.None);
                            if (moving) Directory.Delete(source, true);
                        }
                    }
                    else if (moving)
                    {
                        File.Move(source, target);
                    }
                    else
                    {
                        File.Copy(source, target);
                    }
                });
                done++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failures.Add($"{Path.GetFileName(source)}: {ex.Message}");
            }
        }

        if (moving) _clipboard = null;
        await _active.RefreshAsync();
        if (_second is not null) await _second.RefreshAsync();
        if (failures.Count > 0) await _window.ShowDialogAsync($"{done} item(s) done.\n\nCouldn't finish:\n" + string.Join('\n', failures.Take(6)), "OK");
        else _window.ShowToast($"{done} item{(done == 1 ? "" : "s")} {(moving ? "moved" : "copied")}");
        UpdateToolbarState();
    }

    private static bool SameVolume(string a, string b) =>
        string.Equals(Path.GetPathRoot(Path.GetFullPath(a)), Path.GetPathRoot(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);

    private async void Delete()
    {
        var selection = _active.Selection;
        if (selection.Count == 0) return;
        var what = selection.Count == 1 ? $"\"{selection[0].Name}\"" : $"{selection.Count} items";
        if (await _window.ShowDialogAsync($"Move {what} to the Recycle Bin?", "Delete", "Cancel") != "Delete") return;

        var paths = selection.Select(s => s.FullPath).ToList();
        var ok = await Task.Run(() => RecycleBin.Delete(paths, _window.Handle));
        await _active.RefreshAsync();
        if (_second is not null) await _second.RefreshAsync();
        _window.ShowToast(ok ? $"{what} moved to the Recycle Bin" : "Nothing was deleted");
    }

    private async void NewFolder()
    {
        if (_active.Path is not { } folder) return;
        var name = await AskAsync("New folder", "Folder name", "New folder");
        if (string.IsNullOrWhiteSpace(name)) return;
        if (!FileOperations.IsValidName(name))
        {
            await _window.ShowDialogAsync($"\"{name}\" can't be used as a folder name.", "OK");
            return;
        }
        try
        {
            Directory.CreateDirectory(Path.Combine(folder, FileOperations.UniqueName(name, c => Directory.Exists(Path.Combine(folder, c)))));
            await _active.RefreshAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _window.ShowDialogAsync("The folder couldn't be created.\n" + ex.Message, "OK");
        }
    }

    private async void Rename()
    {
        var selection = _active.Selection;
        if (selection.Count != 1 || _active.Path is not { } folder) return;
        var item = selection[0];
        var name = await AskAsync("Rename", "New name", item.Name);
        if (string.IsNullOrWhiteSpace(name) || name == item.Name) return;
        if (!FileOperations.IsValidName(name))
        {
            await _window.ShowDialogAsync($"\"{name}\" can't be used as a name.", "OK");
            return;
        }
        try
        {
            var target = Path.Combine(folder, name);
            if (item.IsFolder) Directory.Move(item.FullPath, target);
            else File.Move(item.FullPath, target);
            await _active.RefreshAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _window.ShowDialogAsync("It couldn't be renamed.\n" + ex.Message, "OK");
        }
    }

    private async void Compress()
    {
        var selection = _active.Selection;
        if (selection.Count == 0 || _active.Path is not { } folder) return;
        _window.ShowToast("Creating the zip file…");
        try
        {
            var paths = selection.Select(s => s.FullPath).ToList();
            var archive = await Task.Run(() => Archives.Compress(paths, folder, File.Exists));
            await _active.RefreshAsync();
            _window.ShowToast($"{Path.GetFileName(archive)} created");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _window.ShowDialogAsync("The zip file couldn't be created.\n" + ex.Message, "OK");
        }
    }

    private async void Extract()
    {
        var selection = _active.Selection;
        if (selection.Count != 1 || !Archives.IsArchive(selection[0].FullPath)) return;
        _window.ShowToast("Extracting…");
        try
        {
            var archive = selection[0].FullPath;
            var target = await Task.Run(() => Archives.ExtractHere(archive, p => File.Exists(p) || Directory.Exists(p)));
            await _active.RefreshAsync();
            _window.ShowToast($"Extracted to {Path.GetFileName(target)}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            await _window.ShowDialogAsync("The archive couldn't be extracted.\n" + ex.Message, "OK");
        }
    }

    private async void ShowProperties()
    {
        var selection = _active.Selection;
        if (selection.Count != 1) return;
        var path = selection[0].FullPath;
        ItemDetails details;
        try
        {
            details = await Task.Run(() => ItemInspector.Read(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _window.ShowDialogAsync("Those details can't be read.\n" + ex.Message, "OK");
            return;
        }

        await _window.ShowCustomDialogAsync(close =>
        {
            var stack = new StackPanel { Width = 900 };
            stack.Children.Add(ViewKit.Text(details.Name, 44, FontWeights.ExtraBold, "AccentDeepBrush"));
            stack.Children.Add(ViewKit.Text(details.Path, 22, FontWeights.Normal, "SubtleTextBrush"));

            void Row(string label, string? value)
            {
                if (string.IsNullOrWhiteSpace(value)) return;
                var row = new Grid { Margin = new Thickness(0, 10, 0, 0) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.Children.Add(ViewKit.Text(label, 24, FontWeights.Bold, "SubtleTextBrush", wrap: false));
                var text = ViewKit.Text(value!, 24, FontWeights.Normal);
                Grid.SetColumn(text, 1);
                row.Children.Add(text);
                stack.Children.Add(row);
            }

            Row("Type", details.IsFolder ? "Folder" : (Path.GetExtension(details.Path).TrimStart('.').ToUpperInvariant() + " file").Trim());
            Row("Size", details.IsFolder ? $"{FileFormat.Size(details.Size)}  ·  {details.Items} files" : FileFormat.Size(details.Size));
            Row("Created", details.Created.ToString("f"));
            Row("Modified", details.Modified.ToString("f"));
            Row("Owner", details.Owner);
            Row("Shortcut to", details.Target);
            Row("Attributes", Describe(details.Attributes));

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 26, 0, 0) };
            buttons.Children.Add(ViewKit.Pill("Read-only", () =>
            {
                Toggle(details.Path, FileAttributes.ReadOnly);
                close(null);
            }, 240, "SmallPill"));
            buttons.Children.Add(ViewKit.Pill("Close", () => close(null), 200));
            stack.Children.Add(buttons);
            return ViewKit.Panel(stack, 1000);
        });
    }

    private static string Describe(FileAttributes attributes)
    {
        var names = new List<string>();
        if (attributes.HasFlag(FileAttributes.ReadOnly)) names.Add("Read-only");
        if (attributes.HasFlag(FileAttributes.Hidden)) names.Add("Hidden");
        if (attributes.HasFlag(FileAttributes.System)) names.Add("System");
        if (attributes.HasFlag(FileAttributes.Archive)) names.Add("Archive");
        if (attributes.HasFlag(FileAttributes.ReparsePoint)) names.Add("Link");
        if (attributes.HasFlag(FileAttributes.Compressed)) names.Add("Compressed");
        if (attributes.HasFlag(FileAttributes.Encrypted)) names.Add("Encrypted");
        return names.Count == 0 ? "Normal" : string.Join(", ", names);
    }

    private void Toggle(string path, FileAttributes attribute)
    {
        try
        {
            var current = File.GetAttributes(path);
            File.SetAttributes(path, current.HasFlag(attribute) ? current & ~attribute : current | attribute);
            _ = _active.RefreshAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _window.ShowToast("That attribute couldn't be changed");
        }
    }

    /// <summary>A console-style text prompt (new folder, rename, network address).</summary>
    private async Task<string?> AskAsync(string title, string label, string initial)
    {
        var result = await _window.ShowCustomDialogAsync(close =>
        {
            var stack = new StackPanel { Width = 760 };
            stack.Children.Add(ViewKit.Text(title, 44, FontWeights.ExtraBold, "AccentDeepBrush"));
            stack.Children.Add(ViewKit.Text(label, 24, FontWeights.Normal, "SubtleTextBrush"));
            var box = new TextBox { Text = initial, FontSize = 30, Height = 64, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 14, 0, 0) };
            box.Loaded += (_, _) =>
            {
                box.Focus();
                box.SelectAll();
            };
            box.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) close(box.Text);
                else if (e.Key == Key.Escape) close(null);
            };
            stack.Children.Add(box);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 22, 0, 0) };
            buttons.Children.Add(ViewKit.Pill("Cancel", () => close(null), 200));
            buttons.Children.Add(ViewKit.Pill("OK", () => close(box.Text), 200));
            stack.Children.Add(buttons);
            return ViewKit.Panel(stack, 860);
        });
        return result as string;
    }

    // ---------------------------------------------------------------- misc

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        switch (e.Key)
        {
            case Key.F2: Rename(); break;
            case Key.F5: _ = _active.RefreshAsync(); break;
            case Key.Delete: Delete(); break;
            case Key.C when ctrl: SetClipboard(ClipboardMode.Copy); break;
            case Key.X when ctrl: SetClipboard(ClipboardMode.Move); break;
            case Key.V when ctrl: Paste(); break;
            case Key.A when ctrl: _active.SelectAll(); break;
            case Key.T when ctrl: AddTab(_active.Path); break;
            case Key.W when ctrl: CloseTab(_tabIndex); break;
            case Key.F when ctrl: _search.Focus(); break;
            case Key.N when ctrl: NewFolder(); break;
            default: return;
        }
        e.Handled = true;
    }

    private void OpenInExplorer()
    {
        try
        {
            var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            using var _ = Process.Start(new ProcessStartInfo(explorer, _active.Path is null ? "shell:MyComputerFolder" : $"\"{_active.Path}\"") { UseShellExecute = false });
        }
        catch (Exception ex)
        {
            Log.Warn("Could not open an Explorer window", ex);
        }
    }

    private void AddAsChannel()
    {
        if (_active.Path is not { } path)
        {
            _window.ShowToast("Open a folder first");
            return;
        }
        var name = new DirectoryInfo(path).Name;
        var channel = new Channel
        {
            Kind = ChannelKind.Folder,
            Title = string.IsNullOrWhiteSpace(name) ? path : name,
            Launch = new LaunchSpec { Path = path },
            IconSource = path,
            SourceKey = "folder:" + path.ToLowerInvariant(),
        };
        LayoutEditor.Place(_host.Layout.Layout, channel);
        _host.SaveLayout();
        _window.ShowToast($"\"{channel.Title}\" was added to your channels");
    }
}
