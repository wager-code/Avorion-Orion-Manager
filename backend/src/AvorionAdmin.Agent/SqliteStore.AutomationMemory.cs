using System.Globalization;
using System.Text.Json;
using AvorionAdmin.Core;
using AvorionAdmin.Core.Abstractions;
using AvorionAdmin.Core.Configuration;
using AvorionAdmin.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace AvorionAdmin.Agent;

public sealed partial class SqliteStore
{
    public async Task<IReadOnlyList<AutomationTaskRecord>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = TaskSelect + " ORDER BY t.next_run_at_utc ASC, t.created_at_utc ASC;";
        var items = new List<AutomationTaskRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) items.Add(ReadTask(reader));
        return items;
    }

    async Task<AutomationTaskRecord?> IAutomationTaskStore.GetAsync(string taskId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        return await GetTaskAsync(connection, taskId, cancellationToken);
    }

    public async Task<AutomationTaskRecord> CreateAsync(
        string taskId,
        string idempotencyKey,
        string requestHash,
        AutomationTaskUpsertRequest request,
        DateTimeOffset nextRunAt,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO automation_tasks
            (task_id, idempotency_key, request_hash, kind, name, schedule_kind, interval_minutes, day_of_week,
             local_time, timezone, enabled, next_run_at_utc, created_at_utc, updated_at_utc)
            VALUES
            ($id, $key, $hash, $kind, $name, $schedule, $interval, $day, $time, $timezone,
             $enabled, $next, $created, $updated);
            """;
        BindTask(command, taskId, idempotencyKey, request, nextRunAt, now);
        command.Parameters.AddWithValue("$hash", requestHash);
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            var existing = await GetTaskByIdempotencyKeyAsync(connection, idempotencyKey, cancellationToken);
            if (existing.Task is not null && string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal)) return existing.Task;
            if (existing.Task is not null) throw new IdempotencyConflictException("同一个幂等键不能用于不同的自动任务请求");
            throw;
        }
        return (await GetTaskAsync(connection, taskId, cancellationToken))!;
    }

    public async Task<AutomationTaskRecord?> UpdateAsync(
        string taskId,
        AutomationTaskUpsertRequest request,
        DateTimeOffset nextRunAt,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE automation_tasks SET
                kind=$kind, name=$name, schedule_kind=$schedule, interval_minutes=$interval,
                day_of_week=$day, local_time=$time, timezone=$timezone, enabled=$enabled,
                next_run_at_utc=$next, updated_at_utc=$updated
            WHERE task_id=$id;
            """;
        BindTask(command, taskId, string.Empty, request, nextRunAt, DateTimeOffset.UtcNow, includeKey: false);
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0) return null;
        return await GetTaskAsync(connection, taskId, cancellationToken);
    }

    public async Task<bool> DeleteAsync(string taskId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM automation_tasks WHERE task_id=$id;";
        command.Parameters.AddWithValue("$id", taskId);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<MemoryPolicy?> GetMemoryPolicyAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT warning_threshold_bytes, notify, try_unload_idle_sectors,
                   safe_restart_at_critical, updated_at_utc
            FROM memory_policy WHERE singleton_id=1;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new MemoryPolicy(
            reader.GetInt64(0),
            reader.GetInt64(1) == 1,
            reader.GetInt64(2) == 1,
            reader.GetInt64(3) == 1,
            ParseDate(reader.GetString(4))!.Value);
    }

    public async Task<MemoryPolicy> SaveMemoryPolicyAsync(
        MemoryPolicyUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        var updatedAt = DateTimeOffset.UtcNow;
        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO memory_policy
            (singleton_id, warning_threshold_bytes, notify, try_unload_idle_sectors,
             safe_restart_at_critical, updated_at_utc)
            VALUES (1, $threshold, $notify, $unload, $restart, $updated)
            ON CONFLICT(singleton_id) DO UPDATE SET
                warning_threshold_bytes=excluded.warning_threshold_bytes,
                notify=excluded.notify,
                try_unload_idle_sectors=excluded.try_unload_idle_sectors,
                safe_restart_at_critical=excluded.safe_restart_at_critical,
                updated_at_utc=excluded.updated_at_utc;
            """;
        command.Parameters.AddWithValue("$threshold", request.WarningThresholdBytes);
        command.Parameters.AddWithValue("$notify", request.Notify ? 1 : 0);
        command.Parameters.AddWithValue("$unload", request.TryUnloadIdleSectors ? 1 : 0);
        command.Parameters.AddWithValue("$restart", request.SafeRestartAtCritical ? 1 : 0);
        command.Parameters.AddWithValue("$updated", FormatDate(updatedAt));
        await command.ExecuteNonQueryAsync(cancellationToken);
        return new MemoryPolicy(
            request.WarningThresholdBytes,
            request.Notify,
            request.TryUnloadIdleSectors,
            request.SafeRestartAtCritical,
            updatedAt);
    }

    public async Task<IReadOnlyList<DueAutomationTask>> ClaimDueAsync(
        DateTimeOffset now,
        int limit,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var select = connection.CreateCommand();
        select.Transaction = transaction;
        select.CommandText = TaskSelect + " WHERE t.enabled=1 AND t.next_run_at_utc <= $now ORDER BY t.next_run_at_utc ASC LIMIT $limit;";
        select.Parameters.AddWithValue("$now", FormatDate(now));
        select.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 32));
        var due = new List<DueAutomationTask>();
        await using (var reader = await select.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var task = ReadTask(reader);
                due.Add(new DueAutomationTask(task, task.NextRunAt));
            }
        }

        foreach (var item in due)
        {
            var request = new AutomationTaskUpsertRequest(
                item.Task.Kind,
                item.Task.ScheduleKind,
                item.Task.IntervalMinutes,
                item.Task.DayOfWeek,
                item.Task.LocalTime,
                true);
            var next = AutomationSchedule.NextRun(request, item.ScheduledAt);
            while (next <= now) next = AutomationSchedule.NextRun(request, next);
            var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = "UPDATE automation_tasks SET next_run_at_utc=$next, updated_at_utc=$updated WHERE task_id=$id AND next_run_at_utc=$claimed;";
            update.Parameters.AddWithValue("$next", FormatDate(next));
            update.Parameters.AddWithValue("$updated", FormatDate(now));
            update.Parameters.AddWithValue("$id", item.Task.TaskId);
            update.Parameters.AddWithValue("$claimed", FormatDate(item.ScheduledAt));
            await update.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return due;
    }

    public async Task SetLastOperationAsync(
        string taskId,
        DateTimeOffset scheduledAt,
        string operationId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "UPDATE automation_tasks SET last_run_at_utc=$run, last_operation_id=$operation, updated_at_utc=$updated WHERE task_id=$id;";
        command.Parameters.AddWithValue("$run", FormatDate(scheduledAt));
        command.Parameters.AddWithValue("$operation", operationId);
        command.Parameters.AddWithValue("$updated", FormatDate(DateTimeOffset.UtcNow));
        command.Parameters.AddWithValue("$id", taskId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

}
