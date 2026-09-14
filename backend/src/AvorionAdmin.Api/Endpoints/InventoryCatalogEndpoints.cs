using AvorionAdmin.Core.Abstractions;
using AvorionAdmin.Core.Models;

namespace AvorionAdmin.Api.Endpoints;

public static class InventoryCatalogEndpoints
{
    public static WebApplication MapInventoryCatalogEndpoints(this WebApplication app)
    {
        app.MapGet("/api/v1/servers/{serverId}/inventory-catalog", async (
            string serverId,
            string? query,
            string? itemType,
            string? grantPolicy,
            int? offset,
            int? limit,
            HttpContext context,
            IInventoryCatalogService catalog,
            ServerNodeOptionsAccessor node,
            AdminSessionService sessions,
            CancellationToken cancellationToken) =>
        {
            if (!node.Matches(serverId))
                return ApiErrors.Create(context, 404, "SERVER_NOT_FOUND", "未找到服务器节点");
            if (!sessions.TryGet(context, out _))
                return ApiErrors.Create(context, 401, "ADMIN_SESSION_REQUIRED", "查看物品目录需要管理员会话");
            var request = new InventoryCatalogQuery(
                string.IsNullOrWhiteSpace(query) ? null : query.Trim(),
                string.IsNullOrWhiteSpace(itemType) ? null : itemType.Trim(),
                string.IsNullOrWhiteSpace(grantPolicy) ? null : grantPolicy.Trim(),
                offset ?? 0,
                limit ?? InventoryCatalogPolicy.MaximumPageSize);
            var error = InventoryCatalogPolicy.Validate(request);
            if (error is not null) return ApiErrors.Create(context, 400, error.Code, error.Message);
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(await catalog.QueryAsync(request, cancellationToken));
        });

        app.MapGet("/api/v1/servers/{serverId}/inventory-icons", async (
            string serverId,
            string? path,
            HttpContext context,
            IInventoryCatalogService catalog,
            ServerNodeOptionsAccessor node,
            AdminSessionService sessions,
            CancellationToken cancellationToken) =>
        {
            if (!node.Matches(serverId))
                return ApiErrors.Create(context, 404, "SERVER_NOT_FOUND", "未找到服务器节点");
            if (!sessions.TryGet(context, out _))
                return ApiErrors.Create(context, 401, "ADMIN_SESSION_REQUIRED", "读取游戏图标需要管理员会话");
            var normalized = InventoryCatalogPolicy.NormalizeIconPath(path);
            if (normalized is null)
                return ApiErrors.Create(context, 400, "INVENTORY_ICON_PATH_INVALID", "游戏图标路径无效");
            var icon = await catalog.ReadIconAsync(normalized, cancellationToken);
            if (icon is null)
                return ApiErrors.Create(context, 404, "INVENTORY_ICON_NOT_FOUND", "当前客户端资源中没有该图标");
            context.Response.Headers.CacheControl = "private,max-age=3600";
            context.Response.Headers.ETag = $"\"{icon.ETag}\"";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            return Results.File(icon.Content, icon.ContentType);
        });

        return app;
    }
}
