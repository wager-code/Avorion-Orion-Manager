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

public sealed class ServerControlWorker(
    ServerControlQueue queue,
    IOperationStore operationStore,
    IManagedServerControlService controlService,
    ILogger<ServerControlWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var workItem in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await operationStore.MarkRunningAsync(workItem.OperationId, "validating-managed-profile", stoppingToken);
                var progress = (double value, string step, CancellationToken token) =>
                    operationStore.UpdateProgressAsync(workItem.OperationId, value, step, token);
                var result = workItem.Action == "force-stop" && workItem.ExpectedProcessId is { } expectedProcessId
                    ? await controlService.ForceStopAsync(expectedProcessId, progress, stoppingToken)
                    : await controlService.ExecuteAsync(workItem.Action, progress, stoppingToken);
                await operationStore.CompleteAsync(workItem.OperationId, result, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (ServerControlException exception)
            {
                logger.LogWarning("Server control {OperationId} action {Action} failed with {Code}", workItem.OperationId, workItem.Action, exception.Code);
                await operationStore.FailAsync(
                    workItem.OperationId,
                    new ApiErrorDetail(exception.Code, exception.Message, workItem.OperationId, exception.Retryable),
                    CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Server control {OperationId} action {Action} failed unexpectedly", workItem.OperationId, workItem.Action);
                await operationStore.FailAsync(
                    workItem.OperationId,
                    new ApiErrorDetail("SERVER_CONTROL_FAILED", "服务器控制操作失败；未执行强制终止", workItem.OperationId, true),
                    CancellationToken.None);
            }
        }
    }
}

