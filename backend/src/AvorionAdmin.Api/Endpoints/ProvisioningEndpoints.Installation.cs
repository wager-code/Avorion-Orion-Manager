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

public static partial class ProvisioningEndpoints
{
    private static void MapInstallationEndpoints(WebApplication app)
    {
        app.MapPost("/api/v1/servers/{serverId}/installations/steamcmd", (Func<string, SteamCmdInstallRequest, HttpContext, ISteamCmdInstaller, IOperationStore, SteamCmdInstallQueue, VerifiedCommandRegistry, ServerNodeOptionsAccessor, CancellationToken, Task<IResult>>)async delegate(string serverId, SteamCmdInstallRequest request, HttpContext context, ISteamCmdInstaller installer, IOperationStore store, SteamCmdInstallQueue queue, VerifiedCommandRegistry commands, ServerNodeOptionsAccessor node, CancellationToken cancellationToken)
        {
        	if (!node.Matches(serverId))
        	{
        		return ApiErrors.Create(context, 404, "SERVER_NOT_FOUND", "未找到服务器节点");
        	}
        	if (!commands.IsVerified("steamcmd.install"))
        	{
        		return ApiErrors.Locked(context);
        	}
        	string idempotencyKey = context.Request.Headers["Idempotency-Key"].ToString().Trim();
        	int length = idempotencyKey.Length;
        	if ((length < 8 || length > 128) ? true : false)
        	{
        		return ApiErrors.Create(context, 400, "IDEMPOTENCY_KEY_REQUIRED", "SteamCMD 安装需要 8–128 字符的 Idempotency-Key");
        	}
        	string target;
        	try
        	{
        		target = installer.PrepareTarget(request.InstallDirectory);
        	}
        	catch (SteamCmdInstallException ex)
        	{
        		bool flag;
        		switch (ex.Code)
        		{
        		case "INSTALL_TARGET_EXISTS":
        		case "INSTALL_TARGET_NOT_DIRECTORY":
        		case "INSTALL_TARGET_NOT_STEAMCMD":
        		case "EXISTING_STEAMCMD_SIGNATURE_INVALID":
        			flag = true;
        			break;
        		default:
        			flag = false;
        			break;
        		}
        		int status = (flag ? 409 : 400);
        		return ApiErrors.Create(context, status, ex.Code, ex.Message, ex.Retryable);
        	}
        	string operationId = $"op_{Guid.NewGuid():N}";
        	string plannedServerDirectory = (string.IsNullOrWhiteSpace(request.PlannedServerDirectory) ? null : request.PlannedServerDirectory.Trim());
        	string requestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("steamcmd.install:" + target.ToLowerInvariant() + ":" + plannedServerDirectory?.ToLowerInvariant())));
        	try
        	{
        		JsonElement requestEvidence = JsonSerializer.SerializeToElement(new
        		{
        			installDirectory = target,
        			plannedServerDirectory = plannedServerDirectory
        		});
        		OperationCreateResult created = await store.CreateIdempotentAsync(operationId, "steamcmd.install", idempotencyKey, requestHash, cancellationToken, requestEvidence);
        		if (created.Created)
        		{
        			await queue.EnqueueAsync(new SteamCmdInstallWorkItem(created.Operation.OperationId, target), cancellationToken);
        		}
        		OperationRecord operation = created.Operation;
        		return Results.Accepted("/api/v1/operations/" + operation.OperationId, new OperationAccepted(operation.OperationId, operation.Status, operation.AcceptedAt));
        	}
        	catch (IdempotencyConflictException)
        	{
        		return ApiErrors.Create(context, 409, "IDEMPOTENCY_CONFLICT", "该 Idempotency-Key 已用于不同安装路径");
        	}
        });
        app.MapPost("/api/v1/servers/{serverId}/installations/avorion-server", (Func<string, AvorionServerInstallRequest, HttpContext, IAvorionServerInstaller, IOperationStore, AvorionServerInstallQueue, VerifiedCommandRegistry, ServerNodeOptionsAccessor, CancellationToken, Task<IResult>>)async delegate(string serverId, AvorionServerInstallRequest request, HttpContext context, IAvorionServerInstaller installer, IOperationStore store, AvorionServerInstallQueue queue, VerifiedCommandRegistry commands, ServerNodeOptionsAccessor node, CancellationToken cancellationToken)
        {
        	if (!node.Matches(serverId))
        	{
        		return ApiErrors.Create(context, 404, "SERVER_NOT_FOUND", "未找到服务器节点");
        	}
        	if (!commands.IsVerified("avorion.install"))
        	{
        		return ApiErrors.Locked(context);
        	}
        	string idempotencyKey = context.Request.Headers["Idempotency-Key"].ToString().Trim();
        	int length = idempotencyKey.Length;
        	if ((length < 8 || length > 128) ? true : false)
        	{
        		return ApiErrors.Create(context, 400, "IDEMPOTENCY_KEY_REQUIRED", "Avorion 服务端安装需要 8–128 字符的 Idempotency-Key");
        	}
        	AvorionServerInstallPlan plan;
        	try
        	{
        		plan = installer.Prepare(request.SteamCmdPath, request.InstallDirectory);
        	}
        	catch (AvorionServerInstallException ex)
        	{
        		bool flag;
        		switch (ex.Code)
        		{
        		case "INSTALL_TARGET_EXISTS":
        		case "INSTALL_PATH_OVERLAP":
        		case "INSTALL_TARGET_NOT_DIRECTORY":
        		case "INSTALL_TARGET_NOT_AVORION_SERVER":
        		case "EXISTING_AVORION_INCOMPLETE":
        			flag = true;
        			break;
        		default:
        			flag = false;
        			break;
        		}
        		int status = (flag ? 409 : 400);
        		return ApiErrors.Create(context, status, ex.Code, ex.Message, ex.Retryable);
        	}
        	string operationId = $"op_{Guid.NewGuid():N}";
        	string normalizedRequest = "avorion.install:565060:" + plan.SteamCmdPath.ToLowerInvariant() + ":" + plan.InstallDirectory.ToLowerInvariant();
        	string requestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedRequest)));
        	try
        	{
        		JsonElement requestEvidence = JsonSerializer.SerializeToElement(new
        		{
        			steamCmdPath = plan.SteamCmdPath,
        			installDirectory = plan.InstallDirectory,
        			appId = 565060,
        			branch = "public"
        		});
        		OperationCreateResult created = await store.CreateIdempotentAsync(operationId, "avorion.install", idempotencyKey, requestHash, cancellationToken, requestEvidence);
        		if (created.Created)
        		{
        			await queue.EnqueueAsync(new AvorionServerInstallWorkItem(created.Operation.OperationId, plan.SteamCmdPath, plan.InstallDirectory), cancellationToken);
        		}
        		OperationRecord operation = created.Operation;
        		return Results.Accepted("/api/v1/operations/" + operation.OperationId, new OperationAccepted(operation.OperationId, operation.Status, operation.AcceptedAt));
        	}
        	catch (IdempotencyConflictException)
        	{
        		return ApiErrors.Create(context, 409, "IDEMPOTENCY_CONFLICT", "该 Idempotency-Key 已用于不同的 SteamCMD 或服务端路径");
        	}
        });
    }
}
