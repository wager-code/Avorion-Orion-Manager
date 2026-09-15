namespace AvorionAdmin.Core.Models;

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
