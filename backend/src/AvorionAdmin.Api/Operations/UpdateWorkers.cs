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

public sealed class UpdateInspectionWorker(
    UpdateInspectionQueue queue,
    IOperationStore operationStore,
    IUpdateInspectionService inspectionService,
    ILogger<UpdateInspectionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var workItem in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await operationStore.MarkRunningAsync(workItem.OperationId, "validating-update-environment", stoppingToken);
                var result = workItem.Action switch
                {
                    "check" => (object)await inspectionService.CheckAsync(
                        (progress, step, token) => operationStore.UpdateProgressAsync(workItem.OperationId, progress, step, token),
                        stoppingToken),
                    "verify" => await inspectionService.VerifyLocalAsync(
                        (progress, step, token) => operationStore.UpdateProgressAsync(workItem.OperationId, progress, step, token),
                        stoppingToken),
                    _ => throw new InvalidOperationException("Unknown update inspection action")
                };
                await operationStore.CompleteAsync(workItem.OperationId, result, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (UpdateInspectionException exception)
            {
                logger.LogWarning(exception, "Update inspection {OperationId} failed with {Code}", workItem.OperationId, exception.Code);
                await operationStore.FailAsync(
                    workItem.OperationId,
                    new ApiErrorDetail(exception.Code, exception.Message, workItem.OperationId, exception.Retryable, exception.Details),
                    CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Update inspection {OperationId} failed unexpectedly", workItem.OperationId);
                await operationStore.FailAsync(
                    workItem.OperationId,
                    new ApiErrorDetail("UPDATE_INSPECTION_FAILED", "更新检查或本地文件验证失败；未修改服务端文件", workItem.OperationId, true),
                    CancellationToken.None);
            }
        }
    }
}

public sealed class UpdateRollbackPointWorker(
    UpdateRollbackPointQueue queue,
    IOperationStore operationStore,
    IUpdateRollbackPointService rollbackPointService,
    ILogger<UpdateRollbackPointWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var workItem in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await operationStore.MarkRunningAsync(workItem.OperationId, "validating-stopped-server", stoppingToken);
                var result = await rollbackPointService.CreateAsync(
                    workItem.OperationId,
                    (progress, step, token) => operationStore.UpdateProgressAsync(workItem.OperationId, progress, step, token),
                    stoppingToken);
                await operationStore.CompleteAsync(workItem.OperationId, result, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (UpdateRollbackPointException exception)
            {
                logger.LogWarning(exception, "Update rollback point {OperationId} failed with {Code}", workItem.OperationId, exception.Code);
                await operationStore.FailAsync(
                    workItem.OperationId,
                    new ApiErrorDetail(exception.Code, exception.Message, workItem.OperationId, exception.Retryable, exception.Details),
                    CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Update rollback point {OperationId} failed unexpectedly", workItem.OperationId);
                await operationStore.FailAsync(
                    workItem.OperationId,
                    new ApiErrorDetail("ROLLBACK_POINT_CREATE_FAILED", "创建更新回滚点失败；原服务端与 Galaxy 文件未被修改", workItem.OperationId, true),
                    CancellationToken.None);
            }
        }
    }
}

