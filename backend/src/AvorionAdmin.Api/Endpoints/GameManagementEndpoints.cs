using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AvorionAdmin.Core.Abstractions;
using AvorionAdmin.Core.Models;

namespace AvorionAdmin.Api.Endpoints;

public static class GameManagementEndpoints
{
    public static WebApplication MapGameManagementEndpoints(this WebApplication app)
    {
        app.MapGet("/api/v1/servers/{serverId}/reward-batches", async (
            string serverId,
            int? limit,
            HttpContext context,
            IOperationStore store,
            ServerNodeOptionsAccessor node,
            AdminSessionService sessions,
            CancellationToken cancellationToken) =>
        {
            if (!node.Matches(serverId))
                return ApiErrors.Create(context, 404, "SERVER_NOT_FOUND", "未找到服务器节点");
            if (!sessions.TryGet(context, out _))
                return ApiErrors.Create(context, 401, "ADMIN_SESSION_REQUIRED", "查看奖励记录需要管理员会话");

            context.Response.Headers.CacheControl = "no-store";
            var requested = Math.Clamp(limit ?? 10, 1, 30);
            var items = (await store.ListRecentAsync(100, cancellationToken))
                .Where(item => item.Type == "reward.batch.create")
                .Take(requested)
                .ToArray();
            return Results.Ok(new { items });
        });

        app.MapPost("/api/v1/servers/{serverId}/reward-batches", async (
            string serverId,
            RewardBatchRequest request,
            HttpContext context,
            IOperationStore store,
            RewardBatchQueue queue,
            VerifiedCommandRegistry commands,
            ServerNodeOptionsAccessor node,
            CancellationToken cancellationToken) =>
        {
            if (!node.Matches(serverId))
                return ApiErrors.Create(context, 404, "SERVER_NOT_FOUND", "未找到服务器节点");
            if (!commands.IsVerified("reward.batch.create"))
                return ApiErrors.Locked(context);

            var validation = RewardBatchPolicy.Validate(request);
            if (validation is not null)
                return ApiErrors.Create(context, 400, validation.Code, validation.Message);
            var targetCount = request.PlayerIndexes!.Count;
            if (!string.Equals(request.Confirmation, $"CREATE PLAYER BATCH {targetCount}", StringComparison.Ordinal))
                return ApiErrors.Create(
                    context, 400, "DANGER_CONFIRMATION_REQUIRED", "确认信息已失效，请重新核对目标与奖励内容");

            var idempotencyKey = context.Request.Headers["Idempotency-Key"].ToString().Trim();
            if (idempotencyKey.Length is < 8 or > 128)
                return ApiErrors.Create(
                    context, 400, "IDEMPOTENCY_KEY_REQUIRED", "奖励批次需要 8–128 字符的 Idempotency-Key");

            var canonicalRequest = JsonSerializer.Serialize(new
            {
                serverId = serverId.ToLowerInvariant(),
                delivery = request.Delivery,
                playerIndexes = request.PlayerIndexes,
                request.Grant!.Credits,
                request.Grant.Resources,
                mail = request.Delivery == RewardDeliveryModes.Mail ? request.Mail : null
            });
            var requestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalRequest)));
            var operationId = $"op_{Guid.NewGuid():N}";
            try
            {
                var evidence = JsonSerializer.SerializeToElement(new
                {
                    delivery = request.Delivery,
                    playerIndexes = request.PlayerIndexes,
                    grant = request.Grant,
                    mail = request.Mail,
                    confirmationMatched = true,
                    maximumTargets = RewardBatchPolicy.MaximumTargets,
                    automaticRetry = false,
                    targetOperationsPersistent = true
                });
                var created = await store.CreateIdempotentAsync(
                    operationId,
                    "reward.batch.create",
                    idempotencyKey,
                    requestHash,
                    cancellationToken,
                    evidence);
                if (created.Created)
                    await queue.EnqueueAsync(
                        new RewardBatchWorkItem(created.Operation.OperationId, request), cancellationToken);
                var operation = created.Operation;
                return Results.Accepted(
                    $"/api/v1/operations/{operation.OperationId}",
                    new OperationAccepted(operation.OperationId, operation.Status, operation.AcceptedAt));
            }
            catch (IdempotencyConflictException)
            {
                return ApiErrors.Create(
                    context, 409, "IDEMPOTENCY_CONFLICT", "该 Idempotency-Key 已用于不同的奖励批次");
            }
        });

        return app;
    }
}
