using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AvorionAdmin.Core.Abstractions;
using AvorionAdmin.Core.Configuration;
using AvorionAdmin.Core.Models;
using Microsoft.Extensions.Options;

namespace AvorionAdmin.Agent;

public sealed class ServerSetupApplicationException(
    string code,
    string message,
    bool retryable = false,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string Code { get; } = code;
    public bool Retryable { get; } = retryable;
}

public sealed class ServerSetupApplicationService(
    IServerSetupPreflightService preflightService,
    IUpdateEnvironmentStore environmentStore,
    IUpdateEnvironmentService environmentService,
    IOptions<ServerNodeOptions> options) : IServerSetupApplicationService
{
    private const int MaximumManagedProfileBytes = 512 * 1024;
    private const int MaximumServerIniBytes = 2 * 1024 * 1024;
    private readonly ServerNodeOptions _options = options.Value;

    public async Task<ServerSetupApplicationResult> ApplyAsync(
        string operationId,
        ServerSetupPreflightRequest request,
        Func<double, string, CancellationToken, Task> reportProgressAsync,
        CancellationToken cancellationToken = default)
    {
        await reportProgressAsync(5, "revalidating-setup", cancellationToken);
        var preflight = await preflightService.ValidateAsync(request, cancellationToken);
        if (!preflight.Valid)
            throw new ServerSetupApplicationException(
                "SETUP_PREFLIGHT_FAILED",
                string.Join("；", preflight.Issues.Where(issue => issue.Severity == "error").Select(issue => issue.Message)));

        var environment = await environmentStore.GetAsync(cancellationToken)
            ?? throw new ServerSetupApplicationException("UPDATE_ENVIRONMENT_MISSING", "尚未保存 SteamCMD 与 Avorion 服务端路径");
        var validation = await environmentService.ValidateAsync(
            environment.SteamCmdPath,
            environment.ServerDirectory,
            cancellationToken);
        if (!validation.Valid || string.IsNullOrWhiteSpace(validation.Server.ResolvedExecutablePath))
            throw new ServerSetupApplicationException("UPDATE_ENVIRONMENT_INVALID", "已保存的 Avorion 服务端路径不再有效");

        var executablePath = Path.GetFullPath(validation.Server.ResolvedExecutablePath);
        EnsureServerStopped(executablePath);
        var workingDirectory = ResolveServerWorkingDirectory(executablePath);
        var galaxyDirectory = Path.GetFullPath(request.GalaxyDirectory.Trim())
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var galaxyParent = Directory.GetParent(galaxyDirectory)?.FullName
            ?? throw new ServerSetupApplicationException("GALAXY_PARENT_NOT_FOUND", "未找到 Galaxy 父目录");
        var galaxyFolderName = Path.GetFileName(galaxyDirectory);

        await reportProgressAsync(25, "building-launch-profile", cancellationToken);
        var arguments = new[]
        {
            "--datapath", galaxyParent,
            "--galaxy-name", galaxyFolderName,
            "--port", request.GamePort.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--max-players", request.MaxPlayers.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--server-name", request.ServerName.Trim()
        };
        var profile = new ManagedLaunchProfile(
            1,
            executablePath,
            workingDirectory,
            galaxyDirectory,
            request.GalaxyMode,
            arguments,
            request.ListenAddress,
            request.QueryPort,
            request.RconEnabled,
            "127.0.0.1",
            request.RconPort,
            request.AllowFirewallChange,
            false,
            DateTimeOffset.UtcNow);
        var profileBytes = JsonSerializer.SerializeToUtf8Bytes(profile, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        });
        var profileDirectory = Path.Combine(Path.GetFullPath(_options.DataDirectory), "managed");
        Directory.CreateDirectory(profileDirectory);
        var profilePath = Path.Combine(profileDirectory, "server-launch-profile.json");
        var serverIniPath = Path.Combine(galaxyDirectory, "server.ini");
        var iniExists = File.Exists(serverIniPath);
        var originalProfile = ReadOptionalSnapshot(profilePath, MaximumManagedProfileBytes);
        var originalIni = iniExists ? ReadRequiredSnapshot(serverIniPath, MaximumServerIniBytes) : null;

        try
        {
            await reportProgressAsync(45, "writing-launch-profile", cancellationToken);
            WriteAtomic(profilePath, profileBytes, operationId);
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(File.ReadAllBytes(profilePath)), SHA256.HashData(profileBytes)))
                throw new IOException("launch profile verification failed");

            if (iniExists)
            {
                await reportProgressAsync(65, "updating-server-ini", cancellationToken);
                var patchedIni = PatchServerIni(originalIni!, request);
                WriteAtomic(serverIniPath, patchedIni, operationId);
                ValidateServerIni(serverIniPath, request);
            }

            await reportProgressAsync(90, "verifying-applied-configuration", cancellationToken);
            var deferred = new List<string> { "game-listen-address", "steam-query-port" };
            if (!iniExists) deferred.AddRange(["first-galaxy-initialization", "rcon-until-first-initialization"]);
            if (request.AllowFirewallChange) deferred.Add("windows-firewall");
            var fingerprintInput = new byte[profileBytes.Length + (iniExists ? File.ReadAllBytes(serverIniPath).Length : 0)];
            Buffer.BlockCopy(profileBytes, 0, fingerprintInput, 0, profileBytes.Length);
            if (iniExists)
            {
                var iniBytes = File.ReadAllBytes(serverIniPath);
                Buffer.BlockCopy(iniBytes, 0, fingerprintInput, profileBytes.Length, iniBytes.Length);
            }
            return new ServerSetupApplicationResult(
                iniExists ? "server-ini-updated" : "launch-profile-ready",
                galaxyDirectory,
                profilePath,
                iniExists ? serverIniPath : null,
                iniExists,
                iniExists && request.RconEnabled,
                Convert.ToHexString(SHA256.HashData(fingerprintInput)),
                deferred,
                DateTimeOffset.UtcNow);
        }
        catch (ServerSetupApplicationException)
        {
            RestoreSnapshot(profilePath, originalProfile, operationId);
            if (iniExists) RestoreSnapshot(serverIniPath, originalIni, operationId);
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            RestoreSnapshot(profilePath, originalProfile, operationId);
            if (iniExists) RestoreSnapshot(serverIniPath, originalIni, operationId);
            throw new ServerSetupApplicationException(
                "SETUP_ATOMIC_WRITE_FAILED",
                "服务器配置写入失败，原配置已回滚",
                true,
                exception);
        }
    }

    public static string ResolveServerWorkingDirectory(string executablePath)
    {
        var executableDirectory = Path.GetDirectoryName(Path.GetFullPath(executablePath))
            ?? throw new ServerSetupApplicationException("SERVER_WORKING_DIRECTORY_INVALID", "无法确定 Avorion 服务端工作目录");
        if (Directory.Exists(Path.Combine(executableDirectory, "data"))) return executableDirectory;

        var parent = Directory.GetParent(executableDirectory)?.FullName;
        if (parent is not null && Directory.Exists(Path.Combine(parent, "data"))) return parent;

        // Existing-path validation remains responsible for rejecting incomplete installations.
        // Keep the executable directory as a conservative fallback for legacy layouts.
        return executableDirectory;
    }

    private static byte[] PatchServerIni(byte[] original, ServerSetupPreflightRequest request)
    {
        DecodedServerIni decoded;
        try { decoded = ServerIniEncoding.Decode(original); }
        catch (DecoderFallbackException exception)
        {
            throw new ServerSetupApplicationException("SERVER_INI_ENCODING_UNSUPPORTED", "server.ini 既不是有效 UTF-8，也不是当前 Windows ANSI 编码，未修改文件", false, exception);
        }
        var text = decoded.Text;
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var trailingNewline = text.EndsWith("\n", StringComparison.Ordinal);
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
        if (trailingNewline && lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        PatchSection(lines, "Networking", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["port"] = request.GamePort.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["rconIp"] = request.RconEnabled ? "127.0.0.1" : string.Empty,
            ["rconPassword"] = request.RconEnabled ? request.RconPassword! : string.Empty,
            ["rconPort"] = request.RconPort.ToString(System.Globalization.CultureInfo.InvariantCulture)
        });
        PatchSection(lines, "Administration", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["maxPlayers"] = request.MaxPlayers.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["name"] = request.ServerName.Trim()
        });
        var output = string.Join(newline, lines) + (trailingNewline ? newline : string.Empty);
        try { return ServerIniEncoding.Encode(decoded, output); }
        catch (EncoderFallbackException exception)
        {
            throw new ServerSetupApplicationException("SERVER_INI_ENCODING_UNSUPPORTED", "新的服务器配置无法用 server.ini 的原编码安全表示，未修改文件", false, exception);
        }
    }

    private static void PatchSection(List<string> lines, string section, IReadOnlyDictionary<string, string> values)
    {
        var sectionIndexes = lines.Select((line, index) => (line: line.Trim(), index))
            .Where(item => item.line.Equals($"[{section}]", StringComparison.OrdinalIgnoreCase))
            .Select(item => item.index)
            .ToArray();
        if (sectionIndexes.Length > 1)
            throw new ServerSetupApplicationException("SERVER_INI_AMBIGUOUS", $"server.ini 包含重复的 [{section}] 段，未修改文件");
        if (sectionIndexes.Length == 0)
        {
            if (lines.Count > 0 && lines[^1].Length > 0) lines.Add(string.Empty);
            lines.Add($"[{section}]");
            foreach (var pair in values) lines.Add($"{pair.Key}={pair.Value}");
            return;
        }

        var start = sectionIndexes[0] + 1;
        var end = lines.FindIndex(start, line => line.TrimStart().StartsWith("[", StringComparison.Ordinal));
        if (end < 0) end = lines.Count;
        foreach (var pair in values)
        {
            var matches = Enumerable.Range(start, end - start)
                .Where(index => KeyOf(lines[index]).Equals(pair.Key, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (matches.Length > 1)
                throw new ServerSetupApplicationException("SERVER_INI_AMBIGUOUS", $"server.ini 包含重复配置项 {pair.Key}，未修改文件");
            if (matches.Length == 1) lines[matches[0]] = $"{pair.Key}={pair.Value}";
            else
            {
                lines.Insert(end, $"{pair.Key}={pair.Value}");
                end++;
            }
        }
    }

    private static string KeyOf(string line)
    {
        var trimmed = line.TrimStart();
        if (trimmed.Length == 0 || trimmed[0] is ';' or '#') return string.Empty;
        var separator = trimmed.IndexOf('=');
        return separator <= 0 ? string.Empty : trimmed[..separator].Trim();
    }

    private static void ValidateServerIni(string path, ServerSetupPreflightRequest request)
    {
        var bytes = ReadRequiredSnapshot(path, MaximumServerIniBytes);
        var text = ServerIniEncoding.Decode(bytes).Text;
        var required = new[]
        {
            $"port={request.GamePort}",
            $"maxPlayers={request.MaxPlayers}",
            $"name={request.ServerName.Trim()}",
            $"rconPort={request.RconPort}",
            $"rconPassword={(request.RconEnabled ? request.RconPassword : string.Empty)}"
        };
        if (required.Any(value => !text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Any(line => line.Trim().Equals(value, StringComparison.Ordinal))))
            throw new IOException("server.ini verification failed");
    }

    private static void EnsureServerStopped(string executablePath)
    {
        var name = Path.GetFileNameWithoutExtension(executablePath);
        foreach (var process in Process.GetProcessesByName(name))
        {
            using (process)
            {
                try
                {
                    process.Refresh();
                    if (process.HasExited) continue;

                    var runningPath = process.MainModule?.FileName;
                    if (!string.IsNullOrWhiteSpace(runningPath) &&
                        Path.GetFullPath(runningPath).Equals(executablePath, StringComparison.OrdinalIgnoreCase))
                        throw new ServerSetupApplicationException("SERVER_RUNNING", "Avorion 服务端仍在运行，必须先安全停服才能修改配置");
                }
                catch (ServerSetupApplicationException) { throw; }
                catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
                {
                    throw new ServerSetupApplicationException("SERVER_PROCESS_OWNERSHIP_UNKNOWN", "发现同名 Avorion 进程但无法确认归属，已拒绝写入配置", false, exception);
                }
            }
        }
    }

    private static byte[]? ReadOptionalSnapshot(string path, int maximumBytes) =>
        File.Exists(path) ? ReadRequiredSnapshot(path, maximumBytes) : null;

    private static byte[] ReadRequiredSnapshot(string path, int maximumBytes)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("configuration file disappeared", path);
        if (info.Length > maximumBytes) throw new IOException("configuration file exceeds safety limit");
        return File.ReadAllBytes(path);
    }

    private static void WriteAtomic(string path, byte[] content, string operationId)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var safeId = new string(operationId.Where(char.IsLetterOrDigit).Take(48).ToArray());
        var temp = Path.Combine(directory, $".avorion-admin-{safeId}-{Guid.NewGuid():N}.tmp");
        var backup = Path.Combine(directory, $".avorion-admin-{safeId}-{Guid.NewGuid():N}.bak");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(content);
                stream.Flush(true);
            }
            if (File.Exists(path))
            {
                File.Replace(temp, path, backup, true);
                File.Delete(backup);
            }
            else File.Move(temp, path);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            try { if (File.Exists(backup)) File.Delete(backup); } catch { }
        }
    }

    private static void RestoreSnapshot(string path, byte[]? snapshot, string operationId)
    {
        try
        {
            if (snapshot is null)
            {
                if (File.Exists(path)) File.Delete(path);
            }
            else WriteAtomic(path, snapshot, $"rollback-{operationId}");
        }
        catch
        {
            throw new ServerSetupApplicationException("SETUP_ROLLBACK_FAILED", "配置写入失败且自动回滚未完成，请勿启动服务器并人工检查配置文件");
        }
    }

}
