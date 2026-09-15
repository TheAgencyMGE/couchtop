using Couchtop.Core.Diagnostics;

namespace Couchtop.Core.Audio;

/// <summary>A user-chosen audio file: <see cref="File"/> is Couchtop's copy in the custom audio folder, <see cref="Name"/> is what the user picked.</summary>
public sealed record CustomAudioFile(string File, string Name);

/// <summary>A menu sound the user can replace. <see cref="Id"/> matches the app's sound effect name.</summary>
public sealed record CustomSoundSlot(string Id, string Name, string Description);

public static class CustomSoundSlots
{
    public static readonly IReadOnlyList<CustomSoundSlot> All = new CustomSoundSlot[]
    {
        new("Startup", "Startup", "When Couchtop starts"),
        new("Hover", "Point", "Pointing at a channel or button"),
        new("Select", "Select", "Choosing something"),
        new("Back", "Back", "Going back"),
        new("Page", "Page turn", "Changing menu pages"),
        new("Launch", "Start", "Starting an app"),
        new("HomeOpen", "Quick Menu open", "Opening the Quick Menu"),
        new("HomeClose", "Quick Menu close", "Closing the Quick Menu"),
        new("Tick", "Tick", "Small changes"),
        new("Error", "Error", "When something can't be done"),
    };

    public static bool IsKnown(string? id) => id is not null && All.Any(s => s.Id == id);
}

public static class CustomAudio
{
    public const string MusicSlot = "Music";
    public const double MaxSoundSeconds = 5;
    public const long MaxMusicBytes = 300L * 1024 * 1024;
    public const long MaxSoundBytes = 25L * 1024 * 1024;

    public static readonly IReadOnlyList<string> Extensions = new[] { ".mp3", ".wav", ".m4a", ".aac", ".wma", ".flac" };

    public static string FileFilter => "Audio files (MP3, WAV, M4A, AAC, WMA, FLAC)|" + string.Join(";", Extensions.Select(e => "*" + e));

    public static bool IsSupported(string path) => Extensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>Throws a user-readable exception when a file can't be used for the slot.</summary>
    public static void EnsureAcceptable(string sourcePath, string slot)
    {
        if (!System.IO.File.Exists(sourcePath)) throw new FileNotFoundException("That file doesn't exist anymore.", sourcePath);
        if (!IsSupported(sourcePath)) throw new NotSupportedException("That type of file isn't supported. Use MP3, WAV, M4A, AAC, WMA or FLAC.");
        var limit = slot == MusicSlot ? MaxMusicBytes : MaxSoundBytes;
        if (new FileInfo(sourcePath).Length > limit) throw new InvalidDataException($"That file is too large (the limit is {limit / 1024 / 1024} MB).");
    }

    /// <summary>Keeps only plain file names, so settings can never point outside the custom audio folder.</summary>
    public static CustomAudioFile? Clean(CustomAudioFile? file)
    {
        if (file is null || string.IsNullOrWhiteSpace(file.File)) return null;
        var trimmed = file.File.Trim();
        if (trimmed != Path.GetFileName(trimmed) || trimmed.Contains("..") || !IsSupported(trimmed)) return null;
        var name = string.IsNullOrWhiteSpace(file.Name) ? Path.GetFileNameWithoutExtension(trimmed) : file.Name.Trim();
        return new CustomAudioFile(trimmed, name.Length > 120 ? name[..120] : name);
    }
}

/// <summary>Couchtop's private copies of user music and sounds, so moving or deleting the originals doesn't break anything.</summary>
public sealed class CustomAudioLibrary
{
    public CustomAudioLibrary(string directory)
    {
        Directory = directory;
    }

    public string Directory { get; }

    public string? PathFor(CustomAudioFile? file)
    {
        var clean = CustomAudio.Clean(file);
        if (clean is null) return null;
        var path = Path.Combine(Directory, clean.File);
        return System.IO.File.Exists(path) ? path : null;
    }

    public CustomAudioFile Import(string sourcePath, string slot)
    {
        CustomAudio.EnsureAcceptable(sourcePath, slot);
        System.IO.Directory.CreateDirectory(Directory);
        var prefix = new string(slot.Where(char.IsLetterOrDigit).ToArray());
        if (prefix.Length == 0) prefix = "audio";
        var file = $"{prefix}-{Guid.NewGuid():N}"[..(prefix.Length + 13)] + Path.GetExtension(sourcePath).ToLowerInvariant();
        System.IO.File.Copy(sourcePath, Path.Combine(Directory, file), overwrite: false);
        return new CustomAudioFile(file, Path.GetFileNameWithoutExtension(sourcePath));
    }

    public void Delete(CustomAudioFile? file)
    {
        var path = PathFor(file);
        if (path is null) return;
        try
        {
            System.IO.File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Still open (e.g. music playing); it is cleaned up on the next start.
            Log.Warn("Could not delete old custom audio " + path, ex);
        }
    }

    /// <summary>Deletes copies no setting refers to anymore. Returns how many were removed.</summary>
    public int RemoveUnused(IEnumerable<CustomAudioFile?> inUse)
    {
        if (!System.IO.Directory.Exists(Directory)) return 0;
        var keep = new HashSet<string>(inUse.Select(CustomAudio.Clean).Where(f => f is not null).Select(f => f!.File), StringComparer.OrdinalIgnoreCase);
        var removed = 0;
        foreach (var path in System.IO.Directory.EnumerateFiles(Directory))
        {
            if (keep.Contains(Path.GetFileName(path))) continue;
            try
            {
                System.IO.File.Delete(path);
                removed++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warn("Could not remove unused custom audio " + path, ex);
            }
        }
        return removed;
    }
}
