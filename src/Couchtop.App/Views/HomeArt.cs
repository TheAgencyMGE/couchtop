using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Couchtop.App.Controls;
using Couchtop.Core.Channels;

namespace Couchtop.App.Views;

/// <summary>
/// Artwork for the Dashboard and Media Bar screens. Channel tiles reuse the same art the Channels menu draws,
/// so an app looks the same whichever menu style is on; the few shortcuts that have no channel get a glyph.
/// </summary>
public static class HomeArt
{
    /// <summary>A square (or any size) piece of channel art, clipped with rounded corners.</summary>
    public static FrameworkElement Tile(HomeItem item, AppHost host, double width, double height, double radius = 10)
    {
        var host_ = new Border
        {
            Width = width,
            Height = height,
            CornerRadius = new CornerRadius(radius),
            ClipToBounds = true,
            Background = Brushes.Transparent,
        };

        if (item.Channel is { } channel)
        {
            // The art is designed for a 372x222 tile; a Viewbox fills the square from the middle of it.
            var art = ChannelArtFactory.Build(channel, host, large: false, withTitle: false);
            host_.Child = new Viewbox
            {
                Stretch = Stretch.UniformToFill,
                Child = new Grid { Width = 372, Height = 222, Children = { art }, ClipToBounds = true },
            };
            return host_;
        }

        var back = new Grid();
        back.Background = new LinearGradientBrush(Color.FromRgb(0x2C, 0x34, 0x3B), Color.FromRgb(0x18, 0x1D, 0x22), 90);
        back.Children.Add(Glyph(item.Kind, Math.Min(width, height) * 0.42, Brushes.White));
        host_.Child = back;
        return host_;
    }

    /// <summary>Simple line art for the entries that are not channels.</summary>
    public static FrameworkElement Glyph(HomeItemKind kind, double size, Brush stroke)
    {
        var data = kind switch
        {
            // A monitor with a stand, an envelope, a globe.
            HomeItemKind.Desktop => "M 6,10 H 74 V 54 H 6 Z M 28,68 H 52 M 40,54 V 68",
            HomeItemKind.Board => "M 6,14 H 74 V 62 H 6 Z M 6,14 L 40,42 L 74,14",
            _ => "M 40,6 A 34,34 0 1 0 40,74 A 34,34 0 1 0 40,6 M 6,40 H 74 M 40,6 C 22,24 22,56 40,74 M 40,6 C 58,24 58,56 40,74",
        };
        var path = new Path
        {
            Data = Geometry.Parse(data),
            Stroke = stroke,
            StrokeThickness = 5,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Stretch = Stretch.Uniform,
            Width = size,
            Height = size,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        return path;
    }

    /// <summary>A plain head-and-shoulders mark, used where there is no Pal to show yet.</summary>
    public static FrameworkElement PersonGlyph(double size, Brush stroke) => new Path
    {
        Data = Geometry.Parse("M 40,14 A 14,14 0 1 0 40,42 A 14,14 0 1 0 40,14 M 14,72 C 14,54 26,48 40,48 C 54,48 66,54 66,72"),
        Stroke = stroke,
        StrokeThickness = 5,
        StrokeLineJoin = PenLineJoin.Round,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
        Stretch = Stretch.Uniform,
        Width = size,
        Height = size,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    public static TextBlock Label(string text, double size, FontWeight weight, Brush brush, double opacity = 1)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = size,
            FontWeight = weight,
            Foreground = brush,
            Opacity = opacity,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
        };
        block.SetResourceReference(TextBlock.FontFamilyProperty, "AppFont");
        return block;
    }

    /// <summary>Scale/translate transforms every animated element here uses.</summary>
    public static (ScaleTransform Scale, TranslateTransform Shift) Transforms(FrameworkElement element, double originX = 0.5, double originY = 0.5)
    {
        var scale = new ScaleTransform(1, 1);
        var shift = new TranslateTransform();
        element.RenderTransformOrigin = new Point(originX, originY);
        element.RenderTransform = new TransformGroup { Children = { scale, shift } };
        return (scale, shift);
    }

    public static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    /// <summary>Soft glow used behind selected items; frozen so it costs nothing to reuse.</summary>
    public static Brush Glow(Color color, double alpha = 0.55)
    {
        var brush = new RadialGradientBrush(Color.FromArgb((byte)(alpha * 255), color.R, color.G, color.B), Color.FromArgb(0, color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }

    public static void Fade(UIElement element, double to, double ms, double delay = 0) =>
        Anim.To(element, UIElement.OpacityProperty, to, ms, Anim.EaseOut, delay);
}
