using Microsoft.Win32;

namespace Couchtop.Core.Shell;

public static class ShellRegistryPaths
{
    public const string WinlogonKey = @"Software\Microsoft\Windows NT\CurrentVersion\Winlogon";
    public const string ShellValue = "Shell";
    public const string PolicySystemKey = @"Software\Microsoft\Windows\CurrentVersion\Policies\System";
    public const string StateKey = @"Software\Couchtop\Shell";
    public const string SandboxRoot = @"Software\Couchtop\SafetyTest";
}

/// <summary>
/// Registry access that is structurally limited to HKEY_CURRENT_USER. Couchtop never writes HKLM.
/// </summary>
public interface IRegistryStore
{
    string? GetString(string subKey, string name);
    void SetString(string subKey, string name, string value);
    bool DeleteValue(string subKey, string name);
    bool CanWrite(string subKey);
}

public sealed class CurrentUserRegistryStore : IRegistryStore
{
    private readonly string? _sandboxPrefix;

    /// <param name="sandboxPrefix">When set, every path is redirected below this key (must be under Software\Couchtop).</param>
    public CurrentUserRegistryStore(string? sandboxPrefix = null)
    {
        if (sandboxPrefix is not null && !sandboxPrefix.StartsWith(@"Software\Couchtop", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Sandbox registry roots must live under HKCU\\Software\\Couchtop.", nameof(sandboxPrefix));
        _sandboxPrefix = sandboxPrefix?.TrimEnd('\\');
    }

    public bool IsSandboxed => _sandboxPrefix is not null;

    private string Map(string subKey) => _sandboxPrefix is null ? subKey : _sandboxPrefix + "\\" + subKey;

    public string? GetString(string subKey, string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(Map(subKey));
        return key?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString();
    }

    public void SetString(string subKey, string name, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(Map(subKey), true);
        key.SetValue(name, value, RegistryValueKind.String);
    }

    public bool DeleteValue(string subKey, string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(Map(subKey), true);
        if (key?.GetValue(name) is null) return false;
        key.DeleteValue(name, false);
        return true;
    }

    public bool CanWrite(string subKey)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(Map(subKey), true);
            if (key is not null) return true;
            if (_sandboxPrefix is null) return false;
            using var created = Registry.CurrentUser.CreateSubKey(Map(subKey), true);
            return created is not null;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return false;
        }
    }

    /// <summary>Deletes the whole sandbox tree. Refuses to do anything for a non-sandboxed store.</summary>
    public void DeleteSandbox()
    {
        if (_sandboxPrefix is null) throw new InvalidOperationException("Only sandboxed stores can be deleted.");
        Registry.CurrentUser.DeleteSubKeyTree(_sandboxPrefix, false);
    }
}

public sealed class MemoryRegistryStore : IRegistryStore
{
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

    public bool Writable { get; set; } = true;
    public int WriteCount { get; private set; }

    private static string K(string subKey, string name) => subKey + "|" + name;

    public string? GetString(string subKey, string name) => _values.TryGetValue(K(subKey, name), out var v) ? v : null;

    public void SetString(string subKey, string name, string value)
    {
        if (!Writable) throw new UnauthorizedAccessException("Registry is read-only in this test.");
        WriteCount++;
        _values[K(subKey, name)] = value;
    }

    public bool DeleteValue(string subKey, string name)
    {
        if (!Writable) throw new UnauthorizedAccessException("Registry is read-only in this test.");
        return _values.Remove(K(subKey, name));
    }

    public bool CanWrite(string subKey) => Writable;
}
