using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using AvorionAdmin.Agent;
using AvorionAdmin.Core;
using AvorionAdmin.Core.Abstractions;
using AvorionAdmin.Core.Configuration;
using AvorionAdmin.Core.Models;
using Microsoft.Extensions.Options;

sealed class FixtureArchiveSource(byte[] archive) : ISteamCmdArchiveSource
{
    public string SourceUrl => OfficialSteamCmdArchiveSource.OfficialUrl;
    public int DownloadCount { get; private set; }

    public async Task<long> DownloadAsync(
        string destinationPath,
        Func<long, long?, CancellationToken, Task> reportBytesAsync,
        CancellationToken cancellationToken)
    {
        DownloadCount++;
        await File.WriteAllBytesAsync(destinationPath, archive, cancellationToken);
        await reportBytesAsync(archive.Length, archive.Length, cancellationToken);
        return archive.Length;
    }
}

sealed class AcceptValveSignature : IAuthenticodeVerifier
{
    public bool TryVerifyValve(string executablePath, out string signer)
    {
        signer = "Valve";
        return File.Exists(executablePath);
    }
}

sealed class StaticManagedControl(Lifecycle lifecycle) : IManagedServerControlService
{
    public Task<ServerStatus> GetStatusAsync(CancellationToken cancellationToken = default) => Task.FromResult(new ServerStatus(
        "test",
        "测试节点",
        lifecycle,
        lifecycle == Lifecycle.Running ? 1234 : null,
        null,
        null,
        null,
        null,
        null,
        new ConnectionProbe(ConnectionState.Unknown, null),
        new ConnectionProbe(ConnectionState.Unknown, null),
        new LastSaveInfo(null, null),
        new AgentInfo(true, DateTimeOffset.UtcNow),
        new Provenance(DateTimeOffset.UtcNow, "fixture", "live")));

    public Task<ManagedServerActionResult> ExecuteAsync(
        string action,
        Func<double, string, CancellationToken, Task> reportProgressAsync,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<ManagedServerActionResult> ForceStopAsync(
        int expectedProcessId,
        Func<double, string, CancellationToken, Task> reportProgressAsync,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}

sealed class StaticSteamQueryProbe : ISteamQueryProbe
{
    public Task<SteamQuerySnapshot> ProbeAsync(bool serverRunning, CancellationToken cancellationToken = default) =>
        Task.FromResult(new SteamQuerySnapshot(
            new ConnectionProbe(serverRunning ? ConnectionState.Unknown : ConnectionState.Disconnected, null,
                serverRunning ? "STEAM_QUERY_FIXTURE_UNAVAILABLE" : "SERVER_NOT_RUNNING"),
            null, null, null, null, 27000, "fixture"));
}

sealed class FixtureAvorionSteamCmdRunner(bool succeed) : IAvorionSteamCmdRunner
{
    public string? SteamCmdPath { get; private set; }
    public string? WorkingDirectory { get; private set; }
    public int RunCount { get; private set; }

    public async Task<SteamCmdProcessResult> RunAsync(
        string steamCmdPath,
        string workingDirectory,
        string installDirectory,
        Func<double, string, CancellationToken, Task> reportProgressAsync,
        CancellationToken cancellationToken)
    {
        RunCount++;
        SteamCmdPath = steamCmdPath;
        WorkingDirectory = workingDirectory;
        Directory.CreateDirectory(Path.Combine(installDirectory, "bin"));
        var executable = new byte[70 * 1024];
        executable[0] = (byte)'M';
        executable[1] = (byte)'Z';
        await File.WriteAllBytesAsync(Path.Combine(installDirectory, "bin", "AvorionServer.exe"), executable, cancellationToken);
        await File.WriteAllBytesAsync(Path.Combine(installDirectory, "bin", "ServerRunner.exe"), executable, cancellationToken);
        await reportProgressAsync(50, "downloading-avorion-server", cancellationToken);
        return new SteamCmdProcessResult(0, succeed
            ? "Success! App '565060' fully installed."
            : "Update state complete without confirmation");
    }
}

sealed class TransientLockAvorionSteamCmdRunner : IAvorionSteamCmdRunner
{
    public async Task<SteamCmdProcessResult> RunAsync(
        string steamCmdPath,
        string workingDirectory,
        string installDirectory,
        Func<double, string, CancellationToken, Task> reportProgressAsync,
        CancellationToken cancellationToken)
    {
        var bin = Directory.CreateDirectory(Path.Combine(installDirectory, "bin")).FullName;
        var executable = new byte[70 * 1024];
        executable[0] = (byte)'M';
        executable[1] = (byte)'Z';
        await File.WriteAllBytesAsync(Path.Combine(bin, "AvorionServer.exe"), executable, cancellationToken);
        await File.WriteAllBytesAsync(Path.Combine(bin, "ServerRunner.exe"), executable, cancellationToken);
        var lockPath = Path.Combine(bin, "zlib1.dll");
        await File.WriteAllBytesAsync(lockPath, executable, cancellationToken);
        var lockedStream = new FileStream(lockPath, FileMode.Open, FileAccess.Read, FileShare.None);
        _ = Task.Run(async () =>
        {
            await Task.Delay(900);
            lockedStream.Dispose();
        });
        return new SteamCmdProcessResult(0, "Success! App '565060' fully installed.");
    }
}
