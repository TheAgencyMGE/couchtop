namespace Couchtop.App.Pals;

/// <summary>
/// Joint angles for one frame, in degrees. Arms: Forward swings toward the viewer, Out raises sideways,
/// Twist turns the arm, Elbow bends the forearm forward. Legs: Forward swings the leg, Knee bends it back.
/// Head: Pitch looks down, Yaw turns toward the Pal's left (viewer's right), Roll tilts.
/// </summary>
public struct AvatarPose
{
    public double Lift;
    public double Shift;
    public double Squash;
    public double Turn;
    public double HipRoll, HipYaw;
    public double SpinePitch, SpineRoll, SpineYaw;
    public double HeadPitch, HeadYaw, HeadRoll;
    public double LArmForward, LArmOut, LArmTwist, LElbow;
    public double RArmForward, RArmOut, RArmTwist, RElbow;
    public double LLegForward, LKnee, LLegOut;
    public double RLegForward, RKnee, RLegOut;

    public static AvatarPose Rest => new()
    {
        Squash = 1,
        LArmOut = 8, RArmOut = 8,
        LElbow = 8, RElbow = 8,
        LArmForward = 2, RArmForward = 2,
    };

    public static AvatarPose Lerp(in AvatarPose a, in AvatarPose b, double t)
    {
        double L(double x, double y) => x + (y - x) * t;
        return new AvatarPose
        {
            Lift = L(a.Lift, b.Lift),
            Shift = L(a.Shift, b.Shift),
            Squash = L(a.Squash, b.Squash),
            Turn = L(a.Turn, b.Turn),
            HipRoll = L(a.HipRoll, b.HipRoll),
            HipYaw = L(a.HipYaw, b.HipYaw),
            SpinePitch = L(a.SpinePitch, b.SpinePitch),
            SpineRoll = L(a.SpineRoll, b.SpineRoll),
            SpineYaw = L(a.SpineYaw, b.SpineYaw),
            HeadPitch = L(a.HeadPitch, b.HeadPitch),
            HeadYaw = L(a.HeadYaw, b.HeadYaw),
            HeadRoll = L(a.HeadRoll, b.HeadRoll),
            LArmForward = L(a.LArmForward, b.LArmForward),
            LArmOut = L(a.LArmOut, b.LArmOut),
            LArmTwist = L(a.LArmTwist, b.LArmTwist),
            LElbow = L(a.LElbow, b.LElbow),
            RArmForward = L(a.RArmForward, b.RArmForward),
            RArmOut = L(a.RArmOut, b.RArmOut),
            RArmTwist = L(a.RArmTwist, b.RArmTwist),
            RElbow = L(a.RElbow, b.RElbow),
            LLegForward = L(a.LLegForward, b.LLegForward),
            LKnee = L(a.LKnee, b.LKnee),
            LLegOut = L(a.LLegOut, b.LLegOut),
            RLegForward = L(a.RLegForward, b.RLegForward),
            RKnee = L(a.RKnee, b.RKnee),
            RLegOut = L(a.RLegOut, b.RLegOut),
        };
    }

    /// <summary>The same pose seen in a mirror: left and right swap, sideways motion flips.</summary>
    public AvatarPose Mirrored() => new()
    {
        Lift = Lift,
        Shift = -Shift,
        Squash = Squash,
        Turn = -Turn,
        HipRoll = -HipRoll,
        HipYaw = -HipYaw,
        SpinePitch = SpinePitch,
        SpineRoll = -SpineRoll,
        SpineYaw = -SpineYaw,
        HeadPitch = HeadPitch,
        HeadYaw = -HeadYaw,
        HeadRoll = -HeadRoll,
        LArmForward = RArmForward, LArmOut = RArmOut, LArmTwist = -RArmTwist, LElbow = RElbow,
        RArmForward = LArmForward, RArmOut = LArmOut, RArmTwist = -LArmTwist, RElbow = LElbow,
        LLegForward = RLegForward, LKnee = RKnee, LLegOut = RLegOut,
        RLegForward = LLegForward, RKnee = LKnee, RLegOut = LLegOut,
    };
}
