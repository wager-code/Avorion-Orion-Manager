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

public static class SecurityEndpoints
{
    public static WebApplication MapSecurityEndpoints(this WebApplication app)
    {
        app.MapPost("/api/v1/session/local", (Func<HttpContext, AdminSessionService, IResult>)delegate(HttpContext context, AdminSessionService sessions)
        {
        	IPAddress remoteIpAddress = context.Connection.RemoteIpAddress;
        	if (remoteIpAddress != null && !IPAddress.IsLoopback(remoteIpAddress))
        	{
        		return ApiErrors.Create(context, 403, "LOCAL_SESSION_ONLY", "本机管理员会话只能从 Server Agent 所在主机创建");
        	}
        	AdminSession adminSession = sessions.Create();
        	CookieOptions cookieOptions = AdminSessionService.CookieOptions(context);
        	cookieOptions.Expires = adminSession.ExpiresAt;
        	context.Response.Cookies.Append("avorion_admin_session", adminSession.Id, cookieOptions);
        	context.Response.Headers.CacheControl = "no-store";
        	return Results.Ok(new AdminSessionStatus(Authenticated: true, "local-loopback", adminSession.CsrfToken, adminSession.ExpiresAt));
        });
        app.MapGet("/api/v1/session", (Func<HttpContext, AdminSessionService, IResult>)delegate(HttpContext context, AdminSessionService sessions)
        {
        	context.Response.Headers.CacheControl = "no-store";
        	AdminSession session;
        	return (sessions.TryGet(context, out session) && (object)session != null) ? Results.Ok(new AdminSessionStatus(Authenticated: true, "local-loopback", session.CsrfToken, session.ExpiresAt)) : Results.Ok(new AdminSessionStatus(Authenticated: false, "local-loopback", null, null));
        });
        app.MapDelete("/api/v1/session", (Func<HttpContext, AdminSessionService, IResult>)delegate(HttpContext context, AdminSessionService sessions)
        {
        	sessions.Revoke(context);
        	return Results.NoContent();
        });
        app.MapGet("/api/v1/security/write-safety", (Func<VerifiedCommandRegistry, IResult>)((VerifiedCommandRegistry commands) => Results.Ok(commands.GetStatus())));
        app.MapGet("/api/v1/health", (Func<IResult>)(() => Results.Ok(new
        {
        	status = "ok",
        	mode = "r2-write-safety-locked",
        	sampledAt = DateTimeOffset.UtcNow
        })));

        return app;
    }
}
