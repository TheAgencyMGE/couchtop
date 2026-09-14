using System.Security.Cryptography;
using Couchtop.Core.Storage;

namespace Couchtop.Core.Safety;

public static class ExitCodes
{
    public const int Success = 0;
    public const int FatalError = 1;
    public const int SwitchToExplorer = 10;
    public const int Restart = 11;
    public const int SignOut = 12;
    public const int CrashTest = 77;
    public const int Emergency = 99;
}

public static class SafetyStepIds
{
    public const string Compatibility = "compatibility";
    public const string RecoveryTool = "recovery-tool";
    public const string RecoveryScript = "recovery-script";
    public const string RegistryRoundTrip = "registry-roundtrip";
    public const string CrashRecovery = "crash-recovery";
    public const string HangRecovery = "hang-recovery";
    public const string BootLoopGuard = "bootloop-guard";
    public const string EmergencyHotkey = "emergency-hotkey";
    public const string NormalSessions = "normal-sessions";

    public static readonly IReadOnlyList<string> Required = new[]
    {
        Compatibility, RecoveryTool, RecoveryScript, RegistryRoundTrip, CrashRecovery, HangRecovery, BootLoopGuard, EmergencyHotkey, NormalSessions,
    };
}

public sealed class SafetyStepResult
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public bool Passed { get; set; }
    public string Detail { get; set; } = "";
}

public sealed class SafetyVerificationRecord
{
    public DateTimeOffset CompletedAt { get; set; }
    public string AppVersion { get; set; } = "";
    public string InstallDirectory { get; set; } = "";
    public string? AppSha256 { get; set; }
    public string? GuardianSha256 { get; set; }
    public string? RecoverySha256 { get; set; }
    public List<SafetyStepResult> Steps { get; set; } = new();
}

public sealed record SafetyGateResult(bool Passed, IReadOnlyList<string> Reasons);

public sealed class SafetyVerificationStore
{
    private readonly string _path;

    public SafetyVerificationStore(string path)
    {
        _path = path;
    }

    /// <summary>Returns null when missing or corrupt: an unreadable record always counts as "not verified".</summary>
    public SafetyVerificationRecord? Load()
    {
        var result = JsonStore.Load<SafetyVerificationRecord>(_path, () => null!, r => r.Steps is not null);
        return result.Source == LoadSource.Default ? null : result.Value;
    }

    public void Save(SafetyVerificationRecord record) => JsonStore.Save(_path, record);

    public void Clear()
    {
        foreach (var f in new[] { _path, _path + ".bak" })
            if (File.Exists(f)) File.Delete(f);
    }
}

public static class FileHash
{
    public static string? Sha256(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return Convert.ToHexString(SHA256.HashData(stream));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

public static class SafetyGate
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(14);

    public static SafetyGateResult Evaluate(SafetyVerificationRecord? record, InstallLayout layout, DateTimeOffset now, Func<string, string?>? hash = null)
    {
        hash ??= FileHash.Sha256;
        var reasons = new List<string>();
        if (record is null)
        {
            reasons.Add("The Shell Safety Test has never been completed on this installation.");
            return new SafetyGateResult(false, reasons);
        }

        foreach (var id in SafetyStepIds.Required)
        {
            var step = record.Steps.FirstOrDefault(s => s.Id == id);
            if (step is null) reasons.Add($"Safety step '{id}' was not run.");
            else if (!step.Passed) reasons.Add($"Safety step '{step.Title}' did not pass.");
        }

        if (record.CompletedAt > now.AddMinutes(5)) reasons.Add("The safety test record has a future timestamp.");
        if (now - record.CompletedAt > MaxAge) reasons.Add("The safety test is more than 14 days old. Run it again.");

        if (!string.Equals(Path.GetFullPath(record.InstallDirectory).TrimEnd('\\'), layout.Directory.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            reasons.Add("Couchtop has moved since the safety test. Run it again.");

        if (!Matches(record.GuardianSha256, hash(layout.GuardianPath)) || !Matches(record.RecoverySha256, hash(layout.RecoveryPath)) || !Matches(record.AppSha256, hash(layout.AppPath)))
            reasons.Add("Couchtop files changed (update or reinstall) since the safety test. Run it again.");

        return new SafetyGateResult(reasons.Count == 0, reasons);
    }

    private static bool Matches(string? recorded, string? current) =>
        recorded is not null && current is not null && string.Equals(recorded, current, StringComparison.OrdinalIgnoreCase);
}
