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

public sealed class DiagnosticOperationWorker(
    OperationQueue queue,
    IOperationStore operationStore,
    IDiagnosticService diagnosticService,
    ILogger<DiagnosticOperationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var workItem in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await operationStore.MarkRunningAsync(workItem.OperationId, "running-diagnostics", stoppingToken);
                var result = await diagnosticService.RunAsync(workItem.OperationId, stoppingToken);
                await operationStore.CompleteAsync(workItem.OperationId, result, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Diagnostic operation {OperationId} failed", workItem.OperationId);
                await operationStore.FailAsync(
                    workItem.OperationId,
                    new ApiErrorDetail("OPERATION_FAILED", "真实诊断执行失败", workItem.OperationId, true),
                    CancellationToken.None);
            }
        }
    }
}

