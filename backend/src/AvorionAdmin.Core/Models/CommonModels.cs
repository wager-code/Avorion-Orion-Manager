namespace AvorionAdmin.Core.Models;

public enum Lifecycle { Running, Stopped, Starting, Stopping, Restarting, Unknown }

public enum ConnectionState { Connected, Disconnected, Degraded, Unknown }

public enum DiagnosticState { Healthy, Warning, Error, Unknown }

public enum OperationState { Queued, Running, Succeeded, Failed, Cancelled }

public sealed record Provenance(
    DateTimeOffset SampledAt,
    string Source,
    string Freshness,
    string? UnavailableReason = null);

public sealed record ConnectionProbe(
    ConnectionState Status,
    double? LatencyMs,
    string? UnavailableReason = null);

public sealed record SteamQuerySnapshot(
    ConnectionProbe Connection,
    int? OnlinePlayers,
    int? MaxPlayers,
    string? ServerName,
    int? QueryPort,
    int? GamePort,
    string? EvidenceSource);

public sealed record LastSaveInfo(
    DateTimeOffset? At,
    string? Source,
    string? UnavailableReason = null);

public sealed record AgentInfo(bool Connected, DateTimeOffset? LastHeartbeatAt);
