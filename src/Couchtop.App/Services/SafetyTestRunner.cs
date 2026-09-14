using System.Diagnostics;
using System.IO;
using Couchtop.Core.Diagnostics;
using Couchtop.Core.Safety;
using Couchtop.Core.Settings;
using Couchtop.Core.Shell;

namespace Couchtop.App.Services;

/// <summary>
/// Runs every Shell Safety Test step against the installed binaries. Nothing here changes the real shell
/// setting: registry work happens in HKCU\Software\Couchtop\SafetyTest sandboxes and Explorer starts are simulated.
/// </summary>
public sealed class SafetyTestRunner
{
    public static readonly IReadOnlyList<(string Id, string Title)> Steps = new[]
    {
        (SafetyStepIds.Compatibility, "Compatibility check"),
        (SafetyStepIds.RecoveryTool, "Recovery tool self-test"),
        (SafetyStepIds.RecoveryScript, "Emergency recovery script"),
        (SafetyStepIds.RegistryRoundTrip, "Shell setting can be changed and restored"),
        (SafetyStepIds.CrashRecovery, "Crash recovery rehearsal"),
        (SafetyStepIds.HangRecovery, "Freeze recovery rehearsal"),
        (SafetyStepIds.BootLoopGuard, "Boot-loop protection rehearsal"),
        (SafetyStepIds.EmergencyHotkey, "Emergency shortcut Ctrl+Alt+Shift+F12"),
        (SafetyStepIds.NormalSessions, "Used in normal mode first"),
    };

    private readonly AppHost _host;
    private readonly Func<bool> _hotkeyRegistered;

    public SafetyTestRunner(AppHost host, Func<bool> hotkeyRegistered)
    {
        _host = host;
        _hotkeyRegistered = hotkeyRegistered;
    }

    public async Task<SafetyVerificationRecord> RunAsync(Action<SafetyStepResult> started, Action<SafetyStepResult> finished, Func<CancellationToken, Task<bool>> waitForHotkey, CancellationToken ct)
    {
        var record = new SafetyVerificationRecord
        {
            AppVersion = typeof(SafetyTestRunner).Assembly.GetName().Version?.ToString() ?? "",
            InstallDirectory = _host.Install.Directory,
        };

        foreach (var (id, title) in Steps)
        {
            ct.ThrowIfCancellationRequested();
            var step = new SafetyStepResult { Id = id, Title = title, Detail = "Running…" };
            started(step);
            try
            {
                var (passed, detail) = await RunStepAsync(id, waitForHotkey, ct);
                step.Passed = passed;
                step.Detail = detail;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                step.Passed = false;
                step.Detail = ex.Message;
            }
            record.Steps.Add(step);
            finished(step);
            Log.Info($"Safety step {id}: {(step.Passed ? "PASS" : "FAIL")} - {step.Detail}");
        }

        record.CompletedAt = DateTimeOffset.Now;
        record.AppSha256 = FileHash.Sha256(_host.Install.AppPath);
        record.GuardianSha256 = FileHash.Sha256(_host.Install.GuardianPath);
        record.RecoverySha256 = FileHash.Sha256(_host.Install.RecoveryPath);
        _host.SafetyStore.Save(record);
        return record;
    }

    private async Task<(bool, string)> RunStepAsync(string id, Func<CancellationToken, Task<bool>> waitForHotkey, CancellationToken ct)
    {
        switch (id)
        {
            case SafetyStepIds.Compatibility:
            {
                var report = await Task.Run(() => _host.Compatibility.Run(), ct);
                var blocking = report.Items.Where(i => i.Severity == CheckSeverity.Blocking).ToList();
                var warnings = report.Items.Count(i => i.Severity == CheckSeverity.Warning);
                return blocking.Count == 0
                    ? (true, $"No blocking problems ({warnings} warning{(warnings == 1 ? "" : "s")}).")
                    : (false, string.Join("  ", blocking.Select(b => $"{b.Title}: {b.Detail}")));
            }
            case SafetyStepIds.RecoveryTool:
            {
                if (!File.Exists(_host.Install.RecoveryPath)) return (false, "Couchtop.Recovery.exe is missing.");
                var (code, _) = await RunProcessAsync(_host.Install.RecoveryPath, "--verify --quiet --report {report}", TimeSpan.FromSeconds(90), ct);
                return code == 0 ? (true, "Restore logic verified in a registry sandbox.") : (false, $"The recovery self-test failed (exit code {code}).");
            }
            case SafetyStepIds.RecoveryScript:
            {
                var path = _host.Install.RecoveryScriptPath;
                if (!File.Exists(path)) return (false, "Recover-Explorer.cmd is missing.");
                var text = await File.ReadAllTextAsync(path, ct);
                return text.Contains("reg delete", StringComparison.OrdinalIgnoreCase) && text.Contains("explorer.exe", StringComparison.OrdinalIgnoreCase)
                    ? (true, "Present; restores Explorer without needing .NET.")
                    : (false, "Recover-Explorer.cmd looks damaged.");
            }
            case SafetyStepIds.RegistryRoundTrip:
                return await Task.Run(RegistryRoundTrip, ct);
            case SafetyStepIds.CrashRecovery:
                return await RehearsalAsync("crash", "Couchtop was restarted twice, then Explorer was restored and shell mode was turned off.", ct);
            case SafetyStepIds.HangRecovery:
                return await RehearsalAsync("hang", "A frozen Couchtop was detected and stopped, and Explorer was restored.", ct);
            case SafetyStepIds.BootLoopGuard:
                return await RehearsalAsync("bootloop", "Repeated failed sign-ins go straight to Explorer.", ct);
            case SafetyStepIds.EmergencyHotkey:
            {
                if (!_hotkeyRegistered()) return (false, "Another program is using Ctrl+Alt+Shift+F12, so the shortcut could not be verified.");
                return await waitForHotkey(ct) ? (true, "The shortcut was received.") : (false, "The shortcut was not pressed in time.");
            }
            case SafetyStepIds.NormalSessions:
            {
                var clean = _host.Session.Record.CleanExits;
                return clean >= 1
                    ? (true, $"{clean} clean normal-mode session{(clean == 1 ? "" : "s")} recorded.")
                    : (false, "Use Couchtop in normal mode first, exit it once from Power › Exit Couchtop, then run this test again.");
            }
            default:
                return (false, "Unknown step.");
        }
    }

    private (bool, string) RegistryRoundTrip()
    {
        var sandbox = new CurrentUserRegistryStore($@"{ShellRegistryPaths.SandboxRoot}\app-{Guid.NewGuid():N}");
        try
        {
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var manager = new ShellModeManager(sandbox, _host.Install, windowsDirectory: windows);
            manager.Mechanism.Apply(ShellCommandBuilder.Build(_host.Install.GuardianPath, ShellBootstrapMode.Resilient, windows));
            if (!manager.GetStatus().EnabledForCurrentUser) return (false, "The sandboxed shell value could not be written.");
            var disable = manager.Disable();
            if (!disable.Success || sandbox.GetString(ShellRegistryPaths.WinlogonKey, ShellRegistryPaths.ShellValue) is not null)
                return (false, "The sandboxed shell value could not be removed.");
            if (!_host.Registry.CanWrite(ShellRegistryPaths.WinlogonKey)) return (false, "Your per-user Winlogon key is not writable.");
            return (true, "Enable and disable work; your per-user shell key is writable.");
        }
        finally
        {
            try { sandbox.DeleteSandbox(); } catch (Exception ex) { Log.Warn("Sandbox cleanup failed", ex); }
        }
    }

    private async Task<(bool, string)> RehearsalAsync(string scenario, string success, CancellationToken ct)
    {
        if (!File.Exists(_host.Install.GuardianPath)) return (false, "Couchtop.Guardian.exe is missing.");
        var (code, report) = await RunProcessAsync(_host.Install.GuardianPath, $"--rehearsal --scenario {scenario} --result {{report}}", TimeSpan.FromSeconds(150), ct);
        if (code == 0) return (true, success);
        var detail = report.Length > 240 ? report[..240] + "…" : report;
        return (false, $"Rehearsal failed (exit code {code}). {detail}");
    }

    private static async Task<(int ExitCode, string Report)> RunProcessAsync(string exe, string arguments, TimeSpan timeout, CancellationToken ct)
    {
        var report = Path.Combine(Path.GetTempPath(), $"couchtop-safety-{Guid.NewGuid():N}.json");
        using var process = Process.Start(new ProcessStartInfo(exe, arguments.Replace("{report}", $"\"{report}\"")) { UseShellExecute = false, CreateNoWindow = true })
                            ?? throw new InvalidOperationException("Could not start " + Path.GetFileName(exe));
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            ct.ThrowIfCancellationRequested();
            return (-1, "Timed out.");
        }
        var text = "";
        if (File.Exists(report))
        {
            text = await File.ReadAllTextAsync(report, CancellationToken.None);
            try { File.Delete(report); } catch (IOException) { }
        }
        return (process.ExitCode, text);
    }
}
