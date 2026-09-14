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

public static class UpdateCommandEndpoints
{
    public static WebApplication MapUpdateCommandEndpoints(this WebApplication app)
    {
        (string Route, string Action, string Command)[] verifiedUpdateInspections = new(string, string, string)[2]
        {
        	("checks", "check", "avorion.update.check"),
        	("verifications", "verify", "avorion.files.verify")
        };
        (string Route, string Action, string Command)[] array2 = verifiedUpdateInspections;
        for (int num2 = 0; num2 < array2.Length; num2++)
        {
        	(string Route, string Action, string Command) inspection = array2[num2];
        	app.MapPost("/api/v1/servers/{serverId}/updates/" + inspection.Route, (Func<string, HttpContext, IOperationStore, UpdateInspectionQueue, VerifiedCommandRegistry, ServerNodeOptionsAccessor, CancellationToken, Task<IResult>>)async delegate(string serverId, HttpContext context, IOperationStore store, UpdateInspectionQueue queue, VerifiedCommandRegistry commands, ServerNodeOptionsAccessor node, CancellationToken cancellationToken)
        	{
        		if (!node.Matches(serverId))
        		{
        			return ApiErrors.Create(context, 404, "SERVER_NOT_FOUND", "未找到服务器节点");
        		}
        		if (!commands.IsVerified(inspection.Command))
        		{
        			return ApiErrors.Locked(context);
        		}
        		string idempotencyKey = context.Request.Headers["Idempotency-Key"].ToString().Trim();
        		int length = idempotencyKey.Length;
        		if ((length < 8 || length > 128) ? true : false)
        		{
        			return ApiErrors.Create(context, 400, "IDEMPOTENCY_KEY_REQUIRED", "更新检查与文件验证需要 8–128 字符的 Idempotency-Key");
        		}
        		string normalizedRequest = inspection.Command + ":" + serverId.ToLowerInvariant();
        		string requestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedRequest)));
        		string operationId = $"op_{Guid.NewGuid():N}";
        		try
        		{
        			OperationCreateResult created = await store.CreateIdempotentAsync(request: JsonSerializer.SerializeToElement(new
        			{
        				action = inspection.Action,
        				appId = 565060,
        				branch = "public",
        				mutatesServerFiles = false
        			}), operationId: operationId, type: inspection.Command, idempotencyKey: idempotencyKey, requestHash: requestHash, cancellationToken: cancellationToken);
        			if (created.Created)
        			{
        				await queue.EnqueueAsync(new UpdateInspectionWorkItem(created.Operation.OperationId, inspection.Action), cancellationToken);
        			}
        			OperationRecord operation = created.Operation;
        			return Results.Accepted("/api/v1/operations/" + operation.OperationId, new OperationAccepted(operation.OperationId, operation.Status, operation.AcceptedAt));
        		}
        		catch (IdempotencyConflictException)
        		{
        			return ApiErrors.Create(context, 409, "IDEMPOTENCY_CONFLICT", "该 Idempotency-Key 已用于不同的更新检查操作");
        		}
        	});
        }
        app.MapGet("/api/v1/servers/{serverId}/updates/rollback-points/latest", (Func<string, HttpContext, IUpdateRollbackPointService, ServerNodeOptionsAccessor, CancellationToken, Task<IResult>>)(async (string serverId, HttpContext context, IUpdateRollbackPointService service, ServerNodeOptionsAccessor node, CancellationToken cancellationToken) => (!node.Matches(serverId)) ? ApiErrors.Create(context, 404, "SERVER_NOT_FOUND", "未找到服务器节点") : Results.Ok(await service.GetLatestAsync(cancellationToken))));
        app.MapPost("/api/v1/servers/{serverId}/updates/rollback-points", (Func<string, HttpContext, IOperationStore, UpdateRollbackPointQueue, VerifiedCommandRegistry, ServerNodeOptionsAccessor, CancellationToken, Task<IResult>>)async delegate(string serverId, HttpContext context, IOperationStore store, UpdateRollbackPointQueue queue, VerifiedCommandRegistry commands, ServerNodeOptionsAccessor node, CancellationToken cancellationToken)
        {
        	if (!node.Matches(serverId))
        	{
        		return ApiErrors.Create(context, 404, "SERVER_NOT_FOUND", "未找到服务器节点");
        	}
        	if (!commands.IsVerified("avorion.update.rollback-point.create"))
        	{
        		return ApiErrors.Locked(context);
        	}
        	string idempotencyKey = context.Request.Headers["Idempotency-Key"].ToString().Trim();
        	int length = idempotencyKey.Length;
        	if ((length < 8 || length > 128) ? true : false)
        	{
        		return ApiErrors.Create(context, 400, "IDEMPOTENCY_KEY_REQUIRED", "创建更新回滚点需要 8–128 字符的 Idempotency-Key");
        	}
        	string normalizedRequest = "avorion.update.rollback-point.create:" + serverId.ToLowerInvariant();
        	string requestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedRequest)));
        	string operationId = $"op_{Guid.NewGuid():N}";
        	try
        	{
        		JsonElement requestEvidence = JsonSerializer.SerializeToElement(new
        		{
        			appId = 565060,
        			branch = "public",
        			includesServerFiles = true,
        			includesGalaxy = true,
        			restoresFiles = false,
        			startsUpdate = false
        		});
        		OperationCreateResult created = await store.CreateIdempotentAsync(operationId, "avorion.update.rollback-point.create", idempotencyKey, requestHash, cancellationToken, requestEvidence);
        		if (created.Created)
        		{
        			await queue.EnqueueAsync(new UpdateRollbackPointWorkItem(created.Operation.OperationId), cancellationToken);
        		}
        		OperationRecord operation = created.Operation;
        		return Results.Accepted("/api/v1/operations/" + operation.OperationId, new OperationAccepted(operation.OperationId, operation.Status, operation.AcceptedAt));
        	}
        	catch (IdempotencyConflictException)
        	{
        		return ApiErrors.Create(context, 409, "IDEMPOTENCY_CONFLICT", "该 Idempotency-Key 已用于不同的更新安全点操作");
        	}
        });
        string[] lockedRoutes = new string[2] { "/api/v1/servers/{serverId}/updates", "/api/v1/servers/{serverId}/backups" };
        string[] array3 = lockedRoutes;
        foreach (string route in array3)
        {
        	app.MapPost(route, (Func<HttpContext, IResult>)((HttpContext context) => ApiErrors.Locked(context)));
        }

        return app;
    }
}
