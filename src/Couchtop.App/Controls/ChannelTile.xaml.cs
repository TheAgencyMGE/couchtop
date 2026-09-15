using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Couchtop.Core.Channels;

namespace Couchtop.App.Controls;

public partial class ChannelTile : UserControl
{
    public static readonly DependencyProperty IsHighlightedProperty =
        DependencyProperty.Register(nameof(IsHighlighted), typeof(bool), typeof(ChannelTile), new PropertyMetadata(false));

    private bool _highlighted;

    public ChannelTile()
    {
        InitializeComponent();
        SizeChanged += (_, _) => UpdateClip();
    }

    public int Slot { get; set; }
    public Channel? Channel { get; private set; }

    /// <summary>Lets theme ornaments (TileDecorTemplate) react to the pointer.</summary>
    public bool IsHighlighted
    {
        get => (bool)GetValue(IsHighlightedProperty);
        private set => SetValue(IsHighlightedProperty, value);
    }

    private void UpdateClip()
    {
        if (Screen.ActualWidth <= 0) return;
        var chamfer = ThemeManager.Number("TileChamfer", 0);
        if (chamfer > 0)
        {
            Frame.Clip = Chamfer(Frame.ActualWidth, Frame.ActualHeight, chamfer);
            Screen.Clip = Chamfer(Screen.ActualWidth, Screen.ActualHeight, Math.Max(0, chamfer - 2.5));
            return;
        }
        var radius = ThemeManager.Number("TileClipRadius", 20);
        Frame.Clip = null;
        Screen.Clip = new RectangleGeometry(new Rect(0, 0, Screen.ActualWidth, Screen.ActualHeight), radius, radius);
    }

    /// <summary>Rectangle with the top-left and bottom-right corners cut at 45°.</summary>
    private static Geometry Chamfer(double w, double h, double c)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(c, 0), true, true);
            ctx.PolyLineTo(new[] { new Point(w, 0), new Point(w, h - c), new Point(w - c, h), new Point(0, h), new Point(0, c) }, true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    public void SetChannel(Channel? channel, AppHost host, bool editMode)
    {
        Channel = channel;
        if (channel is null)
        {
            Frame.SetResourceReference(Border.BackgroundProperty, "EmptyTileBrush");
            Frame.SetResourceReference(Border.BorderBrushProperty, "EmptyTileBorderBrush");
            Gloss.Visibility = Visibility.Collapsed;
            EditBadge.Visibility = Visibility.Collapsed;
            ArtHost.Content = editMode ? PlusGlyph() : null;
            return;
        }

        Frame.SetResourceReference(Border.BackgroundProperty, "TileBackgroundBrush");
        Frame.SetResourceReference(Border.BorderBrushProperty, "TileBorderBrush");
        Gloss.Visibility = Visibility.Visible;
        EditBadge.Visibility = editMode ? Visibility.Visible : Visibility.Collapsed;
        ArtHost.Content = ChannelArtFactory.Build(channel, host, large: false);
    }

    private static UIElement PlusGlyph()
    {
        var grid = new Grid { IsHitTestVisible = false };
        grid.Children.Add(new Rectangle { Width = 60, Height = 12, RadiusX = 6, RadiusY = 6, Fill = new SolidColorBrush(Color.FromArgb(0x70, 0x8A, 0x96, 0x9C)) });
        grid.Children.Add(new Rectangle { Width = 12, Height = 60, RadiusX = 6, RadiusY = 6, Fill = new SolidColorBrush(Color.FromArgb(0x70, 0x8A, 0x96, 0x9C)) });
        return grid;
    }

    public void SetHighlighted(bool on)
    {
        if (_highlighted == on) return;
        _highlighted = on;
        IsHighlighted = on;
        Anim.To(RootScale, ScaleTransform.ScaleXProperty, on ? 1.06 : 1, on ? 240 : 160, on ? Anim.Springy : Anim.EaseOut);
        Anim.To(RootScale, ScaleTransform.ScaleYProperty, on ? 1.06 : 1, on ? 240 : 160, on ? Anim.Springy : Anim.EaseOut);
        Anim.To(RootLift, TranslateTransform.YProperty, on ? -5 : 0, 180, Anim.EaseOut);
        Anim.To(Glow, OpacityProperty, on ? 1 : 0, 150);
        Frame.SetResourceReference(Border.BorderBrushProperty, on ? "AccentBrush" : Channel is null ? "EmptyTileBorderBrush" : "TileBorderBrush");
        if (on && Channel is not null && !Anim.Reduced) Wiggle();
    }

    private void Wiggle()
    {
        var a = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(520) };
        a.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        a.KeyFrames.Add(new EasingDoubleKeyFrame(-1.3, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(120)), Anim.EaseOut));
        a.KeyFrames.Add(new EasingDoubleKeyFrame(0.9, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(310)), Anim.EaseInOut));
        a.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(520)), Anim.EaseInOut));
        RootRotate.BeginAnimation(RotateTransform.AngleProperty, a);
    }

    public void PlayShine()
    {
        if (Anim.Reduced || Channel is null) return;
        Shine.Opacity = 1;
        var w = Math.Max(200, ActualWidth);
        Anim.To(ShineShift, TranslateTransform.XProperty, w * 1.3, 950, Anim.EaseInOut, from: -w * 1.3, completed: () => Shine.Opacity = 0);
    }

    public void StartIdle(double phaseSeconds)
    {
        if (Channel is null || Anim.Reduced) return;
        IdleAnim.Start(ArtHost, phaseSeconds);
    }

    public void StopIdle() => IdleAnim.Stop(ArtHost);

    public void PopIn(double delayMs)
    {
        if (Anim.Reduced) return;
        Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)) { BeginTime = TimeSpan.FromMilliseconds(delayMs), FillBehavior = FillBehavior.Stop });
        var grow = new DoubleAnimation(0.72, 1, TimeSpan.FromMilliseconds(420)) { BeginTime = TimeSpan.FromMilliseconds(delayMs), EasingFunction = Anim.Springy, FillBehavior = FillBehavior.Stop };
        RootScale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        RootScale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
    }

    public void SetDragSource(bool on) => Root.Opacity = on ? 0.3 : 1;
}

/// <summary>Builds tile artwork for any channel: built-in vector art, Steam/custom banners, or an icon card.</summary>
public static class ChannelArtFactory
{
    public static FrameworkElement Build(Channel channel, AppHost host, bool large)
    {
        if (channel.Kind == ChannelKind.BuiltIn && channel.BuiltInId is not null &&
            Application.Current.TryFindResource("Art." + channel.BuiltInId) is DataTemplate template)
        {
            return new ContentControl { Content = channel, ContentTemplate = template, Focusable = false, IsTabStop = false };
        }

        var root = new Grid { ClipToBounds = true };
        root.Background = CardBackground(ParseAccent(channel.AccentColor) ?? Color.FromRgb(0xB8, 0xDD, 0xF0));

        var bannerPath = !string.IsNullOrEmpty(channel.CustomArt) && File.Exists(channel.CustomArt) ? channel.CustomArt
            : !string.IsNullOrEmpty(channel.BannerImage) && File.Exists(channel.BannerImage) ? channel.BannerImage : null;

        var iconSize = large ? 300.0 : 118.0;
        var iconHost = new Grid { Width = iconSize, Height = iconSize, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, large ? -120 : -34, 0, 0) };
        iconHost.Children.Add(new Ellipse
        {
            Width = iconSize * 0.9,
            Height = iconSize * 0.16,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, -iconSize * 0.14),
            Fill = new RadialGradientBrush(Color.FromArgb(0x38, 0, 0, 0), Color.FromArgb(0, 0, 0, 0)),
        });
        if (Application.Current.TryFindResource("AppIconOrbTemplate") is DataTemplate orb)
        {
            // Glass themes sit each app icon inside a see-through bubble.
            iconHost.Children.Add(new ContentControl
            {
                Content = "orb",
                ContentTemplate = orb,
                Width = iconSize * 1.3,
                Height = iconSize * 1.3,
                Margin = new Thickness(-iconSize * 0.15),
                IsHitTestVisible = false,
                Focusable = false,
            });
        }
        var icon = new Image { Stretch = Stretch.Uniform, Margin = new Thickness(Application.Current.TryFindResource("AppIconOrbTemplate") is null ? 0 : iconSize * 0.12) };
        RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
        var letter = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(channel.Title) ? "?" : channel.Title.Trim()[..1].ToUpperInvariant(),
            FontSize = iconSize * 0.55,
            FontWeight = FontWeights.ExtraBold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        letter.SetResourceReference(TextBlock.FontFamilyProperty, "AppFont");
        var letterDisc = new Border { CornerRadius = new CornerRadius(iconSize * 0.25), Child = letter };
        letterDisc.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        iconHost.Children.Add(letterDisc);
        iconHost.Children.Add(icon);
        IdleAnim.SetKind(iconHost, "Bob");

        var title = new TextBlock
        {
            Text = channel.Title,
            FontSize = large ? 76 : 25,
            FontWeight = FontWeights.Bold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = large ? VerticalAlignment.Center : VerticalAlignment.Bottom,
            Margin = large ? new Thickness(80, 330, 80, 0) : new Thickness(14, 0, 14, 12),
        };
        title.SetResourceReference(TextBlock.FontFamilyProperty, "AppFont");
        title.SetResourceReference(TextBlock.ForegroundProperty, "TileTextBrush");

        root.Children.Add(iconHost);
        root.Children.Add(title);

        if (bannerPath is not null)
        {
            var banner = new Image { Stretch = Stretch.UniformToFill, Opacity = 0 };
            RenderOptions.SetBitmapScalingMode(banner, BitmapScalingMode.HighQuality);
            root.Children.Add(banner);
            _ = LoadBannerAsync(banner, bannerPath, large ? 1600 : 520, iconHost, title);
        }

        var iconSource = channel.IconSource ?? channel.Launch?.ShortcutPath ?? channel.Launch?.Path;
        if (!string.IsNullOrWhiteSpace(iconSource)) _ = LoadIconAsync(host, icon, letterDisc, root, iconSource, channel.AccentColor is null);
        return root;
    }

    private static async Task LoadBannerAsync(Image banner, string path, int width, UIElement iconHost, UIElement title)
    {
        var image = await Services.IconService.LoadImageFileAsync(path, width);
        if (image is null) return;
        banner.Source = image;
        iconHost.Visibility = Visibility.Collapsed;
        title.Visibility = Visibility.Collapsed;
        Anim.To(banner, UIElement.OpacityProperty, 1, 250);
    }

    private static async Task LoadIconAsync(AppHost host, Image icon, UIElement letterDisc, Panel root, string source, bool tint)
    {
        var result = await host.Icons.GetAsync(source, 256);
        if (result is null) return;
        icon.Source = result.Image;
        letterDisc.Visibility = Visibility.Collapsed;
        if (tint) root.Background = CardBackground(result.Dominant);
    }

    /// <summary>Tints an app card with the icon's dominant color, blended toward the theme's card color.</summary>
    public static Brush CardBackground(Color seed)
    {
        var mix = ThemeManager.ColorOf("CardMixColor", Colors.White);
        var top = Mix(seed, mix, ThemeManager.Number("CardMixTop", 0.86));
        var bottom = Mix(seed, mix, ThemeManager.Number("CardMixBottom", 0.62));
        var brush = new LinearGradientBrush(top, bottom, 90) { Opacity = ThemeManager.Number("CardOpacity", 1) };
        brush.Freeze();
        return brush;
    }

    public static Color Mix(Color a, Color b, double amount) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * amount),
        (byte)(a.G + (b.G - a.G) * amount),
        (byte)(a.B + (b.B - a.B) * amount));

    public static Color? ParseAccent(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        try
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
