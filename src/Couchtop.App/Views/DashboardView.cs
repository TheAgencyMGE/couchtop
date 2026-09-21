using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Couchtop.App.Controls;
using Couchtop.App.Services;
using Couchtop.Core.Channels;

namespace Couchtop.App.Views;

/// <summary>
/// The Dashboard home screen: angled blade tabs across the top and a horizontal row of square tiles, the shape
/// mid-2000s console dashboards used. It is only a presentation of the shared channel layout — the same channels,
/// the same screens, the same settings as the Channels menu, reached by moving along a row instead of a grid.
/// All artwork is Couchtop's own.
/// </summary>
public sealed class DashboardView : UserControl, IHomeScreen, IDisposable
{
    private const double TileSize = 272, TileGap = 26, StripLeft = 190, StripTop = 430, RowY = StripTop + TileSize / 2;

    private static readonly Color Accent = Color.FromRgb(0x7B, 0xC6, 0x18);

    /// <summary>One colour per blade, in the era's style: a bright signature green with cooler neighbours.</summary>
    private static readonly Dictionary<string, Color> BladeColors = new()
    {
        [HomeCategories.System] = Color.FromRgb(0x8C, 0x9B, 0xA5),
        [HomeCategories.Media] = Color.FromRgb(0xE0, 0x8A, 0x2E),
        [HomeCategories.Apps] = Color.FromRgb(0x2E, 0x9E, 0xC8),
        [HomeCategories.Games] = Accent,
        [HomeCategories.Web] = Color.FromRgb(0x4F, 0x8F, 0xE0),
        [HomeCategories.Pals] = Color.FromRgb(0xB6, 0x6C, 0xD6),
    };

    private readonly AppHost _host;
    private readonly MainWindow _window;
    private readonly Grid _stage = new() { Width = 1920, Height = 1080 };
    private readonly StackPanel _bladeRail = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(150, 186, 0, 0), VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Canvas _stripHost = new() { ClipToBounds = true, Width = 1920, Height = 360, Margin = new Thickness(0, StripTop - 30, 0, 0), VerticalAlignment = VerticalAlignment.Top };
    private readonly Canvas _strip = new();
    private readonly TranslateTransform _stripShift = new();
    private readonly TranslateTransform _detailShift = new();
    private readonly StackPanel _detail = new() { Margin = new Thickness(150, 780, 700, 0), VerticalAlignment = VerticalAlignment.Top };
    private readonly TextBlock _clock, _dateText, _bladeTitle, _itemTitle, _itemSubtitle, _cardName, _cardLine;
    private readonly Border _avatarHost;
    private readonly Rectangle _sheen;
    private readonly DispatcherTimer _clockTimer;

    private readonly List<HomeCategory> _categories = new();
    private readonly List<Border> _bladeTabs = new();
    private readonly List<DashboardTile> _tiles = new();
    private int _blade;
    private int _index;
    private double _stripTarget;
    private Point _pointerAt = new(-1, -1);
    private bool _active;
    private bool _editMode;
    private DateTime _lastWheel;

    public DashboardView(AppHost host, MainWindow window)
    {
        _host = host;
        _window = window;
        Focusable = true;
        FocusVisualStyle = null;
        Background = Brushes.Transparent;

        _stage.Children.Add(Background_());
        _sheen = Sheen();
        _stage.Children.Add(_sheen);

        // Player card. No avatar: that is the Channels menu's Pal, and this shell has nothing to do with it.
        _avatarHost = new Border { Width = 116, Height = 116, ClipToBounds = true, Background = HomeArt.Frozen(Color.FromRgb(0x18, 0x1D, 0x1F)), BorderThickness = new Thickness(2), BorderBrush = HomeArt.Frozen(Color.FromArgb(0x55, 0x7B, 0xC6, 0x18)) };
        _avatarHost.Child = ConsoleArt.Mark("M 10,10 H 36 V 36 H 10 Z M 44,10 H 70 V 36 H 44 Z M 10,44 H 36 V 70 H 10 Z M 44,44 H 70 V 70 H 44 Z", 56, HomeArt.Frozen(Accent), 5);
        _cardName = HomeArt.Label("Couchtop", 40, FontWeights.ExtraBold, Brushes.White);
        _cardLine = HomeArt.Label("", 24, FontWeights.SemiBold, HomeArt.Frozen(Color.FromRgb(0x9A, 0xB0, 0x9E)));
        var cardText = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20, 0, 0, 0) };
        cardText.Children.Add(_cardName);
        cardText.Children.Add(_cardLine);
        var card = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(150, 46, 0, 0), VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left };
        card.Children.Add(_avatarHost);
        card.Children.Add(cardText);
        _stage.Children.Add(card);

        _clock = HomeArt.Label("", 62, FontWeights.Light, Brushes.White);
        _clock.HorizontalAlignment = HorizontalAlignment.Right;
        _dateText = HomeArt.Label("", 26, FontWeights.SemiBold, HomeArt.Frozen(Color.FromRgb(0x9A, 0xB0, 0x9E)));
        _dateText.HorizontalAlignment = HorizontalAlignment.Right;
        var clockStack = new StackPanel { Margin = new Thickness(0, 52, 150, 0), VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Right };
        clockStack.Children.Add(_clock);
        clockStack.Children.Add(_dateText);
        _stage.Children.Add(clockStack);

        _stage.Children.Add(_bladeRail);

        _bladeTitle = HomeArt.Label("", 34, FontWeights.ExtraBold, HomeArt.Frozen(Accent));
        _bladeTitle.Margin = new Thickness(152, 322, 0, 0);
        _bladeTitle.VerticalAlignment = VerticalAlignment.Top;
        _bladeTitle.HorizontalAlignment = HorizontalAlignment.Left;
        _stage.Children.Add(_bladeTitle);

        _strip.RenderTransform = _stripShift;
        _stripHost.Children.Add(_strip);
        _stage.Children.Add(_stripHost);

        _itemTitle = HomeArt.Label("", 62, FontWeights.ExtraBold, Brushes.White);
        _itemSubtitle = HomeArt.Label("", 30, FontWeights.SemiBold, HomeArt.Frozen(Color.FromRgb(0x9A, 0xB0, 0x9E)));
        _detail.Children.Add(_itemTitle);
        _detail.Children.Add(_itemSubtitle);
        _detail.RenderTransform = _detailShift;
        _stage.Children.Add(_detail);

        _stage.Children.Add(Hints());

        Content = new Viewbox { Stretch = Stretch.Uniform, Child = _stage };

        _clockTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();

        host.ChannelsChanged += OnChannelsChanged;
        MouseWheel += OnMouseWheel;

        Rebuild();
        UpdateClock();
    }

    // ---------------------------------------------------------------- chrome

    private static UIElement Background_()
    {
        var grid = new Grid();
        var back = new LinearGradientBrush(Color.FromRgb(0x10, 0x16, 0x12), Color.FromRgb(0x04, 0x06, 0x08), 90);
        back.Freeze();
        grid.Children.Add(new Rectangle { Fill = back });
        grid.Children.Add(new Ellipse
        {
            Width = 1700,
            Height = 1100,
            Margin = new Thickness(-500, -420, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Fill = HomeArt.Glow(Accent, 0.16),
        });
        grid.Children.Add(new Ellipse
        {
            Width = 1500,
            Height = 900,
            Margin = new Thickness(0, 0, -400, -360),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Fill = HomeArt.Glow(Color.FromRgb(0x2E, 0x9E, 0xC8), 0.12),
        });
        return grid;
    }

    /// <summary>The slow band of light that drifts across these dashboards. Only runs while the menu is in front.</summary>
    private static Rectangle Sheen()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0, 255, 255, 255), 0),
                new GradientStop(Color.FromArgb(16, 255, 255, 255), 0.5),
                new GradientStop(Color.FromArgb(0, 255, 255, 255), 1),
            },
        };
        brush.Freeze();
        return new Rectangle
        {
            Width = 900,
            Height = 1600,
            Fill = brush,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(-900, -260, 0, 0),
            IsHitTestVisible = false,
            RenderTransform = new TransformGroup { Children = { new SkewTransform(-18, 0), new TranslateTransform() } },
        };
    }

    private UIElement Hints()
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(150, 0, 0, 70),
        };
        foreach (var (key, label) in new[] { ("Enter", "Open"), ("← →", "Move"), ("↑ ↓", "Blades"), ("Home", "Quick Menu") })
        {
            var chip = new Border
            {
                CornerRadius = new CornerRadius(8),
                Background = HomeArt.Frozen(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
                Padding = new Thickness(14, 4, 14, 6),
                Margin = new Thickness(0, 0, 12, 0),
                Child = HomeArt.Label(key, 22, FontWeights.Bold, Brushes.White),
            };
            panel.Children.Add(chip);
            var text = HomeArt.Label(label, 22, FontWeights.SemiBold, HomeArt.Frozen(Color.FromRgb(0x8C, 0x9B, 0xA5)));
            text.Margin = new Thickness(0, 4, 34, 0);
            panel.Children.Add(text);
        }
        return panel;
    }

    // ---------------------------------------------------------------- building

    public void Rebuild()
    {
        var previous = SelectedItem();
        _categories.Clear();
        _categories.AddRange(HomeCategories.Build(_host.Layout.Layout, _host.Settings.Current.Bookmarks, includePals: false));
        if (_categories.Count == 0) _categories.Add(new HomeCategory(HomeCategories.System, "System", new[] { new HomeItem(HomeItemKind.Desktop, "Windows Desktop") }));

        _blade = Math.Clamp(_blade, 0, _categories.Count - 1);
        BuildBlades();
        // Keep the same item selected across a rebuild where possible (channels changing, theme switching).
        var keepIndex = previous is null ? _index : _categories[_blade].Items.ToList().FindIndex(i => i.Title == previous.Title);
        BuildStrip(keepIndex < 0 ? _index : keepIndex, animate: false);
        UpdateCard();
    }

    private void BuildBlades()
    {
        _bladeRail.Children.Clear();
        _bladeTabs.Clear();
        for (var i = 0; i < _categories.Count; i++)
        {
            var index = i;
            var category = _categories[i];
            var color = BladeColors.TryGetValue(category.Id, out var c) ? c : Accent;
            var label = HomeArt.Label(category.Title.ToUpperInvariant(), 27, FontWeights.Bold, Brushes.White);
            label.RenderTransform = new SkewTransform(12, 0); // undo the tab's skew so the text stays upright
            var tab = new Border
            {
                Padding = new Thickness(34, 10, 34, 12),
                Margin = new Thickness(0, 0, 10, 0),
                CornerRadius = new CornerRadius(4),
                Background = HomeArt.Frozen(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(0, 0, 0, 5),
                BorderBrush = HomeArt.Frozen(Color.FromArgb(0x00, c.R, c.G, c.B)),
                RenderTransform = new SkewTransform(-12, 0),
                Cursor = Cursors.Arrow,
                Child = label,
                Tag = color,
            };
            tab.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                GoToBlade(index);
            };
            tab.MouseEnter += (_, _) =>
            {
                if (index != _blade) _host.Audio.Play(SoundEffect.Hover);
            };
            _bladeTabs.Add(tab);
            _bladeRail.Children.Add(tab);
        }
        PaintBlades();
    }

    private void PaintBlades()
    {
        for (var i = 0; i < _bladeTabs.Count; i++)
        {
            var selected = i == _blade;
            var color = (Color)_bladeTabs[i].Tag;
            _bladeTabs[i].Background = HomeArt.Frozen(selected
                ? Color.FromArgb(0xE8, color.R, color.G, color.B)
                : Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF));
            _bladeTabs[i].BorderBrush = HomeArt.Frozen(selected ? Colors.White : Color.FromArgb(0, 0, 0, 0));
            if (_bladeTabs[i].Child is TextBlock text)
            {
                text.Foreground = selected ? HomeArt.Frozen(Color.FromRgb(0x0B, 0x10, 0x0B)) : Brushes.White;
                text.Opacity = selected ? 1 : 0.75;
            }
        }
        if (_categories.Count > 0)
        {
            var category = _categories[_blade];
            _bladeTitle.Text = $"{category.Items.Count} {(category.Items.Count == 1 ? "item" : "items")}";
            _bladeTitle.Foreground = HomeArt.Frozen(BladeColors.TryGetValue(category.Id, out var c) ? c : Accent);
        }
    }

    private void BuildStrip(int index, bool animate)
    {
        foreach (var tile in _tiles) tile.Dispose();
        _tiles.Clear();
        _strip.Children.Clear();

        var items = _categories[_blade].Items;
        for (var i = 0; i < items.Count; i++)
        {
            var slot = i;
            var tile = new DashboardTile(items[i], _host, TileSize, BladeColors.TryGetValue(_categories[_blade].Id, out var c) ? c : Accent);
            Canvas.SetLeft(tile, i * (TileSize + TileGap));
            Canvas.SetTop(tile, 30);
            tile.MouseEnter += (_, _) => HoverTile(slot);
            tile.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                Focus();
                if (slot == _index) Activate();
                else Select(slot, fromPointer: true);
            };
            _strip.Children.Add(tile);
            _tiles.Add(tile);
        }

        _index = Math.Clamp(index, 0, Math.Max(0, items.Count - 1));
        for (var i = 0; i < _tiles.Count; i++) _tiles[i].SetSelected(i == _index, animate);
        UpdateDetail();
        ShiftStrip(animate);
        if (animate)
        {
            // Blade change: the row slides in from the right, the way these dashboards moved between sections.
            HomeArt.Fade(_strip, 1, 220);
            _strip.Opacity = 0.2;
            Anim.To(_detailShift, TranslateTransform.XProperty, 0, 300, Anim.EaseOut, from: 70);
            HomeArt.Fade(_detail, 1, 260);
        }
    }

    /// <summary>Tiles fade out as they reach the edges of the screen, so the row never ends in a hard cut.</summary>
    private void UpdateEdgeFade(bool animate)
    {
        for (var i = 0; i < _tiles.Count; i++)
        {
            var center = _stripTarget + i * (TileSize + TileGap) + TileSize / 2;
            var fade = Math.Clamp((center - 30) / 280, 0, 1) * Math.Clamp((1890 - center) / 300, 0, 1);
            _tiles[i].SetFade(fade, animate);
        }
    }

    private void SetStripTarget(double target, bool animate)
    {
        _stripTarget = target;
        UpdateEdgeFade(animate);
        if (animate && !Anim.Reduced) Anim.To(_stripShift, TranslateTransform.XProperty, target, 330, Anim.EaseOut);
        else
        {
            _stripShift.BeginAnimation(TranslateTransform.XProperty, null);
            _stripShift.X = target;
        }
    }

    /// <summary>Keyboard and controller moves bring the selection to the front of the row.</summary>
    private void ShiftStrip(bool animate) => SetStripTarget(StripLeft - _index * (TileSize + TileGap), animate);

    /// <summary>
    /// Pointer moves leave the row where it is and only scroll when the selection would run off an edge, so
    /// hovering never drags the whole row along under a still pointer.
    /// </summary>
    private void KeepSelectionVisible(bool animate)
    {
        const double margin = 150;
        var left = _stripTarget + _index * (TileSize + TileGap);
        var right = left + TileSize;
        var target = _stripTarget;
        if (left < margin) target += margin - left;
        else if (right > 1920 - margin) target -= right - (1920 - margin);
        if (Math.Abs(target - _stripTarget) < 0.5)
        {
            UpdateEdgeFade(animate);
            return;
        }
        SetStripTarget(target, animate);
    }

    private HomeItem? SelectedItem() =>
        _categories.Count > 0 && _index >= 0 && _index < _categories[_blade].Items.Count ? _categories[_blade].Items[_index] : null;

    private void UpdateDetail()
    {
        var item = SelectedItem();
        _itemTitle.Text = item?.Title ?? "";
        _itemSubtitle.Text = item?.Subtitle ?? "";
    }

    private void UpdateCard()
    {
        var apps = _categories.SelectMany(c => c.Items).Count(i => i.Channel is not null);
        _cardLine.Text = $"{apps} {(apps == 1 ? "item" : "items")} · {_categories.Count} blades";
    }

    private void OnChannelsChanged(object? sender, EventArgs e) => Rebuild();

    // ---------------------------------------------------------------- navigation

    private void Select(int index, bool fromPointer)
    {
        var items = _categories[_blade].Items;
        if (items.Count == 0) return;
        index = Math.Clamp(index, 0, items.Count - 1);
        if (index == _index) return;
        _tiles[_index].SetSelected(false, true);
        _index = index;
        _tiles[_index].SetSelected(true, true);
        _host.Audio.Play(SoundEffect.Hover);
        UpdateDetail();
        if (fromPointer) KeepSelectionVisible(true);
        else ShiftStrip(true);
    }

    /// <summary>
    /// A tile the pointer moved onto. Selecting scrolls the row, which slides the next tile under a pointer
    /// that has not moved and would otherwise run away to the end of the row, so a hover only counts when the
    /// pointer is somewhere new since the last one.
    /// </summary>
    private void HoverTile(int slot)
    {
        var at = Mouse.GetPosition(this);
        if (Math.Abs(at.X - _pointerAt.X) < 3 && Math.Abs(at.Y - _pointerAt.Y) < 3) return;
        _pointerAt = at;
        Select(slot, fromPointer: true);
    }

    private void GoToBlade(int blade)
    {
        if (_categories.Count == 0) return;
        blade = Math.Clamp(blade, 0, _categories.Count - 1);
        if (blade == _blade) return;
        _blade = blade;
        _host.Audio.Play(SoundEffect.Page);
        PaintBlades();
        BuildStrip(0, animate: true);
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
        HomeActions.Open(_window, _host, item, TileCenter(_index), () => SetEditMode(true));
    }

    private Point TileCenter(int index)
    {
        if (index < 0 || index >= _tiles.Count) return new Point(_window.ActualWidth / 2, _window.ActualHeight / 2);
        return _tiles[index].TranslatePoint(new Point(TileSize / 2, TileSize / 2), _window.RootGrid);
    }

    public bool HandleKey(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Left: Select(_index - 1, false); return true;
            case Key.Right: Select(_index + 1, false); return true;
            case Key.Up: GoToBlade(_blade - 1); return true;
            case Key.Down: GoToBlade(_blade + 1); return true;
            case Key.PageUp: GoToBlade(_blade - 1); return true;
            case Key.PageDown: GoToBlade(_blade + 1); return true;
            case Key.Home: GoToBlade(0); return true;
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
        Select(_index + (e.Delta < 0 ? 1 : -1), fromPointer: true);
        e.Handled = true;
    }

    // ---------------------------------------------------------------- IHomeScreen

    public FrameworkElement Element => this;
    public string StyleId => Core.Settings.MenuStyleCatalog.Dashboard;
    public bool PlaysAmbience => false;
    public bool IsEditMode => _editMode;

    public void OnShown() => Focus();

    /// <summary>Called when another menu style takes over, so nothing keeps running in the background.</summary>
    public void Dispose()
    {
        _host.ChannelsChanged -= OnChannelsChanged;
        _clockTimer.Stop();
        foreach (var tile in _tiles) tile.Dispose();
        _tiles.Clear();
    }

    public void OnHidden()
    {
    }

    public bool HandleBack()
    {
        if (!_editMode) return false;
        SetEditMode(false);
        return true;
    }

    public void SetActive(bool active)
    {
        if (_active == active) return;
        _active = active;
        _clockTimer.Interval = TimeSpan.FromSeconds(active ? 1 : 15);
        if (active)
        {
            StartSheen();
        }
        else
        {
            _sheen.RenderTransform.BeginAnimation(TranslateTransform.XProperty, null);
        }
        UpdateClock();
    }

    private void StartSheen()
    {
        if (Anim.Reduced || Anim.LowPowerGraphics) return;
        var shift = ((TransformGroup)_sheen.RenderTransform).Children[1];
        var drift = new DoubleAnimation(0, 3100, TimeSpan.FromSeconds(24)) { RepeatBehavior = RepeatBehavior.Forever };
        shift.BeginAnimation(TranslateTransform.XProperty, drift);
    }

    public void PlayIntro()
    {
        if (Anim.Reduced) return;
        for (var i = 0; i < _tiles.Count && i < 10; i++) _tiles[i].PopIn(60 + i * 45);
        Anim.To(_bladeRail, OpacityProperty, 1, 420, Anim.EaseOut, from: 0);
        Anim.To(_detailShift, TranslateTransform.XProperty, 0, 460, Anim.EaseOut, from: 60);
    }

    /// <summary>
    /// The Dashboard has no drag-and-drop grid, so "Customize" opens the shared channel tools instead: the same
    /// add, find, edit and remove actions the Channels menu uses.
    /// </summary>
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
        var items = _categories.Count > 0 ? _categories[_blade].Items : Array.Empty<HomeItem>();
        for (var i = 0; i < items.Count; i++)
            if (items[i].Slot == slot) return TileCenter(i);
        return TileCenter(_index);
    }

    /// <summary>Snapshot rendering: pick a blade and an item directly.</summary>
    internal void SnapshotSelect(int blade, int index)
    {
        GoToBlade(blade);
        Select(index, fromPointer: false);
    }

    public void SnapshotHover(int slot)
    {
        if (slot < 0) return;
        var items = _categories[_blade].Items;
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].Slot != slot) continue;
            Select(i, fromPointer: false);
            return;
        }
        Select(Math.Min(slot, items.Count - 1), fromPointer: false);
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        _clock.Text = _host.Settings.Current.Clock24Hour
            ? now.ToString("HH:mm", CultureInfo.CurrentCulture)
            : now.ToString("h:mm tt", CultureInfo.CurrentCulture);
        _dateText.Text = now.ToString("dddd d MMMM", CultureInfo.CurrentCulture);
    }
}

/// <summary>One square tile on the Dashboard row.</summary>
internal sealed class DashboardTile : Grid, IDisposable
{
    private readonly Border _ring;
    private readonly Border _plate;
    private readonly TextBlock _label;
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly TranslateTransform _lift = new();
    private bool _selected;

    public DashboardTile(HomeItem item, AppHost host, double size, Color accent)
    {
        Width = size;
        Height = size + 46;
        RenderTransformOrigin = new Point(0.5, 0.5);
        RenderTransform = new TransformGroup { Children = { _scale, _lift } };
        Background = Brushes.Transparent;

        _plate = new Border
        {
            Width = size,
            Height = size,
            VerticalAlignment = VerticalAlignment.Top,
            ClipToBounds = true,
            Background = HomeArt.Frozen(Color.FromRgb(0x17, 0x1D, 0x1A)),
            Child = ConsoleArt.DashboardTileArt(item, host, size, accent),
        };
        _ring = new Border
        {
            Width = size + 12,
            Height = size + 12,
            Margin = new Thickness(-6, -6, -6, 0),
            VerticalAlignment = VerticalAlignment.Top,
            BorderThickness = new Thickness(4),
            BorderBrush = HomeArt.Frozen(accent),
            Opacity = 0,
            IsHitTestVisible = false,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = accent, BlurRadius = 34, ShadowDepth = 0, Opacity = 0.8 },
        };
        _label = HomeArt.Label(item.Title, 24, FontWeights.Bold, Brushes.White, 0.72);
        _label.VerticalAlignment = VerticalAlignment.Bottom;
        _label.HorizontalAlignment = HorizontalAlignment.Center;
        _label.MaxWidth = size;

        Children.Add(_plate);
        Children.Add(_ring);
        Children.Add(_label);
    }

    public void SetSelected(bool on, bool animate)
    {
        if (_selected == on) return;
        _selected = on;
        var ms = animate ? 220 : 1;
        Anim.To(_scale, ScaleTransform.ScaleXProperty, on ? 1.09 : 1, ms, Anim.Springy);
        Anim.To(_scale, ScaleTransform.ScaleYProperty, on ? 1.09 : 1, ms, Anim.Springy);
        Anim.To(_lift, TranslateTransform.YProperty, on ? -12 : 0, ms, Anim.EaseOut);
        Anim.To(_ring, OpacityProperty, on ? 1 : 0, animate ? 180 : 1);
        Anim.To(_label, OpacityProperty, on ? 1 : 0.72, animate ? 180 : 1);
        Panel.SetZIndex(this, on ? 2 : 0);
    }

    /// <summary>How visible this tile is; the row fades out towards the edges of the screen.</summary>
    public void SetFade(double opacity, bool animate)
    {
        if (animate && !Anim.Reduced) Anim.To(this, OpacityProperty, opacity, 220, Anim.EaseOut);
        else
        {
            BeginAnimation(OpacityProperty, null);
            Opacity = opacity;
        }
        IsHitTestVisible = opacity > 0.05;
    }

    public void PopIn(double delayMs)
    {
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240)) { BeginTime = TimeSpan.FromMilliseconds(delayMs), FillBehavior = FillBehavior.Stop });
        var grow = new DoubleAnimation(0.8, _selected ? 1.09 : 1, TimeSpan.FromMilliseconds(380)) { BeginTime = TimeSpan.FromMilliseconds(delayMs), EasingFunction = Anim.Springy, FillBehavior = FillBehavior.Stop };
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
    }

    public void Dispose()
    {
        _plate.Child = null;
        Children.Clear();
    }
}
