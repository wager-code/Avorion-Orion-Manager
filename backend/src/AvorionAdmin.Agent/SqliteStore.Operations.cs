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

}
