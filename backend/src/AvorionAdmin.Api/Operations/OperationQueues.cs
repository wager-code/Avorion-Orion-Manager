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

public sealed class OperationQueue
{
    private readonly Channel<DiagnosticWorkItem> _channel = Channel.CreateBounded<DiagnosticWorkItem>(
        new BoundedChannelOptions(32) { FullMode = BoundedChannelFullMode.Wait });

    public ValueTask EnqueueAsync(DiagnosticWorkItem item, CancellationToken cancellationToken) =>
        _channel.Writer.WriteAsync(item, cancellationToken);

    public IAsyncEnumerable<DiagnosticWorkItem> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}

public sealed class SteamCmdInstallQueue
{
    private readonly Channel<SteamCmdInstallWorkItem> _channel = Channel.CreateBounded<SteamCmdInstallWorkItem>(
        new BoundedChannelOptions(8) { FullMode = BoundedChannelFullMode.Wait });

    public ValueTask EnqueueAsync(SteamCmdInstallWorkItem item, CancellationToken cancellationToken) =>
        _channel.Writer.WriteAsync(item, cancellationToken);

    public IAsyncEnumerable<SteamCmdInstallWorkItem> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}

public sealed class AvorionServerInstallQueue
{
    private readonly Channel<AvorionServerInstallWorkItem> _channel = Channel.CreateBounded<AvorionServerInstallWorkItem>(
        new BoundedChannelOptions(4) { FullMode = BoundedChannelFullMode.Wait });

    public ValueTask EnqueueAsync(AvorionServerInstallWorkItem item, CancellationToken cancellationToken) =>
        _channel.Writer.WriteAsync(item, cancellationToken);

    public IAsyncEnumerable<AvorionServerInstallWorkItem> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}

public sealed class ServerSetupApplicationQueue
{
    private readonly Channel<ServerSetupApplicationWorkItem> _channel = Channel.CreateBounded<ServerSetupApplicationWorkItem>(
        new BoundedChannelOptions(4) { FullMode = BoundedChannelFullMode.Wait });

    public ValueTask EnqueueAsync(ServerSetupApplicationWorkItem item, CancellationToken cancellationToken) =>
        _channel.Writer.WriteAsync(item, cancellationToken);

    public IAsyncEnumerable<ServerSetupApplicationWorkItem> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}

public sealed class ServerInitializationQueue
{
    private readonly Channel<ServerInitializationWorkItem> _channel = Channel.CreateBounded<ServerInitializationWorkItem>(
        new BoundedChannelOptions(2) { FullMode = BoundedChannelFullMode.Wait });

    public ValueTask EnqueueAsync(ServerInitializationWorkItem item, CancellationToken cancellationToken) =>
        _channel.Writer.WriteAsync(item, cancellationToken);

    public IAsyncEnumerable<ServerInitializationWorkItem> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}

public sealed class ServerControlQueue
{
    private readonly Channel<ServerControlWorkItem> _channel = Channel.CreateBounded<ServerControlWorkItem>(
        new BoundedChannelOptions(8) { FullMode = BoundedChannelFullMode.Wait });

    public ValueTask EnqueueAsync(ServerControlWorkItem item, CancellationToken cancellationToken) =>
        _channel.Writer.WriteAsync(item, cancellationToken);

    public IAsyncEnumerable<ServerControlWorkItem> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}

public sealed class UpdateInspectionQueue
{
    private readonly Channel<UpdateInspectionWorkItem> _channel = Channel.CreateBounded<UpdateInspectionWorkItem>(
        new BoundedChannelOptions(4) { FullMode = BoundedChannelFullMode.Wait });

    public ValueTask EnqueueAsync(UpdateInspectionWorkItem item, CancellationToken cancellationToken) =>
        _channel.Writer.WriteAsync(item, cancellationToken);

    public IAsyncEnumerable<UpdateInspectionWorkItem> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}

public sealed class UpdateRollbackPointQueue
{
    private readonly Channel<UpdateRollbackPointWorkItem> _channel = Channel.CreateBounded<UpdateRollbackPointWorkItem>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.Wait });

    public ValueTask EnqueueAsync(UpdateRollbackPointWorkItem item, CancellationToken cancellationToken) =>
        _channel.Writer.WriteAsync(item, cancellationToken);

    public IAsyncEnumerable<UpdateRollbackPointWorkItem> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}

public sealed class BackupRestoreQueue
{
    private readonly Channel<BackupRestoreWorkItem> _channel = Channel.CreateBounded<BackupRestoreWorkItem>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.Wait });

    public ValueTask EnqueueAsync(BackupRestoreWorkItem item, CancellationToken cancellationToken) =>
        _channel.Writer.WriteAsync(item, cancellationToken);

    public IAsyncEnumerable<BackupRestoreWorkItem> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}

public sealed class SectorUnloadQueue
{
    private readonly Channel<SectorUnloadWorkItem> _channel = Channel.CreateBounded<SectorUnloadWorkItem>(
        new BoundedChannelOptions(4) { FullMode = BoundedChannelFullMode.Wait });

    public ValueTask EnqueueAsync(SectorUnloadWorkItem item, CancellationToken cancellationToken) =>
        _channel.Writer.WriteAsync(item, cancellationToken);

    public IAsyncEnumerable<SectorUnloadWorkItem> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}

public sealed class PlayerRewardQueue
{
    private readonly Channel<PlayerRewardWorkItem> _channel = Channel.CreateBounded<PlayerRewardWorkItem>(
        new BoundedChannelOptions(16) { FullMode = BoundedChannelFullMode.Wait });

    public ValueTask EnqueueAsync(PlayerRewardWorkItem item, CancellationToken cancellationToken) =>
        _channel.Writer.WriteAsync(item, cancellationToken);

    public IAsyncEnumerable<PlayerRewardWorkItem> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}

public sealed class AllianceRewardQueue
{
    private readonly Channel<AllianceRewardWorkItem> _channel = Channel.CreateBounded<AllianceRewardWorkItem>(
        new BoundedChannelOptions(16) { FullMode = BoundedChannelFullMode.Wait });

    public ValueTask EnqueueAsync(AllianceRewardWorkItem item, CancellationToken cancellationToken) =>
        _channel.Writer.WriteAsync(item, cancellationToken);

    public IAsyncEnumerable<AllianceRewardWorkItem> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}

public sealed class RewardBatchQueue
{
    private readonly Channel<RewardBatchWorkItem> _channel = Channel.CreateBounded<RewardBatchWorkItem>(
        new BoundedChannelOptions(4) { FullMode = BoundedChannelFullMode.Wait });

    public ValueTask EnqueueAsync(RewardBatchWorkItem item, CancellationToken cancellationToken) =>
        _channel.Writer.WriteAsync(item, cancellationToken);

    public IAsyncEnumerable<RewardBatchWorkItem> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}

public sealed class InventoryGrantQueue
{
    private readonly Channel<InventoryGrantWorkItem> _channel = Channel.CreateBounded<InventoryGrantWorkItem>(
        new BoundedChannelOptions(8) { FullMode = BoundedChannelFullMode.Wait });

    public ValueTask EnqueueAsync(InventoryGrantWorkItem item, CancellationToken cancellationToken) =>
        _channel.Writer.WriteAsync(item, cancellationToken);

    public IAsyncEnumerable<InventoryGrantWorkItem> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}
