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

public static class JsonHelpers
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static DiagnosticRun? ReadDiagnosticRun(OperationRecord operation)
    {
        if (operation.Result is null) return null;
        return operation.Result.Value.Deserialize<DiagnosticRun>(Options);
    }
}

