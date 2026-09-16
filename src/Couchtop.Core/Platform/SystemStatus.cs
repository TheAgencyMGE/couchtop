using System.Diagnostics;
using Couchtop.Core.Diagnostics;
using Couchtop.Core.Native;

namespace Couchtop.Core.Platform;

/// <param name="Percent">Charge 0-100, or null when there is no battery.</param>
/// <param name="Remaining">Estimated time left on battery, when Windows reports one.</param>
public sealed record BatteryStatus(int? Percent, bool OnMains, bool Charging, TimeSpan? Remaining)
{
    public string Describe()
    {
        if (Percent is not { } percent) return OnMains ? "Plugged in" : "No battery";
        var state = Charging ? "charging" : OnMains ? "plugged in" : Remaining is { } left ? $"{Format(left)} left" : "on battery";
        return $"{percent}%  ·  {state}";
    }

    private static string Format(TimeSpan span) => span.TotalHours >= 1 ? $"{(int)span.TotalHours} h {span.Minutes} min" : $"{span.Minutes} min";
}

public static class PowerStatus
{
    public static BatteryStatus Read()
    {
        if (!NativeMethods.GetSystemPowerStatus(out var status)) return new BatteryStatus(null, true, false, null);
        var onMains = status.ACLineStatus == 1;
        var noBattery = (status.BatteryFlag & 128) != 0 || status.BatteryLifePercent > 100;
        var charging = (status.BatteryFlag & 8) != 0;
        var remaining = status.BatteryLifeTime > 0 ? TimeSpan.FromSeconds(status.BatteryLifeTime) : (TimeSpan?)null;
        return new BatteryStatus(noBattery ? null : status.BatteryLifePercent, onMains, charging, remaining);
    }
}

public sealed record WifiStatus(bool RadioPresent, bool Connected, string? Network, int? SignalPercent)
{
    public string Describe() =>
        !RadioPresent ? "No Wi-Fi adapter"
        : Connected ? $"{Network}{(SignalPercent is { } s ? $"  ·  {s}%" : "")}"
        : "Not connected";
}

/// <summary>
/// Reads the Wi-Fi state from "netsh wlan show interfaces". Parsing is separate from running the command so it
/// can be tested, and so a machine without Wi-Fi simply reports no adapter instead of failing.
/// </summary>
public static class Wifi
{
    public static WifiStatus Parse(string? output)
    {
        if (string.IsNullOrWhiteSpace(output)) return new WifiStatus(false, false, null, null);
        if (output.Contains("no wireless interface", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("not running", StringComparison.OrdinalIgnoreCase))
            return new WifiStatus(false, false, null, null);

        string? Value(string key)
        {
            foreach (var line in output.Split('\n'))
            {
                var trimmed = line.Trim();
                if (!trimmed.StartsWith(key, StringComparison.OrdinalIgnoreCase)) continue;
                var colon = trimmed.IndexOf(':');
                if (colon < 0) continue;
                var value = trimmed[(colon + 1)..].Trim();
                if (value.Length > 0) return value;
            }
            return null;
        }

        var state = Value("State");
        var connected = string.Equals(state, "connected", StringComparison.OrdinalIgnoreCase);
        var signal = Value("Signal");
        int? percent = signal is not null && int.TryParse(signal.TrimEnd('%', ' '), out var p) ? p : null;
        // "SSID" also matches "BSSID", so read the line that starts exactly with SSID.
        return new WifiStatus(true, connected, connected ? Value("SSID") : null, connected ? percent : null);
    }

    public static async Task<WifiStatus> ReadAsync(CancellationToken token = default)
    {
        try
        {
            var psi = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "netsh.exe"), "wlan show interfaces")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = Process.Start(psi);
            if (process is null) return new WifiStatus(false, false, null, null);
            var output = await process.StandardOutput.ReadToEndAsync(token);
            await process.WaitForExitAsync(token);
            return Parse(output);
        }
        catch (Exception ex)
        {
            Log.Warn("Could not read the Wi-Fi state", ex);
            return new WifiStatus(false, false, null, null);
        }
    }
}

/// <summary>Opens a Windows Settings page (these still work when Couchtop is the shell).</summary>
public static class WindowsSettings
{
    public const string Network = "ms-settings:network";
    public const string Wifi = "ms-settings:network-wifi";
    public const string Bluetooth = "ms-settings:bluetooth";
    public const string Display = "ms-settings:display";
    public const string Sound = "ms-settings:sound";
    public const string Power = "ms-settings:powersleep";
    public const string Accessibility = "ms-settings:easeofaccess";
    public const string Apps = "ms-settings:appsfeatures";
    public const string Update = "ms-settings:windowsupdate";
    public const string Home = "ms-settings:";

    public static bool Open(string page)
    {
        try
        {
            using var _ = Process.Start(new ProcessStartInfo(page) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn("Could not open " + page, ex);
            return false;
        }
    }
}
