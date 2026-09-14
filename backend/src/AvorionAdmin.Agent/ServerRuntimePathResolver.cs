using System.Text.Json;
using AvorionAdmin.Core.Abstractions;
using AvorionAdmin.Core.Configuration;
using AvorionAdmin.Core.Models;
using Microsoft.Extensions.Options;

namespace AvorionAdmin.Agent;

public sealed class ServerRuntimePathResolver(IOptions<ServerNodeOptions> options) : IServerRuntimePathResolver
{
    private const int MaximumProfileBytes = 64 * 1024;
    private const int MaximumServerIniBytes = 1024 * 1024;
    private readonly ServerNodeOptions _options = options.Value;

    public ResolvedLogSource? ResolveLogSource()
    {
        var explicitLogPath = ResolveExistingPath(_options.LogPath);
        if (explicitLogPath is not null) return new ResolvedLogSource(explicitLogPath, false);

        var configuredGalaxy = ResolveExistingDirectory(_options.GalaxyPath);
        if (configuredGalaxy is not null) return new ResolvedLogSource(configuredGalaxy, true);

        var managedGalaxy = ResolveManagedProfile()?.GalaxyDirectory;
        return managedGalaxy is null ? null : new ResolvedLogSource(managedGalaxy, true);
    }

    public ResolvedBackupSource? ResolveBackupSource()
    {
        var managedProfile = ResolveManagedProfile();
        var configuredGalaxy = ResolveExistingDirectory(_options.GalaxyPath) ?? managedProfile?.GalaxyDirectory;
        var galaxyName = configuredGalaxy is null
            ? null
            : Path.GetFileName(configuredGalaxy.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var explicitBackupPath = ResolveReadableDirectory(_options.BackupPath);
        if (explicitBackupPath is not null) return new ResolvedBackupSource(explicitBackupPath, "explicit", galaxyName);

        var galaxyDirectory = configuredGalaxy;
        if (galaxyDirectory is null) return null;

        var serverIniPath = Path.Combine(galaxyDirectory, "server.ini");
        var configuredPath = ReadBackupPath(serverIniPath);
        if (configuredPath is null) return null;
        if (configuredPath.Length > 0)
        {
            if (!Path.IsPathFullyQualified(configuredPath)) return null;
            var custom = ResolveReadableOrExpectedDirectory(configuredPath);
            return custom is null ? null : new ResolvedBackupSource(custom, "server-ini", galaxyName);
        }

        var applicationData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(applicationData)) return null;
        var defaultPath = ResolveReadableOrExpectedDirectory(Path.Combine(applicationData, "Avorion", "backups"));
        return defaultPath is null ? null : new ResolvedBackupSource(defaultPath, "avorion-default", galaxyName);
    }

    private ManagedLaunchProfile? ResolveManagedProfile()
    {
        try
        {
            var profilePath = Path.Combine(Path.GetFullPath(_options.DataDirectory), "managed", "server-launch-profile.json");
            var profileInfo = new FileInfo(profilePath);
            if (!profileInfo.Exists || profileInfo.Length is <= 0 or > MaximumProfileBytes) return null;

            var profile = JsonSerializer.Deserialize<ManagedLaunchProfile>(
                File.ReadAllBytes(profilePath),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (profile is null) return null;

            ManagedServerRuntime.ValidateProfile(profile, requireNewGalaxy: false);
            var galaxyDirectory = ResolveExistingDirectory(profile.GalaxyDirectory);
            return galaxyDirectory is null ? null : profile;
        }
        catch (Exception exception) when (exception is
            JsonException or IOException or UnauthorizedAccessException or ArgumentException or ServerInitializationException)
        {
            return null;
        }
    }

    private static string? ReadBackupPath(string serverIniPath)
    {
        try
        {
            var file = new FileInfo(serverIniPath);
            if (!file.Exists || file.Length is <= 0 or > MaximumServerIniBytes) return null;
            foreach (var rawLine in File.ReadLines(serverIniPath))
            {
                var line = rawLine.Trim();
                if (line.StartsWith('#') || line.StartsWith(';')) continue;
                var separator = line.IndexOf('=');
                if (separator < 0 || !line[..separator].Trim().Equals("backupsPath", StringComparison.OrdinalIgnoreCase)) continue;
                return line[(separator + 1)..].Trim().Trim('"');
            }
            return string.Empty;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? ResolveExistingPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            var canonical = Path.GetFullPath(path);
            return File.Exists(canonical) || Directory.Exists(canonical) ? canonical : null;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? ResolveExistingDirectory(string? path)
    {
        var resolved = ResolveExistingPath(path);
        return resolved is not null && Directory.Exists(resolved) ? resolved : null;
    }

    private static string? ResolveReadableDirectory(string? path)
    {
        var resolved = ResolveExistingDirectory(path);
        return resolved is not null && CanReadDirectory(resolved) ? resolved : null;
    }

    private static string? ResolveReadableOrExpectedDirectory(string path)
    {
        try
        {
            var canonical = Path.GetFullPath(path);
            return !Directory.Exists(canonical) || CanReadDirectory(canonical) ? canonical : null;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool CanReadDirectory(string path)
    {
        try
        {
            _ = Directory.EnumerateFileSystemEntries(path).Take(1).ToArray();
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
