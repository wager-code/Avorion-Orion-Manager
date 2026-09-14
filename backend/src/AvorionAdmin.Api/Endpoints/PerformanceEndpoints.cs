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

public static class PerformanceEndpoints
{
    public static WebApplication MapPerformanceEndpoints(this WebApplication app)
    {
        app.MapGet("/api/v1/servers/{serverId}/performance/current", (Func<string, HttpContext, IServerProbe, ServerNodeOptionsAccessor, CancellationToken, Task<IResult>>)(async (string serverId, HttpContext context, IServerProbe probe, ServerNodeOptionsAccessor node, CancellationToken cancellationToken) => (!node.Matches(serverId)) ? ApiErrors.Create(context, 404, "SERVER_NOT_FOUND", "未找到服务器节点") : Results.Ok(await probe.GetPerformanceAsync(cancellationToken))));
        app.MapGet("/api/v1/servers/{serverId}/performance/history", (Func<string, string, HttpContext, IPerformanceStore, ServerNodeOptionsAccessor, CancellationToken, Task<IResult>>)async delegate(string serverId, string range, HttpContext context, IPerformanceStore store, ServerNodeOptionsAccessor node, CancellationToken cancellationToken)
        {
        	if (!node.Matches(serverId))
        	{
        		return ApiErrors.Create(context, 404, "SERVER_NOT_FOUND", "未找到服务器节点");
        	}
        	bool flag;
        	switch (range)
        	{
        	case "1h":
        	case "24h":
        	case "7d":
        		flag = true;
        		break;
        	default:
        		flag = false;
        		break;
        	}
        	return (!flag) ? ApiErrors.Create(context, 400, "VALIDATION_FAILED", "range 仅支持 1h、24h、7d") : Results.Ok(await store.QueryAsync(range, cancellationToken));
        });

        return app;
    }
}
