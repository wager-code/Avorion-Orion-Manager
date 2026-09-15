using AvorionAdmin.Core.Models;

namespace AvorionAdmin.Core.Abstractions;

public sealed class IdempotencyConflictException(string message) : Exception(message);

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
