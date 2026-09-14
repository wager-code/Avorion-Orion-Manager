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

public sealed class AutomationTaskScheduler(
    IAutomationTaskStore taskStore,
    IOperationStore operationStore,
    ServerControlQueue queue,
    ILogger<AutomationTaskScheduler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var due = await taskStore.ClaimDueAsync(DateTimeOffset.UtcNow, 8, stoppingToken);
                foreach (var item in due)
                {
                    var action = item.Task.Kind == "scheduled-save" ? "save" : "restart";
                    var command = item.Task.Kind == "scheduled-save" ? "server.save" : "server.restart";
                    var operationId = $"op_{Guid.NewGuid():N}";
                    var occurrence = item.ScheduledAt.UtcDateTime.ToString("O");
                    var keyMaterial = $"automation:{item.Task.TaskId}:{occurrence}";
                    var idempotencyKey = $"task-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(keyMaterial))).ToLowerInvariant()[..48]}";
                    var requestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{command}:{item.Task.TaskId}:{occurrence}")));
                    var created = await operationStore.CreateIdempotentAsync(
                        operationId,
                        command,
                        idempotencyKey,
                        requestHash,
                        stoppingToken);
                    await taskStore.SetLastOperationAsync(item.Task.TaskId, item.ScheduledAt, created.Operation.OperationId, stoppingToken);
                    if (created.Created)
                        await queue.EnqueueAsync(new ServerControlWorkItem(created.Operation.OperationId, action), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Automatic task scheduler pass failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}

