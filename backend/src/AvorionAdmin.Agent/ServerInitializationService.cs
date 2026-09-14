using System.Security.Cryptography;
using System.Text.Json;
using AvorionAdmin.Core.Abstractions;
using AvorionAdmin.Core.Models;

namespace AvorionAdmin.Agent;

public sealed class ServerInitializationService(
    IServerSetupApplicationService applicationService,
    IManagedServerRuntime runtime) : IServerInitializationService
{
    private const int MaximumProfileBytes = 512 * 1024;

    public async Task<ServerInitializationResult> InitializeAsync(
        string operationId,
        ServerSetupPreflightRequest request,
        Func<double, string, CancellationToken, Task> reportProgressAsync,
        CancellationToken cancellationToken = default)
    {
        if (!request.GalaxyMode.Equals("new", StringComparison.Ordinal))
            throw new ServerInitializationException("NEW_GALAXY_REQUIRED", "首次初始化只适用于全新 Galaxy");
        if (!request.RconEnabled || string.IsNullOrWhiteSpace(request.RconPassword))
            throw new ServerInitializationException("RCON_REQUIRED", "首次初始化与健康检查必须启用 RCON 并提供本次请求密码");

        await reportProgressAsync(5, "reapplying-launch-profile", cancellationToken);
        var initialApplication = await applicationService.ApplyAsync(
            $"{operationId}-profile",
            request,
            (progress, _, token) => reportProgressAsync(Math.Clamp(progress * 0.15, 5, 15), "reapplying-launch-profile", token),
            cancellationToken);
        if (initialApplication.Mode != "launch-profile-ready")
            throw new ServerInitializationException("GALAXY_ALREADY_INITIALIZED", "Galaxy 已存在配置文件，无法重复执行首次初始化");

        var profile = ReadAndVerifyProfile(
            initialApplication.LaunchProfilePath,
            initialApplication.GalaxyDirectory,
            request,
            initialApplication.ConfigurationSha256);
        var initialization = await runtime.InitializeGalaxyAsync(profile, reportProgressAsync, cancellationToken);

        await reportProgressAsync(60, "writing-rcon-after-safe-stop", cancellationToken);
        var configuredApplication = await applicationService.ApplyAsync(
            $"{operationId}-rcon",
            request with { GalaxyMode = "existing" },
            (progress, _, token) => reportProgressAsync(Math.Clamp(60 + progress * 0.15, 60, 75), "writing-rcon-after-safe-stop", token),
            cancellationToken);
        if (!configuredApplication.ServerIniUpdated || !configuredApplication.RconConfigured)
            throw new ServerInitializationException("RCON_CONFIGURATION_NOT_CONFIRMED", "首次初始化后未能确认 RCON 已写入 server.ini");

        var health = await runtime.StartAndVerifyAsync(profile, request.RconPassword, reportProgressAsync, cancellationToken);
        return new ServerInitializationResult(
            "new-galaxy-running",
            health.ProcessId,
            profile.GalaxyDirectory,
            initialization.ServerIniPath,
            initialization.SaveCommandSent,
            initialization.StopCommandSent,
            configuredApplication.RconConfigured,
            health.RconAuthenticated,
            ["process-running", "server-ini-created", "console-save-sent", "console-stop-confirmed", "rcon-authenticated"],
            configuredApplication.DeferredActions.Where(action => action is not "first-galaxy-initialization" and not "rcon-until-first-initialization").ToArray(),
            health.StartedAt,
            DateTimeOffset.UtcNow);
    }

    private static ManagedLaunchProfile ReadAndVerifyProfile(
        string path,
        string expectedGalaxyDirectory,
        ServerSetupPreflightRequest request,
        string expectedSha256)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length is <= 0 or > MaximumProfileBytes)
            throw new ServerInitializationException("LAUNCH_PROFILE_INVALID", "受控启动档案不存在或大小无效");
        try
        {
            var bytes = File.ReadAllBytes(path);
            var actualSha256 = Convert.ToHexString(SHA256.HashData(bytes));
            if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(expectedSha256),
                    Convert.FromHexString(actualSha256)))
                throw new ServerInitializationException("LAUNCH_PROFILE_TAMPERED", "受控启动档案在配置应用后发生变化");
            var profile = JsonSerializer.Deserialize<ManagedLaunchProfile>(bytes, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? throw new JsonException("empty profile");
            if (!Path.GetFullPath(profile.GalaxyDirectory).Equals(Path.GetFullPath(expectedGalaxyDirectory), StringComparison.OrdinalIgnoreCase))
                throw new ServerInitializationException("LAUNCH_PROFILE_GALAXY_MISMATCH", "启动档案与本次 Galaxy 路径不一致");
            var expectedArguments = new[]
            {
                "--datapath", Directory.GetParent(Path.GetFullPath(request.GalaxyDirectory))?.FullName ?? string.Empty,
                "--galaxy-name", Path.GetFileName(Path.GetFullPath(request.GalaxyDirectory)),
                "--port", request.GamePort.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "--max-players", request.MaxPlayers.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "--server-name", request.ServerName.Trim()
            };
            if (!profile.Arguments.SequenceEqual(expectedArguments, StringComparer.Ordinal) ||
                profile.RconPort != request.RconPort || !profile.RconEnabled)
                throw new ServerInitializationException("LAUNCH_PROFILE_REQUEST_MISMATCH", "启动档案参数与本次初始化请求不一致");
            return profile;
        }
        catch (JsonException exception)
        {
            throw new ServerInitializationException("LAUNCH_PROFILE_INVALID", "受控启动档案无法解析", false, exception);
        }
    }
}
