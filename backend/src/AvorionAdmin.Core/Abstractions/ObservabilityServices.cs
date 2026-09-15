using AvorionAdmin.Core.Models;

namespace AvorionAdmin.Core.Abstractions;

public interface IPerformanceStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task AppendAsync(PerformancePoint point, CancellationToken cancellationToken = default);
    Task<PerformanceHistory> QueryAsync(string range, CancellationToken cancellationToken = default);
    Task PruneAsync(int retentionDays, CancellationToken cancellationToken = default);
}

public interface IBackupScanner
{
    Task<IReadOnlyList<BackupRecord>> ScanAsync(CancellationToken cancellationToken = default);
}

public interface ILogReader
{
    Task<IReadOnlyList<LogEntry>> ReadRecentAsync(
        int limit,
        string? category = null,
        string? query = null,
        CancellationToken cancellationToken = default);
}

public interface IDiagnosticService
{
    Task<DiagnosticRun> RunAsync(string runId, CancellationToken cancellationToken = default);
}

public interface IFileSystemBrowser
{
    Task<FileSystemBrowseResult> BrowseDirectoriesAsync(
        string? path = null,
        CancellationToken cancellationToken = default);
}

public interface IMemoryPolicyStore
{
    Task<MemoryPolicy?> GetMemoryPolicyAsync(CancellationToken cancellationToken = default);
    Task<MemoryPolicy> SaveMemoryPolicyAsync(
        MemoryPolicyUpdateRequest request,
        CancellationToken cancellationToken = default);
}
