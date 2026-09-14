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

public static class LogEndpoints
{
    public static WebApplication MapLogEndpoints(this WebApplication app)
    {
        app.MapGet("/api/v1/servers/{serverId}/logs", (Func<string, int?, string, string, HttpContext, ILogReader, ServerNodeOptionsAccessor, CancellationToken, Task<IResult>>)async delegate(string serverId, int? limit, string? category, string? query, HttpContext context, ILogReader logReader, ServerNodeOptionsAccessor node, CancellationToken cancellationToken)
        {
        	if (!node.Matches(serverId))
        	{
        		return ApiErrors.Create(context, 404, "SERVER_NOT_FOUND", "未找到服务器节点");
        	}
        	return (!node.HasReadableLogPath()) ? ApiErrors.Create(context, 503, "CAPABILITY_UNAVAILABLE", "日志路径未配置或不可访问") : Results.Ok(new
        	{
        		items = await logReader.ReadRecentAsync(Math.Clamp(limit ?? 200, 1, 500), category, query, cancellationToken),
        		nextCursor = (string)null
        	});
        });
        app.MapGet("/api/v1/servers/{serverId}/logs/stream", (Func<string, HttpContext, ILogReader, ServerNodeOptionsAccessor, CancellationToken, Task>)async delegate(string serverId, HttpContext context, ILogReader logReader, ServerNodeOptionsAccessor node, CancellationToken cancellationToken)
        {
        	if (!node.Matches(serverId))
        	{
        		context.Response.StatusCode = 404;
        		await context.Response.WriteAsJsonAsync(new ApiErrorEnvelope(new ApiErrorDetail("SERVER_NOT_FOUND", "未找到服务器节点", context.TraceIdentifier, Retryable: false)), cancellationToken);
        	}
        	else if (!node.HasReadableLogPath())
        	{
        		context.Response.StatusCode = 503;
        		await context.Response.WriteAsJsonAsync(new ApiErrorEnvelope(new ApiErrorDetail("CAPABILITY_UNAVAILABLE", "日志路径未配置或不可访问", context.TraceIdentifier, Retryable: false)), cancellationToken);
        	}
        	else
        	{
        		context.Response.Headers.CacheControl = "no-cache";
        		context.Response.Headers.Connection = "keep-alive";
        		context.Response.ContentType = "text/event-stream";
        		await context.Response.WriteAsync("retry: 2000\n\n", cancellationToken);
        		await context.Response.Body.FlushAsync(cancellationToken);
        		HashSet<string> sentIds = new HashSet<string>(StringComparer.Ordinal);
        		Queue<string> sentOrder = new Queue<string>();
        		int heartbeat = 0;
        		while (!cancellationToken.IsCancellationRequested)
        		{
        			foreach (LogEntry entry in (await logReader.ReadRecentAsync(100, null, null, cancellationToken)).OrderBy((LogEntry item) => item.OccurredAt))
        			{
        				if (sentIds.Add(entry.Id))
        				{
        					sentOrder.Enqueue(entry.Id);
        					if (sentOrder.Count > 1000)
        					{
        						sentIds.Remove(sentOrder.Dequeue());
        					}
        					string payload = JsonSerializer.Serialize(entry, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        					await context.Response.WriteAsync($"id: {entry.Id}\nevent: log\ndata: {payload}\n\n", cancellationToken);
        				}
        			}
        			heartbeat++;
        			if (heartbeat >= 15)
        			{
        				await context.Response.WriteAsync(": heartbeat\n\n", cancellationToken);
        				heartbeat = 0;
        			}
        			await context.Response.Body.FlushAsync(cancellationToken);
        			await Task.Delay(TimeSpan.FromSeconds(1.0), cancellationToken);
        		}
        	}
        });

        return app;
    }
}
