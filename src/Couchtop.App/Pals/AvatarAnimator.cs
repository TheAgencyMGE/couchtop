using Couchtop.Core.Pals;

namespace Couchtop.App.Pals;

/// <summary>What the Pal's body is doing underneath any gesture.</summary>
public enum AvatarBase
{
    Idle,
    Walk,
    Sit,
    Sleep,
    Held,
}

/// <summary>
/// Brings a Pal to life. Motions are procedural (sines, eases and little overshoots rather than keyframes), so
/// every gesture blends smoothly from whatever the Pal was doing. Also blinks, talks, follows a look target
/// and fidgets in character when left alone.
/// </summary>
public sealed class AvatarAnimator
{
    private readonly AvatarModel _model;
    private readonly Random _random = new();
    private AvatarBase _base = AvatarBase.Idle;
    private AvatarBase _previousBase = AvatarBase.Idle;
    private double _baseChanged = -10;
    private PalGesture _gesture;
    private double _gestureStart;
    private double _gestureLength;
    private PalMood _gestureMood;
    private double _talkUntil;
    private double _nextBlink = 2;
    private double _blinkUntil;
    private double _nextFidget = 6;
    private double _lookYaw, _lookPitch, _lookYawTarget, _lookPitchTarget;
    private double _walkPhase;
    private double _landedAt = -10;
    private AvatarPose _last = AvatarPose.Rest;

    public AvatarAnimator(AvatarModel model, string personality)
    {
        _model = model;
        Personality = personality;
    }

    public string Personality { get; set; }
    public double Time { get; private set; }
    public PalMood RestingMood { get; set; } = PalMood.Happy;
    public double WalkSpeed { get; set; } = 1;
    public bool Fidgets { get; set; } = true;
    public bool Mirror { get; set; }
    public double HeldSway { get; set; }
    public bool IsGesturing => _gesture != PalGesture.None && Time < _gestureStart + _gestureLength;
    public bool IsTalking => Time < _talkUntil;
    public AvatarPose Current => _last;

    public AvatarBase Base
    {
        get => _base;
        set
        {
            if (value == _base) return;
            _previousBase = _base;
            _base = value;
            _baseChanged = Time;
            if (_previousBase == AvatarBase.Held && value != AvatarBase.Held) _landedAt = Time;
        }
    }

    public void Play(PalGesture gesture, PalMood mood = PalMood.Happy)
    {
        if (gesture is PalGesture.Sleep) { Base = AvatarBase.Sleep; return; }
        if (gesture is PalGesture.Sit) { Base = AvatarBase.Sit; return; }
        _gesture = gesture;
        _gestureStart = Time;
        _gestureLength = Length(gesture);
        _gestureMood = mood;
        _nextFidget = Time + 8 + _random.NextDouble() * 6;
    }

    public void Talk(double seconds) => _talkUntil = Time + seconds;

    public void StopTalking() => _talkUntil = Time;

    /// <summary>Where the head should look, in degrees: yaw toward the Pal's left (+), pitch down (+).</summary>
    public void LookAt(double yaw, double pitch)
    {
        _lookYawTarget = Math.Clamp(yaw, -45, 45);
        _lookPitchTarget = Math.Clamp(pitch, -25, 25);
    }

    public void Update(double dt)
    {
        dt = Math.Clamp(dt, 0, 0.1);
        Time += dt;
        var t = Time;

        // Head follows the look target with a soft lag.
        var k = 1 - Math.Exp(-dt * 7);
        _lookYaw += (_lookYawTarget - _lookYaw) * k;
        _lookPitch += (_lookPitchTarget - _lookPitch) * k;

        if (_base == AvatarBase.Walk) _walkPhase += dt * Math.PI * 2 * 1.9 * WalkSpeed;

        var basePose = BasePose(_base, t);
        var baseBlend = Math.Clamp((t - _baseChanged) / 0.35, 0, 1);
        if (baseBlend < 1) basePose = AvatarPose.Lerp(BasePose(_previousBase, t), basePose, Ease(baseBlend));

        var pose = basePose;
        var mood = _base == AvatarBase.Sleep ? PalMood.Sleepy : RestingMood;
        if (_gesture != PalGesture.None)
        {
            var local = t - _gestureStart;
            if (local >= _gestureLength)
            {
                _gesture = PalGesture.None;
            }
            else
            {
                var weight = Math.Min(Ease(Math.Clamp(local / 0.22, 0, 1)), Ease(Math.Clamp((_gestureLength - local) / 0.3, 0, 1)));
                pose = AvatarPose.Lerp(basePose, GesturePose(_gesture, local, _gestureLength, basePose), weight);
                mood = _gestureMood;
            }
        }

        // Landing after being carried: a quick squash and recover.
        var sinceLanding = t - _landedAt;
        if (sinceLanding < 0.45) pose.Squash *= 1 - 0.16 * Math.Sin(sinceLanding / 0.45 * Math.PI) * (1 - sinceLanding / 0.45);

        if (_base is AvatarBase.Idle or AvatarBase.Walk)
        {
            pose.HeadYaw += _lookYaw;
            pose.HeadPitch += _lookPitch;
        }

        if (Fidgets && _base == AvatarBase.Idle && !IsGesturing && t > _nextFidget)
        {
            Play(Fidget(), RestingMood);
            _nextFidget = t + 9 + _random.NextDouble() * 9;
        }

        if (Mirror) pose = pose.Mirrored();
        _last = pose;
        _model.Apply(pose, dt);
        _model.SetFace(Face(mood, t));
    }

    private FaceState Face(PalMood mood, double t)
    {
        if (t > _nextBlink)
        {
            _blinkUntil = t + 0.11;
            // Sometimes a quick double blink, like people do.
            _nextBlink = t + (_random.NextDouble() < 0.18 ? 0.25 : 2.2 + _random.NextDouble() * 3.4);
        }
        var blink = t < _blinkUntil || _base == AvatarBase.Sleep;
        // Mouth flaps in an uneven rhythm while talking so it doesn't look mechanical.
        var talk = IsTalking && Math.Sin(t * 17) + 0.6 * Math.Sin(t * 29) > 0.1;
        var look = _lookYaw > 12 ? 1 : _lookYaw < -12 ? -1 : 0;
        if (Mirror) look = -look;
        var up = _lookPitch < -10 ? 1 : 0;
        return new FaceState(mood, blink, talk, look, up);
    }

    private PalGesture Fidget() => Personality switch
    {
        "chill" => Pick(PalGesture.Yawn, PalGesture.Stretch, PalGesture.LookAround, PalGesture.None),
        "sporty" => Pick(PalGesture.Stretch, PalGesture.Jump, PalGesture.Stretch, PalGesture.LookAround),
        "curious" => Pick(PalGesture.LookAround, PalGesture.Think, PalGesture.LookAround, PalGesture.Point),
        "cheeky" => Pick(PalGesture.Dance, PalGesture.LookAround, PalGesture.Spin, PalGesture.Laugh),
        _ => Pick(PalGesture.LookAround, PalGesture.Jump, PalGesture.Nod, PalGesture.Stretch),
    };

    private PalGesture Pick(params PalGesture[] options) => options[_random.Next(options.Length)];

    private static double Length(PalGesture g) => g switch
    {
        PalGesture.Wave => 2.2,
        PalGesture.Cheer => 1.7,
        PalGesture.Clap => 1.9,
        PalGesture.Jump => 1.0,
        PalGesture.Nod => 1.1,
        PalGesture.ShakeHead => 1.3,
        PalGesture.Think => 2.6,
        PalGesture.Shrug => 1.5,
        PalGesture.Point => 1.7,
        PalGesture.Surprised => 1.3,
        PalGesture.Laugh => 1.8,
        PalGesture.Bow => 1.8,
        PalGesture.Dance => 3.2,
        PalGesture.Stretch => 2.6,
        PalGesture.Yawn => 2.4,
        PalGesture.LookAround => 2.8,
        PalGesture.ThumbsUp => 1.6,
        PalGesture.Spin => 1.3,
        _ => 0,
    };

    // ---------------------------------------------------------------- base motion

    private AvatarPose BasePose(AvatarBase b, double t)
    {
        var p = AvatarPose.Rest;
        switch (b)
        {
            case AvatarBase.Idle:
            {
                var speed = Personality == "chill" ? 0.7 : Personality == "sporty" ? 1.25 : 1;
                var breathe = Math.Sin(t * 2.1 * speed);
                p.SpinePitch = 1.2 * breathe;
                p.HeadPitch = -1 + 0.8 * Math.Sin(t * 2.1 * speed + 0.6);
                p.HipRoll = 2.4 * Math.Sin(t * 0.55 * speed);
                p.SpineRoll = -1.6 * Math.Sin(t * 0.55 * speed);
                p.HeadRoll = (Personality == "chill" ? 5 : 1.5) + 1.5 * Math.Sin(t * 0.7);
                p.LArmOut = 8 + 1.5 * breathe;
                p.RArmOut = 8 + 1.5 * breathe;
                p.LArmForward = 2 + 2 * Math.Sin(t * 0.8);
                p.RArmForward = 2 + 2 * Math.Sin(t * 0.8 + 1.4);
                p.LLegOut = p.RLegOut = Personality == "sporty" ? 5 : 2;
                if (Personality == "cheerful") p.Lift = 0.004 * Math.Max(0, Math.Sin(t * 4.2));
                break;
            }
            case AvatarBase.Walk:
            {
                var s = Math.Sin(_walkPhase);
                var c = Math.Cos(_walkPhase);
                var amount = Math.Clamp(WalkSpeed, 0.4, 1.6);
                p.LLegForward = 26 * s * amount;
                p.RLegForward = -26 * s * amount;
                p.LKnee = 34 * Math.Max(0, -c) * amount + 6;
                p.RKnee = 34 * Math.Max(0, c) * amount + 6;
                p.LArmForward = -24 * s * amount;
                p.RArmForward = 24 * s * amount;
                p.LElbow = p.RElbow = 22;
                p.LArmOut = p.RArmOut = 10;
                p.Lift = 0.014 * Math.Abs(c) * amount;
                p.SpinePitch = 5 * amount;
                p.HipYaw = 7 * s;
                p.SpineYaw = -9 * s;
                p.HipRoll = 3 * c;
                p.HeadPitch = -3;
                break;
            }
            case AvatarBase.Sit:
            case AvatarBase.Sleep:
            {
                var sleeping = b == AvatarBase.Sleep;
                var breathe = Math.Sin(t * (sleeping ? 1.3 : 2));
                p.Lift = -0.2;
                p.LLegForward = p.RLegForward = 84;
                p.LKnee = p.RKnee = 70;
                p.LLegOut = p.RLegOut = 9;
                p.SpinePitch = (sleeping ? 16 : 4) + breathe * 1.5;
                p.HeadPitch = sleeping ? 26 + breathe * 3 : -2;
                p.HeadRoll = sleeping ? 12 : 4;
                p.LArmForward = p.RArmForward = sleeping ? 28 : 36;
                p.LElbow = p.RElbow = sleeping ? 50 : 40;
                p.LArmOut = p.RArmOut = 12;
                break;
            }
            case AvatarBase.Held:
            {
                // Dangling from where the pointer holds: legs and arms swing with the motion.
                var sway = Math.Clamp(HeldSway, -30, 30);
                var kick = Math.Sin(t * 9);
                p.Lift = 0;
                p.LLegForward = 14 * kick + sway * 0.4;
                p.RLegForward = -14 * kick + sway * 0.4;
                p.LKnee = 20 + 10 * Math.Max(0, kick);
                p.RKnee = 20 + 10 * Math.Max(0, -kick);
                p.LArmOut = p.RArmOut = 70 + 12 * Math.Sin(t * 7);
                p.LElbow = p.RElbow = 30;
                p.SpineRoll = -sway * 0.6;
                p.HeadRoll = -sway * 0.4;
                p.HeadPitch = -8;
                break;
            }
        }
        return p;
    }

    // ---------------------------------------------------------------- gestures

    private static double Ease(double x) => x * x * (3 - 2 * x);

    /// <summary>0 → 1 → 0 across [a, b].</summary>
    private static double Hump(double t, double a, double b) => t <= a || t >= b ? 0 : Math.Sin((t - a) / (b - a) * Math.PI);

    private static double Hop(double t, double start, double length, double height) =>
        t <= start || t >= start + length ? 0 : height * 4 * ((t - start) / length) * (1 - (t - start) / length);

    private AvatarPose GesturePose(PalGesture g, double t, double length, AvatarPose from)
    {
        var p = from;
        switch (g)
        {
            case PalGesture.Wave:
            {
                // Upper arm out to the side, forearm up, waving from the elbow.
                p.RArmOut = 100;
                p.RArmForward = 6;
                p.RArmTwist = -90;
                p.RElbow = 70 + 26 * Math.Sin(t * 11);
                p.HeadRoll = -8;
                p.SpineRoll = 4;
                p.HeadYaw = from.HeadYaw * 0.3;
                break;
            }
            case PalGesture.Cheer:
            {
                var up = Ease(Math.Clamp(t / 0.3, 0, 1));
                p.LArmOut = p.RArmOut = 20 + 108 * up;
                p.LArmForward = p.RArmForward = -6;
                p.LElbow = p.RElbow = 12;
                p.Lift = Hop(t, 0.2, 0.55, 0.13) + Hop(t, 0.85, 0.45, 0.07);
                p.LKnee = p.RKnee = 20 * Hump(t, 0.05, 0.25) + 18 * Hump(t, 0.72, 0.9);
                p.LLegForward = p.RLegForward = 10 * Hump(t, 0.05, 0.25);
                p.HeadPitch = -12;
                p.Squash = 1 - 0.1 * Hump(t, 0.05, 0.22) - 0.08 * Hump(t, 0.72, 0.86);
                break;
            }
            case PalGesture.Clap:
            {
                var clap = Math.Abs(Math.Sin(t * 10));
                p.LArmForward = p.RArmForward = 62;
                p.LArmOut = p.RArmOut = 6 + 16 * clap;
                p.LArmTwist = 30;
                p.RArmTwist = -30;
                p.LElbow = p.RElbow = 58;
                p.Lift = 0.006 * clap;
                p.HeadPitch = -6;
                break;
            }
            case PalGesture.Jump:
            {
                p.Lift = Hop(t, 0.25, 0.5, 0.16);
                var crouch = Hump(t, 0.0, 0.28) + Hump(t, 0.72, 0.95);
                p.LKnee = p.RKnee = 40 * crouch;
                p.LLegForward = p.RLegForward = 20 * crouch;
                p.SpinePitch = 12 * crouch;
                p.LArmOut = p.RArmOut = 20 + 90 * Hump(t, 0.25, 0.75);
                p.Squash = 1 - 0.12 * crouch;
                break;
            }
            case PalGesture.Nod:
                p.HeadPitch = from.HeadPitch + 14 * Math.Sin(t * Math.PI * 2 * 2.6) * (1 - t / length);
                break;
            case PalGesture.ShakeHead:
                p.HeadYaw = 22 * Math.Sin(t * Math.PI * 2 * 2.4) * (1 - t / length);
                p.HeadPitch = 4;
                break;
            case PalGesture.Think:
            {
                p.RArmForward = 72;
                p.RArmOut = 14;
                p.RArmTwist = -30;
                p.RElbow = 128;
                p.LArmForward = 30;
                p.LArmOut = 14;
                p.LArmTwist = 40;
                p.LElbow = 70;
                p.HeadRoll = 12;
                p.HeadPitch = -12;
                p.HeadYaw = 10 + 4 * Math.Sin(t * 2);
                break;
            }
            case PalGesture.Shrug:
            {
                var s = Hump(t, 0.1, length - 0.1);
                p.LArmOut = p.RArmOut = 26 + 8 * s;
                p.LArmForward = p.RArmForward = 22;
                p.LElbow = p.RElbow = 82;
                p.LArmTwist = -40;
                p.RArmTwist = 40;
                p.HeadRoll = 14 * s;
                p.SpineRoll = -3 * s;
                p.Lift = 0.008 * s;
                break;
            }
            case PalGesture.Point:
            {
                p.RArmForward = 40;
                p.RArmOut = 72;
                p.RElbow = 4;
                p.SpineYaw = -12;
                p.HeadYaw = -18;
                p.LArmOut = 14;
                p.Lift = Hop(t, 0.05, 0.3, 0.03);
                break;
            }
            case PalGesture.Surprised:
            {
                p.Lift = Hop(t, 0.0, 0.34, 0.09);
                p.LArmOut = p.RArmOut = 48;
                p.LElbow = p.RElbow = 72;
                p.LArmForward = p.RArmForward = 20;
                p.SpinePitch = -10;
                p.HeadPitch = -10;
                p.Squash = 1 + 0.06 * Hump(t, 0, 0.3);
                break;
            }
            case PalGesture.Laugh:
            {
                var shake = Math.Sin(t * 24);
                p.SpinePitch = -8 + 3 * shake;
                p.HeadPitch = -14 + 4 * shake;
                p.LArmForward = p.RArmForward = 34;
                p.LElbow = p.RElbow = 90;
                p.LArmOut = p.RArmOut = 6;
                p.Lift = 0.008 * Math.Abs(shake);
                break;
            }
            case PalGesture.Bow:
            {
                var down = Hump(t, 0.1, length - 0.05);
                p.SpinePitch = 38 * down;
                p.HeadPitch = 12 * down;
                p.LArmForward = p.RArmForward = 14 * down;
                p.LArmOut = p.RArmOut = 4;
                p.RArmForward = 50 * down;
                p.RElbow = 80 * down;
                break;
            }
            case PalGesture.Dance:
            {
                var beat = t * Math.PI * 2 * 1.8;
                var s = Math.Sin(beat);
                p.HipRoll = 10 * s;
                p.SpineRoll = -12 * s;
                p.HeadRoll = 10 * s;
                p.Lift = 0.022 * Math.Abs(Math.Sin(beat));
                p.LArmOut = 50 + 62 * Math.Max(0, s);
                p.RArmOut = 50 + 62 * Math.Max(0, -s);
                p.LElbow = p.RElbow = 50;
                p.LArmForward = p.RArmForward = 30;
                p.LLegForward = 16 * Math.Max(0, s);
                p.RLegForward = 16 * Math.Max(0, -s);
                p.LKnee = 22 * Math.Max(0, s);
                p.RKnee = 22 * Math.Max(0, -s);
                break;
            }
            case PalGesture.Stretch:
            {
                var reach = Hump(t, 0.1, length - 0.1);
                p.LArmOut = p.RArmOut = 20 + 112 * reach;
                p.LArmForward = p.RArmForward = -8 * reach;
                p.LElbow = p.RElbow = 4;
                p.SpineRoll = 12 * Math.Sin(t * 2.4) * reach;
                p.SpinePitch = -6 * reach;
                p.Lift = 0.02 * reach;
                p.HeadPitch = -16 * reach;
                break;
            }
            case PalGesture.Yawn:
            {
                var s = Hump(t, 0.1, length - 0.1);
                p.RArmForward = 70 * s;
                p.RArmOut = 18;
                p.RElbow = 130 * s;
                p.LArmOut = 20 + 100 * s;
                p.LElbow = 30;
                p.HeadPitch = -18 * s;
                p.SpinePitch = -6 * s;
                break;
            }
            case PalGesture.LookAround:
            {
                var yaw = t < length * 0.45 ? 40 * Ease(Math.Clamp(t / (length * 0.25), 0, 1)) : 40 - 80 * Ease(Math.Clamp((t - length * 0.45) / (length * 0.3), 0, 1));
                p.HeadYaw = yaw;
                p.SpineYaw = yaw * 0.25;
                p.HeadPitch = -4;
                break;
            }
            case PalGesture.ThumbsUp:
            {
                p.RArmForward = 70;
                p.RArmOut = 22;
                p.RElbow = 72;
                p.RArmTwist = -60;
                p.HeadRoll = -8;
                p.HeadPitch = from.HeadPitch + 10 * Hump(t, 0.4, 0.9);
                p.Lift = Hop(t, 0.05, 0.3, 0.025);
                break;
            }
            case PalGesture.Spin:
            {
                // Wrapped so fading out never unwinds the spin backwards.
                p.Turn = 360 * Ease(Math.Clamp(t / (length * 0.85), 0, 1)) % 360;
                p.Lift = Hop(t, 0, length * 0.85, 0.08);
                p.LArmOut = p.RArmOut = 55;
                break;
            }
        }
        return p;
    }
}
