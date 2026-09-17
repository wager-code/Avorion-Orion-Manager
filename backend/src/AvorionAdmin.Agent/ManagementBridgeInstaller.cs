using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AvorionAdmin.Core.Abstractions;
using AvorionAdmin.Core.Models;

namespace AvorionAdmin.Agent;

public sealed class ManagementBridgeInstallationException(
    string code,
    string message,
    bool retryable = false,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string Code { get; } = code;
    public bool Retryable { get; } = retryable;
}

public sealed class ManagementBridgeInstaller : IManagementBridgeInstaller
{
    private const int MaximumPackageFiles = 256;
    private const long MaximumPackageBytes = 16 * 1024 * 1024;
    private const long MaximumConfigBytes = 1024 * 1024;
    private readonly string _sourceDirectory;

    public ManagementBridgeInstaller()
        : this(Path.Combine(AppContext.BaseDirectory, "management-mod", "OrionAdminBridge")) { }

    public ManagementBridgeInstaller(string sourceDirectory)
    {
        _sourceDirectory = Path.GetFullPath(sourceDirectory);
    }

    public Task<ManagementBridgeStatus> InspectAsync(
        string? galaxyDirectory,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Inspect(galaxyDirectory, cancellationToken));
    }

    public Task<ManagementBridgeInstallationResult> InstallAsync(
        string galaxyDirectory,
        string operationId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var galaxy = ValidateGalaxyDirectory(galaxyDirectory);
        var package = ReadManifest(_sourceDirectory, cancellationToken);
        var version = ReadVersion(_sourceDirectory)
            ?? throw new ManagementBridgeInstallationException("MANAGEMENT_BRIDGE_PACKAGE_INVALID", "随包 OrionAdminBridge 缺少有效版本信息");
        var modsDirectory = Path.Combine(galaxy, "mods");
        Directory.CreateDirectory(modsDirectory);
        RejectReparsePoint(modsDirectory, "Galaxy Mods 目录");
        var target = Path.Combine(modsDirectory, "OrionAdminBridge");
        if (Directory.Exists(target)) RejectReparsePoint(target, "已安装 OrionAdminBridge 目录");

        var safeId = new string(operationId.Where(char.IsLetterOrDigit).Take(40).ToArray());
        if (safeId.Length == 0) safeId = Guid.NewGuid().ToString("N");
        var staging = Path.Combine(modsDirectory, $".orionadmin-bridge-{safeId}-{Guid.NewGuid():N}.tmp");
        var backupRoot = Path.Combine(galaxy, ".orionadmin-backups", "management-bridge",
            $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");
        string? previousBackup = null;
        var published = false;
        var configurationCreated = false;
        var configPath = Path.Combine(galaxy, "modconfig.lua");

        try
        {
            CopyPackage(_sourceDirectory, staging, cancellationToken);
            var staged = ReadManifest(staging, cancellationToken);
            if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(package.Sha256), Convert.FromHexString(staged.Sha256)))
                throw new IOException("staged management bridge manifest mismatch");

            if (Directory.Exists(target))
            {
                Directory.CreateDirectory(backupRoot);
                previousBackup = Path.Combine(backupRoot, "previous-OrionAdminBridge");
                Directory.Move(target, previousBackup);
            }
            Directory.Move(staging, target);
            published = true;

            if (!File.Exists(configPath))
            {
                Directory.CreateDirectory(backupRoot);
                var normalized = target.Replace('\\', '/').Replace(""", "\\"", StringComparison.Ordinal);
                var content = $"scriptCachingEnabled = true{Environment.NewLine}modLocation = \"\"{Environment.NewLine}forceEnabling = false{Environment.NewLine}{Environment.NewLine}mods ={Environment.NewLine}{{{Environment.NewLine}    {{path = \"{normalized}\"}}{Environment.NewLine}}}{Environment.NewLine}{Environment.NewLine}allowed ={Environment.NewLine}{{{Environment.NewLine}}}{Environment.NewLine}";
                WriteAtomic(configPath, Encoding.UTF8.GetBytes(content), safeId);
                configurationCreated = true;
            }

            var status = Inspect(galaxy, cancellationToken);
            if (!status.Current)
                throw new IOException("published management bridge did not match package manifest");
            return Task.FromResult(new ManagementBridgeInstallationResult(
                version,
                target,
                package.Sha256,
                previousBackup is null ? null : backupRoot,
                configurationCreated,
                status.Configured,
                true,
                DateTimeOffset.UtcNow));
        }
        catch (ManagementBridgeInstallationException) { throw; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            try
            {
                Directory.CreateDirectory(backupRoot);
                if (published && Directory.Exists(target))
                    Directory.Move(target, Path.Combine(backupRoot, "failed-OrionAdminBridge"));
                if (previousBackup is not null && Directory.Exists(previousBackup))
                    Directory.Move(previousBackup, target);
                if (configurationCreated && File.Exists(configPath))
                    File.Move(configPath, Path.Combine(backupRoot, "failed-generated-modconfig.lua"));
            }
            catch (Exception rollbackException)
            {
                throw new ManagementBridgeInstallationException(
                    "MANAGEMENT_BRIDGE_ROLLBACK_FAILED",
                    $"管理组件安装失败且回滚未完成；备份目录：{backupRoot}",
                    false,
                    new AggregateException(exception, rollbackException));
            }
            throw new ManagementBridgeInstallationException(
                "MANAGEMENT_BRIDGE_INSTALL_FAILED",
                $"管理组件安装失败，原组件已保留或恢复；诊断目录：{backupRoot}",
                true,
                exception);
        }
    }

    private ManagementBridgeStatus Inspect(string? galaxyDirectory, CancellationToken cancellationToken)
    {
        var issues = new List<string>();
        PackageManifest? package = null;
        string? packageVersion = null;
        try
        {
            package = ReadManifest(_sourceDirectory, cancellationToken);
            packageVersion = ReadVersion(_sourceDirectory);
            if (packageVersion is null) issues.Add("随包组件的 modinfo.lua 版本无效");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            issues.Add($"随包组件不可用：{exception.Message}");
        }

        if (string.IsNullOrWhiteSpace(galaxyDirectory))
            return new ManagementBridgeStatus(package is not null && packageVersion is not null, packageVersion, false, null, false, false, null, "galaxy-unconfigured", issues, DateTimeOffset.UtcNow);

        string galaxy;
        try { galaxy = Path.GetFullPath(galaxyDirectory.Trim()); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            issues.Add("Galaxy 路径无效");
            return new ManagementBridgeStatus(package is not null && packageVersion is not null, packageVersion, false, null, false, false, null, "galaxy-invalid", issues, DateTimeOffset.UtcNow);
        }

        var target = Path.Combine(galaxy, "mods", "OrionAdminBridge");
        var installed = Directory.Exists(target);
        string? installedVersion = null;
        string? installedSha = null;
        if (installed)
        {
            try
            {
                RejectReparsePoint(target, "已安装 OrionAdminBridge 目录");
                installedVersion = ReadVersion(target);
                installedSha = ReadManifest(target, cancellationToken).Sha256;
                if (installedVersion is null) issues.Add("已安装组件版本无法识别");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                issues.Add($"已安装组件无法验证：{exception.Message}");
            }
        }

        var configured = IsConfigured(Path.Combine(galaxy, "modconfig.lua"), issues);
        var current = package is not null && installedSha is not null &&
            package.Sha256.Equals(installedSha, StringComparison.OrdinalIgnoreCase);
        var state = !installed ? "missing" : !current ? "outdated-or-modified" : !configured ? "installed-not-enabled" : "current";
        return new ManagementBridgeStatus(package is not null && packageVersion is not null, packageVersion, installed, installedVersion, current, configured, target, state, issues, DateTimeOffset.UtcNow);
    }

    private static string ValidateGalaxyDirectory(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ManagementBridgeInstallationException("GALAXY_DIRECTORY_REQUIRED", "尚未配置 Galaxy 目录");
        var path = Path.GetFullPath(value.Trim());
        if (!Directory.Exists(path))
            throw new ManagementBridgeInstallationException("GALAXY_DIRECTORY_NOT_FOUND", "Galaxy 目录不存在，首次初始化完成后才能安装管理组件");
        RejectReparsePoint(path, "Galaxy 目录");
        return path;
    }

    private static void CopyPackage(string source, string destination, CancellationToken cancellationToken)
    {
        if (Directory.Exists(destination))
            throw new IOException("management bridge staging directory already exists");
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            RejectReparsePoint(directory, "随包组件目录");
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = new FileInfo(file);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("随包组件包含重解析点");
            var output = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            File.Copy(file, output, false);
        }
    }

    private static PackageManifest ReadManifest(string directory, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException(directory);
        RejectReparsePoint(directory, "OrionAdminBridge 目录");
        var files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .OrderBy(path => Path.GetRelativePath(directory, path), StringComparer.Ordinal)
            .ToArray();
        if (files.Length is 0 or > MaximumPackageFiles) throw new InvalidDataException("OrionAdminBridge 文件数量无效");
        long total = 0;
        using var aggregate = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = new FileInfo(file);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("OrionAdminBridge 包含重解析点");
            total += info.Length;
            if (total > MaximumPackageBytes) throw new InvalidDataException("OrionAdminBridge 包超过安全大小限制");
            using var stream = File.OpenRead(file);
            var hash = Convert.ToHexString(SHA256.HashData(stream));
            var relative = Path.GetRelativePath(directory, file).Replace('\\', '/');
            aggregate.AppendData(Encoding.UTF8.GetBytes($"{relative}\n{info.Length}\n{hash}\n"));
        }
        return new PackageManifest(Convert.ToHexString(aggregate.GetHashAndReset()), files.Length, total);
    }

    private static string? ReadVersion(string directory)
    {
        var path = Path.Combine(directory, "modinfo.lua");
        if (!File.Exists(path) || new FileInfo(path).Length > 256 * 1024) return null;
        var match = Regex.Match(File.ReadAllText(path), "version\\s*=\\s*\"(?<value>[0-9]+(?:\\.[0-9]+){1,3})\"", RegexOptions.CultureInvariant);
        return match.Success ? match.Groups["value"].Value : null;
    }

    private static bool IsConfigured(string path, ICollection<string> issues)
    {
        if (!File.Exists(path))
        {
            issues.Add("modconfig.lua 不存在");
            return false;
        }
        var info = new FileInfo(path);
        if (info.Length > MaximumConfigBytes)
        {
            issues.Add("modconfig.lua 超过安全读取限制");
            return false;
        }
        try { return File.ReadAllText(path).Contains("OrionAdminBridge", StringComparison.OrdinalIgnoreCase); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            issues.Add($"modconfig.lua 无法读取：{exception.Message}");
            return false;
        }
    }

    private static void RejectReparsePoint(string path, string label)
    {
        var info = new DirectoryInfo(path);
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException($"{label}不能是重解析点");
    }

    private static void WriteAtomic(string path, byte[] content, string safeId)
    {
        var temp = Path.Combine(Path.GetDirectoryName(path)!, $".orionadmin-{safeId}-{Guid.NewGuid():N}.tmp");
        File.WriteAllBytes(temp, content);
        File.Move(temp, path);
    }

    private sealed record PackageManifest(string Sha256, int FileCount, long TotalBytes);
}
