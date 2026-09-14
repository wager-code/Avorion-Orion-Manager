using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AvorionAdmin.Core.Abstractions;
using AvorionAdmin.Core.Models;

namespace AvorionAdmin.Agent;

public sealed class SteamCmdInstallException(
    string code,
    string message,
    bool retryable = false,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string Code { get; } = code;
    public bool Retryable { get; } = retryable;
}

public interface ISteamCmdArchiveSource
{
    string SourceUrl { get; }
    Task<long> DownloadAsync(
        string destinationPath,
        Func<long, long?, CancellationToken, Task> reportBytesAsync,
        CancellationToken cancellationToken);
}

public interface IAuthenticodeVerifier
{
    bool TryVerifyValve(string executablePath, out string signer);
}

public sealed class OfficialSteamCmdArchiveSource : ISteamCmdArchiveSource
{
    public const string OfficialUrl = "https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip";
    private const long MaximumArchiveBytes = 20L * 1024 * 1024;
    public string SourceUrl => OfficialUrl;

    public async Task<long> DownloadAsync(
        string destinationPath,
        Func<long, long?, CancellationToken, Task> reportBytesAsync,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            return await DownloadCoreAsync(destinationPath, reportBytesAsync, timeout.Token);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SteamCmdInstallException("STEAMCMD_DOWNLOAD_TIMEOUT", "连接 Valve 官方 CDN 超时", true, exception);
        }
    }

    private static async Task<long> DownloadCoreAsync(
        string destinationPath,
        Func<long, long?, CancellationToken, Task> reportBytesAsync,
        CancellationToken cancellationToken)
    {
        using var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            ConnectTimeout = TimeSpan.FromSeconds(20),
            PooledConnectionLifetime = TimeSpan.FromMinutes(2)
        };
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using var request = new HttpRequestMessage(HttpMethod.Get, OfficialUrl);
        request.Headers.UserAgent.ParseAdd("AvorionAdmin/0.2 SteamCMD-Installer");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode != System.Net.HttpStatusCode.OK)
            throw new SteamCmdInstallException("STEAMCMD_DOWNLOAD_HTTP_ERROR", $"Valve CDN 返回 HTTP {(int)response.StatusCode}", true);

        var contentLength = response.Content.Headers.ContentLength;
        if (contentLength is > MaximumArchiveBytes)
            throw new SteamCmdInstallException("STEAMCMD_ARCHIVE_TOO_LARGE", "SteamCMD 归档超过安全大小限制");

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var buffer = new byte[64 * 1024];
        long total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            total += read;
            if (total > MaximumArchiveBytes)
                throw new SteamCmdInstallException("STEAMCMD_ARCHIVE_TOO_LARGE", "SteamCMD 归档超过安全大小限制");
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            await reportBytesAsync(total, contentLength, cancellationToken);
        }
        await destination.FlushAsync(cancellationToken);
        if (total < 16 * 1024)
            throw new SteamCmdInstallException("STEAMCMD_ARCHIVE_INVALID", "SteamCMD 归档大小异常");
        return total;
    }
}

public sealed class WindowsAuthenticodeVerifier : IAuthenticodeVerifier
{
    private static readonly Guid WinTrustActionGenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    public bool TryVerifyValve(string executablePath, out string signer)
    {
        signer = "未知";
        if (!OperatingSystem.IsWindows()) return false;

        var filePathPointer = Marshal.StringToCoTaskMemUni(executablePath);
        var fileInfoPointer = IntPtr.Zero;
        var trustDataPointer = IntPtr.Zero;
        try
        {
            var fileInfo = new WinTrustFileInfo
            {
                StructSize = (uint)Marshal.SizeOf<WinTrustFileInfo>(),
                FilePath = filePathPointer
            };
            fileInfoPointer = Marshal.AllocCoTaskMem(Marshal.SizeOf<WinTrustFileInfo>());
            Marshal.StructureToPtr(fileInfo, fileInfoPointer, false);

            var trustData = new WinTrustData
            {
                StructSize = (uint)Marshal.SizeOf<WinTrustData>(),
                UiChoice = 2,
                RevocationChecks = 0,
                UnionChoice = 1,
                FileInfo = fileInfoPointer,
                StateAction = 0,
                ProviderFlags = 0x00001000,
                UiContext = 0
            };
            trustDataPointer = Marshal.AllocCoTaskMem(Marshal.SizeOf<WinTrustData>());
            Marshal.StructureToPtr(trustData, trustDataPointer, false);

            if (WinVerifyTrust(IntPtr.Zero, WinTrustActionGenericVerifyV2, trustDataPointer) != 0) return false;
            using var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(executablePath));
            signer = certificate.GetNameInfo(X509NameType.SimpleName, false);
            var officialSigner = signer.Equals("Valve", StringComparison.OrdinalIgnoreCase) ||
                signer.Equals("Valve Corp.", StringComparison.OrdinalIgnoreCase);
            var officialOrganization = certificate.Subject.Contains("O=Valve,", StringComparison.OrdinalIgnoreCase) ||
                certificate.Subject.Contains("O=Valve Corp.,", StringComparison.OrdinalIgnoreCase);
            return officialSigner && officialOrganization;
        }
        catch (CryptographicException)
        {
            return false;
        }
        finally
        {
            if (trustDataPointer != IntPtr.Zero) Marshal.FreeCoTaskMem(trustDataPointer);
            if (fileInfoPointer != IntPtr.Zero) Marshal.FreeCoTaskMem(fileInfoPointer);
            Marshal.FreeCoTaskMem(filePathPointer);
        }
    }

    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = true)]
    private static extern uint WinVerifyTrust(IntPtr windowHandle, [MarshalAs(UnmanagedType.LPStruct)] Guid actionId, IntPtr trustData);

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustFileInfo
    {
        public uint StructSize;
        public IntPtr FilePath;
        public IntPtr FileHandle;
        public IntPtr KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustData
    {
        public uint StructSize;
        public IntPtr PolicyCallbackData;
        public IntPtr SipClientData;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public IntPtr FileInfo;
        public uint StateAction;
        public IntPtr StateData;
        public IntPtr UrlReference;
        public uint ProviderFlags;
        public uint UiContext;
    }
}

public sealed class SteamCmdInstaller(
    ISteamCmdArchiveSource archiveSource,
    IAuthenticodeVerifier authenticodeVerifier) : ISteamCmdInstaller
{
    private const long MinimumFreeBytes = 128L * 1024 * 1024;
    private const long MaximumExpandedBytes = 100L * 1024 * 1024;
    private const int MaximumEntries = 1000;

    public string PrepareTarget(string installDirectory)
    {
        if (!OperatingSystem.IsWindows())
            throw new SteamCmdInstallException("PLATFORM_NOT_SUPPORTED", "当前阶段只验证并支持 Windows SteamCMD 安装");
        if (string.IsNullOrWhiteSpace(installDirectory) || !Path.IsPathFullyQualified(installDirectory))
            throw new SteamCmdInstallException("INVALID_INSTALL_PATH", "SteamCMD 安装目录必须是 Windows 绝对路径");
        if (installDirectory.StartsWith("\\\\", StringComparison.Ordinal) || installDirectory.StartsWith("\\\\?\\", StringComparison.Ordinal))
            throw new SteamCmdInstallException("INVALID_INSTALL_PATH", "SteamCMD 安装目录不允许使用网络路径或设备路径");

        string target;
        try { target = Path.GetFullPath(installDirectory.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new SteamCmdInstallException("INVALID_INSTALL_PATH", "SteamCMD 安装目录格式无效", false, exception);
        }

        var root = Path.GetPathRoot(target)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(root) || target.Equals(root, StringComparison.OrdinalIgnoreCase))
            throw new SteamCmdInstallException("UNSAFE_INSTALL_PATH", "不能把磁盘根目录作为 SteamCMD 安装目录");
        var parent = Path.GetDirectoryName(target);
        if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
            throw new SteamCmdInstallException("INSTALL_PARENT_NOT_FOUND", "请选择已经存在的父目录；目标文件夹将由系统创建");
        if (File.Exists(target))
            throw new SteamCmdInstallException("INSTALL_TARGET_NOT_DIRECTORY", "SteamCMD 目标路径已被文件占用，请选择文件夹路径");

        var targetExists = Directory.Exists(target);
        EnsureNoReparsePoints(targetExists ? target : parent);
        var driveRoot = Path.GetPathRoot(target)!;
        var drive = new DriveInfo(driveRoot);
        if (!drive.IsReady || drive.DriveType is not (DriveType.Fixed or DriveType.Removable))
            throw new SteamCmdInstallException("INSTALL_DRIVE_UNAVAILABLE", "安装磁盘不可用或不受支持");
        if (drive.AvailableFreeSpace < MinimumFreeBytes)
            throw new SteamCmdInstallException("INSUFFICIENT_DISK_SPACE", "SteamCMD 安装磁盘至少需要 128 MB 可用空间");

        if (targetExists)
        {
            var existingExecutable = Path.Combine(target, "steamcmd.exe");
            if (File.Exists(existingExecutable))
            {
                ValidateExecutable(existingExecutable);
                if (!authenticodeVerifier.TryVerifyValve(existingExecutable, out _))
                    throw new SteamCmdInstallException("EXISTING_STEAMCMD_SIGNATURE_INVALID", "现有 steamcmd.exe 未通过 Valve 数字签名验证，不能复用");
            }
            else if (!IsDirectoryEmpty(target))
            {
                throw new SteamCmdInstallException(
                    "INSTALL_TARGET_NOT_STEAMCMD",
                    "所选目录已经包含其他文件，但未找到有效的 steamcmd.exe；请选择现有 SteamCMD 目录或新的空目录");
            }
        }

        ProbeWritable(targetExists ? target : parent);
        return target;
    }

    private static void ProbeWritable(string directory)
    {
        var probe = Path.Combine(directory, $".avorion-admin-write-probe-{Guid.NewGuid():N}.tmp");
        try
        {
            using var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
            stream.WriteByte(0);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new SteamCmdInstallException("INSTALL_PATH_NOT_WRITABLE", "Server Agent 无权写入所选父目录", false, exception);
        }
        catch (IOException exception)
        {
            throw new SteamCmdInstallException("INSTALL_PATH_NOT_WRITABLE", "无法在所选父目录创建安装文件", false, exception);
        }
        finally
        {
            try { if (File.Exists(probe)) File.Delete(probe); } catch { }
        }
    }

    public async Task<SteamCmdInstallationResult> InstallAsync(
        string operationId,
        string installDirectory,
        Func<double, string, CancellationToken, Task> reportProgressAsync,
        CancellationToken cancellationToken = default)
    {
        var target = PrepareTarget(installDirectory);
        var parent = Path.GetDirectoryName(target)!;
        var existingExecutable = Path.Combine(target, "steamcmd.exe");
        if (File.Exists(existingExecutable))
        {
            await reportProgressAsync(5, "validating-existing-installation", cancellationToken);
            ValidateExecutable(existingExecutable);
            if (!authenticodeVerifier.TryVerifyValve(existingExecutable, out var existingSigner))
                throw new SteamCmdInstallException("EXISTING_STEAMCMD_SIGNATURE_INVALID", "现有 steamcmd.exe 未通过 Valve 数字签名验证，不能复用");
            await reportProgressAsync(70, "hashing-existing-executable", cancellationToken);
            string executableSha256;
            await using (var executableStream = File.OpenRead(existingExecutable))
            {
                executableSha256 = Convert.ToHexString(await SHA256.HashDataAsync(executableStream, cancellationToken));
            }
            var verifiedAt = DateTimeOffset.UtcNow;
            await reportProgressAsync(100, "existing-installation-ready", cancellationToken);
            return new SteamCmdInstallationResult(
                target,
                existingExecutable,
                null,
                null,
                null,
                new FileInfo(existingExecutable).Length,
                existingSigner,
                null,
                true,
                executableSha256,
                verifiedAt);
        }

        var replaceExistingEmptyDirectory = Directory.Exists(target) && IsDirectoryEmpty(target);
        var safeOperationId = new string(operationId.Where(char.IsLetterOrDigit).ToArray());
        var staging = Path.Combine(parent, $".avorion-admin-steamcmd-{safeOperationId}.tmp");
        var archive = Path.Combine(parent, $".avorion-admin-steamcmd-{safeOperationId}.zip.download");
        var moved = false;
        try
        {
            if (Directory.Exists(staging) || File.Exists(staging) || File.Exists(archive))
                throw new SteamCmdInstallException("INSTALL_STAGING_CONFLICT", "安装暂存路径发生冲突，请重新提交操作");
            await reportProgressAsync(5, "validating-target", cancellationToken);

            var archiveBytes = await archiveSource.DownloadAsync(
                archive,
                async (received, total, token) =>
                {
                    var fraction = total is > 0 ? Math.Clamp((double)received / total.Value, 0, 1) : 0;
                    await reportProgressAsync(10 + 40 * fraction, "downloading-official-archive", token);
                },
                cancellationToken);
            await reportProgressAsync(55, "verifying-archive", cancellationToken);
            string archiveSha256;
            await using (var archiveStream = File.OpenRead(archive))
            {
                archiveSha256 = Convert.ToHexString(await SHA256.HashDataAsync(archiveStream, cancellationToken));
            }

            Directory.CreateDirectory(staging);
            await ExtractSafelyAsync(archive, staging, cancellationToken);
            var executable = Path.Combine(staging, "steamcmd.exe");
            ValidateExecutable(executable);
            await reportProgressAsync(82, "verifying-valve-signature", cancellationToken);
            if (!authenticodeVerifier.TryVerifyValve(executable, out var signer))
                throw new SteamCmdInstallException("STEAMCMD_SIGNATURE_INVALID", "steamcmd.exe 的 Valve 数字签名无效");

            if (File.Exists(target))
                throw new SteamCmdInstallException("INSTALL_TARGET_CHANGED", "安装期间目标路径被文件占用，已停止以避免覆盖");
            if (Directory.Exists(target))
            {
                if (!replaceExistingEmptyDirectory || !IsDirectoryEmpty(target))
                    throw new SteamCmdInstallException("INSTALL_TARGET_CHANGED", "安装期间目标目录内容发生变化，已停止以避免覆盖");
                try
                {
                    Directory.Delete(target, false);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    throw new SteamCmdInstallException("INSTALL_TARGET_CHANGED", "无法安全使用现有空目录，目录可能已发生变化", false, exception);
                }
            }
            Directory.Move(staging, target);
            moved = true;
            var installedExecutable = Path.Combine(target, "steamcmd.exe");
            string executableSha256;
            await using (var executableStream = File.OpenRead(installedExecutable))
            {
                executableSha256 = Convert.ToHexString(await SHA256.HashDataAsync(executableStream, cancellationToken));
            }
            var installedAt = DateTimeOffset.UtcNow;
            await reportProgressAsync(100, "completed", cancellationToken);
            return new SteamCmdInstallationResult(
                target,
                installedExecutable,
                archiveSource.SourceUrl,
                archiveSha256,
                archiveBytes,
                new FileInfo(installedExecutable).Length,
                signer,
                installedAt,
                false,
                executableSha256,
                installedAt);
        }
        catch (SteamCmdInstallException)
        {
            throw;
        }
        catch (HttpRequestException exception)
        {
            if (ContainsSecurityStatus(exception, 0x8009030E))
            {
                throw new SteamCmdInstallException(
                    "WINDOWS_TLS_CREDENTIALS_UNAVAILABLE",
                    "Windows 安全通道（Schannel）没有可用的 TLS 凭据，无法连接 Valve 官方 CDN。请检查 Windows Defender 防火墙及系统 TLS/网络服务后重试",
                    true,
                    exception);
            }

            throw new SteamCmdInstallException("STEAMCMD_DOWNLOAD_FAILED", "无法从 Valve 官方 CDN 下载 SteamCMD", true, exception);
        }
        catch (InvalidDataException exception)
        {
            throw new SteamCmdInstallException("STEAMCMD_ARCHIVE_INVALID", "SteamCMD 归档损坏或格式不受支持", true, exception);
        }
        finally
        {
            try { if (File.Exists(archive)) File.Delete(archive); } catch { }
            if (!moved)
            {
                try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch { }
            }
        }
    }

    private static bool ContainsSecurityStatus(Exception exception, uint expectedStatus)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (unchecked((uint)current.HResult) == expectedStatus)
                return true;

            if (current is System.ComponentModel.Win32Exception win32Exception &&
                unchecked((uint)win32Exception.NativeErrorCode) == expectedStatus)
                return true;
        }

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
            throw new SteamCmdInstallException("INSTALL_TARGET_NOT_READABLE", "Server Agent 无法读取所选目标目录", false, exception);
        }
    }

    private static async Task ExtractSafelyAsync(string archivePath, string staging, CancellationToken cancellationToken)
    {
        using var zip = ZipFile.OpenRead(archivePath);
        if (zip.Entries.Count is 0 or > MaximumEntries)
            throw new SteamCmdInstallException("STEAMCMD_ARCHIVE_INVALID", "SteamCMD 归档文件数量异常");
        long expandedBytes = 0;
        var stagingPrefix = Path.GetFullPath(staging) + Path.DirectorySeparatorChar;
        foreach (var entry in zip.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Path.IsPathFullyQualified(entry.FullName) || IsSymbolicLink(entry))
                throw new SteamCmdInstallException("STEAMCMD_ARCHIVE_UNSAFE", "SteamCMD 归档包含不安全路径");
            expandedBytes += entry.Length;
            if (expandedBytes > MaximumExpandedBytes)
                throw new SteamCmdInstallException("STEAMCMD_ARCHIVE_TOO_LARGE", "SteamCMD 解压内容超过安全大小限制");
            var destination = Path.GetFullPath(Path.Combine(staging, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
            if (!destination.StartsWith(stagingPrefix, StringComparison.OrdinalIgnoreCase))
                throw new SteamCmdInstallException("STEAMCMD_ARCHIVE_UNSAFE", "SteamCMD 归档包含路径穿越内容");
            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destination);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using var source = entry.Open();
            await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous);
            await source.CopyToAsync(output, cancellationToken);
        }
    }

    private static void ValidateExecutable(string executable)
    {
        if (!File.Exists(executable))
            throw new SteamCmdInstallException("STEAMCMD_EXECUTABLE_MISSING", "官方归档中未找到 steamcmd.exe");
        var info = new FileInfo(executable);
        if (info.Length < 64 * 1024)
            throw new SteamCmdInstallException("STEAMCMD_EXECUTABLE_INVALID", "steamcmd.exe 文件大小异常");
        using var stream = File.OpenRead(executable);
        if (stream.ReadByte() != 'M' || stream.ReadByte() != 'Z')
            throw new SteamCmdInstallException("STEAMCMD_EXECUTABLE_INVALID", "steamcmd.exe 不是有效的 Windows 可执行文件");
    }

    private static bool IsSymbolicLink(ZipArchiveEntry entry) =>
        ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000;

    private static void EnsureNoReparsePoints(string directory)
    {
        var current = new DirectoryInfo(directory);
        while (current.Parent is not null)
        {
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new SteamCmdInstallException("INSTALL_PATH_REPARSE_POINT", "安装父路径不能经过符号链接或目录联接");
            current = current.Parent;
        }
    }
}
