using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Couchtop.App.Services;
using Couchtop.Core.Platform;
using Couchtop.Core.Search;

namespace Couchtop.App.Views;

/// <summary>
/// The command palette: one box that searches channels, installed apps, open windows, settings, actions and
/// the user's files. Opens over anything with the palette shortcut or the search button on the Couchtop Bar.
/// </summary>
public sealed class CommandPaletteWindow : Window
{
    private readonly AppHost _host;
    private readonly SearchService _search;
    private readonly TextBox _box = new();
    private readonly ListBox _results = new();
    private readonly TextBlock _empty;
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(110) };
    private CancellationTokenSource? _query;

    public CommandPaletteWindow(AppHost host, MainWindow main)
    {
        _host = host;
        _search = new SearchService(host, main);

        Title = "Couchtop Search";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        Width = 980;
        Height = 660;
        SetResourceReference(FontFamilyProperty, "AppFont");

        var panel = new Border { Padding = new Thickness(26, 22, 26, 22) };
        panel.SetResourceReference(FrameworkElement.StyleProperty, "AppPanel");

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var search = new Grid();
        search.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        search.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var glass = new Path
        {
            Data = Geometry.Parse("M 26,26 L 38,38 M 4,17 A 13,13 0 1 0 30,17 A 13,13 0 1 0 4,17"),
            Width = 30,
            Height = 30,
            Stretch = Stretch.Uniform,
            StrokeThickness = 4,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Margin = new Thickness(6, 0, 16, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        glass.SetResourceReference(Shape.StrokeProperty, "AccentBrush");
        search.Children.Add(glass);

        _box.FontSize = 40;
        _box.BorderThickness = new Thickness(0);
        _box.Background = Brushes.Transparent;
        _box.VerticalAlignment = VerticalAlignment.Center;
        _box.SetResourceReference(ForegroundProperty, "TextBrush");
        _box.SetResourceReference(TextBox.CaretBrushProperty, "AccentBrush");
        _box.SetResourceReference(TextBox.FontFamilyProperty, "AppFont");
        _box.TextChanged += (_, _) =>
        {
            _debounce.Stop();
            _debounce.Start();
        };
        Grid.SetColumn(_box, 1);
        search.Children.Add(_box);
        layout.Children.Add(search);

        var placeholder = ViewKit.Text("Search apps, files, settings and open windows", 40, FontWeights.Normal, "SubtleTextBrush", wrap: false);
        placeholder.IsHitTestVisible = false;
        placeholder.Margin = new Thickness(52, 0, 0, 0);
        placeholder.VerticalAlignment = VerticalAlignment.Center;
        placeholder.SetBinding(VisibilityProperty, new System.Windows.Data.Binding(nameof(TextBox.Text))
        {
            Source = _box,
            Converter = new EmptyToVisibility(),
        });
        layout.Children.Add(placeholder);

        var divider = new Border { Height = 3, Margin = new Thickness(0, 16, 0, 10), VerticalAlignment = VerticalAlignment.Bottom };
        divider.SetResourceReference(Border.BackgroundProperty, "PanelBorderBrush");
        layout.Children.Add(divider);

        _results.SetResourceReference(ItemsControl.ItemContainerStyleProperty, "CardItem");
        _results.BorderThickness = new Thickness(0);
        _results.Background = Brushes.Transparent;
        _results.Margin = new Thickness(0, 22, 0, 0);
        _results.MouseDoubleClick += (_, _) => RunSelected();
        _results.PreviewMouseLeftButtonUp += (_, _) => RunSelected();
        ScrollViewer.SetHorizontalScrollBarVisibility(_results, ScrollBarVisibility.Disabled);
        // The result list is short, so skip virtualization: it renders instantly and survives off-screen renders.
        VirtualizingStackPanel.SetIsVirtualizing(_results, false);
        Grid.SetRow(_results, 1);
        layout.Children.Add(_results);

        _empty = ViewKit.Text("Nothing found", 30, FontWeights.Bold, "SubtleTextBrush", align: TextAlignment.Center);
        _empty.VerticalAlignment = VerticalAlignment.Center;
        _empty.Visibility = Visibility.Collapsed;
        Grid.SetRow(_empty, 1);
        layout.Children.Add(_empty);

        var hint = ViewKit.Text("Enter opens  ·  arrows choose  ·  Esc closes", 22, FontWeights.Normal, "SubtleTextBrush", wrap: false, align: TextAlignment.Center);
        hint.Margin = new Thickness(0, 14, 0, 0);
        Grid.SetRow(hint, 2);
        layout.Children.Add(hint);

        panel.Child = layout;
        Content = panel;

        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            _ = RunQuery();
        };
        PreviewKeyDown += OnKeyDown;
        Deactivated += (_, _) => Close();
        Closed += (_, _) => _query?.Cancel();
    }

    /// <summary>Centers the palette on the Couchtop screen and starts with the default suggestions.</summary>
    public void ShowPalette()
    {
        var monitor = Monitors.Choose(Monitors.Enumerate(), _host.Settings.Current.TargetMonitor);
        if (monitor is not null)
        {
            var scale = monitor.Scale <= 0 ? 1 : monitor.Scale;
            Left = monitor.X / scale + (monitor.Width / scale - Width) / 2;
            Top = monitor.Y / scale + Math.Max(40, monitor.Height / scale * 0.16);
        }
        Show();
        Activate();
        _box.Focus();
        _ = RunQuery();
    }

    /// <summary>Fills the palette in for a visual regression render, without showing it.</summary>
    internal async Task SnapshotPrepareAsync(string query)
    {
        _box.Text = query;
        _debounce.Stop(); // otherwise the debounce fires a second search that cancels this one mid-render
        await RunQuery();
    }

    private async Task RunQuery()
    {
        _query?.Cancel();
        var cts = new CancellationTokenSource();
        _query = cts;
        try
        {
            var items = await _search.QueryAsync(_box.Text, cts.Token);
            if (cts.IsCancellationRequested) return;
            _results.Items.Clear();
            foreach (var item in items) _results.Items.Add(Row(item));
            if (_results.Items.Count > 0) _results.SelectedIndex = 0;
            _empty.Visibility = _results.Items.Count == 0 && _box.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (OperationCanceledException)
        {
            // A newer keystroke replaced this search.
        }
        catch (Exception ex)
        {
            Core.Diagnostics.Log.Warn("Search failed", ex);
        }
    }

    private ListBoxItem Row(PaletteItem item)
    {
        var grid = new Grid { Margin = new Thickness(6, 3, 6, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var badge = ViewKit.Text(Glyph(item.Kind), 30, FontWeights.Bold, "AccentBrush", wrap: false, align: TextAlignment.Center);
        badge.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(badge);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(ViewKit.Text(item.Title, 24, FontWeights.Bold, wrap: false));
        if (!string.IsNullOrEmpty(item.Subtitle))
        {
            var subtitle = ViewKit.Text(item.Subtitle!, 20, FontWeights.Normal, "SubtleTextBrush", wrap: false);
            subtitle.TextTrimming = TextTrimming.CharacterEllipsis;
            text.Children.Add(subtitle);
        }
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var kind = ViewKit.Text(Label(item.Kind), 19, FontWeights.SemiBold, "SubtleTextBrush", wrap: false);
        kind.VerticalAlignment = VerticalAlignment.Center;
        kind.Margin = new Thickness(16, 0, 8, 0);
        Grid.SetColumn(kind, 2);
        grid.Children.Add(kind);

        return new ListBoxItem { Content = grid, Tag = item, Padding = new Thickness(0) };
    }

    private static string Glyph(SearchKind kind) => kind switch
    {
        SearchKind.Channel => "▣",
        SearchKind.App => "▶",
        SearchKind.Window => "❐",
        SearchKind.File => "▤",
        SearchKind.Folder => "▮",
        SearchKind.Setting => "⚙",
        _ => "✦",
    };

    private static string Label(SearchKind kind) => kind switch
    {
        SearchKind.Channel => "Channel",
        SearchKind.App => "App",
        SearchKind.Window => "Open",
        SearchKind.File => "File",
        SearchKind.Folder => "Folder",
        SearchKind.Setting => "Setting",
        _ => "Action",
    };

    private void RunSelected()
    {
        if (_results.SelectedItem is not ListBoxItem { Tag: PaletteItem item }) return;
        Close();
        try
        {
            item.Run();
        }
        catch (Exception ex)
        {
            Core.Diagnostics.Log.Error("Palette action failed: " + item.Title, ex);
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Close();
                break;
            case Key.Down:
                Move(1);
                break;
            case Key.Up:
                Move(-1);
                break;
            case Key.Enter:
                RunSelected();
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private void Move(int delta)
    {
        if (_results.Items.Count == 0) return;
        var index = _results.SelectedIndex + delta;
        _results.SelectedIndex = ((index % _results.Items.Count) + _results.Items.Count) % _results.Items.Count;
        _results.ScrollIntoView(_results.SelectedItem);
        _host.Audio.Play(SoundEffect.Hover);
    }

    private sealed class EmptyToVisibility : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            value is string { Length: > 0 } ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            System.Windows.Data.Binding.DoNothing;
    }
}
