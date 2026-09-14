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

public sealed class PerformanceSamplingWorker(
    IServerProbe serverProbe,
    IPerformanceStore performanceStore,
    IOptions<ServerNodeOptions> options,
    ILogger<PerformanceSamplingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Clamp(options.Value.PerformanceSampleSeconds, 2, 300));
        using var timer = new PeriodicTimer(interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SampleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Performance sample failed; no fallback data was written");
            }

            if (!await timer.WaitForNextTickAsync(stoppingToken)) break;
        }
    }

    private async Task SampleAsync(CancellationToken cancellationToken)
    {
        var snapshot = await serverProbe.GetPerformanceAsync(cancellationToken);
        await performanceStore.AppendAsync(new PerformancePoint(
            snapshot.Provenance.SampledAt,
            snapshot.Cpu.ProcessPercent,
            snapshot.Memory.WorkingSetBytes,
            snapshot.Network.DownloadBytesPerSecond,
            snapshot.Network.UploadBytesPerSecond,
            snapshot.OnlinePlayers), cancellationToken);
        await performanceStore.PruneAsync(options.Value.HistoryRetentionDays, cancellationToken);
    }
}

