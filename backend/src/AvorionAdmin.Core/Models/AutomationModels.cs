namespace AvorionAdmin.Core.Models;

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
