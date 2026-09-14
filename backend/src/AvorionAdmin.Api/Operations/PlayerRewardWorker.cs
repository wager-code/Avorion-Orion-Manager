using AvorionAdmin.Agent;
using AvorionAdmin.Core.Abstractions;
using AvorionAdmin.Core.Models;

namespace AvorionAdmin.Api;

public sealed class PlayerRewardWorker(
    PlayerRewardQueue queue,
    IOperationStore operationStore,
    IPlayerRewardService rewardService,
    ILogger<PlayerRewardWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var workItem in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await operationStore.MarkRunningAsync(workItem.OperationId, "validating-player-reward", stoppingToken);
                await operationStore.UpdateProgressAsync(workItem.OperationId, 30, "granting-player-assets", stoppingToken);
                var result = await rewardService.GrantAsync(workItem.PlayerIndex, workItem.Grant, stoppingToken);
                await operationStore.UpdateProgressAsync(workItem.OperationId, 90, "verifying-player-assets", stoppingToken);
                await operationStore.CompleteAsync(workItem.OperationId, result, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (ServerControlException exception)
            {
                logger.LogWarning(
                    "Player reward {OperationId} for player {PlayerIndex} failed with {Code}",
                    workItem.OperationId,
                    workItem.PlayerIndex,
                    exception.Code);
                await operationStore.FailAsync(
                    workItem.OperationId,
                    new ApiErrorDetail(exception.Code, exception.Message, workItem.OperationId, exception.Retryable),
                    CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Player reward {OperationId} for player {PlayerIndex} failed unexpectedly",
                    workItem.OperationId,
                    workItem.PlayerIndex);
                await operationStore.FailAsync(
                    workItem.OperationId,
                    new ApiErrorDetail(
                        "PLAYER_REWARD_FAILED",
                        "玩家奖励执行失败；请刷新资产确认结果，系统不会自动重试",
                        workItem.OperationId,
                        false),
                    CancellationToken.None);
            }
        }
    }
}
