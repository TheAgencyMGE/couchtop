using System.Runtime.InteropServices;
using Couchtop.Core.Diagnostics;
using Couchtop.Core.Native;
using Couchtop.Core.Platform;

namespace Couchtop.Core.Shell;

/// <summary>What a program asked the tray to do with one of its icons.</summary>
public enum TrayMessage
{
    Add = 0,
    Modify = 1,
    Delete = 2,
    SetFocus = 3,
    SetVersion = 4,
}

/// <summary>One background app's notification icon.</summary>
public sealed record TrayItem(IntPtr Owner, uint Id, string Tooltip, IntPtr Icon, uint CallbackMessage, bool Hidden)
{
    public string Key => $"{Owner.ToInt64():X}:{Id}";
}

/// <summary>A tray request decoded from the bytes a program sent to the shell.</summary>
public sealed record TrayRequest(TrayMessage Message, TrayItem Item);

/// <summary>
/// Decodes the NOTIFYICONDATA blob programs send to the shell with Shell_NotifyIcon. The struct grew over the
/// years and 32-bit programs send narrower pointers, so every field is read by offset with the size checked first.
/// </summary>
public static class TrayDataReader
{
    private const int ShellTrayHeader = 8;   // dwHz + dwMessage
    public const int NifMessage = 0x01;
    public const int NifIcon = 0x02;
    public const int NifTip = 0x04;
    public const int NifState = 0x08;

    /// <summary>Returns null when the buffer is too short or the message isn't one we handle.</summary>
    public static TrayRequest? Read(byte[] data, bool senderIs64Bit)
    {
        if (data.Length < ShellTrayHeader + 24) return null;
        var message = BitConverter.ToUInt32(data, 4);
        if (message > 4) return null;

        var pointerSize = senderIs64Bit ? 8 : 4;
        var offset = ShellTrayHeader;
        offset += 4;                                   // cbSize
        if (senderIs64Bit) offset += 4;                // padding before the 8-byte handle
        var owner = ReadPointer(data, offset, pointerSize);
        offset += pointerSize;
        var id = BitConverter.ToUInt32(data, offset);
        offset += 4;
        var flags = BitConverter.ToUInt32(data, offset);
        offset += 4;
        var callback = BitConverter.ToUInt32(data, offset);
        offset += 4;
        if (senderIs64Bit) offset += 4;                // padding before hIcon
        var icon = ReadPointer(data, offset, pointerSize);
        offset += pointerSize;

        var tooltip = ReadString(data, offset, 128);
        offset += 128 * 2;
        var hidden = false;
        if ((flags & NifState) != 0 && offset + 8 <= data.Length)
        {
            var state = BitConverter.ToUInt32(data, offset);
            var mask = BitConverter.ToUInt32(data, offset + 4);
            hidden = (state & mask & 1) != 0;          // NIS_HIDDEN
        }

        var item = new TrayItem(owner, id, tooltip,
            (flags & NifIcon) != 0 ? icon : IntPtr.Zero,
            (flags & NifMessage) != 0 ? callback : 0,
            hidden);
        return new TrayRequest((TrayMessage)message, item);
    }

    private static IntPtr ReadPointer(byte[] data, int offset, int size)
    {
        if (offset + size > data.Length) return IntPtr.Zero;
        return size == 8 ? new IntPtr(BitConverter.ToInt64(data, offset)) : new IntPtr(BitConverter.ToInt32(data, offset));
    }

    private static string ReadString(byte[] data, int offset, int maxChars)
    {
        if (offset >= data.Length) return "";
        var end = Math.Min(data.Length, offset + maxChars * 2);
        var chars = new List<char>();
        for (var i = offset; i + 1 < end; i += 2)
        {
            var c = (char)BitConverter.ToUInt16(data, i);
            if (c == '\0') break;
            chars.Add(c);
        }
        return new string(chars.ToArray()).Trim();
    }

    /// <summary>Applies a request to the current list, returning the new list (add, update in place, or remove).</summary>
    public static List<TrayItem> Apply(IReadOnlyList<TrayItem> current, TrayRequest request)
    {
        var items = current.ToList();
        var index = items.FindIndex(i => i.Owner == request.Item.Owner && i.Id == request.Item.Id);
        switch (request.Message)
        {
            case TrayMessage.Add when index < 0:
                items.Add(request.Item);
                break;
            case TrayMessage.Add:
            case TrayMessage.Modify when index >= 0:
                items[index] = Merge(items[index], request.Item);
                break;
            case TrayMessage.Modify:
                items.Add(request.Item);
                break;
            case TrayMessage.Delete when index >= 0:
                items.RemoveAt(index);
                break;
        }
        return items;
    }

    /// <summary>A modify only carries the fields the program set, so keep what it left out.</summary>
    private static TrayItem Merge(TrayItem existing, TrayItem update) => existing with
    {
        Tooltip = string.IsNullOrEmpty(update.Tooltip) ? existing.Tooltip : update.Tooltip,
        Icon = update.Icon == IntPtr.Zero ? existing.Icon : update.Icon,
        CallbackMessage = update.CallbackMessage == 0 ? existing.CallbackMessage : update.CallbackMessage,
        Hidden = update.Hidden,
    };
}

/// <summary>
/// Stands in for Explorer's notification area while Couchtop is the shell: it owns the Shell_TrayWnd window that
/// background programs look for, keeps their icons, and forwards clicks back to them.
/// Only ever created in a shell session, so it can never take icons away from a running Explorer taskbar.
/// </summary>
public sealed class TrayHost : IDisposable
{
    private const int WM_COPYDATA = 0x004A;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONUP = 0x0205;
    private const uint HWND_BROADCAST = 0xFFFF;

    private readonly List<TrayItem> _items = new();
    private NativeMessageWindow? _tray;
    private NativeMessageWindow? _notify;

    public IReadOnlyList<TrayItem> Items => _items;

    /// <summary>Raised when a program added, changed or removed an icon.</summary>
    public event Action? Changed;

    /// <summary>Creates the shell tray windows. Returns false when they could not be created.</summary>
    public bool Start()
    {
        try
        {
            _tray = new NativeMessageWindow("Shell_TrayWnd");
            _notify = new NativeMessageWindow("TrayNotifyWnd");
            _tray.MessageHandler = OnMessage;
            AnnounceTaskbarCreated();
            Log.Info("Tray host started (Couchtop is the notification area)");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Could not start the tray host", ex);
            Dispose();
            return false;
        }
    }

    /// <summary>Tells every program to add its icons again, the way Explorer does when it restarts.</summary>
    public void AnnounceTaskbarCreated()
    {
        var message = RegisterWindowMessage("TaskbarCreated");
        if (message != 0) PostMessage(new IntPtr(HWND_BROADCAST), message, IntPtr.Zero, IntPtr.Zero);
    }

    private IntPtr? OnMessage(uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg != WM_COPYDATA) return null;
        try
        {
            var copy = Marshal.PtrToStructure<COPYDATASTRUCT>(lParam);
            if (copy.dwData.ToInt64() != 1 || copy.cbData <= 0 || copy.cbData > 4096) return new IntPtr(1);
            var bytes = new byte[copy.cbData];
            Marshal.Copy(copy.lpData, bytes, 0, copy.cbData);

            var request = ReadEitherLayout(bytes);
            if (request is null) return new IntPtr(1);

            var updated = TrayDataReader.Apply(_items, request);
            _items.Clear();
            _items.AddRange(updated);
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Warn("Bad tray message ignored", ex);
        }
        return new IntPtr(1);
    }

    /// <summary>
    /// 32-bit and 64-bit programs lay the struct out differently and the size alone doesn't say which (older
    /// struct versions are shorter), so both are tried and the one that names a real window wins.
    /// </summary>
    internal static TrayRequest? ReadEitherLayout(byte[] bytes)
    {
        var wide = TrayDataReader.Read(bytes, senderIs64Bit: true);
        if (wide is not null && NativeMethods.IsWindow(wide.Item.Owner)) return wide;
        var narrow = TrayDataReader.Read(bytes, senderIs64Bit: false);
        if (narrow is not null && NativeMethods.IsWindow(narrow.Item.Owner)) return narrow;
        // Removals arrive after the window is gone; keep the layout that at least produced a handle.
        return wide?.Item.Owner != IntPtr.Zero ? wide : narrow ?? wide;
    }

    /// <summary>Sends a click back to the program that owns the icon.</summary>
    public void Click(TrayItem item, bool rightButton)
    {
        if (item.CallbackMessage == 0 || !NativeMethods.IsWindow(item.Owner)) return;
        NativeMethods.SetForegroundWindow(item.Owner);
        var mouse = rightButton ? WM_RBUTTONUP : WM_LBUTTONUP;
        PostMessage(item.Owner, item.CallbackMessage, new IntPtr(item.Id), new IntPtr(mouse));
    }

    /// <summary>Drops icons whose program has gone away.</summary>
    public bool PruneDeadOwners()
    {
        var removed = _items.RemoveAll(i => !NativeMethods.IsWindow(i.Owner));
        if (removed > 0) Changed?.Invoke();
        return removed > 0;
    }

    public void Dispose()
    {
        _notify?.Dispose();
        _notify = null;
        _tray?.Dispose();
        _tray = null;
        _items.Clear();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct COPYDATASTRUCT
    {
        public IntPtr dwData;
        public int cbData;
        public IntPtr lpData;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
