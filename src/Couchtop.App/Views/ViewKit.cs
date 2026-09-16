using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Couchtop.App.Views;

/// <summary>Small helpers for building consistent console-style screens in code.</summary>
public static class ViewKit
{
    /// <summary>Multiplies every text size Couchtop draws (Settings › Display › Text size).</summary>
    public static double TextScale { get; set; } = 1.0;

    public static TextBlock Text(string text, double size = 30, FontWeight? weight = null, string brush = "TextBrush", bool wrap = true, TextAlignment align = TextAlignment.Left)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = size * TextScale,
            FontWeight = weight ?? FontWeights.Normal,
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis,
            TextAlignment = align,
        };
        tb.SetResourceReference(TextBlock.ForegroundProperty, brush);
        tb.SetResourceReference(TextBlock.FontFamilyProperty, "AppFont");
        return tb;
    }

    public static Button Pill(string text, Action onClick, double minWidth = 200, string style = "PillButton")
    {
        var b = new Button { Content = text, MinWidth = minWidth };
        b.SetResourceReference(FrameworkElement.StyleProperty, style);
        b.Click += (_, _) => onClick();
        return b;
    }

    public static ToggleButton Option(string text, bool isChecked, Action onClick)
    {
        var b = new ToggleButton { Content = text, IsChecked = isChecked };
        b.SetResourceReference(FrameworkElement.StyleProperty, "OptionPill");
        b.Click += (_, _) => onClick();
        return b;
    }

    public static Border Card(UIElement child, Thickness? padding = null, Thickness? margin = null)
    {
        var border = new Border
        {
            Child = child,
            Padding = padding ?? new Thickness(36, 26, 36, 26),
            Margin = margin ?? new Thickness(0, 0, 0, 18),
        };
        border.SetResourceReference(Border.CornerRadiusProperty, "CardCornerRadius");
        border.SetResourceReference(Border.BorderThicknessProperty, "PanelBorderThickness");
        border.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "PanelBorderBrush");
        return border;
    }

    public static Border Panel(UIElement child, double width, Thickness? padding = null)
    {
        var border = new Border { Child = child, Width = width, Padding = padding ?? new Thickness(64, 54, 64, 44) };
        border.SetResourceReference(FrameworkElement.StyleProperty, "AppPanel");
        return border;
    }

    public static StackPanel Horizontal(params UIElement[] children)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var c in children) panel.Children.Add(c);
        return panel;
    }

    /// <summary>
    /// Standard full-screen layout on a 1920x1080 design stage: header bar with title and a "Menu" button, then body.
    /// </summary>
    public static FrameworkElement Scaffold(string title, string? subtitle, UIElement body, Action onBack, out TextBlock subtitleBlock, params UIElement[] headerButtons)
    {
        var stage = new Grid { Width = 1920, Height = 1080 };
        stage.RowDefinitions.Add(new RowDefinition { Height = new GridLength(160) });
        stage.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var headerBack = new Border { BorderThickness = new Thickness(0, 0, 0, 5) };
        headerBack.SetResourceReference(Border.BackgroundProperty, "HeaderBrush");
        headerBack.SetResourceReference(Border.BorderBrushProperty, "BarLineBrush");
        Grid.SetColumnSpan(headerBack, 2);
        header.Children.Add(headerBack);

        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(80, 0, 30, 6), ClipToBounds = true };
        titles.Children.Add(Text(title, 62, FontWeights.ExtraBold, wrap: false));
        subtitleBlock = Text(subtitle ?? "", 28, FontWeights.Normal, "SubtleTextBrush", wrap: false);
        subtitleBlock.HorizontalAlignment = HorizontalAlignment.Left;
        if (!string.IsNullOrEmpty(subtitle)) titles.Children.Add(subtitleBlock);
        else
        {
            subtitleBlock.Visibility = Visibility.Collapsed;
            titles.Children.Add(subtitleBlock);
        }
        header.Children.Add(titles);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 50, 0) };
        foreach (var b in headerButtons) buttons.Children.Add(b);
        buttons.Children.Add(Pill("Menu", onBack, 240));
        Grid.SetColumn(buttons, 1);
        header.Children.Add(buttons);

        Grid.SetRow(header, 0);
        stage.Children.Add(header);

        var bodyHost = new Border { Child = body, Margin = new Thickness(70, 36, 70, 40) };
        Grid.SetRow(bodyHost, 1);
        stage.Children.Add(bodyHost);

        return new Viewbox { Stretch = Stretch.Uniform, Child = stage };
    }

    public static FrameworkElement Scaffold(string title, string? subtitle, UIElement body, Action onBack, params UIElement[] headerButtons) =>
        Scaffold(title, subtitle, body, onBack, out _, headerButtons);

    public static ScrollViewer Scroll(UIElement content) => new()
    {
        Content = content,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        PanningMode = PanningMode.VerticalOnly,
        Focusable = false,
    };

    public static ItemsPanelTemplate WrapPanelTemplate() => new(new FrameworkElementFactory(typeof(WrapPanel)));

    /// <summary>Turns raw BGRA pixels from the Windows shell into an image WPF can draw.</summary>
    public static System.Windows.Media.Imaging.BitmapSource? ToBitmap(Couchtop.Core.Native.RawImage? raw)
    {
        if (raw is null) return null;
        var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(raw.Width, raw.Height, 96, 96,
            System.Windows.Media.PixelFormats.Bgra32, null, raw.Pixels, raw.Width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    public static Image IconImage(double size)
    {
        var image = new Image { Width = size, Height = size, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        return image;
    }
}
