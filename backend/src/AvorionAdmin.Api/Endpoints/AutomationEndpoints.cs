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

public static class AutomationEndpoints
{
    public static WebApplication MapAutomationEndpoints(this WebApplication app)
    {
        app.MapGet("/api/v1/servers/{serverId}/tasks", (Func<string, HttpContext, IAutomationTaskStore, ServerNodeOptionsAccessor, CancellationToken, Task<IResult>>)(async (string serverId, HttpContext context, IAutomationTaskStore store, ServerNodeOptionsAccessor node, CancellationToken cancellationToken) => (!node.Matches(serverId)) ? ApiErrors.Create(context, 404, "SERVER_NOT_FOUND", "未找到服务器节点") : Results.Ok(new AutomationTaskList("Asia/Shanghai", await store.ListAsync(cancellationToken)))));
        app.MapPost("/api/v1/servers/{serverId}/tasks", (Func<string, AutomationTaskUpsertRequest, HttpContext, IAutomationTaskStore, ServerNodeOptionsAccessor, CancellationToken, Task<IResult>>)async delegate(string serverId, AutomationTaskUpsertRequest request, HttpContext context, IAutomationTaskStore store, ServerNodeOptionsAccessor node, CancellationToken cancellationToken)
        {
        	if (!node.Matches(serverId))
        	{
        		return ApiErrors.Create(context, 404, "SERVER_NOT_FOUND", "未找到服务器节点");
        	}
        	string idempotencyKey = context.Request.Headers["Idempotency-Key"].ToString().Trim();
        	int length = idempotencyKey.Length;
        	if ((length < 8 || length > 128) ? true : false)
        	{
        		return ApiErrors.Create(context, 400, "IDEMPOTENCY_KEY_REQUIRED", "新建自动任务需要 8–128 字符的 Idempotency-Key");
        	}
        	string validation = AutomationSchedule.Validate(request);
        	if (validation != null)
        	{
        		return ApiErrors.Create(context, 400, "INVALID_TASK_SCHEDULE", validation);
        	}
        	string requestJson = JsonSerializer.Serialize(request, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        	string requestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(requestJson)));
        	try
        	{
        		string taskId = $"task_{Guid.NewGuid():N}";
        		AutomationTaskRecord task = await store.CreateAsync(taskId, idempotencyKey, requestHash, request, AutomationSchedule.NextRun(request, DateTimeOffset.UtcNow), cancellationToken);
        		return Results.Created("/api/v1/servers/" + serverId + "/tasks/" + task.TaskId, task);
        	}
        	catch (IdempotencyConflictException)
        	{
        		return ApiErrors.Create(context, 409, "IDEMPOTENCY_CONFLICT", "该 Idempotency-Key 已用于不同的自动任务");
        	}
        });
        app.MapPatch("/api/v1/servers/{serverId}/tasks/{taskId}", (Func<string, string, AutomationTaskUpsertRequest, HttpContext, IAutomationTaskStore, ServerNodeOptionsAccessor, CancellationToken, Task<IResult>>)async delegate(string serverId, string taskId, AutomationTaskUpsertRequest request, HttpContext context, IAutomationTaskStore store, ServerNodeOptionsAccessor node, CancellationToken cancellationToken)
        {
        	if (!node.Matches(serverId))
        	{
        		return ApiErrors.Create(context, 404, "SERVER_NOT_FOUND", "未找到服务器节点");
        	}
        	string idempotencyKey = context.Request.Headers["Idempotency-Key"].ToString().Trim();
        	int length = idempotencyKey.Length;
        	if ((length < 8 || length > 128) ? true : false)
        	{
        		return ApiErrors.Create(context, 400, "IDEMPOTENCY_KEY_REQUIRED", "修改自动任务需要 8–128 字符的 Idempotency-Key");
        	}
        	string validation = AutomationSchedule.Validate(request);
        	if (validation != null)
        	{
        		return ApiErrors.Create(context, 400, "INVALID_TASK_SCHEDULE", validation);
        	}
        	AutomationTaskRecord task = await store.UpdateAsync(taskId, request, AutomationSchedule.NextRun(request, DateTimeOffset.UtcNow), cancellationToken);
        	return ((object)task == null) ? ApiErrors.Create(context, 404, "TASK_NOT_FOUND", "未找到自动任务") : Results.Ok(task);
        });
        app.MapDelete("/api/v1/servers/{serverId}/tasks/{taskId}", (Func<string, string, HttpContext, IAutomationTaskStore, ServerNodeOptionsAccessor, CancellationToken, Task<IResult>>)(async (string serverId, string taskId, HttpContext context, IAutomationTaskStore store, ServerNodeOptionsAccessor node, CancellationToken cancellationToken) => (!node.Matches(serverId)) ? ApiErrors.Create(context, 404, "SERVER_NOT_FOUND", "未找到服务器节点") : ((await store.DeleteAsync(taskId, cancellationToken)) ? Results.NoContent() : ApiErrors.Create(context, 404, "TASK_NOT_FOUND", "未找到自动任务"))));
        app.MapGet("/api/v1/servers/{serverId}/memory-policy", (Func<string, HttpContext, IMemoryPolicyStore, ServerNodeOptionsAccessor, CancellationToken, Task<IResult>>)async delegate(string serverId, HttpContext context, IMemoryPolicyStore store, ServerNodeOptionsAccessor node, CancellationToken cancellationToken)
        {
        	if (!node.Matches(serverId))
        	{
        		return ApiErrors.Create(context, 404, "SERVER_NOT_FOUND", "未找到服务器节点");
        	}
        	MemoryPolicy policy = (await store.GetMemoryPolicyAsync(cancellationToken)) ?? new MemoryPolicy(node.Options.MemoryWarningBytes, Notify: true, TryUnloadIdleSectors: false, SafeRestartAtCritical: false, DateTimeOffset.UtcNow);
        	return Results.Ok(policy);
        });
        app.MapPatch("/api/v1/servers/{serverId}/memory-policy", (Func<string, MemoryPolicyUpdateRequest, HttpContext, IMemoryPolicyStore, ServerNodeOptionsAccessor, CancellationToken, Task<IResult>>)async delegate(string serverId, MemoryPolicyUpdateRequest request, HttpContext context, IMemoryPolicyStore store, ServerNodeOptionsAccessor node, CancellationToken cancellationToken)
        {
        	if (!node.Matches(serverId))
        	{
        		return ApiErrors.Create(context, 404, "SERVER_NOT_FOUND", "未找到服务器节点");
        	}
        	long warningThresholdBytes = request.WarningThresholdBytes;
        	if ((warningThresholdBytes < 536870912 || warningThresholdBytes > 1099511627776L) ? true : false)
        	{
        		return ApiErrors.Create(context, 400, "INVALID_MEMORY_POLICY", "内存告警阈值必须在 0.5 GB 到 1024 GB 之间");
        	}
        	return (request.TryUnloadIdleSectors || request.SafeRestartAtCritical) ? ApiErrors.Create(context, 400, "MEMORY_ACTION_NOT_VERIFIED", "自动卸载星区与临界自动重启尚未通过安全验证，当前只能保存告警策略") : Results.Ok(await store.SaveMemoryPolicyAsync(request, cancellationToken));
        });

        return app;
    }
}
