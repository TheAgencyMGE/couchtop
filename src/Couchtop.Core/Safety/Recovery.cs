using System.Diagnostics;
using Couchtop.Core.Diagnostics;
using Couchtop.Core.Native;
using Couchtop.Core.Settings;
using Couchtop.Core.Shell;
using Couchtop.Core.Storage;

namespace Couchtop.Core.Safety;

public interface IExplorerController
{
    bool IsShellRunning();
    void StartExplorer();
}

public sealed class ExplorerController : IExplorerController
{
    public bool IsShellRunning() => NativeMethods.IsExplorerShellRunning();

    public void StartExplorer()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        using var p = Process.Start(new ProcessStartInfo(Path.Combine(windows, "explorer.exe")) { UseShellExecute = false, WorkingDirectory = windows });
        Log.Info("Started explorer.exe");
    }
}

public sealed record RecoveryOptions(bool StartExplorer = true, bool Force = false, bool StopCouchtop = false);

public sealed record RecoveryReport(bool Success, IReadOnlyList<string> Steps, ShellStatus? FinalStatus);

/// <summary>Restores Explorer. Shared by the Recovery tool, Setup (uninstall), emergency handler, and Guardian.</summary>
public sealed class RecoveryService
{
    private readonly ShellModeManager _shell;
    private readonly IExplorerController _explorer;
    private readonly Action? _stopCouchtop;

    public RecoveryService(ShellModeManager shell, IExplorerController explorer, Action? stopCouchtop = null)
    {
        _shell = shell;
        _explorer = explorer;
        _stopCouchtop = stopCouchtop;
    }

    public RecoveryReport Restore(RecoveryOptions options)
    {
        var steps = new List<string>();
        if (options.StopCouchtop && _stopCouchtop is not null)
        {
            try
            {
                _stopCouchtop();
                steps.Add("Stopped running Couchtop processes.");
            }
            catch (Exception ex)
            {
                steps.Add("Could not stop Couchtop processes: " + ex.Message);
            }
        }

        var change = options.Force ? _shell.ForceRestore() : _shell.Disable();
        steps.Add(change.Message);

        if (options.StartExplorer)
        {
            try
            {
                if (_explorer.IsShellRunning())
                {
                    steps.Add("Explorer is already running.");
                }
                else
                {
                    _explorer.StartExplorer();
                    steps.Add("Started Windows Explorer.");
                }
            }
            catch (Exception ex)
            {
                steps.Add("Could not start Explorer: " + ex.Message);
            }
        }

        ShellStatus? status = null;
        try { status = _shell.GetStatus(); } catch (Exception ex) { steps.Add("Could not read shell status: " + ex.Message); }
        var success = status is not null && !status.EnabledForCurrentUser && (!options.Force || status.CurrentValue is null);
        return new RecoveryReport(success, steps, status);
    }

    public static void StopCouchtopProcesses()
    {
        var session = NativeMethods.CurrentSessionId();
        foreach (var name in new[] { "Couchtop.Guardian", "Couchtop" })
        {
            foreach (var p in Process.GetProcessesByName(name))
            {
                using (p)
                {
                    try
                    {
                        if (p.Id == Environment.ProcessId || p.SessionId != session) continue;
                        p.Kill(entireProcessTree: false);
                        p.WaitForExit(3000);
                    }
                    catch (Exception ex)
                    {
                        Log.Warn($"Could not stop {name} ({p.Id})", ex);
                    }
                }
            }
        }
    }
}

/// <summary>Emergency shortcut action: Explorer first (most important), then disable shell for the next sign-in.</summary>
public sealed class EmergencyService
{
    private readonly ShellModeManager _shell;
    private readonly IExplorerController _explorer;

    public EmergencyService(ShellModeManager shell, IExplorerController explorer)
    {
        _shell = shell;
        _explorer = explorer;
    }

    public IReadOnlyList<string> Execute()
    {
        var steps = new List<string>();
        try
        {
            if (!_explorer.IsShellRunning())
            {
                _explorer.StartExplorer();
                steps.Add("Explorer started");
            }
            else
            {
                steps.Add("Explorer already running");
            }
        }
        catch (Exception ex)
        {
            steps.Add("Explorer start failed: " + ex.Message);
        }
        steps.Add(_shell.Disable().Message);
        Log.Warn("EMERGENCY: " + string.Join(" | ", steps));
        return steps;
    }
}

public sealed record VerificationCheck(string Name, bool Passed, string Detail);

/// <summary>
/// Proves the restore logic works on this machine without touching the real shell setting: every scenario
/// runs against a throwaway registry sandbox under HKCU\Software\Couchtop\SafetyTest.
/// </summary>
public static class RecoveryVerifier
{
    public static IReadOnlyList<VerificationCheck> Verify(InstallLayout layout, string? windowsDirectory = null)
    {
        var windows = windowsDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var checks = new List<VerificationCheck>();
        var sandbox = new CurrentUserRegistryStore($@"{ShellRegistryPaths.SandboxRoot}\verify-{Guid.NewGuid():N}");
        try
        {
            var command = ShellCommandBuilder.Build(layout.GuardianPath, ShellBootstrapMode.Resilient, windows);

            checks.Add(Run("Restore with no previous shell", () =>
            {
                var shell = new ShellModeManager(sandbox, layout, windowsDirectory: windows);
                shell.Mechanism.Apply(command);
                if (!shell.GetStatus().EnabledForCurrentUser) return "apply failed";
                var result = shell.Disable();
                return result.Success && sandbox.GetString(ShellRegistryPaths.WinlogonKey, ShellRegistryPaths.ShellValue) is null ? null : "value was not removed";
            }));

            checks.Add(Run("Restore a previous per-user shell", () =>
            {
                const string previous = @"C:\Tools\previous-shell.exe";
                sandbox.SetString(ShellRegistryPaths.WinlogonKey, ShellRegistryPaths.ShellValue, previous);
                var shell = new ShellModeManager(sandbox, layout, windowsDirectory: windows);
                shell.Mechanism.Apply(command);
                shell.Disable();
                var value = sandbox.GetString(ShellRegistryPaths.WinlogonKey, ShellRegistryPaths.ShellValue);
                sandbox.DeleteValue(ShellRegistryPaths.WinlogonKey, ShellRegistryPaths.ShellValue);
                return value == previous ? null : $"expected previous shell, found '{value}'";
            }));

            checks.Add(Run("Forced recovery clears any override", () =>
            {
                sandbox.SetString(ShellRegistryPaths.WinlogonKey, ShellRegistryPaths.ShellValue, @"C:\Other\shell.exe");
                var shell = new ShellModeManager(sandbox, layout, windowsDirectory: windows);
                var result = shell.ForceRestore();
                return result.Success && sandbox.GetString(ShellRegistryPaths.WinlogonKey, ShellRegistryPaths.ShellValue) is null ? null : "override remained";
            }));

            checks.Add(Run("Boot-loop counter resets on restore", () =>
            {
                var shell = new ShellModeManager(sandbox, layout, windowsDirectory: windows);
                shell.BootGuard.BeginBoot();
                shell.BootGuard.BeginBoot();
                shell.Disable();
                return shell.BootGuard.PendingBoots == 0 ? null : "counter not reset";
            }));

            checks.Add(Run("Shell mode cannot be enabled without safety checks", () =>
            {
                var shell = new ShellModeManager(sandbox, layout, windowsDirectory: windows);
                var result = shell.Enable(ShellBootstrapMode.Resilient);
                return !result.Success && sandbox.GetString(ShellRegistryPaths.WinlogonKey, ShellRegistryPaths.ShellValue) is null ? null : "enable was not blocked";
            }));
        }
        finally
        {
            try { sandbox.DeleteSandbox(); } catch (Exception ex) { Log.Warn("Sandbox cleanup failed", ex); }
        }

        var explorer = Path.Combine(windows, "explorer.exe");
        checks.Add(new VerificationCheck("explorer.exe present", File.Exists(explorer), explorer));
        var real = new CurrentUserRegistryStore();
        checks.Add(new VerificationCheck("Per-user Winlogon key writable", real.CanWrite(ShellRegistryPaths.WinlogonKey), ShellRegistryPaths.WinlogonKey));
        return checks;
    }

    private static VerificationCheck Run(string name, Func<string?> scenario)
    {
        try
        {
            var error = scenario();
            return new VerificationCheck(name, error is null, error ?? "ok");
        }
        catch (Exception ex)
        {
            return new VerificationCheck(name, false, ex.Message);
        }
    }
}

public sealed class SessionRecord
{
    public bool Active { get; set; }
    public int Pid { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public int CleanExits { get; set; }
    public int UncleanExits { get; set; }
    public DateTimeOffset? LastCleanExitAt { get; set; }
    public DateTimeOffset? LastUncleanExitAt { get; set; }
}

/// <summary>Detects whether the previous Couchtop session ended without a clean exit (crash, power loss, kill).</summary>
public sealed class SessionSentinel
{
    private readonly string _path;

    public SessionSentinel(string path)
    {
        _path = path;
        Record = JsonStore.Load(path, () => new SessionRecord()).Value;
    }

    public SessionRecord Record { get; }
    public bool PreviousSessionCrashed { get; private set; }

    public void Begin()
    {
        if (Record.Active)
        {
            PreviousSessionCrashed = true;
            Record.UncleanExits++;
            Record.LastUncleanExitAt = DateTimeOffset.Now;
        }
        Record.Active = true;
        Record.Pid = Environment.ProcessId;
        Record.StartedAt = DateTimeOffset.Now;
        TrySave();
    }

    public void End(bool clean)
    {
        Record.Active = false;
        if (clean)
        {
            Record.CleanExits++;
            Record.LastCleanExitAt = DateTimeOffset.Now;
        }
        else
        {
            Record.UncleanExits++;
            Record.LastUncleanExitAt = DateTimeOffset.Now;
        }
        TrySave();
    }

    private void TrySave()
    {
        try { JsonStore.Save(_path, Record); }
        catch (Exception ex) { Log.Warn("Could not save session record", ex); }
    }
}

public static class InstanceSignals
{
    public const string AppMutexName = @"Local\Couchtop.App";
    public const string GuardianMutexName = @"Local\Couchtop.Guardian";

    public static string Name(string baseName, string? suffix) => $@"Local\Couchtop.{baseName}{(string.IsNullOrEmpty(suffix) ? "" : "." + suffix)}";

    public static EventWaitHandle Ready(string? suffix = null) => new(false, EventResetMode.ManualReset, Name("Ready", suffix));
    public static EventWaitHandle Heartbeat(string? suffix = null) => new(false, EventResetMode.AutoReset, Name("Heartbeat", suffix));
    public static EventWaitHandle Activate() => new(false, EventResetMode.AutoReset, Name("Activate", null));
    public static EventWaitHandle HomeMenu() => new(false, EventResetMode.AutoReset, Name("HomeMenu", null));
}
