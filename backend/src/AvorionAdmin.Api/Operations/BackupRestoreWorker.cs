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

public sealed class BackupRestoreWorker(
    BackupRestoreQueue queue,
    IOperationStore operationStore,
    IBackupRestoreService restoreService,
    ILogger<BackupRestoreWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var workItem in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await operationStore.MarkRunningAsync(workItem.OperationId, "validating-backup", stoppingToken);
                var result = await restoreService.RestoreAsync(
                    workItem.OperationId,
                    workItem.BackupId,
                    (progress, step, token) => operationStore.UpdateProgressAsync(workItem.OperationId, progress, step, token),
                    stoppingToken);
                await operationStore.CompleteAsync(workItem.OperationId, result, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (BackupRestoreException exception)
            {
                logger.LogWarning(exception, "Backup restore {OperationId} failed with {Code}", workItem.OperationId, exception.Code);
                await operationStore.FailAsync(
                    workItem.OperationId,
                    new ApiErrorDetail(exception.Code, exception.Message, workItem.OperationId, exception.Retryable),
                    CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Backup restore {OperationId} failed unexpectedly", workItem.OperationId);
                await operationStore.FailAsync(
                    workItem.OperationId,
                    new ApiErrorDetail("BACKUP_RESTORE_FAILED", "备份恢复失败；服务器保持停止，恢复前安全点已保留", workItem.OperationId, true),
                    CancellationToken.None);
            }
        }
    }
}

