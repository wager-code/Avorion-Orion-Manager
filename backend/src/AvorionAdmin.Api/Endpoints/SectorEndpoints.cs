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

public static class SectorEndpoints
{
    public static WebApplication MapSectorEndpoints(this WebApplication app)
    {
        app.MapGet("/api/v1/servers/{serverId}/memory", (Func<string, HttpContext, IServerProbe, IPerformanceStore, IMemoryPolicyStore, ServerNodeOptionsAccessor, CancellationToken, Task<IResult>>)async delegate(string serverId, HttpContext context, IServerProbe probe, IPerformanceStore store, IMemoryPolicyStore policyStore, ServerNodeOptionsAccessor node, CancellationToken cancellationToken)
        {
        	if (!node.Matches(serverId))
        	{
        		return ApiErrors.Create(context, 404, "SERVER_NOT_FOUND", "未找到服务器节点");
        	}
        	PerformanceSnapshot current = await probe.GetPerformanceAsync(cancellationToken);
        	PerformanceHistory history = await store.QueryAsync("1h", cancellationToken);
        	long? baseline = history.Points.FirstOrDefault((PerformancePoint point) => point.MemoryWorkingSetBytes.HasValue)?.MemoryWorkingSetBytes;
        	long? currentBytes = current.Memory.WorkingSetBytes;
        	long? growth = ((!baseline.HasValue || !currentBytes.HasValue) ? ((long?)null) : (currentBytes - baseline));
        	double? growthPerHour = null;
        	if (history.Points.Count >= 2 && growth.HasValue)
        	{
        		IReadOnlyList<PerformancePoint> points = history.Points;
        		double hours = (points[points.Count - 1].At - history.Points[0].At).TotalHours;
        		if (hours > 0.0)
        		{
        			growthPerHour = (double?)growth / hours;
        		}
        	}
        	long warning = (await policyStore.GetMemoryPolicyAsync(cancellationToken))?.WarningThresholdBytes ?? node.Options.MemoryWarningBytes;
        	return Results.Ok(new MemoryOverview(AlertLevel: (!currentBytes.HasValue) ? "unknown" : ((currentBytes >= warning) ? "warning" : "normal"), CurrentWorkingSetBytes: currentBytes, StartupBaselineBytes: baseline, GrowthBytes: growth, GrowthBytesPerHour: growthPerHour, WarningThresholdBytes: warning, LoadedSectorCount: null, PlayerSectorCount: null, IdleSectorCount: null, Provenance: current.Provenance with
        	{
        		UnavailableReason = ((!currentBytes.HasValue) ? "AVORION_PROCESS_NOT_RUNNING" : null)
        	}));
        });
        app.MapGet("/api/v1/servers/{serverId}/sectors", (Func<string, HttpContext, int?, int?, ManagedServerControlService, ServerNodeOptionsAccessor, CancellationToken, Task<IResult>>)async delegate(string serverId, HttpContext context, int? offset, int? limit, ManagedServerControlService control, ServerNodeOptionsAccessor node, CancellationToken cancellationToken)
        {
        	if (!node.Matches(serverId))
        	{
        		return ApiErrors.Create(context, 404, "SERVER_NOT_FOUND", "未找到服务器节点");
        	}
        	int safeOffset = Math.Clamp(offset.GetValueOrDefault(), 0, 100000);
        	int safeLimit = Math.Clamp(limit ?? 50, 1, 50);
        	try
        	{
        		return Results.Ok(await control.QueryManagementBridgeAsync("sectors", safeOffset, safeLimit, cancellationToken));
        	}
        	catch (ServerControlException ex)
        	{
        		return ApiErrors.Create(context, 503, ex.Code, ex.Message, ex.Retryable);
        	}
        });
        app.MapPost("/api/v1/servers/{serverId}/sectors/unload-attempts", (Func<string, SectorUnloadRequest, HttpContext, IOperationStore, SectorUnloadQueue, VerifiedCommandRegistry, ServerNodeOptionsAccessor, CancellationToken, Task<IResult>>)async delegate(string serverId, SectorUnloadRequest request, HttpContext context, IOperationStore store, SectorUnloadQueue queue, VerifiedCommandRegistry commands, ServerNodeOptionsAccessor node, CancellationToken cancellationToken)
        {
        	if (!node.Matches(serverId))
        	{
        		return ApiErrors.Create(context, 404, "SERVER_NOT_FOUND", "未找到服务器节点");
        	}
        	if (!commands.IsVerified("sector.unload"))
        	{
        		return ApiErrors.Locked(context);
        	}
        	int x = request.X;
        	bool flag = ((x < -500 || x > 500) ? true : false);
        	bool flag2 = flag;
        	bool flag3 = flag2;
        	if (!flag3)
        	{
        		int y = request.Y;
        		bool flag4 = ((y < -500 || y > 500) ? true : false);
        		flag3 = flag4;
        	}
        	if (flag3)
        	{
        		return ApiErrors.Create(context, 400, "SECTOR_COORDINATES_INVALID", "星区坐标必须在 -500 到 500 之间");
        	}
        	if (!string.Equals(request.Confirmation, $"UNLOAD {request.X} {request.Y}", StringComparison.Ordinal))
        	{
        		return ApiErrors.Create(context, 400, "DANGER_CONFIRMATION_REQUIRED", "星区卸载确认文本无效，请刷新加载列表后重新确认");
        	}
        	string idempotencyKey = context.Request.Headers["Idempotency-Key"].ToString().Trim();
        	x = idempotencyKey.Length;
        	if ((x < 8 || x > 128) ? true : false)
        	{
        		return ApiErrors.Create(context, 400, "IDEMPOTENCY_KEY_REQUIRED", "星区卸载需要 8–128 字符的 Idempotency-Key");
        	}
        	string requestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"sector.unload:{serverId.ToLowerInvariant()}:{request.X}:{request.Y}")));
        	string operationId = $"op_{Guid.NewGuid():N}";
        	try
        	{
        		JsonElement evidence = JsonSerializer.SerializeToElement(new
        		{
        			x = request.X,
        			y = request.Y,
        			confirmationMatched = true,
        			requiresNoOnlinePlayersInSector = true,
        			forceUnload = false
        		});
        		OperationCreateResult created = await store.CreateIdempotentAsync(operationId, "sector.unload", idempotencyKey, requestHash, cancellationToken, evidence);
        		if (created.Created)
        		{
        			await queue.EnqueueAsync(new SectorUnloadWorkItem(created.Operation.OperationId, request.X, request.Y), cancellationToken);
        		}
        		OperationRecord operation = created.Operation;
        		return Results.Accepted("/api/v1/operations/" + operation.OperationId, new OperationAccepted(operation.OperationId, operation.Status, operation.AcceptedAt));
        	}
        	catch (IdempotencyConflictException)
        	{
        		return ApiErrors.Create(context, 409, "IDEMPOTENCY_CONFLICT", "该 Idempotency-Key 已用于不同的星区卸载请求");
        	}
        });

        return app;
    }
}
