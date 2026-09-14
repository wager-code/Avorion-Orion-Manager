using System.Text.Json;

namespace AvorionAdmin.Core.Models;

public enum Lifecycle { Running, Stopped, Starting, Stopping, Restarting, Unknown }
public enum ConnectionState { Connected, Disconnected, Degraded, Unknown }
public enum DiagnosticState { Healthy, Warning, Error, Unknown }
public enum OperationState { Queued, Running, Succeeded, Failed, Cancelled }

public sealed record Provenance(
    DateTimeOffset SampledAt,
    string Source,
    string Freshness,
    string? UnavailableReason = null);

public sealed record ConnectionProbe(
    ConnectionState Status,
    double? LatencyMs,
    string? UnavailableReason = null);

public sealed record SteamQuerySnapshot(
    ConnectionProbe Connection,
    int? OnlinePlayers,
    int? MaxPlayers,
    string? ServerName,
    int? QueryPort,
    int? GamePort,
    string? EvidenceSource);

public sealed record LastSaveInfo(
    DateTimeOffset? At,
    string? Source,
    string? UnavailableReason = null);

public sealed record AgentInfo(bool Connected, DateTimeOffset? LastHeartbeatAt);

public sealed record ServerSummary(
    string ServerId,
    string Name,
    Lifecycle Lifecycle,
    bool AgentConnected);

public sealed record ServerStatus(
    string ServerId,
    string Name,
    Lifecycle Lifecycle,
    int? ProcessId,
    string? Version,
    string? GalaxyName,
    long? UptimeSeconds,
    int? OnlinePlayers,
    int? MaxPlayers,
    ConnectionProbe Rcon,
    ConnectionProbe SteamQuery,
    LastSaveInfo LastSave,
    AgentInfo Agent,
    Provenance Provenance);

public sealed record CpuMetrics(double? ProcessPercent, double? HostPercent);
public sealed record MemoryMetrics(long? WorkingSetBytes, long? PrivateBytes, long? HostTotalBytes);
public sealed record DiskMetrics(long? TotalBytes, long? UsedBytes, long? AvailableBytes, string? Volume);
public sealed record NetworkMetrics(string Scope, double? DownloadBytesPerSecond, double? UploadBytesPerSecond);

public sealed record PerformanceSnapshot(
    CpuMetrics Cpu,
    MemoryMetrics Memory,
    DiskMetrics Disk,
    NetworkMetrics Network,
    int? OnlinePlayers,
    long? UptimeSeconds,
    Provenance Provenance);

public sealed record PerformancePoint(
    DateTimeOffset At,
    double? CpuProcessPercent,
    long? MemoryWorkingSetBytes,
    double? NetworkDownloadBytesPerSecond,
    double? NetworkUploadBytesPerSecond,
    int? OnlinePlayers);

public sealed record PerformanceHistory(
    string Range,
    IReadOnlyList<PerformancePoint> Points,
    string? WarningCode);

public sealed record MemoryOverview(
    long? CurrentWorkingSetBytes,
    long? StartupBaselineBytes,
    long? GrowthBytes,
    double? GrowthBytesPerHour,
    long? WarningThresholdBytes,
    string AlertLevel,
    int? LoadedSectorCount,
    int? PlayerSectorCount,
    int? IdleSectorCount,
    Provenance Provenance);

public sealed record MemoryPolicy(
    long WarningThresholdBytes,
    bool Notify,
    bool TryUnloadIdleSectors,
    bool SafeRestartAtCritical,
    DateTimeOffset UpdatedAt);

public sealed record MemoryPolicyUpdateRequest(
    long WarningThresholdBytes,
    bool Notify,
    bool TryUnloadIdleSectors,
    bool SafeRestartAtCritical);

public sealed record SectorUnloadRequest(int X, int Y, string Confirmation);

public sealed record SectorUnloadResult(
    int X,
    int Y,
    bool Accepted,
    bool Unloaded,
    string Outcome,
    IReadOnlyList<string> Evidence,
    DateTimeOffset CompletedAt);

public sealed record UpdateStatus(
    bool SteamCmdAvailable,
    bool SteamCmdPathConfigured,
    string? CurrentVersion,
    string? LatestKnownVersion,
    bool? UpdateAvailable,
    DateTimeOffset? LastCheckedAt,
    Provenance Provenance);

public sealed record UpdateCheckResult(
    int AppId,
    string Branch,
    string SteamCmdPath,
    string ServerDirectory,
    string? CurrentVersion,
    string? CurrentBuildId,
    string LatestBuildId,
    bool? UpdateAvailable,
    IReadOnlyList<string> Evidence,
    DateTimeOffset CheckedAt);

public sealed record VerifiedServerFile(
    string Name,
    string Path,
    long SizeBytes,
    string Sha256,
    string? Version);

public sealed record UpdateVerificationResult(
    bool Valid,
    int AppId,
    string Branch,
    string ServerDirectory,
    string? InstalledBuildId,
    IReadOnlyList<VerifiedServerFile> Files,
    IReadOnlyList<string> Checks,
    DateTimeOffset VerifiedAt);

public sealed record UpdateRollbackPointResult(
    string PointId,
    string StoragePath,
    string ServerDirectory,
    string GalaxyDirectory,
    int ServerFileCount,
    int GalaxyFileCount,
    long TotalBytes,
    string ManifestSha256,
    string? InstalledBuildId,
    bool Verified,
    DateTimeOffset CreatedAt,
    DateTimeOffset VerifiedAt,
    IReadOnlyList<string> Evidence);

public sealed record UpdateRollbackPointAvailability(
    bool Available,
    UpdateRollbackPointResult? Point);

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

public sealed record AutomationTaskRecord(
    string TaskId,
    string Kind,
    string Name,
    string ScheduleKind,
    int? IntervalMinutes,
    int? DayOfWeek,
    string? LocalTime,
    string TimeZone,
    bool Enabled,
    DateTimeOffset NextRunAt,
    DateTimeOffset? LastRunAt,
    string? LastOperationId,
    OperationState? LastOperationStatus,
    string? LastErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record AutomationTaskUpsertRequest(
    string Kind,
    string ScheduleKind,
    int? IntervalMinutes,
    int? DayOfWeek,
    string? LocalTime,
    bool Enabled);

public sealed record AutomationTaskList(
    string TimeZone,
    IReadOnlyList<AutomationTaskRecord> Items);

public sealed record DueAutomationTask(
    AutomationTaskRecord Task,
    DateTimeOffset ScheduledAt);

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

public sealed record UpdateComponentValidation(
    string RequestedPath,
    string? ResolvedExecutablePath,
    bool Exists,
    bool Readable,
    string? Version,
    IReadOnlyList<string> Checks,
    IReadOnlyList<string> Issues);

public sealed record UpdateEnvironmentValidation(
    bool Valid,
    UpdateComponentValidation SteamCmd,
    UpdateComponentValidation Server,
    long? ServerDriveAvailableBytes,
    DateTimeOffset ValidatedAt);

public sealed record UpdateEnvironmentDetection(
    string Status,
    UpdateComponentValidation? SteamCmd,
    UpdateComponentValidation? Server,
    DateTimeOffset ScannedAt);

public sealed record UpdateEnvironmentConfiguration(
    string SteamCmdPath,
    string ServerDirectory,
    DateTimeOffset ValidatedAt);

public sealed record UpdateEnvironmentRequest(
    string SteamCmdPath,
    string ServerDirectory);

public sealed record ServerSetupDraftRequest(
    string ServerName,
    string GalaxyName,
    string GalaxyMode,
    int MaxPlayers,
    string GalaxyDirectory,
    string ListenAddress,
    int GamePort,
    int QueryPort,
    bool RconEnabled,
    int RconPort,
    bool AllowFirewallChange,
    bool InstallManagementMod);

public sealed record ServerSetupPreflightRequest(
    string ServerName,
    string GalaxyName,
    string GalaxyMode,
    int MaxPlayers,
    string GalaxyDirectory,
    string ListenAddress,
    int GamePort,
    int QueryPort,
    bool RconEnabled,
    int RconPort,
    string? RconPassword,
    bool AllowFirewallChange,
    bool InstallManagementMod);

public sealed record ServerSetupDraftConfiguration(
    string ServerName,
    string GalaxyName,
    string GalaxyMode,
    int MaxPlayers,
    string GalaxyDirectory,
    string ListenAddress,
    int GamePort,
    int QueryPort,
    bool RconEnabled,
    int RconPort,
    bool AllowFirewallChange,
    bool InstallManagementMod,
    DateTimeOffset UpdatedAt);

public sealed record ServerSetupValidationIssue(
    string Field,
    string Code,
    string Message,
    string Severity);

public sealed record PortAvailability(
    string Purpose,
    int Port,
    string Protocol,
    bool Available,
    string Evidence);

public sealed record ServerSetupPreflightResult(
    bool Valid,
    bool UpdateEnvironmentValid,
    bool GalaxyPathValid,
    bool RconPasswordAccepted,
    IReadOnlyList<PortAvailability> Ports,
    IReadOnlyList<ServerSetupValidationIssue> Issues,
    DateTimeOffset CheckedAt);

public sealed record ServerSetupApplicationResult(
    string Mode,
    string GalaxyDirectory,
    string LaunchProfilePath,
    string? ServerIniPath,
    bool ServerIniUpdated,
    bool RconConfigured,
    string ConfigurationSha256,
    IReadOnlyList<string> DeferredActions,
    DateTimeOffset AppliedAt);

public sealed record ManagedLaunchProfile(
    int SchemaVersion,
    string ExecutablePath,
    string WorkingDirectory,
    string GalaxyDirectory,
    string GalaxyMode,
    IReadOnlyList<string> Arguments,
    string RequestedGameListenAddress,
    int RequestedQueryPort,
    bool RconEnabled,
    string RconHost,
    int RconPort,
    bool FirewallChangeRequested,
    bool ManagementModRequested,
    DateTimeOffset CreatedAt);

public sealed record GalaxyInitializationEvidence(
    string ServerIniPath,
    bool SaveCommandSent,
    bool StopCommandSent,
    int ExitCode,
    DateTimeOffset CompletedAt);

public sealed record ManagedServerHealth(
    int ProcessId,
    DateTimeOffset StartedAt,
    bool RconAuthenticated,
    DateTimeOffset CheckedAt);

public sealed record ManagedServerActionResult(
    string Action,
    string Outcome,
    Lifecycle Lifecycle,
    int? ProcessId,
    bool RconAuthenticated,
    IReadOnlyList<string> Evidence,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt);

public sealed record ForceStopRequest(int ExpectedProcessId, string Confirmation);

public sealed record ServerInitializationResult(
    string Mode,
    int ProcessId,
    string GalaxyDirectory,
    string ServerIniPath,
    bool ConsoleSaveConfirmed,
    bool ConsoleStopConfirmed,
    bool RconConfigured,
    bool RconAuthenticated,
    IReadOnlyList<string> HealthEvidence,
    IReadOnlyList<string> DeferredActions,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt);

public sealed record SteamCmdInstallRequest(
    string InstallDirectory,
    string? PlannedServerDirectory = null);

public sealed record SteamCmdInstallationResult(
    string InstallDirectory,
    string ExecutablePath,
    string? SourceUrl,
    string? ArchiveSha256,
    long? ArchiveBytes,
    long ExecutableBytes,
    string Signer,
    DateTimeOffset? InstalledAt,
    bool ReusedExisting,
    string ExecutableSha256,
    DateTimeOffset VerifiedAt);

public sealed record AvorionServerInstallRequest(
    string SteamCmdPath,
    string InstallDirectory);

public sealed record AvorionServerInstallPlan(
    string SteamCmdPath,
    string InstallDirectory);

public sealed record AvorionServerInstallationResult(
    string InstallDirectory,
    string ExecutablePath,
    string RunnerPath,
    int AppId,
    string Branch,
    string SteamCmdSigner,
    long ExecutableBytes,
    DateTimeOffset? InstalledAt,
    bool ReusedExisting,
    string ExecutableSha256,
    string RunnerSha256,
    DateTimeOffset VerifiedAt);

public sealed record OperationAccepted(string OperationId, OperationState Status, DateTimeOffset AcceptedAt);

public sealed record OperationCreateResult(OperationRecord Operation, bool Created);

public sealed record OperationRecord(
    string OperationId,
    string Type,
    OperationState Status,
    double? ProgressPercent,
    string? CurrentStep,
    DateTimeOffset AcceptedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    JsonElement? Result,
    ApiErrorDetail? Error,
    JsonElement? Request = null);

public sealed record ApiErrorDetail(
    string Code,
    string Message,
    string RequestId,
    bool Retryable,
    IReadOnlyDictionary<string, object?>? Details = null);

public sealed record ApiErrorEnvelope(ApiErrorDetail Error);

public sealed record AdminSessionStatus(
    bool Authenticated,
    string Mode,
    string? CsrfToken,
    DateTimeOffset? ExpiresAt);

public sealed record CommandSafetyStatus(
    string Command,
    bool Verified,
    string Reason);

public sealed record WriteSafetyStatus(
    string DefaultPolicy,
    bool LocalSessionRequired,
    bool CsrfRequired,
    bool IdempotencyRequired,
    IReadOnlyList<CommandSafetyStatus> Commands);
