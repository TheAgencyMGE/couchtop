using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Couchtop.App.Controls;

public static class Anim
{
    public static bool Reduced => AppHost.CurrentOrNull?.Settings.Current.ReduceMotion == true;

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
/// Declarative idle animations for channel artwork (Kind = Spin, SpinReverse, Bob, Sway, Pulse, Drift, Twinkle).
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
    public static double GetDistance(DependencyObject d) => (double)d.GetValue(DistanceProperty);
    public static void SetDistance(DependencyObject d, double value) => d.SetValue(DistanceProperty, value);

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

    private static DoubleAnimation Loop(double from, double to, double seconds, bool autoReverse, IEasingFunction? ease, TimeSpan begin)
    {
        var a = new DoubleAnimation(from, to, TimeSpan.FromSeconds(seconds))
        {
            AutoReverse = autoReverse,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = ease,
            BeginTime = begin,
        };
        // Ambient loops are slow and subtle; 30 FPS halves render work while the menu idles.
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
