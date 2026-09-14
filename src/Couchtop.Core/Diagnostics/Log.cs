using System.Diagnostics;
using System.Text;

namespace Couchtop.Core.Diagnostics;

/// <summary>
/// Tiny local-only rolling file logger. Never throws and never sends data anywhere.
/// </summary>
public static class Log
{
    private const long MaxBytes = 1_000_000;
    private const int KeepFiles = 3;
    private static readonly object Gate = new();
    private static string? _directory;
    private static string _component = "couchtop";

    public static string? CurrentFile => _directory is null ? null : Path.Combine(_directory, _component + ".log");

    public static void Initialize(string directory, string component)
    {
        lock (Gate)
        {
            _directory = directory;
            _component = component;
        }
        Info($"--- {component} started (pid {Environment.ProcessId}, v{typeof(Log).Assembly.GetName().Version}) ---");
    }

    public static void Info(string message) => Write("INFO", message, null);
    public static void Warn(string message, Exception? ex = null) => Write("WARN", message, ex);
    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        var line = new StringBuilder()
            .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
            .Append(" [").Append(level).Append("] ")
            .Append(message);
        if (ex is not null) line.Append(" :: ").Append(ex.GetType().Name).Append(": ").Append(ex.Message).AppendLine().Append(ex.StackTrace);
        var text = line.AppendLine().ToString();
        Debug.Write(text);

        lock (Gate)
        {
            if (_directory is null) return;
            try
            {
                Directory.CreateDirectory(_directory);
                var file = Path.Combine(_directory, _component + ".log");
                var info = new FileInfo(file);
                if (info.Exists && info.Length > MaxBytes) Roll(file);
                File.AppendAllText(file, text, Encoding.UTF8);
            }
            catch
            {
                // Logging must never take the shell down.
            }
        }
    }

    private static void Roll(string file)
    {
        for (var i = KeepFiles - 1; i >= 1; i--)
        {
            var src = $"{file}.{i}";
            var dst = $"{file}.{i + 1}";
            if (File.Exists(src)) File.Move(src, dst, true);
        }
        File.Move(file, file + ".1", true);
    }
}
