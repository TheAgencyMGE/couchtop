using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using System.Windows.Threading;
using Couchtop.App.Controls;
using Couchtop.App.Views;
using Couchtop.Core.Pals;

namespace Couchtop.App.Pals;

/// <summary>A Couchtop screen that shows the user's Pal itself, so reactions play there rather than in a corner peek.</summary>
public interface IPalHost
{
    bool ShowsPal { get; }
}

/// <summary>
/// The user's Pal as a piece of UI: a live 3D figure with a speech bubble. Plays reactions from the director,
/// talks while the bubble is up and answers pokes. Rebuilds itself when the Pal's look changes.
/// </summary>
public sealed class PalActor : Grid
{
    private readonly AppHost _host;
    private readonly Border _bubble;
    private readonly TextBlock _bubbleText;
    private readonly ScaleTransform _bubbleScale = new(1, 1);
    private readonly DispatcherTimer _bubbleTimer;
    private bool _attached;

    public PalActor(AppHost host, double width, double height, AvatarFraming framing = AvatarFraming.FullBody)
    {
        _host = host;
        Width = width;
        Height = height;
        ClipToBounds = false;

        View = new AvatarView { MaxFps = Anim.LowPowerGraphics ? 30 : 60, Background = Brushes.Transparent, Cursor = Cursors.Hand };
        Children.Add(View);
        Framing = framing;

        _bubbleText = new TextBlock { FontSize = 30, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, MaxWidth = 470, TextAlignment = TextAlignment.Center };
        _bubbleText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        _bubbleText.SetResourceReference(TextBlock.FontFamilyProperty, "AppFont");
        var body = new Border { Child = _bubbleText, Padding = new Thickness(28, 14, 28, 16), CornerRadius = new CornerRadius(30), BorderThickness = new Thickness(4) };
        body.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
        body.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
        var tail = new Path { Data = Geometry.Parse("M 0,0 L 34,0 L 12,26 Z"), StrokeThickness = 4, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, -5, 20, 0) };
        tail.SetResourceReference(Shape.FillProperty, "PanelBrush");
        tail.SetResourceReference(Shape.StrokeProperty, "AccentBrush");
        var stack = new StackPanel();
        stack.Children.Add(body);
        stack.Children.Add(tail);
        _bubble = new Border
        {
            Child = stack,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Visibility = Visibility.Collapsed,
            RenderTransform = _bubbleScale,
            RenderTransformOrigin = new Point(0.5, 1),
            IsHitTestVisible = false,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Opacity = 0.22 },
        };
        Children.Add(_bubble);
        _bubbleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _bubbleTimer.Tick += (_, _) => HideBubble();

        View.Tick += _ => PlaceBubble();
        View.MouseLeftButtonUp += (_, e) =>
        {
            if (!Pokable) return;
            e.Handled = true;
            Poked?.Invoke();
            _host.Pals.Report(new PalEvent(PalEventKind.Poked));
        };

        Loaded += (_, _) => Attach(true);
        Unloaded += (_, _) => Attach(false);
        Rebuild();
    }

    public AvatarView View { get; }
    public AvatarAnimator? Animator => View.Animator;
    public bool Pokable { get; set; } = true;
    public bool IsSpeaking => _bubble.Visibility == Visibility.Visible;

    /// <summary>Extra horizontal nudge for the bubble so it stays on screen near the edges.</summary>
    public double BubbleShift { get; set; }

    public event Action? Poked;

    /// <summary>When set, the actor plays screen reactions itself whenever this returns true (its view is in front).</summary>
    public Func<bool>? AcceptsReactions { get; set; }

    public AvatarFraming Framing
    {
        set => View.SetFraming(value, instant: true);
    }

    private void Attach(bool on)
    {
        if (on == _attached) return;
        _attached = on;
        if (on)
        {
            _host.Pals.ProfileChanged += Rebuild;
            _host.Pals.Reacted += OnReacted;
        }
        else
        {
            _host.Pals.ProfileChanged -= Rebuild;
            _host.Pals.Reacted -= OnReacted;
        }
    }

    private void OnReacted(PalReaction reaction, PalStage stage)
    {
        if (stage == PalStage.Screen && AcceptsReactions?.Invoke() == true) React(reaction);
    }

    private void Rebuild()
    {
        if (_host.Pals.Profile is not { } profile) return;
        var facing = View.Facing;
        View.SetProfile(profile);
        View.SnapFacing(facing);
        View.Model?.WarmUpFaces();
    }

    public void React(PalReaction reaction)
    {
        if (reaction.Gesture != PalGesture.None) View.Animator?.Play(reaction.Gesture, reaction.Mood);
        if (reaction.Text is { } text) Say(text);
    }

    /// <summary>Shows a speech bubble; the Pal's mouth moves while it reads out the line.</summary>
    public void Say(string text, double? seconds = null)
    {
        _bubbleText.Text = text;
        var show = seconds ?? Math.Clamp(2.2 + text.Length * 0.055, 2.8, 7);
        View.Animator?.Talk(Math.Min(show - 0.6, 0.6 + text.Length * 0.045));
        _bubble.Visibility = Visibility.Visible;
        PlaceBubble();
        if (!Anim.Reduced)
        {
            var pop = new DoubleAnimation(0.4, 1, TimeSpan.FromMilliseconds(360)) { EasingFunction = Anim.Springy };
            _bubbleScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            _bubbleScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
            _bubble.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
        }
        _bubbleTimer.Stop();
        _bubbleTimer.Interval = TimeSpan.FromSeconds(show);
        _bubbleTimer.Start();
    }

    public void HideBubble()
    {
        _bubbleTimer.Stop();
        View.Animator?.StopTalking();
        if (_bubble.Visibility != Visibility.Visible) return;
        if (Anim.Reduced)
        {
            _bubble.Visibility = Visibility.Collapsed;
            return;
        }
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220));
        fade.Completed += (_, _) =>
        {
            if (!_bubbleTimer.IsEnabled) _bubble.Visibility = Visibility.Collapsed;
        };
        _bubble.BeginAnimation(OpacityProperty, fade);
    }

    private void PlaceBubble()
    {
        if (_bubble.Visibility != Visibility.Visible || View.Model is null) return;
        var top = View.Project(new Point3D(0, View.Model.HeadTop + 0.06, 0));
        // Measure the content, not the bubble: the bubble's own size includes the margin set below, and using it
        // would make the position flip back and forth every frame.
        var content = (UIElement)_bubble.Child;
        content.Measure(new Size(ActualWidth + 520, double.PositiveInfinity));
        var height = content.DesiredSize.Height;
        // Negative side margins give the bubble room to be wider than the Pal's own box.
        const double room = 260;
        _bubble.Margin = new Thickness(-room + BubbleShift * 2, top.Y - height, -room - BubbleShift * 2, 0);
    }
}
