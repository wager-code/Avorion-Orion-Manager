using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using AvorionAdmin.Core.Abstractions;
using AvorionAdmin.Core.Models;

namespace AvorionAdmin.Agent;

public sealed class ServerInitializationException(
    string code,
    string message,
    bool retryable = false,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string Code { get; } = code;
    public bool Retryable { get; } = retryable;
}

public sealed class ManagedServerProcessRegistry
{
    private readonly object _gate = new();
    private Process? _process;

    public void Register(Process process)
    {
        lock (_gate)
        {
            if (_process is { HasExited: false })
                throw new ServerInitializationException("SERVER_ALREADY_RUNNING", "已有受管 Avorion 服务端进程正在运行");
            _process?.Dispose();
            _process = process;
        }
    }

    public Process RegisterOrGet(Process process)
    {
        lock (_gate)
        {
            if (_process is { HasExited: false })
            {
                if (ReferenceEquals(_process, process)) return _process;
                if (_process.Id == process.Id)
                {
                    process.Dispose();
                    return _process;
                }
                throw new ServerInitializationException("SERVER_ALREADY_RUNNING", "已有受管 Avorion 服务端进程正在运行");
            }
            _process?.Dispose();
            _process = process;
            return process;
        }
    }

    public void Clear(Process process)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_process, process)) return;
            _process.Dispose();
            _process = null;
        }
    }

    public Process? GetRunning()
    {
        lock (_gate)
        {
            if (_process is null) return null;
            if (!_process.HasExited) return _process;
            _process.Dispose();
            _process = null;
            return null;
        }
    }
}

public sealed class ManagedServerRuntime(ManagedServerProcessRegistry registry) : IManagedServerRuntime
{
    private static readonly TimeSpan IniCreationTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ServerReadyTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan GracefulStopTimeout = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan RconHealthTimeout = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan RconStabilityWindow = TimeSpan.FromSeconds(5);

    public async Task<GalaxyInitializationEvidence> InitializeGalaxyAsync(
        ManagedLaunchProfile profile,
        Func<double, string, CancellationToken, Task> reportProgressAsync,
        CancellationToken cancellationToken = default)
    {
        ValidateProfile(profile, requireNewGalaxy: true);
        var serverIniPath = Path.Combine(profile.GalaxyDirectory, "server.ini");
        if (File.Exists(serverIniPath))
            throw new ServerInitializationException("GALAXY_ALREADY_INITIALIZED", "Galaxy 已存在 server.ini，无法再次按全新 Galaxy 初始化");

        await reportProgressAsync(20, "starting-first-initialization", cancellationToken);
        var process = StartProcess(profile, redirectStandardInput: true);
        registry.Register(process);
        try
        {
            await WaitForFileAsync(process, serverIniPath, IniCreationTimeout, cancellationToken);
            await reportProgressAsync(40, "server-ini-created", cancellationToken);
            await reportProgressAsync(42, "waiting-for-server-ready", cancellationToken);
            await WaitForServerReadyAsync(process, profile.GalaxyDirectory, ServerReadyTimeout, cancellationToken);
            await reportProgressAsync(46, "server-ready", cancellationToken);
            await SendConsoleCommandAsync(process, "/save", cancellationToken);
            await Task.Delay(1000, cancellationToken);
            await reportProgressAsync(48, "saving-initial-galaxy", cancellationToken);
            await SendConsoleCommandAsync(process, "/stop", cancellationToken);
            await reportProgressAsync(55, "waiting-for-safe-stop", cancellationToken);
            await WaitForExitAsync(process, GracefulStopTimeout, "INITIALIZATION_STOP_UNCONFIRMED", cancellationToken);
            if (process.ExitCode != 0)
                throw new ServerInitializationException("INITIALIZATION_EXIT_FAILED", $"首次初始化进程退出码为 {process.ExitCode}", true);
            if (!File.Exists(serverIniPath) || new FileInfo(serverIniPath).Length == 0)
                throw new ServerInitializationException("SERVER_INI_NOT_CREATED", "首次初始化结束后未生成有效 server.ini", true);
            return new GalaxyInitializationEvidence(serverIniPath, true, true, process.ExitCode, DateTimeOffset.UtcNow);
        }
        finally
        {
            if (process.HasExited) registry.Clear(process);
        }
    }

    public async Task<ManagedServerHealth> StartAndVerifyAsync(
        ManagedLaunchProfile profile,
        string rconPassword,
        Func<double, string, CancellationToken, Task> reportProgressAsync,
        CancellationToken cancellationToken = default)
    {
        ValidateProfile(profile, requireNewGalaxy: false);
        if (!profile.RconEnabled || string.IsNullOrWhiteSpace(rconPassword))
            throw new ServerInitializationException("RCON_REQUIRED", "首次初始化完成前必须设置 RCON 密码");

        await reportProgressAsync(80, "starting-configured-server", cancellationToken);
        var process = StartProcess(profile, redirectStandardInput: false);
        registry.Register(process);
        try
        {
            var deadline = DateTimeOffset.UtcNow + RconHealthTimeout;
            while (DateTimeOffset.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (process.HasExited)
                    throw new ServerInitializationException("SERVER_EXITED_DURING_HEALTH_CHECK", $"服务端在健康检查期间退出，退出码 {process.ExitCode}", true);
                if (await SourceRconProbe.AuthenticateAsync(IPAddress.Loopback, profile.RconPort, rconPassword, TimeSpan.FromSeconds(2), cancellationToken))
                {
                    await reportProgressAsync(95, "verifying-server-stability", cancellationToken);
                    var stabilityDeadline = DateTimeOffset.UtcNow + RconStabilityWindow;
                    while (DateTimeOffset.UtcNow < stabilityDeadline)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (process.HasExited)
                            throw new ServerInitializationException("SERVER_EXITED_AFTER_RCON_READY", $"服务端通过 RCON 认证后很快退出，退出码 {process.ExitCode}", true);
                        await Task.Delay(250, cancellationToken);
                    }
                    if (!await SourceRconProbe.AuthenticateAsync(IPAddress.Loopback, profile.RconPort, rconPassword, TimeSpan.FromSeconds(2), cancellationToken))
                        throw new ServerInitializationException("RCON_UNSTABLE", "服务端首次通过 RCON 认证，但稳定观察后复检失败", true);
                    await reportProgressAsync(100, "rcon-authenticated", cancellationToken);
                    return new ManagedServerHealth(process.Id, process.StartTime.ToUniversalTime(), true, DateTimeOffset.UtcNow);
                }
                await Task.Delay(500, cancellationToken);
            }

            throw new ServerInitializationException(
                "RCON_HEALTH_TIMEOUT",
                "服务端进程已启动，但在时限内未通过本机 RCON 认证；为避免数据损坏未强制终止，请检查日志和实时状态",
                true);
        }
        catch
        {
            if (process.HasExited) registry.Clear(process);
            throw;
        }
    }

    public async Task<bool> StopManagedAsync(CancellationToken cancellationToken = default)
    {
        var process = registry.GetRunning();
        if (process is null) return false;
        await SendConsoleCommandAsync(process, "/save", cancellationToken);
        await Task.Delay(250, cancellationToken);
        await SendConsoleCommandAsync(process, "/stop", cancellationToken);
        await WaitForExitAsync(process, GracefulStopTimeout, "MANAGED_STOP_UNCONFIRMED", cancellationToken);
        registry.Clear(process);
        return true;
    }

    public static ProcessStartInfo CreateStartInfo(ManagedLaunchProfile profile, bool redirectStandardInput = true)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = profile.ExecutablePath,
            WorkingDirectory = profile.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = redirectStandardInput
        };
        foreach (var argument in profile.Arguments) startInfo.ArgumentList.Add(argument);
        return startInfo;
    }

    internal static Process StartProcess(ManagedLaunchProfile profile, bool redirectStandardInput)
    {
        var process = new Process { StartInfo = CreateStartInfo(profile, redirectStandardInput), EnableRaisingEvents = true };
        try
        {
            if (!process.Start()) throw new ServerInitializationException("SERVER_START_FAILED", "Windows 未能启动 AvorionServer.exe", true);
            return process;
        }
        catch (Exception exception) when (exception is not ServerInitializationException)
        {
            process.Dispose();
            throw new ServerInitializationException("SERVER_START_FAILED", "启动 AvorionServer.exe 失败", true, exception);
        }
    }

    internal static void ValidateProfile(ManagedLaunchProfile profile, bool requireNewGalaxy)
    {
        if (profile.SchemaVersion != 1 ||
            !Path.IsPathFullyQualified(profile.ExecutablePath) || !File.Exists(profile.ExecutablePath) ||
            !Path.IsPathFullyQualified(profile.WorkingDirectory) || !Directory.Exists(profile.WorkingDirectory) ||
            !Path.IsPathFullyQualified(profile.GalaxyDirectory) ||
            requireNewGalaxy && !profile.GalaxyMode.Equals("new", StringComparison.Ordinal) ||
            profile.Arguments.Count != 10)
            throw new ServerInitializationException("LAUNCH_PROFILE_INVALID", "受控启动档案缺失、路径无效或参数结构不符合白名单");

        var expected = new[]
        {
            "--datapath", Directory.GetParent(profile.GalaxyDirectory)?.FullName ?? string.Empty,
            "--galaxy-name", Path.GetFileName(profile.GalaxyDirectory),
            "--port", profile.Arguments[5],
            "--max-players", profile.Arguments[7],
            "--server-name", profile.Arguments[9]
        };
        if (!profile.Arguments.SequenceEqual(expected, StringComparer.Ordinal))
            throw new ServerInitializationException("LAUNCH_ARGUMENTS_NOT_ALLOWED", "启动参数不符合固定 Avorion 白名单");
    }

    private static async Task WaitForFileAsync(Process process, string path, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (process.HasExited)
                throw new ServerInitializationException("INITIALIZATION_PROCESS_EXITED", $"首次初始化进程提前退出，退出码 {process.ExitCode}", true);
            if (File.Exists(path) && new FileInfo(path).Length > 0) return;
            await Task.Delay(250, cancellationToken);
        }
        throw new ServerInitializationException("SERVER_INI_CREATION_TIMEOUT", "首次初始化未在时限内生成 server.ini；进程保持运行，请检查日志", true);
    }

    private static async Task WaitForServerReadyAsync(
        Process process,
        string galaxyDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        var processStartedAt = process.StartTime.ToUniversalTime().AddSeconds(-5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (process.HasExited)
                throw new ServerInitializationException("INITIALIZATION_PROCESS_EXITED", $"首次初始化进程提前退出，退出码 {process.ExitCode}", true);

            try
            {
                foreach (var path in Directory.EnumerateFiles(galaxyDirectory, "serverlog*.txt", SearchOption.TopDirectoryOnly))
                {
                    var info = new FileInfo(path);
                    if (info.LastWriteTimeUtc < processStartedAt) continue;
                    await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
                    if (stream.Length > 512 * 1024) stream.Seek(-512 * 1024, SeekOrigin.End);
                    using var reader = new StreamReader(stream, Encoding.UTF8, true, 4096, leaveOpen: false);
                    var text = await reader.ReadToEndAsync(cancellationToken);
                    if (text.Contains("Server startup complete.", StringComparison.Ordinal)) return;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The game keeps its active log open. A sharing race is transient; retry until the bounded deadline.
            }

            await Task.Delay(250, cancellationToken);
        }

        throw new ServerInitializationException(
            "SERVER_READY_TIMEOUT",
            "首次初始化已生成 server.ini，但服务端未在时限内完成启动；进程保持运行，请检查 Galaxy 服务端日志",
            true);
    }

    private static async Task SendConsoleCommandAsync(Process process, string command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (process.HasExited) throw new ServerInitializationException("SERVER_PROCESS_EXITED", "发送控制台命令前服务端进程已退出", true);
        await process.StandardInput.WriteLineAsync(command.AsMemory(), cancellationToken);
        await process.StandardInput.FlushAsync(cancellationToken);
    }

    private static async Task WaitForExitAsync(Process process, TimeSpan timeout, string errorCode, CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try { await process.WaitForExitAsync(timeoutSource.Token); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ServerInitializationException(errorCode, "服务端未确认 /stop；不会强制终止，也不会在运行中修改 server.ini", true);
        }
    }

}

internal static class SourceRconProbe
{
    public static async Task<bool> AuthenticateAsync(
        IPAddress address,
        int port,
        string password,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            using var client = new TcpClient(address.AddressFamily);
            await client.ConnectAsync(address, port, timeoutSource.Token);
            await using var stream = client.GetStream();
            const int requestId = 0x41564F52;
            await WritePacketAsync(stream, requestId, 3, password, timeoutSource.Token);
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var response = await ReadPacketAsync(stream, timeoutSource.Token);
                if (response.Type == 2) return response.Id == requestId;
            }
            return false;
        }
        catch (Exception exception) when (exception is SocketException or IOException or OperationCanceledException)
        {
            return false;
        }
    }

    public static async Task<bool> ExecuteAsync(
        IPAddress address,
        int port,
        string password,
        string command,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            using var client = new TcpClient(address.AddressFamily);
            await client.ConnectAsync(address, port, timeoutSource.Token);
            await using var stream = client.GetStream();
            const int authenticationId = 0x41564F52;
            const int commandId = 0x41564F53;
            await WritePacketAsync(stream, authenticationId, 3, password, timeoutSource.Token);
            var authenticated = false;
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var response = await ReadPacketAsync(stream, timeoutSource.Token);
                if (response.Id == -1) return false;
                if (response.Type == 2 && response.Id == authenticationId)
                {
                    authenticated = true;
                    break;
                }
            }
            if (!authenticated) return false;

            await WritePacketAsync(stream, commandId, 2, command, timeoutSource.Token);
            for (var attempt = 0; attempt < 5; attempt++)
            {
                var response = await ReadPacketAsync(stream, timeoutSource.Token);
                if (response.Id == commandId && response.Type is 0 or 2) return true;
            }
            return false;
        }
        catch (Exception exception) when (exception is SocketException or IOException or OperationCanceledException)
        {
            return false;
        }
    }

    private static async Task WritePacketAsync(Stream stream, int id, int type, string body, CancellationToken cancellationToken)
    {
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var length = 10 + bodyBytes.Length;
        if (length > 64 * 1024) throw new InvalidDataException("RCON packet too large");
        var packet = new byte[4 + length];
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(0, 4), length);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(4, 4), id);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(8, 4), type);
        bodyBytes.CopyTo(packet.AsSpan(12));
        await stream.WriteAsync(packet, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static async Task<(int Id, int Type, byte[] Body)> ReadPacketAsync(Stream stream, CancellationToken cancellationToken)
    {
        var lengthBytes = new byte[4];
        await stream.ReadExactlyAsync(lengthBytes, cancellationToken);
        var length = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
        if (length is < 10 or > 64 * 1024) throw new InvalidDataException("Invalid RCON packet length");
        var packet = new byte[length];
        await stream.ReadExactlyAsync(packet, cancellationToken);
        if (packet[^1] != 0 || packet[^2] != 0) throw new InvalidDataException("Invalid RCON packet terminator");
        return (BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(0, 4)), BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(4, 4)), packet[8..(length - 2)]);
    }

    internal static async Task<string> QueryBridgeAsync(IPAddress address, int port, string password,
        string command, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        using var client = new TcpClient(address.AddressFamily);
        await client.ConnectAsync(address, port, timeout.Token);
        await using var stream = client.GetStream();
        const int authId = 0x41564F54;
        const int queryId = 0x41564F55;
        await WritePacketAsync(stream, authId, 3, password, timeout.Token);
        var authenticated = false;
        for (var i = 0; i < 3; i++)
        {
            var packet = await ReadPacketAsync(stream, timeout.Token);
            if (packet.Id == -1) break;
            if (packet.Id == authId && packet.Type == 2) { authenticated = true; break; }
        }
        if (!authenticated) throw new ServerControlException("BRIDGE_RCON_AUTH_FAILED", "管理组件 RCON 认证失败");
        await WritePacketAsync(stream, queryId, 2, command, timeout.Token);
        using var buffer = new MemoryStream();
        for (var i = 0; i < 32; i++)
        {
            var packet = await ReadPacketAsync(stream, timeout.Token);
            if (packet.Id != queryId || packet.Type is not (0 or 2))
                throw new InvalidDataException("Unexpected bridge RCON response");
            buffer.Write(packet.Body);
            if (buffer.Length > 32768) throw new InvalidDataException("Bridge response too large");
            // RCON can split a UTF-8 character across packets. Decode strictly only after a full frame arrives.
            var text = Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
            if (text.TrimEnd().EndsWith(ManagementBridgeProtocol.End, StringComparison.Ordinal))
                return new UTF8Encoding(false, true).GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
            if (text.Contains("ORION_SECTOR_HAS_PLAYER", StringComparison.Ordinal))
                throw new ServerControlException("SECTOR_HAS_PLAYER", "目标星区已有在线玩家，已拒绝卸载");
            if (text.Contains("ORION_SECTOR_NOT_LOADED", StringComparison.Ordinal))
                throw new ServerControlException("SECTOR_NOT_LOADED", "目标星区已不在加载列表中，请刷新页面");
            if (text.Contains("ORION_UNLOAD_REJECTED", StringComparison.Ordinal))
                throw new ServerControlException("SECTOR_UNLOAD_REJECTED", "Avorion 当前拒绝卸载该星区，可能仍有系统活动保持它加载", true);
            if (text.Contains("ORION_UNLOAD_FAILED", StringComparison.Ordinal))
                throw new ServerControlException("SECTOR_UNLOAD_FAILED", "Avorion 执行星区卸载请求时发生错误", true);
            if (text.Contains("ORION_PLAYER_NOT_FOUND", StringComparison.Ordinal))
                throw new ServerControlException("PLAYER_NOT_FOUND", "服务端找不到该玩家档案，请刷新玩家列表");
            if (text.Contains("ORION_ALLIANCE_NOT_FOUND", StringComparison.Ordinal))
                throw new ServerControlException("ALLIANCE_NOT_FOUND", "服务端找不到该联盟，请刷新联盟列表");
            if (text.Contains("ORION_ALLIANCE_ASSETS_INVALID", StringComparison.Ordinal))
                throw new ServerControlException("ALLIANCE_ASSETS_INVALID", "联盟当前资产无法安全读取");
            if (text.Contains("ORION_ALLIANCE_REWARD_EMPTY", StringComparison.Ordinal))
                throw new ServerControlException("ALLIANCE_REWARD_EMPTY", "奖励内容为空，未修改联盟资产");
            if (text.Contains("ORION_ALLIANCE_REWARD_LIMIT", StringComparison.Ordinal))
                throw new ServerControlException("ALLIANCE_REWARD_BALANCE_LIMIT", "联盟现有资产加上本次奖励将超出安全数值范围，未执行发放");
            if (text.Contains("ORION_ALLIANCE_REWARD_INVALID", StringComparison.Ordinal))
                throw new ServerControlException("ALLIANCE_REWARD_INVALID", "管理组件拒绝了无效的联盟奖励参数");
            if (text.Contains("ORION_ALLIANCE_REWARD_BALANCE_INVALID", StringComparison.Ordinal))
                throw new ServerControlException("ALLIANCE_BALANCE_INVALID", "联盟当前资产无法安全读取，未执行发放");
            if (text.Contains("ORION_ALLIANCE_REWARD_VERIFY_FAILED", StringComparison.Ordinal))
                throw new ServerControlException("ALLIANCE_REWARD_VERIFY_FAILED", "奖励调用后未能确认精确联盟资产变化；请立即刷新资产核对，系统不会自动重试");
            if (text.Contains("ORION_ALLIANCE_REWARD_FAILED", StringComparison.Ordinal))
                throw new ServerControlException("ALLIANCE_REWARD_FAILED", "Avorion 拒绝或未能执行联盟奖励，未确认任何资产变化");
            if (text.Contains("ORION_MAILBOX_FULL", StringComparison.Ordinal))
                throw new ServerControlException("PLAYER_MAILBOX_FULL", "玩家邮箱已满，未投递礼包邮件");
            if (text.Contains("ORION_MAIL_EMPTY", StringComparison.Ordinal))
                throw new ServerControlException("PLAYER_MAIL_EMPTY", "邮件礼包内容为空，未执行投递");
            if (text.Contains("ORION_MAIL_INVALID", StringComparison.Ordinal))
                throw new ServerControlException("PLAYER_MAIL_INVALID", "管理组件拒绝了无效的邮件参数");
            if (text.Contains("ORION_MAIL_VERIFY_FAILED", StringComparison.Ordinal))
                throw new ServerControlException("PLAYER_MAIL_VERIFY_FAILED", "邮件调用后未能按唯一标识确认礼包附件；系统不会自动重发");
            if (text.Contains("ORION_MAIL_FAILED", StringComparison.Ordinal))
                throw new ServerControlException("PLAYER_MAIL_FAILED", "Avorion 拒绝或未能投递礼包邮件");
            if (text.Contains("ORION_REWARD_EMPTY", StringComparison.Ordinal))
                throw new ServerControlException("PLAYER_REWARD_EMPTY", "奖励内容为空，未修改玩家资产");
            if (text.Contains("ORION_REWARD_LIMIT", StringComparison.Ordinal))
                throw new ServerControlException("PLAYER_REWARD_BALANCE_LIMIT", "玩家现有资产加上本次奖励将超出安全数值范围，未执行发放");
            if (text.Contains("ORION_REWARD_INVALID", StringComparison.Ordinal))
                throw new ServerControlException("PLAYER_REWARD_INVALID", "管理组件拒绝了无效的玩家奖励参数");
            if (text.Contains("ORION_REWARD_BALANCE_INVALID", StringComparison.Ordinal))
                throw new ServerControlException("PLAYER_BALANCE_INVALID", "玩家当前资产无法安全读取，未执行发放");
            if (text.Contains("ORION_REWARD_VERIFY_FAILED", StringComparison.Ordinal))
                throw new ServerControlException("PLAYER_REWARD_VERIFY_FAILED", "奖励调用后未能确认精确资产变化；请立即刷新资产核对，系统不会自动重试");
            if (text.Contains("ORION_REWARD_FAILED", StringComparison.Ordinal))
                throw new ServerControlException("PLAYER_REWARD_FAILED", "Avorion 拒绝或未能执行玩家奖励，未确认任何资产变化");
            if (text.Contains("ORION_CONSOLE_ONLY", StringComparison.Ordinal) || text.Contains("ORION_INVALID_", StringComparison.Ordinal) ||
                text.Contains("ORION_ACTION_NOT_ALLOWED", StringComparison.Ordinal) || text.Contains("ORION_QUERY_FAILED", StringComparison.Ordinal))
                throw new ServerControlException("BRIDGE_REQUEST_REJECTED", "管理组件拒绝了请求");
        }
        throw new InvalidDataException("Incomplete bridge response");
    }
}
