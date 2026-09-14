using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Couchtop.Core.Native;

/// <summary>Runs shell COM work on STA threads (many shell namespace extensions require STA).</summary>
public static class StaRunner
{
    public static T Run<T>(Func<T> func, TimeSpan? timeout = null)
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA) return func();
        T result = default!;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { result = func(); }
            catch (Exception ex) { error = ex; }
        })
        { IsBackground = true, Name = "Couchtop STA" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(timeout ?? TimeSpan.FromSeconds(60))) throw new TimeoutException("Shell operation timed out.");
        if (error is not null) ExceptionDispatchInfo.Throw(error);
        return result;
    }
}

/// <summary>A small pool of long-lived STA threads for repeated shell calls (icons, thumbnails).</summary>
public sealed class StaWorkQueue : IDisposable
{
    private readonly BlockingCollection<Action> _queue = new();
    private readonly List<Thread> _threads = new();

    public StaWorkQueue(int threads, string name)
    {
        for (var i = 0; i < threads; i++)
        {
            var t = new Thread(Worker) { IsBackground = true, Name = $"{name} {i}", Priority = ThreadPriority.BelowNormal };
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            _threads.Add(t);
        }
    }

    public Task<T> Enqueue<T>(Func<T> func)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            _queue.Add(() =>
            {
                try { tcs.SetResult(func()); }
                catch (Exception ex) { tcs.SetException(ex); }
            });
        }
        catch (InvalidOperationException)
        {
            tcs.SetCanceled();
        }
        return tcs.Task;
    }

    private void Worker()
    {
        try
        {
            foreach (var work in _queue.GetConsumingEnumerable()) work();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public void Dispose() => _queue.CompleteAdding();
}

public enum SIGDN : uint
{
    NORMALDISPLAY = 0,
    PARENTRELATIVEPARSING = 0x80018001,
    DESKTOPABSOLUTEPARSING = 0x80028000,
    FILESYSPATH = 0x80058000,
}

[ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IShellItem
{
    [PreserveSig] int BindToHandler(IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid bhid, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IntPtr ppv);
    [PreserveSig] int GetParent(out IShellItem ppsi);
    [PreserveSig] int GetDisplayName(SIGDN sigdnName, out IntPtr ppszName);
    [PreserveSig] int GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
    [PreserveSig] int Compare(IShellItem psi, uint hint, out int piOrder);
}

[ComImport, Guid("70629033-e363-4a28-a567-0db78006e6d7"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IEnumShellItems
{
    [PreserveSig] int Next(uint celt, [MarshalAs(UnmanagedType.Interface)] out IShellItem? rgelt, out uint pceltFetched);
    [PreserveSig] int Skip(uint celt);
    [PreserveSig] int Reset();
    [PreserveSig] int Clone(out IEnumShellItems ppenum);
}

[StructLayout(LayoutKind.Sequential)]
public struct NativeSize
{
    public int Width;
    public int Height;
}

[ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IShellItemImageFactory
{
    [PreserveSig] int GetImage(NativeSize size, int flags, out IntPtr phbm);
}

[ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellLinkW
{
    void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, IntPtr pfd, uint fFlags);
    void GetIDList(out IntPtr ppidl);
    void SetIDList(IntPtr pidl);
    void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
    void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
    void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
    void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
    void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
    void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
    void GetHotkey(out short pwHotkey);
    void SetHotkey(short wHotkey);
    void GetShowCmd(out int piShowCmd);
    void SetShowCmd(int iShowCmd);
    void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
    void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
    void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
    void Resolve(IntPtr hwnd, uint fFlags);
    void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
}

[ComImport, Guid("0000010b-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPersistFile
{
    void GetClassID(out Guid pClassID);
    [PreserveSig] int IsDirty();
    void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
    void Save([MarshalAs(UnmanagedType.LPWStr)] string? pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
    void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
    void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
}

[ComImport, Guid("00021401-0000-0000-C000-000000000046")]
internal class CShellLink
{
}

[ComImport, Guid("2e941141-7f97-4756-ba1d-9decde894a3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IApplicationActivationManager
{
    [PreserveSig] int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId, [MarshalAs(UnmanagedType.LPWStr)] string? arguments, int options, out uint processId);
    [PreserveSig] int ActivateForFile([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId, IntPtr itemArray, [MarshalAs(UnmanagedType.LPWStr)] string verb, out uint processId);
    [PreserveSig] int ActivateForProtocol([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId, IntPtr itemArray, out uint processId);
}

[ComImport, Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
internal class ApplicationActivationManager
{
}

public sealed record ShellLinkInfo(string TargetPath, string Arguments, string WorkingDirectory, string IconPath, int IconIndex, string Description);

public static class ShellLink
{
    public static ShellLinkInfo? Read(string lnkPath)
    {
        object? obj = null;
        try
        {
            obj = new CShellLink();
            ((IPersistFile)obj).Load(lnkPath, 0);
            var link = (IShellLinkW)obj;
            var target = new StringBuilder(1024);
            link.GetPath(target, target.Capacity, IntPtr.Zero, 0);
            var args = new StringBuilder(4096);
            link.GetArguments(args, args.Capacity);
            var dir = new StringBuilder(1024);
            link.GetWorkingDirectory(dir, dir.Capacity);
            var icon = new StringBuilder(1024);
            link.GetIconLocation(icon, icon.Capacity, out var iconIndex);
            var desc = new StringBuilder(1024);
            try { link.GetDescription(desc, desc.Capacity); } catch (COMException) { }
            return new ShellLinkInfo(
                Environment.ExpandEnvironmentVariables(target.ToString()),
                args.ToString(),
                Environment.ExpandEnvironmentVariables(dir.ToString()),
                Environment.ExpandEnvironmentVariables(icon.ToString()),
                iconIndex,
                desc.ToString());
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or IOException or ArgumentException)
        {
            return null;
        }
        finally
        {
            if (obj is not null) Marshal.FinalReleaseComObject(obj);
        }
    }

    public static void Create(string lnkPath, string targetPath, string? arguments = null, string? workingDirectory = null, string? description = null, string? iconPath = null, int iconIndex = 0)
    {
        var obj = new CShellLink();
        try
        {
            var link = (IShellLinkW)obj;
            link.SetPath(targetPath);
            if (!string.IsNullOrEmpty(arguments)) link.SetArguments(arguments);
            link.SetWorkingDirectory(workingDirectory ?? Path.GetDirectoryName(targetPath) ?? "");
            if (!string.IsNullOrEmpty(description)) link.SetDescription(description);
            if (!string.IsNullOrEmpty(iconPath)) link.SetIconLocation(iconPath, iconIndex);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(lnkPath))!);
            ((IPersistFile)obj).Save(lnkPath, true);
        }
        finally
        {
            Marshal.FinalReleaseComObject(obj);
        }
    }
}

public static class ShellItems
{
    public static readonly Guid IID_IShellItem = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");
    public static readonly Guid IID_IShellItemImageFactory = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");
    public static readonly Guid IID_IEnumShellItems = new("70629033-e363-4a28-a567-0db78006e6d7");
    public static readonly Guid BHID_EnumItems = new("94f60519-2850-4924-aa5a-d15e84868039");

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    public static extern int SHCreateItemFromParsingName(string pszPath, IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IntPtr ppv);

    public static string? GetName(IShellItem item, SIGDN kind)
    {
        if (item.GetDisplayName(kind, out var ptr) != 0 || ptr == IntPtr.Zero) return null;
        try { return Marshal.PtrToStringUni(ptr); }
        finally { Marshal.FreeCoTaskMem(ptr); }
    }

    /// <summary>Enumerates a shell folder, returning (display name, parsing name) pairs. Call on an STA thread.</summary>
    public static List<(string Name, string Parsing)> EnumerateFolder(string folderParsingName)
    {
        var results = new List<(string, string)>();
        var hr = SHCreateItemFromParsingName(folderParsingName, IntPtr.Zero, IID_IShellItem, out var folderPtr);
        if (hr != 0 || folderPtr == IntPtr.Zero) throw new COMException($"Could not open {folderParsingName}", hr);
        var folder = (IShellItem)Marshal.GetObjectForIUnknown(folderPtr);
        Marshal.Release(folderPtr);
        try
        {
            hr = folder.BindToHandler(IntPtr.Zero, BHID_EnumItems, IID_IEnumShellItems, out var enumPtr);
            if (hr != 0 || enumPtr == IntPtr.Zero) throw new COMException("Could not enumerate folder", hr);
            var enumerator = (IEnumShellItems)Marshal.GetObjectForIUnknown(enumPtr);
            Marshal.Release(enumPtr);
            try
            {
                while (enumerator.Next(1, out var child, out var fetched) == 0 && fetched == 1 && child is not null)
                {
                    try
                    {
                        var name = GetName(child, SIGDN.NORMALDISPLAY);
                        var parsing = GetName(child, SIGDN.PARENTRELATIVEPARSING);
                        if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(parsing)) results.Add((name, parsing));
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(child);
                    }
                }
            }
            finally
            {
                Marshal.ReleaseComObject(enumerator);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(folder);
        }
        return results;
    }
}

/// <summary>BGRA32 straight-alpha pixels, top-down rows.</summary>
public sealed record RawImage(int Width, int Height, byte[] Pixels);

public static class ShellImageLoader
{
    private const int SIIGBF_BIGGERSIZEOK = 0x1;
    private const int SIIGBF_ICONONLY = 0x4;

    /// <summary>Loads an icon or thumbnail through the Windows shell. Must be called on an STA thread.</summary>
    public static RawImage? Load(string parsingName, int size, bool iconOnly)
    {
        var hr = ShellItems.SHCreateItemFromParsingName(parsingName, IntPtr.Zero, ShellItems.IID_IShellItemImageFactory, out var ptr);
        if (hr != 0 || ptr == IntPtr.Zero) return null;
        var factory = (IShellItemImageFactory)Marshal.GetObjectForIUnknown(ptr);
        Marshal.Release(ptr);
        try
        {
            var flags = SIIGBF_BIGGERSIZEOK | (iconOnly ? SIIGBF_ICONONLY : 0);
            hr = factory.GetImage(new NativeSize { Width = size, Height = size }, flags, out var hbitmap);
            if (hr != 0 || hbitmap == IntPtr.Zero) return null;
            try
            {
                return FromHBitmap(hbitmap);
            }
            finally
            {
                NativeMethods.DeleteObject(hbitmap);
            }
        }
        catch (COMException)
        {
            return null;
        }
        finally
        {
            Marshal.ReleaseComObject(factory);
        }
    }

    public static RawImage? FromHBitmap(IntPtr hbitmap)
    {
        if (NativeMethods.GetObject(hbitmap, Marshal.SizeOf<NativeMethods.BITMAP>(), out var bm) == 0) return null;
        int w = bm.bmWidth, h = Math.Abs(bm.bmHeight);
        if (w <= 0 || h <= 0 || w > 4096 || h > 4096) return null;
        var bmi = new NativeMethods.BITMAPINFO32
        {
            bmiHeader = new NativeMethods.BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>(),
                biWidth = w,
                biHeight = -h,
                biPlanes = 1,
                biBitCount = 32,
            },
        };
        var pixels = new byte[w * h * 4];
        var hdc = NativeMethods.CreateCompatibleDC(IntPtr.Zero);
        try
        {
            if (NativeMethods.GetDIBits(hdc, hbitmap, 0, (uint)h, pixels, ref bmi, 0) == 0) return null;
        }
        finally
        {
            NativeMethods.DeleteDC(hdc);
        }
        FixAlpha(pixels);
        return new RawImage(w, h, pixels);
    }

    public static RawImage? FromHIcon(IntPtr hicon, int size)
    {
        var bmi = new NativeMethods.BITMAPINFO32
        {
            bmiHeader = new NativeMethods.BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>(),
                biWidth = size,
                biHeight = -size,
                biPlanes = 1,
                biBitCount = 32,
            },
        };
        var hdc = NativeMethods.CreateCompatibleDC(IntPtr.Zero);
        var dib = NativeMethods.CreateDIBSection(hdc, ref bmi, 0, out var bits, IntPtr.Zero, 0);
        if (dib == IntPtr.Zero)
        {
            NativeMethods.DeleteDC(hdc);
            return null;
        }
        var old = NativeMethods.SelectObject(hdc, dib);
        try
        {
            NativeMethods.DrawIconEx(hdc, 0, 0, hicon, size, size, 0, IntPtr.Zero, 3);
            var pixels = new byte[size * size * 4];
            Marshal.Copy(bits, pixels, 0, pixels.Length);
            FixAlpha(pixels);
            return new RawImage(size, size, pixels);
        }
        finally
        {
            NativeMethods.SelectObject(hdc, old);
            NativeMethods.DeleteObject(dib);
            NativeMethods.DeleteDC(hdc);
        }
    }

    private static void FixAlpha(byte[] pixels)
    {
        for (var i = 3; i < pixels.Length; i += 4)
            if (pixels[i] != 0) return;
        for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
    }

    /// <summary>Average colour of opaque pixels (used to tint app channel tiles).</summary>
    public static (byte R, byte G, byte B) DominantColor(RawImage image)
    {
        long r = 0, g = 0, b = 0, n = 0;
        var p = image.Pixels;
        for (var i = 0; i < p.Length; i += 16)
        {
            if (p[i + 3] < 160) continue;
            int bb = p[i], gg = p[i + 1], rr = p[i + 2];
            var max = Math.Max(rr, Math.Max(gg, bb));
            var min = Math.Min(rr, Math.Min(gg, bb));
            var weight = 1 + (max - min) / 24;
            b += bb * weight; g += gg * weight; r += rr * weight; n += weight;
        }
        if (n == 0) return (120, 170, 210);
        return ((byte)(r / n), (byte)(g / n), (byte)(b / n));
    }
}

public static class AppActivation
{
    /// <summary>Starts a packaged (Store) app by AppUserModelID without needing Explorer.</summary>
    public static int Activate(string aumid, string? arguments)
    {
        return StaRunner.Run(() =>
        {
            var manager = (IApplicationActivationManager)new ApplicationActivationManager();
            try
            {
                var hr = manager.ActivateApplication(aumid, arguments, 0, out var pid);
                if (hr < 0) Marshal.ThrowExceptionForHR(hr);
                return (int)pid;
            }
            finally
            {
                Marshal.ReleaseComObject(manager);
            }
        }, TimeSpan.FromSeconds(30));
    }
}
