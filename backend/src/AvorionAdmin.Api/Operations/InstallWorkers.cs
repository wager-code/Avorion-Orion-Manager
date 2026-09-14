using System.Net;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using AvorionAdmin.Agent;
using AvorionAdmin.Core;
using AvorionAdmin.Core.Abstractions;
using AvorionAdmin.Core.Configuration;
using AvorionAdmin.Core.Models;
using Microsoft.Extensions.Options;

namespace AvorionAdmin.Api;

public sealed class SteamCmdInstallWorker(
    SteamCmdInstallQueue queue,
    IOperationStore operationStore,
    ISteamCmdInstaller installer,
    ILogger<SteamCmdInstallWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var workItem in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await operationStore.MarkRunningAsync(workItem.OperationId, "validating-target", stoppingToken);
                var result = await installer.InstallAsync(
                    workItem.OperationId,
                    workItem.InstallDirectory,
                    (progress, step, token) => operationStore.UpdateProgressAsync(workItem.OperationId, progress, step, token),
                    stoppingToken);
                await operationStore.CompleteAsync(workItem.OperationId, result, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (SteamCmdInstallException exception)
            {
                logger.LogWarning(exception, "SteamCMD installation {OperationId} failed with {Code}", workItem.OperationId, exception.Code);
                await operationStore.FailAsync(
                    workItem.OperationId,
                    new ApiErrorDetail(exception.Code, exception.Message, workItem.OperationId, exception.Retryable),
                    CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "SteamCMD installation {OperationId} failed unexpectedly", workItem.OperationId);
                await operationStore.FailAsync(
                    workItem.OperationId,
                    new ApiErrorDetail("STEAMCMD_INSTALL_FAILED", "SteamCMD 安装失败，暂存内容已清理", workItem.OperationId, true),
                    CancellationToken.None);
            }
        }
    }
}

public sealed class AvorionServerInstallWorker(
    AvorionServerInstallQueue queue,
    IOperationStore operationStore,
    IAvorionServerInstaller installer,
    ILogger<AvorionServerInstallWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var workItem in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await operationStore.MarkRunningAsync(workItem.OperationId, "validating-installation", stoppingToken);
                var result = await installer.InstallAsync(
                    workItem.OperationId,
                    workItem.SteamCmdPath,
                    workItem.InstallDirectory,
                    (progress, step, token) => operationStore.UpdateProgressAsync(workItem.OperationId, progress, step, token),
                    stoppingToken);
                await operationStore.CompleteAsync(workItem.OperationId, result, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (AvorionServerInstallException exception)
            {
                logger.LogWarning(exception, "Avorion server installation {OperationId} failed with {Code}", workItem.OperationId, exception.Code);
                await operationStore.FailAsync(
                    workItem.OperationId,
                    new ApiErrorDetail(exception.Code, exception.Message, workItem.OperationId, exception.Retryable, exception.Details),
                    CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Avorion server installation {OperationId} failed unexpectedly", workItem.OperationId);
                await operationStore.FailAsync(
                    workItem.OperationId,
                    new ApiErrorDetail("AVORION_INSTALL_FAILED", "Avorion 服务端安装失败，服务端暂存目录已清理", workItem.OperationId, true),
                    CancellationToken.None);
            }
        }
    }
}

public sealed class ServerSetupApplicationWorker(
    ServerSetupApplicationQueue queue,
    IOperationStore operationStore,
    IServerSetupApplicationService applicationService,
    ILogger<ServerSetupApplicationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var workItem in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await operationStore.MarkRunningAsync(workItem.OperationId, "revalidating-setup", stoppingToken);
                var result = await applicationService.ApplyAsync(
                    workItem.OperationId,
                    workItem.Request,
                    (progress, step, token) => operationStore.UpdateProgressAsync(workItem.OperationId, progress, step, token),
                    stoppingToken);
                await operationStore.CompleteAsync(workItem.OperationId, result, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (ServerSetupApplicationException exception)
            {
                logger.LogWarning("Server setup application {OperationId} failed with {Code}", workItem.OperationId, exception.Code);
                await operationStore.FailAsync(
                    workItem.OperationId,
                    new ApiErrorDetail(exception.Code, exception.Message, workItem.OperationId, exception.Retryable),
                    CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Server setup application {OperationId} failed unexpectedly", workItem.OperationId);
                await operationStore.FailAsync(
                    workItem.OperationId,
                    new ApiErrorDetail("SETUP_APPLICATION_FAILED", "服务器配置应用失败；未启动服务端", workItem.OperationId, true),
                    CancellationToken.None);
            }
        }
    }
}

public sealed class ServerInitializationWorker(
    ServerInitializationQueue queue,
    IOperationStore operationStore,
    IServerInitializationService initializationService,
    ILogger<ServerInitializationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var workItem in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await operationStore.MarkRunningAsync(workItem.OperationId, "reapplying-launch-profile", stoppingToken);
                var result = await initializationService.InitializeAsync(
                    workItem.OperationId,
                    workItem.Request,
                    (progress, step, token) => operationStore.UpdateProgressAsync(workItem.OperationId, progress, step, token),
                    stoppingToken);
                await operationStore.CompleteAsync(workItem.OperationId, result, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (ServerInitializationException exception)
            {
                logger.LogWarning("Server initialization {OperationId} failed with {Code}", workItem.OperationId, exception.Code);
                await operationStore.FailAsync(
                    workItem.OperationId,
                    new ApiErrorDetail(exception.Code, exception.Message, workItem.OperationId, exception.Retryable),
                    CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Server initialization {OperationId} failed unexpectedly", workItem.OperationId);
                await operationStore.FailAsync(
                    workItem.OperationId,
                    new ApiErrorDetail("SERVER_INITIALIZATION_FAILED", "服务器首次初始化失败；不会强制终止或在运行中修改配置", workItem.OperationId, true),
                    CancellationToken.None);
            }
        }
    }
}

