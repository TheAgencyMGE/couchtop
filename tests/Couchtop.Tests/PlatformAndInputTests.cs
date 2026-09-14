using Couchtop.Core.Input;
using Couchtop.Core.Native;
using Couchtop.Core.Platform;

namespace Couchtop.Tests;

public class HotkeyTests
{
    [Fact]
    public void Parses_emergency_hotkey()
    {
        Assert.True(HotkeyParser.TryParse(HotkeyParser.EmergencyHotkey, out var hk));
        Assert.Equal(NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_SHIFT, hk.Modifiers);
        Assert.Equal(0x7Bu, hk.VirtualKey);
    }

    [Theory]
    [InlineData("Ctrl+Alt+Home", 0x24u)]
    [InlineData("win + shift + h", (uint)'H')]
    [InlineData("Control+F1", 0x70u)]
    public void Parses_valid(string text, uint vk)
    {
        Assert.True(HotkeyParser.TryParse(text, out var hk));
        Assert.Equal(vk, hk.VirtualKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Home")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+A+B")]
    [InlineData("Ctrl+F99")]
    public void Rejects_invalid(string text)
    {
        Assert.False(HotkeyParser.TryParse(text, out _));
    }
}

public class StartupAppTests
{
    [Theory]
    [InlineData("\"C:\\Program Files\\App\\app.exe\" --tray", "C:\\Program Files\\App\\app.exe", "--tray")]
    [InlineData("C:\\Tools\\tool.exe /background", "C:\\Tools\\tool.exe", "/background")]
    [InlineData("C:\\Program Files\\App\\app.exe -minimized", "C:\\Program Files\\App\\app.exe", "-minimized")]
    [InlineData("rundll32.exe shell32.dll,Control_RunDLL", "rundll32.exe", "shell32.dll,Control_RunDLL")]
    public void Splits_command_lines(string command, string file, string args)
    {
        var (f, a) = CommandLineSplitter.Split(command, p => p is "C:\\Program Files\\App\\app.exe" or "C:\\Tools\\tool.exe");
        Assert.Equal(file, f);
        Assert.Equal(args, a);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(new byte[] { 2, 0, 0 }, true)]
    [InlineData(new byte[] { 6, 0 }, true)]
    [InlineData(new byte[] { 3, 0 }, false)]
    [InlineData(new byte[] { 7 }, false)]
    public void Startup_approved_flags(byte[]? data, bool expected)
    {
        Assert.Equal(expected, StartupApps.IsApproved(data));
    }

    [Fact]
    public void Never_relaunches_couchtop_or_explorer()
    {
        Assert.True(StartupApps.ShouldSkip("\"C:\\x\\Couchtop.exe\" --autostart"));
        Assert.True(StartupApps.ShouldSkip("C:\\Windows\\explorer.exe"));
        Assert.False(StartupApps.ShouldSkip("C:\\OneDrive\\OneDrive.exe /background"));
    }

    [Fact]
    public void Collecting_real_startup_entries_does_not_throw()
    {
        var entries = StartupApps.Collect();
        Assert.All(entries, e => Assert.False(string.IsNullOrWhiteSpace(e.FileName)));
    }
}

public class InputTests
{
    [Fact]
    public void Stick_deadzone_and_scaling()
    {
        Assert.Equal((0.0, 0.0), StickMath.Normalize(3000, -3000));
        var (x, y) = StickMath.Normalize(32767, 0);
        Assert.Equal(1.0, x, 3);
        Assert.Equal(0.0, y, 3);
        var (dx, _) = StickMath.Normalize(-20000, 0);
        Assert.InRange(dx, -0.6, -0.4);
        Assert.Equal(0.0, StickMath.Curve(0));
        Assert.True(StickMath.Curve(0.5) < 0.5);
    }

    [Fact]
    public void Parses_wiimote_buttons_and_extended_ir()
    {
        var report = new byte[22];
        report[0] = 0x33;
        report[1] = 0x10 | 0x08; // Plus + Up
        report[2] = 0x08 | 0x80; // A + Home
        // dot 1: x=512 (0x200), y=384 (0x180), size 4
        report[6] = 0x00; report[7] = 0x80; report[8] = (byte)((0x2 << 4) | (0x1 << 6) | 4);
        // dot 2: x=600, y=384
        report[9] = 0x58; report[10] = 0x80; report[11] = (byte)((0x2 << 4) | (0x1 << 6) | 3);
        report[12] = 0xFF; report[13] = 0xFF; report[14] = 0xFF;
        report[15] = 0xFF; report[16] = 0xFF; report[17] = 0xFF;

        var input = WiimoteReportParser.Parse(report)!;
        Assert.Equal(WiimoteButtons.Plus | WiimoteButtons.Up | WiimoteButtons.A | WiimoteButtons.Home, input.Buttons);
        Assert.Equal(2, input.Dots.Count);
        Assert.Equal(new IrDot(512, 384, 4), input.Dots[0]);
        Assert.Equal(600, input.Dots[1].X);

        var point = WiimotePointerMapper.Map(input.Dots)!.Value;
        Assert.InRange(point.X, 0.4, 0.5);
        Assert.InRange(point.Y, 0.49, 0.51);
    }

    [Fact]
    public void Status_report_battery_and_unknown_reports()
    {
        var status = new byte[22];
        status[0] = 0x20;
        status[6] = 200;
        Assert.Equal(100, WiimoteReportParser.Parse(status)!.BatteryPercent);
        Assert.Null(WiimoteReportParser.Parse(new byte[] { 0x3D, 0, 0, 0 }));
        Assert.Null(WiimoteReportParser.Parse(new byte[] { 0x33 }));
        Assert.Null(WiimotePointerMapper.Map(Array.Empty<IrDot>()));
    }

    [Fact]
    public void Xinput_polling_is_safe_without_controllers()
    {
        for (var i = 0; i < 4; i++) _ = XInput.GetState(i);
    }
}
