using System.Globalization;
using System.Text.Json;
using AvorionAdmin.Core;
using AvorionAdmin.Core.Abstractions;
using AvorionAdmin.Core.Configuration;
using AvorionAdmin.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace AvorionAdmin.Agent;

public sealed partial class SqliteStore : IPerformanceStore, IOperationStore, IUpdateEnvironmentStore, IServerSetupDraftStore, IAutomationTaskStore, IMemoryPolicyStore
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
