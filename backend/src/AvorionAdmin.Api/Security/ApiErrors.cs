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

public static class ApiErrors
{
    public static IResult Create(HttpContext context, int statusCode, string code, string message, bool retryable = false) =>
        Results.Json(
            new ApiErrorEnvelope(new ApiErrorDetail(
                code,
                message,
                context.TraceIdentifier,
                retryable,
                new Dictionary<string, object?>())),
            statusCode: statusCode);

    public static IResult Locked(HttpContext context) =>
        Create(context, StatusCodes.Status501NotImplemented, "COMMAND_NOT_VERIFIED", "该真实操作仍处于安全锁定状态，尚未执行任何服务器命令");
}

