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
    private static void MapServerSetupEndpoints(WebApplication app)
    {
        app.MapGet("/api/v1/servers/{serverId}/server-setup/draft", (Func<string, HttpContext, IServerSetupDraftStore, ServerNodeOptionsAccessor, CancellationToken, Task<IResult>>)(async (string serverId, HttpContext context, IServerSetupDraftStore store, ServerNodeOptionsAccessor node, CancellationToken cancellationToken) => (!node.Matches(serverId)) ? ApiErrors.Create(context, 404, "SERVER_NOT_FOUND", "未找到服务器节点") : Results.Ok(new
        {
        	draft = await store.GetAsync(cancellationToken)
        })));
        app.MapPut("/api/v1/servers/{serverId}/server-setup/draft", (Func<string, ServerSetupDraftRequest, HttpContext, IServerSetupDraftStore, ServerNodeOptionsAccessor, CancellationToken, Task<IResult>>)async delegate(string serverId, ServerSetupDraftRequest request, HttpContext context, IServerSetupDraftStore store, ServerNodeOptionsAccessor node, CancellationToken cancellationToken)
        {
        	if (!node.Matches(serverId))
        	{
        		return ApiErrors.Create(context, 404, "SERVER_NOT_FOUND", "未找到服务器节点");
        	}
        	bool flag = request.ServerName == null || request.GalaxyName == null || request.GalaxyDirectory == null || request.ListenAddress == null || request.ServerName.Length > 256 || request.GalaxyName.Length > 256 || request.GalaxyDirectory.Length > 2048 || request.ListenAddress.Length > 128;
        	bool flag2 = flag;
        	if (!flag2)
        	{
        		string galaxyMode = request.GalaxyMode;
        		bool flag3 = ((galaxyMode == "new" || galaxyMode == "existing") ? true : false);
        		flag2 = !flag3;
        	}
        	if (flag2)
        	{
        		return ApiErrors.Create(context, 400, "DRAFT_LIMIT_EXCEEDED", "配置草稿字段超出允许范围");
        	}
        	ServerSetupDraftConfiguration configuration = new ServerSetupDraftConfiguration(request.ServerName, request.GalaxyName, request.GalaxyMode, request.MaxPlayers, request.GalaxyDirectory, request.ListenAddress, request.GamePort, request.QueryPort, request.RconEnabled, request.RconPort, request.AllowFirewallChange, InstallManagementMod: request.InstallManagementMod, DateTimeOffset.UtcNow);
        	await store.SaveAsync(configuration, cancellationToken);
        	return Results.Ok(configuration);
        });
        app.MapPost("/api/v1/servers/{serverId}/server-setup/preflight", (Func<string, ServerSetupPreflightRequest, HttpContext, IServerSetupPreflightService, ServerNodeOptionsAccessor, CancellationToken, Task<IResult>>)async delegate(string serverId, ServerSetupPreflightRequest request, HttpContext context, IServerSetupPreflightService service, ServerNodeOptionsAccessor node, CancellationToken cancellationToken)
        {
        	if (!node.Matches(serverId))
        	{
        		return ApiErrors.Create(context, 404, "SERVER_NOT_FOUND", "未找到服务器节点");
        	}
        	int num4;
        	if (request.ServerName != null && request.GalaxyName != null && request.GalaxyDirectory != null && request.ListenAddress != null && request.ServerName.Length <= 256 && request.GalaxyName.Length <= 256 && request.GalaxyDirectory.Length <= 2048 && request.ListenAddress.Length <= 128)
        	{
        		string? rconPassword = request.RconPassword;
        		num4 = ((rconPassword != null && rconPassword.Length > 128) ? 1 : 0);
        	}
        	else
        	{
        		num4 = 1;
        	}
        	if (num4 != 0)
        	{
        		return ApiErrors.Create(context, 400, "PREFLIGHT_LIMIT_EXCEEDED", "配置预检字段超出允许范围");
        	}
        	return Results.Ok(await service.ValidateAsync(request, cancellationToken));
        });
        app.MapPost("/api/v1/servers/{serverId}/server-setup/applications", (Func<string, ServerSetupPreflightRequest, HttpContext, IServerSetupPreflightService, IOperationStore, ServerSetupApplicationQueue, VerifiedCommandRegistry, ServerNodeOptionsAccessor, CancellationToken, Task<IResult>>)async delegate(string serverId, ServerSetupPreflightRequest request, HttpContext context, IServerSetupPreflightService preflightService, IOperationStore store, ServerSetupApplicationQueue queue, VerifiedCommandRegistry commands, ServerNodeOptionsAccessor node, CancellationToken cancellationToken)
        {
        	if (!node.Matches(serverId))
        	{
        		return ApiErrors.Create(context, 404, "SERVER_NOT_FOUND", "未找到服务器节点");
        	}
        	if (!commands.IsVerified("server.configure"))
        	{
        		return ApiErrors.Locked(context);
        	}
        	int num4;
        	if (request.ServerName != null && request.GalaxyName != null && request.GalaxyDirectory != null && request.ListenAddress != null && request.ServerName.Length <= 256 && request.GalaxyName.Length <= 256 && request.GalaxyDirectory.Length <= 2048 && request.ListenAddress.Length <= 128)
        	{
        		string? rconPassword = request.RconPassword;
        		num4 = ((rconPassword != null && rconPassword.Length > 128) ? 1 : 0);
        	}
        	else
        	{
        		num4 = 1;
        	}
        	if (num4 != 0)
        	{
        		return ApiErrors.Create(context, 400, "SETUP_LIMIT_EXCEEDED", "服务器配置字段超出允许范围");
        	}
        	string idempotencyKey = context.Request.Headers["Idempotency-Key"].ToString().Trim();
        	int length = idempotencyKey.Length;
        	if ((length < 8 || length > 128) ? true : false)
        	{
        		return ApiErrors.Create(context, 400, "IDEMPOTENCY_KEY_REQUIRED", "应用服务器配置需要 8–128 字符的 Idempotency-Key");
        	}
        	ServerSetupPreflightResult preflight = await preflightService.ValidateAsync(request, cancellationToken);
        	if (!preflight.Valid)
        	{
        		string message = string.Join("；", from issue in preflight.Issues
        			where issue.Severity == "error"
        			select issue.Message);
        		return ApiErrors.Create(context, 409, "SETUP_PREFLIGHT_FAILED", message);
        	}
        	string passwordHash = ((request.RconEnabled && request.RconPassword != null) ? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.RconPassword))) : string.Empty);
        	string normalizedRequest = JsonSerializer.Serialize(new
        	{
        		command = "server.configure",
        		ServerName = request.ServerName,
        		GalaxyName = request.GalaxyName,
        		GalaxyMode = request.GalaxyMode,
        		MaxPlayers = request.MaxPlayers,
        		galaxyDirectory = Path.GetFullPath(request.GalaxyDirectory).ToLowerInvariant(),
        		ListenAddress = request.ListenAddress,
        		GamePort = request.GamePort,
        		QueryPort = request.QueryPort,
        		RconEnabled = request.RconEnabled,
        		RconPort = request.RconPort,
        		passwordHash = passwordHash,
        		AllowFirewallChange = request.AllowFirewallChange,
        		InstallManagementMod = request.InstallManagementMod
        	});
        	string requestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedRequest)));
        	string operationId = $"op_{Guid.NewGuid():N}";
        	try
        	{
        		JsonElement requestEvidence = JsonSerializer.SerializeToElement(new
        		{
        			ServerName = request.ServerName,
        			GalaxyName = request.GalaxyName,
        			GalaxyMode = request.GalaxyMode,
        			MaxPlayers = request.MaxPlayers,
        			galaxyDirectory = Path.GetFullPath(request.GalaxyDirectory),
        			ListenAddress = request.ListenAddress,
        			GamePort = request.GamePort,
        			QueryPort = request.QueryPort,
        			RconEnabled = request.RconEnabled,
        			RconPort = request.RconPort,
        			rconPasswordProvided = (request.RconEnabled && request.RconPassword != null),
        			AllowFirewallChange = request.AllowFirewallChange,
        			InstallManagementMod = request.InstallManagementMod
        		});
        		OperationCreateResult created = await store.CreateIdempotentAsync(operationId, "server.configure", idempotencyKey, requestHash, cancellationToken, requestEvidence);
        		if (created.Created)
        		{
        			await queue.EnqueueAsync(new ServerSetupApplicationWorkItem(created.Operation.OperationId, request), cancellationToken);
        		}
        		OperationRecord operation = created.Operation;
        		return Results.Accepted("/api/v1/operations/" + operation.OperationId, new OperationAccepted(operation.OperationId, operation.Status, operation.AcceptedAt));
        	}
        	catch (IdempotencyConflictException)
        	{
        		return ApiErrors.Create(context, 409, "IDEMPOTENCY_CONFLICT", "该 Idempotency-Key 已用于不同的服务器配置");
        	}
        });
        app.MapPost("/api/v1/servers/{serverId}/server-setup/initializations", (Func<string, ServerSetupPreflightRequest, HttpContext, IServerSetupPreflightService, IOperationStore, ServerInitializationQueue, VerifiedCommandRegistry, ServerNodeOptionsAccessor, CancellationToken, Task<IResult>>)async delegate(string serverId, ServerSetupPreflightRequest request, HttpContext context, IServerSetupPreflightService preflightService, IOperationStore store, ServerInitializationQueue queue, VerifiedCommandRegistry commands, ServerNodeOptionsAccessor node, CancellationToken cancellationToken)
        {
        	if (!node.Matches(serverId))
        	{
        		return ApiErrors.Create(context, 404, "SERVER_NOT_FOUND", "未找到服务器节点");
        	}
        	if (!commands.IsVerified("server.initialize"))
        	{
        		return ApiErrors.Locked(context);
        	}
        	if (!request.GalaxyMode.Equals("new", StringComparison.Ordinal) || !request.RconEnabled || string.IsNullOrWhiteSpace(request.RconPassword))
        	{
        		return ApiErrors.Create(context, 400, "INITIALIZATION_REQUIREMENTS_NOT_MET", "首次初始化只适用于已启用 RCON 的全新 Galaxy");
        	}
        	if (request.ServerName == null || request.GalaxyName == null || request.GalaxyDirectory == null || request.ListenAddress == null || request.ServerName.Length > 256 || request.GalaxyName.Length > 256 || request.GalaxyDirectory.Length > 2048 || request.ListenAddress.Length > 128 || request.RconPassword.Length > 128)
        	{
        		return ApiErrors.Create(context, 400, "INITIALIZATION_LIMIT_EXCEEDED", "服务器初始化字段超出允许范围");
        	}
        	string idempotencyKey = context.Request.Headers["Idempotency-Key"].ToString().Trim();
        	int length = idempotencyKey.Length;
        	if ((length < 8 || length > 128) ? true : false)
        	{
        		return ApiErrors.Create(context, 400, "IDEMPOTENCY_KEY_REQUIRED", "首次初始化需要 8–128 字符的 Idempotency-Key");
        	}
        	ServerSetupPreflightResult preflight = await preflightService.ValidateAsync(request, cancellationToken);
        	if (!preflight.Valid)
        	{
        		string message = string.Join("；", from issue in preflight.Issues
        			where issue.Severity == "error"
        			select issue.Message);
        		return ApiErrors.Create(context, 409, "SETUP_PREFLIGHT_FAILED", message);
        	}
        	string passwordHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.RconPassword)));
        	string normalizedRequest = JsonSerializer.Serialize(new
        	{
        		command = "server.initialize",
        		ServerName = request.ServerName,
        		GalaxyName = request.GalaxyName,
        		GalaxyMode = request.GalaxyMode,
        		MaxPlayers = request.MaxPlayers,
        		galaxyDirectory = Path.GetFullPath(request.GalaxyDirectory).ToLowerInvariant(),
        		GamePort = request.GamePort,
        		QueryPort = request.QueryPort,
        		RconPort = request.RconPort,
        		passwordHash = passwordHash
        	});
        	string requestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedRequest)));
        	string operationId = $"op_{Guid.NewGuid():N}";
        	try
        	{
        		JsonElement requestEvidence = JsonSerializer.SerializeToElement(new
        		{
        			ServerName = request.ServerName,
        			GalaxyName = request.GalaxyName,
        			GalaxyMode = request.GalaxyMode,
        			MaxPlayers = request.MaxPlayers,
        			galaxyDirectory = Path.GetFullPath(request.GalaxyDirectory),
        			GamePort = request.GamePort,
        			QueryPort = request.QueryPort,
        			RconPort = request.RconPort,
        			rconPasswordProvided = true
        		});
        		OperationCreateResult created = await store.CreateIdempotentAsync(operationId, "server.initialize", idempotencyKey, requestHash, cancellationToken, requestEvidence);
        		if (created.Created)
        		{
        			await queue.EnqueueAsync(new ServerInitializationWorkItem(created.Operation.OperationId, request), cancellationToken);
        		}
        		OperationRecord operation = created.Operation;
        		return Results.Accepted("/api/v1/operations/" + operation.OperationId, new OperationAccepted(operation.OperationId, operation.Status, operation.AcceptedAt));
        	}
        	catch (IdempotencyConflictException)
        	{
        		return ApiErrors.Create(context, 409, "IDEMPOTENCY_CONFLICT", "该 Idempotency-Key 已用于不同的首次初始化请求");
        	}
        });
    }
}
