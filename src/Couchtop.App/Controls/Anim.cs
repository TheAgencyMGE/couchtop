using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Couchtop.App.Controls;

public static class Anim
{
    public static bool Reduced => AppHost.CurrentOrNull?.Settings.Current.ReduceMotion == true;

    /// <summary>
    /// True when Windows can't hardware-accelerate WPF (software rendering). Then ambient animations drop to a low
    /// frame rate so the menu stays responsive; on a real GPU they run at a smooth 60 fps.
    /// </summary>
    public static bool LowPowerGraphics { get; set; }

    public static readonly IEasingFunction EaseOut = Freeze(new CubicEase { EasingMode = EasingMode.EaseOut });
    public static readonly IEasingFunction EaseInOut = Freeze(new CubicEase { EasingMode = EasingMode.EaseInOut });
    public static readonly IEasingFunction Springy = Freeze(new BackEase { Amplitude = 0.45, EasingMode = EasingMode.EaseOut });

    private static IEasingFunction Freeze(EasingFunctionBase e)
    {
        e.Freeze();
        return e;
    }

    public static DoubleAnimation To(IAnimatable target, DependencyProperty property, double to, double milliseconds,
        IEasingFunction? ease = null, double delayMs = 0, Action? completed = null, double? from = null, bool hold = true)
    {
        var animation = new DoubleAnimation
        {
            To = to,
            Duration = TimeSpan.FromMilliseconds(Reduced ? 1 : milliseconds),
            EasingFunction = ease,
            BeginTime = TimeSpan.FromMilliseconds(Reduced ? 0 : delayMs),
            FillBehavior = hold ? FillBehavior.HoldEnd : FillBehavior.Stop,
        };
        if (from.HasValue) animation.From = from;
        if (completed is not null) animation.Completed += (_, _) => completed();
        target.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
        return animation;
    }
}

/// <summary>
/// Declarative idle animations for channel artwork (Kind = Spin, SpinReverse, Bob, Sway, Pulse, Drift, Twinkle)
/// and theme backdrops (Rise, Fall, Float, Scan, Breathe, Flicker; tuned with Distance and Duration).
/// They only run for tiles on the visible page while the menu is in front, keeping idle CPU near zero.
/// </summary>
public static class IdleAnim
{
    public static readonly DependencyProperty KindProperty =
        DependencyProperty.RegisterAttached("Kind", typeof(string), typeof(IdleAnim), new PropertyMetadata(null));

    public static readonly DependencyProperty DistanceProperty =
        DependencyProperty.RegisterAttached("Distance", typeof(double), typeof(IdleAnim), new PropertyMetadata(180.0));

    public static string? GetKind(DependencyObject d) => (string?)d.GetValue(KindProperty);
    public static void SetKind(DependencyObject d, string? value) => d.SetValue(KindProperty, value);
    public static readonly DependencyProperty DurationProperty =
        DependencyProperty.RegisterAttached("Duration", typeof(double), typeof(IdleAnim), new PropertyMetadata(20.0));

    public static double GetDistance(DependencyObject d) => (double)d.GetValue(DistanceProperty);
    public static void SetDistance(DependencyObject d, double value) => d.SetValue(DistanceProperty, value);
    public static double GetDuration(DependencyObject d) => (double)d.GetValue(DurationProperty);
    public static void SetDuration(DependencyObject d, double value) => d.SetValue(DurationProperty, value);

    public static void Start(DependencyObject root, double phaseSeconds)
    {
        foreach (var element in Descendants(root))
        {
            var kind = GetKind(element);
            if (kind is null) continue;
            var (scale, rotate, translate) = Parts(element);
            var begin = TimeSpan.FromSeconds(phaseSeconds);
            switch (kind)
            {
                case "Spin":
                    rotate.BeginAnimation(RotateTransform.AngleProperty, Loop(0, 360, 10, false, null, begin));
                    break;
                case "SpinReverse":
                    rotate.BeginAnimation(RotateTransform.AngleProperty, Loop(0, -360, 6, false, null, begin));
                    break;
                case "Bob":
                    translate.BeginAnimation(TranslateTransform.YProperty, Loop(0, -7, 1.7, true, Anim.EaseInOut, begin));
                    break;
                case "Sway":
                    rotate.BeginAnimation(RotateTransform.AngleProperty, Loop(-5, 5, 2.4, true, Anim.EaseInOut, begin));
                    break;
                case "Pulse":
                    scale.BeginAnimation(ScaleTransform.ScaleXProperty, Loop(1, 1.07, 1.1, true, Anim.EaseInOut, begin));
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, Loop(1, 1.07, 1.1, true, Anim.EaseInOut, begin));
                    break;
                case "Drift":
                    translate.BeginAnimation(TranslateTransform.XProperty, Loop(0, -GetDistance(element), 14, false, null, begin));
                    break;
                case "Twinkle":
                    element.BeginAnimation(UIElement.OpacityProperty, Loop(0.15, 1, 0.9, true, Anim.EaseInOut, begin));
                    break;
                // Theme backdrops: slow, sparse loops that start and end off-screen so the restart is invisible.
                case "Rise":
                    CacheForScenery(element);
                    translate.BeginAnimation(TranslateTransform.YProperty, Travel(element, up: true, begin));
                    translate.BeginAnimation(TranslateTransform.XProperty, Loop(-14, 14, 3.1, true, Anim.EaseInOut, begin, SceneryFps));
                    break;
                case "Fall":
                    CacheForScenery(element);
                    translate.BeginAnimation(TranslateTransform.YProperty, Travel(element, up: false, begin));
                    translate.BeginAnimation(TranslateTransform.XProperty, Loop(-40, 40, 2.8, true, Anim.EaseInOut, begin, SceneryFps));
                    rotate.BeginAnimation(RotateTransform.AngleProperty, Loop(-50, 50, 2.1, true, Anim.EaseInOut, begin, SceneryFps));
                    break;
                case "Float":
                    CacheForScenery(element);
                    translate.BeginAnimation(TranslateTransform.XProperty, Loop(0, GetDistance(element), GetDuration(element), true, Anim.EaseInOut, begin, SceneryFps));
                    break;
                case "Turn":
                    CacheForScenery(element);
                    rotate.BeginAnimation(RotateTransform.AngleProperty, Loop(0, 360, GetDuration(element), false, null, begin, SceneryFps));
                    break;
                case "Scan":
                    CacheForScenery(element);
                    translate.BeginAnimation(TranslateTransform.YProperty, Loop(0, GetDistance(element), GetDuration(element), false, null, begin, SceneryFps));
                    break;
                case "Breathe":
                    CacheForScenery(element);
                    element.BeginAnimation(UIElement.OpacityProperty, Loop(0.45, 1, GetDuration(element), true, Anim.EaseInOut, begin, SceneryFps));
                    break;
                case "Flicker":
                    element.BeginAnimation(UIElement.OpacityProperty, FlickerLoop(begin));
                    break;
            }
        }
    }

    public static void Stop(DependencyObject root)
    {
        foreach (var element in Descendants(root))
        {
            if (GetKind(element) is null) continue;
            if (element.RenderTransform is TransformGroup { Children.Count: >= 3 } group)
            {
                var n = group.Children.Count;
                group.Children[n - 3].BeginAnimation(ScaleTransform.ScaleXProperty, null);
                group.Children[n - 3].BeginAnimation(ScaleTransform.ScaleYProperty, null);
                group.Children[n - 2].BeginAnimation(RotateTransform.AngleProperty, null);
                group.Children[n - 1].BeginAnimation(TranslateTransform.XProperty, null);
                group.Children[n - 1].BeginAnimation(TranslateTransform.YProperty, null);
            }
            element.BeginAnimation(UIElement.OpacityProperty, null);
        }
    }

    /// <summary>
    /// Ambient scenery and idle tile loops run at a smooth 60 fps on a real GPU (24 under software rendering).
    /// They used to be held to 24-30 fps, which read as a sudden drop to ~22 fps and visible judder on 90-144 Hz
    /// screens whenever the pointer rested. 60 keeps slow drifting motion smooth without making an idle menu on a
    /// 144 Hz screen redraw 144 times a second; anything that reacts to the user is not capped at all.
    /// </summary>
    private static int? SceneryFps => Anim.LowPowerGraphics ? 24 : 60;

    /// <summary>
    /// Moving scenery is rendered once into a GPU texture and then only re-composited each frame,
    /// instead of re-rasterizing big gradients and paths 24 times a second.
    /// </summary>
    private static void CacheForScenery(UIElement element)
    {
        if (element.CacheMode is null) element.CacheMode = new BitmapCache { SnapsToDevicePixels = false };
    }

    private static DoubleAnimation Loop(double from, double to, double seconds, bool autoReverse, IEasingFunction? ease, TimeSpan begin, int? fps = null)
    {
        var a = new DoubleAnimation(from, to, TimeSpan.FromSeconds(seconds))
        {
            AutoReverse = autoReverse,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = ease,
            BeginTime = begin,
        };
        // Ambient loops: 60 fps on a GPU, 30 under software rendering (see SceneryFps).
        var rate = fps ?? (Anim.LowPowerGraphics ? 30 : 60);
        if (rate is { } r) Timeline.SetDesiredFrameRate(a, r);
        a.Freeze();
        return a;
    }

    /// <summary>
    /// Seamless vertical loop for an element placed on a canvas of height Distance: it leaves one edge, re-enters
    /// from the opposite edge while fully off-screen, and arrives back where it was placed. Static renders (and
    /// reduced motion) therefore show every element at its designed position.
    /// </summary>
    private static DoubleAnimationUsingKeyFrames Travel(UIElement element, bool up, TimeSpan begin)
    {
        var canvasHeight = GetDistance(element);
        var top = Canvas.GetTop(element);
        if (double.IsNaN(top)) top = 0;
        var height = element is FrameworkElement { Height: > 0 and var h } ? h : Math.Max(1, element.RenderSize.Height);
        var duration = Math.Max(1, GetDuration(element));
        var speed = (canvasHeight + height) / duration;
        var exit = up ? -(top + height) : canvasHeight - top;
        var reenter = up ? canvasHeight - top : -(top + height);
        var t1 = TimeSpan.FromSeconds(Math.Abs(exit) / speed);

        var a = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(duration), RepeatBehavior = RepeatBehavior.Forever, BeginTime = begin };
        a.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        a.KeyFrames.Add(new LinearDoubleKeyFrame(exit, KeyTime.FromTimeSpan(t1)));
        a.KeyFrames.Add(new DiscreteDoubleKeyFrame(reenter, KeyTime.FromTimeSpan(t1 + TimeSpan.FromMilliseconds(1))));
        a.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(duration))));
        if (SceneryFps is { } rate) Timeline.SetDesiredFrameRate(a, rate);
        a.Freeze();
        return a;
    }

    /// <summary>A neon sign that mostly stays lit, with an occasional stutter.</summary>
    private static DoubleAnimationUsingKeyFrames FlickerLoop(TimeSpan begin)
    {
        var a = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(7), RepeatBehavior = RepeatBehavior.Forever, BeginTime = begin };
        foreach (var (t, v) in new[] { (0.0, 1.0), (4.6, 1.0), (4.66, 0.25), (4.72, 1.0), (4.8, 0.4), (4.95, 1.0), (7.0, 1.0) })
            a.KeyFrames.Add(new DiscreteDoubleKeyFrame(v, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(t))));
        Timeline.SetDesiredFrameRate(a, 30);
        a.Freeze();
        return a;
    }

    private static (ScaleTransform, RotateTransform, TranslateTransform) Parts(UIElement element)
    {
        if (element.RenderTransform is TransformGroup { IsFrozen: false, Children.Count: >= 3 } existing &&
            existing.Children[^3] is ScaleTransform s && existing.Children[^2] is RotateTransform r && existing.Children[^1] is TranslateTransform t)
        {
            return (s, r, t);
        }

        var group = new TransformGroup();
        var original = element.RenderTransform;
        var hadOriginal = original is not null && !ReferenceEquals(original, Transform.Identity) && !original.Value.IsIdentity;
        if (hadOriginal) group.Children.Add(original!.CloneCurrentValue());
        var scale = new ScaleTransform();
        var rotate = new RotateTransform();
        var translate = new TranslateTransform();
        group.Children.Add(scale);
        group.Children.Add(rotate);
        group.Children.Add(translate);
        element.RenderTransform = group;
        if (!hadOriginal) element.RenderTransformOrigin = new Point(0.5, 0.5);
        return (scale, rotate, translate);
    }

    private static IEnumerable<UIElement> Descendants(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is UIElement ui) yield return ui;
            foreach (var d in Descendants(child)) yield return d;
        }
    }
}
