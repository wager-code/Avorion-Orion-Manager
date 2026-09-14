using System.Globalization;
using System.Text.Json;
using AvorionAdmin.Core;
using AvorionAdmin.Core.Abstractions;
using AvorionAdmin.Core.Configuration;
using AvorionAdmin.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace AvorionAdmin.Agent;

public sealed class SqliteStore : IPerformanceStore, IOperationStore, IUpdateEnvironmentStore, IServerSetupDraftStore, IAutomationTaskStore, IMemoryPolicyStore
{
    private readonly string _connectionString;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public SqliteStore(IOptions<ServerNodeOptions> options)
    {
        var directory = Path.GetFullPath(options.Value.DataDirectory);
        Directory.CreateDirectory(directory);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(directory, "avorion-admin.db"),
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    async Task IPerformanceStore.InitializeAsync(CancellationToken cancellationToken) =>
        await InitializeAsync(cancellationToken);

    async Task IOperationStore.InitializeAsync(CancellationToken cancellationToken) =>
        await InitializeAsync(cancellationToken);

    async Task IAutomationTaskStore.InitializeAsync(CancellationToken cancellationToken) =>
        await InitializeAsync(cancellationToken);

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS performance_samples (
                at_utc TEXT NOT NULL PRIMARY KEY,
                cpu_process_percent REAL NULL,
                memory_working_set_bytes INTEGER NULL,
                network_download_bps REAL NULL,
                network_upload_bps REAL NULL,
                online_players INTEGER NULL
            );
            CREATE INDEX IF NOT EXISTS ix_performance_samples_at ON performance_samples(at_utc);
            CREATE TABLE IF NOT EXISTS operations (
                operation_id TEXT NOT NULL PRIMARY KEY,
                type TEXT NOT NULL,
                idempotency_key TEXT NULL,
                request_hash TEXT NULL,
                request_json TEXT NULL,
                status TEXT NOT NULL,
                progress_percent REAL NULL,
                current_step TEXT NULL,
                accepted_at_utc TEXT NOT NULL,
                started_at_utc TEXT NULL,
                completed_at_utc TEXT NULL,
                result_json TEXT NULL,
                error_json TEXT NULL
            );
            CREATE TABLE IF NOT EXISTS update_environment (
                singleton_id INTEGER NOT NULL PRIMARY KEY CHECK (singleton_id = 1),
                steamcmd_path TEXT NOT NULL,
                server_directory TEXT NOT NULL,
                validated_at_utc TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS server_setup_draft (
                singleton_id INTEGER NOT NULL PRIMARY KEY CHECK (singleton_id = 1),
                server_name TEXT NOT NULL,
                galaxy_name TEXT NOT NULL,
                galaxy_mode TEXT NOT NULL,
                max_players INTEGER NOT NULL,
                galaxy_directory TEXT NOT NULL,
                listen_address TEXT NOT NULL,
                game_port INTEGER NOT NULL,
                query_port INTEGER NOT NULL,
                rcon_enabled INTEGER NOT NULL,
                rcon_port INTEGER NOT NULL,
                allow_firewall_change INTEGER NOT NULL,
                install_management_mod INTEGER NOT NULL,
                updated_at_utc TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS automation_tasks (
                task_id TEXT NOT NULL PRIMARY KEY,
                idempotency_key TEXT NOT NULL,
                request_hash TEXT NOT NULL,
                kind TEXT NOT NULL,
                name TEXT NOT NULL,
                schedule_kind TEXT NOT NULL,
                interval_minutes INTEGER NULL,
                day_of_week INTEGER NULL,
                local_time TEXT NULL,
                timezone TEXT NOT NULL,
                enabled INTEGER NOT NULL,
                next_run_at_utc TEXT NOT NULL,
                last_run_at_utc TEXT NULL,
                last_operation_id TEXT NULL,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS memory_policy (
                singleton_id INTEGER NOT NULL PRIMARY KEY CHECK (singleton_id = 1),
                warning_threshold_bytes INTEGER NOT NULL,
                notify INTEGER NOT NULL,
                try_unload_idle_sectors INTEGER NOT NULL,
                safe_restart_at_critical INTEGER NOT NULL,
                updated_at_utc TEXT NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        await EnsureColumnAsync(connection, "operations", "idempotency_key", "TEXT NULL", cancellationToken);
        await EnsureColumnAsync(connection, "operations", "request_hash", "TEXT NULL", cancellationToken);
        await EnsureColumnAsync(connection, "operations", "request_json", "TEXT NULL", cancellationToken);
        var index = connection.CreateCommand();
        index.CommandText = "CREATE UNIQUE INDEX IF NOT EXISTS ux_operations_idempotency_key ON operations(idempotency_key) WHERE idempotency_key IS NOT NULL;";
        await index.ExecuteNonQueryAsync(cancellationToken);
        var taskIndex = connection.CreateCommand();
        taskIndex.CommandText = "CREATE UNIQUE INDEX IF NOT EXISTS ux_automation_tasks_idempotency_key ON automation_tasks(idempotency_key);";
        await taskIndex.ExecuteNonQueryAsync(cancellationToken);
    }

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

    public async Task<UpdateEnvironmentConfiguration?> GetAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT steamcmd_path, server_directory, validated_at_utc FROM update_environment WHERE singleton_id = 1;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new UpdateEnvironmentConfiguration(
            reader.GetString(0),
            reader.GetString(1),
            DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal));
    }

    public async Task SaveAsync(UpdateEnvironmentConfiguration configuration, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO update_environment (singleton_id, steamcmd_path, server_directory, validated_at_utc)
            VALUES (1, $steamcmd, $server, $validated)
            ON CONFLICT(singleton_id) DO UPDATE SET
                steamcmd_path = excluded.steamcmd_path,
                server_directory = excluded.server_directory,
                validated_at_utc = excluded.validated_at_utc;
            """;
        command.Parameters.AddWithValue("$steamcmd", configuration.SteamCmdPath);
        command.Parameters.AddWithValue("$server", configuration.ServerDirectory);
        command.Parameters.AddWithValue("$validated", configuration.ValidatedAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    async Task<ServerSetupDraftConfiguration?> IServerSetupDraftStore.GetAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT server_name, galaxy_name, galaxy_mode, max_players, galaxy_directory,
                   listen_address, game_port, query_port, rcon_enabled, rcon_port,
                   allow_firewall_change, install_management_mod, updated_at_utc
            FROM server_setup_draft WHERE singleton_id = 1;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new ServerSetupDraftConfiguration(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetInt32(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetInt32(6),
            reader.GetInt32(7),
            reader.GetInt64(8) == 1,
            reader.GetInt32(9),
            reader.GetInt64(10) == 1,
            false,
            DateTimeOffset.Parse(reader.GetString(12), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal));
    }

    async Task IServerSetupDraftStore.SaveAsync(ServerSetupDraftConfiguration configuration, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO server_setup_draft
            (singleton_id, server_name, galaxy_name, galaxy_mode, max_players, galaxy_directory,
             listen_address, game_port, query_port, rcon_enabled, rcon_port,
             allow_firewall_change, install_management_mod, updated_at_utc)
            VALUES
            (1, $server_name, $galaxy_name, $galaxy_mode, $max_players, $galaxy_directory,
             $listen_address, $game_port, $query_port, $rcon_enabled, $rcon_port,
             $allow_firewall_change, $install_management_mod, $updated_at)
            ON CONFLICT(singleton_id) DO UPDATE SET
                server_name = excluded.server_name,
                galaxy_name = excluded.galaxy_name,
                galaxy_mode = excluded.galaxy_mode,
                max_players = excluded.max_players,
                galaxy_directory = excluded.galaxy_directory,
                listen_address = excluded.listen_address,
                game_port = excluded.game_port,
                query_port = excluded.query_port,
                rcon_enabled = excluded.rcon_enabled,
                rcon_port = excluded.rcon_port,
                allow_firewall_change = excluded.allow_firewall_change,
                install_management_mod = excluded.install_management_mod,
                updated_at_utc = excluded.updated_at_utc;
            """;
        command.Parameters.AddWithValue("$server_name", configuration.ServerName);
        command.Parameters.AddWithValue("$galaxy_name", configuration.GalaxyName);
        command.Parameters.AddWithValue("$galaxy_mode", configuration.GalaxyMode);
        command.Parameters.AddWithValue("$max_players", configuration.MaxPlayers);
        command.Parameters.AddWithValue("$galaxy_directory", configuration.GalaxyDirectory);
        command.Parameters.AddWithValue("$listen_address", configuration.ListenAddress);
        command.Parameters.AddWithValue("$game_port", configuration.GamePort);
        command.Parameters.AddWithValue("$query_port", configuration.QueryPort);
        command.Parameters.AddWithValue("$rcon_enabled", configuration.RconEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$rcon_port", configuration.RconPort);
        command.Parameters.AddWithValue("$allow_firewall_change", configuration.AllowFirewallChange ? 1 : 0);
        command.Parameters.AddWithValue("$install_management_mod", 0);
        command.Parameters.AddWithValue("$updated_at", configuration.UpdatedAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task AppendAsync(PerformancePoint point, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR REPLACE INTO performance_samples
            (at_utc, cpu_process_percent, memory_working_set_bytes, network_download_bps, network_upload_bps, online_players)
            VALUES ($at, $cpu, $memory, $download, $upload, $players);
            """;
        command.Parameters.AddWithValue("$at", point.At.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$cpu", DbValue(point.CpuProcessPercent));
        command.Parameters.AddWithValue("$memory", DbValue(point.MemoryWorkingSetBytes));
        command.Parameters.AddWithValue("$download", DbValue(point.NetworkDownloadBytesPerSecond));
        command.Parameters.AddWithValue("$upload", DbValue(point.NetworkUploadBytesPerSecond));
        command.Parameters.AddWithValue("$players", DbValue(point.OnlinePlayers));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<PerformanceHistory> QueryAsync(string range, CancellationToken cancellationToken = default)
    {
        var duration = range switch
        {
            "1h" => TimeSpan.FromHours(1),
            "6h" => TimeSpan.FromHours(6),
            "24h" => TimeSpan.FromHours(24),
            "7d" => TimeSpan.FromDays(7),
            _ => throw new ArgumentOutOfRangeException(nameof(range), "仅支持 1h、6h、24h、7d")
        };

        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT at_utc, cpu_process_percent, memory_working_set_bytes, network_download_bps, network_upload_bps, online_players
            FROM performance_samples
            WHERE at_utc >= $since
            ORDER BY at_utc ASC;
            """;
        command.Parameters.AddWithValue("$since", DateTimeOffset.UtcNow.Subtract(duration).UtcDateTime.ToString("O", CultureInfo.InvariantCulture));

        var points = new List<PerformancePoint>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            points.Add(new PerformancePoint(
                DateTimeOffset.Parse(reader.GetString(0), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal),
                reader.IsDBNull(1) ? null : reader.GetDouble(1),
                reader.IsDBNull(2) ? null : reader.GetInt64(2),
                reader.IsDBNull(3) ? null : reader.GetDouble(3),
                reader.IsDBNull(4) ? null : reader.GetDouble(4),
                reader.IsDBNull(5) ? null : reader.GetInt32(5)));
        }

        var usableProcessSamples = points.Count(point =>
            point.CpuProcessPercent is not null || point.MemoryWorkingSetBytes is not null);
        return new PerformanceHistory(range, points, usableProcessSamples < 2 ? "INSUFFICIENT_HISTORY" : null);
    }

    public async Task PruneAsync(int retentionDays, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM performance_samples WHERE at_utc < $before;";
        command.Parameters.AddWithValue("$before", DateTimeOffset.UtcNow.AddDays(-Math.Max(1, retentionDays)).UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<OperationRecord> CreateAsync(string operationId, string type, CancellationToken cancellationToken = default)
    {
        var acceptedAt = DateTimeOffset.UtcNow;
        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO operations(operation_id, type, status, accepted_at_utc)
            VALUES($id, $type, 'Queued', $accepted);
            """;
        command.Parameters.AddWithValue("$id", operationId);
        command.Parameters.AddWithValue("$type", type);
        command.Parameters.AddWithValue("$accepted", acceptedAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken);
        return new OperationRecord(operationId, type, OperationState.Queued, null, null, acceptedAt, null, null, null, null);
    }

    public async Task<OperationCreateResult> CreateIdempotentAsync(
        string operationId,
        string type,
        string idempotencyKey,
        string requestHash,
        CancellationToken cancellationToken = default,
        JsonElement? request = null)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var existing = await GetByIdempotencyKeyAsync(connection, idempotencyKey, cancellationToken);
        if (existing.Operation is not null)
        {
            if (!string.Equals(existing.Type, type, StringComparison.Ordinal) ||
                !string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
                throw new IdempotencyConflictException("同一个幂等键不能用于不同操作或请求内容");
            return new OperationCreateResult(existing.Operation, false);
        }

        var acceptedAt = DateTimeOffset.UtcNow;
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO operations(operation_id, type, idempotency_key, request_hash, request_json, status, accepted_at_utc)
            VALUES($id, $type, $key, $hash, $request, 'Queued', $accepted);
            """;
        command.Parameters.AddWithValue("$id", operationId);
        command.Parameters.AddWithValue("$type", type);
        command.Parameters.AddWithValue("$key", idempotencyKey);
        command.Parameters.AddWithValue("$hash", requestHash);
        command.Parameters.AddWithValue("$request", request is null ? DBNull.Value : request.Value.GetRawText());
        command.Parameters.AddWithValue("$accepted", acceptedAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            existing = await GetByIdempotencyKeyAsync(connection, idempotencyKey, cancellationToken);
            if (existing.Operation is null ||
                !string.Equals(existing.Type, type, StringComparison.Ordinal) ||
                !string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
                throw new IdempotencyConflictException("同一个幂等键不能用于不同操作或请求内容");
            return new OperationCreateResult(existing.Operation, false);
        }
        return new OperationCreateResult(
            new OperationRecord(operationId, type, OperationState.Queued, null, null, acceptedAt, null, null, null, null),
            true);
    }

    public async Task MarkRunningAsync(string operationId, string step, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE operations SET status='Running', current_step=$step, started_at_utc=COALESCE(started_at_utc, $started)
            WHERE operation_id=$id;
            """;
        command.Parameters.AddWithValue("$id", operationId);
        command.Parameters.AddWithValue("$step", step);
        command.Parameters.AddWithValue("$started", DateTimeOffset.UtcNow.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateProgressAsync(
        string operationId,
        double progressPercent,
        string step,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE operations SET progress_percent=$progress, current_step=$step
            WHERE operation_id=$id AND status='Running';
            """;
        command.Parameters.AddWithValue("$id", operationId);
        command.Parameters.AddWithValue("$progress", Math.Clamp(progressPercent, 0, 100));
        command.Parameters.AddWithValue("$step", step);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task CompleteAsync<T>(string operationId, T result, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE operations SET status='Succeeded', progress_percent=100, current_step='completed',
                completed_at_utc=$completed, result_json=$result, error_json=NULL
            WHERE operation_id=$id;
            """;
        command.Parameters.AddWithValue("$id", operationId);
        command.Parameters.AddWithValue("$completed", DateTimeOffset.UtcNow.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$result", JsonSerializer.Serialize(result, _jsonOptions));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task FailAsync(string operationId, ApiErrorDetail error, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE operations SET status='Failed', completed_at_utc=$completed, error_json=$error
            WHERE operation_id=$id;
            """;
        command.Parameters.AddWithValue("$id", operationId);
        command.Parameters.AddWithValue("$completed", DateTimeOffset.UtcNow.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$error", JsonSerializer.Serialize(error, _jsonOptions));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> FailInterruptedAsync(ApiErrorDetail error, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE operations SET status='Failed', completed_at_utc=$completed, error_json=$error
            WHERE status IN ('Queued', 'Running');
            """;
        command.Parameters.AddWithValue("$completed", DateTimeOffset.UtcNow.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$error", JsonSerializer.Serialize(error, _jsonOptions));
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<OperationRecord?> GetAsync(string operationId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT operation_id, type, status, progress_percent, current_step, accepted_at_utc,
                   started_at_utc, completed_at_utc, result_json, error_json, request_json
            FROM operations WHERE operation_id=$id;
            """;
        command.Parameters.AddWithValue("$id", operationId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;

        return new OperationRecord(
            reader.GetString(0),
            reader.GetString(1),
            Enum.Parse<OperationState>(reader.GetString(2), true),
            reader.IsDBNull(3) ? null : reader.GetDouble(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            ParseDate(reader.GetString(5))!.Value,
            reader.IsDBNull(6) ? null : ParseDate(reader.GetString(6)),
            reader.IsDBNull(7) ? null : ParseDate(reader.GetString(7)),
            reader.IsDBNull(8) ? null : JsonSerializer.Deserialize<JsonElement>(reader.GetString(8), _jsonOptions),
            reader.IsDBNull(9) ? null : JsonSerializer.Deserialize<ApiErrorDetail>(reader.GetString(9), _jsonOptions),
            reader.IsDBNull(10) ? null : JsonSerializer.Deserialize<JsonElement>(reader.GetString(10), _jsonOptions));
    }

    public async Task<IReadOnlyList<OperationRecord>> ListRecentAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT operation_id, type, status, progress_percent, current_step, accepted_at_utc,
                   started_at_utc, completed_at_utc, result_json, error_json, request_json
            FROM operations
            ORDER BY accepted_at_utc DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 100));
        var items = new List<OperationRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new OperationRecord(
                reader.GetString(0),
                reader.GetString(1),
                Enum.Parse<OperationState>(reader.GetString(2), true),
                reader.IsDBNull(3) ? null : reader.GetDouble(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                ParseDate(reader.GetString(5))!.Value,
                reader.IsDBNull(6) ? null : ParseDate(reader.GetString(6)),
                reader.IsDBNull(7) ? null : ParseDate(reader.GetString(7)),
                reader.IsDBNull(8) ? null : JsonSerializer.Deserialize<JsonElement>(reader.GetString(8), _jsonOptions),
                reader.IsDBNull(9) ? null : JsonSerializer.Deserialize<ApiErrorDetail>(reader.GetString(9), _jsonOptions),
                reader.IsDBNull(10) ? null : JsonSerializer.Deserialize<JsonElement>(reader.GetString(10), _jsonOptions)));
        }
        return items;
    }

    private const string TaskSelect = """
        SELECT t.task_id, t.kind, t.name, t.schedule_kind, t.interval_minutes, t.day_of_week,
               t.local_time, t.timezone, t.enabled, t.next_run_at_utc, t.last_run_at_utc,
               t.last_operation_id, o.status, o.error_json, t.created_at_utc, t.updated_at_utc
        FROM automation_tasks t
        LEFT JOIN operations o ON o.operation_id = t.last_operation_id
        """;

    private async Task<AutomationTaskRecord?> GetTaskAsync(
        SqliteConnection connection,
        string taskId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = TaskSelect + " WHERE t.task_id=$id;";
        command.Parameters.AddWithValue("$id", taskId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadTask(reader) : null;
    }

    private async Task<(AutomationTaskRecord? Task, string? RequestHash)> GetTaskByIdempotencyKeyAsync(
        SqliteConnection connection,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = TaskSelect.Replace("FROM automation_tasks t", "FROM automation_tasks t") + " WHERE t.idempotency_key=$key;";
        command.Parameters.AddWithValue("$key", idempotencyKey);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return (null, null);
        var task = ReadTask(reader);
        await reader.DisposeAsync();
        var hashCommand = connection.CreateCommand();
        hashCommand.CommandText = "SELECT request_hash FROM automation_tasks WHERE idempotency_key=$key;";
        hashCommand.Parameters.AddWithValue("$key", idempotencyKey);
        return (task, (string?)await hashCommand.ExecuteScalarAsync(cancellationToken));
    }

    private AutomationTaskRecord ReadTask(SqliteDataReader reader)
    {
        ApiErrorDetail? error = null;
        if (!reader.IsDBNull(13))
            error = JsonSerializer.Deserialize<ApiErrorDetail>(reader.GetString(13), _jsonOptions);
        return new AutomationTaskRecord(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetInt32(4),
            reader.IsDBNull(5) ? null : reader.GetInt32(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.GetString(7),
            reader.GetInt64(8) == 1,
            ParseDate(reader.GetString(9))!.Value,
            reader.IsDBNull(10) ? null : ParseDate(reader.GetString(10)),
            reader.IsDBNull(11) ? null : reader.GetString(11),
            reader.IsDBNull(12) ? null : Enum.Parse<OperationState>(reader.GetString(12), true),
            error?.Message,
            ParseDate(reader.GetString(14))!.Value,
            ParseDate(reader.GetString(15))!.Value);
    }

    private static void BindTask(
        SqliteCommand command,
        string taskId,
        string idempotencyKey,
        AutomationTaskUpsertRequest request,
        DateTimeOffset nextRunAt,
        DateTimeOffset now,
        bool includeKey = true)
    {
        command.Parameters.AddWithValue("$id", taskId);
        if (includeKey) command.Parameters.AddWithValue("$key", idempotencyKey);
        command.Parameters.AddWithValue("$kind", request.Kind);
        command.Parameters.AddWithValue("$name", AutomationSchedule.NameFor(request.Kind));
        command.Parameters.AddWithValue("$schedule", request.ScheduleKind);
        command.Parameters.AddWithValue("$interval", request.IntervalMinutes is null ? DBNull.Value : request.IntervalMinutes.Value);
        command.Parameters.AddWithValue("$day", request.DayOfWeek is null ? DBNull.Value : request.DayOfWeek.Value);
        command.Parameters.AddWithValue("$time", request.LocalTime is null ? DBNull.Value : request.LocalTime);
        command.Parameters.AddWithValue("$timezone", AutomationSchedule.TimeZone);
        command.Parameters.AddWithValue("$enabled", request.Enabled ? 1 : 0);
        command.Parameters.AddWithValue("$next", FormatDate(nextRunAt));
        command.Parameters.AddWithValue("$created", FormatDate(now));
        command.Parameters.AddWithValue("$updated", FormatDate(now));
    }

    private static string FormatDate(DateTimeOffset value) =>
        value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    private static async Task EnsureColumnAsync(
        SqliteConnection connection,
        string table,
        string column,
        string definition,
        CancellationToken cancellationToken)
    {
        var inspect = connection.CreateCommand();
        inspect.CommandText = $"PRAGMA table_info({table});";
        await using var reader = await inspect.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (reader.GetString(1).Equals(column, StringComparison.OrdinalIgnoreCase)) return;
        }
        await reader.DisposeAsync();

        var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
        await alter.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<(OperationRecord? Operation, string? Type, string? RequestHash)> GetByIdempotencyKeyAsync(
        SqliteConnection connection,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT operation_id, type, status, progress_percent, current_step, accepted_at_utc,
                   started_at_utc, completed_at_utc, result_json, error_json, request_hash, request_json
            FROM operations WHERE idempotency_key=$key;
            """;
        command.Parameters.AddWithValue("$key", idempotencyKey);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return (null, null, null);
        var operation = new OperationRecord(
            reader.GetString(0),
            reader.GetString(1),
            Enum.Parse<OperationState>(reader.GetString(2), true),
            reader.IsDBNull(3) ? null : reader.GetDouble(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            ParseDate(reader.GetString(5))!.Value,
            reader.IsDBNull(6) ? null : ParseDate(reader.GetString(6)),
            reader.IsDBNull(7) ? null : ParseDate(reader.GetString(7)),
            reader.IsDBNull(8) ? null : JsonSerializer.Deserialize<JsonElement>(reader.GetString(8), _jsonOptions),
            reader.IsDBNull(9) ? null : JsonSerializer.Deserialize<ApiErrorDetail>(reader.GetString(9), _jsonOptions),
            reader.IsDBNull(11) ? null : JsonSerializer.Deserialize<JsonElement>(reader.GetString(11), _jsonOptions));
        return (operation, reader.GetString(1), reader.IsDBNull(10) ? null : reader.GetString(10));
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static object DbValue<T>(T? value) where T : struct => value.HasValue ? value.Value : DBNull.Value;

    private static DateTimeOffset? ParseDate(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
}
