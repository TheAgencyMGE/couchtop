using System.Text.Json;
using Couchtop.Core.Diagnostics;
using Couchtop.Core.Native;
using Couchtop.Core.Safety;
using Couchtop.Core.Shell;
using Couchtop.Core.Storage;

namespace Couchtop.Recovery;

/// <summary>
/// Standalone recovery tool. It never loads the Couchtop UI.
///   (no args)        interactive restore of Windows Explorer
///   --restore        restore without the confirmation prompt
///   --quiet          no message boxes
///   --force          also remove a non-Couchtop per-user shell override
///   --no-explorer    do not start explorer.exe
///   --verify         self-test restore logic in a registry sandbox (exit 0 = pass)
///   --status         report the current shell configuration
///   --report FILE    write a JSON report
///   --sandbox KEY    operate on HKCU\KEY instead of the real setting (testing only)
/// </summary>
internal static class Program
{
    private const string Title = "Couchtop Recovery";

    [STAThread]
    private static int Main(string[] args)
    {
        var paths = AppPaths.ForCurrentUser();
        Log.Initialize(paths.LogDirectory, "recovery");
        var quiet = Has(args, "--quiet");
        var report = Value(args, "--report");
        try
        {
            var layout = InstallLayout.Current;

            if (Has(args, "--verify"))
            {
                var checks = RecoveryVerifier.Verify(layout);
                var passed = checks.All(c => c.Passed);
                WriteReport(report, new { passed, checks });
                if (!quiet)
                {
                    var text = string.Join("\n", checks.Select(c => $"{(c.Passed ? "PASS" : "FAIL")}  {c.Name}  ({c.Detail})"));
                    NativeMethods.MessageBox(IntPtr.Zero, text, Title + (passed ? " - verification passed" : " - verification FAILED"),
                        passed ? NativeMethods.MB_ICONINFORMATION : NativeMethods.MB_ICONWARNING);
                }
                return passed ? 0 : 2;
            }

            var sandbox = Value(args, "--sandbox");
            var store = sandbox is null ? new CurrentUserRegistryStore() : new CurrentUserRegistryStore(sandbox);
            var shell = new ShellModeManager(store, layout);

            if (Has(args, "--status"))
            {
                var status = shell.GetStatus();
                WriteReport(report, new { status, pendingBoots = shell.BootGuard.PendingBoots, explorerRunning = NativeMethods.IsExplorerShellRunning() });
                if (!quiet)
                {
                    NativeMethods.MessageBox(IntPtr.Zero,
                        status.EnabledForCurrentUser ? $"Couchtop shell mode is ENABLED for this user.\n\n{status.CurrentValue}" :
                        status.CurrentValue is null ? "Windows Explorer is the shell for this user." : $"A different per-user shell is set:\n{status.CurrentValue}",
                        Title, NativeMethods.MB_ICONINFORMATION);
                }
                return 0;
            }

            if (!Has(args, "--restore") && !quiet)
            {
                var answer = NativeMethods.MessageBox(IntPtr.Zero,
                    "Restore Windows Explorer as your desktop?\n\nThis stops Couchtop, turns off Couchtop shell mode for your account, and starts Explorer. Your channels and settings are kept.",
                    Title, NativeMethods.MB_YESNO | NativeMethods.MB_ICONWARNING | NativeMethods.MB_SETFOREGROUND | NativeMethods.MB_TOPMOST);
                if (answer != NativeMethods.IDYES) return 1;
            }

            IExplorerController explorer = sandbox is null ? new ExplorerController() : new NullExplorer();
            var service = new RecoveryService(shell, explorer, sandbox is null ? RecoveryService.StopCouchtopProcesses : null);
            var result = service.Restore(new RecoveryOptions(StartExplorer: !Has(args, "--no-explorer"), Force: Has(args, "--force"), StopCouchtop: sandbox is null));

            if (!quiet && result.FinalStatus is { CurrentValue: not null, EnabledForCurrentUser: false } status2 && !Has(args, "--force"))
            {
                var again = NativeMethods.MessageBox(IntPtr.Zero,
                    $"Another per-user shell is still configured:\n{status2.CurrentValue}\n\nRemove it too so Explorer is used?",
                    Title, NativeMethods.MB_YESNO | NativeMethods.MB_ICONWARNING);
                if (again == NativeMethods.IDYES) result = service.Restore(new RecoveryOptions(StartExplorer: true, Force: true));
            }

            WriteReport(report, result);
            if (!quiet)
            {
                NativeMethods.MessageBox(IntPtr.Zero, string.Join("\n", result.Steps) + (result.Success ? "\n\nExplorer is restored." : "\n\nRecovery could not be verified. Run Recover-Explorer.cmd from the Couchtop folder."),
                    Title, result.Success ? NativeMethods.MB_ICONINFORMATION : NativeMethods.MB_ICONWARNING);
            }
            return result.Success ? 0 : 3;
        }
        catch (Exception ex)
        {
            Log.Error("Recovery failed", ex);
            WriteReport(report, new { passed = false, error = ex.Message });
            if (!quiet) NativeMethods.MessageBox(IntPtr.Zero, "Recovery failed: " + ex.Message + "\n\nRun Recover-Explorer.cmd from the Couchtop folder.", Title, NativeMethods.MB_ICONWARNING);
            return 4;
        }
    }

    private static bool Has(string[] args, string flag) => args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));

    private static string? Value(string[] args, string flag)
    {
        var i = Array.FindIndex(args, a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static void WriteReport(string? path, object report)
    {
        if (path is null) return;
        try { File.WriteAllText(path, JsonSerializer.Serialize(report, JsonStore.Options)); }
        catch (Exception ex) { Log.Warn("Could not write report", ex); }
    }

    private sealed class NullExplorer : IExplorerController
    {
        public bool IsShellRunning() => true;
        public void StartExplorer() { }
    }
}
