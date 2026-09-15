using System.Text.Json;

namespace AvorionAdmin.Core.Models;

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
