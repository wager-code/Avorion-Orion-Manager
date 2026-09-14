using AvorionAdmin.Core.Abstractions;
using AvorionAdmin.Core.Configuration;
using AvorionAdmin.Core.Models;
using Microsoft.Extensions.Options;

namespace AvorionAdmin.Agent;

public sealed class DiagnosticService(
    IServerProbe serverProbe,
    IManagedServerControlService managedControl,
    IPerformanceStore performanceStore,
    IBackupScanner backupScanner,
    ILogReader logReader,
    ISteamQueryProbe steamQueryProbe,
    IUpdateEnvironmentStore updateEnvironmentStore,
    IServerRuntimePathResolver pathResolver,
    IOptions<ServerNodeOptions> options) : IDiagnosticService
{
    private readonly ServerNodeOptions _options = options.Value;

    public async Task<DiagnosticRun> RunAsync(string runId, CancellationToken cancellationToken = default)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var status = await managedControl.GetStatusAsync(cancellationToken);
        var performance = await serverProbe.GetPerformanceAsync(cancellationToken);
        var updateEnvironment = await updateEnvironmentStore.GetAsync(cancellationToken);
        var backups = await backupScanner.ScanAsync(cancellationToken);
        var logs = await logReader.ReadRecentAsync(500, cancellationToken: cancellationToken);
        var history = await performanceStore.QueryAsync("6h", cancellationToken);
        var steamQuery = await steamQueryProbe.ProbeAsync(status.Lifecycle == Lifecycle.Running, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var items = new List<DiagnosticItem>
        {
            ProcessItem(status, now),
            GamePortItem(status, steamQuery, now),
            SteamQueryItem(steamQuery, now),
            RconItem(status.Rcon, now),
            Item("steamcmd", updateEnvironment is not null && File.Exists(updateEnvironment.SteamCmdPath) ? DiagnosticState.Healthy : DiagnosticState.Unknown,
                updateEnvironment is not null && File.Exists(updateEnvironment.SteamCmdPath) ? "STEAMCMD_AVAILABLE" : "STEAMCMD_UNAVAILABLE",
                updateEnvironment is not null && File.Exists(updateEnvironment.SteamCmdPath) ? "已保存的 SteamCMD 路径可访问" : "SteamCMD 路径未配置或不可访问",
                new Dictionary<string, object?> { ["path"] = updateEnvironment?.SteamCmdPath }, "steamcmd", now),
            PathItem("galaxy-path", ResolveGalaxyPath(pathResolver.ResolveLogSource()), "Galaxy 路径", now),
            DiskItem(performance.Disk, now),
            SaveItem(status.LastSave, now),
            BackupItem(backups, now),
            MemoryItem(history, performance, now),
            ModErrorItem(logs, now)
        };

        return new DiagnosticRun(runId, "completed", startedAt, DateTimeOffset.UtcNow, items);
    }

    private static string? ResolveGalaxyPath(ResolvedLogSource? source) => source is null
        ? null
        : Directory.Exists(source.Path) ? source.Path : Path.GetDirectoryName(source.Path);

    private static DiagnosticItem RconItem(ConnectionProbe probe, DateTimeOffset now) => probe.Status switch
    {
        ConnectionState.Connected => Item("rcon", DiagnosticState.Healthy, "RCON_AUTHENTICATED", "本机 RCON 认证通过",
            new Dictionary<string, object?> { ["latencyMs"] = probe.LatencyMs }, "rcon", now),
        ConnectionState.Degraded => Item("rcon", DiagnosticState.Warning, "RCON_DEGRADED", "服务端运行，但本机 RCON 认证未通过",
            new Dictionary<string, object?> { ["reason"] = probe.UnavailableReason }, "rcon", now),
        ConnectionState.Disconnected => Item("rcon", DiagnosticState.Warning, "RCON_DISCONNECTED", "RCON 当前未连接",
            new Dictionary<string, object?> { ["reason"] = probe.UnavailableReason }, "rcon", now),
        _ => Unknown("rcon", probe.UnavailableReason ?? "RCON_STATUS_UNKNOWN", "无法确认本机 RCON 状态", "rcon", now)
    };

    private static DiagnosticItem GamePortItem(ServerStatus status, SteamQuerySnapshot query, DateTimeOffset now)
    {
        var evidence = new Dictionary<string, object?>
        {
            ["gamePort"] = query.GamePort,
            ["serverRunning"] = status.Lifecycle == Lifecycle.Running,
            ["serverLog"] = query.EvidenceSource
        };
        if (status.Lifecycle != Lifecycle.Running)
            return Item("game-port", DiagnosticState.Error, "GAME_SERVER_NOT_RUNNING", "服务器未运行，游戏端口不可用", evidence, "server-log", now);
        return query.GamePort is not null
            ? Item("game-port", DiagnosticState.Healthy, "GAME_PORT_ANNOUNCED", "运行中的 Avorion 已在当前启动日志声明游戏端口", evidence, "server-log", now)
            : Unknown("game-port", "GAME_PORT_EVIDENCE_UNAVAILABLE", "当前启动日志中没有可验证的游戏端口声明", "server-log", now);
    }

    private static DiagnosticItem SteamQueryItem(SteamQuerySnapshot query, DateTimeOffset now)
    {
        var evidence = new Dictionary<string, object?>
        {
            ["queryPort"] = query.QueryPort,
            ["latencyMs"] = query.Connection.LatencyMs,
            ["onlinePlayers"] = query.OnlinePlayers,
            ["maxPlayers"] = query.MaxPlayers,
            ["serverName"] = query.ServerName,
            ["serverLog"] = query.EvidenceSource
        };
        return query.Connection.Status switch
        {
            ConnectionState.Connected => Item("steam-query", DiagnosticState.Healthy, "STEAM_QUERY_RESPONDED", "Steam Query A2S_INFO 探测成功", evidence, "steam-query", now),
            ConnectionState.Degraded => Item("steam-query", DiagnosticState.Warning, query.Connection.UnavailableReason ?? "STEAM_QUERY_DEGRADED", "Steam Query UDP 端口已监听，但 A2S_INFO 未返回有效响应", evidence, "steam-query", now),
            ConnectionState.Disconnected => Item("steam-query", DiagnosticState.Error, query.Connection.UnavailableReason ?? "STEAM_QUERY_DISCONNECTED", "Steam Query 当前未连接", evidence, "steam-query", now),
            _ => Item("steam-query", DiagnosticState.Unknown, query.Connection.UnavailableReason ?? "STEAM_QUERY_UNKNOWN", "无法确认 Steam Query 状态", evidence, "steam-query", now, query.Connection.UnavailableReason)
        };
    }

    private static DiagnosticItem ProcessItem(ServerStatus status, DateTimeOffset now)
    {
        var evidence = new Dictionary<string, object?>
        {
            ["processId"] = status.ProcessId,
            ["lifecycle"] = status.Lifecycle.ToString().ToLowerInvariant()
        };

        return status.Lifecycle switch
        {
            Lifecycle.Running => Item("process", DiagnosticState.Healthy, "PROCESS_RUNNING",
                "Avorion 服务进程正在运行", evidence, "windows", now),
            Lifecycle.Stopped => Item("process", DiagnosticState.Error, "PROCESS_NOT_RUNNING",
                "未检测到 Avorion 服务进程", evidence, "windows", now),
            _ => Item("process", DiagnosticState.Unknown, "PROCESS_STATE_INDETERMINATE",
                $"Avorion 进程当前状态为 {status.Lifecycle}", evidence, "windows", now, "PROCESS_STATE_INDETERMINATE")
        };
    }

    private DiagnosticItem DiskItem(DiskMetrics disk, DateTimeOffset now)
    {
        if (disk.AvailableBytes is null)
        {
            return Unknown("disk-space", "DISK_METRICS_UNAVAILABLE", "无法读取 Galaxy 所在磁盘空间", "windows", now);
        }

        var warning = disk.AvailableBytes < _options.DiskWarningAvailableBytes;
        return Item("disk-space", warning ? DiagnosticState.Warning : DiagnosticState.Healthy,
            warning ? "DISK_SPACE_LOW" : "DISK_SPACE_SUFFICIENT",
            warning ? "Galaxy 所在磁盘可用空间偏低" : "Galaxy 所在磁盘空间充足",
            new Dictionary<string, object?> { ["availableBytes"] = disk.AvailableBytes, ["volume"] = disk.Volume }, "windows", now);
    }

    private static DiagnosticItem SaveItem(LastSaveInfo save, DateTimeOffset now) =>
        save.At is null
            ? Unknown("last-save", save.UnavailableReason ?? "LAST_SAVE_UNKNOWN", "无法确认最近保存时间", "filesystem", now)
            : Item("last-save", DiagnosticState.Healthy, "LAST_SAVE_FOUND", "已读取最近保存时间",
                new Dictionary<string, object?> { ["at"] = save.At }, "filesystem", now);

    private static DiagnosticItem BackupItem(IReadOnlyList<BackupRecord> backups, DateTimeOffset now) =>
        backups.Count == 0
            ? Unknown("last-backup", "NO_BACKUP_FILES_FOUND", "未在配置目录读取到备份文件", "filesystem", now)
            : Item("last-backup", DiagnosticState.Healthy, "LAST_BACKUP_FOUND", "已读取最近备份文件",
                new Dictionary<string, object?> { ["backupId"] = backups[0].BackupId, ["createdAt"] = backups[0].CreatedAt, ["sizeBytes"] = backups[0].SizeBytes }, "filesystem", now);

    private static DiagnosticItem MemoryItem(PerformanceHistory history, PerformanceSnapshot current, DateTimeOffset now)
    {
        var memoryPoints = history.Points
            .Where(point => point.MemoryWorkingSetBytes is not null)
            .Select(point => new { at = point.At, valueBytes = point.MemoryWorkingSetBytes!.Value })
            .ToArray();
        var evidence = new Dictionary<string, object?>
        {
            ["currentBytes"] = current.Memory.WorkingSetBytes,
            ["firstBytes"] = memoryPoints.FirstOrDefault()?.valueBytes,
            ["lastBytes"] = memoryPoints.LastOrDefault()?.valueBytes,
            ["sampleCount"] = memoryPoints.Length,
            ["points"] = memoryPoints
        };

        if (current.Memory.WorkingSetBytes is null)
        {
            return Item("memory-trend", DiagnosticState.Unknown, "MEMORY_UNAVAILABLE",
                memoryPoints.Length > 0
                    ? "Avorion 进程当前内存不可用；保留最近 6 小时真实历史"
                    : "Avorion 进程内存不可用",
                evidence, "windows", now, "PROCESS_MEMORY_UNAVAILABLE");
        }

        if (memoryPoints.Length < 4)
        {
            return Item("memory-trend", DiagnosticState.Unknown, "INSUFFICIENT_HISTORY",
                "最近 6 小时真实内存样本不足，暂不能判断趋势", evidence, "database", now, "INSUFFICIENT_HISTORY");
        }

        return Item("memory-trend", DiagnosticState.Healthy, "MEMORY_TREND_AVAILABLE",
            "已根据最近 6 小时真实采样计算内存趋势", evidence, "database", now);
    }

    private static DiagnosticItem ModErrorItem(IReadOnlyList<LogEntry> logs, DateTimeOffset now)
    {
        if (logs.Count == 0)
        {
            return Unknown("mod-errors", "NO_LOGS_SCANNED", "没有可供扫描的服务器日志，无法判断 MOD 错误", "filesystem", now);
        }

        var errors = logs.Count(entry => entry.RawLine.Contains("mod", StringComparison.OrdinalIgnoreCase) &&
            (entry.RawLine.Contains("error", StringComparison.OrdinalIgnoreCase) ||
             entry.RawLine.Contains("错误", StringComparison.OrdinalIgnoreCase) ||
             entry.RawLine.Contains("failed", StringComparison.OrdinalIgnoreCase)));
        return Item("mod-errors", errors > 0 ? DiagnosticState.Warning : DiagnosticState.Healthy,
            errors > 0 ? "MOD_ERRORS_FOUND" : "NO_MOD_ERRORS_IN_SCANNED_LOGS",
            errors > 0 ? $"在已扫描日志中发现 {errors} 条 MOD 错误" : "在已扫描日志范围内未发现 MOD 错误",
            new Dictionary<string, object?> { ["count"] = errors, ["scannedLogCount"] = logs.Count }, "filesystem", now);
    }

    private static DiagnosticItem PathItem(string key, string? path, string label, DateTimeOffset now)
    {
        var exists = !string.IsNullOrWhiteSpace(path) && Directory.Exists(path);
        return Item(key, exists ? DiagnosticState.Healthy : DiagnosticState.Unknown,
            exists ? "PATH_AVAILABLE" : "PATH_UNAVAILABLE",
            exists ? $"{label}可访问" : $"{label}未配置或不可访问",
            new Dictionary<string, object?>(), "filesystem", now);
    }

    private static DiagnosticItem Unknown(string key, string code, string message, string source, DateTimeOffset now) =>
        Item(key, DiagnosticState.Unknown, code, message, new Dictionary<string, object?>(), source, now, "CAPABILITY_UNAVAILABLE");

    private static DiagnosticItem Item(
        string key,
        DiagnosticState state,
        string code,
        string message,
        IReadOnlyDictionary<string, object?> evidence,
        string source,
        DateTimeOffset now,
        string? unavailableReason = null) =>
        new(key, state, code, message, evidence,
            new Provenance(now, source, state == DiagnosticState.Unknown ? "unavailable" : "live", unavailableReason));
}
