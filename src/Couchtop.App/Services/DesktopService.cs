using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Couchtop.Core.Diagnostics;
using Couchtop.Core.Native;
using Couchtop.Core.Platform;
using Couchtop.Core.Shell;

namespace Couchtop.App.Services;

/// <summary>
/// The desktop side of Couchtop: which app windows exist, which one is in front, and the actions the
/// Couchtop Bar and task switcher perform on them. Polling only runs while something is showing the list.
/// </summary>
public sealed class DesktopService : IDisposable
{
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<IntPtr, BitmapSource?> _icons = new();
    private int _listeners;

    /// <param name="source">Where windows come from; the visual regression renders pass a fake list of demo windows.</param>
    public DesktopService(IWindowSource? source = null)
    {
        Windows = new WindowTracker(source ?? new LiveWindowSource(Environment.ProcessId));
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(700) };
        _timer.Tick += (_, _) => Poll();
    }

    public WindowTracker Windows { get; }

    /// <summary>True while a game or video is filling a screen, so the Couchtop Bar can step aside.</summary>
    public bool ForegroundIsFullScreen { get; private set; }

    /// <summary>Raised on the UI thread when the window list or the front window changed.</summary>
    public event Action? Changed;

    public bool IsPolling => _timer.IsEnabled;

    /// <summary>Starts polling while a caller needs a live list; balanced by <see cref="RemoveListener"/>.</summary>
    public void AddListener()
    {
        _listeners++;
        if (_listeners == 1)
        {
            Poll();
            _timer.Start();
        }
    }

    public void RemoveListener()
    {
        _listeners = Math.Max(0, _listeners - 1);
        if (_listeners == 0) _timer.Stop();
    }

    /// <summary>Refreshes right now (after activating or closing something).</summary>
    public void Poll()
    {
        try
        {
            var fullScreen = FullScreenCheck.ForegroundIsFullScreen(Environment.ProcessId);
            var changed = Windows.Refresh();
            if (fullScreen != ForegroundIsFullScreen)
            {
                ForegroundIsFullScreen = fullScreen;
                changed = true;
            }
            if (changed)
            {
                PruneIcons();
                Changed?.Invoke();
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Window polling failed", ex);
        }
    }

    public BitmapSource? IconFor(ManagedWindow window)
    {
        if (_icons.TryGetValue(window.Handle, out var cached)) return cached;
        BitmapSource? image = null;
        try
        {
            image = Views.ViewKit.ToBitmap(WindowEnumerator.GetIcon(window.Handle, 32));
        }
        catch (Exception ex)
        {
            Log.Warn("Could not read a window icon", ex);
        }
        _icons[window.Handle] = image;
        return image;
    }

    private void PruneIcons()
    {
        if (_icons.Count < 64) return;
        foreach (var handle in _icons.Keys.Where(h => !NativeMethods.IsWindow(h)).ToList()) _icons.Remove(handle);
    }

    // ---------------------------------------------------------------- actions

    public void Activate(ManagedWindow window)
    {
        WindowActions.Activate(window.Handle);
        Poll();
    }

    public void Toggle(ManagedWindow window)
    {
        WindowActions.Toggle(window.Handle);
        Poll();
    }

    public void Close(ManagedWindow window)
    {
        WindowActions.Close(window.Handle);
        Dispatcher.CurrentDispatcher.BeginInvoke(Poll, DispatcherPriority.Background);
    }

    public void Minimize(ManagedWindow window)
    {
        WindowActions.Minimize(window.Handle);
        Poll();
    }

    public void Maximize(ManagedWindow window)
    {
        WindowActions.Maximize(window.Handle);
        Poll();
    }

    public void Restore(ManagedWindow window)
    {
        WindowActions.Restore(window.Handle);
        Poll();
    }

    public void MinimizeAll()
    {
        WindowActions.MinimizeAll(Windows.Windows);
        Poll();
    }

    public void Snap(ManagedWindow window, WindowSnap snap)
    {
        var monitor = Monitors.ForWindow(window.Handle) ?? Monitors.Choose(Monitors.Enumerate(), null);
        if (monitor is not null) WindowActions.Snap(window.Handle, monitor, snap);
        Poll();
    }

    /// <summary>Sends a window to the next display, so a maximized app can be moved off the Couchtop screen.</summary>
    public void SendToNextMonitor(ManagedWindow window)
    {
        var monitors = Monitors.Enumerate();
        if (monitors.Count < 2) return;
        var current = Monitors.ForWindow(window.Handle);
        var index = current is null ? 0 : monitors.FindIndex(m => m.DeviceName == current.DeviceName);
        var next = monitors[(Math.Max(0, index) + 1) % monitors.Count];
        WindowActions.MoveToMonitor(window.Handle, next);
        Poll();
    }

    public void Dispose()
    {
        _timer.Stop();
        _icons.Clear();
    }
}
