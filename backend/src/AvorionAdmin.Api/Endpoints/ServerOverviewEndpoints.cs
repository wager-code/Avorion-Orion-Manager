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

public static class ServerOverviewEndpoints
{
    public static WebApplication MapServerOverviewEndpoints(this WebApplication app)
    {
        app.MapGet("/api/v1/servers", (Func<IManagedServerControlService, CancellationToken, Task<IResult>>)async delegate(IManagedServerControlService control, CancellationToken cancellationToken)
        {
        	ServerStatus status = await control.GetStatusAsync(cancellationToken);
        	return Results.Ok(new
        	{
        		items = new ServerSummary[1]
        		{
        			new ServerSummary(status.ServerId, status.Name, status.Lifecycle, status.Agent.Connected)
        		}
        	});
        });
        app.MapGet("/api/v1/servers/{serverId}/status", (Func<string, HttpContext, IManagedServerControlService, ServerNodeOptionsAccessor, CancellationToken, Task<IResult>>)(async (string serverId, HttpContext context, IManagedServerControlService control, ServerNodeOptionsAccessor node, CancellationToken cancellationToken) => (!node.Matches(serverId)) ? ApiErrors.Create(context, 404, "SERVER_NOT_FOUND", "未找到服务器节点") : Results.Ok(await control.GetStatusAsync(cancellationToken))));
        app.MapGet("/api/v1/servers/{serverId}/events", (Func<string, int?, string, HttpContext, ILogReader, ServerNodeOptionsAccessor, CancellationToken, Task<IResult>>)async delegate(string serverId, int? limit, string? category, HttpContext context, ILogReader logReader, ServerNodeOptionsAccessor node, CancellationToken cancellationToken)
        {
        	if (!node.Matches(serverId))
        	{
        		return ApiErrors.Create(context, 404, "SERVER_NOT_FOUND", "未找到服务器节点");
        	}
        	return (!node.HasReadableLogPath()) ? ApiErrors.Create(context, 503, "CAPABILITY_UNAVAILABLE", "日志路径未配置或不可访问") : Results.Ok(new
        	{
        		items = (await logReader.ReadRecentAsync(Math.Clamp(limit ?? 20, 1, 100), string.IsNullOrWhiteSpace(category) ? null : category, null, cancellationToken)).Select((LogEntry log) => new
        		{
        			eventId = log.Id,
        			occurredAt = log.OccurredAt,
        			kind = EventMapping.ToEventKind(log.Category),
        			message = log.Message,
        			source = "filesystem"
        		})
        	});
        });

        return app;
    }
}
