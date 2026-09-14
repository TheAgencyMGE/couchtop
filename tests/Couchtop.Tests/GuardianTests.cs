using Couchtop.Core.Safety;
using Couchtop.Core.Settings;
using Couchtop.Core.Shell;
using Couchtop.Core.Storage;

namespace Couchtop.Tests;

public class CrashPolicyTests
{
    [Fact]
    public void Restarts_until_threshold_then_falls_back()
    {
        var policy = new CrashPolicy(3, TimeSpan.FromMinutes(5));
        var t = DateTime.UtcNow;
        Assert.Equal(GuardianAction.RestartApp, policy.OnAppExit(-1, t, false));
        Assert.Equal(GuardianAction.RestartApp, policy.OnAppExit(-1, t.AddSeconds(10), false));
        Assert.Equal(GuardianAction.FallBackToExplorer, policy.OnAppExit(-1, t.AddSeconds(20), false));
    }

    [Fact]
    public void Old_crashes_expire()
    {
        var policy = new CrashPolicy(3, TimeSpan.FromMinutes(5));
        var t = DateTime.UtcNow;
        policy.OnAppExit(-1, t, false);
        policy.OnAppExit(-1, t.AddMinutes(1), false);
        Assert.Equal(GuardianAction.RestartApp, policy.OnAppExit(-1, t.AddMinutes(10), false));
        Assert.Equal(1, policy.RecentCrashes);
    }

    [Theory]
    [InlineData(ExitCodes.Emergency, GuardianAction.EmergencyFallback)]
    [InlineData(ExitCodes.Restart, GuardianAction.RestartApp)]
    [InlineData(ExitCodes.SignOut, GuardianAction.EndSession)]
    [InlineData(ExitCodes.Success, GuardianAction.HandOffToExplorer)]
    [InlineData(ExitCodes.SwitchToExplorer, GuardianAction.HandOffToExplorer)]
    public void Special_exit_codes(int code, GuardianAction expected)
    {
        Assert.Equal(expected, new CrashPolicy().OnAppExit(code, DateTime.UtcNow, false));
    }

    [Fact]
    public void Session_ending_never_counts_as_crash()
    {
        var policy = new CrashPolicy(1);
        Assert.Equal(GuardianAction.EndSession, policy.OnAppExit(-1, DateTime.UtcNow, sessionEnding: true));
        Assert.Equal(0, policy.RecentCrashes);
    }
}

public class GuardianHostTests
{
    private sealed record Script(int? ExitCode = null, int ExitAfterPolls = 4, bool Ready = true, bool Heartbeat = true, bool NeverExit = false, Action<GuardianHost>? OnPoll = null);

    private sealed class FakeProcess : IAppProcess
    {
        private readonly FakeEnv _env;
        public FakeProcess(FakeEnv env, Script script) { _env = env; Script = script; }
        public Script Script { get; }
        public int Polls { get; private set; }
        public bool Exited { get; private set; }
        public bool Killed { get; private set; }
        public int Code { get; private set; }
        public bool HasExited => Exited;
        public int ExitCode => Code;

        public bool WaitForExit(TimeSpan timeout)
        {
            if (Exited) return true;
            _env.Advance(timeout);
            Polls++;
            Script.OnPoll?.Invoke(_env.Host!);
            if (!Script.NeverExit && Polls >= Script.ExitAfterPolls)
            {
                Exited = true;
                Code = Script.ExitCode ?? 0;
            }
            return Exited;
        }

        public void Kill() { Killed = true; Exited = true; Code = -1; }
        public bool IsHung() => false;
        public void Dispose() { }
    }

    private sealed class FakeEnv : IGuardianEnvironment
    {
        private readonly Queue<Script> _scripts;
        private DateTime _now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public FakeEnv(params Script[] scripts) { _scripts = new Queue<Script>(scripts); }
        public GuardianHost? Host { get; set; }
        public List<FakeProcess> Processes { get; } = new();
        public bool AppExists { get; set; } = true;
        public bool ExplorerRunning { get; set; }
        public int ExplorerStarts { get; private set; }
        public List<string> Notifications { get; } = new();
        public int StartupRuns { get; private set; }
        private FakeProcess? Current => Processes.LastOrDefault();

        public DateTime UtcNow => _now;
        public void Advance(TimeSpan t) => _now += t;
        public bool FileExists(string path) => AppExists;
        public void ResetSignals() { }

        public IAppProcess StartApp(string path, string arguments)
        {
            var script = _scripts.Count > 0 ? _scripts.Dequeue() : new Script(ExitCode: ExitCodes.SwitchToExplorer);
            var p = new FakeProcess(this, script);
            Processes.Add(p);
            return p;
        }

        public bool IsReadySignaled() => Current is { Script.Ready: true, Polls: >= 1 };
        public bool ConsumeHeartbeat() => Current?.Script.Heartbeat == true;
        public bool IsExplorerShellRunning() => ExplorerRunning;
        public void StartExplorer() { ExplorerStarts++; ExplorerRunning = true; }
        public void Notify(string title, string message) => Notifications.Add(message);
        public void RunStartupApps() => StartupRuns++;
        public void Delay(TimeSpan duration, CancellationToken cancellationToken) => Advance(duration);
    }

    private static (GuardianHost Host, FakeEnv Env, MemoryRegistryStore Store, ShellModeManager Shell) Create(GuardianOptions? options, params Script[] scripts)
    {
        var store = new MemoryRegistryStore();
        var layout = new InstallLayout(@"C:\WD");
        var shell = new ShellModeManager(store, layout, windowsDirectory: @"C:\Windows");
        shell.Mechanism.Apply(ShellCommandBuilder.Build(layout.GuardianPath, ShellBootstrapMode.Resilient, @"C:\Windows"));
        var env = new FakeEnv(scripts);
        var host = new GuardianHost(options ?? new GuardianOptions
        {
            AppPath = layout.AppPath,
            HeartbeatTimeout = TimeSpan.FromSeconds(2),
            ReadyTimeout = TimeSpan.FromSeconds(5),
        }, env, shell);
        env.Host = host;
        return (host, env, store, shell);
    }

    [Fact]
    public void Repeated_crashes_fall_back_to_explorer_and_disable_shell()
    {
        var (host, env, _, shell) = Create(null, new Script(ExitCode: 5), new Script(ExitCode: 5), new Script(ExitCode: 5));
        var outcome = host.Run(CancellationToken.None);
        Assert.Equal(GuardianOutcome.CrashFallback, outcome);
        Assert.Equal(3, env.Processes.Count);
        Assert.Equal(1, env.ExplorerStarts);
        Assert.False(shell.GetStatus().EnabledForCurrentUser);
        Assert.Single(env.Notifications);
    }

    [Fact]
    public void Single_crash_is_retried_and_clean_handoff_keeps_shell_enabled()
    {
        var (host, env, _, shell) = Create(null, new Script(ExitCode: -532462766), new Script(ExitCode: ExitCodes.SwitchToExplorer));
        Assert.Equal(GuardianOutcome.HandedToExplorer, host.Run(CancellationToken.None));
        Assert.Equal(2, env.Processes.Count);
        Assert.Equal(1, env.ExplorerStarts);
        Assert.True(shell.GetStatus().EnabledForCurrentUser);
        Assert.Equal(1, env.StartupRuns);
    }

    [Fact]
    public void Hung_app_is_killed_and_counted()
    {
        var hang = new Script(Heartbeat: false, NeverExit: true);
        var (host, env, _, _) = Create(null, hang, hang, hang);
        Assert.Equal(GuardianOutcome.CrashFallback, host.Run(CancellationToken.None));
        Assert.All(env.Processes, p => Assert.True(p.Killed));
    }

    [Fact]
    public void App_that_never_becomes_ready_is_killed()
    {
        var never = new Script(Ready: false, NeverExit: true);
        var (host, env, _, _) = Create(null, never, new Script(ExitCode: ExitCodes.SwitchToExplorer));
        Assert.Equal(GuardianOutcome.HandedToExplorer, host.Run(CancellationToken.None));
        Assert.True(env.Processes[0].Killed);
    }

    [Fact]
    public void Boot_loop_guard_skips_app_entirely()
    {
        var (host, env, _, shell) = Create(null);
        shell.BootGuard.BeginBoot();
        shell.BootGuard.BeginBoot();
        Assert.Equal(GuardianOutcome.BootLoopFallback, host.Run(CancellationToken.None));
        Assert.Empty(env.Processes);
        Assert.Equal(1, env.ExplorerStarts);
        Assert.False(shell.GetStatus().EnabledForCurrentUser);
    }

    [Fact]
    public void Ready_app_resets_boot_counter()
    {
        var (host, _, _, shell) = Create(null, new Script(ExitCode: ExitCodes.SwitchToExplorer));
        shell.BootGuard.BeginBoot();
        host.Run(CancellationToken.None);
        Assert.Equal(0, shell.BootGuard.PendingBoots);
    }

    [Fact]
    public void Emergency_hotkey_restores_explorer_disables_shell_and_stops_app()
    {
        var (host, env, _, shell) = Create(null, new Script(NeverExit: true, OnPoll: h => h.RequestEmergency()));
        Assert.Equal(GuardianOutcome.Emergency, host.Run(CancellationToken.None));
        Assert.Equal(1, env.ExplorerStarts);
        Assert.False(shell.GetStatus().EnabledForCurrentUser);
        Assert.True(env.Processes[0].Killed);
    }

    [Fact]
    public void App_emergency_exit_code_also_disables_shell()
    {
        var (host, env, _, shell) = Create(null, new Script(ExitCode: ExitCodes.Emergency));
        Assert.Equal(GuardianOutcome.Emergency, host.Run(CancellationToken.None));
        Assert.False(shell.GetStatus().EnabledForCurrentUser);
        Assert.Equal(1, env.ExplorerStarts);
    }

    [Fact]
    public void Sign_out_does_not_restart_or_fall_back()
    {
        var (host, env, _, shell) = Create(null, new Script(NeverExit: true, OnPoll: h => h.NotifySessionEnding()));
        Assert.Equal(GuardianOutcome.SessionEnded, host.Run(CancellationToken.None));
        Assert.Single(env.Processes);
        Assert.Equal(0, env.ExplorerStarts);
        Assert.True(shell.GetStatus().EnabledForCurrentUser);
    }

    [Fact]
    public void Restart_requests_do_not_count_as_crashes()
    {
        var (host, env, _, _) = Create(null,
            new Script(ExitCode: ExitCodes.Restart), new Script(ExitCode: ExitCodes.Restart), new Script(ExitCode: ExitCodes.Restart),
            new Script(ExitCode: 3), new Script(ExitCode: ExitCodes.SwitchToExplorer));
        Assert.Equal(GuardianOutcome.HandedToExplorer, host.Run(CancellationToken.None));
        Assert.Equal(5, env.Processes.Count);
    }

    [Fact]
    public void Missing_app_falls_back_immediately()
    {
        var (host, env, _, shell) = Create(null);
        env.AppExists = false;
        Assert.Equal(GuardianOutcome.StartFailureFallback, host.Run(CancellationToken.None));
        Assert.False(shell.GetStatus().EnabledForCurrentUser);
    }

    [Fact]
    public void Explorer_is_not_started_twice_when_already_running()
    {
        var (host, env, _, _) = Create(null, new Script(ExitCode: 9), new Script(ExitCode: 9), new Script(ExitCode: 9));
        env.ExplorerRunning = true;
        host.Run(CancellationToken.None);
        Assert.Equal(0, env.ExplorerStarts);
    }

    [Fact]
    public void Sleep_gap_is_not_treated_as_a_hang()
    {
        var resumed = false;
        var script = new Script(Heartbeat: false, ExitAfterPolls: 12, ExitCode: ExitCodes.SwitchToExplorer, OnPoll: h =>
        {
            if (!resumed) { resumed = true; }
            h.NotifyResumed();
        });
        var (host, env, _, _) = Create(null, script);
        Assert.Equal(GuardianOutcome.HandedToExplorer, host.Run(CancellationToken.None));
        Assert.False(env.Processes[0].Killed);
    }
}

public class RecoveryServiceTests
{
    private sealed class FakeExplorer : IExplorerController
    {
        public bool Running { get; set; }
        public int Starts { get; private set; }
        public bool IsShellRunning() => Running;
        public void StartExplorer() { Starts++; Running = true; }
    }

    [Fact]
    public void Restore_disables_shell_and_starts_explorer_once()
    {
        var store = new MemoryRegistryStore();
        var layout = new InstallLayout(@"C:\WD");
        var shell = new ShellModeManager(store, layout, windowsDirectory: @"C:\Windows");
        shell.Mechanism.Apply(ShellCommandBuilder.Build(layout.GuardianPath, ShellBootstrapMode.Direct, @"C:\Windows"));
        var explorer = new FakeExplorer();
        var stopped = 0;
        var report = new RecoveryService(shell, explorer, () => stopped++).Restore(new RecoveryOptions(StopCouchtop: true));
        Assert.True(report.Success);
        Assert.Equal(1, explorer.Starts);
        Assert.Equal(1, stopped);
        Assert.Null(store.GetString(ShellRegistryPaths.WinlogonKey, "Shell"));

        var again = new RecoveryService(shell, explorer).Restore(new RecoveryOptions());
        Assert.True(again.Success);
        Assert.Equal(1, explorer.Starts);
    }

    [Fact]
    public void Emergency_service_starts_explorer_before_disabling()
    {
        var store = new MemoryRegistryStore();
        var layout = new InstallLayout(@"C:\WD");
        var shell = new ShellModeManager(store, layout, windowsDirectory: @"C:\Windows");
        shell.Mechanism.Apply("\"C:\\WD\\Couchtop.Guardian.exe\" --shell");
        var explorer = new FakeExplorer();
        var steps = new EmergencyService(shell, explorer).Execute();
        Assert.Equal("Explorer started", steps[0]);
        Assert.False(shell.GetStatus().EnabledForCurrentUser);
    }

    [Fact]
    public void Recovery_verifier_passes_on_this_machine_using_sandbox_only()
    {
        var before = new CurrentUserRegistryStore().GetString(ShellRegistryPaths.WinlogonKey, "Shell");
        var checks = RecoveryVerifier.Verify(new InstallLayout(@"C:\WD"));
        Assert.All(checks, c => Assert.True(c.Passed, $"{c.Name}: {c.Detail}"));
        Assert.Equal(before, new CurrentUserRegistryStore().GetString(ShellRegistryPaths.WinlogonKey, "Shell"));
    }
}
