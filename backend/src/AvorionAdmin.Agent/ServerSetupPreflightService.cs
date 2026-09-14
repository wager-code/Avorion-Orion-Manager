using System.Net;
using System.Net.NetworkInformation;
using AvorionAdmin.Core.Abstractions;
using AvorionAdmin.Core.Models;

namespace AvorionAdmin.Agent;

public sealed class ServerSetupPreflightService(
    IUpdateEnvironmentStore updateEnvironmentStore,
    IUpdateEnvironmentService updateEnvironmentService) : IServerSetupPreflightService
{
    public async Task<ServerSetupPreflightResult> ValidateAsync(
        ServerSetupPreflightRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var issues = new List<ServerSetupValidationIssue>();
        ValidateText(request.ServerName, "serverName", "服务器名称", 1, 80, false, issues);
        ValidateText(request.GalaxyName, "galaxyName", "Galaxy 名称", 1, 64, true, issues);
        if (request.GalaxyMode is not ("new" or "existing"))
            Error(issues, "galaxyMode", "GALAXY_MODE_INVALID", "Galaxy 接入方式只允许 new 或 existing");
        if (request.MaxPlayers is < 1 or > 100)
            Error(issues, "maxPlayers", "MAX_PLAYERS_INVALID", "最大玩家数必须在 1–100 之间");
        if (!IPAddress.TryParse(request.ListenAddress, out _))
            Error(issues, "listenAddress", "LISTEN_ADDRESS_INVALID", "监听地址必须是有效 IP 地址");

        var portDefinitions = new List<(string Purpose, int Port, string Protocol)>
        {
            ("游戏端口", request.GamePort, "udp"),
            ("Steam Query", request.QueryPort, "udp")
        };
        if (request.RconEnabled) portDefinitions.Add(("RCON", request.RconPort, "tcp"));
        foreach (var definition in portDefinitions)
        {
            if (definition.Port is < 1 or > 65535)
                Error(issues, PortField(definition.Purpose), "PORT_OUT_OF_RANGE", $"{definition.Purpose}必须在 1–65535 之间");
        }
        var duplicatePorts = portDefinitions.GroupBy(item => item.Port).Where(group => group.Count() > 1).Select(group => group.Key).ToArray();
        foreach (var duplicate in duplicatePorts)
            Error(issues, "ports", "PORT_DUPLICATED", $"端口 {duplicate} 被多个用途重复使用");

        var passwordAccepted = !request.RconEnabled || IsValidPassword(request.RconPassword);
        if (!passwordAccepted)
            Error(issues, "rconPassword", "RCON_PASSWORD_INVALID", "启用 RCON 时密码必须为 8–128 位，且不能包含换行或控制字符");

        var galaxyPathValid = ValidateGalaxyPath(request.GalaxyDirectory, request.GalaxyName, request.GalaxyMode, issues);
        var environmentValid = await ValidateEnvironmentAsync(issues, cancellationToken);
        var ports = CheckPorts(portDefinitions);
        foreach (var port in ports.Where(item => !item.Available))
            Error(issues, PortField(port.Purpose), "PORT_IN_USE", $"{port.Purpose} {port.Port}/{port.Protocol.ToUpperInvariant()} 已被本机占用");

        if (request.AllowFirewallChange)
            Warning(issues, "allowFirewallChange", "FIREWALL_CHANGE_DEFERRED", "已记录防火墙变更意向；本阶段不会修改 Windows 防火墙");
        Warning(issues, "listenAddress", "GAME_BINDING_DEFERRED", "游戏监听地址尚未在 Avorion 2.5.13 启动参数中完成实机验证，本阶段只保存意向");
        Warning(issues, "queryPort", "QUERY_PORT_DEFERRED", "Steam Query 端口的独立配置项尚未完成实机验证，本阶段只检查端口占用");

        return new ServerSetupPreflightResult(
            issues.All(issue => issue.Severity != "error"),
            environmentValid,
            galaxyPathValid,
            passwordAccepted,
            ports,
            issues,
            DateTimeOffset.UtcNow);
    }

    private async Task<bool> ValidateEnvironmentAsync(
        List<ServerSetupValidationIssue> issues,
        CancellationToken cancellationToken)
    {
        var environment = await updateEnvironmentStore.GetAsync(cancellationToken);
        if (environment is null)
        {
            Error(issues, "updateEnvironment", "UPDATE_ENVIRONMENT_MISSING", "尚未保存 SteamCMD 与 Avorion 服务端路径");
            return false;
        }
        var validation = await updateEnvironmentService.ValidateAsync(
            environment.SteamCmdPath,
            environment.ServerDirectory,
            cancellationToken);
        if (validation.Valid) return true;
        Error(issues, "updateEnvironment", "UPDATE_ENVIRONMENT_INVALID", "已保存的 SteamCMD 或服务端路径不再有效，请返回更新页重新验证");
        return false;
    }

    private static bool ValidateGalaxyPath(
        string path,
        string galaxyName,
        string galaxyMode,
        List<ServerSetupValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) ||
            path.StartsWith("\\\\", StringComparison.Ordinal) || path.StartsWith("\\\\?\\", StringComparison.Ordinal))
        {
            Error(issues, "galaxyDirectory", "GALAXY_PATH_INVALID", "Galaxy 存档目录必须是 Windows 本地绝对路径");
            return false;
        }

        string fullPath;
        try { fullPath = Path.GetFullPath(path.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            Error(issues, "galaxyDirectory", "GALAXY_PATH_INVALID", "Galaxy 存档目录格式无效");
            return false;
        }
        var root = Path.GetPathRoot(fullPath)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(root) || fullPath.Equals(root, StringComparison.OrdinalIgnoreCase))
        {
            Error(issues, "galaxyDirectory", "GALAXY_PATH_UNSAFE", "不能把磁盘根目录作为 Galaxy 存档目录");
            return false;
        }

        if (!Path.GetFileName(fullPath).Equals(galaxyName.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            Error(issues, "galaxyDirectory", "GALAXY_PATH_NAME_MISMATCH", "Galaxy 存档目录的文件夹名称必须与 Galaxy 名称一致");
            return false;
        }

        if (galaxyMode == "existing")
        {
            if (!Directory.Exists(fullPath))
            {
                Error(issues, "galaxyDirectory", "GALAXY_NOT_FOUND", "选择的已有 Galaxy 目录不存在");
                return false;
            }
            if (!CanReadDirectory(fullPath))
            {
                Error(issues, "galaxyDirectory", "GALAXY_NOT_READABLE", "Server Agent 无法读取已有 Galaxy 目录");
                return false;
            }
        }
        else if (Directory.Exists(fullPath))
        {
            if (!TryDirectoryHasEntries(fullPath, out var hasEntries))
            {
                Error(issues, "galaxyDirectory", "NEW_GALAXY_TARGET_NOT_READABLE", "Server Agent 无法检查新 Galaxy 目标目录");
                return false;
            }
            if (hasEntries)
            {
                Error(issues, "galaxyDirectory", "NEW_GALAXY_TARGET_NOT_EMPTY", "新 Galaxy 目标目录已经存在且不为空");
                return false;
            }
        }

        var existingAncestor = FindExistingAncestor(fullPath);
        if (existingAncestor is null)
        {
            Error(issues, "galaxyDirectory", "GALAXY_PARENT_NOT_FOUND", "未找到可用的 Galaxy 父目录");
            return false;
        }
        if (HasReparsePoint(existingAncestor))
        {
            Error(issues, "galaxyDirectory", "GALAXY_PATH_REPARSE_POINT", "Galaxy 路径不能经过符号链接或目录联接");
            return false;
        }
        if (galaxyMode == "new" && !CanWriteDirectory(existingAncestor))
        {
            Error(issues, "galaxyDirectory", "GALAXY_PARENT_NOT_WRITABLE", "Server Agent 无权在 Galaxy 父目录创建内容");
            return false;
        }
        return true;
    }

    private static IReadOnlyList<PortAvailability> CheckPorts(
        IReadOnlyList<(string Purpose, int Port, string Protocol)> definitions)
    {
        HashSet<int> tcp;
        HashSet<int> udp;
        try
        {
            var properties = IPGlobalProperties.GetIPGlobalProperties();
            tcp = properties.GetActiveTcpListeners().Select(endpoint => endpoint.Port)
                .Concat(properties.GetActiveTcpConnections().Select(connection => connection.LocalEndPoint.Port))
                .ToHashSet();
            udp = properties.GetActiveUdpListeners().Select(endpoint => endpoint.Port).ToHashSet();
        }
        catch (NetworkInformationException)
        {
            return definitions.Select(item => new PortAvailability(
                item.Purpose, item.Port, item.Protocol, false, "无法读取本机端口表")).ToArray();
        }

        return definitions.Select(item =>
        {
            var valid = item.Port is >= 1 and <= 65535;
            var occupied = valid && (item.Protocol == "tcp" ? tcp.Contains(item.Port) : udp.Contains(item.Port));
            return new PortAvailability(
                item.Purpose,
                item.Port,
                item.Protocol,
                valid && !occupied,
                !valid ? "端口超出范围" : occupied ? "本机监听表中已占用" : "本机监听表中未发现占用");
        }).ToArray();
    }

    private static void ValidateText(
        string value,
        string field,
        string label,
        int minimum,
        int maximum,
        bool rejectPathCharacters,
        List<ServerSetupValidationIssue> issues)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length < minimum || trimmed.Length > maximum)
        {
            Error(issues, field, "TEXT_LENGTH_INVALID", $"{label}长度必须为 {minimum}–{maximum} 个字符");
            return;
        }
        if (trimmed.Any(char.IsControl) || rejectPathCharacters && trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            Error(issues, field, "TEXT_CHARACTER_INVALID", $"{label}包含不允许的字符");
    }

    private static bool IsValidPassword(string? password) =>
        password is { Length: >= 8 and <= 128 } && !password.Any(char.IsControl);

    private static string PortField(string purpose) => purpose switch
    {
        "游戏端口" => "gamePort",
        "Steam Query" => "queryPort",
        _ => "rconPort"
    };

    private static string? FindExistingAncestor(string path)
    {
        var current = new DirectoryInfo(path);
        while (current is not null && !current.Exists) current = current.Parent;
        return current?.FullName;
    }

    private static bool HasReparsePoint(string directory)
    {
        var current = new DirectoryInfo(directory);
        while (current.Parent is not null)
        {
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0) return true;
            current = current.Parent;
        }
        return false;
    }

    private static bool CanReadDirectory(string path)
    {
        try { _ = Directory.EnumerateFileSystemEntries(path).Take(1).ToArray(); return true; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return false; }
    }

    private static bool TryDirectoryHasEntries(string path, out bool hasEntries)
    {
        try
        {
            hasEntries = Directory.EnumerateFileSystemEntries(path).Any();
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            hasEntries = false;
            return false;
        }
    }

    private static bool CanWriteDirectory(string path)
    {
        var probe = Path.Combine(path, $".avorion-admin-setup-probe-{Guid.NewGuid():N}.tmp");
        try
        {
            using var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
            stream.WriteByte(0);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return false; }
        finally { try { if (File.Exists(probe)) File.Delete(probe); } catch { } }
    }

    private static void Error(List<ServerSetupValidationIssue> issues, string field, string code, string message) =>
        issues.Add(new ServerSetupValidationIssue(field, code, message, "error"));

    private static void Warning(List<ServerSetupValidationIssue> issues, string field, string code, string message) =>
        issues.Add(new ServerSetupValidationIssue(field, code, message, "warning"));
}
