using AvorionAdmin.Core.Abstractions;
using AvorionAdmin.Core.Models;

namespace AvorionAdmin.Agent;

public sealed class FileSystemBrowser : IFileSystemBrowser
{
    public Task<FileSystemBrowseResult> BrowseDirectoriesAsync(
        string? path = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(path))
        {
            var drives = DriveInfo.GetDrives()
                .Where(drive => drive.IsReady && drive.DriveType is DriveType.Fixed or DriveType.Removable)
                .Select(drive => new FileSystemDirectoryEntry(
                    drive.Name,
                    drive.RootDirectory.FullName,
                    true,
                    drive.AvailableFreeSpace,
                    drive.TotalSize))
                .OrderBy(drive => drive.Path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return Task.FromResult(new FileSystemBrowseResult(null, null, true, drives));
        }

        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException("目录必须是绝对路径", nameof(path));
        }

        var fullPath = Path.GetFullPath(path);
        var current = new DirectoryInfo(fullPath);
        if (!current.Exists)
        {
            throw new DirectoryNotFoundException($"目录不存在: {fullPath}");
        }

        var directories = current
            .EnumerateDirectories("*", SearchOption.TopDirectoryOnly)
            .Select(directory => new FileSystemDirectoryEntry(
                directory.Name,
                directory.FullName,
                false))
            .OrderBy(directory => directory.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        return Task.FromResult(new FileSystemBrowseResult(
            current.FullName,
            current.Parent?.FullName,
            false,
            directories));
    }
}
