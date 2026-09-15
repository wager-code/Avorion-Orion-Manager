namespace AvorionAdmin.Core.Models;

public sealed record ServerSummary(
    string ServerId,
    string Name,
    Lifecycle Lifecycle,
    bool AgentConnected);

public sealed record ServerStatus(
    string ServerId,
    string Name,
    Lifecycle Lifecycle,
    int? ProcessId,
    string? Version,
    string? GalaxyName,
    long? UptimeSeconds,
    int? OnlinePlayers,
    int? MaxPlayers,
    ConnectionProbe Rcon,
    ConnectionProbe SteamQuery,
    LastSaveInfo LastSave,
    AgentInfo Agent,
    Provenance Provenance);

public sealed record ManagedLaunchProfile(
    int SchemaVersion,
    string ExecutablePath,
    string WorkingDirectory,
    string GalaxyDirectory,
    string GalaxyMode,
    IReadOnlyList<string> Arguments,
    string RequestedGameListenAddress,
    int RequestedQueryPort,
    bool RconEnabled,
    string RconHost,
    int RconPort,
    bool FirewallChangeRequested,
    bool ManagementModRequested,
    DateTimeOffset CreatedAt);

public sealed record GalaxyInitializationEvidence(
    string ServerIniPath,
    bool SaveCommandSent,
    bool StopCommandSent,
    int ExitCode,
    DateTimeOffset CompletedAt);

public sealed record ManagedServerHealth(
    int ProcessId,
    DateTimeOffset StartedAt,
    bool RconAuthenticated,
    DateTimeOffset CheckedAt);

public sealed record ManagedServerActionResult(
    string Action,
    string Outcome,
    Lifecycle Lifecycle,
    int? ProcessId,
    bool RconAuthenticated,
    IReadOnlyList<string> Evidence,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt);

public sealed record ForceStopRequest(int ExpectedProcessId, string Confirmation);

public sealed record ServerInitializationResult(
    string Mode,
    int ProcessId,
    string GalaxyDirectory,
    string ServerIniPath,
    bool ConsoleSaveConfirmed,
    bool ConsoleStopConfirmed,
    bool RconConfigured,
    bool RconAuthenticated,
    IReadOnlyList<string> HealthEvidence,
    IReadOnlyList<string> DeferredActions,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt);
