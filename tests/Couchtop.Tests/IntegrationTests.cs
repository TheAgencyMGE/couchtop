using System.Diagnostics;
using System.Text.Json;
using Couchtop.Core.Settings;
using Couchtop.Core.Shell;
using Couchtop.Core.Storage;

namespace Couchtop.Tests;

/// <summary>Runs the real Guardian and Recovery executables. Everything stays inside HKCU registry sandboxes.</summary>
[Collection("processes")]
public class IntegrationTests
{
    private static (int ExitCode, JsonElement Report) Run(string exe, string args, TimeSpan timeout)
    {
        Assert.True(File.Exists(exe), "Executable not built: " + exe);
        var report = Path.Combine(Path.GetTempPath(), "wd-report-" + Guid.NewGuid().ToString("N") + ".json");
        using var p = Process.Start(new ProcessStartInfo(exe, args.Replace("{report}", report)) { UseShellExecute = false })!;
        if (!p.WaitForExit((int)timeout.TotalMilliseconds))
        {
            p.Kill(true);
            Assert.Fail($"{Path.GetFileName(exe)} {args} timed out");
        }
        var json = File.Exists(report) ? File.ReadAllText(report) : "{}";
        try { File.Delete(report); } catch { }
        return (p.ExitCode, JsonDocument.Parse(json).RootElement.Clone());
    }

    private static string RealShellValue() => new CurrentUserRegistryStore().GetString(ShellRegistryPaths.WinlogonKey, "Shell") ?? "<none>";

    [Theory]
    [InlineData("crash")]
    [InlineData("hang")]
    [InlineData("bootloop")]
    [InlineData("ok")]
    public void Guardian_rehearsal_scenarios_pass(string scenario)
    {
        var before = RealShellValue();
        var (code, report) = Run(RepoPaths.Guardian, $"--rehearsal --scenario {scenario} --app \"{RepoPaths.FakeApp}\" --result \"{{report}}\"", TimeSpan.FromSeconds(120));
        Assert.True(code == 0, report.ToString());
        Assert.True(report.GetProperty("passed").GetBoolean());
        Assert.Equal(before, RealShellValue());
    }

    [Fact]
    public void Recovery_verify_passes()
    {
        var before = RealShellValue();
        var (code, report) = Run(RepoPaths.Recovery, "--verify --quiet --report \"{report}\"", TimeSpan.FromSeconds(60));
        Assert.True(code == 0, report.ToString());
        Assert.Equal(before, RealShellValue());
    }

    [Fact]
    public void Recovery_restore_in_sandbox_removes_couchtop_shell()
    {
        var prefix = $@"{ShellRegistryPaths.SandboxRoot}\it-{Guid.NewGuid():N}";
        var sandbox = new CurrentUserRegistryStore(prefix);
        try
        {
            var layout = new InstallLayout(Path.GetDirectoryName(RepoPaths.Recovery)!);
            sandbox.SetString(ShellRegistryPaths.WinlogonKey, "Shell", ShellCommandBuilder.Build(layout.GuardianPath, ShellBootstrapMode.Resilient, @"C:\Windows"));
            var (code, report) = Run(RepoPaths.Recovery, $"--restore --quiet --sandbox \"{prefix}\" --report \"{{report}}\"", TimeSpan.FromSeconds(60));
            Assert.True(code == 0, report.ToString());
            Assert.Null(sandbox.GetString(ShellRegistryPaths.WinlogonKey, "Shell"));
        }
        finally
        {
            sandbox.DeleteSandbox();
        }
    }

    [Fact]
    public void Recovery_script_is_shipped_and_only_targets_hkcu()
    {
        var script = Path.Combine(Path.GetDirectoryName(RepoPaths.Recovery)!, "Recover-Explorer.cmd");
        Assert.True(File.Exists(script));
        var text = File.ReadAllText(script);
        Assert.Contains(@"reg delete ""HKCU\Software\Microsoft\Windows NT\CurrentVersion\Winlogon"" /v Shell /f", text);
        Assert.DoesNotContain("HKLM", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("explorer.exe", text);
    }
}
