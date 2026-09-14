using Couchtop.Core.Platform;

namespace Couchtop.Tests;

public class MonitorTests
{
    private static readonly MonitorDescriptor Primary = new(@"\\.\DISPLAY1", 0, 0, 2560, 1440, true, 1.5);
    private static readonly MonitorDescriptor Left = new(@"\\.\DISPLAY2", -1920, 180, 1920, 1080, false, 1.0);
    private static readonly MonitorDescriptor Portrait = new(@"\\.\DISPLAY3", 2560, -400, 1080, 1920, false, 1.25);

    [Fact]
    public void Saved_monitor_is_used_when_connected()
    {
        Assert.Equal(Left, Monitors.Choose(new[] { Primary, Left, Portrait }, @"\\.\display2"));
    }

    [Fact]
    public void Falls_back_to_primary_when_saved_monitor_is_unplugged()
    {
        Assert.Equal(Primary, Monitors.Choose(new[] { Left, Primary }, @"\\.\DISPLAY9"));
        Assert.Equal(Primary, Monitors.Choose(new[] { Left, Primary }, null));
    }

    [Fact]
    public void Falls_back_to_first_monitor_without_primary_and_handles_none()
    {
        Assert.Equal(Left, Monitors.Choose(new[] { Left, Portrait }, null));
        Assert.Null(Monitors.Choose(Array.Empty<MonitorDescriptor>(), @"\\.\DISPLAY1"));
    }

    [Fact]
    public void Signature_changes_for_resolution_dpi_and_layout_but_not_order()
    {
        var baseline = Monitors.Signature(new[] { Primary, Left });
        Assert.Equal(baseline, Monitors.Signature(new[] { Left, Primary }));
        Assert.NotEqual(baseline, Monitors.Signature(new[] { Primary with { Width = 1920, Height = 1080 }, Left }));
        Assert.NotEqual(baseline, Monitors.Signature(new[] { Primary with { Scale = 1.0 }, Left }));
        Assert.NotEqual(baseline, Monitors.Signature(new[] { Primary }));
    }

    [Fact]
    public void Negative_coordinates_are_contained_correctly()
    {
        Assert.True(Left.Contains(-10, 500));
        Assert.False(Left.Contains(0, 500));
        Assert.True(Portrait.Contains(3000, -100));
    }

    [Fact]
    public void Real_monitor_enumeration_returns_a_primary_display()
    {
        var monitors = Monitors.Enumerate();
        if (monitors.Count == 0) return; // headless CI agents may expose no display
        Assert.Contains(monitors, m => m.IsPrimary && m.Width > 0 && m.Height > 0 && m.Scale >= 1);
    }
}
