using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AvorionAdmin.Core.Abstractions;
using AvorionAdmin.Core.Configuration;
using AvorionAdmin.Core.Models;
using Microsoft.Extensions.Options;

namespace AvorionAdmin.Agent;

public sealed class UpdateRollbackPointException(
    string code,
    string message,
    bool retryable = false,
    Exception? innerException = null,
    IReadOnlyDictionary<string, object?>? details = null) : Exception(message, innerException)
{
    public string Code { get; } = code;
    public bool Retryable { get; } = retryable;
    public IReadOnlyDictionary<string, object?>? Details { get; } = details;
}

public sealed partial class UpdateRollbackPointService(
    IUpdateEnvironmentStore environmentStore,
    IUpdateEnvironmentService environmentService,
    IManagedServerControlService controlService,
    IOptions<ServerNodeOptions> options) : IUpdateRollbackPointService
{
    private const int MaximumProfileBytes = 512 * 1024;
    private const int MaximumFiles = 100_000;
    private const long MaximumBytes = 100L * 1024 * 1024 * 1024;
    private const long MinimumFreeReserveBytes = 512L * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly ServerNodeOptions _options = options.Value;
    private readonly SemaphoreSlim _createLock = new(1, 1);

    public async Task<UpdateRollbackPointResult> CreateAsync(
        string operationId,
        Func<double, string, CancellationToken, Task> reportProgressAsync,
        CancellationToken cancellationToken = default)
    {
        await _createLock.WaitAsync(cancellationToken);
        string? stagingPath = null;
        var currentStage = "validating-stopped-server";
        try
        {
            await reportProgressAsync(2, "validating-stopped-server", cancellationToken);
            var status = await controlService.GetStatusAsync(cancellationToken);
            if (status.Lifecycle != Lifecycle.Stopped)
                throw new UpdateRollbackPointException("SERVER_MUST_BE_STOPPED", "创建更新回滚点前必须先安全停止服务器");

            var configuration = await environmentStore.GetAsync(cancellationToken)
                ?? throw new UpdateRollbackPointException("UPDATE_ENVIRONMENT_NOT_CONFIGURED", "请先设置并验证 SteamCMD 与 Avorion 服务端路径");
            var validation = await environmentService.ValidateAsync(
                configuration.SteamCmdPath,
                configuration.ServerDirectory,
                cancellationToken);
            if (!validation.Valid || string.IsNullOrWhiteSpace(validation.Server.ResolvedExecutablePath))
                throw new UpdateRollbackPointException("UPDATE_ENVIRONMENT_INVALID", "已保存的服务端路径当前不可用，请重新验证路径");

            var profile = ReadManagedProfile();
            ManagedServerRuntime.ValidateProfile(profile, requireNewGalaxy: false);
            var serverDirectory = NormalizeDirectory(configuration.ServerDirectory);
            var galaxyDirectory = NormalizeDirectory(profile.GalaxyDirectory);
            var profileExecutable = Path.GetFullPath(profile.ExecutablePath);
            var validatedExecutable = Path.GetFullPath(validation.Server.ResolvedExecutablePath);
            if (!profileExecutable.Equals(validatedExecutable, StringComparison.OrdinalIgnoreCase))
                throw new UpdateRollbackPointException("MANAGED_PROFILE_MISMATCH", "受管启动档案与已验证服务端路径不一致，请重新完成服务器配置");
            if (PathsOverlap(serverDirectory, galaxyDirectory))
                throw new UpdateRollbackPointException("ROLLBACK_SOURCE_OVERLAP", "服务端目录与 Galaxy 目录不能互相包含，无法安全建立独立回滚范围");

            EnsurePathChainHasNoReparsePoint(serverDirectory);
            EnsurePathChainHasNoReparsePoint(galaxyDirectory);
            currentStage = "inventorying-update-sources";
            await reportProgressAsync(5, currentStage, cancellationToken);
            var serverInventory = Inventory("server", serverDirectory, cancellationToken);
            var galaxyInventory = Inventory("galaxy", galaxyDirectory, cancellationToken);
            var files = serverInventory.Files.Concat(galaxyInventory.Files).ToArray();
            var totalBytes = files.Sum(file => file.SizeBytes);
            if (files.Length > MaximumFiles)
                throw new UpdateRollbackPointException("ROLLBACK_SOURCE_TOO_MANY_FILES", $"更新回滚范围包含 {files.Length} 个文件，超过安全上限 {MaximumFiles}");
            if (totalBytes > MaximumBytes)
                throw new UpdateRollbackPointException("ROLLBACK_SOURCE_TOO_LARGE", "更新回滚范围超过 100 GB 安全上限");

            var serverParent = Directory.GetParent(serverDirectory)?.FullName
                ?? throw new UpdateRollbackPointException("ROLLBACK_STORAGE_INVALID", "无法确定服务端安装目录的父目录");
            var rollbackRoot = Path.Combine(serverParent, ".avorion-admin-update-rollback");
            if (IsWithin(rollbackRoot, serverDirectory) || IsWithin(rollbackRoot, galaxyDirectory))
                throw new UpdateRollbackPointException("ROLLBACK_STORAGE_INVALID", "回滚点目录不能位于服务端或 Galaxy 源目录内部");
            EnsurePathChainHasNoReparsePoint(serverParent);
            EnsureFreeSpace(rollbackRoot, totalBytes);

            Directory.CreateDirectory(rollbackRoot);
            var pointId = $"rollback_{DateTimeOffset.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}";
            stagingPath = Path.Combine(rollbackRoot, $".{pointId}.staging");
            var finalPath = Path.Combine(rollbackRoot, pointId);
            Directory.CreateDirectory(stagingPath);
            Directory.CreateDirectory(Path.Combine(stagingPath, "server"));
            Directory.CreateDirectory(Path.Combine(stagingPath, "galaxy"));
            CreateEmptyDirectories(stagingPath, serverInventory.Directories);
            CreateEmptyDirectories(stagingPath, galaxyInventory.Directories);

            currentStage = "copying-update-safety-point";
            var manifestEntries = new List<RollbackManifestEntry>(files.Length);
            long copiedBytes = 0;
            var lastReported = -1;
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destination = SafeDestination(stagingPath, file.Scope, file.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                var hash = await CopyAndHashAsync(file, destination, cancellationToken);
                manifestEntries.Add(new RollbackManifestEntry(file.Scope, file.RelativePath, file.SizeBytes, hash, file.LastWriteTimeUtc));
                copiedBytes += file.SizeBytes;
                var percent = totalBytes == 0 ? 75 : 10 + (int)Math.Floor(65d * copiedBytes / totalBytes);
                if (percent > lastReported)
                {
                    lastReported = percent;
                    await reportProgressAsync(percent, currentStage, cancellationToken);
                }
            }

            var createdAt = DateTimeOffset.UtcNow;
            var manifest = new RollbackPointManifest(
                1,
                pointId,
                565060,
                "public",
                serverDirectory,
                galaxyDirectory,
                TryReadInstalledBuildId(serverDirectory),
                serverInventory.Files.Count,
                galaxyInventory.Files.Count,
                totalBytes,
                createdAt,
                null,
                manifestEntries);

            currentStage = "verifying-update-safety-point";
            await reportProgressAsync(80, currentStage, cancellationToken);
            var verifiedFiles = 0;
            foreach (var entry in manifestEntries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destination = SafeDestination(stagingPath, entry.Scope, entry.RelativePath);
                var info = new FileInfo(destination);
                if (!info.Exists || info.Length != entry.SizeBytes)
                    throw new UpdateRollbackPointException("ROLLBACK_VERIFICATION_FAILED", $"回滚点文件大小复核失败：{entry.RelativePath}");
                var hash = await HashFileAsync(destination, cancellationToken);
                if (!hash.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new UpdateRollbackPointException("ROLLBACK_VERIFICATION_FAILED", $"回滚点文件哈希复核失败：{entry.RelativePath}");
                verifiedFiles++;
                var percent = 80 + (int)Math.Floor(18d * verifiedFiles / Math.Max(1, manifestEntries.Count));
                if (percent > lastReported)
                {
                    lastReported = percent;
                    await reportProgressAsync(percent, currentStage, cancellationToken);
                }
            }

            currentStage = "writing-verified-manifest";
            await reportProgressAsync(99, currentStage, cancellationToken);
            var verifiedAt = DateTimeOffset.UtcNow;
            manifest = manifest with { VerifiedAt = verifiedAt };
            var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);
            var manifestSha256 = Convert.ToHexString(SHA256.HashData(manifestBytes));
            await File.WriteAllBytesAsync(Path.Combine(stagingPath, "manifest.json"), manifestBytes, cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(stagingPath, "manifest.sha256"), manifestSha256, Encoding.ASCII, cancellationToken);
            var storedManifestHash = (await File.ReadAllTextAsync(Path.Combine(stagingPath, "manifest.sha256"), cancellationToken)).Trim();
            var actualManifestHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(stagingPath, "manifest.json"), cancellationToken)));
            if (!storedManifestHash.Equals(actualManifestHash, StringComparison.OrdinalIgnoreCase))
                throw new UpdateRollbackPointException("ROLLBACK_MANIFEST_INVALID", "回滚点清单完整性复核失败");

            currentStage = "publishing-update-safety-point";
            await PublishDirectoryAsync(stagingPath, finalPath, cancellationToken);
            stagingPath = null;
            await reportProgressAsync(100, "update-safety-point-ready", cancellationToken);
            return ToResult(manifest, finalPath, manifestSha256, verifiedAt);
        }
        catch (UpdateRollbackPointException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new UpdateRollbackPointException(
                "ROLLBACK_POINT_CREATE_FAILED",
                "创建更新回滚点失败；原服务端与 Galaxy 文件未被修改",
                true,
                exception,
                new Dictionary<string, object?>
                {
                    ["stage"] = currentStage,
                    ["systemError"] = exception.Message,
                    ["exceptionType"] = exception.GetType().Name
                });
        }
        finally
        {
            if (stagingPath is not null) await DeleteExactStagingDirectoryAsync(stagingPath);
            _createLock.Release();
        }
    }

    public async Task<UpdateRollbackPointAvailability> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        var configuration = await environmentStore.GetAsync(cancellationToken);
        if (configuration is null) return new UpdateRollbackPointAvailability(false, null);
        string rollbackRoot;
        try
        {
            var serverDirectory = NormalizeDirectory(configuration.ServerDirectory);
            rollbackRoot = Path.Combine(Directory.GetParent(serverDirectory)?.FullName ?? string.Empty, ".avorion-admin-update-rollback");
        }
        catch
        {
            return new UpdateRollbackPointAvailability(false, null);
        }
        if (!Directory.Exists(rollbackRoot)) return new UpdateRollbackPointAvailability(false, null);

        foreach (var directory in new DirectoryInfo(rollbackRoot).EnumerateDirectories("rollback_*").OrderByDescending(item => item.Name))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var manifestPath = Path.Combine(directory.FullName, "manifest.json");
                var hashPath = Path.Combine(directory.FullName, "manifest.sha256");
                if (!File.Exists(manifestPath) || !File.Exists(hashPath)) continue;
                var bytes = await File.ReadAllBytesAsync(manifestPath, cancellationToken);
                var expected = (await File.ReadAllTextAsync(hashPath, cancellationToken)).Trim();
                var actual = Convert.ToHexString(SHA256.HashData(bytes));
                if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase)) continue;
                var manifest = JsonSerializer.Deserialize<RollbackPointManifest>(bytes, JsonOptions);
                if (manifest is null || manifest.SchemaVersion != 1 || !manifest.PointId.Equals(directory.Name, StringComparison.Ordinal)) continue;
                if (!NormalizeDirectory(manifest.ServerDirectory).Equals(NormalizeDirectory(configuration.ServerDirectory), StringComparison.OrdinalIgnoreCase)) continue;
                return new UpdateRollbackPointAvailability(true, ToResult(manifest, directory.FullName, actual, manifest.VerifiedAt ?? manifest.CreatedAt));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
            {
                // Ignore incomplete or tampered point directories; only a valid signed manifest is returned.
            }
        }
        return new UpdateRollbackPointAvailability(false, null);
    }

    private ManagedLaunchProfile ReadManagedProfile()
    {
        var profilePath = Path.Combine(Path.GetFullPath(_options.DataDirectory), "managed", "server-launch-profile.json");
        if (!File.Exists(profilePath))
            throw new UpdateRollbackPointException("MANAGED_PROFILE_MISSING", "未找到受管启动档案，请先完成服务器配置");
        var info = new FileInfo(profilePath);
        if (info.Length is <= 0 or > MaximumProfileBytes)
            throw new UpdateRollbackPointException("MANAGED_PROFILE_INVALID", "受管启动档案大小异常");
        try
        {
            return JsonSerializer.Deserialize<ManagedLaunchProfile>(File.ReadAllBytes(profilePath), JsonOptions)
                ?? throw new UpdateRollbackPointException("MANAGED_PROFILE_INVALID", "无法读取受管启动档案");
        }
        catch (JsonException exception)
        {
            throw new UpdateRollbackPointException("MANAGED_PROFILE_INVALID", "受管启动档案格式无效", false, exception);
        }
    }

    private static SourceInventory Inventory(string scope, string root, CancellationToken cancellationToken)
    {
        var files = new List<SourceFile>();
        var directories = new List<SourceDirectory>();
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(current))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new UpdateRollbackPointException("ROLLBACK_REPARSE_POINT_REJECTED", $"回滚范围包含符号链接或目录联接：{entry}");
                var relative = Path.GetRelativePath(root, entry);
                if (Directory.Exists(entry))
                {
                    directories.Add(new SourceDirectory(scope, relative));
                    pending.Push(entry);
                }
                else
                {
                    var info = new FileInfo(entry);
                    files.Add(new SourceFile(scope, entry, relative, info.Length, info.LastWriteTimeUtc));
                    if (files.Count > MaximumFiles)
                        throw new UpdateRollbackPointException("ROLLBACK_SOURCE_TOO_MANY_FILES", $"更新回滚范围超过 {MaximumFiles} 个文件的安全上限");
                }
            }
        }
        return new SourceInventory(files, directories);
    }

    private static async Task<string> CopyAndHashAsync(SourceFile source, string destination, CancellationToken cancellationToken)
    {
        var before = new FileInfo(source.FullPath);
        if (before.Length != source.SizeBytes || before.LastWriteTimeUtc != source.LastWriteTimeUtc)
            throw new UpdateRollbackPointException("ROLLBACK_SOURCE_CHANGED", $"建立回滚点期间源文件发生变化：{source.RelativePath}", true);
        await using var input = new FileStream(source.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1024 * 1024];
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            hash.AppendData(buffer, 0, read);
        }
        await output.FlushAsync(cancellationToken);
        var after = new FileInfo(source.FullPath);
        if (after.Length != source.SizeBytes || after.LastWriteTimeUtc != source.LastWriteTimeUtc)
            throw new UpdateRollbackPointException("ROLLBACK_SOURCE_CHANGED", $"建立回滚点期间源文件发生变化：{source.RelativePath}", true);
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }

    private static void CreateEmptyDirectories(string stagingPath, IEnumerable<SourceDirectory> directories)
    {
        foreach (var directory in directories)
            Directory.CreateDirectory(SafeDestination(stagingPath, directory.Scope, directory.RelativePath));
    }

    private static string SafeDestination(string stagingPath, string scope, string relativePath)
    {
        if (Path.IsPathRooted(relativePath)) throw new UpdateRollbackPointException("ROLLBACK_PATH_INVALID", "回滚清单包含绝对路径");
        var scopeRoot = Path.GetFullPath(Path.Combine(stagingPath, scope));
        var destination = Path.GetFullPath(Path.Combine(scopeRoot, relativePath));
        if (!IsWithin(destination, scopeRoot)) throw new UpdateRollbackPointException("ROLLBACK_PATH_INVALID", "回滚清单路径越出安全范围");
        return destination;
    }

    private static string NormalizeDirectory(string path)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (!Directory.Exists(full)) throw new DirectoryNotFoundException(full);
        return full;
    }

    private static bool PathsOverlap(string first, string second) =>
        IsWithin(first, second) || IsWithin(second, first);

    private static bool IsWithin(string candidate, string root)
    {
        var normalizedCandidate = Path.GetFullPath(candidate);
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        return normalizedCandidate.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
            normalizedCandidate.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsurePathChainHasNoReparsePoint(string path)
    {
        for (var current = new DirectoryInfo(Path.GetFullPath(path)); current is not null; current = current.Parent)
        {
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new UpdateRollbackPointException("ROLLBACK_REPARSE_POINT_REJECTED", $"安全路径不能经过符号链接或目录联接：{current.FullName}");
        }
    }

    private static void EnsureFreeSpace(string rollbackRoot, long sourceBytes)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(rollbackRoot));
        if (string.IsNullOrWhiteSpace(root)) throw new UpdateRollbackPointException("ROLLBACK_STORAGE_INVALID", "无法确定回滚点所在磁盘");
        var required = checked(sourceBytes + Math.Max(MinimumFreeReserveBytes, sourceBytes / 10));
        if (new DriveInfo(root).AvailableFreeSpace < required)
            throw new UpdateRollbackPointException("ROLLBACK_DISK_SPACE_INSUFFICIENT", "磁盘空间不足，无法创建并完整校验更新回滚点");
    }

    private static string? TryReadInstalledBuildId(string serverDirectory)
    {
        var path = Path.Combine(serverDirectory, "steamapps", "appmanifest_565060.acf");
        if (!File.Exists(path)) return null;
        try
        {
            var match = BuildIdRegex().Match(File.ReadAllText(path));
            return match.Success ? match.Groups[1].Value : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static UpdateRollbackPointResult ToResult(RollbackPointManifest manifest, string path, string hash, DateTimeOffset verifiedAt) =>
        new(
            manifest.PointId,
            path,
            manifest.ServerDirectory,
            manifest.GalaxyDirectory,
            manifest.ServerFileCount,
            manifest.GalaxyFileCount,
            manifest.TotalBytes,
            hash,
            manifest.InstalledBuildId,
            true,
            manifest.CreatedAt,
            verifiedAt,
            [
                "server-stopped-before-copy",
                "server-and-galaxy-copied",
                "source-reparse-points-rejected",
                "all-file-sha256-verified",
                "manifest-sha256-verified",
                "staging-directory-atomically-published"
            ]);

    private static async Task PublishDirectoryAsync(string stagingPath, string finalPath, CancellationToken cancellationToken)
    {
        var delays = new[]
        {
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(250),
            TimeSpan.FromMilliseconds(500),
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(4),
            TimeSpan.FromSeconds(8)
        };
        Exception? lastException = null;
        for (var attempt = 0; attempt < delays.Length; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (attempt > 0) await Task.Delay(delays[attempt], cancellationToken);
            if (Directory.Exists(finalPath) || File.Exists(finalPath))
                throw new UpdateRollbackPointException("ROLLBACK_POINT_TARGET_EXISTS", "发布期间回滚点目标路径被创建，已停止以避免覆盖");
            try
            {
                Directory.Move(stagingPath, finalPath);
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                lastException = exception;
            }
        }

        throw new UpdateRollbackPointException(
            "ROLLBACK_POINT_PUBLISH_LOCKED",
            "安全点已经复制并校验，但 Windows 持续占用暂存目录，无法原子发布；请关闭可能扫描该目录的程序后重试",
            true,
            lastException,
            new Dictionary<string, object?>
            {
                ["stage"] = "publishing-update-safety-point",
                ["systemError"] = lastException?.Message,
                ["exceptionType"] = lastException?.GetType().Name
            });
    }

    private static async Task DeleteExactStagingDirectoryAsync(string stagingPath)
    {
        var info = new DirectoryInfo(Path.GetFullPath(stagingPath));
        if (!info.Name.StartsWith(".rollback_", StringComparison.Ordinal) || !info.Name.EndsWith(".staging", StringComparison.Ordinal)) return;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                info.Refresh();
                if (!info.Exists) return;
                info.Delete(true);
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                if (attempt == 4) return;
                await Task.Delay(TimeSpan.FromMilliseconds(250 * (attempt + 1)));
            }
        }
    }

    [GeneratedRegex("\\\"buildid\\\"\\s+\\\"([0-9]+)\\\"", RegexOptions.IgnoreCase)]
    private static partial Regex BuildIdRegex();

    private sealed record SourceInventory(IReadOnlyList<SourceFile> Files, IReadOnlyList<SourceDirectory> Directories);
    private sealed record SourceFile(string Scope, string FullPath, string RelativePath, long SizeBytes, DateTime LastWriteTimeUtc);
    private sealed record SourceDirectory(string Scope, string RelativePath);
    private sealed record RollbackManifestEntry(string Scope, string RelativePath, long SizeBytes, string Sha256, DateTime LastWriteTimeUtc);
    private sealed record RollbackPointManifest(
        int SchemaVersion,
        string PointId,
        int AppId,
        string Branch,
        string ServerDirectory,
        string GalaxyDirectory,
        string? InstalledBuildId,
        int ServerFileCount,
        int GalaxyFileCount,
        long TotalBytes,
        DateTimeOffset CreatedAt,
        DateTimeOffset? VerifiedAt,
        IReadOnlyList<RollbackManifestEntry> Files);
}
