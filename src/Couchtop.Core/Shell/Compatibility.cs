using System.Security.Principal;
using Microsoft.Win32;
using Couchtop.Core.Native;
using Couchtop.Core.Storage;

namespace Couchtop.Core.Shell;

public enum EditionFamily
{
    Home,
    Pro,
    Education,
    Enterprise,
    IoT,
    Server,
    SMode,
    Unknown,
}

public sealed record WindowsEditionInfo(string EditionId, string ProductName, int Build, int Ubr, string DisplayVersion, string InstallationType, bool SModeEnabled)
{
    public EditionFamily Family => Classify(EditionId, InstallationType, SModeEnabled);

    public string FriendlyName
    {
        get
        {
            var name = Build >= 22000 ? ProductName.Replace("Windows 10", "Windows 11") : ProductName;
            return $"{name} {DisplayVersion} (build {Build}.{Ubr})".Trim();
        }
    }

    public static EditionFamily Classify(string editionId, string installationType, bool sMode)
    {
        var e = editionId ?? "";
        if (sMode || e.StartsWith("Cloud", StringComparison.OrdinalIgnoreCase)) return EditionFamily.SMode;
        if (string.Equals(installationType, "Server", StringComparison.OrdinalIgnoreCase) || string.Equals(installationType, "Server Core", StringComparison.OrdinalIgnoreCase) ||
            (e.Contains("Server", StringComparison.OrdinalIgnoreCase) && !e.Equals("ServerRdsh", StringComparison.OrdinalIgnoreCase))) return EditionFamily.Server;
        if (e.StartsWith("Core", StringComparison.OrdinalIgnoreCase)) return EditionFamily.Home;
        if (e.StartsWith("IoT", StringComparison.OrdinalIgnoreCase)) return EditionFamily.IoT;
        if (e.StartsWith("Enterprise", StringComparison.OrdinalIgnoreCase) || e.Equals("ServerRdsh", StringComparison.OrdinalIgnoreCase)) return EditionFamily.Enterprise;
        if (e.StartsWith("Education", StringComparison.OrdinalIgnoreCase) || e.Equals("ProfessionalEducation", StringComparison.OrdinalIgnoreCase)) return EditionFamily.Education;
        if (e.StartsWith("Professional", StringComparison.OrdinalIgnoreCase)) return EditionFamily.Pro;
        return EditionFamily.Unknown;
    }

    public static WindowsEditionInfo Detect()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        string S(string name) => key?.GetValue(name)?.ToString() ?? "";
        int I(string name) => int.TryParse(key?.GetValue(name)?.ToString(), out var n) ? n : 0;
        var sMode = false;
        try
        {
            using var ci = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\CI\Policy");
            sMode = ci?.GetValue("SkuPolicyRequired") is int v && v == 1;
        }
        catch
        {
        }
        return new WindowsEditionInfo(S("EditionID"), S("ProductName"), I("CurrentBuildNumber") > 0 ? I("CurrentBuildNumber") : I("CurrentBuild"), I("UBR"), S("DisplayVersion"), S("InstallationType"), sMode);
    }
}

public enum CheckSeverity
{
    Pass,
    Info,
    Warning,
    Blocking,
}

public sealed record CompatibilityItem(string Id, string Title, CheckSeverity Severity, string Detail);

public sealed record CompatibilityReport(IReadOnlyList<CompatibilityItem> Items, WindowsEditionInfo Edition)
{
    public bool HasBlocking => Items.Any(i => i.Severity == CheckSeverity.Blocking);
}

public interface ISystemProbe
{
    WindowsEditionInfo Edition { get; }
    bool IsSafeMode { get; }
    bool IsRemoteSession { get; }
    bool IsElevated { get; }
    bool IsSystemAccount { get; }
    string UserName { get; }
    string WindowsDirectory { get; }
    string? PolicyShell { get; }
    string? MachineDefaultShell { get; }
    bool FileExists(string path);
    DriveType? GetDriveType(string path);
    bool CanWriteUserWinlogon();
}

public sealed class WindowsSystemProbe : ISystemProbe
{
    private readonly IRegistryStore _hkcu;

    public WindowsSystemProbe(IRegistryStore hkcu)
    {
        _hkcu = hkcu;
        Edition = WindowsEditionInfo.Detect();
    }

    public WindowsEditionInfo Edition { get; }
    public bool IsSafeMode => NativeMethods.GetSystemMetrics(NativeMethods.SM_CLEANBOOT) != 0;
    public bool IsRemoteSession => NativeMethods.GetSystemMetrics(NativeMethods.SM_REMOTESESSION) != 0;

    public bool IsElevated
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator) && identity.Owner is not null && identity.User != identity.Owner;
        }
    }

    public bool IsSystemAccount
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return identity.IsSystem;
        }
    }

    public string UserName => Environment.UserDomainName + "\\" + Environment.UserName;
    public string WindowsDirectory => Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    public string? PolicyShell
    {
        get
        {
            var user = _hkcu.GetString(ShellRegistryPaths.PolicySystemKey, "Shell");
            if (!string.IsNullOrWhiteSpace(user)) return user;
            using var key = Registry.LocalMachine.OpenSubKey(ShellRegistryPaths.PolicySystemKey);
            var machine = key?.GetValue("Shell")?.ToString();
            return string.IsNullOrWhiteSpace(machine) ? null : machine;
        }
    }

    public string? MachineDefaultShell
    {
        get
        {
            using var key = Registry.LocalMachine.OpenSubKey(ShellRegistryPaths.WinlogonKey);
            return key?.GetValue("Shell")?.ToString();
        }
    }

    public bool FileExists(string path) => File.Exists(path);

    public DriveType? GetDriveType(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(root)) return null;
            if (root.StartsWith(@"\\", StringComparison.Ordinal)) return DriveType.Network;
            return new DriveInfo(root).DriveType;
        }
        catch
        {
            return null;
        }
    }

    public bool CanWriteUserWinlogon() => _hkcu.CanWrite(ShellRegistryPaths.WinlogonKey);
}

public sealed class CompatibilityChecker
{
    private readonly ISystemProbe _probe;
    private readonly InstallLayout _layout;

    public CompatibilityChecker(ISystemProbe probe, InstallLayout layout)
    {
        _probe = probe;
        _layout = layout;
    }

    public CompatibilityReport Run()
    {
        var items = new List<CompatibilityItem>();
        void Add(string id, string title, CheckSeverity severity, string detail) => items.Add(new CompatibilityItem(id, title, severity, detail));
        var edition = _probe.Edition;

        Add("os-build", "Windows version",
            edition.Build >= 17763 ? CheckSeverity.Pass : CheckSeverity.Blocking,
            edition.Build >= 17763 ? edition.FriendlyName : $"Build {edition.Build} is too old (Windows 10 1809 or newer is required).");

        switch (edition.Family)
        {
            case EditionFamily.SMode:
                Add("edition", "Windows edition", CheckSeverity.Blocking, "Windows in S mode cannot run a custom shell.");
                break;
            case EditionFamily.Server:
                Add("edition", "Windows edition", CheckSeverity.Warning, "Windows Server is not a tested target. The per-user Winlogon shell is used.");
                break;
            case EditionFamily.Enterprise or EditionFamily.Education or EditionFamily.IoT:
                Add("edition", "Windows edition", CheckSeverity.Pass,
                    $"{edition.Family}: using the per-user Winlogon shell. (Shell Launcher v2 is also available on this edition, but Couchtop uses the per-user mechanism so Explorer fallback stays guaranteed and no admin rights are needed.)");
                break;
            case EditionFamily.Unknown:
                Add("edition", "Windows edition", CheckSeverity.Warning, $"Unrecognized edition '{edition.EditionId}'. The per-user Winlogon shell will be used.");
                break;
            default:
                Add("edition", "Windows edition", CheckSeverity.Pass, $"{edition.Family}: using the per-user Winlogon shell (affects only {_probe.UserName}).");
                break;
        }

        Add("safe-mode", "Normal boot", _probe.IsSafeMode ? CheckSeverity.Blocking : CheckSeverity.Pass,
            _probe.IsSafeMode ? "Windows is running in Safe Mode." : "Windows is not in Safe Mode.");

        var policy = _probe.PolicyShell;
        Add("policy", "Group Policy shell override", policy is null ? CheckSeverity.Pass : CheckSeverity.Blocking,
            policy is null ? "No 'Custom User Interface' policy is set." : $"A policy forces the shell to '{policy}', which would override Couchtop.");

        var machineShell = _probe.MachineDefaultShell;
        var machineIsExplorer = machineShell is null || machineShell.Trim().Trim('"').EndsWith("explorer.exe", StringComparison.OrdinalIgnoreCase);
        Add("machine-shell", "System default shell", machineIsExplorer ? CheckSeverity.Pass : CheckSeverity.Warning,
            machineIsExplorer ? "The machine-wide default shell is Explorer (Couchtop never changes it)." : $"The machine-wide shell is '{machineShell}'. Couchtop will not change it.");

        Add("account", "User account", _probe.IsSystemAccount ? CheckSeverity.Blocking : _probe.IsElevated ? CheckSeverity.Warning : CheckSeverity.Pass,
            _probe.IsSystemAccount ? "Running as SYSTEM. Run Couchtop as a normal signed-in user."
            : _probe.IsElevated ? "Couchtop is running elevated. Run it as your normal user so the right account is configured."
            : $"Shell mode will only affect {_probe.UserName}.");

        foreach (var (id, title, path) in new[]
                 {
                     ("guardian", "Shell guardian", _layout.GuardianPath),
                     ("app", "Couchtop app", _layout.AppPath),
                     ("recovery-exe", "Recovery tool", _layout.RecoveryPath),
                     ("recovery-cmd", "Recovery script", _layout.RecoveryScriptPath),
                 })
        {
            var exists = _probe.FileExists(path);
            Add(id, title, exists ? CheckSeverity.Pass : CheckSeverity.Blocking, exists ? path : $"Missing: {path}");
        }

        var drive = _probe.GetDriveType(_layout.GuardianPath);
        Add("drive", "Install drive", drive is DriveType.Fixed ? CheckSeverity.Pass : CheckSeverity.Blocking,
            drive is DriveType.Fixed ? "Installed on a local fixed drive." : $"Couchtop is on a {drive?.ToString() ?? "unknown"} drive that may be unavailable at sign-in.");

        var installed = _layout.IsInStandardInstallLocation();
        Add("location", "Install location", installed ? CheckSeverity.Pass : CheckSeverity.Warning,
            installed ? _layout.Directory : $"Running from '{_layout.Directory}'. Install Couchtop with Setup so the shell path stays stable.");

        var resilient = ShellCommandBuilder.CanUseResilient(_layout.GuardianPath);
        Add("path", "Install path characters", resilient ? CheckSeverity.Pass : CheckSeverity.Warning,
            resilient ? "Path is safe for the resilient bootstrap." : "The path contains characters that prevent the resilient bootstrap; the direct bootstrap will be used.");

        var explorer = Path.Combine(_probe.WindowsDirectory, "explorer.exe");
        Add("explorer", "Explorer available", _probe.FileExists(explorer) ? CheckSeverity.Pass : CheckSeverity.Blocking,
            _probe.FileExists(explorer) ? explorer : "explorer.exe was not found, so fallback would be impossible.");

        var cmdOk = _probe.FileExists(Path.Combine(_probe.WindowsDirectory, "System32", "cmd.exe")) && _probe.FileExists(Path.Combine(_probe.WindowsDirectory, "System32", "reg.exe"));
        Add("bootstrap-tools", "Resilient bootstrap tools", cmdOk ? CheckSeverity.Pass : CheckSeverity.Warning,
            cmdOk ? "cmd.exe and reg.exe are available." : "cmd.exe or reg.exe is missing; only the direct bootstrap is possible.");

        Add("registry", "Per-user shell setting writable", _probe.CanWriteUserWinlogon() ? CheckSeverity.Pass : CheckSeverity.Blocking,
            _probe.CanWriteUserWinlogon() ? "HKCU Winlogon key can be changed and restored." : "The per-user Winlogon key is not writable.");

        if (_probe.IsRemoteSession)
            Add("remote", "Remote session", CheckSeverity.Warning, "This is a Remote Desktop session. Test shell mode on the physical console or a VM first.");

        return new CompatibilityReport(items, edition);
    }
}
