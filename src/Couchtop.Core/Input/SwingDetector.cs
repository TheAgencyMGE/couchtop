namespace Couchtop.Core.Input;

/// <summary>
/// Turns Wii Remote accelerometer samples into swing gestures. A swing is reported at the peak of a sharp
/// movement (so timing-based sports feel responsive) with a strength from 0 to 1 and a left/right hint.
/// Gravity is tracked with a slow filter while the remote is calm, so any grip orientation works.
/// </summary>
public sealed class SwingDetector
{
    /// <summary>8-bit accelerometer value for 0 g on each axis.</summary>
    public const double Zero = 128;

    /// <summary>Approximate counts per g on each axis.</summary>
    public const double CountsPerG = 25.5;

    public const double Threshold = 1.35;
    private const double StrongSwing = 3.8;
    private const double Cooldown = 0.28;

    private bool _initialized;
    private double _gx, _gy, _gz;
    private bool _inSwing;
    private double _peak;
    private double _peakX;
    private double _previous;
    private double _swingStart;
    private double _lastSwing = double.NegativeInfinity;

    public (float Strength, int Side)? Feed(int rawX, int rawY, int rawZ, double time)
    {
        var ax = (rawX - Zero) / CountsPerG;
        var ay = (rawY - Zero) / CountsPerG;
        var az = (rawZ - Zero) / CountsPerG;
        if (!_initialized)
        {
            (_gx, _gy, _gz) = (ax, ay, az);
            _initialized = true;
            return null;
        }

        var dx = ax - _gx;
        var dy = ay - _gy;
        var dz = az - _gz;
        var magnitude = Math.Sqrt(dx * dx + dy * dy + dz * dz);

        if (!_inSwing)
        {
            if (magnitude < 0.3)
            {
                _gx += (ax - _gx) * 0.08;
                _gy += (ay - _gy) * 0.08;
                _gz += (az - _gz) * 0.08;
            }
            if (magnitude > Threshold && time - _lastSwing > Cooldown)
            {
                _inSwing = true;
                _peak = magnitude;
                _peakX = dx;
                _previous = magnitude;
                _swingStart = time;
            }
            return null;
        }

        if (magnitude > _peak)
        {
            _peak = magnitude;
            _peakX = dx;
        }
        var falling = magnitude < _previous * 0.9;
        _previous = magnitude;
        if (!falling && time - _swingStart < 0.12) return null;

        _inSwing = false;
        _lastSwing = time;
        var strength = (float)Math.Clamp((_peak - Threshold) / (StrongSwing - Threshold), 0.08, 1.0);
        var side = Math.Abs(_peakX) > 0.9 ? Math.Sign(_peakX) : 0;
        return (strength, side);
    }
}
