using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using Couchtop.Core.Diagnostics;
using Couchtop.Core.Native;
using Couchtop.Core.Safety;
using Couchtop.Core.Settings;
using Couchtop.Core.Shell;
using Couchtop.Core.Storage;

namespace Couchtop.Setup;

public sealed record InstallOptions(bool DesktopShortcut = true, bool StartAtSignIn = false, bool LaunchWhenDone = true);

public sealed record SetupResult(bool Success, string Message);

/// <summary>
/// Per-user installer (no admin rights): copies files to %LOCALAPPDATA%\Programs\Couchtop, creates Start menu
/// shortcuts and an "Apps &amp; features" entry. Uninstall always restores Explorer first and refuses to delete
/// anything if the shell setting cannot be verified as restored.
/// </summary>
public sealed class Installer
{
    public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Couchtop";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private readonly Action<string> _progress;

    public Installer(Action<string>? progress = null)
    {
        _progress = progress ?? (_ => { });
    }

    public static string StartMenuFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Couchtop");
    public static string DesktopShortcut => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Couchtop.lnk");

    public static string? InstalledDirectory()
    {
        using var key = Registry.CurrentUser.OpenSubKey(UninstallKey);
        var location = key?.GetValue("InstallLocation") as string;
        return !string.IsNullOrWhiteSpace(location) && File.Exists(Path.Combine(location, InstallLayout.AppExeName)) ? location : null;
    }

    private void Report(string message)
    {
        Log.Info("[setup] " + message);
        _progress(message);
    }

    public SetupResult Install(string sourceDirectory, string targetDirectory, InstallOptions options)
    {
        sourceDirectory = Path.GetFullPath(sourceDirectory);
        targetDirectory = Path.GetFullPath(targetDirectory);
        if (!File.Exists(Path.Combine(sourceDirectory, InstallLayout.AppExeName)) || !File.Exists(Path.Combine(sourceDirectory, InstallLayout.GuardianExeName)))
            return new SetupResult(false, "The installation files are incomplete. Extract the whole Couchtop folder and run Setup again.");
        if (string.Equals(sourceDirectory.TrimEnd('\\'), targetDirectory.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            return new SetupResult(false, "Couchtop is already installed in this folder.");

        var registry = new CurrentUserRegistryStore();
        var shell = new ShellModeManager(registry, new InstallLayout(targetDirectory));
        var status = shell.GetStatus();
        var runningAsShell = status.EnabledForCurrentUser && !NativeMethods.IsExplorerShellRunning() && Process.GetProcessesByName("Couchtop.Guardian").Length > 0;
        var appWasRunning = Process.GetProcessesByName("Couchtop").Length > 0;

        if (status.EnabledForCurrentUser && status.CurrentValue is { } command && !command.Contains(targetDirectory, StringComparison.OrdinalIgnoreCase))
        {
            Report("Shell mode points to another folder; turning it off for safety.");
            shell.Disable();
        }

        Report("Closing Couchtop…");
        RecoveryService.StopCouchtopProcesses();

        var staging = targetDirectory + ".new";
        var backup = targetDirectory + ".old";
        try
        {
            Report("Copying files…");
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            CopyDirectory(sourceDirectory, staging);

            if (Directory.Exists(backup)) Directory.Delete(backup, true);
            if (Directory.Exists(targetDirectory)) MoveWithRetry(targetDirectory, backup);
            MoveWithRetry(staging, targetDirectory);
            TryDelete(backup);
        }
        catch (Exception ex)
        {
            Log.Error("Install copy failed", ex);
            if (!Directory.Exists(targetDirectory) && Directory.Exists(backup))
            {
                try { Directory.Move(backup, targetDirectory); } catch (Exception rollbackEx) { Log.Error("Rollback failed", rollbackEx); }
            }
            if (status.EnabledForCurrentUser && !File.Exists(Path.Combine(targetDirectory, InstallLayout.GuardianExeName)))
            {
                shell.Disable();
                if (!NativeMethods.IsExplorerShellRunning()) new ExplorerController().StartExplorer();
            }
            return new SetupResult(false, "Files could not be copied: " + ex.Message);
        }

        var layout = new InstallLayout(targetDirectory);
        Report("Creating shortcuts…");
        Directory.CreateDirectory(StartMenuFolder);
        ShellLink.Create(Path.Combine(StartMenuFolder, "Couchtop.lnk"), layout.AppPath, description: "console-style desktop for Windows");
        ShellLink.Create(Path.Combine(StartMenuFolder, "Couchtop Recovery.lnk"), layout.RecoveryPath, description: "Restore Windows Explorer as your desktop");
        ShellLink.Create(Path.Combine(StartMenuFolder, "Uninstall Couchtop.lnk"), layout.SetupPath, "--uninstall", description: "Remove Couchtop");
        if (options.DesktopShortcut) ShellLink.Create(DesktopShortcut, layout.AppPath, description: "Couchtop");

        Report("Registering Couchtop…");
        WriteUninstallEntry(layout, sourceDirectory);
        using (var run = Registry.CurrentUser.CreateSubKey(RunKey, true))
        {
            if (options.StartAtSignIn) run.SetValue("Couchtop", $"\"{layout.AppPath}\" --autostart");
            else if (run.GetValue("Couchtop") is string existing && existing.Contains("Couchtop.exe", StringComparison.OrdinalIgnoreCase)) run.DeleteValue("Couchtop", false);
        }

        if (runningAsShell)
        {
            Report("Restarting the Couchtop shell…");
            using var _ = Process.Start(new ProcessStartInfo(layout.GuardianPath, "--shell") { UseShellExecute = false, WorkingDirectory = layout.Directory });
        }
        else if (options.LaunchWhenDone || appWasRunning)
        {
            using var _ = Process.Start(new ProcessStartInfo(layout.AppPath) { UseShellExecute = false, WorkingDirectory = layout.Directory });
        }

        Report("Done.");
        return new SetupResult(true, $"Couchtop was installed to {targetDirectory}.");
    }

    private static void WriteUninstallEntry(InstallLayout layout, string sourceDirectory)
    {
        using var key = Registry.CurrentUser.CreateSubKey(UninstallKey, true);
        var version = FileVersionInfo.GetVersionInfo(layout.AppPath).ProductVersion ?? "1.0.0";
        key.SetValue("DisplayName", "Couchtop");
        key.SetValue("DisplayVersion", version.Split('+')[0]);
        key.SetValue("Publisher", "Couchtop contributors");
        key.SetValue("InstallLocation", layout.Directory);
        key.SetValue("DisplayIcon", layout.AppPath);
        key.SetValue("UninstallString", $"\"{layout.SetupPath}\" --uninstall");
        key.SetValue("QuietUninstallString", $"\"{layout.SetupPath}\" --uninstall --quiet");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
        try
        {
            var bytes = Directory.EnumerateFiles(layout.Directory, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
            key.SetValue("EstimatedSize", (int)(bytes / 1024), RegistryValueKind.DWord);
        }
        catch (IOException)
        {
        }
        _ = sourceDirectory;
    }

    public SetupResult Uninstall(string installDirectory, bool purgeUserData)
    {
        installDirectory = Path.GetFullPath(installDirectory);
        var layout = new InstallLayout(installDirectory);

        // 1. Restore Explorer FIRST. Nothing is deleted unless this is verified.
        Report("Restoring Windows Explorer…");
        var registry = new CurrentUserRegistryStore();
        var shell = new ShellModeManager(registry, layout);
        var recovery = new RecoveryService(shell, new ExplorerController(), RecoveryService.StopCouchtopProcesses);
        var report = recovery.Restore(new RecoveryOptions(StartExplorer: true, Force: false, StopCouchtop: true));
        foreach (var step in report.Steps) Log.Info("[uninstall] " + step);
        var after = shell.GetStatus();
        if (after.EnabledForCurrentUser || ShellCommandBuilder.IsCouchtopCommand(after.CurrentValue))
        {
            return new SetupResult(false,
                "Couchtop could not verify that Windows Explorer was restored, so nothing was removed.\n" +
                "Run Recover-Explorer.cmd from the Couchtop folder, then uninstall again.");
        }

        Report("Removing startup entry and shortcuts…");
        using (var run = Registry.CurrentUser.OpenSubKey(RunKey, true))
        {
            if (run?.GetValue("Couchtop") is string value && value.Contains("Couchtop", StringComparison.OrdinalIgnoreCase)) run.DeleteValue("Couchtop", false);
        }
        TryDelete(StartMenuFolder);
        if (File.Exists(DesktopShortcut))
        {
            var target = ShellLink.Read(DesktopShortcut)?.TargetPath;
            if (target is null || target.StartsWith(installDirectory, StringComparison.OrdinalIgnoreCase)) TryDeleteFile(DesktopShortcut);
        }

        Report("Removing registration…");
        Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Couchtop", false);
        }
        catch (Exception ex)
        {
            Log.Warn("Could not remove Couchtop registry state", ex);
        }

        if (purgeUserData)
        {
            Report("Removing channels and settings…");
            Log.Info("Purging user data");
            TryDelete(AppPaths.ForCurrentUser().DataRoot);
        }

        Report("Removing program files…");
        var running = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd('\\');
        if (running.StartsWith(installDirectory.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
        {
            ScheduleDelete(installDirectory);
        }
        else
        {
            TryDelete(installDirectory);
        }

        Report("Done.");
        return new SetupResult(true, "Couchtop was removed and Windows Explorer is your desktop again.");
    }

    /// <summary>Deletes the install folder after this process exits (Setup runs from inside it during uninstall).</summary>
    private static void ScheduleDelete(string directory)
    {
        var cmd = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        var arguments = $"/d /c ping 127.0.0.1 -n 4 >nul & rmdir /s /q \"{directory}\"";
        using var _ = Process.Start(new ProcessStartInfo(cmd, arguments) { CreateNoWindow = true, UseShellExecute = false, WorkingDirectory = Path.GetTempPath() });
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, dir)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)), true);
    }

    private static void MoveWithRetry(string from, string to)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                Directory.Move(from, to);
                return;
            }
            catch (IOException) when (attempt < 10)
            {
                Thread.Sleep(500);
            }
        }
    }

    private static void TryDelete(string directory)
    {
        for (var attempt = 0; attempt < 5 && Directory.Exists(directory); attempt++)
        {
            try
            {
                Directory.Delete(directory, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(400);
            }
        }
    }

    private static void TryDeleteFile(string file)
    {
        try { File.Delete(file); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Warn("Could not delete " + file, ex); }
    }
}
