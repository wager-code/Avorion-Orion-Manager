using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AvorionAdmin.Agent;
using AvorionAdmin.Core.Abstractions;
using AvorionAdmin.Core.Models;

namespace AvorionAdmin.Api.Endpoints;

public static class AllianceEndpoints
{
    public static WebApplication MapAllianceEndpoints(this WebApplication app)
    {
        app.MapGet("/api/v1/servers/{serverId}/alliances/{allianceIndex:int}/assets", async (
            string serverId,
            int allianceIndex,
            HttpContext context,
            ManagedServerControlService control,
            ServerNodeOptionsAccessor node,
            AdminSessionService sessions,
            CancellationToken cancellationToken) =>
        {
            if (!node.Matches(serverId))
                return ApiErrors.Create(context, 404, "SERVER_NOT_FOUND", "未找到服务器节点");
            if (allianceIndex is < 1 or > 100000)
                return ApiErrors.Create(context, 400, "ALLIANCE_INDEX_INVALID", "联盟索引超出允许范围");
            if (!sessions.TryGet(context, out _))
                return ApiErrors.Create(context, 401, "ADMIN_SESSION_REQUIRED", "查看联盟资产需要管理员会话");

            context.Response.Headers.CacheControl = "no-store";
            try
            {
                var data = await control.QueryManagementBridgeAsync(
                    "alliance-assets", allianceIndex, 1, cancellationToken);
                var alliance = data.GetProperty("alliance");
                var resources = data.GetProperty("resources");
                return Results.Ok(new AllianceAssetsSnapshot(
                    alliance.GetProperty("index").GetInt32(),
                    alliance.GetProperty("name").GetString()!,
                    alliance.GetProperty("online").GetBoolean(),
                    data.GetProperty("credits").GetInt64(),
                    new AllianceResourceBalances(
                        resources.GetProperty("iron").GetInt64(),
                        resources.GetProperty("titanium").GetInt64(),
                        resources.GetProperty("naonite").GetInt64(),
                        resources.GetProperty("trinium").GetInt64(),
                        resources.GetProperty("xanion").GetInt64(),
                        resources.GetProperty("ogonite").GetInt64(),
                        resources.GetProperty("avorion").GetInt64()),
                    DateTimeOffset.UtcNow,
                    "live",
                    "avorion-lua-api",
                    data.GetProperty("componentVersion").GetString()!));
            }
            catch (ServerControlException exception)
            {
                var statusCode = exception.Code switch
                {
                    "BRIDGE_INVALID_QUERY" => 400,
                    "ALLIANCE_NOT_FOUND" => 404,
                    _ => 503
                };
                return ApiErrors.Create(context, statusCode, exception.Code, exception.Message, exception.Retryable);
            }
        });

        app.MapPost("/api/v1/servers/{serverId}/alliances/{allianceIndex:int}/reward-grants", async (
            string serverId,
            int allianceIndex,
            AllianceRewardRequest request,
            HttpContext context,
            IOperationStore store,
            AllianceRewardQueue queue,
            VerifiedCommandRegistry commands,
            ServerNodeOptionsAccessor node,
            CancellationToken cancellationToken) =>
        {
            if (!node.Matches(serverId))
                return ApiErrors.Create(context, 404, "SERVER_NOT_FOUND", "未找到服务器节点");
            if (!commands.IsVerified("alliance.reward.grant"))
                return ApiErrors.Locked(context);
            if (allianceIndex is < 1 or > 100000)
                return ApiErrors.Create(context, 400, "ALLIANCE_INDEX_INVALID", "联盟索引超出允许范围");

            var validation = AllianceRewardPolicy.Validate(request);
            if (validation is not null)
                return ApiErrors.Create(context, 400, validation.Code, validation.Message);
            if (!string.Equals(request.Confirmation, $"GRANT ALLIANCE {allianceIndex}", StringComparison.Ordinal))
                return ApiErrors.Create(
                    context, 400, "DANGER_CONFIRMATION_REQUIRED", "确认信息已失效，请重新核对联盟与奖励内容");

            var idempotencyKey = context.Request.Headers["Idempotency-Key"].ToString().Trim();
            if (idempotencyKey.Length is < 8 or > 128)
                return ApiErrors.Create(
                    context, 400, "IDEMPOTENCY_KEY_REQUIRED", "联盟奖励需要 8–128 字符的 Idempotency-Key");

            var grant = request.Grant!;
            var canonicalRequest = JsonSerializer.Serialize(new
            {
                serverId = serverId.ToLowerInvariant(),
                allianceIndex,
                grant.Credits,
                grant.Resources
            });
            var requestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalRequest)));
            var operationId = $"op_{Guid.NewGuid():N}";
            try
            {
                var evidence = JsonSerializer.SerializeToElement(new
                {
                    allianceIndex,
                    grant,
                    confirmationMatched = true,
                    maximumCreditsPerGrant = AllianceRewardPolicy.MaximumCreditsPerGrant,
                    maximumResourcePerGrant = AllianceRewardPolicy.MaximumResourcePerGrant,
                    automaticRetry = false
                });
                var created = await store.CreateIdempotentAsync(
                    operationId, "alliance.reward.grant", idempotencyKey, requestHash, cancellationToken, evidence);
                if (created.Created)
                    await queue.EnqueueAsync(
                        new AllianceRewardWorkItem(created.Operation.OperationId, allianceIndex, grant),
                        cancellationToken);
                var operation = created.Operation;
                return Results.Accepted(
                    $"/api/v1/operations/{operation.OperationId}",
                    new OperationAccepted(operation.OperationId, operation.Status, operation.AcceptedAt));
            }
            catch (IdempotencyConflictException)
            {
                return ApiErrors.Create(
                    context, 409, "IDEMPOTENCY_CONFLICT", "该 Idempotency-Key 已用于不同的联盟奖励请求");
            }
        });

        return app;
    }
}
