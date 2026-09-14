using Couchtop.Core.Safety;

// Stand-in for Couchtop.exe in integration tests. Mirrors the app's safety-test switches.
var suffix = Arg("--signal-suffix");
var marker = Arg("--marker");
if (marker is not null) File.WriteAllText(marker, string.Join(" ", args));

if (Has("--crash-test")) return ExitCodes.CrashTest;

using var ready = InstanceSignals.Ready(suffix);
using var heartbeat = InstanceSignals.Heartbeat(suffix);

if (Has("--hang-test"))
{
    ready.Set();
    Thread.Sleep(TimeSpan.FromSeconds(60));
    return 0;
}

if (Has("--ok-test"))
{
    ready.Set();
    for (var i = 0; i < 6; i++)
    {
        heartbeat.Set();
        Thread.Sleep(300);
    }
    return ExitCodes.SwitchToExplorer;
}

return 0;

bool Has(string flag) => args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));
string? Arg(string flag)
{
    var i = Array.FindIndex(args, a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}
