using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Couchtop.App.Services;
using Couchtop.Core.Audio;
using Couchtop.Core.Channels;
using Couchtop.Core.Diagnostics;
using Couchtop.Core.Discovery;
using Couchtop.Core.Launching;
using Couchtop.Core.Native;
using Couchtop.Core.Safety;
using Couchtop.Core.Settings;
using Couchtop.Core.Shell;
using Couchtop.Core.Storage;

namespace Couchtop.App;

public sealed record BoardMessage(string Title, string Body, DateTime At);

/// <summary>Composition root: owns every long-lived service for the Couchtop UI process.</summary>
public sealed class AppHost
{
    private EventWaitHandle? _ready;
    private EventWaitHandle? _heartbeat;
    private DispatcherTimer? _heartbeatTimer;
    private int _discoveryRunning;

    private AppHost(CommandLineOptions options)
    {
        Options = options;
        Paths = AppPaths.ForCurrentUser();
        Paths.EnsureCreated();
        Settings = new SettingsService(Paths);
        Layout = new LayoutService(Paths);
        Install = InstallLayout.Current;
        Registry = new CurrentUserRegistryStore();
        SafetyStore = new SafetyVerificationStore(Paths.SafetyFile);
        Compatibility = new CompatibilityChecker(new WindowsSystemProbe(Registry), Install);
        Shell = new ShellModeManager(Registry, Install, () => Compatibility.Run(), EvaluateSafetyGate);
        Discovery = DiscoveryService.CreateDefault(Paths, Settings.Current);
        Launcher = new AppLauncher(new SystemProcessStarter(), new DiscoveryChannelResolver(Discovery));
        AudioLibrary = new CustomAudioLibrary(System.IO.Path.Combine(Paths.DataRoot, "custom-audio"));
        Audio = new AudioService(Settings.Current, AudioLibrary);
        Icons = new IconService(Paths.IconCacheDirectory);
        Session = new SessionSentinel(Paths.SessionFile);
        IsShellSession = options.ShellSession || (!options.IsSnapshot && !NativeMethods.IsExplorerShellRunning());
    }

    public static AppHost Current { get; private set; } = null!;
    public static AppHost? CurrentOrNull { get; private set; }

    public CommandLineOptions Options { get; }
    public AppPaths Paths { get; }
    public SettingsService Settings { get; }
    public LayoutService Layout { get; }
    public InstallLayout Install { get; }
    public CurrentUserRegistryStore Registry { get; }
    public SafetyVerificationStore SafetyStore { get; }
    public CompatibilityChecker Compatibility { get; }
    public ShellModeManager Shell { get; }
    public DiscoveryService Discovery { get; }
    public AppLauncher Launcher { get; }
    public AudioService Audio { get; }
    public CustomAudioLibrary AudioLibrary { get; }
    public IconService Icons { get; }
    public SessionSentinel Session { get; }
    public InputService? Input { get; set; }
    public bool IsShellSession { get; }
    public ObservableCollection<BoardMessage> Messages { get; } = new();
    public int UnreadMessages { get; set; }

    public event EventHandler? ChannelsChanged;
    public event EventHandler? MessagesChanged;

    public static AppHost Create(CommandLineOptions options)
    {
        var host = new AppHost(options);
        Current = host;
        CurrentOrNull = host;

        if (!options.IsSnapshot)
        {
            host.Session.Begin();
            if (host.Session.PreviousSessionCrashed)
                host.PostMessage("Couchtop closed unexpectedly", "The last Couchtop session did not exit normally. Your channels and settings were kept.");
        }

        var cached = host.Discovery.LoadCache();
        if (cached is null && options.IsSnapshot) cached = host.Discovery.Discover();
        if (cached is not null)
        {
            var seed = ChannelSeeder.Apply(host.Layout.Layout, cached.Apps, host.Settings.Current.AutoAddNewApps);
            host.SaveLayout(raiseChanged: false);
            if (seed.BuiltInsAdded > 0 && !options.IsSnapshot)
                host.PostMessage("Coming soon: Couchtop Sports", "A Sports channel is now on your menu as a sneak peek: tennis, baseball, bowling, golf and boxing, built right into Couchtop. It's still being made and isn't playable yet.");
        }
        else if (!host.Layout.Layout.Seeded)
        {
            // Very first start without a discovery cache: show the built-in channels right away, but leave the
            // layout unseeded so the first background discovery performs the real (capped) first-run seed
            // instead of treating every installed app as "newly installed".
            foreach (var id in BuiltInChannels.All)
            {
                var builtIn = BuiltInChannels.Create(id);
                if (LayoutEditor.Find(host.Layout.Layout, builtIn.Id) is null) LayoutEditor.Place(host.Layout.Layout, builtIn);
            }
        }

        if (!options.IsSnapshot)
        {
            var settings = host.Settings.Current;
            _ = Task.Run(() => host.AudioLibrary.RemoveUnused(settings.CustomSounds.Values.Append(settings.CustomMusic).ToList()));
        }
        _ = host.Audio.InitializeAsync();
        return host;
    }

    public SafetyGateResult EvaluateSafetyGate() => SafetyGate.Evaluate(SafetyStore.Load(), Install, DateTimeOffset.Now);

    public void SaveLayout(bool raiseChanged = true)
    {
        try
        {
            Layout.Save(raiseChanged: false);
        }
        catch (Exception ex)
        {
            Log.Error("Could not save channels", ex);
            PostMessage("Could not save channels", ex.Message);
        }
        if (raiseChanged) ChannelsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SaveSettings()
    {
        try
        {
            Settings.Save();
        }
        catch (Exception ex)
        {
            Log.Error("Could not save settings", ex);
        }
    }

    public void PostMessagesChanged() => MessagesChanged?.Invoke(this, EventArgs.Empty);

    public void PostMessage(string title, string body)
    {
        void Add()
        {
            Messages.Insert(0, new BoardMessage(title, body, DateTime.Now));
            while (Messages.Count > 50) Messages.RemoveAt(Messages.Count - 1);
            UnreadMessages++;
            MessagesChanged?.Invoke(this, EventArgs.Empty);
        }
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) Add();
        else dispatcher.BeginInvoke(Add);
    }

    /// <summary>Runs full app discovery in the background and merges newly installed apps into the menu.</summary>
    public void RefreshChannelsInBackground(bool announce = true)
    {
        if (Options.IsSnapshot || Interlocked.Exchange(ref _discoveryRunning, 1) == 1) return;
        Task.Run(() =>
        {
            try
            {
                var result = Discovery.Discover();
                Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    var seed = ChannelSeeder.Apply(Layout.Layout, result.Apps, Settings.Current.AutoAddNewApps);
                    SaveLayout(raiseChanged: seed.Added > 0 || seed.FirstRun || seed.BuiltInsAdded > 0);
                    if (announce && seed.Added > 0 && !seed.FirstRun)
                        PostMessage("New channels", $"{seed.Added} newly installed app{(seed.Added == 1 ? " was" : "s were")} added to your channels.");
                });
            }
            catch (Exception ex)
            {
                Log.Error("Background discovery failed", ex);
            }
            finally
            {
                Interlocked.Exchange(ref _discoveryRunning, 0);
            }
        });
    }

    /// <summary>Tells the Guardian (if any) that the UI is up, then keeps a UI-thread heartbeat going.</summary>
    public void SignalReady()
    {
        if (Options.IsSnapshot || _ready is not null) return;
        try
        {
            _ready = InstanceSignals.Ready(Options.SignalSuffix);
            _heartbeat = InstanceSignals.Heartbeat(Options.SignalSuffix);
            _ready.Set();
            _heartbeat.Set();
            _heartbeatTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2) };
            _heartbeatTimer.Tick += (_, _) => _heartbeat?.Set();
            _heartbeatTimer.Start();
            Log.Info("Ready signaled");
        }
        catch (Exception ex)
        {
            Log.Warn("Could not signal readiness", ex);
        }
    }

    public void ApplyStartWithWindows()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (Settings.Current.StartWithWindows) key.SetValue("Couchtop", $"\"{Install.AppPath}\" --autostart");
            else key.DeleteValue("Couchtop", false);
        }
        catch (Exception ex)
        {
            Log.Warn("Could not update startup entry", ex);
        }
    }

    /// <summary>Emergency shortcut: Explorer first, shell mode off for next sign-in, then exit immediately.</summary>
    public void Emergency(string source)
    {
        Log.Warn("Emergency requested via " + source);
        try
        {
            new EmergencyService(Shell, new ExplorerController()).Execute();
        }
        catch (Exception ex)
        {
            Log.Error("Emergency handling failed", ex);
        }
        Exit(ExitCodes.Emergency);
    }

    /// <summary>Leaves Couchtop for the normal Windows desktop.</summary>
    public void ExitToWindows()
    {
        if (IsShellSession && !NativeMethods.IsExplorerShellRunning())
        {
            try { new ExplorerController().StartExplorer(); }
            catch (Exception ex) { Log.Error("Could not start Explorer", ex); }
        }
        Exit(IsShellSession ? ExitCodes.SwitchToExplorer : ExitCodes.Success);
    }

    public bool IsExiting { get; private set; }

    public void Exit(int code)
    {
        if (IsExiting) return;
        IsExiting = true;
        try
        {
            _heartbeatTimer?.Stop();
            SaveLayout(raiseChanged: false);
            if (!Options.IsSnapshot) Session.End(clean: code is ExitCodes.Success or ExitCodes.SwitchToExplorer or ExitCodes.Restart or ExitCodes.SignOut or ExitCodes.Emergency);
            Input?.Dispose();
            Audio.Dispose();
            Icons.Dispose();
        }
        catch (Exception ex)
        {
            Log.Warn("Error during exit", ex);
        }
        Log.Info($"Exiting with code {code}");
        Application.Current.Shutdown(code);
    }
}
