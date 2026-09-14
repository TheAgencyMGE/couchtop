using System.Globalization;
using Couchtop.Core.Diagnostics;
using Couchtop.Core.Safety;
using Couchtop.Core.Settings;
using Couchtop.Core.Storage;

namespace Couchtop.Core.Shell;

public sealed record ShellStatus(
    bool EnabledForCurrentUser,
    string? CurrentValue,
    bool ForeignShellConfigured,
    string? PreviousValue,
    DateTimeOffset? EnabledAt,
    string MechanismName);

public sealed record ShellChangeResult(bool Success, string Message, IReadOnlyList<string> Reasons)
{
    public static ShellChangeResult Ok(string message) => new(true, message, Array.Empty<string>());
    public static ShellChangeResult Fail(string message, IEnumerable<string>? reasons = null) => new(false, message, reasons?.ToList() ?? new List<string>());
}

public static class ShellCommandBuilder
{
    public const string Marker = "Couchtop.Guardian.exe";

    public static bool CanUseResilient(string guardianPath) =>
        !guardianPath.Contains('%') && !guardianPath.Contains('"') && !guardianPath.Contains('\n') && !guardianPath.Contains('\r');

    /// <summary>
    /// Builds the per-user Winlogon shell command. The resilient form only uses components that ship with Windows:
    /// if the Guardian executable is missing (e.g. the folder was deleted), it removes the per-user override and starts Explorer,
    /// so a deleted install can never produce a black screen.
    /// </summary>
    public static string Build(string guardianPath, ShellBootstrapMode mode, string windowsDirectory)
    {
        if (guardianPath.Contains('"')) throw new ArgumentException("Path cannot contain quotes.", nameof(guardianPath));
        var direct = $"\"{guardianPath}\" --shell";
        if (mode == ShellBootstrapMode.Direct || !CanUseResilient(guardianPath)) return direct;

        var sys = Path.Combine(windowsDirectory, "System32");
        return $"\"{sys}\\cmd.exe\" /d /c if exist \"{guardianPath}\" (start \"\" \"{guardianPath}\" --shell) " +
               $"else (\"{sys}\\reg.exe\" delete \"HKCU\\{ShellRegistryPaths.WinlogonKey}\" /v {ShellRegistryPaths.ShellValue} /f & start \"\" \"{Path.Combine(windowsDirectory, "explorer.exe")}\")";
    }

    public static bool IsCouchtopCommand(string? value) =>
        value is not null && value.Contains(Marker, StringComparison.OrdinalIgnoreCase);
}

public interface IShellMechanism
{
    string Id { get; }
    string DisplayName { get; }
    ShellStatus GetStatus();
    void Apply(string commandLine);
    ShellChangeResult Revert(bool force);
}

/// <summary>
/// Per-user shell replacement via HKCU\Software\Microsoft\Windows NT\CurrentVersion\Winlogon\Shell.
/// Only affects the current Windows user; the machine-wide HKLM value is never touched.
/// </summary>
public sealed class WinlogonUserShellMechanism : IShellMechanism
{
    private readonly IRegistryStore _store;

    public WinlogonUserShellMechanism(IRegistryStore store)
    {
        _store = store;
    }

    public string Id => "winlogon-user";
    public string DisplayName => "Per-user Winlogon shell (HKCU)";

    public ShellStatus GetStatus()
    {
        var current = _store.GetString(ShellRegistryPaths.WinlogonKey, ShellRegistryPaths.ShellValue);
        var ours = ShellCommandBuilder.IsCouchtopCommand(current);
        var enabledAt = DateTimeOffset.TryParse(_store.GetString(ShellRegistryPaths.StateKey, "EnabledAt"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var at) ? at : (DateTimeOffset?)null;
        return new ShellStatus(
            ours,
            current,
            current is not null && !ours && !IsExplorer(current),
            _store.GetString(ShellRegistryPaths.StateKey, "PreviousShell"),
            ours ? enabledAt : null,
            DisplayName);
    }

    public void Apply(string commandLine)
    {
        var current = _store.GetString(ShellRegistryPaths.WinlogonKey, ShellRegistryPaths.ShellValue);
        if (!ShellCommandBuilder.IsCouchtopCommand(current))
        {
            _store.SetString(ShellRegistryPaths.StateKey, "PreviousShellExisted", current is null ? "False" : "True");
            _store.SetString(ShellRegistryPaths.StateKey, "PreviousShell", current ?? "");
        }
        _store.SetString(ShellRegistryPaths.WinlogonKey, ShellRegistryPaths.ShellValue, commandLine);
        _store.SetString(ShellRegistryPaths.StateKey, "Enabled", "True");
        _store.SetString(ShellRegistryPaths.StateKey, "EnabledAt", DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture));
        _store.SetString(ShellRegistryPaths.StateKey, "CommandLine", commandLine);
    }

    public ShellChangeResult Revert(bool force)
    {
        var current = _store.GetString(ShellRegistryPaths.WinlogonKey, ShellRegistryPaths.ShellValue);
        string message;
        if (current is null)
        {
            message = "Windows Explorer is already the shell for this user.";
        }
        else if (ShellCommandBuilder.IsCouchtopCommand(current))
        {
            var previousExisted = string.Equals(_store.GetString(ShellRegistryPaths.StateKey, "PreviousShellExisted"), "True", StringComparison.OrdinalIgnoreCase);
            var previous = _store.GetString(ShellRegistryPaths.StateKey, "PreviousShell");
            if (!force && previousExisted && !string.IsNullOrWhiteSpace(previous) && !ShellCommandBuilder.IsCouchtopCommand(previous))
            {
                _store.SetString(ShellRegistryPaths.WinlogonKey, ShellRegistryPaths.ShellValue, previous);
                message = "Restored the per-user shell that was configured before Couchtop.";
            }
            else
            {
                _store.DeleteValue(ShellRegistryPaths.WinlogonKey, ShellRegistryPaths.ShellValue);
                message = "Removed the Couchtop shell setting. Explorer will start at the next sign-in.";
            }
        }
        else if (force)
        {
            _store.DeleteValue(ShellRegistryPaths.WinlogonKey, ShellRegistryPaths.ShellValue);
            message = $"Removed a per-user shell override ('{current}'). Explorer will start at the next sign-in.";
        }
        else
        {
            MarkDisabled();
            return ShellChangeResult.Fail($"A different per-user shell is configured ('{current}'). It was left unchanged; use forced recovery to remove it.");
        }

        MarkDisabled();
        var after = _store.GetString(ShellRegistryPaths.WinlogonKey, ShellRegistryPaths.ShellValue);
        if (ShellCommandBuilder.IsCouchtopCommand(after) || (force && after is not null))
            return ShellChangeResult.Fail("The shell setting could not be verified after the change.");
        return ShellChangeResult.Ok(message);
    }

    private void MarkDisabled()
    {
        _store.SetString(ShellRegistryPaths.StateKey, "Enabled", "False");
        _store.SetString(ShellRegistryPaths.StateKey, "DisabledAt", DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture));
    }

    private static bool IsExplorer(string value)
    {
        var v = value.Trim().Trim('"');
        return v.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase) || v.EndsWith("\\explorer.exe", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Counts sign-ins that never reached a healthy Couchtop UI, to break boot loops.</summary>
public sealed class BootGuard
{
    private readonly IRegistryStore _store;

    public BootGuard(IRegistryStore store)
    {
        _store = store;
    }

    public int PendingBoots =>
        int.TryParse(_store.GetString(ShellRegistryPaths.StateKey, "PendingBoots"), out var n) && n >= 0 ? n : 0;

    public int BeginBoot()
    {
        var n = PendingBoots + 1;
        _store.SetString(ShellRegistryPaths.StateKey, "PendingBoots", n.ToString(CultureInfo.InvariantCulture));
        _store.SetString(ShellRegistryPaths.StateKey, "LastBootAt", DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture));
        return n;
    }

    public void MarkHealthy()
    {
        _store.SetString(ShellRegistryPaths.StateKey, "PendingBoots", "0");
        _store.SetString(ShellRegistryPaths.StateKey, "LastHealthyAt", DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture));
    }

    public void Reset()
    {
        try
        {
            _store.DeleteValue(ShellRegistryPaths.StateKey, "PendingBoots");
        }
        catch (Exception ex)
        {
            Log.Warn("Could not reset boot counter", ex);
        }
    }
}

public sealed class ShellModeManager
{
    private readonly IShellMechanism _mechanism;
    private readonly BootGuard _bootGuard;
    private readonly Func<CompatibilityReport>? _compatibility;
    private readonly Func<SafetyGateResult>? _safetyGate;
    private readonly InstallLayout _layout;
    private readonly string _windowsDirectory;

    /// <summary>
    /// Enabling requires both a compatibility check and a safety gate. Tools that only need to restore Explorer
    /// (Recovery, Setup, emergency handler) pass null for both and can therefore never enable shell mode.
    /// </summary>
    public ShellModeManager(IRegistryStore store, InstallLayout layout, Func<CompatibilityReport>? compatibility = null, Func<SafetyGateResult>? safetyGate = null, string? windowsDirectory = null)
    {
        _mechanism = new WinlogonUserShellMechanism(store);
        _bootGuard = new BootGuard(store);
        _compatibility = compatibility;
        _safetyGate = safetyGate;
        _layout = layout;
        _windowsDirectory = windowsDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    }

    public IShellMechanism Mechanism => _mechanism;
    public BootGuard BootGuard => _bootGuard;

    public ShellStatus GetStatus() => _mechanism.GetStatus();

    public ShellChangeResult Enable(ShellBootstrapMode mode)
    {
        if (_compatibility is null || _safetyGate is null)
            return ShellChangeResult.Fail("Shell mode can only be enabled from Couchtop Settings after the Shell Safety Test.");

        var compat = _compatibility();
        var blocking = compat.Items.Where(i => i.Severity == CheckSeverity.Blocking).Select(i => $"{i.Title}: {i.Detail}").ToList();
        if (blocking.Count > 0) return ShellChangeResult.Fail("This PC is not ready for shell mode.", blocking);

        var gate = _safetyGate();
        if (!gate.Passed) return ShellChangeResult.Fail("The Shell Safety Test has not been passed for this installation.", gate.Reasons);

        try
        {
            var command = ShellCommandBuilder.Build(_layout.GuardianPath, mode, _windowsDirectory);
            _bootGuard.Reset();
            _mechanism.Apply(command);
            var status = _mechanism.GetStatus();
            if (!status.EnabledForCurrentUser || status.CurrentValue != command)
            {
                _mechanism.Revert(false);
                return ShellChangeResult.Fail("The shell setting could not be verified, so it was rolled back.");
            }
            Log.Info($"Shell mode enabled: {command}");
            return ShellChangeResult.Ok("Couchtop will start instead of Explorer the next time you sign in.");
        }
        catch (Exception ex)
        {
            Log.Error("Enabling shell mode failed", ex);
            try { _mechanism.Revert(false); } catch (Exception revertEx) { Log.Error("Rollback failed", revertEx); }
            return ShellChangeResult.Fail("Shell mode could not be enabled: " + ex.Message);
        }
    }

    /// <summary>Never gated: turning shell mode off must always be possible.</summary>
    public ShellChangeResult Disable()
    {
        try
        {
            var result = _mechanism.Revert(force: false);
            _bootGuard.Reset();
            Log.Info("Shell mode disable: " + result.Message);
            return result;
        }
        catch (Exception ex)
        {
            Log.Error("Disabling shell mode failed", ex);
            return ShellChangeResult.Fail("Could not change the shell setting: " + ex.Message);
        }
    }

    public ShellChangeResult ForceRestore()
    {
        try
        {
            var result = _mechanism.Revert(force: true);
            _bootGuard.Reset();
            return result;
        }
        catch (Exception ex)
        {
            Log.Error("Forced restore failed", ex);
            return ShellChangeResult.Fail("Could not change the shell setting: " + ex.Message);
        }
    }
}
