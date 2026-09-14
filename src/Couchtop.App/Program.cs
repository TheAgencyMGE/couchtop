using System.IO;
using System.Windows.Threading;
using Couchtop.Core.Diagnostics;
using Couchtop.Core.Native;
using Couchtop.Core.Safety;
using Couchtop.Core.Storage;

namespace Couchtop.App;

public sealed class CommandLineOptions
{
    public bool ShellSession { get; private set; }
    public bool Autostart { get; private set; }
    public bool CrashTest { get; private set; }
    public bool HangTest { get; private set; }
    public bool OkTest { get; private set; }
    public string? SignalSuffix { get; private set; }
    public string? SnapshotDirectory { get; private set; }
    public int SnapshotWidth { get; private set; } = 1920;
    public int SnapshotHeight { get; private set; } = 1080;
    public bool IsSnapshot => SnapshotDirectory is not null;

    /// <summary>--smoke-test N: start normally, log startup metrics, then exit cleanly after N seconds.</summary>
    public int SmokeTestSeconds { get; private set; }

    public static CommandLineOptions Parse(string[] args)
    {
        var o = new CommandLineOptions();
        for (var i = 0; i < args.Length; i++)
        {
            string? Next() => i + 1 < args.Length ? args[++i] : null;
            switch (args[i].ToLowerInvariant())
            {
                case "--shell-session": o.ShellSession = true; break;
                case "--autostart": o.Autostart = true; break;
                case "--crash-test": o.CrashTest = true; break;
                case "--hang-test": o.HangTest = true; break;
                case "--ok-test": o.OkTest = true; break;
                case "--signal-suffix": o.SignalSuffix = Next(); break;
                case "--render-snapshot": o.SnapshotDirectory = Next(); break;
                case "--smoke-test": o.SmokeTestSeconds = int.TryParse(Next(), out var seconds) ? Math.Clamp(seconds, 1, 600) : 8; break;
                case "--size":
                    var parts = (Next() ?? "").Split('x');
                    if (parts.Length == 2 && int.TryParse(parts[0], out var w) && int.TryParse(parts[1], out var h))
                    {
                        o.SnapshotWidth = Math.Clamp(w, 640, 7680);
                        o.SnapshotHeight = Math.Clamp(h, 480, 4320);
                    }
                    break;
            }
        }
        return o;
    }
}

public static class Program
{
    private static readonly List<DateTime> RecentUiErrors = new();

    public static CommandLineOptions Options { get; private set; } = new();

    [STAThread]
    public static int Main(string[] args)
    {
        Options = CommandLineOptions.Parse(args);

        // Shell Safety Test helpers: exercised by the Guardian rehearsal before any UI is created.
        if (Options.CrashTest) return ExitCodes.CrashTest;
        if (args.Length >= 2 && string.Equals(args[0], "--export-audio", StringComparison.OrdinalIgnoreCase))
        {
            Services.SoundExporter.ExportAll(args[1]);
            return ExitCodes.Success;
        }
        if (Options.HangTest || Options.OkTest) return RunSignalTest();

        var paths = AppPaths.ForCurrentUser();
        try { paths.EnsureCreated(); } catch { }
        Log.Initialize(paths.LogDirectory, Options.IsSnapshot ? "snapshot" : "couchtop");

        Mutex? mutex = null;
        if (!Options.IsSnapshot)
        {
            mutex = new Mutex(true, InstanceSignals.AppMutexName, out var created);
            if (!created)
            {
                try
                {
                    using var activate = InstanceSignals.Activate();
                    activate.Set();
                }
                catch (Exception ex)
                {
                    Log.Warn("Could not signal running instance", ex);
                }
                return ExitCodes.Success;
            }
        }

        AppDomain.CurrentDomain.UnhandledException += (_, e) => OnFatal(e.ExceptionObject as Exception, paths);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Warn("Unobserved task exception", e.Exception);
            e.SetObserved();
        };

        try
        {
            var app = new App();
            app.InitializeComponent();
            app.DispatcherUnhandledException += (_, e) => OnDispatcherException(e, paths);
            return app.Run();
        }
        catch (Exception ex)
        {
            OnFatal(ex, paths);
            return ExitCodes.FatalError;
        }
        finally
        {
            mutex?.Dispose();
        }
    }

    private static int RunSignalTest()
    {
        using var ready = InstanceSignals.Ready(Options.SignalSuffix);
        using var heartbeat = InstanceSignals.Heartbeat(Options.SignalSuffix);
        ready.Set();
        if (Options.HangTest)
        {
            Thread.Sleep(TimeSpan.FromSeconds(60));
            return 0;
        }
        for (var i = 0; i < 6; i++)
        {
            heartbeat.Set();
            Thread.Sleep(300);
        }
        return ExitCodes.SwitchToExplorer;
    }

    private static void OnDispatcherException(DispatcherUnhandledExceptionEventArgs e, AppPaths paths)
    {
        Log.Error("UI exception", e.Exception);
        lock (RecentUiErrors)
        {
            var now = DateTime.UtcNow;
            RecentUiErrors.RemoveAll(t => now - t > TimeSpan.FromMinutes(1));
            RecentUiErrors.Add(now);
            if (RecentUiErrors.Count <= 3 && !Options.IsSnapshot)
            {
                e.Handled = true;
                AppHost.CurrentOrNull?.PostMessage("Something went wrong", "Couchtop recovered from an error. Details are in the log folder.");
                return;
            }
        }
        OnFatal(e.Exception, paths);
    }

    private static void OnFatal(Exception? ex, AppPaths paths)
    {
        Log.Error("Fatal Couchtop error", ex);
        try
        {
            File.WriteAllText(Path.Combine(paths.LogDirectory, $"crash-{DateTime.Now:yyyyMMdd-HHmmss}.txt"), ex?.ToString() ?? "unknown");
        }
        catch
        {
        }

        // Never leave a black screen: if nothing supervises us and there is no Explorer shell, start one.
        if (!Options.ShellSession && !Options.IsSnapshot)
        {
            try
            {
                if (!NativeMethods.IsExplorerShellRunning()) new ExplorerController().StartExplorer();
            }
            catch (Exception startEx)
            {
                Log.Error("Could not start Explorer after crash", startEx);
            }
        }
    }
}
