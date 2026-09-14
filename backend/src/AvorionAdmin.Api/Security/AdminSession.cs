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

public sealed record AdminSession(string Id, string CsrfToken, DateTimeOffset ExpiresAt);

public sealed class AdminSessionService(IOptions<SecurityOptions> options)
{
    public const string CookieName = "avorion_admin_session";
    private readonly ConcurrentDictionary<string, AdminSession> _sessions = new(StringComparer.Ordinal);

    public AdminSession Create()
    {
        var now = DateTimeOffset.UtcNow;
        var session = new AdminSession(
            CreateToken(32),
            CreateToken(32),
            now.AddMinutes(Math.Clamp(options.Value.LocalSessionMinutes, 5, 240)));
        _sessions[session.Id] = session;
        Prune(now);
        return session;
    }

    public bool TryGet(HttpContext context, out AdminSession? session)
    {
        session = null;
        if (!context.Request.Cookies.TryGetValue(CookieName, out var id)) return false;
        if (!_sessions.TryGetValue(id, out var existing)) return false;
        if (existing.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            _sessions.TryRemove(id, out _);
            return false;
        }
        session = existing;
        return true;
    }

    public bool CsrfMatches(AdminSession session, string candidate)
    {
        var expectedBytes = Encoding.UTF8.GetBytes(session.CsrfToken);
        var candidateBytes = Encoding.UTF8.GetBytes(candidate);
        return expectedBytes.Length == candidateBytes.Length &&
            CryptographicOperations.FixedTimeEquals(expectedBytes, candidateBytes);
    }

    public void Revoke(HttpContext context)
    {
        if (context.Request.Cookies.TryGetValue(CookieName, out var id)) _sessions.TryRemove(id, out _);
        context.Response.Cookies.Delete(CookieName, CookieOptions(context));
    }

    public static CookieOptions CookieOptions(HttpContext context) => new()
    {
        HttpOnly = true,
        Secure = context.Request.IsHttps,
        SameSite = SameSiteMode.Strict,
        IsEssential = true,
        Path = "/"
    };

    private static string CreateToken(int byteLength) =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(byteLength))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private void Prune(DateTimeOffset now)
    {
        foreach (var pair in _sessions)
        {
            if (pair.Value.ExpiresAt <= now) _sessions.TryRemove(pair.Key, out _);
        }
    }
}

