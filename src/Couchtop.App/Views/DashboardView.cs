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
/// The Dashboard home screen: a rail of plain lowercase tabs over a mosaic of flat tiles in two rows, the shape
/// late-2000s console dashboards settled on. Square corners, no gloss, one bright green for Couchtop's own
/// entries, and the section either side peeking in from the edge of the screen. All artwork is drawn in code.
/// </summary>
public sealed class DashboardView : UserControl, IHomeScreen, IDisposable
{
    // One tile unit; a hero tile is two units square, a wide tile two units across.
    private const double Unit = 228, Gap = 14, MosaicLeft = 150, MosaicTop = 268;
    private const double RowHeight = Unit * 2 + Gap;

    private static readonly Color Accent = Color.FromRgb(0x6C, 0xB4, 0x2C);
    private static readonly Color AccentBright = Color.FromRgb(0x8C, 0xD4, 0x3C);

    private readonly AppHost _host;
    private readonly MainWindow _window;
    private readonly Grid _stage = new() { Width = 1920, Height = 1080 };
    private readonly StackPanel _tabRail = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(150, 96, 0, 0), VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Canvas _mosaicHost = new() { ClipToBounds = true, Width = 1920, Height = RowHeight + 40, Margin = new Thickness(0, MosaicTop - 20, 0, 0), VerticalAlignment = VerticalAlignment.Top };
    private readonly Canvas _mosaic = new();
    private readonly TranslateTransform _mosaicShift = new();
    private readonly Border _edgeLeft, _edgeRight;
    private readonly TextBlock _edgeLeftText, _edgeRightText;
    private readonly TextBlock _clock, _itemTitle, _itemSubtitle, _profileLine;
    private readonly DispatcherTimer _clockTimer;

    private readonly List<HomeCategory> _categories = new();
    private readonly List<TextBlock> _tabs = new();
    private readonly List<DashboardTile> _tiles = new();
    private readonly List<TileSlot> _slots = new();
    private int _tab;
    private int _index;
    private double _mosaicTarget;
    private double _mosaicWidth;
    private Point _pointerAt = new(-1, -1);
    private bool _active;
    private bool _editMode;
    private DateTime _lastWheel;

    /// <summary>Where a tile sits in the mosaic, and which row it is on for up/down moves.</summary>
    private readonly record struct TileSlot(double X, double Y, double Width, double Height, int Row);

    public DashboardView(AppHost host, MainWindow window)
    {
        _host = host;
        _window = window;
        Focusable = true;
        FocusVisualStyle = null;
        Background = Brushes.Transparent;

        _stage.Children.Add(Backdrop());

        // The sections either side, peeking in from the edges the way these dashboards hinted at their neighbours.
        (_edgeLeft, _edgeLeftText) = EdgePanel(left: true);
        (_edgeRight, _edgeRightText) = EdgePanel(left: false);
        _stage.Children.Add(_edgeLeft);
        _stage.Children.Add(_edgeRight);

        _stage.Children.Add(_tabRail);

        _mosaic.RenderTransform = _mosaicShift;
        _mosaicHost.Children.Add(_mosaic);
        _stage.Children.Add(_mosaicHost);

        _itemTitle = HomeArt.Label("", 32, FontWeights.SemiBold, HomeArt.Frozen(Color.FromRgb(0xE4, 0xE9, 0xE5)));
        _itemSubtitle = HomeArt.Label("", 23, FontWeights.Normal, HomeArt.Frozen(Color.FromRgb(0x7E, 0x86, 0x80)));
        var detail = new StackPanel { Margin = new Thickness(MosaicLeft, MosaicTop + RowHeight + 46, 600, 0), VerticalAlignment = VerticalAlignment.Top };
        detail.Children.Add(_itemTitle);
        detail.Children.Add(_itemSubtitle);
        _stage.Children.Add(detail);

        // Top right: the clock, and a small plate standing in for a profile picture.
        _clock = HomeArt.Label("", 34, FontWeights.Normal, HomeArt.Frozen(Color.FromRgb(0xC8, 0xD0, 0xCA)));
        _clock.VerticalAlignment = VerticalAlignment.Center;
        _profileLine = HomeArt.Label("", 22, FontWeights.Normal, HomeArt.Frozen(Color.FromRgb(0x90, 0x98, 0x94)));
        _profileLine.VerticalAlignment = VerticalAlignment.Center;
        var profilePlate = new Border
        {
            Width = 54,
            Height = 54,
            Margin = new Thickness(22, 0, 0, 0),
            Background = HomeArt.Frozen(Accent),
            Child = ConsoleArt.Mark("M 10,10 H 36 V 36 H 10 Z M 44,10 H 70 V 36 H 44 Z M 10,44 H 36 V 70 H 10 Z M 44,44 H 70 V 70 H 44 Z", 28, Brushes.White, 6),
        };
        var topRight = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 92, 150, 0),
        };
        topRight.Children.Add(_profileLine);
        topRight.Children.Add(new Border { Width = 2, Height = 30, Margin = new Thickness(22, 0, 22, 0), Background = HomeArt.Frozen(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)), VerticalAlignment = VerticalAlignment.Center });
        topRight.Children.Add(_clock);
        topRight.Children.Add(profilePlate);
        _stage.Children.Add(topRight);

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

    /// <summary>Near-black, with one soft green wash in the corner and a vignette. Nothing moves.</summary>
    private static UIElement Backdrop()
    {
        var grid = new Grid();
        var back = new LinearGradientBrush(Color.FromRgb(0x12, 0x14, 0x12), Color.FromRgb(0x05, 0x06, 0x05), 90);
        back.Freeze();
        grid.Children.Add(new Rectangle { Fill = back });
        grid.Children.Add(new Ellipse
        {
            Width = 1900,
            Height = 1200,
            Margin = new Thickness(-600, -560, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Fill = HomeArt.Glow(Accent, 0.1),
        });
        return grid;
    }

    /// <summary>A dim panel at the very edge carrying the neighbouring section's name.</summary>
    private static (Border Panel, TextBlock Label) EdgePanel(bool left)
    {
        var label = HomeArt.Label("", 30, FontWeights.SemiBold, HomeArt.Frozen(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF)));
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.LayoutTransform = new RotateTransform(left ? -90 : 90);

        var panel = new Border
        {
            Width = 74,
            Height = RowHeight,
            Margin = left ? new Thickness(0, MosaicTop, 0, 0) : new Thickness(0, MosaicTop, 0, 0),
            HorizontalAlignment = left ? HorizontalAlignment.Left : HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Background = HomeArt.Frozen(Color.FromArgb(0x1E, 0xFF, 0xFF, 0xFF)),
            Child = label,
        };
        return (panel, label);
    }

    private UIElement Hints()
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(150, 0, 0, 64),
        };

        // The A button, drawn rather than borrowed: a green disc with a letter in it.
        var a = new Grid { Width = 42, Height = 42, Margin = new Thickness(0, 0, 14, 0) };
        a.Children.Add(new Ellipse { Fill = HomeArt.Frozen(Accent) });
        var letter = HomeArt.Label("A", 24, FontWeights.Bold, Brushes.White);
        letter.HorizontalAlignment = HorizontalAlignment.Center;
        letter.VerticalAlignment = VerticalAlignment.Center;
        a.Children.Add(letter);
        panel.Children.Add(a);

        var select = HomeArt.Label("Select", 26, FontWeights.Normal, HomeArt.Frozen(Color.FromRgb(0xD0, 0xD8, 0xD2)));
        select.VerticalAlignment = VerticalAlignment.Center;
        select.Margin = new Thickness(0, 0, 44, 0);
        panel.Children.Add(select);

        foreach (var (key, label) in new[] { ("LB / RB", "Sections"), ("Home", "Quick Menu") })
        {
            var chip = new Border
            {
                Background = HomeArt.Frozen(Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF)),
                Padding = new Thickness(12, 3, 12, 5),
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = HomeArt.Label(key, 22, FontWeights.SemiBold, Brushes.White),
            };
            panel.Children.Add(chip);
            var text = HomeArt.Label(label, 22, FontWeights.Normal, HomeArt.Frozen(Color.FromRgb(0x84, 0x8C, 0x86)));
            text.Margin = new Thickness(0, 0, 40, 0);
            text.VerticalAlignment = VerticalAlignment.Center;
            panel.Children.Add(text);
        }
        return panel;
    }

    // ---------------------------------------------------------------- building

    public void Rebuild()
    {
        var previous = SelectedItem()?.Title;
        _categories.Clear();
        _categories.AddRange(HomeCategories.Build(_host.Layout.Layout, _host.Settings.Current.Bookmarks, includePals: false));
        if (_categories.Count == 0) _categories.Add(new HomeCategory(HomeCategories.System, "System", new[] { new HomeItem(HomeItemKind.Desktop, "Windows Desktop") }));

        _tab = Math.Clamp(_tab, 0, _categories.Count - 1);
        BuildTabs();
        var keep = previous is null ? _index : _categories[_tab].Items.ToList().FindIndex(i => i.Title == previous);
        BuildMosaic(keep < 0 ? _index : keep, animate: false);
        UpdateProfile();
    }

    private void BuildTabs()
    {
        _tabRail.Children.Clear();
        _tabs.Clear();
        for (var i = 0; i < _categories.Count; i++)
        {
            var index = i;
            // Lowercase, plain text, no plate: the rail is type and nothing else.
            var tab = HomeArt.Label(_categories[i].Title.ToLowerInvariant(), 44, FontWeights.Normal, Brushes.White);
            tab.Margin = new Thickness(0, 0, 44, 0);
            tab.Cursor = Cursors.Arrow;
            tab.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                GoToTab(index);
            };
            tab.MouseEnter += (_, _) =>
            {
                if (index != _tab && PointerMoved()) _host.Audio.Play(SoundEffect.Hover);
            };
            _tabs.Add(tab);
            _tabRail.Children.Add(tab);
        }
        PaintTabs();
    }

    private void PaintTabs()
    {
        for (var i = 0; i < _tabs.Count; i++)
        {
            var selected = i == _tab;
            _tabs[i].FontSize = selected ? 50 : 42;
            _tabs[i].FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
            _tabs[i].Foreground = selected ? Brushes.White : HomeArt.Frozen(Color.FromRgb(0x77, 0x7F, 0x79));
        }
        if (_categories.Count > 0)
        {
            _edgeLeftText.Text = _tab > 0 ? _categories[_tab - 1].Title.ToLowerInvariant() : "";
            _edgeRightText.Text = _tab < _categories.Count - 1 ? _categories[_tab + 1].Title.ToLowerInvariant() : "";
            _edgeLeft.Visibility = _tab > 0 ? Visibility.Visible : Visibility.Hidden;
            _edgeRight.Visibility = _tab < _categories.Count - 1 ? Visibility.Visible : Visibility.Hidden;
        }
    }

    /// <summary>
    /// Lays the section's items out as a mosaic: a two-unit hero to open with, then columns of stacked tiles
    /// with a pair of wide ones every so often, so the wall of tiles is never a plain grid.
    /// </summary>
    private void BuildMosaic(int index, bool animate)
    {
        foreach (var tile in _tiles) tile.Dispose();
        _tiles.Clear();
        _slots.Clear();
        _mosaic.Children.Clear();

        var items = _categories[_tab].Items;
        var x = 0.0;
        var column = 0;
        var i = 0;
        while (i < items.Count)
        {
            var remaining = items.Count - i;
            if (i == 0)
            {
                _slots.Add(new TileSlot(x, 0, Unit * 2 + Gap, RowHeight, 0));
                x += Unit * 2 + Gap * 2;
            }
            else if (column % 4 == 3 && remaining >= 2)
            {
                // A pair of wide tiles, stacked.
                _slots.Add(new TileSlot(x, 0, Unit * 2 + Gap, Unit, 0));
                _slots.Add(new TileSlot(x, Unit + Gap, Unit * 2 + Gap, Unit, 1));
                x += Unit * 2 + Gap * 2;
            }
            else if (remaining >= 2)
            {
                _slots.Add(new TileSlot(x, 0, Unit, Unit, 0));
                _slots.Add(new TileSlot(x, Unit + Gap, Unit, Unit, 1));
                x += Unit + Gap;
            }
            else
            {
                _slots.Add(new TileSlot(x, 0, Unit, Unit, 0));
                x += Unit + Gap;
            }
            i = _slots.Count;
            column++;
        }
        _mosaicWidth = x;

        for (var s = 0; s < items.Count && s < _slots.Count; s++)
        {
            var slot = _slots[s];
            var at = s;
            var tile = new DashboardTile(items[s], _host, slot.Width, slot.Height, Accent, AccentBright);
            Canvas.SetLeft(tile, slot.X);
            Canvas.SetTop(tile, slot.Y);
            tile.MouseEnter += (_, _) => HoverTile(at);
            tile.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                Focus();
                if (at == _index) Activate();
                else Select(at, fromPointer: true);
            };
            _mosaic.Children.Add(tile);
            _tiles.Add(tile);
        }

        _index = Math.Clamp(index, 0, Math.Max(0, _tiles.Count - 1));
        for (var t = 0; t < _tiles.Count; t++) _tiles[t].SetSelected(t == _index, false);
        UpdateDetail();
        CenterOnSelection(animate);
        if (animate && !Anim.Reduced)
        {
            _mosaic.Opacity = 0.25;
            HomeArt.Fade(_mosaic, 1, 240);
        }
    }

    private void SetMosaicTarget(double target, bool animate)
    {
        // A wall that fits on screen is centred and never scrolls; a longer one stops at its ends.
        if (_mosaicWidth <= 1920 - MosaicLeft * 2)
        {
            _mosaicTarget = (1920 - _mosaicWidth) / 2;
        }
        else
        {
            var min = 1920 - MosaicLeft - _mosaicWidth;
            _mosaicTarget = Math.Clamp(target, min, MosaicLeft);
        }
        if (animate && !Anim.Reduced) Anim.To(_mosaicShift, TranslateTransform.XProperty, _mosaicTarget, 320, Anim.EaseOut);
        else
        {
            _mosaicShift.BeginAnimation(TranslateTransform.XProperty, null);
            _mosaicShift.X = _mosaicTarget;
        }
    }

    /// <summary>Keyboard and controller moves bring the selection towards the left of the wall.</summary>
    private void CenterOnSelection(bool animate)
    {
        if (_slots.Count == 0) return;
        SetMosaicTarget(MosaicLeft - _slots[Math.Min(_index, _slots.Count - 1)].X, animate);
    }

    /// <summary>Pointer moves leave the wall alone until the selection would run off an edge.</summary>
    private void KeepSelectionVisible(bool animate)
    {
        if (_slots.Count == 0) return;
        const double margin = 120;
        var slot = _slots[Math.Min(_index, _slots.Count - 1)];
        var left = _mosaicTarget + slot.X;
        var right = left + slot.Width;
        var target = _mosaicTarget;
        if (left < margin) target += margin - left;
        else if (right > 1920 - margin) target -= right - (1920 - margin);
        if (Math.Abs(target - _mosaicTarget) < 0.5) return;
        SetMosaicTarget(target, animate);
    }

    private HomeItem? SelectedItem() =>
        _categories.Count > 0 && _index >= 0 && _index < _categories[_tab].Items.Count ? _categories[_tab].Items[_index] : null;

    private void UpdateDetail()
    {
        var item = SelectedItem();
        _itemTitle.Text = item?.Title ?? "";
        _itemSubtitle.Text = item?.Subtitle ?? "";
    }

    private void UpdateProfile()
    {
        var apps = _categories.SelectMany(c => c.Items).Count(i => i.Channel is not null);
        _profileLine.Text = $"Couchtop · {apps} {(apps == 1 ? "item" : "items")}";
    }

    private void OnChannelsChanged(object? sender, EventArgs e) => Rebuild();

    // ---------------------------------------------------------------- navigation

    private void Select(int index, bool fromPointer)
    {
        if (_tiles.Count == 0) return;
        index = Math.Clamp(index, 0, _tiles.Count - 1);
        if (index == _index) return;
        _tiles[_index].SetSelected(false, true);
        _index = index;
        _tiles[_index].SetSelected(true, true);
        _host.Audio.Play(SoundEffect.Hover);
        UpdateDetail();
        if (fromPointer) KeepSelectionVisible(true);
        else CenterOnSelection(true);
    }

    /// <summary>True when the pointer is somewhere new since the last hover, so a moving wall cannot select.</summary>
    private bool PointerMoved()
    {
        var at = Mouse.GetPosition(this);
        if (Math.Abs(at.X - _pointerAt.X) < 3 && Math.Abs(at.Y - _pointerAt.Y) < 3) return false;
        _pointerAt = at;
        return true;
    }

    private void HoverTile(int slot)
    {
        if (!PointerMoved()) return;
        Select(slot, fromPointer: true);
    }

    /// <summary>Up and down move between the two rows of the same column, as the mosaic implies.</summary>
    private void MoveRow(int direction)
    {
        if (_slots.Count == 0) return;
        var current = _slots[_index];
        var wanted = current.Row + direction;
        for (var i = 0; i < _slots.Count; i++)
        {
            if (Math.Abs(_slots[i].X - current.X) > 1 || _slots[i].Row != wanted) continue;
            Select(i, fromPointer: false);
            return;
        }
        // A column with nothing above or below: move sections instead, so the stick is never dead.
        GoToTab(_tab + direction);
    }

    private void GoToTab(int tab)
    {
        if (_categories.Count == 0) return;
        tab = Math.Clamp(tab, 0, _categories.Count - 1);
        if (tab == _tab) return;
        _tab = tab;
        _host.Audio.Play(SoundEffect.Page);
        PaintTabs();
        BuildMosaic(0, animate: true);
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
        var slot = _slots[index];
        return _tiles[index].TranslatePoint(new Point(slot.Width / 2, slot.Height / 2), _window.RootGrid);
    }

    public bool HandleKey(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Left: Select(_index - 1, false); return true;
            case Key.Right: Select(_index + 1, false); return true;
            case Key.Up: MoveRow(-1); return true;
            case Key.Down: MoveRow(1); return true;
            case Key.PageUp: GoToTab(_tab - 1); return true;
            case Key.PageDown: GoToTab(_tab + 1); return true;
            case Key.Home: GoToTab(0); return true;
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
        UpdateClock();
    }

    public void PlayIntro()
    {
        if (Anim.Reduced) return;
        for (var i = 0; i < _tiles.Count && i < 12; i++) _tiles[i].PopIn(50 + i * 38);
        Anim.To(_tabRail, OpacityProperty, 1, 420, Anim.EaseOut, from: 0);
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
        var items = _categories.Count > 0 ? _categories[_tab].Items : Array.Empty<HomeItem>();
        for (var i = 0; i < items.Count; i++)
            if (items[i].Slot == slot) return TileCenter(i);
        return TileCenter(_index);
    }

    /// <summary>Snapshot rendering: pick a section and a tile directly.</summary>
    internal void SnapshotSelect(int tab, int index)
    {
        GoToTab(tab);
        Select(index, fromPointer: false);
    }

    public void SnapshotHover(int slot)
    {
        if (slot < 0) return;
        var items = _categories[_tab].Items;
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
    }
}

/// <summary>
/// One flat tile on the Dashboard wall: square corners, the artwork filling it, and the name on a strip along
/// the bottom. Selection is a white outline, the way these dashboards marked the tile you were on.
/// </summary>
internal sealed class DashboardTile : Grid, IDisposable
{
    private readonly Border _plate;
    private readonly Border _outline;
    private readonly Border _caption;
    private readonly TextBlock _label;
    private readonly ScaleTransform _scale = new(1, 1);
    private bool _selected;

    public DashboardTile(HomeItem item, AppHost host, double width, double height, Color accent, Color accentBright)
    {
        Width = width;
        Height = height;
        RenderTransformOrigin = new Point(0.5, 0.5);
        RenderTransform = _scale;
        Background = Brushes.Transparent;

        _plate = new Border
        {
            Width = width,
            Height = height,
            ClipToBounds = true,
            Child = ConsoleArt.DashboardTileArt(item, host, width, height, accent),
        };

        _label = HomeArt.Label(item.Title, height > 200 ? 26 : 22, FontWeights.SemiBold, Brushes.White);
        _label.Margin = new Thickness(16, 0, 12, 0);
        _label.VerticalAlignment = VerticalAlignment.Center;
        _label.MaxWidth = width - 24;
        _caption = new Border
        {
            Height = height > 200 ? 52 : 44,
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = HomeArt.Frozen(Color.FromArgb(0xC4, 0x00, 0x00, 0x00)),
            Child = _label,
        };

        _outline = new Border
        {
            BorderThickness = new Thickness(4),
            BorderBrush = HomeArt.Frozen(Colors.White),
            Opacity = 0,
            IsHitTestVisible = false,
        };

        Children.Add(_plate);
        Children.Add(_caption);
        Children.Add(_outline);
    }

    public void SetSelected(bool on, bool animate)
    {
        if (_selected == on) return;
        _selected = on;
        var ms = animate ? 160 : 1;
        Anim.To(_scale, ScaleTransform.ScaleXProperty, on ? 1.035 : 1, ms, Anim.EaseOut);
        Anim.To(_scale, ScaleTransform.ScaleYProperty, on ? 1.035 : 1, ms, Anim.EaseOut);
        Anim.To(_outline, OpacityProperty, on ? 1 : 0, ms);
        Anim.To(_caption, OpacityProperty, on ? 1 : 0.85, ms);
        Panel.SetZIndex(this, on ? 2 : 0);
    }

    public void PopIn(double delayMs)
    {
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)) { BeginTime = TimeSpan.FromMilliseconds(delayMs), FillBehavior = FillBehavior.Stop });
        var grow = new DoubleAnimation(0.9, _selected ? 1.035 : 1, TimeSpan.FromMilliseconds(320)) { BeginTime = TimeSpan.FromMilliseconds(delayMs), EasingFunction = Anim.EaseOut, FillBehavior = FillBehavior.Stop };
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
    }

    public void Dispose()
    {
        _plate.Child = null;
        Children.Clear();
    }
}
