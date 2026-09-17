namespace AvorionAdmin.Core.Models;

public sealed record UpdateStatus(
    bool SteamCmdAvailable,
    bool SteamCmdPathConfigured,
    string? CurrentVersion,
    string? LatestKnownVersion,
    bool? UpdateAvailable,
    DateTimeOffset? LastCheckedAt,
    Provenance Provenance);

public sealed record UpdateCheckResult(
    int AppId,
    string Branch,
    string SteamCmdPath,
    string ServerDirectory,
    string? CurrentVersion,
    string? CurrentBuildId,
    string LatestBuildId,
    bool? UpdateAvailable,
    IReadOnlyList<string> Evidence,
    DateTimeOffset CheckedAt);

public sealed record VerifiedServerFile(
    string Name,
    string Path,
    long SizeBytes,
    string Sha256,
    string? Version);

public sealed record UpdateVerificationResult(
    bool Valid,
    int AppId,
    string Branch,
    string ServerDirectory,
    string? InstalledBuildId,
    IReadOnlyList<VerifiedServerFile> Files,
    IReadOnlyList<string> Checks,
    DateTimeOffset VerifiedAt);

public sealed record UpdateRollbackPointResult(
    string PointId,
    string StoragePath,
    string ServerDirectory,
    string GalaxyDirectory,
    int ServerFileCount,
    int GalaxyFileCount,
    long TotalBytes,
    string ManifestSha256,
    string? InstalledBuildId,
    bool Verified,
    DateTimeOffset CreatedAt,
    DateTimeOffset VerifiedAt,
    IReadOnlyList<string> Evidence);

public sealed record UpdateRollbackPointAvailability(
    bool Available,
    UpdateRollbackPointResult? Point);

public sealed record UpdateComponentValidation(
    string RequestedPath,
    string? ResolvedExecutablePath,
    bool Exists,
    bool Readable,
    string? Version,
    IReadOnlyList<string> Checks,
    IReadOnlyList<string> Issues);

public sealed record UpdateEnvironmentValidation(
    bool Valid,
    UpdateComponentValidation SteamCmd,
    UpdateComponentValidation Server,
    long? ServerDriveAvailableBytes,
    DateTimeOffset ValidatedAt);

public sealed record UpdateEnvironmentDetection(
    string Status,
    UpdateComponentValidation? SteamCmd,
    UpdateComponentValidation? Server,
    DateTimeOffset ScannedAt);

public sealed record UpdateEnvironmentConfiguration(
    string SteamCmdPath,
    string ServerDirectory,
    DateTimeOffset ValidatedAt);

public sealed record UpdateEnvironmentRequest(
    string SteamCmdPath,
    string ServerDirectory);

public sealed record ServerSetupDraftRequest(
    string ServerName,
    string GalaxyName,
    string GalaxyMode,
    int MaxPlayers,
    string GalaxyDirectory,
    string ListenAddress,
    int GamePort,
    int QueryPort,
    bool RconEnabled,
    int RconPort,
    bool AllowFirewallChange,
    bool InstallManagementMod);

public sealed record ServerSetupPreflightRequest(
    string ServerName,
    string GalaxyName,
    string GalaxyMode,
    int MaxPlayers,
    string GalaxyDirectory,
    string ListenAddress,
    int GamePort,
    int QueryPort,
    bool RconEnabled,
    int RconPort,
    string? RconPassword,
    bool AllowFirewallChange,
    bool InstallManagementMod);

public sealed record ServerSetupDraftConfiguration(
    string ServerName,
    string GalaxyName,
    string GalaxyMode,
    int MaxPlayers,
    string GalaxyDirectory,
    string ListenAddress,
    int GamePort,
    int QueryPort,
    bool RconEnabled,
    int RconPort,
    bool AllowFirewallChange,
    bool InstallManagementMod,
    DateTimeOffset UpdatedAt);

public sealed record ServerSetupValidationIssue(
    string Field,
    string Code,
    string Message,
    string Severity);

public sealed record PortAvailability(
    string Purpose,
    int Port,
    string Protocol,
    bool Available,
    string Evidence);

public sealed record ServerSetupPreflightResult(
    bool Valid,
    bool UpdateEnvironmentValid,
    bool GalaxyPathValid,
    bool RconPasswordAccepted,
    IReadOnlyList<PortAvailability> Ports,
    IReadOnlyList<ServerSetupValidationIssue> Issues,
    DateTimeOffset CheckedAt);

public sealed record ServerSetupApplicationResult(
    string Mode,
    string GalaxyDirectory,
    string LaunchProfilePath,
    string? ServerIniPath,
    bool ServerIniUpdated,
    bool RconConfigured,
    string ConfigurationSha256,
    IReadOnlyList<string> DeferredActions,
    DateTimeOffset AppliedAt,
    ManagementBridgeInstallationResult? ManagementBridge = null);

public sealed record SteamCmdInstallRequest(
    string InstallDirectory,
    string? PlannedServerDirectory = null);

public sealed record SteamCmdInstallationResult(
    string InstallDirectory,
    string ExecutablePath,
    string? SourceUrl,
    string? ArchiveSha256,
    long? ArchiveBytes,
    long ExecutableBytes,
    string Signer,
    DateTimeOffset? InstalledAt,
    bool ReusedExisting,
    string ExecutableSha256,
    DateTimeOffset VerifiedAt);

public sealed record AvorionServerInstallRequest(
    string SteamCmdPath,
    string InstallDirectory);

public sealed record AvorionServerInstallPlan(
    string SteamCmdPath,
    string InstallDirectory);

public sealed record AvorionServerInstallationResult(
    string InstallDirectory,
    string ExecutablePath,
    string RunnerPath,
    int AppId,
    string Branch,
    string SteamCmdSigner,
    long ExecutableBytes,
    DateTimeOffset? InstalledAt,
    bool ReusedExisting,
    string ExecutableSha256,
    string RunnerSha256,
    DateTimeOffset VerifiedAt);

public sealed record ManagementBridgeStatus(
    bool PackageAvailable,
    string? PackageVersion,
    bool Installed,
    string? InstalledVersion,
    bool Current,
    bool Configured,
    string? TargetDirectory,
    string State,
    IReadOnlyList<string> Issues,
    DateTimeOffset CheckedAt);

public sealed record ManagementBridgeInstallationResult(
    string Version,
    string TargetDirectory,
    string ManifestSha256,
    string? BackupDirectory,
    bool ConfigurationCreated,
    bool Configured,
    bool RestartRequired,
    DateTimeOffset InstalledAt);
