namespace Couchtop.Core.Storage;

/// <summary>
/// All per-user file locations. Everything lives under %LOCALAPPDATA%\Couchtop (local only).
/// COUCHTOP_DATA_DIR overrides the root, which tests and VM experiments use.
/// </summary>
public sealed class AppPaths
{
    public const string DataDirEnvironmentVariable = "COUCHTOP_DATA_DIR";

    public AppPaths(string dataRoot)
    {
        DataRoot = Path.GetFullPath(dataRoot);
    }

    public string DataRoot { get; }
    public string SettingsFile => Path.Combine(DataRoot, "settings.json");
    public string LayoutFile => Path.Combine(DataRoot, "channels.json");
    public string SafetyFile => Path.Combine(DataRoot, "shell-safety.json");
    public string SessionFile => Path.Combine(DataRoot, "session.json");
    public string CacheDirectory => Path.Combine(DataRoot, "cache");
    public string DiscoveryCacheFile => Path.Combine(CacheDirectory, "discovery.json");
    public string IconCacheDirectory => Path.Combine(CacheDirectory, "icons");
    public string ArtDirectory => Path.Combine(DataRoot, "channel-art");
    public string LogDirectory => Path.Combine(DataRoot, "logs");
    public string WebViewDataDirectory => Path.Combine(DataRoot, "browser");
    public string SafetyTestDirectory => Path.Combine(DataRoot, "safety-test");

    public static AppPaths ForCurrentUser()
    {
        var overrideRoot = Environment.GetEnvironmentVariable(DataDirEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(overrideRoot)) return new AppPaths(overrideRoot);
        return new AppPaths(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Couchtop"));
    }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(DataRoot);
        Directory.CreateDirectory(CacheDirectory);
        Directory.CreateDirectory(IconCacheDirectory);
        Directory.CreateDirectory(ArtDirectory);
        Directory.CreateDirectory(LogDirectory);
    }

    public static string DefaultInstallDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Couchtop");
}

/// <summary>File names of the shipped executables, resolved relative to an application directory.</summary>
public sealed class InstallLayout
{
    public const string AppExeName = "Couchtop.exe";
    public const string GuardianExeName = "Couchtop.Guardian.exe";
    public const string RecoveryExeName = "Couchtop.Recovery.exe";
    public const string SetupExeName = "Couchtop.Setup.exe";
    public const string RecoveryScriptName = "Recover-Explorer.cmd";

    public InstallLayout(string directory)
    {
        Directory = Path.GetFullPath(directory);
    }

    public string Directory { get; }
    public string AppPath => Path.Combine(Directory, AppExeName);
    public string GuardianPath => Path.Combine(Directory, GuardianExeName);
    public string RecoveryPath => Path.Combine(Directory, RecoveryExeName);
    public string SetupPath => Path.Combine(Directory, SetupExeName);
    public string RecoveryScriptPath => Path.Combine(Directory, RecoveryScriptName);

    public static InstallLayout Current => new(AppContext.BaseDirectory);

    public bool IsInStandardInstallLocation()
    {
        var dir = Directory.TrimEnd('\\') + "\\";
        var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs") + "\\";
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) + "\\";
        return dir.StartsWith(local, StringComparison.OrdinalIgnoreCase) || dir.StartsWith(pf, StringComparison.OrdinalIgnoreCase);
    }
}
