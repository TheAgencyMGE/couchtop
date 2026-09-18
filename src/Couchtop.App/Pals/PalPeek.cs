using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Couchtop.App.Controls;
using Couchtop.Core.Pals;

namespace Couchtop.App.Pals;

/// <summary>
/// On Couchtop screens without a Pal of their own (Files, Settings, the web...), the Pal pops in from the
/// bottom-right corner to say its line, then ducks back out of the way.
/// </summary>
public sealed class PalPeek : Viewbox
{
    private readonly AppHost _host;
    private readonly PalActor _actor;
    private readonly TranslateTransform _slide = new(360, 0);
    private readonly DispatcherTimer _leave;
    private bool _shown;

    public PalPeek(AppHost host)
    {
        _host = host;
        Stretch = Stretch.Uniform;
        IsHitTestVisible = true;
        var stage = new Grid { Width = 1920, Height = 1080, IsHitTestVisible = true };
        _actor = new PalActor(host, 240, 270)
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 10, -26),
            BubbleShift = -120,
            RenderTransform = _slide,
            Visibility = Visibility.Collapsed,
        };
        _actor.View.SetFraming(0.5, 1.28, instant: true);
        _actor.View.HitTestBody = true;
        _actor.View.SnapFacing(-28);
        stage.Children.Add(_actor);
        Child = stage;

        _leave = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _leave.Tick += (_, _) => Hide();
        host.Pals.Reacted += OnReacted;
    }

    private void OnReacted(PalReaction reaction, PalStage stage)
    {
        if (stage != PalStage.Peek) return;
        Show();
        _actor.React(reaction);
        _leave.Stop();
        _leave.Interval = TimeSpan.FromSeconds(Math.Clamp(3.4 + (reaction.Text?.Length ?? 0) * 0.055, 3.5, 7.5));
        _leave.Start();
    }

    private void Show()
    {
        _actor.Visibility = Visibility.Visible;
        if (_shown) return;
        _shown = true;
        _actor.Animator?.Play(PalGesture.Wave, PalMood.Happy);
        Slide(0, 420, Anim.Springy);
    }

    /// <summary>Tucks the Pal away (also when the user moves on to another screen).</summary>
    public void Hide()
    {
        _leave.Stop();
        if (!_shown) return;
        _shown = false;
        _actor.HideBubble();
        Slide(360, 320, Anim.EaseInOut, () =>
        {
            if (!_shown) _actor.Visibility = Visibility.Collapsed;
        });
    }

    internal void SnapshotShow(PalReaction reaction)
    {
        _actor.Visibility = Visibility.Visible;
        _shown = true;
        _slide.X = 0;
        if (_actor.Animator is { } animator) animator.Fidgets = false;
        _actor.React(reaction);
        _actor.View.Step(0.7, 20);
        if (reaction.Text is { } text) _actor.Say(text, 30);
        _actor.View.Step(0.05, 2);
    }

    private void Slide(double to, double ms, IEasingFunction easing, Action? done = null)
    {
        if (Anim.Reduced)
        {
            _slide.X = to;
            done?.Invoke();
            return;
        }
        var animation = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = easing };
        if (done is not null) animation.Completed += (_, _) => done();
        _slide.BeginAnimation(TranslateTransform.XProperty, animation);
    }
}
