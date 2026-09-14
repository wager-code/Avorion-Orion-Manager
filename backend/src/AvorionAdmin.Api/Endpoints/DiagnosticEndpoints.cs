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

public static class DiagnosticEndpoints
{
    public static WebApplication MapDiagnosticEndpoints(this WebApplication app)
    {
        app.MapPost("/api/v1/servers/{serverId}/diagnostics/runs", (Func<string, HttpContext, IOperationStore, OperationQueue, ServerNodeOptionsAccessor, CancellationToken, Task<IResult>>)async delegate(string serverId, HttpContext context, IOperationStore store, OperationQueue queue, ServerNodeOptionsAccessor node, CancellationToken cancellationToken)
        {
        	if (!node.Matches(serverId))
        	{
        		return ApiErrors.Create(context, 404, "SERVER_NOT_FOUND", "未找到服务器节点");
        	}
        	string idempotencyKey = context.Request.Headers["Idempotency-Key"].ToString().Trim();
        	int length = idempotencyKey.Length;
        	if ((length < 8 || length > 128) ? true : false)
        	{
        		return ApiErrors.Create(context, 400, "IDEMPOTENCY_KEY_REQUIRED", "任务写操作需要 8–128 字符的 Idempotency-Key");
        	}
        	string operationId = $"op_{Guid.NewGuid():N}";
        	string requestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("diagnostics:" + serverId.ToLowerInvariant())));
        	try
        	{
        		OperationCreateResult created = await store.CreateIdempotentAsync(operationId, "diagnostics", idempotencyKey, requestHash, cancellationToken);
        		if (created.Created)
        		{
        			await queue.EnqueueAsync(new DiagnosticWorkItem(created.Operation.OperationId), cancellationToken);
        		}
        		OperationRecord operation = created.Operation;
        		return Results.Accepted("/api/v1/operations/" + operation.OperationId, new OperationAccepted(operation.OperationId, operation.Status, operation.AcceptedAt));
        	}
        	catch (IdempotencyConflictException)
        	{
        		return ApiErrors.Create(context, 409, "IDEMPOTENCY_CONFLICT", "该 Idempotency-Key 已用于不同请求");
        	}
        });
        app.MapGet("/api/v1/servers/{serverId}/diagnostics/runs/{runId}", (Func<string, string, HttpContext, IOperationStore, ServerNodeOptionsAccessor, CancellationToken, Task<IResult>>)async delegate(string serverId, string runId, HttpContext context, IOperationStore store, ServerNodeOptionsAccessor node, CancellationToken cancellationToken)
        {
        	if (!node.Matches(serverId))
        	{
        		return ApiErrors.Create(context, 404, "SERVER_NOT_FOUND", "未找到服务器节点");
        	}
        	OperationRecord operation = await store.GetAsync(runId, cancellationToken);
        	if ((object)operation == null || operation.Type != "diagnostics")
        	{
        		return ApiErrors.Create(context, 404, "OPERATION_NOT_FOUND", "未找到诊断记录");
        	}
        	DiagnosticRun run = JsonHelpers.ReadDiagnosticRun(operation);
        	return ((object)run == null) ? Results.Ok(new
        	{
        		runId = runId,
        		status = operation.Status.ToString().ToLowerInvariant(),
        		items = Array.Empty<object>()
        	}) : Results.Ok(run);
        });
        app.MapGet("/api/v1/operations/{operationId}", (Func<string, HttpContext, IOperationStore, CancellationToken, Task<IResult>>)async delegate(string operationId, HttpContext context, IOperationStore store, CancellationToken cancellationToken)
        {
        	OperationRecord operation = await store.GetAsync(operationId, cancellationToken);
        	return ((object)operation == null) ? ApiErrors.Create(context, 404, "OPERATION_NOT_FOUND", "未找到操作记录") : Results.Ok(operation);
        });
        app.MapGet("/api/v1/operations", (Func<int?, IOperationStore, CancellationToken, Task<IResult>>)(async (int? limit, IOperationStore store, CancellationToken cancellationToken) => Results.Ok(new
        {
        	items = await store.ListRecentAsync(Math.Clamp(limit ?? 30, 1, 100), cancellationToken)
        })));

        return app;
    }
}
