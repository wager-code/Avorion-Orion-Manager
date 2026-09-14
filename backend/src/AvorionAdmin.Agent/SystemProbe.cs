using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using AvorionAdmin.Core.Abstractions;
using AvorionAdmin.Core.Configuration;
using AvorionAdmin.Core.Models;
using Microsoft.Extensions.Options;

namespace AvorionAdmin.Agent;

public sealed class SystemProbe(IOptions<ServerNodeOptions> options, ISteamQueryProbe steamQueryProbe) : IServerProbe
{
    private readonly ServerNodeOptions _options = options.Value;
    private readonly SemaphoreSlim _sampleLock = new(1, 1);
    private TimeSpan? _lastCpuTime;
    private DateTimeOffset? _lastCpuSampleAt;
    private long? _lastReceivedBytes;
    private long? _lastSentBytes;
    private DateTimeOffset? _lastNetworkSampleAt;

    public async Task<ServerStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;
        using var process = FindProcess();
        var executableVersion = ReadExecutableVersion(process);
        var lastSave = FindLastSave();
        var steamQuery = await steamQueryProbe.ProbeAsync(process is not null, cancellationToken);

        var status = new ServerStatus(
            _options.ServerId,
            _options.Name,
            process is null ? Lifecycle.Stopped : Lifecycle.Running,
            process?.Id,
            executableVersion,
            ReadGalaxyName(),
            process is null ? null : SafeUptimeSeconds(process, now),
            steamQuery.OnlinePlayers,
            steamQuery.MaxPlayers,
            new ConnectionProbe(ConnectionState.Unknown, null, "RCON_PROTOCOL_NOT_CONFIGURED"),
            steamQuery.Connection,
            lastSave,
            new AgentInfo(true, now),
            new Provenance(now, "windows", "live"));

        return status;
    }

    public async Task<PerformanceSnapshot> GetPerformanceAsync(CancellationToken cancellationToken = default)
    {
        await _sampleLock.WaitAsync(cancellationToken);
        try
        {
            var now = DateTimeOffset.UtcNow;
            using var process = FindProcess();
            double? processCpu = null;
            long? workingSet = null;
            long? privateBytes = null;
            long? uptime = null;

            if (process is not null)
            {
                try
                {
                    process.Refresh();
                    var cpuTime = process.TotalProcessorTime;
                    if (_lastCpuTime is not null && _lastCpuSampleAt is not null)
                    {
                        var elapsedMs = (now - _lastCpuSampleAt.Value).TotalMilliseconds;
                        var cpuMs = (cpuTime - _lastCpuTime.Value).TotalMilliseconds;
                        if (elapsedMs > 0)
                        {
                            processCpu = Math.Clamp(cpuMs / elapsedMs / Environment.ProcessorCount * 100d, 0d, 100d);
                        }
                    }

                    _lastCpuTime = cpuTime;
                    _lastCpuSampleAt = now;
                    workingSet = process.WorkingSet64;
                    privateBytes = process.PrivateMemorySize64;
                    uptime = SafeUptimeSeconds(process, now);
                }
                catch (InvalidOperationException)
                {
                    ResetProcessSample();
                }
            }
            else
            {
                ResetProcessSample();
            }

            var (received, sent) = ReadNetworkBytes();
            double? download = null;
            double? upload = null;
            if (_lastNetworkSampleAt is not null && _lastReceivedBytes is not null && _lastSentBytes is not null)
            {
                var elapsed = (now - _lastNetworkSampleAt.Value).TotalSeconds;
                if (elapsed > 0)
                {
                    download = Math.Max(0, received - _lastReceivedBytes.Value) / elapsed;
                    upload = Math.Max(0, sent - _lastSentBytes.Value) / elapsed;
                }
            }

            _lastReceivedBytes = received;
            _lastSentBytes = sent;
            _lastNetworkSampleAt = now;

            var disk = ReadDiskMetrics();
            var totalMemory = ReadTotalPhysicalMemory();
            var unavailable = process is null ? "AVORION_PROCESS_NOT_RUNNING" : null;
            var steamQuery = await steamQueryProbe.ProbeAsync(process is not null, cancellationToken);

            return new PerformanceSnapshot(
                new CpuMetrics(processCpu, null),
                new MemoryMetrics(workingSet, privateBytes, totalMemory),
                disk,
                new NetworkMetrics("host", download, upload),
                steamQuery.OnlinePlayers,
                uptime,
                new Provenance(now, "windows", process is null ? "unavailable" : "live", unavailable));
        }
        finally
        {
            _sampleLock.Release();
        }
    }

    public Task<UpdateStatus> GetUpdateStatusAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;
        var configured = !string.IsNullOrWhiteSpace(_options.SteamCmdPath);
        var available = configured && File.Exists(_options.SteamCmdPath);
        using var process = FindProcess();

        return Task.FromResult(new UpdateStatus(
            available,
            configured,
            ReadExecutableVersion(process),
            null,
            null,
            null,
            new Provenance(now, "steamcmd", available ? "recent" : "unavailable", available ? null : "STEAMCMD_PATH_UNAVAILABLE")));
    }

    private Process? FindProcess()
    {
        if (_options.ProcessId is int processId)
        {
            try
            {
                var configured = Process.GetProcessById(processId);
                if (!configured.HasExited) return configured;
                configured.Dispose();
            }
            catch (ArgumentException) { }
        }

        if (string.IsNullOrWhiteSpace(_options.ProcessName)) return null;
        var normalized = Path.GetFileNameWithoutExtension(_options.ProcessName);
        return Process.GetProcessesByName(normalized).OrderByDescending(SafeStartTime).FirstOrDefault();
    }

    private string? ReadExecutableVersion(Process? process)
    {
        var path = _options.ExecutablePath;
        if (string.IsNullOrWhiteSpace(path) && process is not null)
        {
            try { path = process.MainModule?.FileName; }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        }

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        return FileVersionInfo.GetVersionInfo(path).FileVersion;
    }

    private LastSaveInfo FindLastSave()
    {
        var root = !string.IsNullOrWhiteSpace(_options.SavePath) ? _options.SavePath : _options.GalaxyPath;
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return new LastSaveInfo(null, null, "SAVE_PATH_UNAVAILABLE");
        }

        try
        {
            var latest = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Take(100_000)
                .Select(path => new FileInfo(path))
                .Where(file => file.Exists && !file.Name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .FirstOrDefault();

            return latest is null
                ? new LastSaveInfo(null, "filesystem", "NO_SAVE_FILES_FOUND")
                : new LastSaveInfo(new DateTimeOffset(latest.LastWriteTimeUtc, TimeSpan.Zero), "filesystem");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new LastSaveInfo(null, "filesystem", "SAVE_PATH_UNREADABLE");
        }
    }

    private string? ReadGalaxyName()
    {
        if (string.IsNullOrWhiteSpace(_options.GalaxyPath)) return null;
        return Directory.Exists(_options.GalaxyPath)
            ? new DirectoryInfo(_options.GalaxyPath).Name
            : null;
    }

    private DiskMetrics ReadDiskMetrics()
    {
        var candidate = _options.GalaxyPath;
        if (string.IsNullOrWhiteSpace(candidate)) candidate = AppContext.BaseDirectory;

        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(candidate));
            if (string.IsNullOrWhiteSpace(root)) return new DiskMetrics(null, null, null, null);
            var drive = new DriveInfo(root);
            return drive.IsReady
                ? new DiskMetrics(drive.TotalSize, drive.TotalSize - drive.AvailableFreeSpace, drive.AvailableFreeSpace, drive.Name)
                : new DiskMetrics(null, null, null, drive.Name);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new DiskMetrics(null, null, null, null);
        }
    }

    private static (long Received, long Sent) ReadNetworkBytes()
    {
        long received = 0;
        long sent = 0;
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (adapter.OperationalStatus != OperationalStatus.Up ||
                adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            {
                continue;
            }

            try
            {
                var statistics = adapter.GetIPv4Statistics();
                received += statistics.BytesReceived;
                sent += statistics.BytesSent;
            }
            catch (NetworkInformationException) { }
        }

        return (received, sent);
    }

    private static long? ReadTotalPhysicalMemory()
    {
        if (!OperatingSystem.IsWindows()) return null;
        var status = new MemoryStatusEx();
        return GlobalMemoryStatusEx(status) ? checked((long)status.TotalPhysical) : null;
    }

    private static long? SafeUptimeSeconds(Process process, DateTimeOffset now)
    {
        try { return Math.Max(0, (long)(now - process.StartTime.ToUniversalTime()).TotalSeconds); }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception) { return null; }
    }

    private static DateTime SafeStartTime(Process process)
    {
        try { return process.StartTime; }
        catch { return DateTime.MinValue; }
    }

    private void ResetProcessSample()
    {
        _lastCpuTime = null;
        _lastCpuSampleAt = null;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class MemoryStatusEx
    {
        public uint Length = (uint)Marshal.SizeOf<MemoryStatusEx>();
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx buffer);
}
