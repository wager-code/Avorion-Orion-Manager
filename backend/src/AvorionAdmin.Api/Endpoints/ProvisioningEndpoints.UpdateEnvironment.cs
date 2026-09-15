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
    private static void MapUpdateEnvironmentEndpoints(WebApplication app)
    {
        app.MapGet("/api/v1/servers/{serverId}/updates/status", (Func<string, HttpContext, IServerProbe, ServerNodeOptionsAccessor, CancellationToken, Task<IResult>>)(async (string serverId, HttpContext context, IServerProbe probe, ServerNodeOptionsAccessor node, CancellationToken cancellationToken) => (!node.Matches(serverId)) ? ApiErrors.Create(context, 404, "SERVER_NOT_FOUND", "未找到服务器节点") : Results.Ok(await probe.GetUpdateStatusAsync(cancellationToken))));
        app.MapGet("/api/v1/servers/{serverId}/update-environment", (Func<string, HttpContext, IUpdateEnvironmentStore, IUpdateEnvironmentService, ServerNodeOptionsAccessor, CancellationToken, Task<IResult>>)async delegate(string serverId, HttpContext context, IUpdateEnvironmentStore store, IUpdateEnvironmentService service, ServerNodeOptionsAccessor node, CancellationToken cancellationToken)
        {
        	if (!node.Matches(serverId))
        	{
        		return ApiErrors.Create(context, 404, "SERVER_NOT_FOUND", "未找到服务器节点");
        	}
        	UpdateEnvironmentConfiguration configuration = await store.GetAsync(cancellationToken);
        	return ((object)configuration == null) ? Results.Ok(new
        	{
        		configured = false,
        		configuration = (UpdateEnvironmentConfiguration)null,
        		validation = (UpdateEnvironmentValidation)null
        	}) : Results.Ok(new
        	{
        		configured = true,
        		configuration = configuration,
        		validation = await service.ValidateAsync(configuration.SteamCmdPath, configuration.ServerDirectory, cancellationToken)
        	});
        });
        app.MapGet("/api/v1/servers/{serverId}/update-environment/detection", (Func<string, HttpContext, IUpdateEnvironmentService, ServerNodeOptionsAccessor, CancellationToken, Task<IResult>>)(async (string serverId, HttpContext context, IUpdateEnvironmentService service, ServerNodeOptionsAccessor node, CancellationToken cancellationToken) => (!node.Matches(serverId)) ? ApiErrors.Create(context, 404, "SERVER_NOT_FOUND", "未找到服务器节点") : Results.Ok(await service.DetectAsync(cancellationToken))));
        app.MapPost("/api/v1/servers/{serverId}/update-environment/validation", (Func<string, UpdateEnvironmentRequest, HttpContext, IUpdateEnvironmentService, ServerNodeOptionsAccessor, CancellationToken, Task<IResult>>)async delegate(string serverId, UpdateEnvironmentRequest request, HttpContext context, IUpdateEnvironmentService service, ServerNodeOptionsAccessor node, CancellationToken cancellationToken)
        {
        	if (!node.Matches(serverId))
        	{
        		return ApiErrors.Create(context, 404, "SERVER_NOT_FOUND", "未找到服务器节点");
        	}
        	return (string.IsNullOrWhiteSpace(request.SteamCmdPath) || string.IsNullOrWhiteSpace(request.ServerDirectory)) ? ApiErrors.Create(context, 400, "VALIDATION_FAILED", "SteamCMD 与服务端路径不能为空") : Results.Ok(await service.ValidateAsync(request.SteamCmdPath, request.ServerDirectory, cancellationToken));
        });
        app.MapPut("/api/v1/servers/{serverId}/update-environment", (Func<string, UpdateEnvironmentRequest, HttpContext, IUpdateEnvironmentStore, IUpdateEnvironmentService, ServerNodeOptionsAccessor, CancellationToken, Task<IResult>>)async delegate(string serverId, UpdateEnvironmentRequest request, HttpContext context, IUpdateEnvironmentStore store, IUpdateEnvironmentService service, ServerNodeOptionsAccessor node, CancellationToken cancellationToken)
        {
        	if (!node.Matches(serverId))
        	{
        		return ApiErrors.Create(context, 404, "SERVER_NOT_FOUND", "未找到服务器节点");
        	}
        	if (string.IsNullOrWhiteSpace(request.SteamCmdPath) || string.IsNullOrWhiteSpace(request.ServerDirectory))
        	{
        		return ApiErrors.Create(context, 400, "VALIDATION_FAILED", "SteamCMD 与服务端路径不能为空");
        	}
        	UpdateEnvironmentValidation validation = await service.ValidateAsync(request.SteamCmdPath, request.ServerDirectory, cancellationToken);
        	if (validation.Valid)
        	{
        		await store.SaveAsync(new UpdateEnvironmentConfiguration(validation.SteamCmd.ResolvedExecutablePath, validation.Server.RequestedPath, validation.ValidatedAt), cancellationToken);
        	}
        	return Results.Ok(validation);
        });
    }
}
