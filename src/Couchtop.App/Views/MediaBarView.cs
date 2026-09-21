using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Couchtop.App.Controls;
using Couchtop.Core.Channels;
using Couchtop.App.Services;

namespace Couchtop.App.Views;

/// <summary>
/// The Media Bar home screen: a row of categories crossed by a column of items, the shape late-2000s console
/// menus used. Categories are a view over the same channel layout the Channels menu shows, so nothing is stored
/// twice and every channel opens exactly the same screens. All artwork is Couchtop's own.
/// </summary>
public sealed class MediaBarView : UserControl, IHomeScreen, IDisposable
{
    private const double CrossX = 560, CategoryY = 300, CrossY = 580, CategoryGap = 232, ItemGap = 116;

    private readonly AppHost _host;
    private readonly MainWindow _window;
    private readonly Grid _stage = new() { Width = 1920, Height = 1080 };
    private readonly Canvas _categoryRow = new();
    private readonly TranslateTransform _rowShift = new();
    private readonly Canvas _columnHost = new() { ClipToBounds = true, Width = 1920, Height = 1080 };
    private readonly Canvas _column = new();
    private readonly TranslateTransform _columnShift = new();
    private readonly StackPanel _detail = new();
    private readonly TranslateTransform _detailShift = new();
    private readonly TextBlock _itemTitle, _itemSubtitle, _clock, _dateText;
    private readonly Rectangle _backdrop;
    private readonly List<Path> _ribbons = new();
    private readonly DispatcherTimer _clockTimer;

    private readonly List<HomeCategory> _categories = new();
    private readonly List<MediaBarIcon> _categoryIcons = new();
    private readonly List<MediaBarRow> _rows = new();
    private int _category;
    private int _index;
    private bool _active;
    private bool _editMode;
    private DateTime _lastWheel;
    private Point _pointerAt = new(-1, -1);

    public MediaBarView(AppHost host, MainWindow window)
    {
        _host = host;
        _window = window;
        Focusable = true;
        FocusVisualStyle = null;
        Background = Brushes.Transparent;

        _backdrop = new Rectangle();
        _stage.Children.Add(_backdrop);
        _stage.Children.Add(BuildRibbons());
        _stage.Children.Add(CrossGlow());

        _categoryRow.RenderTransform = _rowShift;
        _stage.Children.Add(_categoryRow);

        _column.RenderTransform = _columnShift;
        _columnHost.Children.Add(_column);
        _stage.Children.Add(_columnHost);

        _itemTitle = HomeArt.Label("", 54, FontWeights.ExtraBold, Brushes.White);
        _itemSubtitle = HomeArt.Label("", 27, FontWeights.SemiBold, HomeArt.Frozen(Color.FromArgb(0xC0, 0xDD, 0xE8, 0xF2)));
        _detail.Children.Add(_itemTitle);
        _detail.Children.Add(_itemSubtitle);
        _detail.RenderTransform = _detailShift;
        _detail.HorizontalAlignment = HorizontalAlignment.Left;
        _detail.VerticalAlignment = VerticalAlignment.Top;
        _detail.Margin = new Thickness(CrossX + 120, CrossY - 56, 120, 0);
        _stage.Children.Add(_detail);

        _clock = HomeArt.Label("", 44, FontWeights.Light, Brushes.White);
        _clock.HorizontalAlignment = HorizontalAlignment.Right;
        _dateText = HomeArt.Label("", 24, FontWeights.SemiBold, HomeArt.Frozen(Color.FromArgb(0xB0, 0xDD, 0xE8, 0xF2)));
        _dateText.HorizontalAlignment = HorizontalAlignment.Right;
        var clockStack = new StackPanel { Margin = new Thickness(0, 54, 120, 0), VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Right };
        clockStack.Children.Add(_clock);
        clockStack.Children.Add(_dateText);
        _stage.Children.Add(clockStack);

        Content = new Viewbox { Stretch = Stretch.Uniform, Child = _stage };

        _clockTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();

        host.ChannelsChanged += OnChannelsChanged;
        MouseWheel += OnMouseWheel;

        PaintBackdrop();
        Rebuild();
        UpdateClock();
    }

    // ---------------------------------------------------------------- backdrop

    /// <summary>
    /// These menus tinted the background by the month and dimmed it by the hour; Couchtop does the same with its
    /// own palette, so the menu looks a little different in March than in November.
    /// </summary>
    private void PaintBackdrop()
    {
        var now = DateTime.Now;
        var hue = (now.Month - 1) / 12.0 * 360.0;
        var night = now.Hour is < 7 or >= 19;
        var top = FromHsv(hue, night ? 0.72 : 0.62, night ? 0.26 : 0.42);
        var bottom = FromHsv((hue + 26) % 360, night ? 0.85 : 0.78, night ? 0.07 : 0.12);
        var brush = new LinearGradientBrush(top, bottom, 90);
        brush.Freeze();
        _backdrop.Fill = brush;
    }

    private static Color FromHsv(double hue, double saturation, double value)
    {
        var c = value * saturation;
        var x = c * (1 - Math.Abs(hue / 60.0 % 2 - 1));
        var m = value - c;
        var (r, g, b) = hue switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }

    /// <summary>Slow ribbons of light drifting across the background.</summary>
    private UIElement BuildRibbons()
    {
        var canvas = new Canvas { IsHitTestVisible = false, ClipToBounds = true, Width = 1920, Height = 1080 };
        var shapes = new[]
        {
            ("M 0,620 C 420,470 760,760 1180,600 C 1500,480 1760,560 2600,470", 0.20, 6.0),
            ("M 0,760 C 380,880 820,600 1240,780 C 1560,915 1820,760 2600,690", 0.14, 9.0),
            ("M 0,430 C 500,330 900,520 1380,380 C 1700,285 1900,360 2600,300", 0.10, 4.0),
        };
        foreach (var (data, alpha, thickness) in shapes)
        {
            var stroke = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 0),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0, 255, 255, 255), 0),
                    new GradientStop(Color.FromArgb((byte)(alpha * 255), 255, 255, 255), 0.45),
                    new GradientStop(Color.FromArgb(0, 255, 255, 255), 1),
                },
            };
            stroke.Freeze();
            var path = new Path
            {
                Data = Geometry.Parse(data),
                Stroke = stroke,
                StrokeThickness = thickness,
                RenderTransform = new TranslateTransform(),
            };
            _ribbons.Add(path);
            canvas.Children.Add(path);
        }
        return canvas;
    }

    /// <summary>The column of light where the row and the column cross.</summary>
    private UIElement CrossGlow()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0, 255, 255, 255), 0),
                new GradientStop(Color.FromArgb(38, 255, 255, 255), 0.5),
                new GradientStop(Color.FromArgb(0, 255, 255, 255), 1),
            },
        };
        brush.Freeze();
        return new Rectangle
        {
            Width = 420,
            Height = 1080,
            Fill = brush,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(CrossX - 210, 0, 0, 0),
            IsHitTestVisible = false,
        };
    }

    // ---------------------------------------------------------------- building

    public void Rebuild()
    {
        var previous = SelectedItem()?.Title;
        _categories.Clear();
        _categories.AddRange(HomeCategories.Build(_host.Layout.Layout, _host.Settings.Current.Bookmarks, includePals: false));
        if (_categories.Count == 0) _categories.Add(new HomeCategory(HomeCategories.System, "System", new[] { new HomeItem(HomeItemKind.Desktop, "Windows Desktop") }));
        _category = Math.Clamp(_category, 0, _categories.Count - 1);

        BuildCategoryRow();
        var items = _categories[_category].Items;
        var keep = previous is null ? _index : items.ToList().FindIndex(i => i.Title == previous);
        BuildColumn(keep < 0 ? _index : keep, animate: false);
    }

    private void BuildCategoryRow()
    {
        _categoryRow.Children.Clear();
        _categoryIcons.Clear();
        for (var i = 0; i < _categories.Count; i++)
        {
            var index = i;
            var icon = new MediaBarIcon(_categories[i].Id, _categories[i].Title);
            Canvas.SetLeft(icon, i * CategoryGap - MediaBarIcon.Size / 2);
            Canvas.SetTop(icon, CategoryY - 60);
            icon.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                Focus();
                GoToCategory(index);
            };
            icon.MouseEnter += (_, _) =>
            {
                if (index != _category) _host.Audio.Play(SoundEffect.Hover);
            };
            _categoryIcons.Add(icon);
            _categoryRow.Children.Add(icon);
        }
        PaintCategories(false);
    }

    private void PaintCategories(bool animate)
    {
        for (var i = 0; i < _categoryIcons.Count; i++) _categoryIcons[i].SetSelected(i == _category, animate);
        var target = CrossX - _category * CategoryGap;
        if (animate && !Anim.Reduced) Anim.To(_rowShift, TranslateTransform.XProperty, target, 340, Anim.EaseOut);
        else
        {
            _rowShift.BeginAnimation(TranslateTransform.XProperty, null);
            _rowShift.X = target;
        }
    }

    private void BuildColumn(int index, bool animate)
    {
        foreach (var row in _rows) row.Dispose();
        _rows.Clear();
        _column.Children.Clear();

        var items = _categories[_category].Items;
        for (var i = 0; i < items.Count; i++)
        {
            var slot = i;
            var row = new MediaBarRow(items[i], _host);
            Canvas.SetLeft(row, CrossX - MediaBarRow.IconSize / 2);
            Canvas.SetTop(row, i * ItemGap - MediaBarRow.IconSize / 2);
            row.MouseEnter += (_, _) => HoverRow(slot);
            row.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                Focus();
                if (slot == _index) Activate();
                else Select(slot);
            };
            _column.Children.Add(row);
            _rows.Add(row);
        }

        _index = Math.Clamp(index, 0, Math.Max(0, items.Count - 1));
        for (var i = 0; i < _rows.Count; i++) _rows[i].SetSelected(i == _index, false);
        UpdateRowFade(false);
        ShiftColumn(animate);
        UpdateDetail(animate);
        if (animate && !Anim.Reduced)
        {
            _column.Opacity = 0;
            HomeArt.Fade(_column, 1, 260);
        }
    }

    private void ShiftColumn(bool animate)
    {
        var target = CrossY - _index * ItemGap;
        if (animate && !Anim.Reduced) Anim.To(_columnShift, TranslateTransform.YProperty, target, 300, Anim.EaseOut);
        else
        {
            _columnShift.BeginAnimation(TranslateTransform.YProperty, null);
            _columnShift.Y = target;
        }
    }

    /// <summary>
    /// Items dim with distance from the cross, and the ones above it fade out entirely before they reach the
    /// row of categories, so the two arms of the cross never collide.
    /// </summary>
    private void UpdateRowFade(bool animate)
    {
        for (var i = 0; i < _rows.Count; i++)
        {
            var distance = i - _index;
            var opacity = distance switch
            {
                0 => 1.0,
                > 0 => Math.Max(0.18, 0.72 - (distance - 1) * 0.13),
                _ => Math.Max(0.0, 0.5 + (distance + 1) * 0.5),
            };
            _rows[i].SetFade(opacity, animate);
        }
    }

    private HomeItem? SelectedItem() =>
        _categories.Count > 0 && _index >= 0 && _index < _categories[_category].Items.Count ? _categories[_category].Items[_index] : null;

    private void UpdateDetail(bool animate)
    {
        var item = SelectedItem();
        _itemTitle.Text = item?.Title ?? "";
        _itemSubtitle.Text = item?.Subtitle ?? "";
        if (animate && !Anim.Reduced)
        {
            Anim.To(_detailShift, TranslateTransform.XProperty, 0, 260, Anim.EaseOut, from: 26);
            HomeArt.Fade(_detail, 1, 200);
        }
    }

    private void OnChannelsChanged(object? sender, EventArgs e) => Rebuild();

    // ---------------------------------------------------------------- navigation

    private void Select(int index)
    {
        var items = _categories[_category].Items;
        if (items.Count == 0) return;
        index = Math.Clamp(index, 0, items.Count - 1);
        if (index == _index) return;
        _rows[_index].SetSelected(false, true);
        _index = index;
        _rows[_index].SetSelected(true, true);
        UpdateRowFade(true);
        _host.Audio.Play(SoundEffect.Tick);
        ShiftColumn(true);
        UpdateDetail(true);
    }

    /// <summary>
    /// An item the pointer moved onto. The column slides to put the choice on the cross, which drags the next
    /// item under a pointer that has not moved and would run away down the list, so a hover only counts when
    /// the pointer is somewhere new since the last one.
    /// </summary>
    private void HoverRow(int slot)
    {
        var at = Mouse.GetPosition(this);
        if (Math.Abs(at.X - _pointerAt.X) < 3 && Math.Abs(at.Y - _pointerAt.Y) < 3) return;
        _pointerAt = at;
        Select(slot);
    }

    private void GoToCategory(int category)
    {
        category = Math.Clamp(category, 0, _categories.Count - 1);
        if (category == _category) return;
        _category = category;
        _host.Audio.Play(SoundEffect.Hover);
        PaintCategories(true);
        BuildColumn(0, animate: true);
    }

    private void Activate()
    {
        if (SelectedItem() is not { } item) return;
        if (_editMode && item.Channel is { } editable)
        {
            _ = ChannelDialogs.EditChannelAsync(_window, _host, editable);
            return;
        }
        _host.Audio.Play(SoundEffect.Select);
        HomeActions.Open(_window, _host, item, RowCenter(_index), () => SetEditMode(true));
    }

    private Point RowCenter(int index)
    {
        if (index < 0 || index >= _rows.Count) return new Point(_window.ActualWidth / 2, _window.ActualHeight / 2);
        return _rows[index].TranslatePoint(new Point(MediaBarRow.IconSize / 2, MediaBarRow.IconSize / 2), _window.RootGrid);
    }

    public bool HandleKey(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Up: Select(_index - 1); return true;
            case Key.Down: Select(_index + 1); return true;
            case Key.Left: GoToCategory(_category - 1); return true;
            case Key.Right: GoToCategory(_category + 1); return true;
            // Shoulder buttons (and a Wii Remote's + / -) move between categories.
            case Key.PageUp: GoToCategory(_category - 1); return true;
            case Key.PageDown: GoToCategory(_category + 1); return true;
            case Key.Home: GoToCategory(0); return true;
            case Key.Enter or Key.Space: Activate(); return true;
            case Key.F5:
                _host.RefreshChannelsInBackground();
                _window.ShowToast("Looking for newly installed apps…");
                return true;
        }
        return false;
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (DateTime.UtcNow - _lastWheel < TimeSpan.FromMilliseconds(120)) return;
        _lastWheel = DateTime.UtcNow;
        Select(_index + (e.Delta < 0 ? 1 : -1));
        e.Handled = true;
    }

    // ---------------------------------------------------------------- IHomeScreen

    public FrameworkElement Element => this;
    public string StyleId => Core.Settings.MenuStyleCatalog.MediaBar;
    public bool PlaysAmbience => false;
    public bool IsEditMode => _editMode;

    public void OnShown() => Focus();

    /// <summary>Called when another menu style takes over, so nothing keeps running in the background.</summary>
    public void Dispose()
    {
        _host.ChannelsChanged -= OnChannelsChanged;
        _clockTimer.Stop();
        foreach (var row in _rows) row.Dispose();
        _rows.Clear();
    }

    public void OnHidden()
    {
    }

    public bool HandleBack()
    {
        if (!_editMode) return false;
        _editMode = false;
        return true;
    }

    public void SetActive(bool active)
    {
        if (_active == active) return;
        _active = active;
        _clockTimer.Interval = TimeSpan.FromSeconds(active ? 1 : 15);
        if (active)
        {
            PaintBackdrop();
            StartRibbons();
        }
        else
        {
            foreach (var ribbon in _ribbons) ribbon.RenderTransform.BeginAnimation(TranslateTransform.XProperty, null);
        }
        UpdateClock();
    }

    private void StartRibbons()
    {
        if (Anim.Reduced || Anim.LowPowerGraphics) return;
        var seconds = new[] { 38.0, 52.0, 44.0 };
        for (var i = 0; i < _ribbons.Count; i++)
        {
            var drift = new DoubleAnimation(0, -700, TimeSpan.FromSeconds(seconds[i % seconds.Length]))
            {
                RepeatBehavior = RepeatBehavior.Forever,
                AutoReverse = true,
                EasingFunction = Anim.EaseInOut,
            };
            _ribbons[i].RenderTransform.BeginAnimation(TranslateTransform.XProperty, drift);
        }
    }

    public void PlayIntro()
    {
        if (Anim.Reduced) return;
        Anim.To(_rowShift, TranslateTransform.XProperty, CrossX - _category * CategoryGap, 520, Anim.EaseOut, from: CrossX - _category * CategoryGap + 120);
        Anim.To(_detailShift, TranslateTransform.XProperty, 0, 520, Anim.EaseOut, from: 60);
        HomeArt.Fade(_column, 1, 420);
    }

    public void SetEditMode(bool on)
    {
        _editMode = on;
        if (on) _ = HomeActions.ShowChannelToolsAsync(_window, _host, SelectedItem()?.Channel, () => _editMode = false);
    }

    public void OpenBuiltIn(string id, Point? origin) =>
        HomeActions.OpenBuiltIn(_window, _host, id, origin, () => SetEditMode(true));

    public void RefreshClock() => UpdateClock();

    public Point SlotCenter(int slot)
    {
        var items = _categories.Count > 0 ? _categories[_category].Items : Array.Empty<HomeItem>();
        for (var i = 0; i < items.Count; i++)
            if (items[i].Slot == slot) return RowCenter(i);
        return RowCenter(_index);
    }

    /// <summary>Snapshot rendering: pick a category and an item directly.</summary>
    internal void SnapshotSelect(int category, int index)
    {
        GoToCategory(category);
        Select(index);
    }

    public void SnapshotHover(int slot)
    {
        if (slot < 0) return;
        var items = _categories[_category].Items;
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].Slot != slot) continue;
            Select(i);
            return;
        }
        Select(Math.Min(slot, items.Count - 1));
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        _clock.Text = _host.Settings.Current.Clock24Hour
            ? now.ToString("HH:mm", CultureInfo.CurrentCulture)
            : now.ToString("h:mm tt", CultureInfo.CurrentCulture);
        _dateText.Text = now.ToString("ddd d MMM", CultureInfo.CurrentCulture);
    }
}

/// <summary>A category on the horizontal bar: original line art plus its name.</summary>
internal sealed class MediaBarIcon : Grid
{
    public const double Size = 120;

    private readonly Path _glyph;
    private readonly TextBlock _label;
    private readonly ScaleTransform _scale = new(1, 1);

    public MediaBarIcon(string categoryId, string title)
    {
        Width = Size;
        Height = 210;
        Background = Brushes.Transparent;
        RenderTransformOrigin = new Point(0.5, 0.42);
        RenderTransform = _scale;

        _glyph = new Path
        {
            Data = Geometry.Parse(GlyphFor(categoryId)),
            Stroke = Brushes.White,
            StrokeThickness = 4.5,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Stretch = Stretch.Uniform,
            Width = 84,
            Height = 84,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 10, 0, 0),
        };
        _label = HomeArt.Label(title, 28, FontWeights.Bold, Brushes.White);
        _label.HorizontalAlignment = HorizontalAlignment.Center;
        _label.VerticalAlignment = VerticalAlignment.Top;
        _label.Margin = new Thickness(0, 110, 0, 0);
        _label.TextAlignment = TextAlignment.Center;

        Children.Add(_glyph);
        Children.Add(_label);
        Opacity = 0.55;
    }

    /// <summary>Simple original pictograms: a cog, a picture, a window grid, a pad, a globe and a face.</summary>
    private static string GlyphFor(string id) => id switch
    {
        HomeCategories.System => "M 40,26 A 14,14 0 1 0 40,54 A 14,14 0 1 0 40,26 M 40,6 L 40,16 M 40,64 L 40,74 M 6,40 L 16,40 M 64,40 L 74,40 M 16,16 L 23,23 M 57,57 L 64,64 M 64,16 L 57,23 M 23,57 L 16,64",
        HomeCategories.Media => "M 8,16 H 72 V 64 H 8 Z M 8,52 L 28,34 L 44,48 L 56,40 L 72,54 M 56,28 A 6,6 0 1 0 56,27.9",
        HomeCategories.Apps => "M 10,10 H 34 V 34 H 10 Z M 46,10 H 70 V 34 H 46 Z M 10,46 H 34 V 70 H 10 Z M 46,46 H 70 V 70 H 46 Z",
        HomeCategories.Games => "M 12,28 H 68 A 12,12 0 0 1 68,56 H 12 A 12,12 0 0 1 12,28 Z M 26,36 V 48 M 20,42 H 32 M 54,38 A 3,3 0 1 0 54,37.9 M 60,48 A 3,3 0 1 0 60,47.9",
        HomeCategories.Web => "M 40,6 A 34,34 0 1 0 40,74 A 34,34 0 1 0 40,6 M 6,40 H 74 M 40,6 C 22,24 22,56 40,74 M 40,6 C 58,24 58,56 40,74",
        _ => "M 40,10 A 26,26 0 1 0 40,62 A 26,26 0 1 0 40,10 M 30,32 A 3,3 0 1 0 30,31.9 M 50,32 A 3,3 0 1 0 50,31.9 M 28,44 C 34,52 46,52 52,44",
    };

    public void SetSelected(bool on, bool animate)
    {
        var ms = animate && !Anim.Reduced ? 240 : 1;
        Anim.To(_scale, ScaleTransform.ScaleXProperty, on ? 1.12 : 0.74, ms, Anim.EaseOut);
        Anim.To(_scale, ScaleTransform.ScaleYProperty, on ? 1.12 : 0.74, ms, Anim.EaseOut);
        Anim.To(this, OpacityProperty, on ? 1 : 0.5, ms);
        _label.Opacity = on ? 1 : 0.85;
        _glyph.StrokeThickness = on ? 5 : 4;
    }
}

/// <summary>One item in the vertical column: channel art and its name.</summary>
internal sealed class MediaBarRow : Grid, IDisposable
{
    public const double IconSize = 78;

    private readonly Border _art;
    private readonly TextBlock _label;
    private readonly ScaleTransform _scale = new(1, 1);
    private bool _selected;

    public MediaBarRow(HomeItem item, AppHost host)
    {
        Width = 620;
        Height = IconSize;
        Background = Brushes.Transparent;
        HorizontalAlignment = HorizontalAlignment.Left;
        RenderTransformOrigin = new Point(IconSize / 2 / 620, 0.5);
        RenderTransform = _scale;

        // Icons hang straight on the bar: no plate, no rounded tile, no shadow.
        _art = new Border
        {
            Width = IconSize,
            Height = IconSize,
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = Brushes.Transparent,
            Child = ConsoleArt.MediaBarIconArt(item, host, IconSize),
        };
        _label = HomeArt.Label(item.Title, 30, FontWeights.SemiBold, Brushes.White, 0.8);
        _label.Margin = new Thickness(IconSize + 26, 0, 0, 0);
        _label.VerticalAlignment = VerticalAlignment.Center;
        _label.MaxWidth = 500;

        Children.Add(_art);
        Children.Add(_label);
        Opacity = 0.62;
    }

    public void SetSelected(bool on, bool animate)
    {
        if (_selected == on && animate) return;
        _selected = on;
        var ms = animate && !Anim.Reduced ? 220 : 1;
        Anim.To(_scale, ScaleTransform.ScaleXProperty, on ? 1.24 : 1, ms, Anim.EaseOut);
        Anim.To(_scale, ScaleTransform.ScaleYProperty, on ? 1.24 : 1, ms, Anim.EaseOut);
        // The selected item's own name lives in the big label to the right of the cross.
        Anim.To(_label, OpacityProperty, on ? 0 : 0.8, ms);
    }

    /// <summary>How visible this item is, set by its distance from the cross.</summary>
    public void SetFade(double opacity, bool animate)
    {
        if (animate && !Anim.Reduced) Anim.To(this, OpacityProperty, opacity, 200, Anim.EaseOut);
        else
        {
            BeginAnimation(OpacityProperty, null);
            Opacity = opacity;
        }
        IsHitTestVisible = opacity > 0.05;
    }

    public void Dispose()
    {
        _art.Child = null;
        Children.Clear();
    }
}
