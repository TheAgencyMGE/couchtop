namespace Couchtop.Core.Pals;

/// <summary>A rectangle in screen pixels.</summary>
public readonly record struct ScreenRect(double Left, double Top, double Right, double Bottom)
{
    public double Width => Right - Left;
    public double Height => Bottom - Top;
    public bool Contains(double x, double y) => x >= Left && x < Right && y >= Top && y < Bottom;
}

/// <summary>
/// Something a Pal can stand on: the top edge of an app window, or the floor of a screen (just above the
/// taskbar). <paramref name="Owner"/> is the window's handle, or zero for a floor.
/// </summary>
public sealed record Surface(double Y, double Left, double Right, IntPtr Owner)
{
    public bool IsFloor => Owner == IntPtr.Zero;
    public double Width => Right - Left;
    public bool Spans(double x) => x >= Left && x <= Right;
}

/// <summary>
/// Works out where a desktop Pal can walk: the top edges of app windows (only the stretches no other window
/// covers) and the bottom of each screen's work area. Pure geometry, so it is easy to test.
/// </summary>
public static class DesktopSurfaces
{
    /// <param name="windowsTopFirst">Visible, non-minimized app windows in z-order, frontmost first.</param>
    /// <param name="workAreas">Each screen's work area (the screen minus the taskbar).</param>
    /// <param name="headroom">How much space a Pal needs above a surface, so it never stands with its head off screen.</param>
    /// <param name="minWidth">Shorter stretches aren't worth standing on.</param>
    public static List<Surface> Build(IReadOnlyList<(IntPtr Handle, ScreenRect Bounds)> windowsTopFirst, IReadOnlyList<ScreenRect> workAreas,
        double headroom = 160, double minWidth = 90)
    {
        var surfaces = new List<Surface>();
        foreach (var area in workAreas) surfaces.Add(new Surface(area.Bottom, area.Left, area.Right, IntPtr.Zero));

        for (var i = 0; i < windowsTopFirst.Count; i++)
        {
            var (handle, bounds) = windowsTopFirst[i];
            if (bounds.Width < minWidth || bounds.Height < 40) continue;
            var y = bounds.Top;
            var area = workAreas.FirstOrDefault(a => a.Contains(Math.Clamp((bounds.Left + bounds.Right) / 2, a.Left, a.Right - 1), y));
            if (area == default) continue;
            // Too close to the top of the screen (maximized windows, for example): no room for a Pal.
            if (y - headroom < area.Top) continue;

            // Start with the whole edge on screen, then cut away every part a window in front covers.
            var spans = new List<(double L, double R)> { (Math.Max(bounds.Left, area.Left), Math.Min(bounds.Right, area.Right)) };
            for (var j = 0; j < i && spans.Count > 0; j++)
            {
                var cover = windowsTopFirst[j].Bounds;
                // A window in front hides the edge if it overlaps the edge itself or the space right above it
                // where the Pal would stand.
                if (cover.Bottom <= y - headroom * 0.5 || cover.Top > y) continue;
                spans = Subtract(spans, cover.Left, cover.Right);
            }
            foreach (var (l, r) in spans)
                if (r - l >= minWidth) surfaces.Add(new Surface(y, l, r, handle));
        }
        return surfaces;
    }

    private static List<(double L, double R)> Subtract(List<(double L, double R)> spans, double left, double right)
    {
        var result = new List<(double, double)>();
        foreach (var (l, r) in spans)
        {
            if (right <= l || left >= r)
            {
                result.Add((l, r));
                continue;
            }
            if (left > l) result.Add((l, left));
            if (right < r) result.Add((right, r));
        }
        return result;
    }

    /// <summary>The first surface at or below (x, y): where a falling Pal lands.</summary>
    public static Surface? Below(IReadOnlyList<Surface> surfaces, double x, double y, double tolerance = 6) =>
        surfaces.Where(s => s.Spans(x) && s.Y >= y - tolerance).OrderBy(s => s.Y).FirstOrDefault();

    /// <summary>
    /// A surface worth hopping to from <paramref name="from"/>: another window edge within reach (up to
    /// <paramref name="maxRise"/> above or anywhere below, within <paramref name="maxReach"/> sideways).
    /// </summary>
    public static IEnumerable<Surface> Reachable(IReadOnlyList<Surface> surfaces, Surface from, double x, double maxReach = 520, double maxRise = 380) =>
        surfaces.Where(s => !ReferenceEquals(s, from) && s.Owner != from.Owner
                            && s.Y >= from.Y - maxRise
                            && Math.Max(0, Math.Max(s.Left - x, x - s.Right)) <= maxReach);
}
