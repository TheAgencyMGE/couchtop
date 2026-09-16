using System.IO.Compression;
using System.Runtime.InteropServices;
using Couchtop.Core.Diagnostics;
using Couchtop.Core.Native;

namespace Couchtop.Core.Files;

public static class FileFormat
{
    private static readonly string[] Units = { "bytes", "KB", "MB", "GB", "TB", "PB" };

    /// <summary>"3.4 MB" style sizes, rounded the way file managers do.</summary>
    public static string Size(long bytes)
    {
        if (bytes < 0) return "";
        if (bytes < 1024) return bytes == 1 ? "1 byte" : $"{bytes} bytes";
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return value >= 100 ? $"{value:0} {Units[unit]}" : value >= 10 ? $"{value:0.0} {Units[unit]}" : $"{value:0.00} {Units[unit]}";
    }

    public static string When(DateTime time) =>
        time.Date == DateTime.Today ? time.ToString("HH:mm") :
        time.Year == DateTime.Today.Year ? time.ToString("d MMM HH:mm") : time.ToString("d MMM yyyy");
}

/// <summary>Pure helpers behind copy, move and rename, kept out of the UI so they can be tested.</summary>
public static class FileOperations
{
    /// <summary>Adds " (2)", " (3)" … to a name until it is free, keeping the extension in place.</summary>
    public static string UniqueName(string name, Func<string, bool> exists)
    {
        if (!exists(name)) return name;
        var stem = Path.GetFileNameWithoutExtension(name);
        var extension = Path.GetExtension(name);
        for (var n = 2; n < 1000; n++)
        {
            var candidate = $"{stem} ({n}){extension}";
            if (!exists(candidate)) return candidate;
        }
        return $"{stem} ({Guid.NewGuid():N}){extension}";
    }

    /// <summary>True when the name is a usable file name (no separators, no reserved Windows names).</summary>
    public static bool IsValidName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 255) return false;
        if (name.Trim() != name || name.EndsWith('.')) return false;
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
        var stem = Path.GetFileNameWithoutExtension(name).ToUpperInvariant();
        string[] reserved = { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };
        return !reserved.Contains(stem);
    }

    /// <summary>Stops a folder being copied or moved into itself or into one of its own children.</summary>
    public static bool IsInsideItself(string source, string destinationFolder)
    {
        var from = Normalize(source);
        var to = Normalize(destinationFolder);
        return to.Equals(from, StringComparison.OrdinalIgnoreCase) ||
               to.StartsWith(from.EndsWith(Path.DirectorySeparatorChar) ? from : from + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    /// <summary>Copies a folder tree, reporting each item so the UI can show progress and stop early.</summary>
    public static void CopyDirectory(string source, string destination, CancellationToken token, Action<string>? progress = null)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            token.ThrowIfCancellationRequested();
            var target = Path.Combine(destination, Path.GetFileName(file));
            File.Copy(file, target, overwrite: false);
            progress?.Invoke(target);
        }
        foreach (var folder in Directory.EnumerateDirectories(source))
        {
            token.ThrowIfCancellationRequested();
            CopyDirectory(folder, Path.Combine(destination, Path.GetFileName(folder)), token, progress);
        }
    }
}

public enum ClipboardMode
{
    Copy,
    Move,
}

/// <summary>What the user cut or copied in Files, kept inside Couchtop so it survives view changes.</summary>
public sealed record FileClipboard(ClipboardMode Mode, IReadOnlyList<string> Paths);

public enum PlaceKind
{
    Folder,
    FixedDrive,
    RemovableDrive,
    NetworkDrive,
    OpticalDrive,
    RecycleBin,
    Network,
}

public sealed record Place(string Name, string? Path, PlaceKind Kind, string? Detail = null);

public static class Places
{
    /// <summary>The user's own folders, in the order a file manager usually lists them.</summary>
    public static List<Place> UserFolders()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var candidates = new (string Name, string Path)[]
        {
            ("Desktop", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)),
            ("Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
            ("Downloads", Path.Combine(profile, "Downloads")),
            ("Pictures", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)),
            ("Music", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)),
            ("Videos", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)),
        };
        return candidates
            .Where(c => !string.IsNullOrEmpty(c.Path) && Directory.Exists(c.Path))
            .Select(c => new Place(c.Name, c.Path, PlaceKind.Folder))
            .ToList();
    }

    /// <summary>Drives that are ready, including USB sticks, discs and mapped network drives.</summary>
    public static List<Place> Drives()
    {
        var places = new List<Place>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady) continue;
                var kind = drive.DriveType switch
                {
                    DriveType.Removable => PlaceKind.RemovableDrive,
                    DriveType.Network => PlaceKind.NetworkDrive,
                    DriveType.CDRom => PlaceKind.OpticalDrive,
                    _ => PlaceKind.FixedDrive,
                };
                var letter = drive.Name.TrimEnd(Path.DirectorySeparatorChar);
                var label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? DefaultLabel(kind) : drive.VolumeLabel;
                var detail = drive.TotalSize > 0 ? $"{FileFormat.Size(drive.AvailableFreeSpace)} free of {FileFormat.Size(drive.TotalSize)}" : null;
                places.Add(new Place($"{label} ({letter})", drive.RootDirectory.FullName, kind, detail));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A disconnected or unreadable drive is simply not listed.
            }
        }
        return places;
    }

    private static string DefaultLabel(PlaceKind kind) => kind switch
    {
        PlaceKind.RemovableDrive => "USB drive",
        PlaceKind.NetworkDrive => "Network drive",
        PlaceKind.OpticalDrive => "Disc drive",
        _ => "Local disk",
    };
}

/// <summary>Deleting to the Recycle Bin, and emptying it, through the Windows shell.</summary>
public static class RecycleBin
{
    private const uint FO_DELETE = 0x0003;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_WANTNUKEWARNING = 0x4000;
    private const uint SHERB_NOCONFIRMATION = 0x00000001;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHFileOperation(ref SHFILEOPSTRUCT op);
    [DllImport("shell32.dll")] private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? rootPath, uint flags);

    /// <summary>Moves files and folders to the Recycle Bin. Returns false when Windows refused.</summary>
    public static bool Delete(IEnumerable<string> paths, IntPtr owner = default)
    {
        var list = paths.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        if (list.Count == 0) return true;
        var op = new SHFILEOPSTRUCT
        {
            hwnd = owner,
            wFunc = FO_DELETE,
            pFrom = string.Join('\0', list) + "\0\0",
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_WANTNUKEWARNING,
        };
        var result = SHFileOperation(ref op);
        if (result != 0) Log.Warn($"Recycle Bin delete failed with code {result}");
        return result == 0 && !op.fAnyOperationsAborted;
    }

    public static bool Empty(IntPtr owner = default)
    {
        var result = SHEmptyRecycleBin(owner, null, SHERB_NOCONFIRMATION);
        // 0 = emptied, -2147418113 = already empty on some Windows builds.
        return result is 0 or unchecked((int)0x8000FFFF);
    }
}

/// <summary>Zip files: extracting one into a folder and making one from a selection.</summary>
public static class Archives
{
    public static readonly string[] Extensions = { ".zip" };

    public static bool IsArchive(string path) => Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>Extracts into a new folder named after the archive, and returns that folder.</summary>
    public static string ExtractHere(string archivePath, Func<string, bool> exists)
    {
        var parent = Path.GetDirectoryName(archivePath) ?? throw new IOException("The archive has no folder.");
        var name = FileOperations.UniqueName(Path.GetFileNameWithoutExtension(archivePath), candidate => exists(Path.Combine(parent, candidate)));
        var target = Path.Combine(parent, name);
        Directory.CreateDirectory(target);
        ZipFile.ExtractToDirectory(archivePath, target, overwriteFiles: false);
        return target;
    }

    /// <summary>Zips files and folders into one archive next to them, and returns it.</summary>
    public static string Compress(IReadOnlyList<string> paths, string folder, Func<string, bool> exists)
    {
        if (paths.Count == 0) throw new ArgumentException("Nothing to compress.", nameof(paths));
        var baseName = paths.Count == 1 ? Path.GetFileNameWithoutExtension(paths[0]) : new DirectoryInfo(folder).Name;
        if (string.IsNullOrWhiteSpace(baseName)) baseName = "Archive";
        var name = FileOperations.UniqueName(baseName + ".zip", candidate => exists(Path.Combine(folder, candidate)));
        var target = Path.Combine(folder, name);

        using var stream = File.Create(target);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                var root = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    var relative = Path.Combine(root, Path.GetRelativePath(path, file));
                    zip.CreateEntryFromFile(file, relative.Replace('\\', '/'));
                }
            }
            else if (File.Exists(path))
            {
                zip.CreateEntryFromFile(path, Path.GetFileName(path));
            }
        }
        return target;
    }
}

public sealed record ItemDetails(string Name, string Path, bool IsFolder, long Size, int Items, DateTime Created, DateTime Modified, FileAttributes Attributes, string? Owner, string? Target);

/// <summary>Reads the properties Files shows: size, dates, attributes, owner and shortcut target.</summary>
public static class ItemInspector
{
    public static ItemDetails Read(string path, CancellationToken token = default)
    {
        var isFolder = Directory.Exists(path);
        var info = isFolder ? new DirectoryInfo(path) : (FileSystemInfo)new FileInfo(path);
        long size = 0;
        var items = 0;
        if (isFolder)
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }))
            {
                token.ThrowIfCancellationRequested();
                items++;
                try
                {
                    size += new FileInfo(file).Length;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Skip files that vanish or refuse access while measuring.
                }
                if (items > 200000) break;
            }
        }
        else if (info is FileInfo file)
        {
            size = file.Length;
        }

        return new ItemDetails(
            string.IsNullOrEmpty(info.Name) ? path : info.Name,
            path,
            isFolder,
            size,
            items,
            info.CreationTime,
            info.LastWriteTime,
            info.Attributes,
            Owner(path),
            ShortcutTarget(path));
    }

    private static string? Owner(string path)
    {
        try
        {
            var security = System.IO.FileSystemAclExtensions.GetAccessControl(new FileInfo(path));
            return security.GetOwner(typeof(System.Security.Principal.NTAccount))?.Value;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException or NotSupportedException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Where a .lnk or .url points, read without the shell's COM interfaces.</summary>
    private static string? ShortcutTarget(string path)
    {
        try
        {
            var extension = Path.GetExtension(path);
            if (extension.Equals(".url", StringComparison.OrdinalIgnoreCase))
            {
                return File.ReadLines(path).FirstOrDefault(l => l.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))?[4..];
            }
            if (!extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)) return null;
            var link = ShellLink.Read(path);
            return string.IsNullOrWhiteSpace(link?.TargetPath) ? null : link!.TargetPath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
