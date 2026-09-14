using System.Net;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using AvorionAdmin.Agent;
using AvorionAdmin.Core;
using AvorionAdmin.Core.Abstractions;
using AvorionAdmin.Core.Configuration;
using AvorionAdmin.Core.Models;
using Microsoft.Extensions.Options;

namespace AvorionAdmin.Api;

public sealed class LocalOnlyMiddleware(RequestDelegate next, IOptions<SecurityOptions> options)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var address = context.Connection.RemoteIpAddress;
        if (!options.Value.AllowRemote && address is not null && !IPAddress.IsLoopback(address))
        {
            await Results.Json(
                new ApiErrorEnvelope(new ApiErrorDetail(
                    "FORBIDDEN",
                    "R0/R1 阶段 API 仅允许本机访问",
                    context.TraceIdentifier,
                    false)),
                statusCode: StatusCodes.Status403Forbidden).ExecuteAsync(context);
            return;
        }

        await next(context);
    }
}

