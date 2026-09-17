#nullable disable
#pragma warning disable AD0001

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AvorionAdmin.Agent;
using AvorionAdmin.Core;
using AvorionAdmin.Core.Abstractions;
using AvorionAdmin.Core.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace AvorionAdmin.Api.Endpoints;

public static class ManagementBridgeEndpoints
{
    public static WebApplication MapManagementBridgeEndpoints(this WebApplication app)
    {
        app.MapGet("/api/v1/servers/{serverId}/management-bridge/status", (Func<string, HttpContext, IManagementBridgeInstaller, IServerSetupDraftStore, ServerNodeOptionsAccessor, CancellationToken, Task<IResult>>)async delegate(string serverId, HttpContext context, IManagementBridgeInstaller installer, IServerSetupDraftStore drafts, ServerNodeOptionsAccessor node, CancellationToken cancellationToken)
        {
            if (!node.Matches(serverId)) return ApiErrors.Create(context, 404, "SERVER_NOT_FOUND", "未找到服务器节点");
            context.Response.Headers.CacheControl = "no-store";
            var draft = await drafts.GetAsync(cancellationToken);
            return Results.Ok(await installer.InspectAsync(draft?.GalaxyDirectory, cancellationToken));
        });
        app.MapGet("/api/v1/servers/{serverId}/management-bridge/{query}", (Func<string, string, int?, int?, int?, HttpContext, ManagedServerControlService, ServerNodeOptionsAccessor, AdminSessionService, CancellationToken, Task<IResult>>)async delegate(string serverId, string query, int? offset, int? limit, int? index, HttpContext context, ManagedServerControlService control, ServerNodeOptionsAccessor node, AdminSessionService sessions, CancellationToken cancellationToken)
        {
        	if (!node.Matches(serverId))
        	{
        		return ApiErrors.Create(context, 404, "SERVER_NOT_FOUND", "未找到服务器节点");
        	}
        	if (!sessions.TryGet(context, out AdminSession _))
        	{
        		return ApiErrors.Create(context, 401, "ADMIN_SESSION_REQUIRED", "查看游戏内数据需要管理员会话");
        	}
        	context.Response.Headers.CacheControl = "no-store";
        	bool flag;
        	switch (query)
        	{
        	case "hello":
        	case "players":
        	case "players-known":
        	case "alliances":
        	case "alliance":
        		flag = true;
        		break;
        	default:
        		flag = false;
        		break;
        	}
        	if (!flag)
        	{
        		return ApiErrors.Create(context, 404, "BRIDGE_QUERY_NOT_FOUND", "未开放该管理组件能力");
        	}
        	try
        	{
        		int first = ((query == "alliance") ? index.GetValueOrDefault() : offset.GetValueOrDefault());
        		JsonElement data = await control.QueryManagementBridgeAsync(query, first, limit ?? ((query == "alliance") ? 20 : 10), cancellationToken);
        		return Results.Ok(new
        		{
        			sampledAt = DateTimeOffset.UtcNow,
        			freshness = "live",
        			data = data
        		});
        	}
        	catch (ServerControlException ex)
        	{
        		return ApiErrors.Create(context, (ex.Code == "BRIDGE_INVALID_QUERY") ? 400 : 503, ex.Code, ex.Message);
        	}
        });

        return app;
    }
}
