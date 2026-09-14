using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using Couchtop.Core.Diagnostics;
using Couchtop.Core.Native;

namespace Couchtop.Core.Platform;

public readonly record struct HotkeyDefinition(uint Modifiers, uint VirtualKey, string Display);

public static class HotkeyParser
{
    public const string EmergencyHotkey = "Ctrl+Alt+Shift+F12";

    private static readonly Dictionary<string, uint> NamedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Home"] = 0x24, ["End"] = 0x23, ["Insert"] = 0x2D, ["Delete"] = 0x2E, ["PageUp"] = 0x21, ["PageDown"] = 0x22,
        ["Space"] = 0x20, ["Escape"] = 0x1B, ["Esc"] = 0x1B, ["Tab"] = 0x09, ["Enter"] = 0x0D, ["Pause"] = 0x13,
        ["Up"] = 0x26, ["Down"] = 0x28, ["Left"] = 0x25, ["Right"] = 0x27, ["Backspace"] = 0x08,
    };

    public static bool TryParse(string? text, out HotkeyDefinition hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        uint mods = 0;
        uint? vk = null;
        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= NativeMethods.MOD_CONTROL; continue;
                case "alt": mods |= NativeMethods.MOD_ALT; continue;
                case "shift": mods |= NativeMethods.MOD_SHIFT; continue;
                case "win" or "windows": mods |= NativeMethods.MOD_WIN; continue;
            }
            if (vk is not null) return false;
            if (NamedKeys.TryGetValue(raw, out var named)) vk = named;
            else if (raw.Length >= 2 && (raw[0] is 'F' or 'f') && int.TryParse(raw[1..], out var f) && f is >= 1 and <= 24) vk = (uint)(0x70 + f - 1);
            else if (raw.Length == 1 && char.IsLetterOrDigit(raw[0])) vk = char.ToUpperInvariant(raw[0]);
            else return false;
        }
        if (vk is null || mods == 0) return false;
        hotkey = new HotkeyDefinition(mods, vk.Value, text.Trim());
        return true;
    }
}

public sealed record StartupEntry(string Name, string FileName, string Arguments, string Source);

public static class CommandLineSplitter
{
    public static (string FileName, string Arguments) Split(string commandLine, Func<string, bool>? fileExists = null)
    {
        fileExists ??= File.Exists;
        var cmd = Environment.ExpandEnvironmentVariables(commandLine ?? "").Trim();
        if (cmd.Length == 0) return ("", "");
        if (cmd[0] == '"')
        {
            var end = cmd.IndexOf('"', 1);
            if (end < 0) return (cmd.Trim('"'), "");
            return (cmd[1..end], cmd[(end + 1)..].Trim());
        }

        // Unquoted paths with spaces: grow the candidate until an existing file is found.
        var index = 0;
        while ((index = cmd.IndexOf(' ', index + 1)) > 0)
        {
            var candidate = cmd[..index];
            if (fileExists(candidate) || fileExists(candidate + ".exe")) return (candidate, cmd[(index + 1)..].Trim());
        }
        if (fileExists(cmd)) return (cmd, "");
        var space = cmd.IndexOf(' ');
        return space < 0 ? (cmd, "") : (cmd[..space], cmd[(space + 1)..].Trim());
    }
}

/// <summary>
/// Explorer normally launches Run-key and Startup-folder programs. In shell mode Explorer is absent, so the
/// Guardian launches the same enabled entries once per sign-in.
/// </summary>
public static class StartupApps
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedRoot = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved";

    /// <summary>StartupApproved data: first byte even (02/06) = enabled, odd (03/07) = disabled by the user.</summary>
    public static bool IsApproved(byte[]? data) => data is null || data.Length == 0 || (data[0] & 1) == 0;

    public static bool ShouldSkip(string command) =>
        command.Contains("Couchtop", StringComparison.OrdinalIgnoreCase) ||
        command.Trim().Trim('"').EndsWith("explorer.exe", StringComparison.OrdinalIgnoreCase);

    public static List<StartupEntry> Collect()
    {
        var entries = new List<StartupEntry>();
        void FromRun(RegistryKey hive, string keyPath, string approvedSub, string source)
        {
            try
            {
                using var key = hive.OpenSubKey(keyPath);
                if (key is null) return;
                using var approved = Registry.CurrentUser.OpenSubKey($@"{ApprovedRoot}\{approvedSub}");
                using var approvedMachine = Registry.LocalMachine.OpenSubKey($@"{ApprovedRoot}\{approvedSub}");
                foreach (var name in key.GetValueNames())
                {
                    if (key.GetValue(name) is not string command || string.IsNullOrWhiteSpace(command) || ShouldSkip(command)) continue;
                    var flag = approved?.GetValue(name) as byte[] ?? approvedMachine?.GetValue(name) as byte[];
                    if (!IsApproved(flag)) continue;
                    var (file, args) = CommandLineSplitter.Split(command);
                    entries.Add(new StartupEntry(name, file, args, source));
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"Could not read {source}", ex);
            }
        }

        FromRun(Registry.CurrentUser, RunKey, "Run", "HKCU Run");
        FromRun(Registry.LocalMachine, RunKey, "Run", "HKLM Run");
        FromRun(Registry.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", "Run32", "HKLM Run32");

        using var approvedFolder = Registry.CurrentUser.OpenSubKey($@"{ApprovedRoot}\StartupFolder");
        foreach (var folder in new[] { Environment.GetFolderPath(Environment.SpecialFolder.Startup), Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup) })
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) continue;
            foreach (var file in Directory.EnumerateFiles(folder))
            {
                var name = Path.GetFileName(file);
                if (name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase) || ShouldSkip(file)) continue;
                if (!IsApproved(approvedFolder?.GetValue(name) as byte[])) continue;
                entries.Add(new StartupEntry(name, file, "", "Startup folder"));
            }
        }
        return entries;
    }

    public static void LaunchAll(IEnumerable<StartupEntry> entries)
    {
        foreach (var entry in entries)
        {
            try
            {
                var psi = new ProcessStartInfo(entry.FileName, entry.Arguments) { UseShellExecute = true };
                var dir = Path.IsPathRooted(entry.FileName) ? Path.GetDirectoryName(entry.FileName) : null;
                if (dir is not null && Directory.Exists(dir)) psi.WorkingDirectory = dir;
                using var _ = Process.Start(psi);
                Log.Info($"Startup app launched: {entry.Name}");
            }
            catch (Exception ex)
            {
                Log.Warn($"Startup app '{entry.Name}' failed", ex);
            }
        }
    }
}

public static class PowerActions
{
    public static void Lock() => NativeMethods.LockWorkStation();
    public static void Sleep() => NativeMethods.SetSuspendState(false, false, false);
    public static void Hibernate() => NativeMethods.SetSuspendState(true, false, false);
    public static void SignOut() => NativeMethods.ExitWindowsEx(NativeMethods.EWX_LOGOFF, 0);
    public static void Restart() => RunShutdown("/r /t 0");
    public static void ShutDown() => RunShutdown("/s /t 0");

    private static void RunShutdown(string args)
    {
        var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "shutdown.exe");
        using var _ = Process.Start(new ProcessStartInfo(exe, args) { CreateNoWindow = true, UseShellExecute = false });
    }
}

public sealed record TopLevelWindow(IntPtr Handle, string Title, int ProcessId, string? ProcessPath);

/// <summary>Lists user-switchable windows (the task switcher for shell mode, where there is no taskbar).</summary>
public static class WindowEnumerator
{
    private const int DWMWA_CLOAKED = 14;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private static readonly HashSet<string> IgnoredClasses = new(StringComparer.Ordinal)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow", "ApplicationManager_ImmersiveShellWindow",
    };

    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);

    public static List<TopLevelWindow> GetSwitchableWindows(int excludeProcessId)
    {
        var list = new List<TopLevelWindow>();
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hwnd)) return true;
            var title = NativeMethods.GetWindowTitle(hwnd);
            if (string.IsNullOrWhiteSpace(title)) return true;
            if (IgnoredClasses.Contains(NativeMethods.GetWindowClass(hwnd))) return true;
            var ex = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
            var appWindow = (ex & NativeMethods.WS_EX_APPWINDOW) != 0;
            if (!appWindow && ((ex & NativeMethods.WS_EX_TOOLWINDOW) != 0 || NativeMethods.GetWindow(hwnd, NativeMethods.GW_OWNER) != IntPtr.Zero)) return true;
            if (NativeMethods.DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0 && cloaked != 0) return true;
            NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == excludeProcessId) return true;
            list.Add(new TopLevelWindow(hwnd, title, (int)pid, GetProcessPath((int)pid)));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    public static string? GetProcessPath(int pid)
    {
        var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            var size = sb.Capacity;
            return QueryFullProcessImageName(handle, 0, sb, ref size) ? sb.ToString() : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    public static void Activate(IntPtr hwnd)
    {
        if (NativeMethods.IsIconic(hwnd)) NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
        NativeMethods.SetForegroundWindow(hwnd);
    }

    public static void Close(IntPtr hwnd) => NativeMethods.PostMessage(hwnd, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);

    public static RawImage? GetIcon(IntPtr hwnd, int size = 48)
    {
        const int ICON_BIG = 1;
        const int GCLP_HICON = -14;
        NativeMethods.SendMessageTimeout(hwnd, NativeMethods.WM_GETICON, (IntPtr)ICON_BIG, IntPtr.Zero, 0x0002, 100, out var icon);
        if (icon == IntPtr.Zero) icon = NativeMethods.GetClassLongPtr(hwnd, GCLP_HICON);
        return icon == IntPtr.Zero ? null : ShellImageLoader.FromHIcon(icon, size);
    }
}

/// <summary>A hidden top-level Win32 window with its own message loop (used by the Guardian for hotkeys and session events).</summary>
public sealed class NativeMessageWindow : IDisposable
{
    private readonly NativeMethods.WndProc _proc;
    private readonly string _className;

    public NativeMessageWindow(string className)
    {
        _className = className;
        _proc = WindowProc;
        var wc = new NativeMethods.WNDCLASSEX
        {
            cbSize = Marshal.SizeOf<NativeMethods.WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_proc),
            hInstance = NativeMethods.GetModuleHandle(null),
            lpszClassName = className,
        };
        NativeMethods.RegisterClassEx(ref wc);
        Handle = NativeMethods.CreateWindowEx(0, className, className, 0x80000000 /* WS_POPUP */, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, wc.hInstance, IntPtr.Zero);
        if (Handle == IntPtr.Zero) throw new InvalidOperationException("Could not create message window: " + Marshal.GetLastWin32Error());
    }

    public IntPtr Handle { get; }

    /// <summary>Return a value to handle the message; null falls through to DefWindowProc.</summary>
    public Func<uint, IntPtr, IntPtr, IntPtr?>? MessageHandler { get; set; }

    public void RunMessageLoop()
    {
        while (NativeMethods.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            NativeMethods.TranslateMessage(ref msg);
            NativeMethods.DispatchMessage(ref msg);
        }
    }

    public void Close() => NativeMethods.PostMessage(Handle, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);

    private IntPtr WindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            var handled = MessageHandler?.Invoke(msg, wParam, lParam);
            if (handled is not null) return handled.Value;
        }
        catch (Exception ex)
        {
            Log.Error("Message handler failed", ex);
        }
        if (msg == NativeMethods.WM_CLOSE)
        {
            NativeMethods.DestroyWindow(hwnd);
            return IntPtr.Zero;
        }
        if (msg == NativeMethods.WM_DESTROY)
        {
            NativeMethods.PostQuitMessage(0);
            return IntPtr.Zero;
        }
        return NativeMethods.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (NativeMethods.IsWindow(Handle)) NativeMethods.DestroyWindow(Handle);
        GC.KeepAlive(_proc);
        _ = _className;
    }
}
