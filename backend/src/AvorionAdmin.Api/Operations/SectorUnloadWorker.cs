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

public sealed class SectorUnloadWorker(
    SectorUnloadQueue queue,
    IOperationStore operationStore,
    ManagedServerControlService controlService,
    ILogger<SectorUnloadWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var workItem in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await operationStore.MarkRunningAsync(workItem.OperationId, "validating-empty-sector", stoppingToken);
                await operationStore.UpdateProgressAsync(workItem.OperationId, 30, "requesting-avorion-unload", stoppingToken);
                var result = await controlService.TryUnloadSectorAsync(workItem.X, workItem.Y, stoppingToken);
                await operationStore.UpdateProgressAsync(workItem.OperationId, 90, "verifying-loaded-sectors", stoppingToken);
                await operationStore.CompleteAsync(workItem.OperationId, result, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (ServerControlException exception)
            {
                logger.LogWarning("Sector unload {OperationId} for ({X}, {Y}) failed with {Code}",
                    workItem.OperationId, workItem.X, workItem.Y, exception.Code);
                await operationStore.FailAsync(
                    workItem.OperationId,
                    new ApiErrorDetail(exception.Code, exception.Message, workItem.OperationId, exception.Retryable),
                    CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Sector unload {OperationId} for ({X}, {Y}) failed unexpectedly",
                    workItem.OperationId, workItem.X, workItem.Y);
                await operationStore.FailAsync(
                    workItem.OperationId,
                    new ApiErrorDetail("SECTOR_UNLOAD_FAILED", "空闲星区卸载失败；没有强制修改星区数据", workItem.OperationId, true),
                    CancellationToken.None);
            }
        }
    }
}

