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

public sealed class AdminWriteProtectionMiddleware(RequestDelegate next)
{
    private static readonly HashSet<string> UnsafeMethods =
        new(["POST", "PUT", "PATCH", "DELETE"], StringComparer.OrdinalIgnoreCase);

    public async Task InvokeAsync(HttpContext context, AdminSessionService sessions)
    {
        if (!context.Request.Path.StartsWithSegments("/api") ||
            !UnsafeMethods.Contains(context.Request.Method) ||
            IsLocalSessionBootstrap(context))
        {
            await next(context);
            return;
        }

        if (!IsAllowedOrigin(context.Request.Headers.Origin.ToString()))
        {
            await ApiErrors.Create(context, 403, "ORIGIN_REJECTED", "写操作来源不受信任").ExecuteAsync(context);
            return;
        }

        if (!sessions.TryGet(context, out var session) || session is null)
        {
            await ApiErrors.Create(context, 401, "ADMIN_SESSION_REQUIRED", "写操作需要有效的本机管理员会话").ExecuteAsync(context);
            return;
        }

        var csrf = context.Request.Headers["X-CSRF-Token"].ToString();
        if (string.IsNullOrWhiteSpace(csrf) || !sessions.CsrfMatches(session, csrf))
        {
            await ApiErrors.Create(context, 403, "CSRF_VALIDATION_FAILED", "写操作防伪令牌无效").ExecuteAsync(context);
            return;
        }

        await next(context);
    }

    private static bool IsLocalSessionBootstrap(HttpContext context) =>
        HttpMethods.IsPost(context.Request.Method) &&
        context.Request.Path.Equals("/api/v1/session/local", StringComparison.OrdinalIgnoreCase);

    private static bool IsAllowedOrigin(string origin)
    {
        if (string.IsNullOrWhiteSpace(origin)) return true;
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;
        return uri.Scheme is "http" or "https" &&
            (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
             IPAddress.TryParse(uri.Host, out var address) && IPAddress.IsLoopback(address));
    }
}

