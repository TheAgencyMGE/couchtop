using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Couchtop.App.Controls;
using Couchtop.Core.Channels;
using Couchtop.Core.Pals;

namespace Couchtop.App.Pals;

/// <summary>
/// The Pal on the home screen. Stands on the top edge of the menu's bottom bar, strolls between the spots
/// beside the clock, watches the pointer, looks up at tiles you hover, can be picked up and dropped, sits
/// down when things are quiet and falls asleep when you leave it alone for a while.
/// </summary>
public sealed class PalHomeLayer : Canvas
{
    private const double BoxWidth = 260, BoxHeight = 280;
    // The Pal stands about this far above the bottom of its box (see the framing below).
    private const double FeetInset = 38;
    private const double MinX = 560, MaxX = 1370;
    private static readonly double[] Spots = { 650, 1290 };

    private readonly AppHost _host;
    private readonly Func<double> _barOffset;
    private readonly PalActor _actor;
    private readonly DispatcherTimer _idleTimer;
    private readonly Random _random = new();
    private (double X, double Y)[] _ground = Array.Empty<(double, double)>();
    private double _x = Spots[0];
    private double _y;
    private double _targetX = Spots[0];
    private double _nextWander;
    private double _clock;
    private bool _active;
    private bool _arrivePending;
    private bool _held;
    private bool _falling;
    private double _fallSpeed;
    private Point? _pressAt;
    private Point _pointer = new(960, 540);
    private double _heldVelocity;
    private DateTime _lastInput = DateTime.Now;
    private bool _reportedBored, _reportedSleep;
    private DateTime _hoverStarted;
    private Channel? _hoverChannel;
    private bool _hoverReported;
    private double? _lookUpAtX;

    public PalHomeLayer(AppHost host, Func<double> barOffset)
    {
        _host = host;
        _barOffset = barOffset;
        Width = 1920;
        Height = 1080;
        ClipToBounds = false;
        BuildGround();

        _actor = new PalActor(host, BoxWidth, BoxHeight);
        // Show the Pal a little smaller than full-bleed so there is room below its feet for the shadow.
        _actor.View.SetFraming(0.5, 1.28, instant: true);
        Children.Add(_actor);
        _actor.View.Tick += OnTick;
        _actor.View.MouseLeftButtonDown += OnPress;
        _actor.View.MouseMove += OnDrag;
        _actor.View.MouseLeftButtonUp += OnRelease;
        _actor.Pokable = false;
        _actor.View.HitTestBody = true;

        _idleTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _idleTimer.Tick += (_, _) => CheckIdle();

        host.Pals.Reacted += OnReacted;
        host.Pals.ProfileChanged += () =>
        {
            if (!_seenPal && _host.Pals.HasPal) _arrivePending = true;
            UpdateVisibility();
        };
        host.Pals.PreferencesChanged += UpdateVisibility;
        _seenPal = host.Pals.HasPal;
        UpdateVisibility();
        Place();
    }

    private bool _seenPal;
    private bool _arriving;

    /// <summary>True while the Pal is actually on screen, so reactions play here.</summary>
    public bool ShowsPal => _active && _host.Pals.HasPal && _host.Pals.Preferences.ShowOnHome;

    public void SetActive(bool active)
    {
        _active = active;
        UpdateVisibility();
        if (active)
        {
            _lastInput = DateTime.Now;
            _idleTimer.Start();
            if (_arrivePending) Arrive();
            else Dispatcher.BeginInvoke(_host.Pals.AnnounceIfPending, DispatcherPriority.ApplicationIdle);
        }
        else
        {
            _idleTimer.Stop();
            _actor.HideBubble();
            if (_held) Release(drop: true);
        }
    }

    private void UpdateVisibility()
    {
        var show = ShowsPal;
        _actor.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (_host.Pals.HasPal) _seenPal = true;
    }

    // ---------------------------------------------------------------- input from the menu

    /// <summary>The pointer moved (stage coordinates). Wakes the Pal and gives it something to look at.</summary>
    public void Pointer(Point stage)
    {
        _pointer = stage;
        Activity();
    }

    public void Activity()
    {
        var wasAsleep = _actor.Animator?.Base == AvatarBase.Sleep;
        var wasSitting = _actor.Animator?.Base == AvatarBase.Sit;
        _lastInput = DateTime.Now;
        _reportedBored = _reportedSleep = false;
        if (_actor.Animator is not { } animator || _held) return;
        if (wasAsleep)
        {
            animator.Base = AvatarBase.Idle;
            animator.Play(PalGesture.Surprised, PalMood.Surprised);
            _host.Pals.Report(new PalEvent(PalEventKind.WokeUp));
        }
        else if (wasSitting)
        {
            animator.Base = AvatarBase.Idle;
        }
    }

    /// <summary>A tile got the pointer: bottom-row tiles get a look, and a lingering hover a comment.</summary>
    public void Hover(Channel? channel, Point? tileBottom)
    {
        _lookUpAtX = tileBottom is { } p && p.Y > 700 ? p.X : null;
        if (!ReferenceEquals(channel, _hoverChannel))
        {
            _hoverChannel = channel;
            _hoverStarted = DateTime.Now;
            _hoverReported = false;
        }
    }

    public void ChannelOpened()
    {
        if (!ShowsPal || _actor.Animator is null) return;
        _actor.Animator.Play(PalGesture.Wave, PalMood.Happy);
    }

    public void PageTurned(int direction)
    {
        if (!ShowsPal || _actor.Animator is not { } animator || _held) return;
        animator.Play(PalGesture.Jump, PalMood.Excited);
        _actor.View.Facing = direction * 55;
        _nextWander = _clock + 2;
    }

    // ---------------------------------------------------------------- reactions

    private void OnReacted(PalReaction reaction, PalStage stage)
    {
        if (stage != PalStage.Screen || !ShowsPal) return;
        if (_held && reaction.Gesture is not (PalGesture.None or PalGesture.Surprised)) reaction = reaction with { Gesture = PalGesture.None };
        if (_actor.Animator?.Base is AvatarBase.Sit or AvatarBase.Sleep && reaction.Gesture is not (PalGesture.Sleep or PalGesture.Sit))
            _actor.Animator.Base = AvatarBase.Idle;
        // Stop to talk, and turn to face the user.
        if (reaction.Text is not null && !_held)
        {
            _targetX = _x;
            _actor.View.Facing = 0;
        }
        _actor.BubbleShift = _x < 760 ? (760 - _x) * 0.5 : _x > 1200 ? -(_x - 1200) * 0.5 : 0;
        _actor.React(reaction);
    }

    /// <summary>A brand-new Pal drops in from above the clock.</summary>
    private void Arrive()
    {
        _arrivePending = false;
        if (_actor.Animator is not { } animator) return;
        _x = _targetX = Spots[0];
        _y = -BoxHeight;
        _falling = true;
        _arriving = true;
        _fallSpeed = 0;
        animator.Base = AvatarBase.Held;
        Place();
    }

    // ---------------------------------------------------------------- per frame

    private void OnTick(double dt)
    {
        _clock += dt;
        if (_actor.Animator is not { } animator) return;
        var ground = GroundY(_x) + _barOffset();

        if (_held)
        {
            // Follow the pointer, dangling from the top of the head.
            var targetX = Math.Clamp(_pointer.X, 60, 1860);
            _heldVelocity = (targetX - _x) / Math.Max(dt, 0.001);
            _x = targetX;
            _y = _pointer.Y - 40;
            animator.HeldSway = Math.Clamp(-_heldVelocity * 0.03, -30, 30);
        }
        else if (_falling)
        {
            _fallSpeed += 2600 * dt;
            _y += _fallSpeed * dt;
            var landing = ground - BoxHeight + FeetInset;
            if (_y >= landing)
            {
                _y = landing;
                _falling = false;
                animator.Base = AvatarBase.Idle;
                _host.Audio.Play(Services.SoundEffect.Hover);
                if (_arriving)
                {
                    _arriving = false;
                    _host.Pals.AnnounceIfPending();
                }
                else
                {
                    _host.Pals.Report(new PalEvent(PalEventKind.Dropped));
                }
            }
        }
        else
        {
            Walk(dt, animator);
            _y = ground - BoxHeight + FeetInset;
        }

        // Keep an eye on the pointer (or the tile being looked at).
        var headY = _y + 80;
        var lookX = _lookUpAtX ?? _pointer.X;
        var lookY = _lookUpAtX is not null ? 700 : _pointer.Y;
        var yaw = Math.Clamp((lookX - _x) / 14, -42, 42) - _actor.View.Facing * 0.3;
        var pitch = Math.Clamp((lookY - headY) / 16, -24, 18);
        animator.LookAt(yaw, pitch);

        var busy = animator.Base == AvatarBase.Walk || animator.IsGesturing || animator.IsTalking || _held || _falling;
        _actor.View.MaxFps = Anim.LowPowerGraphics ? (busy ? 30 : 20) : (busy ? 60 : 30);
        Place();

        // A hover that lingers may earn a remark about the tile.
        if (_hoverChannel is { } channel && !_hoverReported && DateTime.Now - _hoverStarted > TimeSpan.FromSeconds(1.6))
        {
            _hoverReported = true;
            _host.Pals.ReportHover(channel);
        }
    }

    private void Walk(double dt, AvatarAnimator animator)
    {
        var prefs = _host.Pals.Preferences;
        var calm = animator.Base is AvatarBase.Sit or AvatarBase.Sleep || animator.IsTalking || animator.IsGesturing;
        if (prefs.Wander && !calm && _clock > _nextWander && Math.Abs(_targetX - _x) < 1)
        {
            // Mostly hop between the two spots beside the clock; sometimes just shuffle a little.
            _targetX = _random.NextDouble() < 0.65 ? Spots.OrderByDescending(s => Math.Abs(s - _x)).First() : Math.Clamp(_x + (_random.NextDouble() - 0.5) * 240, MinX, MaxX);
            _nextWander = _clock + 16 + _random.NextDouble() * 22 * (animator.Personality == "chill" ? 1.6 : animator.Personality == "sporty" ? 0.6 : 1);
        }

        var distance = _targetX - _x;
        if (Math.Abs(distance) > 2 && !calm)
        {
            var speed = animator.Personality == "sporty" ? 190 : animator.Personality == "chill" ? 110 : 150;
            var step = Math.Sign(distance) * Math.Min(Math.Abs(distance), speed * dt);
            _x += step;
            animator.WalkSpeed = speed / 150;
            animator.Base = AvatarBase.Walk;
            _actor.View.Facing = Math.Sign(distance) * 72;
        }
        else if (animator.Base == AvatarBase.Walk)
        {
            _targetX = _x;
            animator.Base = AvatarBase.Idle;
            _actor.View.Facing = 0;
        }
    }

    private void Place()
    {
        SetLeft(_actor, _x - BoxWidth / 2);
        SetTop(_actor, _y);
    }

    // ---------------------------------------------------------------- picking up

    private void OnPress(object sender, MouseButtonEventArgs e)
    {
        _pressAt = e.GetPosition(this);
        _actor.View.CaptureMouse();
        e.Handled = true;
    }

    private void OnDrag(object sender, MouseEventArgs e)
    {
        var at = e.GetPosition(this);
        _pointer = at;
        if (_pressAt is not { } press || _held) return;
        if ((at - press).Length < 10) return;
        _held = true;
        _falling = false;
        _actor.HideBubble();
        if (_actor.Animator is { } animator) animator.Base = AvatarBase.Held;
        _actor.View.Facing = 0;
        _host.Pals.Report(new PalEvent(PalEventKind.PickedUp));
    }

    private void OnRelease(object sender, MouseButtonEventArgs e)
    {
        _actor.View.ReleaseMouseCapture();
        var wasHeld = _held;
        _pressAt = null;
        e.Handled = true;
        if (wasHeld)
        {
            Release(drop: true);
            return;
        }
        Activity();
        _host.Pals.Report(new PalEvent(PalEventKind.Poked));
    }

    private void Release(bool drop)
    {
        _held = false;
        _pressAt = null;
        _x = _targetX = Math.Clamp(_x, MinX, MaxX);
        _falling = drop;
        _fallSpeed = 0;
        _nextWander = _clock + 12;
    }

    // ---------------------------------------------------------------- quiet time

    private void CheckIdle()
    {
        if (!ShowsPal || _actor.Animator is not { } animator || _held) return;
        var idle = DateTime.Now - _lastInput;
        if (idle > TimeSpan.FromSeconds(50) && animator.Base == AvatarBase.Idle && !animator.IsTalking &&
            animator.Personality is "chill" or "curious" or "cheerful")
            animator.Base = AvatarBase.Sit;
        if (!_reportedBored && idle > TimeSpan.FromMinutes(2))
        {
            _reportedBored = true;
            _host.Pals.Report(new PalEvent(PalEventKind.MenuIdle) { Duration = idle });
        }
        if (!_reportedSleep && idle > TimeSpan.FromMinutes(6))
        {
            _reportedSleep = true;
            _host.Pals.Report(new PalEvent(PalEventKind.MenuIdle) { Duration = idle });
            animator.Base = AvatarBase.Sleep;
        }
    }

    // ---------------------------------------------------------------- the floor

    /// <summary>Samples the top edge of the menu's bottom bar (the same curve MenuView draws).</summary>
    private void BuildGround()
    {
        const double top = 1080 - 300;
        var points = new List<(double X, double Y)> { (-1200, 78) };
        void Curve((double X, double Y) p0, (double X, double Y) p1, (double X, double Y) p2, (double X, double Y) p3)
        {
            for (var i = 1; i <= 24; i++)
            {
                var t = i / 24.0;
                var u = 1 - t;
                points.Add((u * u * u * p0.X + 3 * u * u * t * p1.X + 3 * u * t * t * p2.X + t * t * t * p3.X,
                            u * u * u * p0.Y + 3 * u * u * t * p1.Y + 3 * u * t * t * p2.Y + t * t * t * p3.Y));
            }
        }
        points.Add((0, 78));
        Curve((0, 78), (120, 70), (250, 66), (330, 100));
        Curve((330, 100), (380, 122), (440, 125), (520, 125));
        points.Add((1400, 125));
        Curve((1400, 125), (1480, 125), (1540, 122), (1590, 100));
        Curve((1590, 100), (1670, 66), (1800, 70), (1920, 78));
        points.Add((3120, 78));
        _ground = points.Select(p => (p.X, p.Y + top)).ToArray();
    }

    private double GroundY(double x)
    {
        for (var i = 1; i < _ground.Length; i++)
        {
            if (_ground[i].X < x) continue;
            var (x0, y0) = _ground[i - 1];
            var (x1, y1) = _ground[i];
            var t = x1 - x0 < 1e-6 ? 0 : (x - x0) / (x1 - x0);
            // Stand a few pixels into the line so the feet look planted.
            return y0 + (y1 - y0) * t + 4;
        }
        return 1080 - 300 + 78;
    }

    /// <summary>Snapshot renders: settle at a spot, optionally saying something.</summary>
    internal void SnapshotPose(double x, PalGesture gesture, string? line)
    {
        _x = _targetX = x;
        _y = GroundY(x) - BoxHeight + FeetInset;
        _actor.Visibility = Visibility.Visible;
        _actor.View.SnapFacing(0);
        if (_actor.Animator is { } animator)
        {
            animator.Fidgets = false;
            if (gesture != PalGesture.None) animator.Play(gesture, PalMood.Happy);
        }
        _actor.View.Step(0.8, 24);
        Place();
        _actor.BubbleShift = x < 760 ? (760 - x) * 0.5 : 0;
        if (line is not null) _actor.Say(line, 30);
        _actor.View.Step(0.05, 2);
    }
}
