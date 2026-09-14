using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AvorionAdmin.Core.Abstractions;
using AvorionAdmin.Core.Models;

namespace AvorionAdmin.Agent;

public sealed class LogReader(IServerRuntimePathResolver pathResolver) : ILogReader
{
    public Task<IReadOnlyList<LogEntry>> ReadRecentAsync(
        int limit,
        string? category = null,
        string? query = null,
        CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 500);
        var files = ResolveLogFiles();
        var entries = new List<LogEntry>();

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<TailLine> lines;
            try
            {
                var tailLimit = category?.Equals("Player", StringComparison.OrdinalIgnoreCase) == true
                    ? Math.Max(limit * 100, 10_000)
                    : Math.Max(limit * 4, 300);
                lines = ReadTail(file.FullName, tailLimit);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { continue; }

            foreach (var line in lines.Reverse())
            {
                var entry = Parse(file, line.Text, line.Number);
                if (!string.IsNullOrWhiteSpace(category) &&
                    !entry.Category.Equals(category, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.IsNullOrWhiteSpace(query) &&
                    !entry.RawLine.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
                entries.Add(entry);
            }
        }

        return Task.FromResult<IReadOnlyList<LogEntry>>(entries
            .OrderByDescending(entry => entry.OccurredAt)
            .ThenByDescending(entry => entry.SourceLastWriteAt)
            .ThenByDescending(entry => entry.SourceLineNumber)
            .Take(limit)
            .ToArray());
    }

    private IReadOnlyList<FileInfo> ResolveLogFiles()
    {
        var source = pathResolver.ResolveLogSource();
        if (source is null) return [];
        if (File.Exists(source.Path)) return [new FileInfo(source.Path)];
        if (!Directory.Exists(source.Path)) return [];

        try
        {
            return Directory.EnumerateFiles(source.Path, "*", SearchOption.TopDirectoryOnly)
                .Select(path => new FileInfo(path))
                .Where(file => file.Exists && IsSupportedLogFile(file, source.IsGalaxyRoot))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Take(10)
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static bool IsSupportedLogFile(FileInfo file, bool isGalaxyRoot)
    {
        if (isGalaxyRoot)
            return file.Name.StartsWith("serverlog", StringComparison.OrdinalIgnoreCase) &&
                   file.Extension.Equals(".txt", StringComparison.OrdinalIgnoreCase);

        return file.Extension.Equals(".log", StringComparison.OrdinalIgnoreCase) ||
               file.Extension.Equals(".txt", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<TailLine> ReadTail(string path, int limit)
    {
        var queue = new Queue<TailLine>(limit);
        long lineNumber = 0;
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            4096,
            FileOptions.SequentialScan);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        while (reader.ReadLine() is { } line)
        {
            if (queue.Count == limit) queue.Dequeue();
            queue.Enqueue(new TailLine(lineNumber, line));
            lineNumber++;
        }
        return queue.ToArray();
    }

    private static LogEntry Parse(FileInfo file, string line, long lineNumber)
    {
        var category = Categorize(line);
        var (occurredAt, confidence) = ParseTimestamp(line, file.LastWriteTimeUtc);
        var identity = $"{file.FullName}|{lineNumber}|{line}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        return new LogEntry(
            $"log_{Convert.ToHexString(hash.AsSpan(0, 12)).ToLowerInvariant()}",
            occurredAt,
            category,
            line,
            line,
            file.Name,
            lineNumber,
            new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero),
            confidence);
    }

    private static string Categorize(string line)
    {
        if (ContainsAny(line, "error", "exception", "错误", "failed", "fatal")) return "Error";
        if (ContainsAny(line, "warning", "warn", "警告")) return "Warning";
        if (ContainsAny(line, "rcon")) return "RCON";
        if (ContainsAny(line, "mod", "workshop")) return "MOD";
        if (ContainsAny(line, "save", "保存")) return "Save";
        if (ContainsAny(line, "player", "steamid", "玩家")) return "Player";
        return "Other";
    }

    private static bool ContainsAny(string value, params string[] candidates) =>
        candidates.Any(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));

    private static (DateTimeOffset At, double Confidence) ParseTimestamp(string line, DateTime fallbackUtc)
    {
        var candidates = new[]
        {
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-dd HH-mm-ss",
            "yyyy-MM-ddTHH:mm:ss",
            "yyyy-MM-dd HH:mm:ss.fff",
            "yyyy-MM-ddTHH:mm:ss.fff"
        };

        foreach (var length in new[] { 23, 19 })
        {
            if (line.Length < length) continue;
            var prefix = line[..length];
            if (DateTime.TryParseExact(prefix, candidates, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed))
            {
                return (new DateTimeOffset(parsed).ToUniversalTime(), 0.95);
            }
        }

        return (new DateTimeOffset(fallbackUtc, TimeSpan.Zero), 0.4);
    }

    private sealed record TailLine(long Number, string Text);
}
