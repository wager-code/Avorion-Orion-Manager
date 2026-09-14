namespace AvorionAdmin.Core.Configuration;

public sealed class ServerNodeOptions
{
    public const string SectionName = "Avorion";

    public string ServerId { get; set; } = "local";
    public string Name { get; set; } = "本机 Avorion 服务器";
    public string ProcessName { get; set; } = "AvorionServer";
    public int? ProcessId { get; set; }
    public string? ExecutablePath { get; set; }
    public string? GalaxyPath { get; set; }
    public string? SavePath { get; set; }
    public string? BackupPath { get; set; }
    public string? LogPath { get; set; }
    public string? SteamCmdPath { get; set; }
    public string? ClientDirectory { get; set; }
    public int? GamePort { get; set; }
    public int? QueryPort { get; set; }
    public string RconHost { get; set; } = "127.0.0.1";
    public int? RconPort { get; set; }
    public string DataDirectory { get; set; } = "data";
    public int PerformanceSampleSeconds { get; set; } = 5;
    public int HistoryRetentionDays { get; set; } = 7;
    public long DiskWarningAvailableBytes { get; set; } = 20L * 1024 * 1024 * 1024;
    public long MemoryWarningBytes { get; set; } = 14L * 1024 * 1024 * 1024;
}

public sealed class SecurityOptions
{
    public const string SectionName = "Security";
    public bool AllowRemote { get; set; }
    public int LocalSessionMinutes { get; set; } = 30;
}
