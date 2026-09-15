using AvorionAdmin.Core.Models;

namespace AvorionAdmin.Core.Abstractions;

public interface IServerProbe
{
    Task<ServerStatus> GetStatusAsync(CancellationToken cancellationToken = default);
    Task<PerformanceSnapshot> GetPerformanceAsync(CancellationToken cancellationToken = default);
    Task<UpdateStatus> GetUpdateStatusAsync(CancellationToken cancellationToken = default);
}

public interface ISteamQueryProbe
{
    Task<SteamQuerySnapshot> ProbeAsync(bool serverRunning, CancellationToken cancellationToken = default);
}

public sealed record ResolvedLogSource(string Path, bool IsGalaxyRoot);

public sealed record ResolvedBackupSource(string Path, string Origin, string? GalaxyName);

public interface IServerRuntimePathResolver
{
    ResolvedLogSource? ResolveLogSource();
    ResolvedBackupSource? ResolveBackupSource();
}

public interface IManagedServerRuntime
{
    Task<GalaxyInitializationEvidence> InitializeGalaxyAsync(
        ManagedLaunchProfile profile,
        Func<double, string, CancellationToken, Task> reportProgressAsync,
        CancellationToken cancellationToken = default);
    Task<ManagedServerHealth> StartAndVerifyAsync(
        ManagedLaunchProfile profile,
        string rconPassword,
        Func<double, string, CancellationToken, Task> reportProgressAsync,
        CancellationToken cancellationToken = default);
    Task<bool> StopManagedAsync(CancellationToken cancellationToken = default);
}

public interface IManagedServerControlService
{
    Task<ServerStatus> GetStatusAsync(CancellationToken cancellationToken = default);
    Task<ManagedServerActionResult> ExecuteAsync(
        string action,
        Func<double, string, CancellationToken, Task> reportProgressAsync,
        CancellationToken cancellationToken = default);
    Task<ManagedServerActionResult> ForceStopAsync(
        int expectedProcessId,
        Func<double, string, CancellationToken, Task> reportProgressAsync,
        CancellationToken cancellationToken = default);
}
