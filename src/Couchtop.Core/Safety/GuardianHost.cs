using Couchtop.Core.Diagnostics;
using Couchtop.Core.Shell;

namespace Couchtop.Core.Safety;

public enum GuardianAction
{
    RestartApp,
    HandOffToExplorer,
    FallBackToExplorer,
    EmergencyFallback,
    EndSession,
}

/// <summary>Decides what the Guardian does when Couchtop exits. Pure logic, unit tested.</summary>
public sealed class CrashPolicy
{
    private readonly int _maxCrashes;
    private readonly TimeSpan _window;
    private readonly List<DateTime> _crashes = new();

    public CrashPolicy(int maxCrashes = 3, TimeSpan? window = null)
    {
        _maxCrashes = Math.Max(1, maxCrashes);
        _window = window ?? TimeSpan.FromMinutes(5);
    }

    public int RecentCrashes => _crashes.Count;

    public TimeSpan RestartDelay => _crashes.Count <= 1 ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(3);

    public GuardianAction OnAppExit(int exitCode, DateTime utcNow, bool sessionEnding)
    {
        if (sessionEnding) return GuardianAction.EndSession;
        return exitCode switch
        {
            ExitCodes.Emergency => GuardianAction.EmergencyFallback,
            ExitCodes.Restart => GuardianAction.RestartApp,
            ExitCodes.SignOut => GuardianAction.EndSession,
            ExitCodes.Success or ExitCodes.SwitchToExplorer => GuardianAction.HandOffToExplorer,
            _ => RegisterCrash(utcNow),
        };
    }

    public GuardianAction OnAppHung(DateTime utcNow) => RegisterCrash(utcNow);

    public GuardianAction OnStartFailure(DateTime utcNow) => RegisterCrash(utcNow);

    private GuardianAction RegisterCrash(DateTime utcNow)
    {
        _crashes.RemoveAll(t => utcNow - t > _window);
        _crashes.Add(utcNow);
        return _crashes.Count >= _maxCrashes ? GuardianAction.FallBackToExplorer : GuardianAction.RestartApp;
    }
}

public enum GuardianOutcome
{
    HandedToExplorer,
    CrashFallback,
    BootLoopFallback,
    StartFailureFallback,
    Emergency,
    SessionEnded,
    Stopped,
}

public sealed class GuardianOptions
{
    public string AppPath { get; set; } = "";
    public string AppArguments { get; set; } = "--shell-session";
    public TimeSpan ReadyTimeout { get; set; } = TimeSpan.FromSeconds(90);
    public TimeSpan HeartbeatTimeout { get; set; } = TimeSpan.FromSeconds(45);
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMilliseconds(250);
    public int MaxCrashes { get; set; } = 3;
    public TimeSpan CrashWindow { get; set; } = TimeSpan.FromMinutes(5);
    public int MaxPendingBoots { get; set; } = 2;
    public bool RunStartupApps { get; set; } = true;
}

public interface IAppProcess : IDisposable
{
    bool HasExited { get; }
    int ExitCode { get; }
    bool WaitForExit(TimeSpan timeout);
    void Kill();
    bool IsHung();
}

public interface IGuardianEnvironment
{
    DateTime UtcNow { get; }
    bool FileExists(string path);
    void ResetSignals();
    IAppProcess StartApp(string path, string arguments);
    bool IsReadySignaled();
    bool ConsumeHeartbeat();
    bool IsExplorerShellRunning();
    void StartExplorer();
    void Notify(string title, string message);
    void RunStartupApps();
    void Delay(TimeSpan duration, CancellationToken cancellationToken);
}

/// <summary>
/// The shell-mode supervisor. It is started by Winlogon, launches Couchtop, and guarantees that the user
/// always ends up with a working shell: crashes are retried a few times, hangs are detected, boot loops are
/// broken, and every failure path ends with Explorer running and shell mode disabled for the next sign-in.
/// </summary>
public sealed class GuardianHost
{
    private readonly GuardianOptions _options;
    private readonly IGuardianEnvironment _env;
    private readonly ShellModeManager _shell;
    private volatile bool _emergency;
    private volatile bool _sessionEnding;
    private volatile bool _resumed;

    public GuardianHost(GuardianOptions options, IGuardianEnvironment environment, ShellModeManager shell)
    {
        _options = options;
        _env = environment;
        _shell = shell;
    }

    public List<string> Events { get; } = new();
    public bool ExplorerStarted { get; private set; }
    public bool ShellDisabled { get; private set; }

    public void RequestEmergency() => _emergency = true;
    public void NotifySessionEnding() => _sessionEnding = true;
    public void NotifyResumed() => _resumed = true;

    private void Event(string message)
    {
        lock (Events) Events.Add(message);
        Log.Info("[guardian] " + message);
    }

    public GuardianOutcome Run(CancellationToken cancellationToken)
    {
        var pending = _shell.BootGuard.BeginBoot();
        Event($"sign-in attempt {pending}");
        if (pending > _options.MaxPendingBoots)
        {
            return Fallback(GuardianOutcome.BootLoopFallback,
                $"Couchtop did not start correctly on the last {pending - 1} sign-ins, so Windows Explorer was started instead and shell mode was turned off.");
        }

        var policy = new CrashPolicy(_options.MaxCrashes, _options.CrashWindow);
        var startupAppsRan = false;

        while (true)
        {
            if (cancellationToken.IsCancellationRequested) return GuardianOutcome.Stopped;
            if (_emergency) return Emergency(null);
            if (_sessionEnding) return GuardianOutcome.SessionEnded;

            if (!_env.FileExists(_options.AppPath))
            {
                return Fallback(GuardianOutcome.StartFailureFallback, "Couchtop could not be found, so Windows Explorer was started and shell mode was turned off.");
            }

            IAppProcess process;
            _env.ResetSignals();
            try
            {
                process = _env.StartApp(_options.AppPath, _options.AppArguments);
                Event("app started");
            }
            catch (Exception ex)
            {
                Event("app failed to start: " + ex.Message);
                if (policy.OnStartFailure(_env.UtcNow) == GuardianAction.FallBackToExplorer)
                    return Fallback(GuardianOutcome.StartFailureFallback, "Couchtop could not be started, so Windows Explorer was started and shell mode was turned off.");
                _env.Delay(policy.RestartDelay, cancellationToken);
                continue;
            }

            GuardianAction action;
            using (process)
            {
                var supervision = Supervise(process, cancellationToken, ref startupAppsRan);
                if (supervision is { } outcome) return outcome;

                if (_sessionEnding) return GuardianOutcome.SessionEnded;
                action = _lastHung
                    ? policy.OnAppHung(_env.UtcNow)
                    : policy.OnAppExit(process.ExitCode, _env.UtcNow, _sessionEnding);
                Event(_lastHung ? "app hung and was stopped" : $"app exited with code {process.ExitCode}");
            }

            switch (action)
            {
                case GuardianAction.RestartApp:
                    Event($"restarting app (recent crashes: {policy.RecentCrashes})");
                    _env.Delay(policy.RestartDelay, cancellationToken);
                    continue;
                case GuardianAction.HandOffToExplorer:
                    EnsureExplorer();
                    return GuardianOutcome.HandedToExplorer;
                case GuardianAction.FallBackToExplorer:
                    return Fallback(GuardianOutcome.CrashFallback, "Couchtop stopped unexpectedly several times, so Windows Explorer was started and shell mode was turned off for your next sign-in.");
                case GuardianAction.EmergencyFallback:
                    return Emergency(null);
                case GuardianAction.EndSession:
                    return GuardianOutcome.SessionEnded;
            }
        }
    }

    private bool _lastHung;

    private GuardianOutcome? Supervise(IAppProcess process, CancellationToken cancellationToken, ref bool startupAppsRan)
    {
        _lastHung = false;
        var started = _env.UtcNow;
        var lastBeat = started;
        var lastTick = started;
        var ready = false;

        while (true)
        {
            if (_emergency) return Emergency(process);
            if (cancellationToken.IsCancellationRequested) return GuardianOutcome.Stopped;
            if (process.WaitForExit(_options.PollInterval)) return null;
            if (_sessionEnding)
            {
                process.WaitForExit(TimeSpan.FromSeconds(5));
                return GuardianOutcome.SessionEnded;
            }

            var now = _env.UtcNow;
            if (_resumed || now - lastTick > TimeSpan.FromSeconds(5))
            {
                // Sleep/resume or a long stall of this process: don't count the gap against the app.
                _resumed = false;
                lastBeat = now;
                if (!ready) started = now;
            }
            lastTick = now;

            if (!ready)
            {
                if (_env.IsReadySignaled())
                {
                    ready = true;
                    lastBeat = now;
                    _shell.BootGuard.MarkHealthy();
                    Event("app reported ready");
                    if (!startupAppsRan && _options.RunStartupApps)
                    {
                        startupAppsRan = true;
                        try { _env.RunStartupApps(); }
                        catch (Exception ex) { Log.Warn("Startup apps failed", ex); }
                    }
                }
                else if (now - started > _options.ReadyTimeout)
                {
                    Event("app did not become ready in time");
                    SafeKill(process);
                    _lastHung = true;
                    return null;
                }
            }
            else if (_env.ConsumeHeartbeat())
            {
                lastBeat = now;
            }
            else
            {
                var stale = now - lastBeat;
                if (stale > _options.HeartbeatTimeout && (process.IsHung() || stale > _options.HeartbeatTimeout * 3))
                {
                    Event($"app stopped responding for {stale.TotalSeconds:0}s");
                    SafeKill(process);
                    _lastHung = true;
                    return null;
                }
            }
        }
    }

    private static void SafeKill(IAppProcess process)
    {
        try
        {
            process.Kill();
            process.WaitForExit(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            Log.Warn("Could not stop Couchtop", ex);
        }
    }

    private GuardianOutcome Emergency(IAppProcess? process)
    {
        Event("emergency shortcut");
        EnsureExplorer();
        DisableShell();
        if (process is not null && !process.HasExited)
        {
            if (!process.WaitForExit(TimeSpan.FromSeconds(2))) SafeKill(process);
        }
        return GuardianOutcome.Emergency;
    }

    private GuardianOutcome Fallback(GuardianOutcome outcome, string message)
    {
        Event("fallback: " + outcome);
        EnsureExplorer();
        DisableShell();
        try { _env.Notify("Couchtop", message + "\n\nYou can turn shell mode back on in Couchtop Settings after fixing the problem."); }
        catch (Exception ex) { Log.Warn("Notify failed", ex); }
        return outcome;
    }

    private void EnsureExplorer()
    {
        try
        {
            if (!_env.IsExplorerShellRunning())
            {
                _env.StartExplorer();
                ExplorerStarted = true;
                Event("explorer started");
            }
        }
        catch (Exception ex)
        {
            Log.Error("Could not start Explorer", ex);
        }
    }

    private void DisableShell()
    {
        var result = _shell.Disable();
        ShellDisabled = result.Success;
        Event("shell disabled: " + result.Message);
    }
}
