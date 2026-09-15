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
    public static WebApplication MapProvisioningEndpoints(this WebApplication app)
    {
        MapUpdateEnvironmentEndpoints(app);
        MapServerSetupEndpoints(app);
        MapInstallationEndpoints(app);
        MapFileSystemEndpoints(app);
        return app;
    }
}
