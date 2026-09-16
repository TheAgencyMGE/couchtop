using Couchtop.Core.Native;

namespace Couchtop.Core.Platform;

/// <summary>
/// A display in physical pixels. The work area is the part left for app windows (the whole screen in shell mode,
/// or the screen minus Explorer's taskbar when Explorer is running).
/// </summary>
public sealed record MonitorDescriptor(string DeviceName, int X, int Y, int Width, int Height, bool IsPrimary, double Scale,
    int WorkX = 0, int WorkY = 0, int WorkWidth = 0, int WorkHeight = 0)
{
    public bool Contains(int x, int y) => x >= X && y >= Y && x < X + Width && y < Y + Height;

    public int WorkLeft => WorkWidth > 0 ? WorkX : X;
    public int WorkTop => WorkHeight > 0 ? WorkY : Y;
    public int UsableWidth => WorkWidth > 0 ? WorkWidth : Width;
    public int UsableHeight => WorkHeight > 0 ? WorkHeight : Height;
}

public static class Monitors
{
    public static List<MonitorDescriptor> Enumerate()
    {
        var list = new List<MonitorDescriptor>();
        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr handle, IntPtr _, ref NativeMethods.RECT _, IntPtr _) =>
        {
            var descriptor = Describe(handle);
            if (descriptor is not null) list.Add(descriptor);
            return true;
        }, IntPtr.Zero);
        return list;
    }

    public static MonitorDescriptor? ForWindow(IntPtr hwnd)
    {
        const uint MONITOR_DEFAULTTONEAREST = 2;
        var handle = NativeMethods.MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        return handle == IntPtr.Zero ? null : Describe(handle);
    }

    private static MonitorDescriptor? Describe(IntPtr handle)
    {
        var info = new NativeMethods.MONITORINFOEX { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFOEX>() };
        if (!NativeMethods.GetMonitorInfo(handle, ref info)) return null;
        var scale = 1.0;
        if (NativeMethods.GetDpiForMonitor(handle, 0, out var dpiX, out _) == 0 && dpiX > 0) scale = dpiX / 96.0;
        var r = info.rcMonitor;
        var work = info.rcWork;
        return new MonitorDescriptor(info.szDevice, r.Left, r.Top, r.Width, r.Height, (info.dwFlags & 1) != 0, scale,
            work.Left, work.Top, work.Width, work.Height);
    }

    /// <summary>
    /// Picks the monitor for the main menu: the saved one if still connected, otherwise the primary,
    /// otherwise the first. Returns null only when no monitors are reported.
    /// </summary>
    public static MonitorDescriptor? Choose(IReadOnlyList<MonitorDescriptor> monitors, string? preferredDeviceName)
    {
        if (monitors.Count == 0) return null;
        if (!string.IsNullOrWhiteSpace(preferredDeviceName))
        {
            var saved = monitors.FirstOrDefault(m => string.Equals(m.DeviceName, preferredDeviceName, StringComparison.OrdinalIgnoreCase));
            if (saved is not null) return saved;
        }
        return monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
    }

    /// <summary>A stable signature so display-change events that don't change anything are ignored.</summary>
    public static string Signature(IEnumerable<MonitorDescriptor> monitors) =>
        string.Join(";", monitors.OrderBy(m => m.DeviceName).Select(m => $"{m.DeviceName}:{m.X},{m.Y},{m.Width}x{m.Height}@{m.Scale:0.##}{(m.IsPrimary ? "P" : "")}"));
}
