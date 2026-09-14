using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using AvorionAdmin.Core.Abstractions;
using AvorionAdmin.Core.Models;

namespace AvorionAdmin.Agent;

public sealed class AvorionServerInstallException(
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

public sealed record SteamCmdProcessResult(int ExitCode, string Output);

public interface IAvorionSteamCmdRunner
{
    Task<SteamCmdProcessResult> RunAsync(
        string steamCmdPath,
        string workingDirectory,
        string installDirectory,
        Func<double, string, CancellationToken, Task> reportProgressAsync,
        CancellationToken cancellationToken);
}

public sealed partial class AvorionSteamCmdRunner : IAvorionSteamCmdRunner
{
    public const int AppId = 565060;
    private const int MaximumCapturedCharacters = 128 * 1024;
    private static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(30);

    public async Task<SteamCmdProcessResult> RunAsync(
        string steamCmdPath,
        string workingDirectory,
        string installDirectory,
        Func<double, string, CancellationToken, Task> reportProgressAsync,
        CancellationToken cancellationToken)
    {
        var progressGate = new SemaphoreSlim(1, 1);
        async Task ReportProgressSerializedAsync(double progress, string step = "downloading-avorion-server")
        {
            await progressGate.WaitAsync(cancellationToken);
            try { await reportProgressAsync(progress, step, cancellationToken); }
            finally { progressGate.Release(); }
        }

        var firstAttempt = await RunSingleAttemptAsync(
            steamCmdPath,
            workingDirectory,
            installDirectory,
            progress => ReportProgressSerializedAsync(progress),
            cancellationToken);
        if (!ShouldRetryAfterSelfUpdate(firstAttempt.ExitCode, firstAttempt.Output))
            return firstAttempt;

        // A brand-new SteamCMD can replace/relaunch itself during its first command and return
        // "Missing configuration" even though the self-update completed. Retry this one known,
        // strongly-evidenced bootstrap case once in a fresh process; all other failures stay closed.
        await ReportProgressSerializedAsync(15, "restarting-steamcmd-after-self-update");
        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        var secondAttempt = await RunSingleAttemptAsync(
            steamCmdPath,
            workingDirectory,
            installDirectory,
            progress => ReportProgressSerializedAsync(progress),
            cancellationToken);
        return new SteamCmdProcessResult(
            secondAttempt.ExitCode,
            CombineAttemptOutput(firstAttempt.Output, secondAttempt.Output));
    }

    private static async Task<SteamCmdProcessResult> RunSingleAttemptAsync(
        string steamCmdPath,
        string workingDirectory,
        string installDirectory,
        Func<double, Task> reportProgressAsync,
        CancellationToken cancellationToken)
    {
        var startInfo = CreateStartInfo(steamCmdPath, workingDirectory, installDirectory);
        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var output = new StringBuilder();
        Task HandleLineAsync(string? line)
        {
            if (string.IsNullOrWhiteSpace(line)) return Task.CompletedTask;
            lock (output)
            {
                if (output.Length < MaximumCapturedCharacters)
                {
                    var remaining = MaximumCapturedCharacters - output.Length;
                    output.AppendLine(line.Length <= remaining ? line : line[..remaining]);
                }
            }
            var match = DownloadProgressRegex().Match(line);
            if (!match.Success || !double.TryParse(
                match.Groups[1].Value,
                System.Globalization.NumberStyles.AllowDecimalPoint,
                System.Globalization.CultureInfo.InvariantCulture,
                out var steamProgress)) return Task.CompletedTask;
            return reportProgressAsync(Math.Clamp(15 + steamProgress * 0.7, 15, 85));
        }

        try
        {
            if (!process.Start())
                throw new AvorionServerInstallException("STEAMCMD_START_FAILED", "无法启动已验证的 SteamCMD", true);

            var stdout = ConsumeAsync(process.StandardOutput, HandleLineAsync, cancellationToken);
            var stderr = ConsumeAsync(process.StandardError, HandleLineAsync, cancellationToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(InstallTimeout);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
                await Task.WhenAll(stdout, stderr);
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                TryKill(process);
                await AwaitReadersAsync(stdout, stderr);
                throw new AvorionServerInstallException("AVORION_INSTALL_TIMEOUT", "SteamCMD 安装 Avorion 服务端超过 30 分钟，任务已终止", true, exception);
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                await AwaitReadersAsync(stdout, stderr);
                throw;
            }

            return new SteamCmdProcessResult(process.ExitCode, output.ToString());
        }
        catch (AvorionServerInstallException)
        {
            throw;
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            throw new AvorionServerInstallException("STEAMCMD_START_FAILED", "Windows 无法启动已验证的 SteamCMD", true, exception);
        }
    }

    public static bool ShouldRetryAfterSelfUpdate(int exitCode, string output)
    {
        if (exitCode == 0 || string.IsNullOrWhiteSpace(output)) return false;
        return output.Contains("Missing configuration", StringComparison.OrdinalIgnoreCase) &&
            (output.Contains("Update complete, launching", StringComparison.OrdinalIgnoreCase) ||
             output.Contains("Update complete", StringComparison.OrdinalIgnoreCase));
    }

    private static string CombineAttemptOutput(string first, string second)
    {
        const string separator = "\n[SteamCMD bootstrap retry: attempt 2]\n";
        var combined = $"[SteamCMD bootstrap retry: attempt 1]\n{first}{separator}{second}";
        return combined.Length <= MaximumCapturedCharacters
            ? combined
            : combined[^MaximumCapturedCharacters..];
    }

    public static ProcessStartInfo CreateStartInfo(string steamCmdPath, string workingDirectory, string installDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = steamCmdPath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("+force_install_dir");
        startInfo.ArgumentList.Add(installDirectory);
        startInfo.ArgumentList.Add("+login");
        startInfo.ArgumentList.Add("anonymous");
        startInfo.ArgumentList.Add("+app_update");
        startInfo.ArgumentList.Add(AppId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("-beta");
        startInfo.ArgumentList.Add("public");
        startInfo.ArgumentList.Add("validate");
        startInfo.ArgumentList.Add("+quit");
        return startInfo;
    }

    private static async Task ConsumeAsync(
        StreamReader reader,
        Func<string?, Task> onLineAsync,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null) break;
            await onLineAsync(line);
        }
    }

    private static async Task AwaitReadersAsync(params Task[] readers)
    {
        try { await Task.WhenAll(readers).WaitAsync(TimeSpan.FromSeconds(5)); }
        catch { }
    }

    private static void TryKill(Process process)
    {
        try { if (!process.HasExited) process.Kill(true); }
        catch { }
    }

    [GeneratedRegex(@"progress:\s*([0-9]+(?:\.[0-9]+)?)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DownloadProgressRegex();
}

public sealed partial class AvorionServerInstaller(
    IAuthenticodeVerifier authenticodeVerifier,
    IAvorionSteamCmdRunner steamCmdRunner) : IAvorionServerInstaller
{
    private const long MinimumFreeBytes = 512L * 1024 * 1024;

    public AvorionServerInstallPlan Prepare(string steamCmdPath, string installDirectory)
    {
        if (!OperatingSystem.IsWindows())
            throw new AvorionServerInstallException("PLATFORM_NOT_SUPPORTED", "当前阶段只验证并支持 Windows Avorion 服务端安装");
        var executable = ValidateSteamCmd(steamCmdPath);
        var target = ValidateTarget(installDirectory);
        if (IsSameOrNestedPath(Path.GetDirectoryName(executable)!, target) || IsSameOrNestedPath(target, Path.GetDirectoryName(executable)!))
            throw new AvorionServerInstallException("INSTALL_PATH_OVERLAP", "SteamCMD 与 Avorion 服务端必须使用互不包含的独立目录");
        return new AvorionServerInstallPlan(executable, target);
    }

    public async Task<AvorionServerInstallationResult> InstallAsync(
        string operationId,
        string steamCmdPath,
        string installDirectory,
        Func<double, string, CancellationToken, Task> reportProgressAsync,
        CancellationToken cancellationToken = default)
    {
        var plan = Prepare(steamCmdPath, installDirectory);
        if (TryGetExistingServer(plan.InstallDirectory, out var existingExecutable, out var existingRunner))
        {
            await reportProgressAsync(5, "validating-existing-server", cancellationToken);
            if (!authenticodeVerifier.TryVerifyValve(plan.SteamCmdPath, out var existingSigner))
                throw new AvorionServerInstallException("STEAMCMD_SIGNATURE_INVALID", "steamcmd.exe 的 Valve 数字签名无效");
            await reportProgressAsync(70, "hashing-existing-server", cancellationToken);
            var executableSha256 = await ComputeSha256Async(existingExecutable, cancellationToken);
            var runnerSha256 = await ComputeSha256Async(existingRunner, cancellationToken);
            var verifiedAt = DateTimeOffset.UtcNow;
            await reportProgressAsync(100, "existing-server-ready", cancellationToken);
            return new AvorionServerInstallationResult(
                plan.InstallDirectory,
                existingExecutable,
                existingRunner,
                AvorionSteamCmdRunner.AppId,
                "public",
                existingSigner,
                new FileInfo(existingExecutable).Length,
                null,
                true,
                executableSha256,
                runnerSha256,
                verifiedAt);
        }

        var parent = Path.GetDirectoryName(plan.InstallDirectory)!;
        var replaceExistingEmptyDirectory = Directory.Exists(plan.InstallDirectory) && IsDirectoryEmpty(plan.InstallDirectory);
        var safeOperationId = new string(operationId.Where(char.IsLetterOrDigit).ToArray());
        var staging = Path.Combine(parent, $".avorion-admin-server-{safeOperationId}.tmp");
        var moved = false;
        try
        {
            if (Directory.Exists(staging) || File.Exists(staging))
                throw new AvorionServerInstallException("INSTALL_STAGING_CONFLICT", "服务端安装暂存路径发生冲突，请重新提交操作");
            await reportProgressAsync(5, "validating-installation", cancellationToken);

            if (!authenticodeVerifier.TryVerifyValve(plan.SteamCmdPath, out var signer))
                throw new AvorionServerInstallException("STEAMCMD_SIGNATURE_INVALID", "steamcmd.exe 的 Valve 数字签名无效");
            await reportProgressAsync(10, "starting-steamcmd", cancellationToken);

            var processResult = await steamCmdRunner.RunAsync(
                plan.SteamCmdPath,
                Path.GetDirectoryName(plan.SteamCmdPath)!,
                staging,
                reportProgressAsync,
                cancellationToken);
            await reportProgressAsync(88, "verifying-steam-result", cancellationToken);

            if (processResult.ExitCode != 0)
                throw new AvorionServerInstallException(
                    "STEAMCMD_APP_UPDATE_FAILED",
                    $"SteamCMD 退出码为 {processResult.ExitCode}，Avorion 服务端未安装",
                    true,
                    details: BuildFailureDetails("steamcmd", processResult.Output, processResult.ExitCode));
            if (!SuccessRegex().IsMatch(processResult.Output))
                throw new AvorionServerInstallException(
                    "STEAMCMD_SUCCESS_NOT_CONFIRMED",
                    "SteamCMD 未返回 App 565060 安装成功标记",
                    true,
                    details: BuildFailureDetails("steamcmd", processResult.Output));

            var executable = Path.Combine(staging, "bin", "AvorionServer.exe");
            var runner = Path.Combine(staging, "bin", "ServerRunner.exe");
            ValidateServerExecutable(executable);
            if (!File.Exists(runner))
                throw new AvorionServerInstallException("AVORION_RUNNER_MISSING", "安装结果中未找到 bin\\ServerRunner.exe", true);
            if (File.Exists(plan.InstallDirectory))
                throw new AvorionServerInstallException("INSTALL_TARGET_CHANGED", "安装期间目标路径被文件占用，已停止以避免覆盖");
            if (Directory.Exists(plan.InstallDirectory))
            {
                if (!replaceExistingEmptyDirectory || !IsDirectoryEmpty(plan.InstallDirectory))
                    throw new AvorionServerInstallException("INSTALL_TARGET_CHANGED", "安装期间服务端目标目录内容发生变化，已停止以避免覆盖");
                try
                {
                    Directory.Delete(plan.InstallDirectory, false);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    throw new AvorionServerInstallException("INSTALL_TARGET_CHANGED", "无法安全使用现有空目录，目录可能已发生变化", false, exception);
                }
            }

            await reportProgressAsync(94, "publishing-installation", cancellationToken);
            await PublishDirectoryAsync(staging, plan.InstallDirectory, processResult.Output, cancellationToken);
            moved = true;
            var installedExecutable = Path.Combine(plan.InstallDirectory, "bin", "AvorionServer.exe");
            var installedRunner = Path.Combine(plan.InstallDirectory, "bin", "ServerRunner.exe");
            var executableSha256 = await ComputeSha256Async(installedExecutable, cancellationToken);
            var runnerSha256 = await ComputeSha256Async(installedRunner, cancellationToken);
            var installedAt = DateTimeOffset.UtcNow;
            await reportProgressAsync(100, "completed", cancellationToken);
            return new AvorionServerInstallationResult(
                plan.InstallDirectory,
                installedExecutable,
                installedRunner,
                AvorionSteamCmdRunner.AppId,
                "public",
                signer,
                new FileInfo(installedExecutable).Length,
                installedAt,
                false,
                executableSha256,
                runnerSha256,
                installedAt);
        }
        finally
        {
            if (!moved)
            {
                await TryDeleteDirectoryAsync(staging);
            }
        }
    }

    private static async Task PublishDirectoryAsync(
        string staging,
        string target,
        string steamCmdOutput,
        CancellationToken cancellationToken)
    {
        var delays = new[]
        {
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(250),
            TimeSpan.FromMilliseconds(500),
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(4),
            TimeSpan.FromSeconds(8)
        };
        Exception? lastException = null;
        for (var attempt = 0; attempt < delays.Length; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (attempt > 0) await Task.Delay(delays[attempt], cancellationToken);
            if (Directory.Exists(target) || File.Exists(target))
                throw new AvorionServerInstallException("INSTALL_TARGET_EXISTS", "安装期间目标目录被创建，已停止以避免覆盖");
            try
            {
                Directory.Move(staging, target);
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                lastException = exception;
            }
        }

        throw new AvorionServerInstallException(
            "INSTALL_PUBLISH_ACCESS_DENIED",
            "服务端文件已经下载并校验，但 Windows 持续占用暂存目录，无法发布到目标路径；请关闭可能扫描该目录的程序后重试",
            true,
            lastException,
            BuildFailureDetails("publishing-installation", steamCmdOutput));
    }

    private static async Task TryDeleteDirectoryAsync(string directory)
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                if (!Directory.Exists(directory)) return;
                Directory.Delete(directory, true);
                return;
            }
            catch when (attempt < 3)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250 * (attempt + 1)));
            }
            catch
            {
                return;
            }
        }
    }

    private static IReadOnlyDictionary<string, object?> BuildFailureDetails(string stage, string output, int? exitCode = null)
    {
        var details = new Dictionary<string, object?>
        {
            ["stage"] = stage,
            ["logExcerpt"] = TailLog(output)
        };
        if (exitCode.HasValue) details["exitCode"] = exitCode.Value;
        return details;
    }

    private static string TailLog(string output)
    {
        const int maximumCharacters = 16 * 1024;
        var sanitized = new string(output
            .Where(character => character is '\r' or '\n' or '\t' || !char.IsControl(character))
            .ToArray())
            .Trim();
        return sanitized.Length <= maximumCharacters ? sanitized : sanitized[^maximumCharacters..];
    }

    private string ValidateSteamCmd(string steamCmdPath)
    {
        if (string.IsNullOrWhiteSpace(steamCmdPath) || !Path.IsPathFullyQualified(steamCmdPath))
            throw new AvorionServerInstallException("INVALID_STEAMCMD_PATH", "必须提供 steamcmd.exe 的 Windows 绝对路径");
        if (steamCmdPath.StartsWith("\\\\", StringComparison.Ordinal) || steamCmdPath.StartsWith("\\\\?\\", StringComparison.Ordinal))
            throw new AvorionServerInstallException("INVALID_STEAMCMD_PATH", "steamcmd.exe 不允许位于网络路径或设备路径");
        string executable;
        try { executable = Path.GetFullPath(steamCmdPath.Trim()); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new AvorionServerInstallException("INVALID_STEAMCMD_PATH", "steamcmd.exe 路径格式无效", false, exception);
        }
        if (!Path.GetFileName(executable).Equals("steamcmd.exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(executable))
            throw new AvorionServerInstallException("STEAMCMD_NOT_FOUND", "未找到 steamcmd.exe");
        EnsureNoReparsePoints(Path.GetDirectoryName(executable)!, "STEAMCMD_PATH_REPARSE_POINT", "SteamCMD 路径不能经过符号链接或目录联接");
        ValidatePortableExecutable(executable, 64 * 1024, "STEAMCMD_EXECUTABLE_INVALID", "steamcmd.exe 不是有效的 Windows 可执行文件");
        if (!authenticodeVerifier.TryVerifyValve(executable, out _))
            throw new AvorionServerInstallException("STEAMCMD_SIGNATURE_INVALID", "steamcmd.exe 的 Valve 数字签名无效");
        return executable;
    }

    private static string ValidateTarget(string installDirectory)
    {
        if (string.IsNullOrWhiteSpace(installDirectory) || !Path.IsPathFullyQualified(installDirectory))
            throw new AvorionServerInstallException("INVALID_INSTALL_PATH", "Avorion 服务端安装目录必须是 Windows 绝对路径");
        if (installDirectory.StartsWith("\\\\", StringComparison.Ordinal) || installDirectory.StartsWith("\\\\?\\", StringComparison.Ordinal))
            throw new AvorionServerInstallException("INVALID_INSTALL_PATH", "服务端安装目录不允许使用网络路径或设备路径");
        string target;
        try { target = Path.GetFullPath(installDirectory.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new AvorionServerInstallException("INVALID_INSTALL_PATH", "Avorion 服务端安装目录格式无效", false, exception);
        }
        var root = Path.GetPathRoot(target)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(root) || target.Equals(root, StringComparison.OrdinalIgnoreCase))
            throw new AvorionServerInstallException("UNSAFE_INSTALL_PATH", "不能把磁盘根目录作为 Avorion 服务端安装目录");
        var parent = Path.GetDirectoryName(target);
        if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
            throw new AvorionServerInstallException("INSTALL_PARENT_NOT_FOUND", "请选择已经存在的父目录；服务端目标文件夹将由系统创建");
        if (File.Exists(target))
            throw new AvorionServerInstallException("INSTALL_TARGET_NOT_DIRECTORY", "Avorion 服务端目标路径已被文件占用，请选择文件夹路径");
        var targetExists = Directory.Exists(target);
        EnsureNoReparsePoints(targetExists ? target : parent, "INSTALL_PATH_REPARSE_POINT", "服务端安装路径不能经过符号链接或目录联接");
        var drive = new DriveInfo(Path.GetPathRoot(target)!);
        if (!drive.IsReady || drive.DriveType is not (DriveType.Fixed or DriveType.Removable))
            throw new AvorionServerInstallException("INSTALL_DRIVE_UNAVAILABLE", "服务端安装磁盘不可用或不受支持");
        if (drive.AvailableFreeSpace < MinimumFreeBytes)
            throw new AvorionServerInstallException("INSUFFICIENT_DISK_SPACE", "Avorion 服务端安装磁盘至少需要 512 MB 可用空间");
        if (targetExists) TryGetExistingServer(target, out _, out _);
        ProbeWritable(targetExists ? target : parent);
        return target;
    }

    private static bool TryGetExistingServer(string target, out string executable, out string runner)
    {
        executable = Path.Combine(target, "bin", "AvorionServer.exe");
        runner = Path.Combine(target, "bin", "ServerRunner.exe");
        if (!Directory.Exists(target)) return false;
        var hasExecutable = File.Exists(executable);
        var hasRunner = File.Exists(runner);
        if (hasExecutable && hasRunner)
        {
            ValidateServerExecutable(executable);
            ValidatePortableExecutable(runner, 64 * 1024, "AVORION_RUNNER_INVALID", "bin\\ServerRunner.exe 不是有效的 Windows 可执行文件");
            return true;
        }
        if (hasExecutable || hasRunner)
            throw new AvorionServerInstallException("EXISTING_AVORION_INCOMPLETE", "现有目录中的 Avorion 服务端文件不完整，不能直接复用");
        if (!IsDirectoryEmpty(target))
            throw new AvorionServerInstallException("INSTALL_TARGET_NOT_AVORION_SERVER", "所选目录已经包含其他文件，但未找到完整的 Avorion 服务端；请选择现有服务端目录或新的空目录");
        return false;
    }

    private static bool IsDirectoryEmpty(string directory)
    {
        try
        {
            return !Directory.EnumerateFileSystemEntries(directory).Any();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new AvorionServerInstallException("INSTALL_TARGET_NOT_READABLE", "Server Agent 无法读取所选服务端目录", false, exception);
        }
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(stream, cancellationToken));
    }

    private static void ProbeWritable(string parent)
    {
        var probe = Path.Combine(parent, $".avorion-admin-write-probe-{Guid.NewGuid():N}.tmp");
        try
        {
            using var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
            stream.WriteByte(0);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            throw new AvorionServerInstallException("INSTALL_PATH_NOT_WRITABLE", "Server Agent 无权写入所选服务端父目录", false, exception);
        }
        finally
        {
            try { if (File.Exists(probe)) File.Delete(probe); } catch { }
        }
    }

    private static void ValidateServerExecutable(string executable)
    {
        if (!File.Exists(executable))
            throw new AvorionServerInstallException("AVORION_EXECUTABLE_MISSING", "安装结果中未找到 bin\\AvorionServer.exe", true);
        ValidatePortableExecutable(executable, 64 * 1024, "AVORION_EXECUTABLE_INVALID", "bin\\AvorionServer.exe 不是有效的 Windows 可执行文件");
    }

    private static void ValidatePortableExecutable(string path, long minimumBytes, string code, string message)
    {
        var info = new FileInfo(path);
        if (info.Length < minimumBytes) throw new AvorionServerInstallException(code, message);
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.ReadByte() != 'M' || stream.ReadByte() != 'Z')
                throw new AvorionServerInstallException(code, message);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new AvorionServerInstallException(code, message, false, exception);
        }
    }

    private static bool IsSameOrNestedPath(string parent, string child)
    {
        var parentPath = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var childPath = Path.GetFullPath(child).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return childPath.StartsWith(parentPath, StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureNoReparsePoints(string directory, string code, string message)
    {
        var current = new DirectoryInfo(directory);
        while (current.Parent is not null)
        {
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new AvorionServerInstallException(code, message);
            current = current.Parent;
        }
    }

    [GeneratedRegex(@"Success!\s+App\s+'565060'\s+fully installed\.", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SuccessRegex();
}
