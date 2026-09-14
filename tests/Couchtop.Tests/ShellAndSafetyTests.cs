using Couchtop.Core.Safety;
using Couchtop.Core.Settings;
using Couchtop.Core.Shell;
using Couchtop.Core.Storage;

namespace Couchtop.Tests;

public class ShellCommandTests
{
    private const string Guardian = @"C:\Users\me\AppData\Local\Programs\Couchtop\Couchtop.Guardian.exe";

    [Fact]
    public void Resilient_command_falls_back_to_explorer_using_only_windows_tools()
    {
        var cmd = ShellCommandBuilder.Build(Guardian, ShellBootstrapMode.Resilient, @"C:\Windows");
        Assert.StartsWith("\"C:\\Windows\\System32\\cmd.exe\" /d /c if exist", cmd);
        Assert.Contains($"(start \"\" \"{Guardian}\" --shell)", cmd);
        Assert.Contains("reg.exe\" delete \"HKCU\\Software\\Microsoft\\Windows NT\\CurrentVersion\\Winlogon\" /v Shell /f", cmd);
        Assert.Contains("C:\\Windows\\explorer.exe", cmd);
        Assert.True(ShellCommandBuilder.IsCouchtopCommand(cmd));
    }

    [Fact]
    public void Direct_command_and_unsafe_paths()
    {
        Assert.Equal($"\"{Guardian}\" --shell", ShellCommandBuilder.Build(Guardian, ShellBootstrapMode.Direct, @"C:\Windows"));
        var percent = @"C:\100%\Couchtop.Guardian.exe";
        Assert.Equal($"\"{percent}\" --shell", ShellCommandBuilder.Build(percent, ShellBootstrapMode.Resilient, @"C:\Windows"));
        Assert.Throws<ArgumentException>(() => ShellCommandBuilder.Build("C:\\a\"b\\Couchtop.Guardian.exe", ShellBootstrapMode.Direct, @"C:\Windows"));
        Assert.False(ShellCommandBuilder.IsCouchtopCommand("explorer.exe"));
        Assert.False(ShellCommandBuilder.IsCouchtopCommand(null));
    }
}

public class ShellMechanismTests
{
    private static InstallLayout Layout => new(@"C:\Users\me\AppData\Local\Programs\Couchtop");

    [Fact]
    public void Enable_disable_with_no_previous_shell_removes_value()
    {
        var store = new MemoryRegistryStore();
        var mech = new WinlogonUserShellMechanism(store);
        mech.Apply("\"x\\Couchtop.Guardian.exe\" --shell");
        Assert.True(mech.GetStatus().EnabledForCurrentUser);

        var result = mech.Revert(force: false);
        Assert.True(result.Success);
        Assert.Null(store.GetString(ShellRegistryPaths.WinlogonKey, "Shell"));
        Assert.False(mech.GetStatus().EnabledForCurrentUser);
    }

    [Fact]
    public void Previous_custom_shell_is_restored_and_reapply_keeps_original_previous()
    {
        var store = new MemoryRegistryStore();
        store.SetString(ShellRegistryPaths.WinlogonKey, "Shell", @"C:\Kiosk\kiosk.exe");
        var mech = new WinlogonUserShellMechanism(store);
        mech.Apply("\"a\\Couchtop.Guardian.exe\" --shell");
        mech.Apply("\"b\\Couchtop.Guardian.exe\" --shell"); // re-enable must not record our own value as "previous"
        mech.Revert(false);
        Assert.Equal(@"C:\Kiosk\kiosk.exe", store.GetString(ShellRegistryPaths.WinlogonKey, "Shell"));
    }

    [Fact]
    public void Foreign_shell_is_only_removed_when_forced()
    {
        var store = new MemoryRegistryStore();
        store.SetString(ShellRegistryPaths.WinlogonKey, "Shell", @"C:\Other\shell.exe");
        var mech = new WinlogonUserShellMechanism(store);
        Assert.False(mech.Revert(false).Success);
        Assert.NotNull(store.GetString(ShellRegistryPaths.WinlogonKey, "Shell"));
        Assert.True(mech.GetStatus().ForeignShellConfigured);
        Assert.True(mech.Revert(true).Success);
        Assert.Null(store.GetString(ShellRegistryPaths.WinlogonKey, "Shell"));
    }

    [Fact]
    public void Manager_refuses_to_enable_without_safety_dependencies()
    {
        var store = new MemoryRegistryStore();
        var manager = new ShellModeManager(store, Layout, windowsDirectory: @"C:\Windows");
        Assert.False(manager.Enable(ShellBootstrapMode.Resilient).Success);
        Assert.Null(store.GetString(ShellRegistryPaths.WinlogonKey, "Shell"));
    }

    [Fact]
    public void Manager_enable_is_gated_by_compatibility_and_safety_test()
    {
        var store = new MemoryRegistryStore();
        var edition = new WindowsEditionInfo("Core", "Windows 10 Home", 26200, 1, "25H2", "Client", false);
        var compat = new CompatibilityReport(new List<CompatibilityItem>(), edition);
        var gate = new SafetyGateResult(false, new[] { "not tested" });
        var manager = new ShellModeManager(store, Layout, () => compat, () => gate, @"C:\Windows");

        var blocked = manager.Enable(ShellBootstrapMode.Resilient);
        Assert.False(blocked.Success);
        Assert.Contains("not tested", blocked.Reasons);
        Assert.Null(store.GetString(ShellRegistryPaths.WinlogonKey, "Shell"));

        compat = new CompatibilityReport(new[] { new CompatibilityItem("policy", "Policy", CheckSeverity.Blocking, "forced") }, edition);
        gate = new SafetyGateResult(true, Array.Empty<string>());
        Assert.False(manager.Enable(ShellBootstrapMode.Resilient).Success);

        compat = new CompatibilityReport(new[] { new CompatibilityItem("remote", "Remote", CheckSeverity.Warning, "rdp") }, edition);
        var ok = manager.Enable(ShellBootstrapMode.Resilient);
        Assert.True(ok.Success, ok.Message);
        Assert.True(manager.GetStatus().EnabledForCurrentUser);

        store.Writable = true;
        Assert.True(manager.Disable().Success);
        Assert.False(manager.GetStatus().EnabledForCurrentUser);
    }

    [Fact]
    public void Enable_rolls_back_when_registry_write_fails()
    {
        var store = new MemoryRegistryStore { Writable = false };
        var edition = new WindowsEditionInfo("Professional", "Windows 10 Pro", 22631, 1, "23H2", "Client", false);
        var manager = new ShellModeManager(store, Layout, () => new CompatibilityReport(Array.Empty<CompatibilityItem>(), edition), () => new SafetyGateResult(true, Array.Empty<string>()), @"C:\Windows");
        var result = manager.Enable(ShellBootstrapMode.Direct);
        Assert.False(result.Success);
        Assert.Null(store.GetString(ShellRegistryPaths.WinlogonKey, "Shell"));
    }

    [Fact]
    public void Boot_guard_counts_and_resets()
    {
        var store = new MemoryRegistryStore();
        var guard = new BootGuard(store);
        Assert.Equal(1, guard.BeginBoot());
        Assert.Equal(2, guard.BeginBoot());
        guard.MarkHealthy();
        Assert.Equal(0, guard.PendingBoots);
        store.SetString(ShellRegistryPaths.StateKey, "PendingBoots", "garbage");
        Assert.Equal(0, guard.PendingBoots);
    }

    [Fact]
    public void Real_hkcu_sandbox_round_trip_never_touches_real_winlogon()
    {
        var realBefore = new CurrentUserRegistryStore().GetString(ShellRegistryPaths.WinlogonKey, "Shell");
        var sandbox = new CurrentUserRegistryStore($@"{ShellRegistryPaths.SandboxRoot}\unit-{Guid.NewGuid():N}");
        try
        {
            var manager = new ShellModeManager(sandbox, Layout, windowsDirectory: @"C:\Windows");
            manager.Mechanism.Apply(ShellCommandBuilder.Build(Layout.GuardianPath, ShellBootstrapMode.Resilient, @"C:\Windows"));
            Assert.True(manager.GetStatus().EnabledForCurrentUser);
            Assert.True(manager.Disable().Success);
            Assert.Null(sandbox.GetString(ShellRegistryPaths.WinlogonKey, "Shell"));
        }
        finally
        {
            sandbox.DeleteSandbox();
        }
        Assert.Equal(realBefore, new CurrentUserRegistryStore().GetString(ShellRegistryPaths.WinlogonKey, "Shell"));
        Assert.Throws<ArgumentException>(() => new CurrentUserRegistryStore(@"Software\Microsoft"));
    }
}

public class EditionAndCompatibilityTests
{
    [Theory]
    [InlineData("Core", "Client", false, EditionFamily.Home)]
    [InlineData("CoreSingleLanguage", "Client", false, EditionFamily.Home)]
    [InlineData("Professional", "Client", false, EditionFamily.Pro)]
    [InlineData("ProfessionalEducation", "Client", false, EditionFamily.Education)]
    [InlineData("Enterprise", "Client", false, EditionFamily.Enterprise)]
    [InlineData("EnterpriseS", "Client", false, EditionFamily.Enterprise)]
    [InlineData("IoTEnterprise", "Client", false, EditionFamily.IoT)]
    [InlineData("ServerStandard", "Server", false, EditionFamily.Server)]
    [InlineData("Core", "Client", true, EditionFamily.SMode)]
    [InlineData("CloudEdition", "Client", false, EditionFamily.SMode)]
    [InlineData("Mystery", "Client", false, EditionFamily.Unknown)]
    public void Edition_classification(string id, string type, bool sMode, EditionFamily expected)
    {
        Assert.Equal(expected, WindowsEditionInfo.Classify(id, type, sMode));
    }

    [Fact]
    public void Detects_current_machine_edition()
    {
        var info = WindowsEditionInfo.Detect();
        Assert.True(info.Build >= 10240);
        Assert.False(string.IsNullOrWhiteSpace(info.EditionId));
    }

    private sealed class FakeProbe : ISystemProbe
    {
        public WindowsEditionInfo Edition { get; set; } = new("Core", "Windows 10 Home", 26200, 1, "25H2", "Client", false);
        public bool IsSafeMode { get; set; }
        public bool IsRemoteSession { get; set; }
        public bool IsElevated { get; set; }
        public bool IsSystemAccount { get; set; }
        public string UserName => "PC\\me";
        public string WindowsDirectory => @"C:\Windows";
        public string? PolicyShell { get; set; }
        public string? MachineDefaultShell { get; set; } = "explorer.exe";
        public HashSet<string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
        public DriveType? Drive { get; set; } = DriveType.Fixed;
        public bool Writable { get; set; } = true;
        public bool FileExists(string path) => Files.Contains(path);
        public DriveType? GetDriveType(string path) => Drive;
        public bool CanWriteUserWinlogon() => Writable;
    }

    private static (FakeProbe, CompatibilityChecker) Setup()
    {
        var layout = new InstallLayout(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Couchtop"));
        var probe = new FakeProbe();
        foreach (var f in new[] { layout.AppPath, layout.GuardianPath, layout.RecoveryPath, layout.RecoveryScriptPath, @"C:\Windows\explorer.exe", @"C:\Windows\System32\cmd.exe", @"C:\Windows\System32\reg.exe" })
            probe.Files.Add(f);
        return (probe, new CompatibilityChecker(probe, layout));
    }

    [Fact]
    public void Healthy_home_pc_has_no_blocking_items()
    {
        var (_, checker) = Setup();
        var report = checker.Run();
        Assert.False(report.HasBlocking, string.Join("; ", report.Items.Where(i => i.Severity == CheckSeverity.Blocking).Select(i => i.Detail)));
    }

    [Fact]
    public void Portable_copy_blocks_shell_mode()
    {
        var (probe, checker) = Setup();
        probe.Files.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Couchtop", InstallLayout.PortableMarkerName));
        var report = checker.Run();
        Assert.Contains(report.Items, i => i.Id == "portable" && i.Severity == CheckSeverity.Blocking);
    }

    [Theory]
    [InlineData("policy")]
    [InlineData("safemode")]
    [InlineData("smode")]
    [InlineData("oldbuild")]
    [InlineData("network")]
    [InlineData("missing-recovery")]
    [InlineData("readonly")]
    [InlineData("system")]
    [InlineData("no-explorer")]
    public void Blocking_conditions(string condition)
    {
        var (probe, checker) = Setup();
        switch (condition)
        {
            case "policy": probe.PolicyShell = @"C:\kiosk.exe"; break;
            case "safemode": probe.IsSafeMode = true; break;
            case "smode": probe.Edition = probe.Edition with { SModeEnabled = true }; break;
            case "oldbuild": probe.Edition = probe.Edition with { Build = 14393 }; break;
            case "network": probe.Drive = DriveType.Network; break;
            case "missing-recovery": probe.Files.RemoveWhere(f => f.EndsWith("Recover-Explorer.cmd")); break;
            case "readonly": probe.Writable = false; break;
            case "system": probe.IsSystemAccount = true; break;
            case "no-explorer": probe.Files.Remove(@"C:\Windows\explorer.exe"); break;
        }
        Assert.True(checker.Run().HasBlocking);
    }

    [Fact]
    public void Remote_session_and_elevation_are_warnings_only()
    {
        var (probe, checker) = Setup();
        probe.IsRemoteSession = true;
        probe.IsElevated = true;
        var report = checker.Run();
        Assert.False(report.HasBlocking);
        Assert.Contains(report.Items, i => i.Id == "remote" && i.Severity == CheckSeverity.Warning);
    }
}

public class SafetyGateTests
{
    private static SafetyVerificationRecord PassingRecord(InstallLayout layout) => new()
    {
        CompletedAt = DateTimeOffset.Now,
        InstallDirectory = layout.Directory,
        AppSha256 = "A",
        GuardianSha256 = "G",
        RecoverySha256 = "R",
        Steps = SafetyStepIds.Required.Select(id => new SafetyStepResult { Id = id, Title = id, Passed = true }).ToList(),
    };

    private static string? Hash(string path) => Path.GetFileName(path) switch
    {
        "Couchtop.exe" => "A",
        "Couchtop.Guardian.exe" => "G",
        "Couchtop.Recovery.exe" => "R",
        _ => null,
    };

    [Fact]
    public void Passing_record_opens_gate()
    {
        var layout = new InstallLayout(@"C:\WD");
        Assert.True(SafetyGate.Evaluate(PassingRecord(layout), layout, DateTimeOffset.Now, Hash).Passed);
    }

    [Fact]
    public void Gate_closes_for_missing_failed_stale_moved_or_modified()
    {
        var layout = new InstallLayout(@"C:\WD");
        Assert.False(SafetyGate.Evaluate(null, layout, DateTimeOffset.Now, Hash).Passed);

        var failed = PassingRecord(layout);
        failed.Steps.Single(s => s.Id == SafetyStepIds.EmergencyHotkey).Passed = false;
        Assert.False(SafetyGate.Evaluate(failed, layout, DateTimeOffset.Now, Hash).Passed);

        var missingStep = PassingRecord(layout);
        missingStep.Steps.RemoveAll(s => s.Id == SafetyStepIds.CrashRecovery);
        Assert.False(SafetyGate.Evaluate(missingStep, layout, DateTimeOffset.Now, Hash).Passed);

        Assert.False(SafetyGate.Evaluate(PassingRecord(layout), layout, DateTimeOffset.Now.AddDays(20), Hash).Passed);
        Assert.False(SafetyGate.Evaluate(PassingRecord(layout), new InstallLayout(@"D:\Elsewhere"), DateTimeOffset.Now, Hash).Passed);
        Assert.False(SafetyGate.Evaluate(PassingRecord(layout), layout, DateTimeOffset.Now, p => p.EndsWith("Guardian.exe") ? "CHANGED" : Hash(p)).Passed);
    }

    [Fact]
    public void Corrupt_verification_file_counts_as_not_verified()
    {
        using var tmp = new TempDir();
        var file = Path.Combine(tmp.Path, "shell-safety.json");
        File.WriteAllText(file, "{ broken");
        Assert.Null(new SafetyVerificationStore(file).Load());
    }
}
