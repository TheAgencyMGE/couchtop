using Couchtop.Core.Diagnostics;
using Couchtop.Core.Native;
using Couchtop.Core.Platform;

namespace Couchtop.Core.Shell;

/// <summary>One top-level window of a running app, as the taskbar and switcher see it.</summary>
public sealed record ManagedWindow(IntPtr Handle, string Title, int ProcessId, string? ProcessPath, bool Minimized, bool Maximized)
{
    /// <summary>Windows of the same program share this key, so the taskbar can group them.</summary>
    public string AppKey => string.IsNullOrEmpty(ProcessPath) ? "pid:" + ProcessId : ProcessPath!.ToLowerInvariant();

    public string AppName => string.IsNullOrEmpty(ProcessPath) ? Title : Path.GetFileNameWithoutExtension(ProcessPath!);
}

/// <summary>Windows of one program, newest activity first.</summary>
public sealed record WindowGroup(string AppKey, string AppName, IReadOnlyList<ManagedWindow> Windows)
{
    public ManagedWindow Primary => Windows[0];
    public bool AnyVisible => Windows.Any(w => !w.Minimized);
}

/// <summary>Where the window list comes from. Faked in tests.</summary>
public interface IWindowSource
{
    IReadOnlyList<ManagedWindow> List();
    IntPtr Foreground { get; }
}

/// <summary>Keeps a most-recently-used window order that survives refreshes (the order Alt+Tab walks).</summary>
public static class WindowOrdering
{
    /// <summary>
    /// Returns the previous order with closed windows dropped, new windows appended, and <paramref name="foreground"/>
    /// moved to the front. New windows go to the back so a background window opening never steals the switcher's first slot.
    /// </summary>
    public static List<IntPtr> Merge(IReadOnlyList<IntPtr> previous, IReadOnlyList<IntPtr> current, IntPtr foreground)
    {
        var alive = new HashSet<IntPtr>(current);
        var merged = previous.Where(alive.Contains).ToList();
        var known = new HashSet<IntPtr>(merged);
        merged.AddRange(current.Where(h => !known.Contains(h)));
        if (foreground != IntPtr.Zero && merged.Remove(foreground)) merged.Insert(0, foreground);
        return merged;
    }

    /// <summary>Orders windows by an MRU list; anything missing from it keeps its original relative position at the end.</summary>
    public static List<ManagedWindow> Apply(IReadOnlyList<ManagedWindow> windows, IReadOnlyList<IntPtr> order)
    {
        var rank = new Dictionary<IntPtr, int>();
        for (var i = 0; i < order.Count; i++) rank[order[i]] = i;
        return windows.OrderBy(w => rank.TryGetValue(w.Handle, out var r) ? r : int.MaxValue).ToList();
    }

    /// <summary>Groups windows per program, keeping both the groups and each group's windows in MRU order.</summary>
    public static List<WindowGroup> Group(IReadOnlyList<ManagedWindow> ordered)
    {
        var groups = new List<WindowGroup>();
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var window in ordered)
        {
            if (index.TryGetValue(window.AppKey, out var at))
            {
                var list = (List<ManagedWindow>)groups[at].Windows;
                list.Add(window);
                continue;
            }
            index[window.AppKey] = groups.Count;
            groups.Add(new WindowGroup(window.AppKey, window.AppName, new List<ManagedWindow> { window }));
        }
        return groups;
    }

    /// <summary>The window <paramref name="steps"/> along from the front of the MRU list, wrapping around.</summary>
    public static ManagedWindow? Step(IReadOnlyList<ManagedWindow> ordered, int steps)
    {
        if (ordered.Count == 0) return null;
        var index = ((steps % ordered.Count) + ordered.Count) % ordered.Count;
        return ordered[index];
    }
}

/// <summary>The real window list, from the Win32 window enumerator.</summary>
public sealed class LiveWindowSource : IWindowSource
{
    private readonly int _excludeProcessId;

    public LiveWindowSource(int excludeProcessId)
    {
        _excludeProcessId = excludeProcessId;
    }

    public IntPtr Foreground => NativeMethods.GetForegroundWindow();

    public IReadOnlyList<ManagedWindow> List() =>
        WindowEnumerator.GetSwitchableWindows(_excludeProcessId)
            .Select(w => new ManagedWindow(w.Handle, w.Title, w.ProcessId, w.ProcessPath, NativeMethods.IsIconic(w.Handle), IsMaximized(w.Handle)))
            .ToList();

    private static bool IsMaximized(IntPtr hwnd)
    {
        const int WS_MAXIMIZE = 0x01000000;
        return (NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_STYLE).ToInt64() & WS_MAXIMIZE) != 0;
    }
}

/// <summary>
/// Tracks the running apps' windows for the Couchtop Bar and the task switcher: a most-recently-used order,
/// per-app grouping, and the window actions a desktop shell needs.
/// </summary>
public sealed class WindowTracker
{
    private readonly IWindowSource _source;
    private List<IntPtr> _order = new();

    public WindowTracker(IWindowSource source)
    {
        _source = source;
        Windows = Array.Empty<ManagedWindow>();
        Groups = Array.Empty<WindowGroup>();
    }

    /// <summary>All switchable windows, most recently used first.</summary>
    public IReadOnlyList<ManagedWindow> Windows { get; private set; }

    public IReadOnlyList<WindowGroup> Groups { get; private set; }

    public IntPtr Foreground { get; private set; }

    /// <summary>Re-reads the window list. Returns true when anything the UI shows has changed.</summary>
    public bool Refresh()
    {
        List<ManagedWindow> windows;
        IntPtr foreground;
        try
        {
            windows = _source.List().ToList();
            foreground = _source.Foreground;
        }
        catch (Exception ex)
        {
            Log.Warn("Could not list windows", ex);
            return false;
        }

        _order = WindowOrdering.Merge(_order, windows.Select(w => w.Handle).ToList(), foreground);
        var ordered = WindowOrdering.Apply(windows, _order);
        var changed = foreground != Foreground || !Same(ordered, Windows);
        Windows = ordered;
        Groups = WindowOrdering.Group(ordered);
        Foreground = foreground;
        return changed;
    }

    private static bool Same(IReadOnlyList<ManagedWindow> a, IReadOnlyList<ManagedWindow> b)
    {
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++)
        {
            if (a[i].Handle != b[i].Handle || a[i].Minimized != b[i].Minimized || a[i].Maximized != b[i].Maximized || a[i].Title != b[i].Title) return false;
        }
        return true;
    }

    /// <summary>The window Alt+Tab would land on after <paramref name="steps"/> presses (1 = previous app).</summary>
    public ManagedWindow? Step(int steps) => WindowOrdering.Step(Windows, steps);
}

/// <summary>Window actions: activate, minimize, maximize, close, and moving windows between monitors.</summary>
public static class WindowActions
{
    private const int SW_MAXIMIZE = 3;

    public static void Activate(IntPtr hwnd)
    {
        if (!NativeMethods.IsWindow(hwnd)) return;
        WindowEnumerator.Activate(hwnd);
    }

    /// <summary>Activates a window, or minimizes it when it is already in front (taskbar button behavior).</summary>
    public static void Toggle(IntPtr hwnd)
    {
        if (!NativeMethods.IsWindow(hwnd)) return;
        if (NativeMethods.GetForegroundWindow() == hwnd && !NativeMethods.IsIconic(hwnd)) Minimize(hwnd);
        else Activate(hwnd);
    }

    public static void Minimize(IntPtr hwnd) => NativeMethods.ShowWindow(hwnd, NativeMethods.SW_MINIMIZE);

    public static void Maximize(IntPtr hwnd) => NativeMethods.ShowWindow(hwnd, SW_MAXIMIZE);

    public static void Restore(IntPtr hwnd) => NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);

    public static void Close(IntPtr hwnd) => WindowEnumerator.Close(hwnd);

    /// <summary>Closes every window of a program (the taskbar's "Close all").</summary>
    public static void CloseGroup(WindowGroup group)
    {
        foreach (var window in group.Windows) Close(window.Handle);
    }

    public static void MinimizeAll(IEnumerable<ManagedWindow> windows)
    {
        foreach (var window in windows.Where(w => !w.Minimized)) Minimize(window.Handle);
    }

    /// <summary>Fills a monitor's work area with the window (Couchtop's version of snapping to a screen).</summary>
    public static void MoveToMonitor(IntPtr hwnd, MonitorDescriptor monitor)
    {
        if (!NativeMethods.IsWindow(hwnd)) return;
        Restore(hwnd);
        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, monitor.WorkLeft, monitor.WorkTop, monitor.UsableWidth, monitor.UsableHeight,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_SHOWWINDOW);
    }

    /// <summary>Snaps a window to half (or a quarter) of its monitor's work area.</summary>
    public static void Snap(IntPtr hwnd, MonitorDescriptor monitor, WindowSnap snap)
    {
        if (!NativeMethods.IsWindow(hwnd)) return;
        Restore(hwnd);
        var halfWidth = monitor.UsableWidth / 2;
        var halfHeight = monitor.UsableHeight / 2;
        var (x, y, w, h) = snap switch
        {
            WindowSnap.Left => (monitor.WorkLeft, monitor.WorkTop, halfWidth, monitor.UsableHeight),
            WindowSnap.Right => (monitor.WorkLeft + halfWidth, monitor.WorkTop, halfWidth, monitor.UsableHeight),
            WindowSnap.TopLeft => (monitor.WorkLeft, monitor.WorkTop, halfWidth, halfHeight),
            WindowSnap.TopRight => (monitor.WorkLeft + halfWidth, monitor.WorkTop, halfWidth, halfHeight),
            WindowSnap.BottomLeft => (monitor.WorkLeft, monitor.WorkTop + halfHeight, halfWidth, halfHeight),
            _ => (monitor.WorkLeft + halfWidth, monitor.WorkTop + halfHeight, halfWidth, halfHeight),
        };
        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, x, y, w, h, NativeMethods.SWP_NOZORDER | NativeMethods.SWP_SHOWWINDOW);
    }
}

public static class FullScreenCheck
{
    /// <summary>
    /// True when a window covers a whole monitor, which is how games and video players run. The Couchtop Bar
    /// hides itself for these so it never sits on top of a game.
    /// </summary>
    public static bool Covers(int left, int top, int right, int bottom, MonitorDescriptor monitor)
    {
        const int slack = 2;
        return left <= monitor.X + slack && top <= monitor.Y + slack &&
               right >= monitor.X + monitor.Width - slack && bottom >= monitor.Y + monitor.Height - slack;
    }

    /// <summary>Checks the window that is currently in front (ignoring the desktop itself).</summary>
    public static bool ForegroundIsFullScreen(int ourProcessId)
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return false;
        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == ourProcessId) return false;
        var className = NativeMethods.GetWindowClass(hwnd);
        if (className is "Progman" or "WorkerW" or "Shell_TrayWnd") return false;
        if (!NativeMethods.GetWindowRect(hwnd, out var rect)) return false;
        var monitor = Monitors.ForWindow(hwnd);
        return monitor is not null && Covers(rect.Left, rect.Top, rect.Right, rect.Bottom, monitor);
    }
}

public enum WindowSnap
{
    Left,
    Right,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}
