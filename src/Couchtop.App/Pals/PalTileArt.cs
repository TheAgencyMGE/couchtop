using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Couchtop.App.Controls;
using Couchtop.Core.Pals;

namespace Couchtop.App.Pals;

/// <summary>
/// Artwork for the Pals channel tile: the user's own Pal waving hello, on a backdrop in the theme's accent
/// color. Before a Pal exists it shows an example and invites the user to make one.
/// </summary>
public static class PalTileArt
{
    private static readonly PalProfile Example = CreateExample();

    public static FrameworkElement Build(AppHost host)
    {
        var accent = (Application.Current.TryFindResource("AccentBrush") as SolidColorBrush)?.Color ?? Color.FromRgb(53, 180, 229);
        var canvas = new Canvas { Width = 400, Height = 240, ClipToBounds = true };
        canvas.Children.Add(new Rectangle
        {
            Width = 400,
            Height = 240,
            Fill = new LinearGradientBrush(AvatarMaterials.Lighten(accent, 0.62), AvatarMaterials.Lighten(accent, 0.12), 90),
        });
        canvas.Children.Add(At(new Ellipse { Width = 300, Height = 300, Fill = new SolidColorBrush(Color.FromArgb(46, 255, 255, 255)) }, 150, -40));
        canvas.Children.Add(At(new Ellipse { Width = 170, Height = 170, Fill = new SolidColorBrush(Color.FromArgb(34, 255, 255, 255)) }, -50, 130));

        var profile = host.Pals.Profile ?? Example;
        var picture = new Image
        {
            Source = PalPortrait.Render(profile, 240, 250, AvatarFraming.Bust, PalGesture.Wave, PalMood.Happy, 0.95, -14),
            Width = 240,
            Height = 250,
        };
        IdleAnim.SetKind(picture, "Bob");
        canvas.Children.Add(At(picture, 170, 6));

        foreach (var (x, y) in new[] { (132.0, 36.0), (370.0, 190.0) })
        {
            var sparkle = new Path { Data = Geometry.Parse("M10,0 L13,7 L20,10 L13,13 L10,20 L7,13 L0,10 L7,7 Z"), Fill = Brushes.White };
            IdleAnim.SetKind(sparkle, "Twinkle");
            canvas.Children.Add(At(sparkle, x, y));
        }

        var shadow = Label("Pals", AvatarMaterials.Darken(accent, 0.25));
        canvas.Children.Add(At(shadow, 26, 23));
        canvas.Children.Add(At(Label("Pals", Colors.White), 24, 20));

        if (!host.Pals.HasPal)
        {
            var chip = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(242, 255, 255, 255)),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(16, 3, 16, 5),
                Child = new TextBlock { Text = "Make yours", FontSize = 24, FontWeight = FontWeights.ExtraBold, Foreground = new SolidColorBrush(AvatarMaterials.Darken(accent, 0.3)) },
            };
            ((TextBlock)chip.Child).SetResourceReference(TextBlock.FontFamilyProperty, "AppFont");
            canvas.Children.Add(At(chip, 24, 186));
        }
        else
        {
            var name = Label(host.Pals.Profile!.Name, Colors.White);
            name.FontSize = 26;
            name.Opacity = 0.92;
            canvas.Children.Add(At(name, 26, 76));
        }
        return new Viewbox { Stretch = Stretch.UniformToFill, Child = canvas };
    }

    private static TextBlock Label(string text, Color color)
    {
        var label = new TextBlock { Text = text, FontSize = 44, FontWeight = FontWeights.ExtraBold, Foreground = new SolidColorBrush(color) };
        label.SetResourceReference(TextBlock.FontFamilyProperty, "AppFont");
        return label;
    }

    private static T At<T>(T element, double x, double y) where T : UIElement
    {
        Canvas.SetLeft(element, x);
        Canvas.SetTop(element, y);
        return element;
    }

    private static PalProfile CreateExample()
    {
        var example = PalProfile.Random(21);
        example.Hat = "none";
        example.Glasses = "none";
        example.HairStyle = "swept";
        example.TopStyle = "hoodie";
        example.EyeStyle = "round";
        return example.Normalize();
    }
}
