using AvorionAdmin.Agent;
using AvorionAdmin.Core.Abstractions;
using AvorionAdmin.Core.Models;

namespace AvorionAdmin.Api;

public sealed class InventoryGrantWorker(
    InventoryGrantQueue queue,
    IOperationStore operationStore,
    IInventoryService inventoryService,
    ILogger<InventoryGrantWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var workItem in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await operationStore.MarkRunningAsync(workItem.OperationId, "validating-system-upgrade", stoppingToken);
                await operationStore.UpdateProgressAsync(workItem.OperationId, 35, "adding-whitelisted-system-upgrade", stoppingToken);
                var result = await inventoryService.GrantSystemUpgradeAsync(
                    workItem.OwnerKind,
                    workItem.OwnerIndex,
                    workItem.UpgradeKey,
                    workItem.Rarity,
                    workItem.Seed,
                    stoppingToken);
                await operationStore.UpdateProgressAsync(workItem.OperationId, 90, "verifying-inventory-delta", stoppingToken);
                await operationStore.CompleteAsync(workItem.OperationId, result, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (ServerControlException exception)
            {
                logger.LogWarning(
                    "Inventory grant {OperationId} for {OwnerKind} {OwnerIndex} failed with {Code}",
                    workItem.OperationId, workItem.OwnerKind, workItem.OwnerIndex, exception.Code);
                await operationStore.FailAsync(
                    workItem.OperationId,
                    new ApiErrorDetail(exception.Code, exception.Message, workItem.OperationId, exception.Retryable),
                    CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Inventory grant {OperationId} for {OwnerKind} {OwnerIndex} failed unexpectedly",
                    workItem.OperationId, workItem.OwnerKind, workItem.OwnerIndex);
                await operationStore.FailAsync(
                    workItem.OperationId,
                    new ApiErrorDetail(
                        "SYSTEM_UPGRADE_GRANT_FAILED",
                        "系统插件发放结果无法确认；请刷新库存核对，系统不会自动重试",
                        workItem.OperationId,
                        false),
                    CancellationToken.None);
            }
        }
    }
}
