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

}
