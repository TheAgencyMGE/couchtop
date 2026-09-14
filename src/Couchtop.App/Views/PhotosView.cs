using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using Couchtop.App.Controls;
using Couchtop.App.Services;
using Path = System.IO.Path;

namespace Couchtop.App.Views;

/// <summary>The Photos: thumbnail wall plus a full-screen viewer with a slow-zoom slideshow.</summary>
public sealed class PhotosView : UserControl, IScreenView, IDisposable
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp", ".tif", ".tiff", ".jfif", ".heic" };

    private readonly AppHost _host;
    private readonly MainWindow _window;
    private readonly ListBox _grid;
    private readonly TextBlock _subtitle;
    private readonly TextBlock _empty;
    private readonly Grid _viewer;
    private readonly Image _photo;
    private readonly ScaleTransform _zoom = new(1, 1);
    private readonly TextBlock _caption;
    private readonly Button _slideButton;
    private readonly DispatcherTimer _slideTimer;
    private List<string> _files = new();
    private int _index = -1;
    private bool _slideshow;

    public PhotosView(AppHost host, MainWindow window)
    {
        _host = host;
        _window = window;

        var root = new Grid();
        var body = new Grid();
        _grid = new ListBox { ItemsPanel = ViewKit.WrapPanelTemplate() };
        ScrollViewer.SetHorizontalScrollBarVisibility(_grid, ScrollBarVisibility.Disabled);
        _grid.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (ItemsControl.ContainerFromElement(_grid, e.OriginalSource as DependencyObject) is ListBoxItem { Content: FrameworkElement { Tag: int index } })
            {
                Open(index);
                e.Handled = true;
            }
        };
        _grid.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && _grid.SelectedItem is FrameworkElement { Tag: int index }) Open(index);
        };
        body.Children.Add(_grid);
        _empty = ViewKit.Text("", 34, FontWeights.Bold, "SubtleTextBrush", align: TextAlignment.Center);
        _empty.HorizontalAlignment = HorizontalAlignment.Center;
        _empty.VerticalAlignment = VerticalAlignment.Center;
        body.Children.Add(_empty);

        root.Children.Add(ViewKit.Scaffold("Photos", "", body, window.ReturnToMenu, out _subtitle,
            ViewKit.Pill("Slideshow", () =>
            {
                if (_files.Count == 0) return;
                Open(0);
                ToggleSlideshow();
            }, 240),
            ViewKit.Pill("Choose Folder", ChooseFolder, 280)));

        // Full-screen viewer
        var stage = new Grid { Width = 1920, Height = 1080 };
        stage.Children.Add(new Rectangle { Fill = new SolidColorBrush(Color.FromArgb(0xF5, 0x10, 0x14, 0x18)) });
        var photoFrame = new Border { Margin = new Thickness(60, 60, 60, 190), ClipToBounds = true };
        _photo = new Image { Stretch = Stretch.Uniform, RenderTransform = _zoom, RenderTransformOrigin = new Point(0.5, 0.5) };
        RenderOptions.SetBitmapScalingMode(_photo, BitmapScalingMode.HighQuality);
        photoFrame.Child = _photo;
        stage.Children.Add(photoFrame);
        _caption = ViewKit.Text("", 28, FontWeights.Bold, "InverseTextBrush", wrap: false);
        _caption.Margin = new Thickness(70, 16, 70, 0);
        _caption.VerticalAlignment = VerticalAlignment.Top;
        _caption.Opacity = 0.8;
        stage.Children.Add(_caption);
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 40) };
        _slideButton = ViewKit.Pill("Slideshow", ToggleSlideshow, 300);
        toolbar.Children.Add(ViewKit.Pill("Previous", () => Step(-1), 260));
        toolbar.Children.Add(_slideButton);
        toolbar.Children.Add(ViewKit.Pill("Next", () => Step(1), 260));
        toolbar.Children.Add(ViewKit.Pill("Back to Photos", CloseViewer, 340));
        stage.Children.Add(toolbar);
        _viewer = new Grid { Visibility = Visibility.Collapsed };
        _viewer.Children.Add(new Viewbox { Stretch = Stretch.Uniform, Child = stage });
        root.Children.Add(_viewer);
        Content = root;

        _slideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _slideTimer.Tick += (_, _) => Step(1);
        Loaded += (_, _) => Scan();
    }

    private string Folder => string.IsNullOrWhiteSpace(_host.Settings.Current.PhotosFolder) || !Directory.Exists(_host.Settings.Current.PhotosFolder)
        ? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
        : _host.Settings.Current.PhotosFolder;

    private bool _scanned;

    private async void Scan(bool force = false)
    {
        if (_scanned && !force) return;
        _scanned = true;
        var folder = Folder;
        _subtitle.Text = folder;
        _subtitle.Visibility = Visibility.Visible;
        _grid.Items.Clear();
        _empty.Text = "Looking for photos…";
        _empty.Visibility = Visibility.Visible;
        _files = await Task.Run(() => Collect(folder));
        _empty.Visibility = _files.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var folderName = new DirectoryInfo(folder).Name;
        _empty.Text = $"No photos were found in \"{folderName}\".\nUse Choose Folder to pick another one.";
        for (var i = 0; i < Math.Min(_files.Count, 600); i++) _grid.Items.Add(BuildThumb(i));
    }

    private static List<string> Collect(string folder)
    {
        var found = new List<string>();
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((folder, 0));
        var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System };
        while (pending.Count > 0 && found.Count < 4000)
        {
            var (path, depth) = pending.Pop();
            try
            {
                found.AddRange(Directory.EnumerateFiles(path, "*", options).Where(f => Extensions.Contains(Path.GetExtension(f))));
                if (depth < 3)
                    foreach (var sub in Directory.EnumerateDirectories(path, "*", options)) pending.Push((sub, depth + 1));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
        return found.Select(f => (File: f, Time: SafeTime(f))).OrderByDescending(x => x.Time).Select(x => x.File).Take(2000).ToList();
    }

    private static DateTime SafeTime(string file)
    {
        try { return File.GetLastWriteTimeUtc(file); }
        catch (IOException) { return DateTime.MinValue; }
    }

    private FrameworkElement BuildThumb(int index)
    {
        var frame = new Border { Width = 300, Height = 210, CornerRadius = new CornerRadius(18), ClipToBounds = true, Tag = index };
        frame.SetResourceReference(Border.BackgroundProperty, "TrackBrush");
        var image = new Image { Stretch = Stretch.UniformToFill };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        frame.Child = image;
        frame.Loaded += (_, _) => frame.Clip = new RectangleGeometry(new Rect(0, 0, 300, 210), 18, 18);
        _ = LoadThumbAsync(image, _files[index]);
        return frame;
    }

    private async Task LoadThumbAsync(Image image, string file)
    {
        var result = await _host.Icons.LoadUncachedAsync(file, 320, iconOnly: false);
        if (result is not null) image.Source = result.Image;
    }

    private async void Open(int index)
    {
        if (_files.Count == 0) return;
        _index = (index % _files.Count + _files.Count) % _files.Count;
        if (_viewer.Visibility != Visibility.Visible)
        {
            _viewer.Visibility = Visibility.Visible;
            Anim.To(_viewer, OpacityProperty, 1, 250, from: 0);
            _host.Audio.Play(SoundEffect.Select);
        }
        var file = _files[_index];
        _caption.Text = $"{Path.GetFileName(file)}   ·   {_index + 1} of {_files.Count}";
        var width = (int)Math.Min(3840, Math.Max(1280, _window.ActualWidth * (PresentationSource.FromVisual(_window)?.CompositionTarget?.TransformToDevice.M11 ?? 1)));
        var bitmap = await IconService.LoadImageFileAsync(file, width);
        if (_index < 0 || _files.Count == 0 || _files[_index] != file) return;
        _photo.Source = bitmap;
        Anim.To(_photo, OpacityProperty, 1, 350, from: 0.2);
        _zoom.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _zoom.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        if (_slideshow && !Anim.Reduced)
        {
            var zoom = new DoubleAnimation(1, 1.08, TimeSpan.FromSeconds(5.5));
            _zoom.BeginAnimation(ScaleTransform.ScaleXProperty, zoom);
            _zoom.BeginAnimation(ScaleTransform.ScaleYProperty, zoom);
        }
    }

    private void Step(int delta)
    {
        if (_index < 0) return;
        Open(_index + delta);
        if (_slideshow)
        {
            _slideTimer.Stop();
            _slideTimer.Start();
        }
    }

    private void ToggleSlideshow()
    {
        _slideshow = !_slideshow;
        _slideButton.Content = _slideshow ? "Pause" : "Slideshow";
        if (_slideshow)
        {
            _slideTimer.Start();
            if (_index >= 0) Open(_index);
        }
        else
        {
            _slideTimer.Stop();
            _zoom.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            _zoom.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        }
    }

    private void CloseViewer()
    {
        if (_slideshow) ToggleSlideshow();
        _index = -1;
        Anim.To(_viewer, OpacityProperty, 0, 200, completed: () =>
        {
            _viewer.Visibility = Visibility.Collapsed;
            _photo.Source = null;
        });
        _host.Audio.Play(SoundEffect.Back);
    }

    private void ChooseFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Choose your photo folder", InitialDirectory = Folder };
        if (dialog.ShowDialog(_window) != true) return;
        _host.Settings.Current.PhotosFolder = dialog.FolderName;
        _host.SaveSettings();
        Scan(force: true);
    }

    public void OnShown() { }

    public void OnHidden()
    {
        _slideTimer.Stop();
        _slideshow = false;
    }

    public bool HandleBack()
    {
        if (_viewer.Visibility != Visibility.Visible) return false;
        CloseViewer();
        return true;
    }

    public bool HandleKey(KeyEventArgs e)
    {
        if (_viewer.Visibility != Visibility.Visible) return false;
        switch (e.Key)
        {
            case Key.Left: Step(-1); return true;
            case Key.Right: Step(1); return true;
            case Key.Space: ToggleSlideshow(); return true;
        }
        return false;
    }

    public void Dispose() => _slideTimer.Stop();
}
