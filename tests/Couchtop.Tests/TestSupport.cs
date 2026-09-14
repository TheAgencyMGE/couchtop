using Couchtop.Core.Storage;

namespace Couchtop.Tests;

public sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "CouchtopTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string relative, string? contents = null)
    {
        var full = System.IO.Path.Combine(Path, relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        if (contents is not null) System.IO.File.WriteAllText(full, contents);
        return full;
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, true); } catch { }
    }
}

public static class RepoPaths
{
    public static string Root
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "global.json"))) dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("Repository root not found");
        }
    }

    public static string Configuration => AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}") ? "Release" : "Debug";

    public static string Output(string projectDir, string exeName) =>
        Path.Combine(Root, projectDir, "bin", Configuration, "net10.0-windows", exeName);

    public static string Guardian => Output(@"src\Couchtop.Guardian", "Couchtop.Guardian.exe");
    public static string Recovery => Output(@"src\Couchtop.Recovery", "Couchtop.Recovery.exe");
    public static string FakeApp => Output(@"tests\Couchtop.FakeShellApp", "Couchtop.FakeShellApp.exe");

    public static InstallLayout FakeLayout(string dir) => new(dir);
}
