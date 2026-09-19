using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Couchtop.Core.Pals;

namespace Couchtop.App.Pals;

/// <summary>
/// Developer tool for promo videos: renders a Pal performing a scripted scene as a transparent PNG sequence at
/// 30 fps, driven through the same animator the app uses. Only reachable from snapshot mode.
/// </summary>
internal static class PalClipExporter
{
    public const int Fps = 30;

    /// <summary>
    /// Renders <paramref name="seconds"/> of animation to <c>dir/name/0000.png…</c>. The script runs before every
    /// frame with the view, the frame number and the time, and can start gestures, talk or change the pose.
    /// </summary>
    public static int Render(string dir, string name, PalProfile profile, int width, int height, AvatarFraming framing, double seconds,
        Action<AvatarView, int, double> script, double facing = 0, double? centerY = null, double? viewHeight = null)
    {
        const int supersample = 2;
        var folder = Path.Combine(dir, name);
        Directory.CreateDirectory(folder);
        var view = new AvatarView { Width = width * supersample, Height = height * supersample };
        view.SetProfile(profile);
        if (centerY is { } c && viewHeight is { } h) view.SetFraming(c, h, instant: true);
        else view.SetFraming(framing, instant: true);
        view.SnapFacing(facing);
        view.Animator!.Fidgets = false;
        view.Model!.WarmUpFaces();
        view.Measure(new Size(view.Width, view.Height));
        view.Arrange(new Rect(0, 0, view.Width, view.Height));
        // Let the idle pose settle so the first frame isn't a snap from the rest pose.
        view.Step(0.6, 18);

        var frames = (int)Math.Round(seconds * Fps);
        for (var i = 0; i < frames; i++)
        {
            script(view, i, i / (double)Fps);
            view.Advance(1.0 / Fps);
            view.UpdateLayout();
            var big = new RenderTargetBitmap(width * supersample, height * supersample, 96, 96, PixelFormats.Pbgra32);
            big.Render(view);
            var small = new TransformedBitmap(big, new ScaleTransform(1.0 / supersample, 1.0 / supersample));
            PalPortrait.Save(small, Path.Combine(folder, $"{i:0000}.png"));
        }
        return frames;
    }

    /// <summary>Renders any element (the hand pointer, a tile) to a transparent PNG at twice its size.</summary>
    public static void RenderElement(FrameworkElement element, double width, double height, string path, double scale = 2)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)(width * scale), (int)(height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(element);
        PalPortrait.Save(bitmap, path);
    }
}
