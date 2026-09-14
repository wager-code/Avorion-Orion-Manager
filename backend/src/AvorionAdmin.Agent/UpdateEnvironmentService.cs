using System.Diagnostics;
using System.Text.RegularExpressions;
using AvorionAdmin.Core.Abstractions;
using AvorionAdmin.Core.Configuration;
using AvorionAdmin.Core.Models;
using Microsoft.Extensions.Options;

namespace AvorionAdmin.Agent;

public sealed partial class UpdateEnvironmentService(IOptions<ServerNodeOptions> options) : IUpdateEnvironmentService
{
    private readonly ServerNodeOptions _options = options.Value;

    public Task<UpdateEnvironmentDetection> DetectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var steamCmd = SteamCmdCandidates().Select(ValidateSteamCmd).FirstOrDefault(item => item.Exists && item.Readable);
        var server = ServerDirectoryCandidates().Select(ValidateServer).FirstOrDefault(item => item.Exists && item.Readable);
        var status = steamCmd is not null && server is not null ? "complete" : steamCmd is not null || server is not null ? "partial" : "not-found";
        return Task.FromResult(new UpdateEnvironmentDetection(status, steamCmd, server, DateTimeOffset.UtcNow));
    }

    public Task<UpdateEnvironmentValidation> ValidateAsync(
        string steamCmdPath,
        string serverDirectory,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var steamCmd = ValidateSteamCmd(steamCmdPath);
        var server = ValidateServer(serverDirectory);
        return Task.FromResult(new UpdateEnvironmentValidation(
            steamCmd.Exists && steamCmd.Readable && server.Exists && server.Readable,
            steamCmd,
            server,
            ReadAvailableBytes(serverDirectory),
            DateTimeOffset.UtcNow));
    }

    private IEnumerable<string> SteamCmdCandidates()
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddFile(candidates, _options.SteamCmdPath);

        foreach (var root in ReadyDriveRoots())
        {
            AddFile(candidates, Path.Combine(root, "SteamCMD", "steamcmd.exe"));
            AddFile(candidates, Path.Combine(root, "steamcmd", "steamcmd.exe"));
            AddFile(candidates, Path.Combine(root, "AvorionAdmin", "tools", "steamcmd", "steamcmd.exe"));
        }

        foreach (var library in SteamLibraryRoots())
        {
            AddFile(candidates, Path.Combine(library, "steamcmd.exe"));
            AddFile(candidates, Path.Combine(library, "steamapps", "common", "SteamCMD", "steamcmd.exe"));
        }

        return candidates;
    }

    private IEnumerable<string> ServerDirectoryCandidates()
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(_options.ExecutablePath))
        {
            var executable = new FileInfo(Path.GetFullPath(_options.ExecutablePath));
            var directory = executable.Directory;
            if (directory?.Name.Equals("bin", StringComparison.OrdinalIgnoreCase) == true) directory = directory.Parent;
            AddDirectory(candidates, directory?.FullName);
        }

        foreach (var root in ReadyDriveRoots())
        {
            AddDirectory(candidates, Path.Combine(root, "AvorionServer"));
            AddDirectory(candidates, Path.Combine(root, "Avorion Dedicated Server"));
        }

        foreach (var library in SteamLibraryRoots())
        {
            AddDirectory(candidates, Path.Combine(library, "steamapps", "common", "AvorionServer"));
            AddDirectory(candidates, Path.Combine(library, "steamapps", "common", "Avorion Dedicated Server"));
        }

        return candidates;
    }

    private static UpdateComponentValidation ValidateSteamCmd(string path)
    {
        var issues = new List<string>();
        var checks = new List<string>();
        string requested;
        try { requested = Path.GetFullPath(path); }
        catch { return InvalidComponent(path, "路径格式无效"); }

        if (!Path.GetFileName(requested).Equals("steamcmd.exe", StringComparison.OrdinalIgnoreCase))
            issues.Add("必须指向 steamcmd.exe");

        var exists = File.Exists(requested);
        if (exists) checks.Add("文件存在"); else issues.Add("未找到 steamcmd.exe");
        var readable = exists && CanReadFile(requested);
        if (readable) checks.Add("文件可读取"); else if (exists) issues.Add("文件无法读取");
        return new UpdateComponentValidation(requested, exists ? requested : null, exists, readable, ReadVersion(requested), checks, issues);
    }

    private static UpdateComponentValidation ValidateServer(string path)
    {
        var issues = new List<string>();
        var checks = new List<string>();
        string requested;
        try { requested = Path.GetFullPath(path); }
        catch { return InvalidComponent(path, "路径格式无效"); }

        var directoryExists = Directory.Exists(requested);
        if (directoryExists) checks.Add("目录存在"); else issues.Add("服务端目录不存在");
        var executable = directoryExists ? ResolveServerExecutable(requested) : null;
        if (executable is not null) checks.Add("已找到 AvorionServer.exe"); else if (directoryExists) issues.Add("未找到 AvorionServer.exe");
        var readable = executable is not null && CanReadDirectory(requested) && CanReadFile(executable);
        if (readable) checks.Add("目录与程序可读取"); else if (executable is not null) issues.Add("目录或程序无法读取");
        return new UpdateComponentValidation(requested, executable, executable is not null, readable, ReadVersion(executable), checks, issues);
    }

    private static string? ResolveServerExecutable(string directory)
    {
        foreach (var path in new[] { Path.Combine(directory, "AvorionServer.exe"), Path.Combine(directory, "bin", "AvorionServer.exe") })
        {
            if (File.Exists(path)) return path;
        }
        return null;
    }

    private static UpdateComponentValidation InvalidComponent(string path, string issue) =>
        new(path, null, false, false, null, Array.Empty<string>(), new[] { issue });

    private static bool CanReadFile(string path)
    {
        try { using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); return stream.CanRead; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return false; }
    }

    private static bool CanReadDirectory(string path)
    {
        try { _ = Directory.EnumerateFileSystemEntries(path).Take(1).ToArray(); return true; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return false; }
    }

    private static string? ReadVersion(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            return string.IsNullOrWhiteSpace(info.FileVersion) ? info.ProductVersion : info.FileVersion;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return null; }
    }

    private static long? ReadAvailableBytes(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            return string.IsNullOrWhiteSpace(root) ? null : new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }

    private static IEnumerable<string> ReadyDriveRoots() => DriveInfo.GetDrives()
        .Where(drive => drive.IsReady && drive.DriveType is DriveType.Fixed or DriveType.Removable)
        .Select(drive => drive.RootDirectory.FullName);

    private static IEnumerable<string> SteamLibraryRoots()
    {
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (string.IsNullOrWhiteSpace(programFilesX86)) yield break;
        var steamRoot = Path.Combine(programFilesX86, "Steam");
        if (Directory.Exists(steamRoot)) yield return steamRoot;
        var file = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(file)) yield break;

        string text;
        try { text = File.ReadAllText(file); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { yield break; }
        foreach (Match match in SteamLibraryPathRegex().Matches(text))
        {
            var path = match.Groups[1].Value.Replace("\\\\", "\\");
            if (Directory.Exists(path)) yield return path;
        }
    }

    private static void AddFile(HashSet<string> candidates, string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try { candidates.Add(Path.GetFullPath(path)); } catch { }
    }

    private static void AddDirectory(HashSet<string> candidates, string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try { candidates.Add(Path.GetFullPath(path)); } catch { }
    }

    [GeneratedRegex("\\\"path\\\"\\s+\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase)]
    private static partial Regex SteamLibraryPathRegex();
}
