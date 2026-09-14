using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AvorionAdmin.Core.Abstractions;
using AvorionAdmin.Core.Models;

namespace AvorionAdmin.Agent;

public sealed class UpdateInspectionException(
    string code,
    string message,
    bool retryable = false,
    Exception? innerException = null,
    IReadOnlyDictionary<string, object?>? details = null) : Exception(message, innerException)
{
    public string Code { get; } = code;
    public bool Retryable { get; } = retryable;
    public IReadOnlyDictionary<string, object?>? Details { get; } = details;
}

public sealed partial class UpdateInspectionService(
    IUpdateEnvironmentStore environmentStore,
    IUpdateEnvironmentService environmentService,
    IAuthenticodeVerifier authenticodeVerifier) : IUpdateInspectionService
{
    private const int AppId = 565060;
    private const int MaximumCapturedCharacters = 256 * 1024;
    private const long MinimumExecutableBytes = 64 * 1024;
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromMinutes(3);

    public async Task<UpdateCheckResult> CheckAsync(
        Func<double, string, CancellationToken, Task> reportProgressAsync,
        CancellationToken cancellationToken = default)
    {
        var (configuration, validation) = await RequireEnvironmentAsync(cancellationToken);
        await reportProgressAsync(8, "validating-update-environment", cancellationToken);
        if (!authenticodeVerifier.TryVerifyValve(configuration.SteamCmdPath, out var signer))
            throw new UpdateInspectionException("STEAMCMD_SIGNATURE_INVALID", "steamcmd.exe 的 Valve 数字签名无效，已停止更新检查");

        var manifestPath = FindManifest(configuration);
        var currentBuildId = manifestPath is null ? null : ReadManifestBuildId(manifestPath);
        await reportProgressAsync(20, "reading-installed-build", cancellationToken);

        var output = await QueryLatestBuildAsync(configuration.SteamCmdPath, reportProgressAsync, cancellationToken);
        var latestBuildId = ParseLatestPublicBuildId(output);
        if (latestBuildId is null)
            throw new UpdateInspectionException(
                "STEAM_BUILD_ID_NOT_FOUND",
                "SteamCMD 已完成查询，但返回内容中没有 App 565060 public 分支的 Build ID",
                true,
                details: FailureDetails(output));

        await reportProgressAsync(100, "update-check-completed", cancellationToken);
        var evidence = new List<string>
        {
            $"SteamCMD Valve 签名：{signer}",
            $"官方 App：{AppId}",
            "更新分支：public",
            $"最新 Build ID：{latestBuildId}"
        };
        if (currentBuildId is not null) evidence.Add($"本机 Build ID：{currentBuildId}");
        else evidence.Add("本机 appmanifest_565060.acf 不可用，无法比较 Build ID");
        return new UpdateCheckResult(
            AppId,
            "public",
            configuration.SteamCmdPath,
            configuration.ServerDirectory,
            validation.Server.Version,
            currentBuildId,
            latestBuildId,
            currentBuildId is null ? null : !string.Equals(currentBuildId, latestBuildId, StringComparison.Ordinal),
            evidence,
            DateTimeOffset.UtcNow);
    }

    public async Task<UpdateVerificationResult> VerifyLocalAsync(
        Func<double, string, CancellationToken, Task> reportProgressAsync,
        CancellationToken cancellationToken = default)
    {
        var (configuration, validation) = await RequireEnvironmentAsync(cancellationToken);
        await reportProgressAsync(10, "validating-update-environment", cancellationToken);
        var executable = validation.Server.ResolvedExecutablePath!;
        var runner = Path.Combine(Path.GetDirectoryName(executable)!, "ServerRunner.exe");
        var required = new[] { executable, runner };
        var files = new List<VerifiedServerFile>();
        for (var index = 0; index < required.Length; index++)
        {
            var path = required[index];
            ValidateExecutable(path);
            await reportProgressAsync(25 + index * 30, $"hashing-{Path.GetFileNameWithoutExtension(path).ToLowerInvariant()}", cancellationToken);
            var info = new FileInfo(path);
            files.Add(new VerifiedServerFile(
                info.Name,
                info.FullName,
                info.Length,
                await ComputeSha256Async(path, cancellationToken),
                ReadVersion(path)));
        }

        var manifestPath = FindManifest(configuration);
        var buildId = manifestPath is null ? null : ReadManifestBuildId(manifestPath);
        await reportProgressAsync(100, "local-verification-completed", cancellationToken);
        var checks = new List<string>
        {
            "已重新验证保存的 SteamCMD 与服务端路径",
            "AvorionServer.exe 文件头、大小、读取权限和 SHA-256 已验证",
            "ServerRunner.exe 文件头、大小、读取权限和 SHA-256 已验证",
            "本次检查只读，未运行 app_update validate，未修复或覆盖文件"
        };
        if (buildId is not null) checks.Add($"已读取本机 App {AppId} Build ID：{buildId}");
        return new UpdateVerificationResult(true, AppId, "public", configuration.ServerDirectory, buildId, files, checks, DateTimeOffset.UtcNow);
    }

    public static string? ParseLatestPublicBuildId(string output)
    {
        if (string.IsNullOrWhiteSpace(output)) return null;
        var match = PublicBranchBuildRegex().Match(output);
        return match.Success ? match.Groups[1].Value : null;
    }

    public static string? ReadManifestBuildId(string path)
    {
        try
        {
            var match = ManifestBuildRegex().Match(File.ReadAllText(path));
            return match.Success ? match.Groups[1].Value : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return null; }
    }

    private async Task<(UpdateEnvironmentConfiguration Configuration, UpdateEnvironmentValidation Validation)> RequireEnvironmentAsync(CancellationToken cancellationToken)
    {
        var configuration = await environmentStore.GetAsync(cancellationToken);
        if (configuration is null)
            throw new UpdateInspectionException("UPDATE_ENVIRONMENT_NOT_CONFIGURED", "请先设置并验证 SteamCMD 与 Avorion 服务端路径");
        var validation = await environmentService.ValidateAsync(configuration.SteamCmdPath, configuration.ServerDirectory, cancellationToken);
        if (!validation.Valid)
            throw new UpdateInspectionException("UPDATE_ENVIRONMENT_INVALID", "已保存的 SteamCMD 或服务端路径当前不可用，请重新验证路径");
        return (configuration, validation);
    }

    private static async Task<string> QueryLatestBuildAsync(
        string steamCmdPath,
        Func<double, string, CancellationToken, Task> reportProgressAsync,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = steamCmdPath,
            WorkingDirectory = Path.GetDirectoryName(steamCmdPath)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("+login");
        startInfo.ArgumentList.Add("anonymous");
        startInfo.ArgumentList.Add("+app_info_update");
        startInfo.ArgumentList.Add("1");
        startInfo.ArgumentList.Add("+app_info_print");
        startInfo.ArgumentList.Add(AppId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("+quit");

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start()) throw new UpdateInspectionException("STEAMCMD_START_FAILED", "无法启动已验证的 SteamCMD", true);
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            throw new UpdateInspectionException("STEAMCMD_START_FAILED", "Windows 无法启动已验证的 SteamCMD", true, exception);
        }

        await reportProgressAsync(35, "querying-steam-public-branch", cancellationToken);
        var stdout = ReadBoundedAsync(process.StandardOutput, cancellationToken);
        var stderr = ReadBoundedAsync(process.StandardError, cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CheckTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            await DrainReadersAsync(stdout, stderr);
            throw new UpdateInspectionException("STEAMCMD_QUERY_TIMEOUT", "SteamCMD 查询更新超过 3 分钟，任务已终止", true, exception);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            await DrainReadersAsync(stdout, stderr);
            throw;
        }
        var output = (await stdout) + Environment.NewLine + (await stderr);
        if (process.ExitCode != 0)
            throw new UpdateInspectionException(
                "STEAMCMD_QUERY_FAILED",
                $"SteamCMD 查询退出码为 {process.ExitCode}",
                true,
                details: FailureDetails(output, process.ExitCode));
        await reportProgressAsync(85, "parsing-steam-build-id", cancellationToken);
        return output;
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var buffer = new char[4096];
        var result = new StringBuilder();
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0) break;
            if (result.Length < MaximumCapturedCharacters)
                result.Append(buffer, 0, Math.Min(read, MaximumCapturedCharacters - result.Length));
        }
        return result.ToString();
    }

    private static async Task DrainReadersAsync(Task<string> stdout, Task<string> stderr)
    {
        try { await Task.WhenAll(stdout, stderr); } catch { }
    }

    private static string? FindManifest(UpdateEnvironmentConfiguration configuration)
    {
        var steamDirectory = Path.GetDirectoryName(configuration.SteamCmdPath)!;
        var serverParent = Directory.GetParent(configuration.ServerDirectory)?.FullName;
        var candidates = new[]
        {
            Path.Combine(steamDirectory, "steamapps", $"appmanifest_{AppId}.acf"),
            Path.Combine(configuration.ServerDirectory, "steamapps", $"appmanifest_{AppId}.acf"),
            serverParent is null ? null : Path.Combine(serverParent, "steamapps", $"appmanifest_{AppId}.acf")
        };
        return candidates.FirstOrDefault(path => path is not null && File.Exists(path));
    }

    private static void ValidateExecutable(string path)
    {
        if (!File.Exists(path))
            throw new UpdateInspectionException("SERVER_FILE_MISSING", $"未找到必须的服务端文件：{Path.GetFileName(path)}");
        var info = new FileInfo(path);
        if (info.Length < MinimumExecutableBytes)
            throw new UpdateInspectionException("SERVER_FILE_INVALID", $"服务端文件大小异常：{info.Name}");
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.ReadByte() != 'M' || stream.ReadByte() != 'Z')
                throw new UpdateInspectionException("SERVER_FILE_INVALID", $"服务端文件不是有效的 Windows PE 文件：{info.Name}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new UpdateInspectionException("SERVER_FILE_UNREADABLE", $"无法读取服务端文件：{info.Name}", true, exception);
        }
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1024 * 1024, true);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
    }

    private static string? ReadVersion(string path)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            return string.IsNullOrWhiteSpace(info.FileVersion) ? info.ProductVersion : info.FileVersion;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return null; }
    }

    private static IReadOnlyDictionary<string, object?> FailureDetails(string output, int? exitCode = null) =>
        new Dictionary<string, object?>
        {
            ["source"] = "steamcmd",
            ["exitCode"] = exitCode,
            ["logExcerpt"] = output.Length <= 8_000 ? output : output[^8_000..]
        };

    private static void TryKill(Process process)
    {
        try { if (!process.HasExited) process.Kill(true); } catch { }
    }

    [GeneratedRegex("\\\"branches\\\"\\s*\\{.*?\\\"public\\\"\\s*\\{.*?\\\"buildid\\\"\\s*\\\"([0-9]+)\\\"", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PublicBranchBuildRegex();

    [GeneratedRegex("\\\"buildid\\\"\\s*\\\"([0-9]+)\\\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ManifestBuildRegex();
}
