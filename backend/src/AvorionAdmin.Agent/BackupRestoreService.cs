using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AvorionAdmin.Core.Abstractions;
using AvorionAdmin.Core.Configuration;
using AvorionAdmin.Core.Models;
using Microsoft.Extensions.Options;

namespace AvorionAdmin.Agent;

public sealed class BackupRestoreException(
    string code,
    string message,
    bool retryable = false,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string Code { get; } = code;
    public bool Retryable { get; } = retryable;
}

public interface IBackupRestoreService
{
    Task<BackupRestoreResult> RestoreAsync(
        string operationId,
        string backupId,
        Func<double, string, CancellationToken, Task> reportProgressAsync,
        CancellationToken cancellationToken = default);
}

public sealed class BackupRestoreService(
    BackupScanner scanner,
    IManagedServerControlService control,
    IUpdateRollbackPointService rollbackPoints,
    ManagedServerProcessRegistry registry,
    IOptions<ServerNodeOptions> options) : IBackupRestoreService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromMinutes(1);
    private readonly ServerNodeOptions _options = options.Value;
    private readonly SemaphoreSlim _restoreLock = new(1, 1);

    public async Task<BackupRestoreResult> RestoreAsync(
        string operationId,
        string backupId,
        Func<double, string, CancellationToken, Task> reportProgressAsync,
        CancellationToken cancellationToken = default)
    {
        await _restoreLock.WaitAsync(cancellationToken);
        var serverWasRunning = false;
        var restoreAttempted = false;
        var restoreCompleted = false;
        UpdateRollbackPointResult? safetyPoint = null;
        ManagedLaunchProfile? profile = null;
        try
        {
            await reportProgressAsync(2, "validating-backup", cancellationToken);
            var backup = await scanner.ResolveAvailableAsync(backupId, cancellationToken)
                ?? throw new BackupRestoreException("BACKUP_NOT_FOUND", "所选备份已不存在、不可读取或不属于当前 Galaxy");
            profile = ReadManagedProfile();
            var initialStatus = await control.GetStatusAsync(cancellationToken);
            if (initialStatus.Lifecycle is not (Lifecycle.Running or Lifecycle.Stopped))
                throw new BackupRestoreException("SERVER_STATE_UNKNOWN", "无法确认受管服务器状态，已拒绝恢复");
            var restartAfterRestore = initialStatus.Lifecycle == Lifecycle.Running;
            serverWasRunning = restartAfterRestore;

            if (restartAfterRestore)
            {
                await reportProgressAsync(8, "safely-stopping-server", cancellationToken);
                await control.ExecuteAsync("shutdown", Scale(reportProgressAsync, 8, 20), cancellationToken);
            }

            await reportProgressAsync(22, "creating-pre-restore-safety-point", cancellationToken);
            safetyPoint = await rollbackPoints.CreateAsync(operationId, Scale(reportProgressAsync, 22, 55), cancellationToken);

            await reportProgressAsync(57, "restoring-official-backup", cancellationToken);
            restoreAttempted = true;
            var restoredAt = await RunOfficialRestoreAsync(profile, backup.Path, Scale(reportProgressAsync, 57, 85), cancellationToken);
            restoreCompleted = true;

            int? processId = null;
            if (restartAfterRestore)
            {
                await reportProgressAsync(87, "restarting-restored-server", cancellationToken);
                var start = await control.ExecuteAsync("start", Scale(reportProgressAsync, 87, 99), cancellationToken);
                processId = start.ProcessId;
            }

            await reportProgressAsync(100, "backup-restore-completed", cancellationToken);
            return new BackupRestoreResult(
                backup.Record.BackupId,
                backup.Record.FileName,
                profile.GalaxyDirectory,
                safetyPoint.PointId,
                safetyPoint.StoragePath,
                restartAfterRestore,
                processId,
                restoredAt,
                [
                    "backup-id-resolved-inside-current-source",
                    "pre-restore-server-and-galaxy-safety-point-verified",
                    "official-avorion-backup-file-restore",
                    "restore-process-save-and-stop-confirmed",
                    restartAfterRestore ? "restored-server-rcon-authenticated" : "server-left-stopped-as-before"
                ]);
        }
        catch (Exception exception) when (exception is BackupRestoreException or ServerControlException or UpdateRollbackPointException or ServerInitializationException or IOException or UnauthorizedAccessException or JsonException)
        {
            var recoveryEvidence = "";
            if (restoreAttempted && !restoreCompleted && safetyPoint is not null && profile is not null)
            {
                try
                {
                    await reportProgressAsync(90, "recovering-pre-restore-galaxy", CancellationToken.None);
                    await RecoverGalaxyAsync(operationId, profile.GalaxyDirectory, safetyPoint, CancellationToken.None);
                    recoveryEvidence = "；已从恢复前安全点自动还原 Galaxy";
                }
                catch (Exception recoveryException)
                {
                    throw new BackupRestoreException(
                        "BACKUP_RESTORE_RECOVERY_FAILED",
                        "备份恢复失败，自动还原也未完成；服务器保持停止，请使用操作日志中的恢复前安全点手动恢复",
                        false,
                        new AggregateException(exception, recoveryException));
                }
            }
            if (serverWasRunning && !restoreCompleted)
            {
                try { await control.ExecuteAsync("start", (_, _, _) => Task.CompletedTask, CancellationToken.None); }
                catch { recoveryEvidence += "；原服务器未能自动重启"; }
            }
            throw new BackupRestoreException(
                "BACKUP_RESTORE_FAILED",
                $"备份恢复未完成{recoveryEvidence}；恢复前安全点已保留，请查看操作日志",
                true,
                exception);
        }
        finally
        {
            _restoreLock.Release();
        }
    }

    private static async Task RecoverGalaxyAsync(
        string operationId,
        string galaxyDirectory,
        UpdateRollbackPointResult safetyPoint,
        CancellationToken cancellationToken)
    {
        var canonicalGalaxy = Path.TrimEndingDirectorySeparator(Path.GetFullPath(galaxyDirectory));
        if (!canonicalGalaxy.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(safetyPoint.GalaxyDirectory)), StringComparison.OrdinalIgnoreCase))
            throw new BackupRestoreException("RECOVERY_TARGET_MISMATCH", "恢复前安全点与当前 Galaxy 路径不一致");
        var source = Path.Combine(Path.GetFullPath(safetyPoint.StoragePath), "galaxy");
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException(source);
        var parent = Directory.GetParent(canonicalGalaxy)?.FullName ?? throw new DirectoryNotFoundException(canonicalGalaxy);
        var safeOperationId = new string(operationId.Where(char.IsLetterOrDigit).ToArray());
        var staging = Path.Combine(parent, $".{Path.GetFileName(canonicalGalaxy)}.restore-recovery-{safeOperationId}");
        var failed = Path.Combine(parent, $".{Path.GetFileName(canonicalGalaxy)}.failed-restore-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{safeOperationId}");
        if (Directory.Exists(staging) || File.Exists(staging) || Directory.Exists(failed) || File.Exists(failed))
            throw new BackupRestoreException("RECOVERY_PATH_CONFLICT", "Galaxy 自动还原暂存路径发生冲突");
        Directory.CreateDirectory(staging);
        foreach (var sourcePath in Directory.EnumerateFileSystemEntries(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var attributes = File.GetAttributes(sourcePath);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new BackupRestoreException("RECOVERY_REPARSE_POINT_REJECTED", "恢复前安全点包含不允许的目录联接或符号链接");
            var relative = Path.GetRelativePath(source, sourcePath);
            var destination = Path.GetFullPath(Path.Combine(staging, relative));
            if (!destination.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(staging)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new BackupRestoreException("RECOVERY_PATH_INVALID", "恢复前安全点路径越出 Galaxy 范围");
            if (Directory.Exists(sourcePath))
            {
                Directory.CreateDirectory(destination);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using (var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
                await input.CopyToAsync(output, cancellationToken);
            await using var sourceStream = File.OpenRead(sourcePath);
            await using var destinationStream = File.OpenRead(destination);
            var sourceHash = await SHA256.HashDataAsync(sourceStream, cancellationToken);
            var destinationHash = await SHA256.HashDataAsync(destinationStream, cancellationToken);
            if (!CryptographicOperations.FixedTimeEquals(sourceHash, destinationHash))
                throw new BackupRestoreException("RECOVERY_COPY_VERIFICATION_FAILED", $"Galaxy 安全点文件复核失败：{relative}");
        }

        Directory.Move(canonicalGalaxy, failed);
        try { Directory.Move(staging, canonicalGalaxy); }
        catch
        {
            if (!Directory.Exists(canonicalGalaxy) && Directory.Exists(failed)) Directory.Move(failed, canonicalGalaxy);
            throw;
        }
    }

    private ManagedLaunchProfile ReadManagedProfile()
    {
        var path = Path.Combine(Path.GetFullPath(_options.DataDirectory), "managed", "server-launch-profile.json");
        try
        {
            var profile = JsonSerializer.Deserialize<ManagedLaunchProfile>(File.ReadAllBytes(path), JsonOptions)
                ?? throw new JsonException("empty profile");
            ManagedServerRuntime.ValidateProfile(profile, requireNewGalaxy: false);
            return profile;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ServerInitializationException)
        {
            throw new BackupRestoreException("MANAGED_PROFILE_INVALID", "受管启动档案不可用，已拒绝恢复", false, exception);
        }
    }

    private async Task<DateTimeOffset> RunOfficialRestoreAsync(
        ManagedLaunchProfile profile,
        string backupPath,
        Func<double, string, CancellationToken, Task> reportProgressAsync,
        CancellationToken cancellationToken)
    {
        var startInfo = ManagedServerRuntime.CreateStartInfo(profile, redirectStandardInput: true);
        startInfo.ArgumentList.Add("--backup-file");
        startInfo.ArgumentList.Add(backupPath);
        var existingLogLengths = Directory.Exists(profile.GalaxyDirectory)
            ? Directory.EnumerateFiles(profile.GalaxyDirectory, "serverlog*.txt", SearchOption.TopDirectoryOnly)
                .ToDictionary(path => Path.GetFullPath(path), path => new FileInfo(path).Length, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        if (!process.Start()) throw new BackupRestoreException("RESTORE_PROCESS_START_FAILED", "Windows 未能启动官方备份恢复进程", true);
        registry.Register(process);
        try
        {
            await reportProgressAsync(15, "waiting-for-official-restore", cancellationToken);
            await WaitForReadyLogAsync(process, profile.GalaxyDirectory, existingLogLengths, cancellationToken);
            await reportProgressAsync(70, "saving-restored-galaxy", cancellationToken);
            await process.StandardInput.WriteLineAsync("/save".AsMemory(), cancellationToken);
            await process.StandardInput.FlushAsync(cancellationToken);
            await Task.Delay(1000, cancellationToken);
            await process.StandardInput.WriteLineAsync("/stop".AsMemory(), cancellationToken);
            await process.StandardInput.FlushAsync(cancellationToken);
            await reportProgressAsync(85, "waiting-for-restore-process-exit", cancellationToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(StopTimeout);
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            { throw new BackupRestoreException("RESTORE_STOP_TIMEOUT", "官方恢复已启动，但未能确认恢复进程安全退出", true); }
            if (process.ExitCode != 0)
                throw new BackupRestoreException("RESTORE_PROCESS_FAILED", $"官方备份恢复进程退出码为 {process.ExitCode}", true);
            await reportProgressAsync(100, "official-restore-confirmed", cancellationToken);
            return DateTimeOffset.UtcNow;
        }
        finally
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(15)); }
                catch { }
            }
            if (process.HasExited) registry.Clear(process);
        }
    }

    private static async Task WaitForReadyLogAsync(
        Process process,
        string galaxyDirectory,
        IReadOnlyDictionary<string, long> existingLogLengths,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + ReadyTimeout;
        var startedAt = process.StartTime.ToUniversalTime().AddSeconds(-5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (process.HasExited)
                throw new BackupRestoreException("RESTORE_PROCESS_EXITED", $"官方备份恢复进程提前退出，退出码 {process.ExitCode}", true);
            foreach (var path in Directory.EnumerateFiles(galaxyDirectory, "serverlog*.txt", SearchOption.TopDirectoryOnly))
            {
                var canonicalPath = Path.GetFullPath(path);
                var info = new FileInfo(canonicalPath);
                if (info.LastWriteTimeUtc < startedAt) continue;
                var originalLength = existingLogLengths.GetValueOrDefault(canonicalPath, 0);
                if (info.Length <= originalLength) continue;
                await using var stream = new FileStream(canonicalPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
                stream.Seek(Math.Max(originalLength, stream.Length - 512 * 1024), SeekOrigin.Begin);
                using var reader = new StreamReader(stream, Encoding.UTF8, true, 4096, false);
                var text = await reader.ReadToEndAsync(cancellationToken);
                if (text.Contains("Server startup complete.", StringComparison.Ordinal)) return;
            }
            await Task.Delay(250, cancellationToken);
        }
        throw new BackupRestoreException("RESTORE_READY_TIMEOUT", "官方备份恢复未在时限内完成启动验证", true);
    }

    private static Func<double, string, CancellationToken, Task> Scale(
        Func<double, string, CancellationToken, Task> report,
        double start,
        double end) => (progress, step, token) => report(start + (end - start) * Math.Clamp(progress, 0, 100) / 100, step, token);
}
