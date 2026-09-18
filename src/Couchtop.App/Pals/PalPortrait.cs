using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Couchtop.Core.Pals;

namespace Couchtop.App.Pals;

/// <summary>Renders still pictures of a Pal (the Couchtop Bar button, the channel tile, snapshots).</summary>
public static class PalPortrait
{
    private static readonly Dictionary<string, BitmapSource> Cache = new();

    /// <summary>
    /// A transparent picture of <paramref name="profile"/>. Rendered at twice the size and scaled down, which
    /// smooths the edges the software renderer would otherwise leave jagged.
    /// </summary>
    public static BitmapSource Render(PalProfile profile, int width, int height, AvatarFraming framing,
        PalGesture gesture = PalGesture.None, PalMood mood = PalMood.Happy, double gestureTime = 0.6, double facing = 0)
    {
        var key = $"{System.Text.Json.JsonSerializer.Serialize(profile)}|{width}x{height}|{framing}|{gesture}|{mood}|{gestureTime}|{facing}";
        if (Cache.TryGetValue(key, out var cached)) return cached;

        const int supersample = 2;
        var view = new AvatarView { Width = width * supersample, Height = height * supersample };
        view.SetProfile(profile);
        view.SetFraming(framing, instant: true);
        view.SnapFacing(facing);
        view.Animator!.Fidgets = false;
        view.Animator.RestingMood = mood;
        view.Model!.WarmUpFaces();
        view.Measure(new Size(view.Width, view.Height));
        view.Arrange(new Rect(0, 0, view.Width, view.Height));
        // Settle the idle pose, then play into the gesture and stop at the requested moment.
        view.Step(0.5, 10);
        if (gesture != PalGesture.None)
        {
            view.Animator.Play(gesture, mood);
            view.Step(gestureTime, Math.Max(1, (int)(gestureTime * 30)));
        }
        view.UpdateLayout();

        var big = new RenderTargetBitmap(width * supersample, height * supersample, 96, 96, PixelFormats.Pbgra32);
        big.Render(view);
        var small = new TransformedBitmap(big, new ScaleTransform(1.0 / supersample, 1.0 / supersample));
        var result = new WriteableBitmap(small);
        result.Freeze();
        if (Cache.Count > 30) Cache.Clear();
        Cache[key] = result;
        return result;
    }

    public static void Save(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var fs = System.IO.File.Create(path);
        encoder.Save(fs);
    }
}
