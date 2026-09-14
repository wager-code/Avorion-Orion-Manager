using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AvorionAdmin.Core.Abstractions;
using AvorionAdmin.Core.Configuration;
using AvorionAdmin.Core.Models;
using Microsoft.Extensions.Options;

namespace AvorionAdmin.Agent;

public sealed class ServerControlException(
    string code,
    string message,
    bool retryable = false,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string Code { get; } = code;
    public bool Retryable { get; } = retryable;
}

public sealed partial class ManagedServerControlService(
    ManagedServerProcessRegistry registry,
    ISteamQueryProbe steamQueryProbe,
    IOptions<ServerNodeOptions> options) : IManagedServerControlService, IPlayerRewardService, IAllianceRewardService, IPlayerMailService, IInventoryService
{
    private const int MaximumProfileBytes = 512 * 1024;
    private const int MaximumServerIniBytes = 2 * 1024 * 1024;
    private static readonly TimeSpan RconTimeout = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan StartHealthTimeout = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan RconStabilityWindow = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromMinutes(1);
    private readonly ServerNodeOptions _options = options.Value;
    private readonly SemaphoreSlim _actionLock = new(1, 1);
    private readonly SemaphoreSlim _statusProbeLock = new(1, 1);
    private readonly SemaphoreSlim _bridgeProbeLock = new(1, 1);
    private (int ProcessId, DateTimeOffset SampledAt, ConnectionProbe Probe)? _cachedRconProbe;
    private (int ProcessId, DateTimeOffset SampledAt, int OnlinePlayers)? _cachedBridgePlayers;

    public async Task<ServerStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;
        try
        {
            var context = LoadContext();
            var process = ResolveOwnedProcess(context.Profile);
            if (process is null)
            {
                var stoppedQuery = await steamQueryProbe.ProbeAsync(false, cancellationToken);
                return BuildStatus(
                    context,
                    Lifecycle.Stopped,
                    null,
                    new ConnectionProbe(ConnectionState.Disconnected, null, "SERVER_NOT_RUNNING"),
                    stoppedQuery,
                    null,
                    now);
            }

            var rconProbe = await ProbeRconForStatusAsync(process, context, cancellationToken);
            var runningQuery = await steamQueryProbe.ProbeAsync(true, cancellationToken);
            var bridgePlayers = runningQuery.OnlinePlayers is null && rconProbe.Status == ConnectionState.Connected
                ? await ProbeBridgePlayersForStatusAsync(process, context, cancellationToken)
                : null;
            return BuildStatus(
                context,
                Lifecycle.Running,
                process,
                rconProbe,
                runningQuery,
                bridgePlayers,
                now);
        }
        catch (ServerControlException exception)
        {
            return new ServerStatus(
                _options.ServerId,
                _options.Name,
                Lifecycle.Unknown,
                null,
                null,
                null,
                null,
                null,
                null,
                new ConnectionProbe(ConnectionState.Unknown, null, exception.Code),
                new ConnectionProbe(ConnectionState.Unknown, null, "STEAM_QUERY_PROTOCOL_NOT_IMPLEMENTED"),
                new LastSaveInfo(null, null, exception.Code),
                new AgentInfo(true, now),
                new Provenance(now, "managed-profile", "unavailable", exception.Code));
        }
    }

    private async Task<int?> ProbeBridgePlayersForStatusAsync(
        Process process,
        ManagedControlContext context,
        CancellationToken cancellationToken)
    {
        var cached = _cachedBridgePlayers;
        if (cached is not null && cached.Value.ProcessId == process.Id &&
            DateTimeOffset.UtcNow - cached.Value.SampledAt < TimeSpan.FromSeconds(15))
            return cached.Value.OnlinePlayers;
        await _bridgeProbeLock.WaitAsync(cancellationToken);
        try
        {
            cached = _cachedBridgePlayers;
            if (cached is not null && cached.Value.ProcessId == process.Id &&
                DateTimeOffset.UtcNow - cached.Value.SampledAt < TimeSpan.FromSeconds(15))
                return cached.Value.OnlinePlayers;
            var requestId = Guid.NewGuid().ToString("N");
            var response = await SourceRconProbe.QueryBridgeAsync(
                context.Address,
                context.Port,
                context.Password,
                $"/orionadmin players {requestId} 0 10",
                cancellationToken);
            var total = ManagementBridgeProtocol.Parse(response, requestId, "players", 0, 10).GetProperty("total").GetInt32();
            _cachedBridgePlayers = (process.Id, DateTimeOffset.UtcNow, total);
            return total;
        }
        catch (Exception exception) when (exception is ServerControlException or IOException or SocketException or
            OperationCanceledException or JsonException or KeyNotFoundException or InvalidOperationException or
            FormatException or OverflowException)
        {
            return null;
        }
        finally
        {
            _bridgeProbeLock.Release();
        }
    }

    private async Task<ConnectionProbe> ProbeRconForStatusAsync(
        Process process,
        ManagedControlContext context,
        CancellationToken cancellationToken)
    {
        var cached = _cachedRconProbe;
        if (cached is not null && cached.Value.ProcessId == process.Id &&
            DateTimeOffset.UtcNow - cached.Value.SampledAt < TimeSpan.FromSeconds(10))
            return cached.Value.Probe;

        await _statusProbeLock.WaitAsync(cancellationToken);
        try
        {
            cached = _cachedRconProbe;
            if (cached is not null && cached.Value.ProcessId == process.Id &&
                DateTimeOffset.UtcNow - cached.Value.SampledAt < TimeSpan.FromSeconds(10))
                return cached.Value.Probe;

            var started = DateTimeOffset.UtcNow;
            var authenticated = await SourceRconProbe.AuthenticateAsync(
                context.Address,
                context.Port,
                context.Password,
                TimeSpan.FromSeconds(2),
                cancellationToken);
            var probe = authenticated
                ? new ConnectionProbe(ConnectionState.Connected, Math.Max(0, (DateTimeOffset.UtcNow - started).TotalMilliseconds))
                : new ConnectionProbe(ConnectionState.Degraded, null, "RCON_AUTHENTICATION_FAILED");
            _cachedRconProbe = (process.Id, DateTimeOffset.UtcNow, probe);
            return probe;
        }
        finally
        {
            _statusProbeLock.Release();
        }
    }

    public async Task<ManagedServerActionResult> ExecuteAsync(
        string action,
        Func<double, string, CancellationToken, Task> reportProgressAsync,
        CancellationToken cancellationToken = default)
    {
        await _actionLock.WaitAsync(cancellationToken);
        try
        {
            var startedAt = DateTimeOffset.UtcNow;
            var normalized = action.Trim().ToLowerInvariant();
            return normalized switch
            {
                "start" => await StartAsync(normalized, startedAt, reportProgressAsync, cancellationToken),
                "save" => await SaveAsync(normalized, startedAt, reportProgressAsync, cancellationToken),
                "shutdown" => await ShutdownAsync(normalized, startedAt, reportProgressAsync, cancellationToken),
                "restart" => await RestartAsync(normalized, startedAt, reportProgressAsync, cancellationToken),
                _ => throw new ServerControlException("CONTROL_ACTION_NOT_ALLOWED", "该服务器控制操作不在允许列表中")
            };
        }
        finally
        {
            _actionLock.Release();
        }
    }

    public async Task<ManagedServerActionResult> ForceStopAsync(
        int expectedProcessId,
        Func<double, string, CancellationToken, Task> reportProgressAsync,
        CancellationToken cancellationToken = default)
    {
        await _actionLock.WaitAsync(cancellationToken);
        try
        {
            var startedAt = DateTimeOffset.UtcNow;
            var context = LoadContext();
            await reportProgressAsync(15, "validating-managed-profile", cancellationToken);
            var process = ResolveOwnedProcess(context.Profile)
                ?? throw new ServerControlException("SERVER_NOT_RUNNING", "受管服务器没有运行，无需强制终止");
            if (process.Id != expectedProcessId)
                throw new ServerControlException("PROCESS_ID_CHANGED", "受管进程 PID 已变化，已拒绝强制终止；请刷新状态后重新确认");

            await reportProgressAsync(45, "force-stopping-exact-process", cancellationToken);
            try
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or TimeoutException)
            {
                throw new ServerControlException("FORCE_STOP_FAILED", "无法确认受管进程已被强制终止", true, exception);
            }
            finally
            {
                if (process.HasExited) registry.Clear(process);
            }

            await reportProgressAsync(100, "force-stop-confirmed", cancellationToken);
            return Result("force-stop", "force-stopped", Lifecycle.Stopped, null, false,
                ["managed-profile-validated", "exact-process-path", $"expected-pid:{expectedProcessId}", "process-exit-confirmed"], startedAt);
        }
        finally
        {
            _actionLock.Release();
        }
    }

    private async Task<ManagedServerActionResult> StartAsync(
        string action,
        DateTimeOffset startedAt,
        Func<double, string, CancellationToken, Task> reportProgressAsync,
        CancellationToken cancellationToken)
    {
        var context = LoadContext();
        await reportProgressAsync(10, "validating-managed-profile", cancellationToken);
        var existing = ResolveOwnedProcess(context.Profile);
        if (existing is not null)
        {
            await RequireRconAsync(context, "START_EXISTING_RCON_FAILED", cancellationToken);
            await reportProgressAsync(100, "already-running-verified", cancellationToken);
            return Result(action, "already-running", Lifecycle.Running, existing.Id, true,
                ["managed-profile-validated", "exact-process-path", "rcon-authenticated"], startedAt);
        }

        await reportProgressAsync(30, "starting-managed-server", cancellationToken);
        var process = ManagedServerRuntime.StartProcess(context.Profile, redirectStandardInput: false);
        try
        {
            registry.Register(process);
        }
        catch
        {
            process.Dispose();
            throw;
        }

        await reportProgressAsync(55, "waiting-for-rcon", cancellationToken);
        var deadline = DateTimeOffset.UtcNow + StartHealthTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (process.HasExited)
            {
                var exitCode = process.ExitCode;
                registry.Clear(process);
                throw new ServerControlException(
                    "SERVER_EXITED_DURING_START",
                    $"服务端启动后提前退出，退出码 {exitCode}；请查看实时日志",
                    true);
            }
            if (await SourceRconProbe.AuthenticateAsync(context.Address, context.Port, context.Password, TimeSpan.FromSeconds(2), cancellationToken))
            {
                await reportProgressAsync(90, "verifying-server-stability", cancellationToken);
                await RequireStableProcessAndRconAsync(
                    process,
                    context,
                    "SERVER_EXITED_AFTER_RCON_READY",
                    "服务端通过 RCON 认证后很快退出",
                    cancellationToken);
                await reportProgressAsync(100, "rcon-authenticated", cancellationToken);
                return Result(action, "started", Lifecycle.Running, process.Id, true,
                    ["managed-profile-validated", "exact-process-started", "rcon-authenticated"], startedAt);
            }
            await Task.Delay(500, cancellationToken);
        }

        throw new ServerControlException(
            "RCON_HEALTH_TIMEOUT",
            $"服务端进程 PID {process.Id} 已启动，但未在时限内通过本机 RCON 认证；未强制终止，请检查实时状态和日志",
            true);
    }

    private async Task<ManagedServerActionResult> SaveAsync(
        string action,
        DateTimeOffset startedAt,
        Func<double, string, CancellationToken, Task> reportProgressAsync,
        CancellationToken cancellationToken)
    {
        var context = LoadContext();
        await reportProgressAsync(15, "resolving-managed-process", cancellationToken);
        var process = RequireRunningProcess(context.Profile);
        await reportProgressAsync(40, "authenticating-rcon", cancellationToken);
        await ExecuteRconAsync(context, "/save", "SAVE_COMMAND_REJECTED", cancellationToken);
        await reportProgressAsync(100, "save-command-acknowledged", cancellationToken);
        return Result(action, "save-acknowledged", Lifecycle.Running, process.Id, true,
            ["exact-process-path", "rcon-authenticated", "save-command-acknowledged"], startedAt);
    }

    private async Task<ManagedServerActionResult> ShutdownAsync(
        string action,
        DateTimeOffset startedAt,
        Func<double, string, CancellationToken, Task> reportProgressAsync,
        CancellationToken cancellationToken)
    {
        var context = LoadContext();
        await reportProgressAsync(10, "resolving-managed-process", cancellationToken);
        var process = ResolveOwnedProcess(context.Profile);
        if (process is null)
        {
            await reportProgressAsync(100, "already-stopped-verified", cancellationToken);
            return Result(action, "already-stopped", Lifecycle.Stopped, null, false,
                ["managed-profile-validated", "no-owned-process-running"], startedAt);
        }

        var processId = process.Id;
        await SafeStopAsync(context, process, reportProgressAsync, 25, 100, cancellationToken);
        return Result(action, "stopped", Lifecycle.Stopped, null, false,
            ["exact-process-path", "rcon-authenticated", "save-command-acknowledged", "stop-command-acknowledged", $"process-{processId}-exited"], startedAt);
    }

    private async Task<ManagedServerActionResult> RestartAsync(
        string action,
        DateTimeOffset startedAt,
        Func<double, string, CancellationToken, Task> reportProgressAsync,
        CancellationToken cancellationToken)
    {
        var context = LoadContext();
        await reportProgressAsync(5, "resolving-managed-process", cancellationToken);
        var process = RequireRunningProcess(context.Profile);
        var previousProcessId = process.Id;
        await SafeStopAsync(context, process, reportProgressAsync, 10, 50, cancellationToken);

        await reportProgressAsync(60, "starting-managed-server", cancellationToken);
        var replacement = ManagedServerRuntime.StartProcess(context.Profile, redirectStandardInput: false);
        try
        {
            registry.Register(replacement);
        }
        catch
        {
            replacement.Dispose();
            throw;
        }

        var deadline = DateTimeOffset.UtcNow + StartHealthTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (replacement.HasExited)
            {
                var exitCode = replacement.ExitCode;
                registry.Clear(replacement);
                throw new ServerControlException(
                    "SERVER_EXITED_DURING_RESTART",
                    $"服务端重启后提前退出，退出码 {exitCode}；旧进程 PID {previousProcessId} 已安全停止",
                    true);
            }
            if (await SourceRconProbe.AuthenticateAsync(context.Address, context.Port, context.Password, TimeSpan.FromSeconds(2), cancellationToken))
            {
                await reportProgressAsync(90, "verifying-server-stability", cancellationToken);
                await RequireStableProcessAndRconAsync(
                    replacement,
                    context,
                    "SERVER_EXITED_AFTER_RESTART_RCON_READY",
                    "服务端重启并通过 RCON 认证后很快退出",
                    cancellationToken);
                await reportProgressAsync(100, "restart-rcon-authenticated", cancellationToken);
                return Result(action, "restarted", Lifecycle.Running, replacement.Id, true,
                    ["exact-process-path", "save-command-acknowledged", "stop-command-acknowledged", $"process-{previousProcessId}-exited", "replacement-process-started", "rcon-authenticated"], startedAt);
            }
            await Task.Delay(500, cancellationToken);
        }

        throw new ServerControlException(
            "RESTART_RCON_HEALTH_TIMEOUT",
            $"旧进程 PID {previousProcessId} 已安全停止，新进程 PID {replacement.Id} 已启动但未通过 RCON 健康检查；未强制终止",
            true);
    }

    private static async Task RequireStableProcessAndRconAsync(
        Process process,
        ManagedControlContext context,
        string exitCode,
        string exitMessage,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + RconStabilityWindow;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (process.HasExited)
                throw new ServerControlException(exitCode, $"{exitMessage}，退出码 {process.ExitCode}；请查看实时日志", true);
            await Task.Delay(250, cancellationToken);
        }
        if (!await SourceRconProbe.AuthenticateAsync(context.Address, context.Port, context.Password, TimeSpan.FromSeconds(2), cancellationToken))
            throw new ServerControlException("RCON_UNSTABLE", "服务端首次通过 RCON 认证，但稳定观察后复检失败", true);
    }

    private async Task SafeStopAsync(
        ManagedControlContext context,
        Process process,
        Func<double, string, CancellationToken, Task> reportProgressAsync,
        double startProgress,
        double endProgress,
        CancellationToken cancellationToken)
    {
        await reportProgressAsync(startProgress, "saving-before-stop", cancellationToken);
        await ExecuteRconAsync(context, "/save", "SAVE_BEFORE_STOP_REJECTED", cancellationToken);
        await Task.Delay(500, cancellationToken);
        await reportProgressAsync(startProgress + (endProgress - startProgress) * 0.4, "requesting-safe-stop", cancellationToken);
        await ExecuteRconAsync(context, "/stop", "STOP_COMMAND_REJECTED", cancellationToken);
        await reportProgressAsync(startProgress + (endProgress - startProgress) * 0.7, "waiting-for-process-exit", cancellationToken);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(StopTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ServerControlException(
                "SAFE_STOP_UNCONFIRMED",
                $"已发送 /save 与 /stop，但进程 PID {process.Id} 未在时限内退出；未执行强制终止",
                true);
        }
        registry.Clear(process);
        await reportProgressAsync(endProgress, "safe-stop-confirmed", cancellationToken);
    }

    private Process RequireRunningProcess(ManagedLaunchProfile profile) =>
        ResolveOwnedProcess(profile)
        ?? throw new ServerControlException("SERVER_NOT_RUNNING", "受管 Avorion 服务端当前未运行");

    private async Task RequireRconAsync(ManagedControlContext context, string code, CancellationToken cancellationToken)
    {
        if (!await SourceRconProbe.AuthenticateAsync(context.Address, context.Port, context.Password, RconTimeout, cancellationToken))
            throw new ServerControlException(code, "受管进程存在，但本机 RCON 认证失败；已拒绝控制操作", true);
    }

    private async Task ExecuteRconAsync(ManagedControlContext context, string command, string code, CancellationToken cancellationToken)
    {
        if (!await SourceRconProbe.ExecuteAsync(context.Address, context.Port, context.Password, command, RconTimeout, cancellationToken))
            throw new ServerControlException(code, $"本机 RCON 未确认命令 {command}；未执行后续步骤", true);
    }

    private Process? ResolveOwnedProcess(ManagedLaunchProfile profile)
    {
        var expectedPath = Path.GetFullPath(profile.ExecutablePath);
        var tracked = registry.GetRunning();
        if (tracked is not null)
        {
            var trackedPath = ReadProcessPath(tracked);
            if (!trackedPath.Equals(expectedPath, StringComparison.OrdinalIgnoreCase))
                throw new ServerControlException("TRACKED_PROCESS_MISMATCH", "受管进程注册表与当前启动档案不一致，已拒绝控制操作");
            return tracked;
        }

        var processName = Path.GetFileNameWithoutExtension(expectedPath);
        var matches = new List<Process>();
        var ownershipUnknown = false;
        foreach (var candidate in Process.GetProcessesByName(processName))
        {
            try
            {
                candidate.Refresh();
                if (candidate.HasExited)
                {
                    candidate.Dispose();
                    continue;
                }
                var candidatePath = candidate.MainModule?.FileName;
                if (!string.IsNullOrWhiteSpace(candidatePath) &&
                    Path.GetFullPath(candidatePath).Equals(expectedPath, StringComparison.OrdinalIgnoreCase))
                    matches.Add(candidate);
                else candidate.Dispose();
            }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
            {
                ownershipUnknown = true;
                candidate.Dispose();
            }
        }

        if (ownershipUnknown)
        {
            foreach (var match in matches) match.Dispose();
            throw new ServerControlException("SERVER_PROCESS_OWNERSHIP_UNKNOWN", "发现同名进程但无法验证可执行文件路径，已拒绝控制操作");
        }
        if (matches.Count > 1)
        {
            foreach (var match in matches) match.Dispose();
            throw new ServerControlException("MULTIPLE_MANAGED_PROCESSES", "发现多个来自受管路径的 Avorion 进程，已拒绝控制操作");
        }
        if (matches.Count == 0) return null;

        var process = matches[0];
        try
        {
            return registry.RegisterOrGet(process);
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }

    private ManagedControlContext LoadContext()
    {
        var profilePath = Path.Combine(Path.GetFullPath(_options.DataDirectory), "managed", "server-launch-profile.json");
        var profileInfo = new FileInfo(profilePath);
        if (!profileInfo.Exists)
            throw new ServerControlException("MANAGED_PROFILE_MISSING", "尚未完成服务器配置，没有可用的受控启动档案");
        if (profileInfo.Length is <= 0 or > MaximumProfileBytes)
            throw new ServerControlException("MANAGED_PROFILE_INVALID", "受控启动档案大小无效");

        ManagedLaunchProfile profile;
        try
        {
            profile = JsonSerializer.Deserialize<ManagedLaunchProfile>(
                File.ReadAllBytes(profilePath),
                new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? throw new JsonException("empty managed profile");
            ManagedServerRuntime.ValidateProfile(profile, requireNewGalaxy: false);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or ServerInitializationException)
        {
            throw new ServerControlException("MANAGED_PROFILE_INVALID", "受控启动档案无法验证，已拒绝服务器控制", false, exception);
        }

        if (!profile.RconEnabled || !IPAddress.TryParse(profile.RconHost, out var address) || !IPAddress.IsLoopback(address))
            throw new ServerControlException("RCON_NOT_LOCAL", "服务器控制只允许使用受控的本机 RCON 配置");

        var serverIniPath = Path.Combine(profile.GalaxyDirectory, "server.ini");
        var values = ReadNetworkingValues(serverIniPath);
        if (!values.TryGetValue("rconPassword", out var password) || string.IsNullOrWhiteSpace(password) || password.Length > 128)
            throw new ServerControlException("RCON_PASSWORD_UNAVAILABLE", "server.ini 中没有可用的 RCON 密码");
        if (!values.TryGetValue("rconPort", out var portText) || !int.TryParse(portText.Trim(), out var port) || port != profile.RconPort)
            throw new ServerControlException("RCON_PORT_MISMATCH", "server.ini 的 RCON 端口与受控启动档案不一致");
        if (!values.TryGetValue("rconIp", out var addressText) || !IPAddress.TryParse(addressText.Trim(), out var iniAddress) || !IPAddress.IsLoopback(iniAddress))
            throw new ServerControlException("RCON_ADDRESS_NOT_LOCAL", "server.ini 的 RCON 地址不是本机回环地址");

        return new ManagedControlContext(profile, address, port, password);
    }

    private static Dictionary<string, string> ReadNetworkingValues(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new ServerControlException("SERVER_INI_MISSING", "Galaxy 中没有 server.ini，无法执行服务器控制");
        if (info.Length is <= 0 or > MaximumServerIniBytes)
            throw new ServerControlException("SERVER_INI_INVALID", "server.ini 大小无效");

        string text;
        try { text = ServerIniEncoding.Decode(File.ReadAllBytes(path)).Text; }
        catch (DecoderFallbackException exception)
        {
            throw new ServerControlException("SERVER_INI_ENCODING_UNSUPPORTED", "server.ini 既不是有效 UTF-8，也不是当前 Windows ANSI 编码", false, exception);
        }

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var inNetworking = false;
        var networkingSections = 0;
        foreach (var rawLine in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = rawLine.Trim();
            if (trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal))
            {
                inNetworking = trimmed.Equals("[Networking]", StringComparison.OrdinalIgnoreCase);
                if (inNetworking && ++networkingSections > 1)
                    throw new ServerControlException("SERVER_INI_AMBIGUOUS", "server.ini 包含重复的 [Networking] 段");
                continue;
            }
            if (!inNetworking || trimmed.Length == 0 || trimmed[0] is ';' or '#') continue;
            var separator = rawLine.IndexOf('=');
            if (separator <= 0) continue;
            var key = rawLine[..separator].Trim();
            if (key is not ("rconIp" or "rconPassword" or "rconPort") &&
                !key.Equals("rconIp", StringComparison.OrdinalIgnoreCase) &&
                !key.Equals("rconPassword", StringComparison.OrdinalIgnoreCase) &&
                !key.Equals("rconPort", StringComparison.OrdinalIgnoreCase)) continue;
            if (!values.TryAdd(key, rawLine[(separator + 1)..]))
                throw new ServerControlException("SERVER_INI_AMBIGUOUS", $"server.ini 包含重复配置项 {key}");
        }
        if (networkingSections != 1)
            throw new ServerControlException("SERVER_INI_NETWORKING_MISSING", "server.ini 中没有唯一的 [Networking] 段");
        return values;
    }

    private ServerStatus BuildStatus(
        ManagedControlContext context,
        Lifecycle lifecycle,
        Process? process,
        ConnectionProbe rcon,
        SteamQuerySnapshot steamQuery,
        int? bridgeOnlinePlayers,
        DateTimeOffset sampledAt)
    {
        int? maxPlayers = null;
        if (context.Profile.Arguments.Count > 7 && int.TryParse(context.Profile.Arguments[7], out var parsedMaxPlayers))
            maxPlayers = parsedMaxPlayers;
        return new ServerStatus(
            _options.ServerId,
            context.Profile.Arguments.Count > 9 ? context.Profile.Arguments[9] : _options.Name,
            lifecycle,
            process?.Id,
            ReadVersion(context.Profile.ExecutablePath),
            Path.GetFileName(context.Profile.GalaxyDirectory),
            process is null ? null : SafeUptimeSeconds(process, sampledAt),
            steamQuery.OnlinePlayers ?? bridgeOnlinePlayers,
            steamQuery.MaxPlayers ?? maxPlayers,
            rcon,
            steamQuery.Connection,
            ReadLastSave(context.Profile.GalaxyDirectory),
            new AgentInfo(true, sampledAt),
            new Provenance(sampledAt, bridgeOnlinePlayers is null ? "managed-profile+rcon" : "managed-profile+rcon+orion-bridge", "live"));
    }

    private static ManagedServerActionResult Result(
        string action,
        string outcome,
        Lifecycle lifecycle,
        int? processId,
        bool rconAuthenticated,
        IReadOnlyList<string> evidence,
        DateTimeOffset startedAt) =>
        new(action, outcome, lifecycle, processId, rconAuthenticated, evidence, startedAt, DateTimeOffset.UtcNow);

    private static string? ReadVersion(string executablePath)
    {
        if (!File.Exists(executablePath)) return null;
        var fileVersion = FileVersionInfo.GetVersionInfo(executablePath).FileVersion;
        if (!string.IsNullOrWhiteSpace(fileVersion)) return fileVersion;
        try
        {
            var serverDirectory = Directory.GetParent(Directory.GetParent(executablePath)?.FullName ?? string.Empty)?.FullName;
            if (string.IsNullOrWhiteSpace(serverDirectory)) return null;
            var manifest = Path.Combine(serverDirectory, "steamapps", "appmanifest_565060.acf");
            if (!File.Exists(manifest)) return null;
            var match = Regex.Match(File.ReadAllText(manifest), "\\\"buildid\\\"\\s+\\\"([0-9]+)\\\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return match.Success ? $"Build {match.Groups[1].Value}" : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private static long? SafeUptimeSeconds(Process process, DateTimeOffset now)
    {
        try { return Math.Max(0, (long)(now - process.StartTime.ToUniversalTime()).TotalSeconds); }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception) { return null; }
    }

    private static string ReadProcessPath(Process process)
    {
        try
        {
            var path = process.MainModule?.FileName;
            return string.IsNullOrWhiteSpace(path)
                ? throw new ServerControlException("SERVER_PROCESS_OWNERSHIP_UNKNOWN", "无法读取受管进程的可执行文件路径")
                : Path.GetFullPath(path);
        }
        catch (ServerControlException) { throw; }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            throw new ServerControlException("SERVER_PROCESS_OWNERSHIP_UNKNOWN", "无法验证受管进程的可执行文件路径", false, exception);
        }
    }

    private static LastSaveInfo ReadLastSave(string galaxyDirectory)
    {
        try
        {
            var latest = Directory.EnumerateFiles(galaxyDirectory, "*", SearchOption.AllDirectories)
                .Take(100_000)
                .Select(path => new FileInfo(path))
                .Where(file => file.Exists && !file.Name.Equals("server.ini", StringComparison.OrdinalIgnoreCase) && !file.Name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
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

    private sealed record ManagedControlContext(
        ManagedLaunchProfile Profile,
        IPAddress Address,
        int Port,
        string Password);
}
