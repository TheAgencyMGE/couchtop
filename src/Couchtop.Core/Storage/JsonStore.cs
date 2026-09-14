using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Couchtop.Core.Diagnostics;

namespace Couchtop.Core.Storage;

public static class AtomicFile
{
    /// <summary>
    /// Writes to a temp file, flushes to disk, then swaps it in. The previous good version is kept as .bak,
    /// so a power cut mid-write can never leave a half-written settings file as the only copy.
    /// </summary>
    public static void WriteAllText(string path, string contents)
    {
        path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        var bytes = new UTF8Encoding(false).GetBytes(contents);
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            fs.Write(bytes);
            fs.Flush(true);
        }

        if (!File.Exists(path))
        {
            File.Move(tmp, path);
            return;
        }

        try
        {
            File.Replace(tmp, path, path + ".bak", ignoreMetadataErrors: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            File.Copy(path, path + ".bak", true);
            File.Move(tmp, path, true);
        }
    }
}

public enum LoadSource
{
    Primary,
    Backup,
    Default,
}

public sealed record LoadResult<T>(T Value, LoadSource Source, IReadOnlyList<string> Issues);

public static class JsonStore
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static LoadResult<T> Load<T>(string path, Func<T> createDefault, Func<T, bool>? validate = null) where T : class
    {
        var issues = new List<string>();
        if (TryRead(path, validate, out var primary, out var error)) return new LoadResult<T>(primary!, LoadSource.Primary, issues);

        if (File.Exists(path))
        {
            issues.Add($"'{Path.GetFileName(path)}' was unreadable ({error}); it was set aside.");
            Quarantine(path);
        }

        var backup = path + ".bak";
        if (TryRead(backup, validate, out var restored, out var backupError))
        {
            issues.Add("Settings were restored from the last good backup.");
            try
            {
                AtomicFile.WriteAllText(path, File.ReadAllText(backup));
            }
            catch (Exception ex)
            {
                Log.Warn("Could not rewrite primary file from backup", ex);
            }
            return new LoadResult<T>(restored!, LoadSource.Backup, issues);
        }
        if (File.Exists(backup)) issues.Add($"Backup was also unreadable ({backupError}).");

        foreach (var issue in issues) Log.Warn(issue);
        return new LoadResult<T>(createDefault(), LoadSource.Default, issues);
    }

    public static void Save<T>(string path, T value) => AtomicFile.WriteAllText(path, JsonSerializer.Serialize(value, Options));

    private static bool TryRead<T>(string path, Func<T, bool>? validate, out T? value, out string? error) where T : class
    {
        value = null;
        error = null;
        if (!File.Exists(path)) return false;
        try
        {
            var text = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(text) || text.Trim('\0').Length == 0)
            {
                error = "file is empty";
                return false;
            }
            value = JsonSerializer.Deserialize<T>(text, Options);
            if (value is null)
            {
                error = "file contains null";
                return false;
            }
            if (validate is not null && !validate(value))
            {
                error = "content failed validation";
                value = null;
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            value = null;
            return false;
        }
    }

    private static void Quarantine(string path)
    {
        try
        {
            File.Move(path, $"{path}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss-fff}", true);
            var dir = Path.GetDirectoryName(path)!;
            var old = Directory.GetFiles(dir, Path.GetFileName(path) + ".corrupt-*").OrderByDescending(f => f).Skip(5);
            foreach (var f in old) File.Delete(f);
        }
        catch (Exception ex)
        {
            Log.Warn("Could not quarantine corrupt file", ex);
        }
    }
}
