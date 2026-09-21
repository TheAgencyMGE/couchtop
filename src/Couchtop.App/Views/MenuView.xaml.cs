using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Couchtop.App.Controls;
using Couchtop.App.Services;
using Couchtop.Core.Channels;

namespace Couchtop.App.Views;

public partial class MenuView : UserControl, IHomeScreen, Pals.IPalHost, IDisposable
{
    public const double TileW = 372, TileH = 222, GapX = 38, GapY = 28, Left0 = 159, Top0 = 58, PageWidth = 1920;

    private readonly AppHost _host;
    private readonly MainWindow _window;
    private readonly List<ChannelTile> _tiles = new();
    private readonly DispatcherTimer _clockTimer;
    private readonly DispatcherTimer _shineTimer;
    private readonly DispatcherTimer _edgeTimer;
    private readonly Random _random = new();
    private int _page;
    private int _pageCount = 1;
    private bool _editMode;
    private bool _active;
    private int _hoverSlot = -1;
    private int _pickedSlot = -1;
    private bool _colonOn = true;
    private DateTime _lastWheel;

    private int _pressSlot = -1;
    private Point _pressPoint;
    private bool _dragging;
    private int _dragSource = -1;
    private int _dropSlot = -1;
    private double _dragX;
    private Image? _ghost;
    private readonly Pals.PalHomeLayer _pal;

    public MenuView(AppHost host, MainWindow window)
    {
        InitializeComponent();
        _host = host;
        _window = window;

        _clockTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();
        _shineTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4.5) };
        _shineTimer.Tick += (_, _) => ShineRandomTile();
        _edgeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _edgeTimer.Tick += (_, _) => FlipPageWhileDragging();

        host.ChannelsChanged += OnChannelsChanged;
        host.Pals.ProfileChanged += RebuildPages;
        host.MessagesChanged += OnMessagesChanged;

        PrevArrow.Click += (_, _) => GoToPage(_page - 1);
        NextArrow.Click += (_, _) => GoToPage(_page + 1);
        OptionsButton.Click += (_, _) => ShowOptions();
        DesktopButton.Click += (_, _) => _window.ShowWindowsDesktop();
        BoardButton.Click += (_, _) => _window.Navigate(new MessageBoardView(_host, _window), BoardButton.TranslatePoint(new Point(83, 83), _window.RootGrid));
        DoneButton.Click += (_, _) => SetEditMode(false);
        AddButton.Click += (_, _) => _ = ChannelDialogs.AddChannelAsync(_window, _host, null);
        RescanButton.Click += (_, _) =>
        {
            _host.RefreshChannelsInBackground();
            _window.ShowToast("Looking for newly installed apps…");
        };

        MouseWheel += OnMouseWheel;
        PreviewMouseMove += OnDragMove;
        PreviewMouseLeftButtonUp += OnMouseUp;
        LostMouseCapture += (_, _) =>
        {
            if (_dragging) CancelDrag();
            _pressSlot = -1;
        };

        // The Pal walks along the top of the bottom bar, above everything but the edit banner.
        _pal = new Pals.PalHomeLayer(host, () => BarShift.Y);
        Stage.Children.Insert(Stage.Children.IndexOf(EditBanner), _pal);
        PreviewMouseMove += (_, e) => _pal.Pointer(e.GetPosition(Stage));
        PreviewKeyDown += (_, _) => _pal.Activity();

        RebuildPages();
        UpdateClock();
        UpdateBadge();
    }

    public bool ShowsPal => _pal.ShowsPal;

    private void OnChannelsChanged(object? sender, EventArgs e) => RebuildPages();
    private void OnMessagesChanged(object? sender, EventArgs e) => UpdateBadge();

    /// <summary>Called when another menu style takes over, so nothing keeps running in the background.</summary>
    public void Dispose()
    {
        _host.ChannelsChanged -= OnChannelsChanged;
        _host.Pals.ProfileChanged -= RebuildPages;
        _host.MessagesChanged -= OnMessagesChanged;
        _clockTimer.Stop();
        _shineTimer.Stop();
        _edgeTimer.Stop();
        StopIdle();
        _pal.SetActive(false);
    }

    internal void SnapshotPal(double x, Core.Pals.PalGesture gesture, string? line) => _pal.SnapshotPose(x, gesture, line);

    public bool PlaysAmbience => true;
    public bool IsEditMode => _editMode;
    public FrameworkElement Element => this;
    public string StyleId => Core.Settings.MenuStyleCatalog.Channels;

    /// <summary>Channels, theme or text size changed.</summary>
    public void Rebuild() => RebuildPages();

    public void OnShown() => Focus();

    public void OnHidden()
    {
        SetHoverSlot(-1, fromPointer: true);
        if (_dragging) CancelDrag();
    }

    public bool HandleBack()
    {
        if (_dragging)
        {
            CancelDrag();
            return true;
        }
        if (_pickedSlot >= 0)
        {
            _tiles[_pickedSlot].SetDragSource(false);
            _pickedSlot = -1;
            return true;
        }
        if (_editMode)
        {
            SetEditMode(false);
            return true;
        }
        return false;
    }

    // ---------------------------------------------------------------- pages

    public void RebuildPages()
    {
        foreach (var tile in _tiles) tile.StopIdle();
        PageStrip.Children.Clear();
        _tiles.Clear();

        var layout = _host.Layout.Layout;
        _pageCount = LayoutEditor.PageCount(layout, includeSparePage: _editMode);
        _page = Math.Clamp(_page, 0, _pageCount - 1);

        for (var p = 0; p < _pageCount; p++)
        {
            for (var i = 0; i < ChannelLayout.SlotsPerPage; i++)
            {
                var slot = p * ChannelLayout.SlotsPerPage + i;
                var tile = new ChannelTile { Width = TileW, Height = TileH, Slot = slot };
                tile.SetChannel(LayoutEditor.At(layout, slot), _host, _editMode);
                Canvas.SetLeft(tile, p * PageWidth + Left0 + (i % 4) * (TileW + GapX));
                Canvas.SetTop(tile, Top0 + (i / 4) * (TileH + GapY));
                tile.MouseEnter += (_, _) =>
                {
                    if (!_dragging && _pressSlot < 0) SetHoverSlot(slot, fromPointer: true);
                };
                tile.MouseLeave += (_, _) =>
                {
                    if (!_dragging && _hoverSlot == slot && _pressSlot < 0) SetHoverSlot(-1, fromPointer: true);
                };
                tile.MouseLeftButtonDown += (_, e) => OnTilePressed(slot, e);
                PageStrip.Children.Add(tile);
                _tiles.Add(tile);
            }
        }

        StripShift.BeginAnimation(TranslateTransform.XProperty, null);
        StripShift.X = -_page * PageWidth;
        _hoverSlot = -1;
        _pickedSlot = -1;
        UpdateArrows();
        if (_active) StartPageIdle();
    }

    public void GoToPage(int page)
    {
        page = Math.Clamp(page, 0, _pageCount - 1);
        if (page == _page) return;
        StopIdle();
        SetHoverSlot(-1, fromPointer: true);
        _pal.PageTurned(Math.Sign(page - _page));
        _page = page;
        _host.Audio.Play(SoundEffect.Page);
        Anim.To(StripShift, TranslateTransform.XProperty, -page * PageWidth, 520, Anim.EaseInOut, completed: () =>
        {
            if (_active) StartPageIdle();
        });
        UpdateArrows();
    }

    private void UpdateArrows()
    {
        PrevArrow.Visibility = _page > 0 ? Visibility.Visible : Visibility.Hidden;
        NextArrow.Visibility = _page < _pageCount - 1 ? Visibility.Visible : Visibility.Hidden;
    }

    private IEnumerable<ChannelTile> PageTiles(int page) =>
        _tiles.Skip(page * ChannelLayout.SlotsPerPage).Take(ChannelLayout.SlotsPerPage);

    public void SetActive(bool active)
    {
        if (_active == active) return;
        _active = active;
        _pal.SetActive(active);
        if (active) _host.Pals.Start(_window);
        if (active)
        {
            StartPageIdle();
            _shineTimer.Start();
            _clockTimer.Interval = TimeSpan.FromSeconds(1);
        }
        else
        {
            StopIdle();
            _shineTimer.Stop();
            _clockTimer.Interval = TimeSpan.FromSeconds(15);
            if (!_dragging) SetHoverSlot(-1, fromPointer: true);
        }
        UpdateClock();
    }

    private void StartPageIdle()
    {
        var i = 0;
        foreach (var tile in PageTiles(_page)) tile.StartIdle(i++ * 0.13);
    }

    private void StopIdle()
    {
        foreach (var tile in _tiles) tile.StopIdle();
    }

    private void ShineRandomTile()
    {
        var candidates = PageTiles(_page).Where(t => t.Channel is not null && t.Slot != _hoverSlot).ToList();
        if (candidates.Count > 0) candidates[_random.Next(candidates.Count)].PlayShine();
    }

    public void PlayIntro()
    {
        var i = 0;
        foreach (var tile in PageTiles(_page)) tile.PopIn(80 + i++ * 45);
        Anim.To(BarShift, TranslateTransform.YProperty, 0, 620, Anim.EaseOut, from: 320);
    }

    // ---------------------------------------------------------------- hover, focus & activation

    private void SetHoverSlot(int slot, bool fromPointer)
    {
        if (_hoverSlot == slot) return;
        if (_hoverSlot >= 0 && _hoverSlot < _tiles.Count && _hoverSlot != _pickedSlot) _tiles[_hoverSlot].SetHighlighted(false);
        _hoverSlot = slot;
        if (slot < 0 || slot >= _tiles.Count)
        {
            _hoverSlot = -1;
            _window.ShowBubble(null);
            _pal.Hover(null, null);
            return;
        }

        var tile = _tiles[slot];
        if (!_editMode) _pal.Hover(tile.Channel, tile.TranslatePoint(new Point(TileW / 2, TileH), Stage));
        if (tile.Channel is not null || _editMode || !fromPointer) tile.SetHighlighted(true);
        var text = tile.Channel?.Title ?? (_editMode ? "Add a channel" : null);
        if (tile.Channel is not null) _host.Audio.Play(SoundEffect.Hover);
        if (fromPointer) _window.ShowBubble(text);
        else _window.ShowBubble(text, tile.TranslatePoint(new Point(TileW / 2, -6), _window.RootGrid));
    }

    public void SnapshotHover(int slot) => SetHoverSlot(slot, fromPointer: slot >= 0);

    public Point SlotCenter(int slot)
    {
        if (slot < 0 || slot >= _tiles.Count) return new Point(_window.ActualWidth / 2, _window.ActualHeight / 2);
        return _tiles[slot].TranslatePoint(new Point(TileW / 2, TileH / 2), _window.RootGrid);
    }

    public bool HandleKey(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Left: MoveFocus(-1, 0); return true;
            case Key.Right: MoveFocus(1, 0); return true;
            case Key.Up: MoveFocus(0, -1); return true;
            case Key.Down: MoveFocus(0, 1); return true;
            case Key.Enter or Key.Space:
                if (_hoverSlot >= 0)
                {
                    if (_editMode) KeyboardEditActivate();
                    else Activate(_hoverSlot);
                }
                else
                {
                    MoveFocus(0, 0);
                }
                return true;
            case Key.PageUp: GoToPage(_page - 1); return true;
            case Key.PageDown: GoToPage(_page + 1); return true;
            case Key.Home: GoToPage(0); return true;
            case Key.Delete when _editMode && _hoverSlot >= 0 && _tiles[_hoverSlot].Channel is { } channel:
                _ = ChannelDialogs.ConfirmRemoveAsync(_window, _host, channel);
                return true;
            case Key.F5:
                _host.RefreshChannelsInBackground();
                _window.ShowToast("Looking for newly installed apps…");
                return true;
        }
        return false;
    }

    private void MoveFocus(int dx, int dy)
    {
        if (_hoverSlot < 0)
        {
            SetHoverSlot(_page * ChannelLayout.SlotsPerPage, fromPointer: false);
            return;
        }
        var page = _hoverSlot / ChannelLayout.SlotsPerPage;
        var index = _hoverSlot % ChannelLayout.SlotsPerPage;
        var col = index % 4 + dx;
        var row = index / 4 + dy;
        if (row is < 0 or > 2) return;
        if (col < 0)
        {
            if (page == 0) return;
            page--;
            col = 3;
            GoToPage(page);
        }
        else if (col > 3)
        {
            if (page >= _pageCount - 1) return;
            page++;
            col = 0;
            GoToPage(page);
        }
        SetHoverSlot(page * ChannelLayout.SlotsPerPage + row * 4 + col, fromPointer: false);
    }

    private void KeyboardEditActivate()
    {
        var tile = _tiles[_hoverSlot];
        if (_pickedSlot < 0)
        {
            if (tile.Channel is null)
            {
                _ = ChannelDialogs.AddChannelAsync(_window, _host, _hoverSlot);
                return;
            }
            _pickedSlot = _hoverSlot;
            tile.SetDragSource(true);
            _host.Audio.Play(SoundEffect.Tick);
            _window.ShowToast("Choose a new spot with the arrow keys, then press Enter");
            return;
        }
        var from = _pickedSlot;
        _pickedSlot = -1;
        _tiles[from].SetDragSource(false);
        if (from != _hoverSlot && LayoutEditor.Move(_host.Layout.Layout, from, _hoverSlot)) _host.SaveLayout();
    }

    private void Activate(int slot)
    {
        if (slot < 0 || slot >= _tiles.Count) return;
        var channel = LayoutEditor.At(_host.Layout.Layout, slot);
        if (_editMode)
        {
            if (channel is null) _ = ChannelDialogs.AddChannelAsync(_window, _host, slot);
            else _ = ChannelDialogs.EditChannelAsync(_window, _host, channel);
            return;
        }
        if (channel is null) return;

        HomeActions.Open(_window, _host, HomeItem.For(channel), SlotCenter(slot), () => SetEditMode(true), () => _pal.ChannelOpened());
    }

    public void OpenBuiltIn(string id, Point? origin) =>
        HomeActions.OpenBuiltIn(_window, _host, id, origin, () => SetEditMode(true), () => _pal.ChannelOpened());

    public void SetEditMode(bool on)
    {
        if (_editMode == on) return;
        if (_dragging) CancelDrag();
        _editMode = on;
        EditBanner.IsHitTestVisible = on;
        Anim.To(BannerShift, TranslateTransform.YProperty, on ? 0 : -160, 420, on ? Anim.Springy : Anim.EaseInOut);
        Anim.To(EditBanner, OpacityProperty, on ? 1 : 0, 260);
        Anim.To(ViewportScale, ScaleTransform.ScaleXProperty, on ? 0.9 : 1, 420, Anim.EaseInOut);
        Anim.To(ViewportScale, ScaleTransform.ScaleYProperty, on ? 0.9 : 1, 420, Anim.EaseInOut);
        Anim.To(ViewportShift, TranslateTransform.YProperty, on ? 78 : 0, 420, Anim.EaseInOut);
        _host.Audio.Play(on ? SoundEffect.Select : SoundEffect.Back);
        RebuildPages();
    }

    private async void ShowOptions()
    {
        var result = await _window.ShowCustomDialogAsync(close =>
        {
            var stack = new StackPanel();
            stack.Children.Add(ViewKit.Text("Couchtop Menu", 52, FontWeights.ExtraBold, align: TextAlignment.Center));
            foreach (var (label, key) in new[]
                     {
                         ("Windows Desktop", "desktop"), ("Settings", "settings"), ("Customize Channels", "customize"),
                         ("Shell Mode & Safety", "shell"), ("Message Board", "board"), ("Power", "power"), ("Back", ""),
                     })
            {
                var button = ViewKit.Pill(label, () => close(key), 620);
                button.HorizontalAlignment = HorizontalAlignment.Stretch;
                button.Margin = new Thickness(0, 14, 0, 0);
                stack.Children.Add(button);
            }
            return ViewKit.Panel(stack, 860);
        });

        switch (result as string)
        {
            case "desktop": _window.ShowWindowsDesktop(); break;
            case "settings": _window.Navigate(new SettingsView(_host, _window)); break;
            case "customize": SetEditMode(true); break;
            case "shell":
                var settings = new SettingsView(_host, _window);
                _window.Navigate(settings);
                settings.SelectCategory("Shell Mode");
                break;
            case "board": _window.Navigate(new MessageBoardView(_host, _window)); break;
            case "power": _window.Navigate(new PowerView(_host, _window)); break;
        }
    }

    // ---------------------------------------------------------------- clock & badge

    private void UpdateClock()
    {
        var now = DateTime.Now;
        if (_host.Settings.Current.Clock24Hour)
        {
            Hours.Text = now.Hour.ToString(CultureInfo.InvariantCulture);
            AmPm.Visibility = Visibility.Collapsed;
        }
        else
        {
            var hour = now.Hour % 12;
            Hours.Text = (hour == 0 ? 12 : hour).ToString(CultureInfo.InvariantCulture);
            AmPm.Text = now.Hour < 12 ? "AM" : "PM";
            AmPm.Visibility = Visibility.Visible;
        }
        Minutes.Text = now.Minute.ToString("00", CultureInfo.InvariantCulture);
        DateText.Text = $"{now.ToString("ddd", CultureInfo.CurrentCulture)} {now.Month}/{now.Day}";
        _colonOn = !_active || !_colonOn;
        Colon.Opacity = _colonOn ? 1 : 0.25;
    }

    public void RefreshClock() => UpdateClock();

    private void UpdateBadge()
    {
        var unread = _host.UnreadMessages;
        Badge.Visibility = unread > 0 ? Visibility.Visible : Visibility.Collapsed;
        BadgeText.Text = unread > 9 ? "9+" : unread.ToString(CultureInfo.InvariantCulture);
    }

    // ---------------------------------------------------------------- mouse: click, wheel, drag & drop

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (DateTime.UtcNow - _lastWheel < TimeSpan.FromMilliseconds(380)) return;
        _lastWheel = DateTime.UtcNow;
        GoToPage(_page + (e.Delta < 0 ? 1 : -1));
        e.Handled = true;
    }

    private void OnTilePressed(int slot, MouseButtonEventArgs e)
    {
        e.Handled = true;
        Focus();
        _pressSlot = slot;
        _pressPoint = e.GetPosition(Stage);
        CaptureMouse();
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_pressSlot < 0) return;
        var slot = _pressSlot;
        var stripPoint = e.GetPosition(PageStrip);
        if (_dragging)
        {
            FinishDrag(stripPoint);
            _pressSlot = -1;
            ReleaseMouseCapture();
            e.Handled = true;
            return;
        }
        _pressSlot = -1;
        ReleaseMouseCapture();
        if (HitSlot(stripPoint) == slot)
        {
            Activate(slot);
            e.Handled = true;
        }
    }

    private void OnDragMove(object sender, MouseEventArgs e)
    {
        if (_pressSlot < 0 || !_editMode || e.LeftButton != MouseButtonState.Pressed) return;
        var stagePoint = e.GetPosition(Stage);
        if (!_dragging)
        {
            if ((stagePoint - _pressPoint).Length < 14 || LayoutEditor.At(_host.Layout.Layout, _pressSlot) is null) return;
            StartDrag(_pressSlot);
        }
        UpdateDrag(stagePoint, e.GetPosition(PageStrip));
    }

    private void StartDrag(int slot)
    {
        _dragging = true;
        _dragSource = slot;
        var tile = _tiles[slot];
        tile.SetHighlighted(false);
        var bitmap = new RenderTargetBitmap((int)(TileW * 1.5), (int)(TileH * 1.5), 144, 144, PixelFormats.Pbgra32);
        bitmap.Render(tile);
        _ghost = new Image { Source = bitmap, Width = TileW * 1.08, Height = TileH * 1.08, Opacity = 0.95 };
        DragLayer.Children.Add(_ghost);
        tile.SetDragSource(true);
        _window.ShowBubble(null);
        _host.Audio.Play(SoundEffect.Tick);
    }

    private void UpdateDrag(Point stagePoint, Point stripPoint)
    {
        if (_ghost is null) return;
        Canvas.SetLeft(_ghost, stagePoint.X - _ghost.Width / 2);
        Canvas.SetTop(_ghost, stagePoint.Y - _ghost.Height / 2);
        _dragX = stagePoint.X;

        var target = HitSlot(stripPoint);
        if (target != _dropSlot)
        {
            if (_dropSlot >= 0 && _dropSlot < _tiles.Count && _dropSlot != _dragSource) _tiles[_dropSlot].SetHighlighted(false);
            _dropSlot = target;
            if (target >= 0 && target < _tiles.Count && target != _dragSource)
            {
                _tiles[target].SetHighlighted(true);
                _host.Audio.Play(SoundEffect.Tick);
            }
        }

        var nearEdge = (stagePoint.X < 120 && _page > 0) || (stagePoint.X > PageWidth - 120 && _page < _pageCount - 1);
        if (nearEdge && !_edgeTimer.IsEnabled) _edgeTimer.Start();
        else if (!nearEdge) _edgeTimer.Stop();
    }

    private void FlipPageWhileDragging()
    {
        if (!_dragging)
        {
            _edgeTimer.Stop();
            return;
        }
        if (_dragX < 120) GoToPage(_page - 1);
        else if (_dragX > PageWidth - 120) GoToPage(_page + 1);
    }

    private void FinishDrag(Point stripPoint)
    {
        var target = HitSlot(stripPoint);
        var source = _dragSource;
        EndDragVisuals();
        if (target >= 0 && target != source && LayoutEditor.Move(_host.Layout.Layout, source, target))
        {
            _host.Audio.Play(SoundEffect.Select);
            _host.SaveLayout();
        }
    }

    private void CancelDrag()
    {
        EndDragVisuals();
        _host.Audio.Play(SoundEffect.Back);
    }

    private void EndDragVisuals()
    {
        _edgeTimer.Stop();
        if (_ghost is not null) DragLayer.Children.Remove(_ghost);
        _ghost = null;
        if (_dragSource >= 0 && _dragSource < _tiles.Count) _tiles[_dragSource].SetDragSource(false);
        if (_dropSlot >= 0 && _dropSlot < _tiles.Count) _tiles[_dropSlot].SetHighlighted(false);
        _dragging = false;
        _dragSource = -1;
        _dropSlot = -1;
    }

    private int HitSlot(Point stripPoint)
    {
        var page = (int)Math.Floor(stripPoint.X / PageWidth);
        if (page < 0 || page >= _pageCount) return -1;
        var localX = stripPoint.X - page * PageWidth - Left0 + GapX / 2;
        var localY = stripPoint.Y - Top0 + GapY / 2;
        if (localX < 0 || localY < 0) return -1;
        var col = (int)(localX / (TileW + GapX));
        var row = (int)(localY / (TileH + GapY));
        if (col > 3 || row > 2) return -1;
        return page * ChannelLayout.SlotsPerPage + row * 4 + col;
    }
}
