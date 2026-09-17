using AvorionAdmin.Core.Models;

namespace AvorionAdmin.Core.Abstractions;

public interface IUpdateEnvironmentService
{
    Task<UpdateEnvironmentDetection> DetectAsync(CancellationToken cancellationToken = default);
    Task<UpdateEnvironmentValidation> ValidateAsync(
        string steamCmdPath,
        string serverDirectory,
        CancellationToken cancellationToken = default);
}

public interface IUpdateEnvironmentStore
{
    Task<UpdateEnvironmentConfiguration?> GetAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(UpdateEnvironmentConfiguration configuration, CancellationToken cancellationToken = default);
}

public interface IUpdateInspectionService
{
    Task<UpdateCheckResult> CheckAsync(
        Func<double, string, CancellationToken, Task> reportProgressAsync,
        CancellationToken cancellationToken = default);
    Task<UpdateVerificationResult> VerifyLocalAsync(
        Func<double, string, CancellationToken, Task> reportProgressAsync,
        CancellationToken cancellationToken = default);
}

public interface IUpdateRollbackPointService
{
    Task<UpdateRollbackPointResult> CreateAsync(
        string operationId,
        Func<double, string, CancellationToken, Task> reportProgressAsync,
        CancellationToken cancellationToken = default);
    Task<UpdateRollbackPointAvailability> GetLatestAsync(CancellationToken cancellationToken = default);
}

public interface IServerSetupDraftStore
{
    Task<ServerSetupDraftConfiguration?> GetAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(ServerSetupDraftConfiguration configuration, CancellationToken cancellationToken = default);
}

public interface IServerSetupPreflightService
{
    Task<ServerSetupPreflightResult> ValidateAsync(
        ServerSetupPreflightRequest request,
        CancellationToken cancellationToken = default);
}

public interface IServerSetupApplicationService
{
    Task<ServerSetupApplicationResult> ApplyAsync(
        string operationId,
        ServerSetupPreflightRequest request,
        Func<double, string, CancellationToken, Task> reportProgressAsync,
        CancellationToken cancellationToken = default);
}

public interface IServerInitializationService
{
    Task<ServerInitializationResult> InitializeAsync(
        string operationId,
        ServerSetupPreflightRequest request,
        Func<double, string, CancellationToken, Task> reportProgressAsync,
        CancellationToken cancellationToken = default);
}

public interface ISteamCmdInstaller
{
    string PrepareTarget(string installDirectory);
    Task<SteamCmdInstallationResult> InstallAsync(
        string operationId,
        string installDirectory,
        Func<double, string, CancellationToken, Task> reportProgressAsync,
        CancellationToken cancellationToken = default);
}

public interface IAvorionServerInstaller
{
    AvorionServerInstallPlan Prepare(string steamCmdPath, string installDirectory);
    Task<AvorionServerInstallationResult> InstallAsync(
        string operationId,
        string steamCmdPath,
        string installDirectory,
        Func<double, string, CancellationToken, Task> reportProgressAsync,
        CancellationToken cancellationToken = default);
}

public interface IManagementBridgeInstaller
{
    Task<ManagementBridgeStatus> InspectAsync(
        string? galaxyDirectory,
        CancellationToken cancellationToken = default);
    Task<ManagementBridgeInstallationResult> InstallAsync(
        string galaxyDirectory,
        string operationId,
        CancellationToken cancellationToken = default);
}
