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
    private static void MapFileSystemEndpoints(WebApplication app)
    {
        app.MapGet("/api/v1/servers/{serverId}/filesystem/directories", (Func<string, string, HttpContext, IFileSystemBrowser, ServerNodeOptionsAccessor, CancellationToken, Task<IResult>>)async delegate(string serverId, string? path, HttpContext context, IFileSystemBrowser browser, ServerNodeOptionsAccessor node, CancellationToken cancellationToken)
        {
        	if (!node.Matches(serverId))
        	{
        		return ApiErrors.Create(context, 404, "SERVER_NOT_FOUND", "未找到服务器节点");
        	}
        	try
        	{
        		return Results.Ok(await browser.BrowseDirectoriesAsync(path, cancellationToken));
        	}
        	catch (ArgumentException)
        	{
        		return ApiErrors.Create(context, 400, "INVALID_PATH", "目录路径无效，请选择服务器上的绝对路径");
        	}
        	catch (DirectoryNotFoundException)
        	{
        		return ApiErrors.Create(context, 404, "PATH_NOT_FOUND", "目录不存在或已经被移除");
        	}
        	catch (UnauthorizedAccessException)
        	{
        		return ApiErrors.Create(context, 403, "PATH_ACCESS_DENIED", "Server Agent 无权读取该目录");
        	}
        	catch (IOException)
        	{
        		return ApiErrors.Create(context, 503, "FILESYSTEM_UNAVAILABLE", "服务器文件系统暂时不可用", retryable: true);
        	}
        });
    }
}
