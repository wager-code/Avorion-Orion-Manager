using AvorionAdmin.Agent;
using AvorionAdmin.Core.Abstractions;
using AvorionAdmin.Core.Models;

namespace AvorionAdmin.Api;

public sealed class AllianceRewardWorker(
    AllianceRewardQueue queue,
    IOperationStore operationStore,
    IAllianceRewardService rewardService,
    ILogger<AllianceRewardWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var workItem in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await operationStore.MarkRunningAsync(workItem.OperationId, "validating-alliance-reward", stoppingToken);
                await operationStore.UpdateProgressAsync(workItem.OperationId, 30, "granting-alliance-assets", stoppingToken);
                var result = await rewardService.GrantAllianceAsync(
                    workItem.AllianceIndex, workItem.Grant, stoppingToken);
                await operationStore.UpdateProgressAsync(workItem.OperationId, 90, "verifying-alliance-assets", stoppingToken);
                await operationStore.CompleteAsync(workItem.OperationId, result, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (ServerControlException exception)
            {
                logger.LogWarning(
                    "Alliance reward {OperationId} for alliance {AllianceIndex} failed with {Code}",
                    workItem.OperationId, workItem.AllianceIndex, exception.Code);
                await operationStore.FailAsync(
                    workItem.OperationId,
                    new ApiErrorDetail(exception.Code, exception.Message, workItem.OperationId, exception.Retryable),
                    CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Alliance reward {OperationId} for alliance {AllianceIndex} failed unexpectedly",
                    workItem.OperationId, workItem.AllianceIndex);
                await operationStore.FailAsync(
                    workItem.OperationId,
                    new ApiErrorDetail(
                        "ALLIANCE_REWARD_FAILED",
                        "联盟奖励执行失败；请刷新资产确认结果，系统不会自动重试",
                        workItem.OperationId,
                        false),
                    CancellationToken.None);
            }
        }
    }
}
