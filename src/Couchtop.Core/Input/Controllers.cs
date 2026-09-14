using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Couchtop.Core.Diagnostics;

namespace Couchtop.Core.Input;

[Flags]
public enum GamepadButtons : ushort
{
    None = 0,
    DPadUp = 0x0001,
    DPadDown = 0x0002,
    DPadLeft = 0x0004,
    DPadRight = 0x0008,
    Start = 0x0010,
    Back = 0x0020,
    LeftThumb = 0x0040,
    RightThumb = 0x0080,
    LeftShoulder = 0x0100,
    RightShoulder = 0x0200,
    Guide = 0x0400,
    A = 0x1000,
    B = 0x2000,
    X = 0x4000,
    Y = 0x8000,
}

public readonly record struct GamepadState(bool Connected, GamepadButtons Buttons, double LeftX, double LeftY, double RightX, double RightY, double LeftTrigger, double RightTrigger);

public static class StickMath
{
    public const int DefaultDeadzone = 7849;

    /// <summary>Radial deadzone; returns components in -1..1 with a smooth ramp outside the deadzone.</summary>
    public static (double X, double Y) Normalize(short rawX, short rawY, int deadzone = DefaultDeadzone)
    {
        double x = rawX, y = rawY;
        var magnitude = Math.Sqrt(x * x + y * y);
        if (magnitude <= deadzone) return (0, 0);
        var clipped = Math.Min(magnitude, 32767);
        var scaled = (clipped - deadzone) / (32767 - deadzone);
        return (x / magnitude * scaled, y / magnitude * scaled);
    }

    /// <summary>Pointer acceleration curve: fine control near center, fast at the edge.</summary>
    public static double Curve(double v) => Math.Sign(v) * Math.Pow(Math.Abs(v), 1.8);
}

public static class XInput
{
    [StructLayout(LayoutKind.Sequential)]
    private struct XINPUT_GAMEPAD
    {
        public ushort wButtons;
        public byte bLeftTrigger;
        public byte bRightTrigger;
        public short sThumbLX;
        public short sThumbLY;
        public short sThumbRX;
        public short sThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XINPUT_STATE
    {
        public uint dwPacketNumber;
        public XINPUT_GAMEPAD Gamepad;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XINPUT_BATTERY_INFORMATION
    {
        public byte BatteryType;
        public byte BatteryLevel;
    }

    [DllImport("xinput1_4.dll", EntryPoint = "#100")] private static extern int XInputGetStateEx(int index, out XINPUT_STATE state);
    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")] private static extern int XInputGetState(int index, out XINPUT_STATE state);
    [DllImport("xinput1_4.dll")] private static extern int XInputGetBatteryInformation(int index, byte devType, out XINPUT_BATTERY_INFORMATION info);

    private static bool _exAvailable = true;
    private static bool _available = true;

    public static GamepadState GetState(int index)
    {
        if (!_available) return default;
        XINPUT_STATE s;
        int result;
        try
        {
            if (_exAvailable)
            {
                try { result = XInputGetStateEx(index, out s); }
                catch (EntryPointNotFoundException) { _exAvailable = false; result = XInputGetState(index, out s); }
            }
            else
            {
                result = XInputGetState(index, out s);
            }
        }
        catch (DllNotFoundException)
        {
            _available = false;
            return default;
        }

        if (result != 0) return default;
        var (lx, ly) = StickMath.Normalize(s.Gamepad.sThumbLX, s.Gamepad.sThumbLY);
        var (rx, ry) = StickMath.Normalize(s.Gamepad.sThumbRX, s.Gamepad.sThumbRY, 8689);
        return new GamepadState(true, (GamepadButtons)s.Gamepad.wButtons, lx, ly, rx, ry, s.Gamepad.bLeftTrigger / 255.0, s.Gamepad.bRightTrigger / 255.0);
    }

    /// <summary>0 = empty, 1 = low, 2 = medium, 3 = full, null = wired/unknown.</summary>
    public static int? BatteryLevel(int index)
    {
        try
        {
            if (XInputGetBatteryInformation(index, 0, out var info) != 0) return null;
            if (info.BatteryType is 0 or 1) return null;
            return info.BatteryLevel;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }
}

[Flags]
public enum WiimoteButtons : ushort
{
    None = 0,
    Two = 0x0001,
    One = 0x0002,
    B = 0x0004,
    A = 0x0008,
    Minus = 0x0010,
    Home = 0x0080,
    Left = 0x0100,
    Right = 0x0200,
    Down = 0x0400,
    Up = 0x0800,
    Plus = 0x1000,
}

public readonly record struct IrDot(int X, int Y, int Size);

public sealed record WiimoteInput(byte ReportId, WiimoteButtons Buttons, IReadOnlyList<IrDot> Dots, int? BatteryPercent);

public static class WiimoteReportParser
{
    private const ushort ButtonMask = 0x1F9F;

    public static WiimoteInput? Parse(ReadOnlySpan<byte> report)
    {
        if (report.Length < 3) return null;
        var id = report[0];
        var hasButtons = id is 0x20 or 0x21 or 0x22 || (id >= 0x30 && id <= 0x3F && id != 0x3D);
        if (!hasButtons) return null;
        var buttons = (WiimoteButtons)(((report[1] << 8) | report[2]) & ButtonMask);

        var dots = new List<IrDot>();
        int? battery = null;
        switch (id)
        {
            case 0x20 when report.Length >= 7:
                battery = Math.Clamp(report[6] * 100 / 200, 0, 100);
                break;
            case 0x33 when report.Length >= 18:
                for (var i = 0; i < 4; i++)
                {
                    var dot = ParseExtended(report.Slice(6 + i * 3, 3));
                    if (dot is not null) dots.Add(dot.Value);
                }
                break;
            case 0x36 when report.Length >= 13:
                dots.AddRange(ParseBasic(report.Slice(3, 10)));
                break;
            case 0x37 when report.Length >= 16:
                dots.AddRange(ParseBasic(report.Slice(6, 10)));
                break;
        }
        return new WiimoteInput(id, buttons, dots, battery);
    }

    public static IrDot? ParseExtended(ReadOnlySpan<byte> b)
    {
        if (b.Length < 3 || (b[0] == 0xFF && b[1] == 0xFF && b[2] == 0xFF)) return null;
        var x = b[0] | ((b[2] >> 4) & 0x03) << 8;
        var y = b[1] | ((b[2] >> 6) & 0x03) << 8;
        if (x >= 1023 && y >= 1023) return null;
        return new IrDot(x, y, b[2] & 0x0F);
    }

    public static IEnumerable<IrDot> ParseBasic(ReadOnlySpan<byte> b)
    {
        var result = new List<IrDot>();
        for (var pair = 0; pair < 2; pair++)
        {
            var o = pair * 5;
            var x1 = b[o] | ((b[o + 2] >> 4) & 0x03) << 8;
            var y1 = b[o + 1] | ((b[o + 2] >> 6) & 0x03) << 8;
            var x2 = b[o + 3] | (b[o + 2] & 0x03) << 8;
            var y2 = b[o + 4] | ((b[o + 2] >> 2) & 0x03) << 8;
            if (x1 < 1023 && y1 < 1023) result.Add(new IrDot(x1, y1, 3));
            if (x2 < 1023 && y2 < 1023) result.Add(new IrDot(x2, y2, 3));
        }
        return result;
    }
}

public static class WiimotePointerMapper
{
    /// <summary>
    /// Maps sensor-bar dots to a normalized screen point (0..1). The IR camera sees the image mirrored,
    /// so X is inverted. The central region of the camera is expanded to cover the whole screen.
    /// </summary>
    public static (double X, double Y)? Map(IReadOnlyList<IrDot> dots)
    {
        if (dots.Count == 0) return null;
        var pair = dots.OrderByDescending(d => d.Size).Take(2).ToList();
        var mx = pair.Average(d => d.X);
        var my = pair.Average(d => d.Y);
        var nx = 1.0 - mx / 1023.0;
        var ny = my / 767.0;
        const double gain = 1.6;
        return (Math.Clamp((nx - 0.5) * gain + 0.5, 0, 1), Math.Clamp((ny - 0.5) * gain + 0.5, 0, 1));
    }
}

/// <summary>
/// Real Wii Remote support over Bluetooth HID (RVL-CNT-01 and -TR). Pair the remote in Windows Bluetooth
/// settings first; Couchtop then enables the IR camera and reads buttons and pointer data.
/// </summary>
public sealed class WiimoteManager : IDisposable
{
    private const ushort NintendoVendorId = 0x057E;
    private static readonly ushort[] ProductIds = { 0x0306, 0x0330 };
    private const int ReportLength = 22;

    private CancellationTokenSource? _cts;
    private Thread? _thread;

    public event Action<WiimoteInput>? InputReceived;
    public event Action<bool>? ConnectionChanged;

    public bool IsConnected { get; private set; }

    public void Start()
    {
        if (_thread is not null) return;
        _cts = new CancellationTokenSource();
        _thread = new Thread(() => Loop(_cts.Token)) { IsBackground = true, Name = "Wiimote", Priority = ThreadPriority.BelowNormal };
        _thread.Start();
    }

    public void Stop()
    {
        _cts?.Cancel();
        _thread = null;
    }

    private void Loop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            string? path = null;
            try
            {
                path = Hid.FindDevice(NintendoVendorId, ProductIds);
            }
            catch (Exception ex)
            {
                Log.Warn("HID enumeration failed", ex);
            }

            if (path is null)
            {
                ct.WaitHandle.WaitOne(TimeSpan.FromSeconds(4));
                continue;
            }

            try
            {
                RunDevice(path, ct);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or UnauthorizedAccessException or ObjectDisposedException)
            {
                Log.Info("Wiimote disconnected: " + ex.Message);
            }
            finally
            {
                if (IsConnected)
                {
                    IsConnected = false;
                    ConnectionChanged?.Invoke(false);
                }
            }
            ct.WaitHandle.WaitOne(TimeSpan.FromSeconds(2));
        }
    }

    private void RunDevice(string path, CancellationToken ct)
    {
        using var handle = Hid.Open(path);
        using var stream = new FileStream(handle, FileAccess.ReadWrite, 1, isAsync: true);
        Log.Info("Wiimote connected: " + path);

        void Send(params byte[] data)
        {
            var buffer = new byte[ReportLength];
            data.CopyTo(buffer, 0);
            try
            {
                stream.Write(buffer, 0, buffer.Length);
            }
            catch (IOException)
            {
                if (!Hid.SetOutputReport(handle, buffer)) throw;
            }
            Thread.Sleep(40);
        }

        void WriteRegister(int address, params byte[] data)
        {
            var packet = new byte[22];
            packet[0] = 0x16;
            packet[1] = 0x04;
            packet[2] = (byte)(address >> 16);
            packet[3] = (byte)(address >> 8);
            packet[4] = (byte)address;
            packet[5] = (byte)data.Length;
            data.CopyTo(packet, 6);
            Send(packet);
        }

        Send(0x11, 0x10); // player 1 LED
        Send(0x13, 0x04); // IR pixel clock
        Send(0x1A, 0x04); // IR logic
        WriteRegister(0xB00030, 0x08);
        WriteRegister(0xB00000, 0x02, 0x00, 0x00, 0x71, 0x01, 0x00, 0xAA, 0x00, 0x64);
        WriteRegister(0xB0001A, 0x63, 0x03);
        WriteRegister(0xB00033, 0x03); // extended IR mode
        WriteRegister(0xB00030, 0x08);
        Send(0x12, 0x04, 0x33); // continuous buttons + accelerometer + IR
        Send(0x15, 0x00); // status (battery)

        IsConnected = true;
        ConnectionChanged?.Invoke(true);

        var buffer = new byte[ReportLength];
        var lastStatus = DateTime.UtcNow;
        while (!ct.IsCancellationRequested)
        {
            var read = stream.ReadAsync(buffer.AsMemory(0, ReportLength), ct).AsTask().GetAwaiter().GetResult();
            if (read <= 0) throw new IOException("Device closed");
            var input = WiimoteReportParser.Parse(buffer.AsSpan(0, read));
            if (input is not null)
            {
                InputReceived?.Invoke(input);
                if (input.ReportId == 0x20) Send(0x12, 0x04, 0x33); // status reports reset the mode
            }
            if (DateTime.UtcNow - lastStatus > TimeSpan.FromSeconds(60))
            {
                lastStatus = DateTime.UtcNow;
                Send(0x15, 0x00);
            }
        }
    }

    public void Dispose() => Stop();

    private static class Hid
    {
        private const uint DIGCF_PRESENT = 0x02;
        private const uint DIGCF_DEVICEINTERFACE = 0x10;
        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_READ = 1;
        private const uint FILE_SHARE_WRITE = 2;
        private const uint OPEN_EXISTING = 3;
        private const uint FILE_FLAG_OVERLAPPED = 0x40000000;

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVICE_INTERFACE_DATA
        {
            public int cbSize;
            public Guid InterfaceClassGuid;
            public int Flags;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HIDD_ATTRIBUTES
        {
            public int Size;
            public ushort VendorID;
            public ushort ProductID;
            public ushort VersionNumber;
        }

        [DllImport("hid.dll")] private static extern void HidD_GetHidGuid(out Guid guid);
        [DllImport("hid.dll")] private static extern bool HidD_GetAttributes(SafeFileHandle handle, ref HIDD_ATTRIBUTES attributes);
        [DllImport("hid.dll")] private static extern bool HidD_SetOutputReport(SafeFileHandle handle, byte[] buffer, int length);
        [DllImport("setupapi.dll", SetLastError = true)] private static extern IntPtr SetupDiGetClassDevs(ref Guid guid, IntPtr enumerator, IntPtr parent, uint flags);
        [DllImport("setupapi.dll", SetLastError = true)] private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr info, ref Guid guid, int index, ref SP_DEVICE_INTERFACE_DATA data);
        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail, int size, out int required, IntPtr info);
        [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

        public static bool SetOutputReport(SafeFileHandle handle, byte[] buffer) => HidD_SetOutputReport(handle, buffer, buffer.Length);

        public static SafeFileHandle Open(string path)
        {
            var handle = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
            if (handle.IsInvalid) throw new IOException("Could not open HID device: " + Marshal.GetLastWin32Error());
            return handle;
        }

        public static string? FindDevice(ushort vendor, ushort[] products)
        {
            HidD_GetHidGuid(out var guid);
            var set = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
            if (set == new IntPtr(-1)) return null;
            try
            {
                for (var i = 0; ; i++)
                {
                    var data = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
                    if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref data)) return null;
                    SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out var required, IntPtr.Zero);
                    if (required <= 0) continue;
                    var detail = Marshal.AllocHGlobal(required);
                    try
                    {
                        Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                        if (!SetupDiGetDeviceInterfaceDetail(set, ref data, detail, required, out _, IntPtr.Zero)) continue;
                        var path = Marshal.PtrToStringUni(detail + 4);
                        if (path is null) continue;
                        using var handle = CreateFile(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                        if (handle.IsInvalid) continue;
                        var attrs = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() };
                        if (HidD_GetAttributes(handle, ref attrs) && attrs.VendorID == vendor && products.Contains(attrs.ProductID)) return path;
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(detail);
                    }
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(set);
            }
        }
    }
}
