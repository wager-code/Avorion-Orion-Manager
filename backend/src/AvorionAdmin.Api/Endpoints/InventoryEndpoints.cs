using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AvorionAdmin.Agent;
using AvorionAdmin.Core.Abstractions;
using AvorionAdmin.Core.Models;

namespace AvorionAdmin.Api.Endpoints;

public static class InventoryEndpoints
{
    public static WebApplication MapInventoryEndpoints(this WebApplication app)
    {
        app.MapGet("/api/v1/servers/{serverId}/players/{ownerIndex:int}/inventory",
            (string serverId, int ownerIndex, int? offset, int? limit, HttpContext context,
                IInventoryService service, ServerNodeOptionsAccessor node, AdminSessionService sessions,
                CancellationToken cancellationToken) =>
                ReadAsync(serverId, InventoryOwnerKinds.Player, ownerIndex, offset, limit, context,
                    service, node, sessions, cancellationToken));

        app.MapGet("/api/v1/servers/{serverId}/alliances/{ownerIndex:int}/inventory",
            (string serverId, int ownerIndex, int? offset, int? limit, HttpContext context,
                IInventoryService service, ServerNodeOptionsAccessor node, AdminSessionService sessions,
                CancellationToken cancellationToken) =>
                ReadAsync(serverId, InventoryOwnerKinds.Alliance, ownerIndex, offset, limit, context,
                    service, node, sessions, cancellationToken));

        app.MapPost("/api/v1/servers/{serverId}/players/{ownerIndex:int}/system-upgrade-grants",
            (string serverId, int ownerIndex, SystemUpgradeGrantRequest request, HttpContext context,
                IOperationStore store, InventoryGrantQueue queue, VerifiedCommandRegistry commands,
                ServerNodeOptionsAccessor node, CancellationToken cancellationToken) =>
                CreateGrantAsync(serverId, InventoryOwnerKinds.Player, ownerIndex, request, context,
                    store, queue, commands, node, cancellationToken));

        app.MapPost("/api/v1/servers/{serverId}/alliances/{ownerIndex:int}/system-upgrade-grants",
            (string serverId, int ownerIndex, SystemUpgradeGrantRequest request, HttpContext context,
                IOperationStore store, InventoryGrantQueue queue, VerifiedCommandRegistry commands,
                ServerNodeOptionsAccessor node, CancellationToken cancellationToken) =>
                CreateGrantAsync(serverId, InventoryOwnerKinds.Alliance, ownerIndex, request, context,
                    store, queue, commands, node, cancellationToken));

        return app;
    }

    private static async Task<IResult> ReadAsync(
        string serverId,
        string ownerKind,
        int ownerIndex,
        int? offset,
        int? limit,
        HttpContext context,
        IInventoryService service,
        ServerNodeOptionsAccessor node,
        AdminSessionService sessions,
        CancellationToken cancellationToken)
    {
        if (!node.Matches(serverId))
            return ApiErrors.Create(context, 404, "SERVER_NOT_FOUND", "未找到服务器节点");
        var ownerValidation = InventoryPolicy.ValidateOwner(ownerKind, ownerIndex);
        if (ownerValidation is not null)
            return ApiErrors.Create(context, 400, ownerValidation.Code, ownerValidation.Message);
        if (!sessions.TryGet(context, out _))
            return ApiErrors.Create(context, 401, "ADMIN_SESSION_REQUIRED", "查看库存需要管理员会话");
        var safeOffset = Math.Clamp(offset ?? 0, 0, 100000);
        var safeLimit = Math.Clamp(limit ?? 50, 1, InventoryPolicy.MaximumPageSize);
        context.Response.Headers.CacheControl = "no-store";
        try
        {
            return Results.Ok(await service.QueryInventoryAsync(
                ownerKind, ownerIndex, safeOffset, safeLimit, cancellationToken));
        }
        catch (ServerControlException exception)
        {
            var statusCode = exception.Code switch
            {
                "BRIDGE_INVALID_QUERY" => 400,
                "PLAYER_NOT_FOUND" or "ALLIANCE_NOT_FOUND" => 404,
                _ => 503
            };
            return ApiErrors.Create(context, statusCode, exception.Code, exception.Message, exception.Retryable);
        }
    }

    private static async Task<IResult> CreateGrantAsync(
        string serverId,
        string ownerKind,
        int ownerIndex,
        SystemUpgradeGrantRequest request,
        HttpContext context,
        IOperationStore store,
        InventoryGrantQueue queue,
        VerifiedCommandRegistry commands,
        ServerNodeOptionsAccessor node,
        CancellationToken cancellationToken)
    {
        if (!node.Matches(serverId))
            return ApiErrors.Create(context, 404, "SERVER_NOT_FOUND", "未找到服务器节点");
        var command = ownerKind == InventoryOwnerKinds.Player
            ? "player.inventory.system-upgrade.grant"
            : "alliance.inventory.system-upgrade.grant";
        if (!commands.IsVerified(command)) return ApiErrors.Locked(context);
        var ownerValidation = InventoryPolicy.ValidateOwner(ownerKind, ownerIndex);
        if (ownerValidation is not null)
            return ApiErrors.Create(context, 400, ownerValidation.Code, ownerValidation.Message);
        var validation = InventoryPolicy.ValidateGrant(request);
        if (validation is not null)
            return ApiErrors.Create(context, 400, validation.Code, validation.Message);

        var ownerWord = ownerKind == InventoryOwnerKinds.Player ? "PLAYER" : "ALLIANCE";
        if (!string.Equals(request.Confirmation, $"GRANT {ownerWord} SYSTEM {ownerIndex}", StringComparison.Ordinal))
            return ApiErrors.Create(context, 400, "DANGER_CONFIRMATION_REQUIRED", "确认信息已失效，请重新核对库存归属与插件");

        var idempotencyKey = context.Request.Headers["Idempotency-Key"].ToString().Trim();
        if (idempotencyKey.Length is < 8 or > 128)
            return ApiErrors.Create(context, 400, "IDEMPOTENCY_KEY_REQUIRED", "插件发放需要 8–128 字符的 Idempotency-Key");

        var canonical = JsonSerializer.Serialize(new
        {
            serverId = serverId.ToLowerInvariant(),
            ownerKind,
            ownerIndex,
            upgradeKey = request.UpgradeKey,
            rarity = request.Rarity
        });
        var requestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        var operationId = $"op_{Guid.NewGuid():N}";
        try
        {
            var definition = SystemUpgradeCatalog.Items[request.UpgradeKey!];
            var evidence = JsonSerializer.SerializeToElement(new
            {
                ownerKind,
                ownerIndex,
                definition.Key,
                definition.Script,
                rarity = request.Rarity,
                whitelistMatched = true,
                confirmationMatched = true,
                automaticRetry = false
            });
            var created = await store.CreateIdempotentAsync(
                operationId, command, idempotencyKey, requestHash, cancellationToken, evidence);
            if (created.Created)
            {
                var seed = created.Operation.OperationId["op_".Length..];
                await queue.EnqueueAsync(new InventoryGrantWorkItem(
                    created.Operation.OperationId,
                    ownerKind,
                    ownerIndex,
                    request.UpgradeKey!,
                    request.Rarity!,
                    seed), cancellationToken);
            }
            var operation = created.Operation;
            return Results.Accepted(
                $"/api/v1/operations/{operation.OperationId}",
                new OperationAccepted(operation.OperationId, operation.Status, operation.AcceptedAt));
        }
        catch (IdempotencyConflictException)
        {
            return ApiErrors.Create(context, 409, "IDEMPOTENCY_CONFLICT", "该 Idempotency-Key 已用于不同的插件发放请求");
        }
    }
}
