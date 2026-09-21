using System.Runtime.InteropServices;
using Couchtop.Core.Diagnostics;
using Couchtop.Core.Native;
using Couchtop.Core.Settings;

namespace Couchtop.Core.Shell;

/// <summary>
/// Puts the Explorer taskbar into auto-hide while the Couchtop Bar is on screen, so there are not two taskbars
/// at the bottom of the display, and puts it back exactly as it was afterwards. Only applies in launcher mode:
/// as the Windows shell there is no Explorer taskbar to hide.
/// </summary>
public static class WindowsTaskbar
{
    private static SettingsService? _settings;

    /// <summary>
    /// The state to put back, kept in settings rather than memory so that a crash, a kill or a sign-out
    /// cannot leave someone with a hidden taskbar and no way to know why.
    /// </summary>
    private static int? StateBefore
    {
        get => _settings?.Current.TaskbarStateBeforeHiding;
        set
        {
            if (_settings is null || _settings.Current.TaskbarStateBeforeHiding == value) return;
            _settings.Current.TaskbarStateBeforeHiding = value;
            _settings.Save();
        }
    }

    /// <summary>True while Couchtop is the one holding the taskbar hidden.</summary>
    public static bool Hidden => StateBefore is not null;

    /// <summary>
    /// Connects the helper to settings and puts the taskbar back if a previous run was killed while holding
    /// it hidden. Called once at startup, before anything decides to hide it again.
    /// </summary>
    public static void Attach(SettingsService settings)
    {
        _settings = settings;
        if (settings.Current.TaskbarStateBeforeHiding is not null) Restore();
    }

    /// <summary>The current auto-hide / always-on-top flags Explorer has set.</summary>
    public static int State()
    {
        var data = new NativeMethods.APPBARDATA { cbSize = Marshal.SizeOf<NativeMethods.APPBARDATA>() };
        return (int)NativeMethods.SHAppBarMessage(NativeMethods.ABM_GETSTATE, ref data);
    }

    /// <summary>Hides the taskbar, remembering how the user had it so nothing is taken away permanently.</summary>
    public static void Hide()
    {
        if (StateBefore is not null) return;
        var before = State();
        StateBefore = before;
        if ((before & NativeMethods.ABS_AUTOHIDE) != 0)
        {
            // Already auto-hiding: leave it alone, and remember that we changed nothing.
            Log.Info("Windows taskbar already auto-hides; leaving it as it is.");
            return;
        }
        SetState(before | NativeMethods.ABS_AUTOHIDE);
        Log.Info("Windows taskbar set to auto-hide while the Couchtop Bar is shown.");
    }

    /// <summary>Puts the taskbar back the way the user had it. Safe to call when nothing was changed.</summary>
    public static void Restore()
    {
        if (StateBefore is not { } before) return;
        StateBefore = null;
        if ((before & NativeMethods.ABS_AUTOHIDE) != 0) return;
        SetState(before);
        Log.Info("Windows taskbar restored.");
    }

    private static void SetState(int state)
    {
        try
        {
            var data = new NativeMethods.APPBARDATA
            {
                cbSize = Marshal.SizeOf<NativeMethods.APPBARDATA>(),
                lParam = state,
            };
            NativeMethods.SHAppBarMessage(NativeMethods.ABM_SETSTATE, ref data);
        }
        catch (Exception ex)
        {
            Log.Warn("Could not change the Windows taskbar auto-hide state", ex);
        }
    }
}
