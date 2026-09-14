using AvorionAdmin.Core.Models;

namespace AvorionAdmin.Core.Abstractions;

public sealed class IdempotencyConflictException(string message) : Exception(message);

public interface IServerProbe
{
    Task<ServerStatus> GetStatusAsync(CancellationToken cancellationToken = default);
    Task<PerformanceSnapshot> GetPerformanceAsync(CancellationToken cancellationToken = default);
    Task<UpdateStatus> GetUpdateStatusAsync(CancellationToken cancellationToken = default);
}

public interface ISteamQueryProbe
{
    Task<SteamQuerySnapshot> ProbeAsync(bool serverRunning, CancellationToken cancellationToken = default);
}

public interface IPlayerRewardService
{
    Task<PlayerRewardResult> GrantAsync(
        int playerIndex,
        PlayerRewardGrant grant,
        CancellationToken cancellationToken = default);
}

public interface IAllianceRewardService
{
    Task<AllianceRewardResult> GrantAllianceAsync(
        int allianceIndex,
        AllianceRewardGrant grant,
        CancellationToken cancellationToken = default);
}

public interface IPlayerMailService
{
    Task<PlayerMailDeliveryResult> SendMailAsync(
        int playerIndex,
        PlayerRewardGrant grant,
        RewardMailMessage mail,
        string deliveryId,
        CancellationToken cancellationToken = default);
}

public interface IInventoryService
{
    Task<InventorySnapshot> QueryInventoryAsync(
        string ownerKind,
        int ownerIndex,
        int offset,
        int limit,
        CancellationToken cancellationToken = default);

    Task<SystemUpgradeGrantResult> GrantSystemUpgradeAsync(
        string ownerKind,
        int ownerIndex,
        string upgradeKey,
        string rarity,
        string seed,
        CancellationToken cancellationToken = default);
}

public interface IInventoryCatalogService
{
    Task<InventoryCatalogPage> QueryAsync(
        InventoryCatalogQuery query,
        CancellationToken cancellationToken = default);

    Task<InventoryIconFile?> ReadIconAsync(
        string iconPath,
        CancellationToken cancellationToken = default);
}

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

public sealed record ResolvedLogSource(string Path, bool IsGalaxyRoot);
public sealed record ResolvedBackupSource(string Path, string Origin, string? GalaxyName);

public interface IServerRuntimePathResolver
{
    ResolvedLogSource? ResolveLogSource();
    ResolvedBackupSource? ResolveBackupSource();
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

public interface IUpdateEnvironmentService
{
    Task<UpdateEnvironmentDetection> DetectAsync(CancellationToken cancellationToken = default);
    Task<UpdateEnvironmentValidation> ValidateAsync(
        string steamCmdPath,
        string serverDirectory,
        CancellationToken cancellationToken = default);
}

public interface IUpdateEnvironmentStore
{
    Task<UpdateEnvironmentConfiguration?> GetAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(UpdateEnvironmentConfiguration configuration, CancellationToken cancellationToken = default);
}

public interface IUpdateInspectionService
{
    Task<UpdateCheckResult> CheckAsync(
        Func<double, string, CancellationToken, Task> reportProgressAsync,
        CancellationToken cancellationToken = default);
    Task<UpdateVerificationResult> VerifyLocalAsync(
        Func<double, string, CancellationToken, Task> reportProgressAsync,
        CancellationToken cancellationToken = default);
}

public interface IUpdateRollbackPointService
{
    Task<UpdateRollbackPointResult> CreateAsync(
        string operationId,
        Func<double, string, CancellationToken, Task> reportProgressAsync,
        CancellationToken cancellationToken = default);
    Task<UpdateRollbackPointAvailability> GetLatestAsync(CancellationToken cancellationToken = default);
}

public interface IServerSetupDraftStore
{
    Task<ServerSetupDraftConfiguration?> GetAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(ServerSetupDraftConfiguration configuration, CancellationToken cancellationToken = default);
}

public interface IServerSetupPreflightService
{
    Task<ServerSetupPreflightResult> ValidateAsync(
        ServerSetupPreflightRequest request,
        CancellationToken cancellationToken = default);
}

public interface IServerSetupApplicationService
{
    Task<ServerSetupApplicationResult> ApplyAsync(
        string operationId,
        ServerSetupPreflightRequest request,
        Func<double, string, CancellationToken, Task> reportProgressAsync,
        CancellationToken cancellationToken = default);
}

public interface IManagedServerRuntime
{
    Task<GalaxyInitializationEvidence> InitializeGalaxyAsync(
        ManagedLaunchProfile profile,
        Func<double, string, CancellationToken, Task> reportProgressAsync,
        CancellationToken cancellationToken = default);
    Task<ManagedServerHealth> StartAndVerifyAsync(
        ManagedLaunchProfile profile,
        string rconPassword,
        Func<double, string, CancellationToken, Task> reportProgressAsync,
        CancellationToken cancellationToken = default);
    Task<bool> StopManagedAsync(CancellationToken cancellationToken = default);
}

public interface IManagedServerControlService
{
    Task<ServerStatus> GetStatusAsync(CancellationToken cancellationToken = default);
    Task<ManagedServerActionResult> ExecuteAsync(
        string action,
        Func<double, string, CancellationToken, Task> reportProgressAsync,
        CancellationToken cancellationToken = default);
    Task<ManagedServerActionResult> ForceStopAsync(
        int expectedProcessId,
        Func<double, string, CancellationToken, Task> reportProgressAsync,
        CancellationToken cancellationToken = default);
}

public interface IServerInitializationService
{
    Task<ServerInitializationResult> InitializeAsync(
        string operationId,
        ServerSetupPreflightRequest request,
        Func<double, string, CancellationToken, Task> reportProgressAsync,
        CancellationToken cancellationToken = default);
}

public interface ISteamCmdInstaller
{
    string PrepareTarget(string installDirectory);
    Task<SteamCmdInstallationResult> InstallAsync(
        string operationId,
        string installDirectory,
        Func<double, string, CancellationToken, Task> reportProgressAsync,
        CancellationToken cancellationToken = default);
}

public interface IAvorionServerInstaller
{
    AvorionServerInstallPlan Prepare(string steamCmdPath, string installDirectory);
    Task<AvorionServerInstallationResult> InstallAsync(
        string operationId,
        string steamCmdPath,
        string installDirectory,
        Func<double, string, CancellationToken, Task> reportProgressAsync,
        CancellationToken cancellationToken = default);
}

public interface IOperationStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<OperationRecord> CreateAsync(string operationId, string type, CancellationToken cancellationToken = default);
    Task<OperationCreateResult> CreateIdempotentAsync(
        string operationId,
        string type,
        string idempotencyKey,
        string requestHash,
        CancellationToken cancellationToken = default,
        System.Text.Json.JsonElement? request = null);
    Task MarkRunningAsync(string operationId, string step, CancellationToken cancellationToken = default);
    Task UpdateProgressAsync(string operationId, double progressPercent, string step, CancellationToken cancellationToken = default);
    Task CompleteAsync<T>(string operationId, T result, CancellationToken cancellationToken = default);
    Task FailAsync(string operationId, ApiErrorDetail error, CancellationToken cancellationToken = default);
    Task<int> FailInterruptedAsync(ApiErrorDetail error, CancellationToken cancellationToken = default);
    Task<OperationRecord?> GetAsync(string operationId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OperationRecord>> ListRecentAsync(int limit, CancellationToken cancellationToken = default);
}

public interface IAutomationTaskStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AutomationTaskRecord>> ListAsync(CancellationToken cancellationToken = default);
    Task<AutomationTaskRecord?> GetAsync(string taskId, CancellationToken cancellationToken = default);
    Task<AutomationTaskRecord> CreateAsync(
        string taskId,
        string idempotencyKey,
        string requestHash,
        AutomationTaskUpsertRequest request,
        DateTimeOffset nextRunAt,
        CancellationToken cancellationToken = default);
    Task<AutomationTaskRecord?> UpdateAsync(
        string taskId,
        AutomationTaskUpsertRequest request,
        DateTimeOffset nextRunAt,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DueAutomationTask>> ClaimDueAsync(
        DateTimeOffset now,
        int limit,
        CancellationToken cancellationToken = default);
    Task SetLastOperationAsync(
        string taskId,
        DateTimeOffset scheduledAt,
        string operationId,
        CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string taskId, CancellationToken cancellationToken = default);
}

public interface IMemoryPolicyStore
{
    Task<MemoryPolicy?> GetMemoryPolicyAsync(CancellationToken cancellationToken = default);
    Task<MemoryPolicy> SaveMemoryPolicyAsync(
        MemoryPolicyUpdateRequest request,
        CancellationToken cancellationToken = default);
}
