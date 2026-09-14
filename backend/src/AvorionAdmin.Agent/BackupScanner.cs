using System.Security.Cryptography;
using System.Text;
using AvorionAdmin.Core.Abstractions;
using AvorionAdmin.Core.Models;

namespace AvorionAdmin.Agent;

public sealed class BackupScanner(IServerRuntimePathResolver pathResolver) : IBackupScanner
{
    public sealed record ResolvedBackupFile(BackupRecord Record, string Path);

    public Task<IReadOnlyList<BackupRecord>> ScanAsync(CancellationToken cancellationToken = default)
    {
        var source = pathResolver.ResolveBackupSource();
        if (source is null || !Directory.Exists(source.Path))
        {
            return Task.FromResult<IReadOnlyList<BackupRecord>>([]);
        }

        var canonicalRoot = Path.GetFullPath(source.Path);
        var records = new List<BackupRecord>();
        try
        {
            var enumeration = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint
            };
            foreach (var path in Directory.EnumerateFiles(canonicalRoot, "*.bak", enumeration).Take(5_000))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var canonicalPath = Path.GetFullPath(path);
                if (!canonicalPath.StartsWith(canonicalRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                    !canonicalPath.Equals(canonicalRoot, StringComparison.OrdinalIgnoreCase)) continue;
                if (source.Origin == "avorion-default" && !string.IsNullOrWhiteSpace(source.GalaxyName) &&
                    !Path.GetFileName(canonicalPath).StartsWith($"{source.GalaxyName}-", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    var file = new FileInfo(canonicalPath);
                    records.Add(new BackupRecord(
                        CreateId(canonicalPath),
                        file.Name,
                        new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero),
                        file.Length,
                        file.Length > 0 ? "available" : "incomplete"));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    records.Add(new BackupRecord(CreateId(canonicalPath), Path.GetFileName(canonicalPath), DateTimeOffset.MinValue, 0, "unreadable"));
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult<IReadOnlyList<BackupRecord>>([]);
        }

        return Task.FromResult<IReadOnlyList<BackupRecord>>(records
            .OrderByDescending(record => record.CreatedAt)
            .ToArray());
    }

    public async Task<ResolvedBackupFile?> ResolveAvailableAsync(string backupId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(backupId) || backupId.Length > 128) return null;
        var source = pathResolver.ResolveBackupSource();
        if (source is null || !Directory.Exists(source.Path)) return null;
        var canonicalRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source.Path));
        var records = await ScanAsync(cancellationToken);
        var record = records.SingleOrDefault(item => item.BackupId.Equals(backupId, StringComparison.Ordinal) && item.Status == "available");
        if (record is null) return null;
        foreach (var path in Directory.EnumerateFiles(canonicalRoot, "*.bak", new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        }).Take(5_000))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var canonicalPath = Path.GetFullPath(path);
            if (!canonicalPath.StartsWith(canonicalRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                !canonicalPath.Equals(canonicalRoot, StringComparison.OrdinalIgnoreCase)) continue;
            if (CreateId(canonicalPath).Equals(backupId, StringComparison.Ordinal) && new FileInfo(canonicalPath).Length > 0)
                return new ResolvedBackupFile(record, canonicalPath);
        }
        return null;
    }

    private static string CreateId(string path)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(path.ToUpperInvariant()));
        return $"backup_{Convert.ToHexString(hash.AsSpan(0, 12)).ToLowerInvariant()}";
    }
}
