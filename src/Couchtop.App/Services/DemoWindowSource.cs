using Couchtop.Core.Shell;

namespace Couchtop.App.Services;

/// <summary>
/// A stand-in window list for the visual regression renders, so screenshots of the Couchtop Bar and the task
/// switcher show neutral demo apps instead of whatever the person running them happens to have open.
/// </summary>
public sealed class DemoWindowSource : IWindowSource
{
    private static readonly ManagedWindow[] Demo =
    {
        new(new IntPtr(101), "Notes - shopping list.txt", 4101, @"C:\Windows\System32\notepad.exe", false, false),
        new(new IntPtr(102), "Holiday photos", 4102, @"C:\Program Files\Demo\Photo Viewer.exe", false, true),
        new(new IntPtr(103), "Couchtop - project board", 4103, @"C:\Program Files\Demo\Web Browser.exe", false, false),
        new(new IntPtr(104), "Media Player", 4104, @"C:\Program Files\Demo\Media Player.exe", true, false),
    };

    public IntPtr Foreground => Demo[0].Handle;

    public IReadOnlyList<ManagedWindow> List() => Demo;
}
