using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Couchtop.Core.Channels;
using Couchtop.Core.Diagnostics;
using Couchtop.Core.Discovery;
using Couchtop.Core.Native;

namespace Couchtop.Core.Launching;

public enum LaunchStatus
{
    Started,
    NotFound,
    Cancelled,
    Failed,
}

public sealed record LaunchResult(LaunchStatus Status, string Message, int? ProcessId = null, bool LaunchSpecUpdated = false)
{
    public bool Succeeded => Status == LaunchStatus.Started;
}

public interface IProcessStarter
{
    int? Start(ProcessStartInfo startInfo);
    int ActivatePackagedApp(string aumid, string? arguments);
}

public sealed class SystemProcessStarter : IProcessStarter
{
    public int? Start(ProcessStartInfo startInfo)
    {
        using var process = Process.Start(startInfo);
        return process?.Id;
    }

    public int ActivatePackagedApp(string aumid, string? arguments) => AppActivation.Activate(aumid, arguments);
}

public interface IChannelResolver
{
    LaunchSpec? Resolve(Channel channel);
}

/// <summary>Finds the new location of an app whose shortcut or executable moved (typically after an update).</summary>
public sealed class DiscoveryChannelResolver : IChannelResolver
{
    private readonly DiscoveryService _discovery;

    public DiscoveryChannelResolver(DiscoveryService discovery)
    {
        _discovery = discovery;
    }

    public LaunchSpec? Resolve(Channel channel)
    {
        var apps = _discovery.Discover().Apps;
        var match = apps.FirstOrDefault(a => !string.IsNullOrEmpty(channel.SourceKey) && string.Equals(a.SourceKey, channel.SourceKey, StringComparison.OrdinalIgnoreCase))
                    ?? apps.FirstOrDefault(a => a.Kind == channel.Kind && DiscoveryService.NormalizeTitle(a.Title) == DiscoveryService.NormalizeTitle(channel.Title));
        if (match is not null) Log.Info($"Re-resolved channel '{channel.Title}' via {match.SourceKey}");
        return match?.Launch.Clone();
    }
}

public sealed class AppLauncher
{
    private const int ErrorCancelled = 1223;
    private const int AppNotRegistered = unchecked((int)0x80270254);
    private const int PackageNotFound = unchecked((int)0x80073CF1);
    private static readonly string[] BlockedSchemes = { "javascript", "vbscript", "data" };

    private readonly IProcessStarter _starter;
    private readonly IChannelResolver? _resolver;
    private readonly Func<string, bool> _exists;

    public AppLauncher(IProcessStarter starter, IChannelResolver? resolver = null, Func<string, bool>? exists = null)
    {
        _starter = starter;
        _resolver = resolver;
        _exists = exists ?? (p => File.Exists(p) || Directory.Exists(p));
    }

    public LaunchResult Launch(Channel channel)
    {
        if (channel.Kind == ChannelKind.BuiltIn) return new LaunchResult(LaunchStatus.Failed, "Built-in channels open inside Couchtop.");
        var spec = channel.Launch;
        if (spec is null || spec.IsEmpty) return NotFound(channel);

        if (IsMissing(spec))
        {
            LaunchSpec? resolved = null;
            try
            {
                resolved = _resolver?.Resolve(channel);
            }
            catch (Exception ex)
            {
                Log.Warn("Channel re-resolution failed", ex);
            }
            if (resolved is not null && !resolved.IsEmpty && !IsMissing(resolved))
            {
                channel.Launch = resolved;
                return Start(channel.Title, resolved) with { LaunchSpecUpdated = true };
            }
            return NotFound(channel);
        }
        return Start(channel.Title, spec);
    }

    private static LaunchResult NotFound(Channel channel) =>
        new(LaunchStatus.NotFound, $"\"{channel.Title}\" could not be found. It may have been uninstalled or moved.");

    internal bool IsMissing(LaunchSpec spec)
    {
        if (!string.IsNullOrWhiteSpace(spec.Aumid)) return false;
        if (!string.IsNullOrWhiteSpace(spec.ShortcutPath))
        {
            if (_exists(spec.ShortcutPath)) return false;
            return string.IsNullOrWhiteSpace(spec.Path) ? string.IsNullOrWhiteSpace(spec.Uri) : PathMissing(spec.Path);
        }
        if (!string.IsNullOrWhiteSpace(spec.Path)) return PathMissing(spec.Path);
        return string.IsNullOrWhiteSpace(spec.Uri);
    }

    private bool PathMissing(string path)
    {
        var expanded = Environment.ExpandEnvironmentVariables(path);
        return Path.IsPathRooted(expanded) && !_exists(expanded);
    }

    private LaunchResult Start(string title, LaunchSpec spec)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(spec.Aumid))
            {
                var pid = _starter.ActivatePackagedApp(spec.Aumid, spec.Arguments);
                return new LaunchResult(LaunchStatus.Started, $"Started {title}", pid);
            }

            ProcessStartInfo psi;
            if (!string.IsNullOrWhiteSpace(spec.ShortcutPath) && _exists(spec.ShortcutPath))
            {
                psi = new ProcessStartInfo(spec.ShortcutPath) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(spec.ShortcutPath) ?? "" };
            }
            else if (!string.IsNullOrWhiteSpace(spec.Path))
            {
                var path = Environment.ExpandEnvironmentVariables(spec.Path);
                psi = new ProcessStartInfo(path, spec.Arguments ?? "") { UseShellExecute = true };
                var workDir = spec.WorkingDirectory ?? (Path.IsPathRooted(path) && !Directory.Exists(path) ? Path.GetDirectoryName(path) : null);
                if (!string.IsNullOrWhiteSpace(workDir) && Directory.Exists(workDir)) psi.WorkingDirectory = workDir;
            }
            else if (!string.IsNullOrWhiteSpace(spec.Uri))
            {
                if (!Uri.TryCreate(spec.Uri, UriKind.Absolute, out var uri) || BlockedSchemes.Contains(uri.Scheme, StringComparer.OrdinalIgnoreCase))
                    return new LaunchResult(LaunchStatus.Failed, "This channel's address is not valid.");
                psi = new ProcessStartInfo(spec.Uri) { UseShellExecute = true };
            }
            else
            {
                return new LaunchResult(LaunchStatus.NotFound, $"\"{title}\" has nothing to start.");
            }

            if (spec.RunAsAdministrator) psi.Verb = "runas";
            var id = _starter.Start(psi);
            Log.Info($"Launched '{title}' ({psi.FileName}) pid={id?.ToString() ?? "n/a"}");
            return new LaunchResult(LaunchStatus.Started, $"Started {title}", id);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return new LaunchResult(LaunchStatus.Cancelled, "Launch was cancelled.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode is 2 or 3)
        {
            return new LaunchResult(LaunchStatus.NotFound, $"\"{title}\" could not be found. It may have been uninstalled or moved.");
        }
        catch (COMException ex) when (ex.HResult is AppNotRegistered or PackageNotFound)
        {
            return new LaunchResult(LaunchStatus.NotFound, $"\"{title}\" is no longer installed.");
        }
        catch (Exception ex)
        {
            Log.Error($"Launching '{title}' failed", ex);
            return new LaunchResult(LaunchStatus.Failed, $"\"{title}\" could not be started: {ex.Message}");
        }
    }
}
