using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Couchtop.Core.Channels;
using Couchtop.Core.Settings;

namespace Couchtop.App.Views;

public enum ConsoleStyle
{
    Dashboard,
    MediaBar,
}

/// <summary>
/// Artwork for the Dashboard and Media Bar shells. Nothing here is shared with the Channels menu: no glossy
/// cards, no rounded channel tiles, no illustrated channel art. Apps show their real Windows icon on a flat
/// plate, and Couchtop's own screens get a plain geometric mark drawn for the shell it appears in.
/// </summary>
public static class ConsoleArt
{
    public static ConsoleStyle StyleFor(string menuStyle) =>
        menuStyle == MenuStyleCatalog.MediaBar ? ConsoleStyle.MediaBar : ConsoleStyle.Dashboard;

    /// <summary>True while a console shell is on, so the rest of the app can drop the Channels-menu look.</summary>
    public static bool IsConsoleShell(AppHost? host = null)
    {
        var style = (host ?? AppHost.CurrentOrNull)?.Settings.Current.MenuStyle ?? MenuStyleCatalog.Channels;
        return style != MenuStyleCatalog.Channels;
    }

    // ---------------------------------------------------------------- marks

    /// <summary>
    /// One geometric mark per Couchtop screen. Flat, single-weight and drawn on a square grid: a folder, a
    /// picture, a globe, a gear, a power ring, three sliders, a trophy, a monitor, an envelope and a star.
    /// </summary>
    public static string MarkFor(HomeItem item) => item.Kind switch
    {
        HomeItemKind.Desktop => "M 8,14 H 72 V 54 H 8 Z M 30,66 H 50 M 40,54 V 66",
        HomeItemKind.Board => "M 8,18 H 72 V 62 H 8 Z M 8,18 L 40,44 L 72,18",
        HomeItemKind.Bookmark => "M 40,8 L 49,30 L 72,32 L 55,47 L 60,70 L 40,58 L 20,70 L 25,47 L 8,32 L 31,30 Z",
        _ => item.Channel is { Kind: ChannelKind.BuiltIn, BuiltInId: { } id } ? BuiltInMark(id) : "",
    };

    private static string BuiltInMark(string builtInId) => builtInId switch
    {
        BuiltInChannels.Files => "M 8,20 H 32 L 39,29 H 72 V 62 H 8 Z",
        BuiltInChannels.Photos => "M 8,16 H 72 V 62 H 8 Z M 8,52 L 27,34 L 42,48 L 54,38 L 72,54 M 56,28 A 5,5 0 1 0 56,27.9",
        BuiltInChannels.Browser => "M 40,8 A 32,32 0 1 0 40,72 A 32,32 0 1 0 40,8 M 8,40 H 72 M 40,8 C 24,25 24,55 40,72 M 40,8 C 56,25 56,55 40,72",
        BuiltInChannels.Settings => "M 40,28 A 12,12 0 1 0 40,52 A 12,12 0 1 0 40,28 M 40,8 V 18 M 40,62 V 72 M 8,40 H 18 M 62,40 H 72 M 17,17 L 24,24 M 56,56 L 63,63 M 63,17 L 56,24 M 24,56 L 17,63",
        BuiltInChannels.Power => "M 26,20 A 28,28 0 1 0 54,20 M 40,8 V 38",
        BuiltInChannels.Customize => "M 10,22 H 70 M 10,40 H 70 M 10,58 H 70 M 30,22 A 6,6 0 1 0 30,21.9 M 52,40 A 6,6 0 1 0 52,39.9 M 24,58 A 6,6 0 1 0 24,57.9",
        BuiltInChannels.Sports => "M 24,12 H 56 V 32 A 16,16 0 0 1 24,32 Z M 24,18 H 12 V 26 A 12,12 0 0 0 24,34 M 56,18 H 68 V 26 A 12,12 0 0 1 56,34 M 40,48 V 60 M 26,68 H 54",
        _ => "M 14,14 H 66 V 66 H 14 Z",
    };

    public static System.Windows.Shapes.Path Mark(string data, double size, Brush stroke, double thickness) => new()
    {
        Data = Geometry.Parse(data),
        Stroke = stroke,
        StrokeThickness = thickness,
        StrokeLineJoin = PenLineJoin.Miter,
        Stretch = Stretch.Uniform,
        Width = size,
        Height = size,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        SnapsToDevicePixels = true,
    };

    // ---------------------------------------------------------------- tiles and icons

    /// <summary>
    /// A Dashboard tile: a flat plate with the app's real icon, or Couchtop's mark for its own screens.
    /// Square corners, no gloss, no gradient card.
    /// </summary>
    public static FrameworkElement DashboardTileArt(HomeItem item, AppHost host, double size, Color accent)
    {
        var plate = new Grid { Width = size, Height = size, Background = Frozen(Color.FromRgb(0x1C, 0x20, 0x22)), ClipToBounds = true };

        // A thin colour bar along the bottom, the way these dashboards marked each section.
        plate.Children.Add(new Rectangle
        {
            Height = 6,
            VerticalAlignment = VerticalAlignment.Bottom,
            Fill = Frozen(accent),
        });

        var mark = MarkFor(item);
        if (mark.Length > 0)
        {
            plate.Children.Add(Mark(mark, size * 0.46, Brushes.White, 5));
            return plate;
        }

        if (item.Channel is not { } channel) return plate;

        var icon = new Image { Width = size * 0.56, Height = size * 0.56, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
        var initial = Initial(channel, size * 0.34);
        plate.Children.Add(initial);
        plate.Children.Add(icon);
        _ = LoadIconAsync(host, channel, icon, initial, plate, tintPlate: true);
        return plate;
    }

    /// <summary>
    /// A Media Bar icon: the app's icon, or a thin outlined mark, sitting straight on the background with no
    /// plate behind it — the way icons hang on the bar itself.
    /// </summary>
    public static FrameworkElement MediaBarIconArt(HomeItem item, AppHost host, double size)
    {
        var grid = new Grid { Width = size, Height = size, Background = Brushes.Transparent };

        var mark = MarkFor(item);
        if (mark.Length > 0)
        {
            grid.Children.Add(Mark(mark, size * 0.92, Brushes.White, 3.2));
            return grid;
        }

        if (item.Channel is not { } channel) return grid;

        var icon = new Image { Stretch = Stretch.Uniform, Width = size * 0.88, Height = size * 0.88, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
        var initial = Initial(channel, size * 0.5);
        grid.Children.Add(initial);
        grid.Children.Add(icon);
        _ = LoadIconAsync(host, channel, icon, initial, null, tintPlate: false);
        return grid;
    }

    /// <summary>
    /// The channel start screen and any other place that shows a big piece of art, drawn in shell style:
    /// the icon over a flat backdrop with the name underneath.
    /// </summary>
    public static FrameworkElement Card(Channel channel, AppHost host, bool large, ConsoleStyle style)
    {
        var item = HomeItem.For(channel);
        var root = new Grid { ClipToBounds = true, Background = Frozen(style == ConsoleStyle.MediaBar ? Color.FromArgb(0x30, 0x0A, 0x12, 0x22) : Color.FromRgb(0x14, 0x17, 0x19)) };
        var size = large ? 320.0 : 120.0;

        var content = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(style == ConsoleStyle.MediaBar ? MediaBarIconArt(item, host, size) : DashboardTileArt(item, host, size, Color.FromRgb(0x7B, 0xC6, 0x18)));
        if (large)
        {
            var title = new TextBlock
            {
                Text = channel.Title,
                FontSize = 58,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 34, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            title.SetResourceReference(TextBlock.FontFamilyProperty, "AppFont");
            content.Children.Add(title);
        }
        root.Children.Add(content);
        return root;
    }

    // ---------------------------------------------------------------- helpers

    private static TextBlock Initial(Channel channel, double size)
    {
        var letter = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(channel.Title) ? "?" : channel.Title.Trim()[..1].ToUpperInvariant(),
            FontSize = size,
            FontWeight = FontWeights.SemiBold,
            Foreground = Frozen(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        letter.SetResourceReference(TextBlock.FontFamilyProperty, "AppFont");
        return letter;
    }

    private static async Task LoadIconAsync(AppHost host, Channel channel, Image icon, UIElement initial, Panel? plate, bool tintPlate)
    {
        var banner = !string.IsNullOrEmpty(channel.CustomArt) && File.Exists(channel.CustomArt) ? channel.CustomArt : null;
        if (banner is not null && await Services.IconService.LoadImageFileAsync(banner, 512) is { } custom)
        {
            icon.Source = custom;
            initial.Visibility = Visibility.Collapsed;
            return;
        }

        var source = channel.IconSource ?? channel.Launch?.ShortcutPath ?? channel.Launch?.Path;
        if (string.IsNullOrWhiteSpace(source)) return;
        var result = await host.Icons.GetAsync(source, 256);
        if (result is null) return;
        icon.Source = result.Image;
        initial.Visibility = Visibility.Collapsed;
        // A hint of the icon's own colour in the plate, kept dark so the row still reads as one surface.
        if (tintPlate && plate is not null) plate.Background = Frozen(Mix(result.Dominant, Color.FromRgb(0x16, 0x1A, 0x1C), 0.78));
    }

    private static Color Mix(Color a, Color b, double amount) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * amount),
        (byte)(a.G + (b.G - a.G) * amount),
        (byte)(a.B + (b.B - a.B) * amount));

    public static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
