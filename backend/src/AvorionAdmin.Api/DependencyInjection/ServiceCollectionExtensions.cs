using System.Text.Json;
using System.Text.Json.Serialization;
using AvorionAdmin.Agent;
using AvorionAdmin.Core;
using AvorionAdmin.Core.Abstractions;
using AvorionAdmin.Core.Configuration;

namespace AvorionAdmin.Api.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOrionAdminModules(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ServerNodeOptions>(configuration.GetSection(ServerNodeOptions.SectionName));
        services.Configure<SecurityOptions>(configuration.GetSection(SecurityOptions.SectionName));
        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        });
        services.AddSingleton<IServerProbe, SystemProbe>();
        services.AddSingleton<ISteamQueryProbe, SteamQueryProbe>();
        services.AddSingleton<BackupScanner>();
        services.AddSingleton<IBackupScanner>(provider => provider.GetRequiredService<BackupScanner>());
        services.AddSingleton<IBackupRestoreService, BackupRestoreService>();
        services.AddSingleton<IServerRuntimePathResolver, ServerRuntimePathResolver>();
        services.AddSingleton<ILogReader, LogReader>();
        services.AddSingleton<IFileSystemBrowser, FileSystemBrowser>();
        services.AddSingleton<IUpdateEnvironmentService, UpdateEnvironmentService>();
        services.AddSingleton<IManagementBridgeInstaller, ManagementBridgeInstaller>();
        services.AddSingleton<IUpdateInspectionService, UpdateInspectionService>();
        services.AddSingleton<IUpdateRollbackPointService, UpdateRollbackPointService>();
        services.AddSingleton<ISteamCmdArchiveSource, OfficialSteamCmdArchiveSource>();
        services.AddSingleton<IAuthenticodeVerifier, WindowsAuthenticodeVerifier>();
        services.AddSingleton<ISteamCmdInstaller, SteamCmdInstaller>();
        services.AddSingleton<IAvorionSteamCmdRunner, AvorionSteamCmdRunner>();
        services.AddSingleton<IAvorionServerInstaller, AvorionServerInstaller>();
        services.AddSingleton<SqliteStore>();
        services.AddSingleton<IPerformanceStore>(provider => provider.GetRequiredService<SqliteStore>());
        services.AddSingleton<IOperationStore>(provider => provider.GetRequiredService<SqliteStore>());
        services.AddSingleton<IUpdateEnvironmentStore>(provider => provider.GetRequiredService<SqliteStore>());
        services.AddSingleton<IServerSetupDraftStore>(provider => provider.GetRequiredService<SqliteStore>());
        services.AddSingleton<IAutomationTaskStore>(provider => provider.GetRequiredService<SqliteStore>());
        services.AddSingleton<IMemoryPolicyStore>(provider => provider.GetRequiredService<SqliteStore>());
        services.AddSingleton<IDiagnosticService, DiagnosticService>();
        services.AddSingleton<IServerSetupPreflightService, ServerSetupPreflightService>();
        services.AddSingleton<IServerSetupApplicationService, ServerSetupApplicationService>();
        services.AddSingleton<ManagedServerProcessRegistry>();
        services.AddSingleton<IManagedServerRuntime, ManagedServerRuntime>();
        services.AddSingleton<ManagedServerControlService>();
        services.AddSingleton<IManagedServerControlService>(provider => provider.GetRequiredService<ManagedServerControlService>());
        services.AddSingleton<IPlayerRewardService>(provider => provider.GetRequiredService<ManagedServerControlService>());
        services.AddSingleton<IAllianceRewardService>(provider => provider.GetRequiredService<ManagedServerControlService>());
        services.AddSingleton<IPlayerMailService>(provider => provider.GetRequiredService<ManagedServerControlService>());
        services.AddSingleton<IInventoryService>(provider => provider.GetRequiredService<ManagedServerControlService>());
        services.AddSingleton<IInventoryCatalogService, InventoryCatalogService>();
        services.AddSingleton<IServerInitializationService, ServerInitializationService>();
        services.AddSingleton<OperationQueue>();
        services.AddSingleton<SteamCmdInstallQueue>();
        services.AddSingleton<AvorionServerInstallQueue>();
        services.AddSingleton<ServerSetupApplicationQueue>();
        services.AddSingleton<ServerInitializationQueue>();
        services.AddSingleton<ServerControlQueue>();
        services.AddSingleton<UpdateInspectionQueue>();
        services.AddSingleton<UpdateRollbackPointQueue>();
        services.AddSingleton<BackupRestoreQueue>();
        services.AddSingleton<SectorUnloadQueue>();
        services.AddSingleton<PlayerRewardQueue>();
        services.AddSingleton<AllianceRewardQueue>();
        services.AddSingleton<RewardBatchQueue>();
        services.AddSingleton<InventoryGrantQueue>();
        services.AddSingleton<AdminSessionService>();
        services.AddSingleton<VerifiedCommandRegistry>();
        services.AddSingleton<ServerNodeOptionsAccessor>();
        services.AddHostedService<PerformanceSamplingWorker>();
        services.AddHostedService<DiagnosticOperationWorker>();
        services.AddHostedService<SteamCmdInstallWorker>();
        services.AddHostedService<AvorionServerInstallWorker>();
        services.AddHostedService<ServerSetupApplicationWorker>();
        services.AddHostedService<ServerInitializationWorker>();
        services.AddHostedService<ServerControlWorker>();
        services.AddHostedService<UpdateInspectionWorker>();
        services.AddHostedService<UpdateRollbackPointWorker>();
        services.AddHostedService<BackupRestoreWorker>();
        services.AddHostedService<SectorUnloadWorker>();
        services.AddHostedService<PlayerRewardWorker>();
        services.AddHostedService<AllianceRewardWorker>();
        services.AddHostedService<RewardBatchWorker>();
        services.AddHostedService<InventoryGrantWorker>();
        services.AddHostedService<AutomationTaskScheduler>();
        return services;
    }
}
