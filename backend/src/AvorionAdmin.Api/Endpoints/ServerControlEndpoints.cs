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

public static class ServerControlEndpoints
{
    public static WebApplication MapServerControlEndpoints(this WebApplication app)
    {
        (string Action, string Command)[] verifiedControlActions = new(string, string)[4]
        {
        	("start", "server.start"),
        	("save", "server.save"),
        	("shutdown", "server.shutdown"),
        	("restart", "server.restart")
        };
        (string Action, string Command)[] array = verifiedControlActions;
        for (int num = 0; num < array.Length; num++)
        {
        	(string Action, string Command) controlAction = array[num];
        	app.MapPost("/api/v1/servers/{serverId}/actions/" + controlAction.Action, (Func<string, HttpContext, IOperationStore, ServerControlQueue, VerifiedCommandRegistry, ServerNodeOptionsAccessor, CancellationToken, Task<IResult>>)async delegate(string serverId, HttpContext context, IOperationStore store, ServerControlQueue queue, VerifiedCommandRegistry commands, ServerNodeOptionsAccessor node, CancellationToken cancellationToken)
        	{
        		if (!node.Matches(serverId))
        		{
        			return ApiErrors.Create(context, 404, "SERVER_NOT_FOUND", "未找到服务器节点");
        		}
        		if (!commands.IsVerified(controlAction.Command))
        		{
        			return ApiErrors.Locked(context);
        		}
        		string idempotencyKey = context.Request.Headers["Idempotency-Key"].ToString().Trim();
        		int length = idempotencyKey.Length;
        		if ((length < 8 || length > 128) ? true : false)
        		{
        			return ApiErrors.Create(context, 400, "IDEMPOTENCY_KEY_REQUIRED", "服务器控制操作需要 8–128 字符的 Idempotency-Key");
        		}
        		string normalizedRequest = controlAction.Command + ":" + serverId.ToLowerInvariant();
        		string requestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedRequest)));
        		string operationId = $"op_{Guid.NewGuid():N}";
        		try
        		{
        			OperationCreateResult created = await store.CreateIdempotentAsync(request: JsonSerializer.SerializeToElement(new
        			{
        				action = controlAction.Action
        			}), operationId: operationId, type: controlAction.Command, idempotencyKey: idempotencyKey, requestHash: requestHash, cancellationToken: cancellationToken);
        			if (created.Created)
        			{
        				await queue.EnqueueAsync(new ServerControlWorkItem(created.Operation.OperationId, controlAction.Action), cancellationToken);
        			}
        			OperationRecord operation = created.Operation;
        			return Results.Accepted("/api/v1/operations/" + operation.OperationId, new OperationAccepted(operation.OperationId, operation.Status, operation.AcceptedAt));
        		}
        		catch (IdempotencyConflictException)
        		{
        			return ApiErrors.Create(context, 409, "IDEMPOTENCY_CONFLICT", "该 Idempotency-Key 已用于不同的服务器控制操作");
        		}
        	});
        }
        app.MapPost("/api/v1/servers/{serverId}/actions/force-stop", (Func<string, ForceStopRequest, HttpContext, IManagedServerControlService, IOperationStore, ServerControlQueue, VerifiedCommandRegistry, ServerNodeOptionsAccessor, CancellationToken, Task<IResult>>)async delegate(string serverId, ForceStopRequest request, HttpContext context, IManagedServerControlService control, IOperationStore store, ServerControlQueue queue, VerifiedCommandRegistry commands, ServerNodeOptionsAccessor node, CancellationToken cancellationToken)
        {
        	if (!node.Matches(serverId))
        	{
        		return ApiErrors.Create(context, 404, "SERVER_NOT_FOUND", "未找到服务器节点");
        	}
        	if (!commands.IsVerified("server.force-stop"))
        	{
        		return ApiErrors.Locked(context);
        	}
        	if (!string.Equals(request.Confirmation, $"FORCE STOP {request.ExpectedProcessId}", StringComparison.Ordinal))
        	{
        		return ApiErrors.Create(context, 400, "DANGER_CONFIRMATION_REQUIRED", "强制终止确认文本无效，请刷新页面后重新确认当前 PID");
        	}
        	ServerStatus status = await control.GetStatusAsync(cancellationToken);
        	if (status.Lifecycle != Lifecycle.Running || !status.ProcessId.HasValue)
        	{
        		return ApiErrors.Create(context, 409, "SERVER_NOT_RUNNING", "受管服务器没有运行，无需强制终止");
        	}
        	if (status.ProcessId != request.ExpectedProcessId)
        	{
        		return ApiErrors.Create(context, 409, "PROCESS_ID_CHANGED", "受管进程 PID 已变化，请刷新状态后重新确认");
        	}
        	string idempotencyKey = context.Request.Headers["Idempotency-Key"].ToString().Trim();
        	int length = idempotencyKey.Length;
        	if ((length < 8 || length > 128) ? true : false)
        	{
        		return ApiErrors.Create(context, 400, "IDEMPOTENCY_KEY_REQUIRED", "强制终止需要 8–128 字符的 Idempotency-Key");
        	}
        	string normalizedRequest = $"server.force-stop:{serverId.ToLowerInvariant()}:{request.ExpectedProcessId}";
        	string requestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedRequest)));
        	string operationId = $"op_{Guid.NewGuid():N}";
        	try
        	{
        		JsonElement evidence = JsonSerializer.SerializeToElement(new
        		{
        			action = "force-stop",
        			expectedProcessId = request.ExpectedProcessId,
        			confirmationMatched = true
        		});
        		OperationCreateResult created = await store.CreateIdempotentAsync(operationId, "server.force-stop", idempotencyKey, requestHash, cancellationToken, evidence);
        		if (created.Created)
        		{
        			await queue.EnqueueAsync(new ServerControlWorkItem(created.Operation.OperationId, "force-stop", request.ExpectedProcessId), cancellationToken);
        		}
        		OperationRecord operation = created.Operation;
        		return Results.Accepted("/api/v1/operations/" + operation.OperationId, new OperationAccepted(operation.OperationId, operation.Status, operation.AcceptedAt));
        	}
        	catch (IdempotencyConflictException)
        	{
        		return ApiErrors.Create(context, 409, "IDEMPOTENCY_CONFLICT", "该 Idempotency-Key 已用于不同的强制终止请求");
        	}
        });

        return app;
    }
}
