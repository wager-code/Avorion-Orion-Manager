using System.Security.Cryptography;
using System.Text;
using AvorionAdmin.Agent;
using AvorionAdmin.Core.Abstractions;
using AvorionAdmin.Core.Models;

namespace AvorionAdmin.Api;

public sealed class RewardBatchWorker(
    RewardBatchQueue queue,
    IOperationStore operationStore,
    IPlayerRewardService rewardService,
    IPlayerMailService mailService,
    ILogger<RewardBatchWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var workItem in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await RunBatchAsync(workItem, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Reward batch {OperationId} failed unexpectedly", workItem.OperationId);
                await operationStore.FailAsync(
                    workItem.OperationId,
                    new ApiErrorDetail(
                        "REWARD_BATCH_FAILED",
                        "奖励批次执行失败；已完成的逐人操作不会自动重试，请在活动记录中核对",
                        workItem.OperationId,
                        false),
                    CancellationToken.None);
            }
        }
    }

    private async Task RunBatchAsync(RewardBatchWorkItem workItem, CancellationToken cancellationToken)
    {
        var request = workItem.Request;
        var targets = request.PlayerIndexes!;
        var grant = request.Grant!;
        var delivery = request.Delivery!;
        var results = new List<RewardBatchTargetResult>(targets.Count);

        await operationStore.MarkRunningAsync(workItem.OperationId, "validating-reward-batch", cancellationToken);
        for (var targetOffset = 0; targetOffset < targets.Count; targetOffset++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var playerIndex = targets[targetOffset];
            var childId = CreateChildOperationId(workItem.OperationId, playerIndex);
            var deliveryId = CreateDeliveryId(workItem.OperationId, playerIndex);
            var childType = delivery == RewardDeliveryModes.Mail
                ? "reward.batch.target.mail"
                : "reward.batch.target.direct";
            var childKey = $"reward-target-{workItem.OperationId}-{playerIndex}";
            var childRequestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                $"{workItem.OperationId}|{delivery}|{playerIndex}|{deliveryId}")));
            var childEvidence = System.Text.Json.JsonSerializer.SerializeToElement(new
            {
                batchOperationId = workItem.OperationId,
                playerIndex,
                delivery,
                deliveryId = delivery == RewardDeliveryModes.Mail ? deliveryId : null,
                grant,
                automaticRetry = false
            });
            var created = await operationStore.CreateIdempotentAsync(
                childId, childType, childKey, childRequestHash, cancellationToken, childEvidence);
            if (!created.Created)
                throw new InvalidOperationException("Duplicate reward-batch child operation");

            await operationStore.MarkRunningAsync(childId, "delivering-player-reward", cancellationToken);
            try
            {
                RewardBatchTargetResult targetResult;
                if (delivery == RewardDeliveryModes.Mail)
                {
                    var mailResult = await mailService.SendMailAsync(
                        playerIndex, grant, request.Mail!, deliveryId, cancellationToken);
                    targetResult = new RewardBatchTargetResult(
                        childId,
                        playerIndex,
                        mailResult.PlayerName,
                        "succeeded",
                        mailResult.DeliveryId,
                        mailResult.Replayed ? "already-delivered-and-verified" : mailResult.Outcome,
                        null,
                        null,
                        mailResult.MailId,
                        mailResult.MailIndex,
                        null,
                        null);
                }
                else
                {
                    var rewardResult = await rewardService.GrantAsync(playerIndex, grant, cancellationToken);
                    targetResult = new RewardBatchTargetResult(
                        childId,
                        playerIndex,
                        rewardResult.PlayerName,
                        "succeeded",
                        null,
                        rewardResult.Outcome,
                        rewardResult.Before,
                        rewardResult.After,
                        null,
                        null,
                        null,
                        null);
                }

                await operationStore.CompleteAsync(childId, targetResult, cancellationToken);
                results.Add(targetResult);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (ServerControlException exception)
            {
                logger.LogWarning(
                    "Reward batch {BatchOperationId} target {PlayerIndex} failed with {Code}",
                    workItem.OperationId,
                    playerIndex,
                    exception.Code);
                await operationStore.FailAsync(
                    childId,
                    new ApiErrorDetail(exception.Code, exception.Message, childId, exception.Retryable),
                    CancellationToken.None);
                results.Add(new RewardBatchTargetResult(
                    childId, playerIndex, null, "failed",
                    delivery == RewardDeliveryModes.Mail ? deliveryId : null,
                    null, null, null, null, null, exception.Code, exception.Message));
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Reward batch {BatchOperationId} target {PlayerIndex} failed unexpectedly",
                    workItem.OperationId,
                    playerIndex);
                const string message = "该玩家奖励执行失败；系统不会自动重试";
                await operationStore.FailAsync(
                    childId,
                    new ApiErrorDetail("REWARD_TARGET_FAILED", message, childId, false),
                    CancellationToken.None);
                results.Add(new RewardBatchTargetResult(
                    childId, playerIndex, null, "failed",
                    delivery == RewardDeliveryModes.Mail ? deliveryId : null,
                    null, null, null, null, null, "REWARD_TARGET_FAILED", message));
            }

            var progress = 10d + 85d * (targetOffset + 1) / targets.Count;
            await operationStore.UpdateProgressAsync(
                workItem.OperationId, progress, "delivering-player-rewards", cancellationToken);
        }

        var succeeded = results.Count(result => result.Status == "succeeded");
        var failed = results.Count - succeeded;
        await operationStore.CompleteAsync(
            workItem.OperationId,
            new RewardBatchResult(
                workItem.OperationId,
                delivery,
                targets.Count,
                succeeded,
                failed,
                results,
                failed == 0 ? "completed" : "completed-with-failures",
                DateTimeOffset.UtcNow),
            cancellationToken);
    }

    private static string CreateChildOperationId(string batchOperationId, int playerIndex)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{batchOperationId}|target|{playerIndex}"));
        return $"op_{Convert.ToHexString(hash.AsSpan(0, 16)).ToLowerInvariant()}";
    }

    private static string CreateDeliveryId(string batchOperationId, int playerIndex)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{batchOperationId}|mail|{playerIndex}"));
        return Convert.ToHexString(hash.AsSpan(0, 16)).ToLowerInvariant();
    }
}
