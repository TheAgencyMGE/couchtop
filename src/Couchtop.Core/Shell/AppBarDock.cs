using System.Runtime.InteropServices;
using Couchtop.Core.Diagnostics;
using Couchtop.Core.Native;

namespace Couchtop.Core.Shell;

public enum DockEdge
{
    Bottom,
    Top,
}

/// <summary>
/// Registers a window as an application desktop toolbar, so Windows keeps maximized apps clear of it
/// exactly like it does for the Explorer taskbar. Reserving space is best-effort: if Windows refuses,
/// the bar still shows, it just overlaps maximized windows.
/// </summary>
public sealed class AppBarDock : IDisposable
{
    private readonly IntPtr _window;
    private readonly uint _callbackMessage;
    private bool _registered;
    private bool _reserving;

    public AppBarDock(IntPtr window, uint callbackMessage)
    {
        _window = window;
        _callbackMessage = callbackMessage;
    }

    public bool IsRegistered => _registered;

    public bool Register()
    {
        if (_registered || _window == IntPtr.Zero) return _registered;
        var data = Create();
        data.uCallbackMessage = _callbackMessage;
        _registered = NativeMethods.SHAppBarMessage(NativeMethods.ABM_NEW, ref data) != IntPtr.Zero;
        if (!_registered) Log.Warn("Windows would not register the Couchtop Bar as a desktop toolbar; it will overlap maximized windows.");
        return _registered;
    }

    /// <summary>
    /// Claims a strip of the screen. Windows may move the strip (another toolbar is already there), so the
    /// rectangle it agreed to is returned and the window should be placed there.
    /// </summary>
    public NativeMethods.RECT Reserve(DockEdge edge, int left, int top, int right, int bottom)
    {
        var data = Create();
        data.uEdge = edge == DockEdge.Bottom ? NativeMethods.ABE_BOTTOM : NativeMethods.ABE_TOP;
        data.rc = new NativeMethods.RECT { Left = left, Top = top, Right = right, Bottom = bottom };
        if (!_registered) return data.rc;

        NativeMethods.SHAppBarMessage(NativeMethods.ABM_QUERYPOS, ref data);
        var height = bottom - top;
        if (edge == DockEdge.Bottom) data.rc.Top = data.rc.Bottom - height;
        else data.rc.Bottom = data.rc.Top + height;
        NativeMethods.SHAppBarMessage(NativeMethods.ABM_SETPOS, ref data);
        _reserving = true;
        return data.rc;
    }

    /// <summary>Gives the screen space back but keeps the toolbar registered (used when the bar auto-hides).</summary>
    public void ReleaseSpace()
    {
        if (!_registered || !_reserving) return;
        var data = Create();
        data.uEdge = NativeMethods.ABE_BOTTOM;
        data.rc = new NativeMethods.RECT();
        NativeMethods.SHAppBarMessage(NativeMethods.ABM_SETPOS, ref data);
        _reserving = false;
    }

    public void Dispose()
    {
        if (!_registered) return;
        try
        {
            var data = Create();
            NativeMethods.SHAppBarMessage(NativeMethods.ABM_REMOVE, ref data);
        }
        catch (Exception ex)
        {
            Log.Warn("Could not unregister the Couchtop Bar", ex);
        }
        _registered = false;
        _reserving = false;
    }

    private NativeMethods.APPBARDATA Create() => new()
    {
        cbSize = Marshal.SizeOf<NativeMethods.APPBARDATA>(),
        hWnd = _window,
    };
}
