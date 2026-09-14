using System.Diagnostics;
using System.Text.Json;
using Couchtop.Core.Diagnostics;
using Couchtop.Core.Native;
using Couchtop.Core.Platform;
using Couchtop.Core.Safety;
using Couchtop.Core.Settings;
using Couchtop.Core.Shell;
using Couchtop.Core.Storage;

namespace Couchtop.Guardian;

/// <summary>
/// Couchtop Guardian: the process Winlogon starts in shell mode. It deliberately has no UI framework
/// dependency so it keeps working even if the Couchtop UI is broken.
/// </summary>
internal static class Program
{
    private const int EmergencyHotkeyId = 1;

    [STAThread]
    private static int Main(string[] args)
    {
        var paths = AppPaths.ForCurrentUser();
        Log.Initialize(paths.LogDirectory, "guardian");
        try
        {
            if (Has(args, "--rehearsal")) return Rehearsal.Run(args, paths);
            if (Has(args, "--shell")) return RunShell(paths);

            NativeMethods.MessageBox(IntPtr.Zero,
                "Couchtop Guardian starts automatically when shell mode is enabled.\n\nManage shell mode from Couchtop > Settings > Shell Mode.",
                "Couchtop Guardian", NativeMethods.MB_OK | NativeMethods.MB_ICONINFORMATION);
            return 0;
        }
        catch (Exception ex)
        {
            Log.Error("Guardian fatal error", ex);
            if (Has(args, "--shell")) Failsafe();
            return ExitCodes.FatalError;
        }
    }

    internal static bool Has(string[] args, string flag) => args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));

    internal static string? Value(string[] args, string flag)
    {
        var i = Array.FindIndex(args, a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    /// <summary>Last-resort path when the Guardian itself fails: get the user to a working desktop.</summary>
    private static void Failsafe()
    {
        try
        {
            if (!NativeMethods.IsExplorerShellRunning()) new ExplorerController().StartExplorer();
        }
        catch (Exception ex)
        {
            Log.Error("Failsafe could not start Explorer", ex);
        }
        try
        {
            new ShellModeManager(new CurrentUserRegistryStore(), InstallLayout.Current).Disable();
        }
        catch (Exception ex)
        {
            Log.Error("Failsafe could not disable shell mode", ex);
        }
    }

    private static int RunShell(AppPaths paths)
    {
        using var mutex = new Mutex(true, InstanceSignals.GuardianMutexName, out var created);
        if (!created)
        {
            Log.Info("Another Guardian is already running in this session.");
            return 0;
        }

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Log.Error("Unhandled Guardian exception", e.ExceptionObject as Exception);
            Failsafe();
        };

        var layout = InstallLayout.Current;
        var shell = new ShellModeManager(new CurrentUserRegistryStore(), layout);
        UserSettings settings;
        try
        {
            settings = new SettingsService(paths).Current;
        }
        catch (Exception ex)
        {
            Log.Warn("Settings unreadable; using defaults", ex);
            settings = new UserSettings();
        }

        using var environment = new RealGuardianEnvironment(null);
        var host = new GuardianHost(new GuardianOptions
        {
            AppPath = layout.AppPath,
            RunStartupApps = settings.RunStartupAppsInShell && !NativeMethods.IsExplorerShellRunning(),
        }, environment, shell);

        using var window = new NativeMessageWindow("CouchtopGuardianWindow");
        HotkeyParser.TryParse(HotkeyParser.EmergencyHotkey, out var hotkey);
        var hotkeyOk = NativeMethods.RegisterHotKey(window.Handle, EmergencyHotkeyId, hotkey.Modifiers | NativeMethods.MOD_NOREPEAT, hotkey.VirtualKey);
        Log.Info(hotkeyOk ? "Emergency hotkey registered by Guardian." : "Emergency hotkey could not be registered by Guardian.");

        window.MessageHandler = (msg, wParam, lParam) =>
        {
            switch (msg)
            {
                case NativeMethods.WM_HOTKEY when wParam.ToInt32() == EmergencyHotkeyId:
                    host.RequestEmergency();
                    return IntPtr.Zero;
                case NativeMethods.WM_QUERYENDSESSION:
                    host.NotifySessionEnding();
                    return (IntPtr)1;
                case NativeMethods.WM_ENDSESSION when wParam != IntPtr.Zero:
                    host.NotifySessionEnding();
                    return IntPtr.Zero;
                case NativeMethods.WM_POWERBROADCAST when wParam.ToInt32() is NativeMethods.PBT_APMRESUMEAUTOMATIC or NativeMethods.PBT_APMRESUMESUSPEND:
                    host.NotifyResumed();
                    return (IntPtr)1;
                default:
                    return null;
            }
        };

        var worker = new Thread(() =>
        {
            try
            {
                var outcome = host.Run(CancellationToken.None);
                Log.Info("Guardian finished: " + outcome);
            }
            catch (Exception ex)
            {
                Log.Error("Guardian supervision failed", ex);
                Failsafe();
            }
            finally
            {
                window.Close();
            }
        })
        { Name = "Guardian supervisor" };
        worker.Start();

        window.RunMessageLoop();
        worker.Join(TimeSpan.FromSeconds(15));
        if (hotkeyOk) NativeMethods.UnregisterHotKey(window.Handle, EmergencyHotkeyId);
        return 0;
    }
}

internal sealed class AppProcess : IAppProcess
{
    private readonly Process _process;

    public AppProcess(Process process)
    {
        _process = process;
    }

    public bool HasExited => _process.HasExited;
    public int ExitCode => _process.ExitCode;

    public bool WaitForExit(TimeSpan timeout) => _process.WaitForExit((int)Math.Max(0, timeout.TotalMilliseconds));

    public void Kill()
    {
        if (!_process.HasExited) _process.Kill(entireProcessTree: true);
    }

    public bool IsHung()
    {
        try
        {
            _process.Refresh();
            var hwnd = _process.MainWindowHandle;
            return hwnd != IntPtr.Zero && NativeMethods.IsHungAppWindow(hwnd);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public void Dispose() => _process.Dispose();
}

internal class RealGuardianEnvironment : IGuardianEnvironment, IDisposable
{
    private readonly EventWaitHandle _ready;
    private readonly EventWaitHandle _heartbeat;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly DateTime _origin = DateTime.UtcNow;

    public RealGuardianEnvironment(string? signalSuffix)
    {
        SignalSuffix = signalSuffix;
        _ready = InstanceSignals.Ready(signalSuffix);
        _heartbeat = InstanceSignals.Heartbeat(signalSuffix);
    }

    public string? SignalSuffix { get; }
    public DateTime UtcNow => _origin + _clock.Elapsed;

    public bool FileExists(string path) => File.Exists(path);

    public void ResetSignals()
    {
        _ready.Reset();
        _heartbeat.Reset();
    }

    public virtual IAppProcess StartApp(string path, string arguments)
    {
        var psi = new ProcessStartInfo(path, arguments)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(path) ?? Environment.CurrentDirectory,
        };
        var process = Process.Start(psi) ?? throw new InvalidOperationException("Process.Start returned null");
        return new AppProcess(process);
    }

    public bool IsReadySignaled() => _ready.WaitOne(0);
    public bool ConsumeHeartbeat() => _heartbeat.WaitOne(0);

    public virtual bool IsExplorerShellRunning() => NativeMethods.IsExplorerShellRunning();
    public virtual void StartExplorer() => new ExplorerController().StartExplorer();

    public virtual void Notify(string title, string message)
    {
        // Foreground thread: the message stays visible even after the Guardian's work is done.
        new Thread(() => NativeMethods.MessageBox(IntPtr.Zero, message, title,
            NativeMethods.MB_OK | NativeMethods.MB_ICONWARNING | NativeMethods.MB_SETFOREGROUND | NativeMethods.MB_TOPMOST))
        { IsBackground = false }.Start();
    }

    public virtual void RunStartupApps()
    {
        Task.Run(() =>
        {
            Thread.Sleep(3000);
            StartupApps.LaunchAll(StartupApps.Collect());
        });
    }

    public void Delay(TimeSpan duration, CancellationToken cancellationToken) => cancellationToken.WaitHandle.WaitOne(duration);

    public void Dispose()
    {
        _ready.Dispose();
        _heartbeat.Dispose();
    }
}

/// <summary>Records what would happen instead of touching Explorer; used by the Shell Safety Test.</summary>
internal sealed class RehearsalEnvironment : RealGuardianEnvironment
{
    public RehearsalEnvironment(string suffix) : base(suffix)
    {
    }

    public int AppStarts { get; private set; }
    public bool ExplorerStartRequested { get; private set; }
    public List<string> Notifications { get; } = new();

    public override IAppProcess StartApp(string path, string arguments)
    {
        AppStarts++;
        return base.StartApp(path, arguments);
    }

    public override bool IsExplorerShellRunning() => ExplorerStartRequested;
    public override void StartExplorer() => ExplorerStartRequested = true;
    public override void Notify(string title, string message) => Notifications.Add(message);
    public override void RunStartupApps() { }
}

internal static class Rehearsal
{
    public static int Run(string[] args, AppPaths paths)
    {
        var scenario = (Program.Value(args, "--scenario") ?? "crash").ToLowerInvariant();
        var resultFile = Program.Value(args, "--result");
        var layout = InstallLayout.Current;
        var appPath = Program.Value(args, "--app") ?? layout.AppPath;
        var suffix = "rehearsal-" + Guid.NewGuid().ToString("N")[..12];
        var sandbox = new CurrentUserRegistryStore($@"{ShellRegistryPaths.SandboxRoot}\{suffix}");
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        object result;
        var passed = false;
        try
        {
            var shell = new ShellModeManager(sandbox, layout, windowsDirectory: windows);
            shell.Mechanism.Apply(ShellCommandBuilder.Build(layout.GuardianPath, ShellBootstrapMode.Resilient, windows));
            if (scenario == "bootloop")
            {
                shell.BootGuard.BeginBoot();
                shell.BootGuard.BeginBoot();
            }

            var appArgs = scenario switch
            {
                "crash" => "--crash-test",
                "hang" => $"--hang-test --signal-suffix {suffix}",
                _ => $"--ok-test --signal-suffix {suffix}",
            };

            using var env = new RehearsalEnvironment(suffix);
            var host = new GuardianHost(new GuardianOptions
            {
                AppPath = appPath,
                AppArguments = appArgs,
                ReadyTimeout = TimeSpan.FromSeconds(20),
                HeartbeatTimeout = TimeSpan.FromSeconds(2),
                PollInterval = TimeSpan.FromMilliseconds(100),
                CrashWindow = TimeSpan.FromMinutes(2),
                RunStartupApps = false,
            }, env, shell);

            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            var outcome = host.Run(cts.Token);
            var status = shell.GetStatus();

            passed = scenario switch
            {
                "crash" => outcome == GuardianOutcome.CrashFallback && env.ExplorerStartRequested && !status.EnabledForCurrentUser && env.AppStarts == 3 && env.Notifications.Count == 1,
                "hang" => outcome == GuardianOutcome.CrashFallback && env.ExplorerStartRequested && !status.EnabledForCurrentUser && env.AppStarts == 3,
                "bootloop" => outcome == GuardianOutcome.BootLoopFallback && env.ExplorerStartRequested && !status.EnabledForCurrentUser && env.AppStarts == 0,
                _ => outcome == GuardianOutcome.HandedToExplorer && env.ExplorerStartRequested && status.EnabledForCurrentUser && env.AppStarts == 1,
            };

            result = new
            {
                scenario,
                passed,
                outcome = outcome.ToString(),
                env.AppStarts,
                env.ExplorerStartRequested,
                shellStillEnabled = status.EnabledForCurrentUser,
                notifications = env.Notifications,
                events = host.Events,
            };
        }
        catch (Exception ex)
        {
            Log.Error("Rehearsal failed", ex);
            result = new { scenario, passed = false, error = ex.Message };
        }
        finally
        {
            try { sandbox.DeleteSandbox(); } catch (Exception ex) { Log.Warn("Rehearsal sandbox cleanup failed", ex); }
        }

        if (resultFile is not null)
            File.WriteAllText(resultFile, JsonSerializer.Serialize(result, JsonStore.Options));
        return passed ? 0 : 2;
    }
}
