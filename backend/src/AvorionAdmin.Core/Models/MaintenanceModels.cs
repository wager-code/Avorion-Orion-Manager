namespace AvorionAdmin.Core.Models;

public sealed record BackupRecord(
    string BackupId,
    string FileName,
    DateTimeOffset CreatedAt,
    long SizeBytes,
    string Status);

public sealed record BackupRestoreRequest(string Confirmation);

public sealed record BackupRestoreResult(
    string BackupId,
    string FileName,
    string GalaxyDirectory,
    string SafetyPointId,
    string SafetyPointPath,
    bool Restarted,
    int? ProcessId,
    DateTimeOffset RestoredAt,
    IReadOnlyList<string> Evidence);

public sealed record LogEntry(
    string Id,
    DateTimeOffset OccurredAt,
    string Category,
    string Message,
    string RawLine,
    string SourceFile,
    long SourceLineNumber,
    DateTimeOffset SourceLastWriteAt,
    double ParseConfidence);

public sealed record DiagnosticItem(
    string Key,
    DiagnosticState Status,
    string ResultCode,
    string Message,
    IReadOnlyDictionary<string, object?> Evidence,
    Provenance Provenance);

public sealed record DiagnosticRun(
    string RunId,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<DiagnosticItem> Items);

public sealed record FileSystemDirectoryEntry(
    string Name,
    string Path,
    bool IsDrive,
    long? AvailableBytes = null,
    long? TotalBytes = null);

public sealed record FileSystemBrowseResult(
    string? CurrentPath,
    string? ParentPath,
    bool IsRootView,
    IReadOnlyList<FileSystemDirectoryEntry> Items);
