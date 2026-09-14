using AvorionAdmin.Core.Abstractions;
using AvorionAdmin.Core.Configuration;
using Microsoft.Extensions.Options;

namespace AvorionAdmin.Api;

public sealed class ServerNodeOptionsAccessor(IOptions<ServerNodeOptions> options, IServerRuntimePathResolver runtimePaths)
{
    public ServerNodeOptions Options { get; } = options.Value;
    public bool Matches(string serverId) => serverId.Equals(Options.ServerId, StringComparison.OrdinalIgnoreCase);
    public bool HasReadableBackupPath() => runtimePaths.ResolveBackupSource() is not null;
    public bool HasReadableLogPath() => runtimePaths.ResolveLogSource() is not null;
}
