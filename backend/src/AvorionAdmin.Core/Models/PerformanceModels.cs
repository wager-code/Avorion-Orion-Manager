namespace AvorionAdmin.Core.Models;

public sealed record CpuMetrics(double? ProcessPercent, double? HostPercent);

public sealed record MemoryMetrics(long? WorkingSetBytes, long? PrivateBytes, long? HostTotalBytes);

public sealed record DiskMetrics(long? TotalBytes, long? UsedBytes, long? AvailableBytes, string? Volume);

public sealed record NetworkMetrics(string Scope, double? DownloadBytesPerSecond, double? UploadBytesPerSecond);

public sealed record PerformanceSnapshot(
    CpuMetrics Cpu,
    MemoryMetrics Memory,
    DiskMetrics Disk,
    NetworkMetrics Network,
    int? OnlinePlayers,
    long? UptimeSeconds,
    Provenance Provenance);

public sealed record PerformancePoint(
    DateTimeOffset At,
    double? CpuProcessPercent,
    long? MemoryWorkingSetBytes,
    double? NetworkDownloadBytesPerSecond,
    double? NetworkUploadBytesPerSecond,
    int? OnlinePlayers);

public sealed record PerformanceHistory(
    string Range,
    IReadOnlyList<PerformancePoint> Points,
    string? WarningCode);

public sealed record MemoryOverview(
    long? CurrentWorkingSetBytes,
    long? StartupBaselineBytes,
    long? GrowthBytes,
    double? GrowthBytesPerHour,
    long? WarningThresholdBytes,
    string AlertLevel,
    int? LoadedSectorCount,
    int? PlayerSectorCount,
    int? IdleSectorCount,
    Provenance Provenance);

public sealed record MemoryPolicy(
    long WarningThresholdBytes,
    bool Notify,
    bool TryUnloadIdleSectors,
    bool SafeRestartAtCritical,
    DateTimeOffset UpdatedAt);

public sealed record MemoryPolicyUpdateRequest(
    long WarningThresholdBytes,
    bool Notify,
    bool TryUnloadIdleSectors,
    bool SafeRestartAtCritical);

public sealed record SectorUnloadRequest(int X, int Y, string Confirmation);

public sealed record SectorUnloadResult(
    int X,
    int Y,
    bool Accepted,
    bool Unloaded,
    string Outcome,
    IReadOnlyList<string> Evidence,
    DateTimeOffset CompletedAt);
