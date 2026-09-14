namespace AvorionAdmin.Core.Models;

public sealed record AllianceRewardGrant(
    long Credits,
    AllianceResourceBalances Resources);

public sealed record AllianceRewardRequest(
    AllianceRewardGrant? Grant,
    string? Confirmation);

public sealed record AllianceRewardResult(
    int AllianceIndex,
    string AllianceName,
    bool Online,
    AllianceRewardGrant Grant,
    AllianceAssetsSnapshot Before,
    AllianceAssetsSnapshot After,
    string Outcome,
    DateTimeOffset VerifiedAt,
    string Source,
    string ComponentVersion);

public sealed record AllianceRewardValidationError(string Code, string Message);

public static class AllianceRewardPolicy
{
    public const long MaximumCreditsPerGrant = 1_000_000_000;
    public const long MaximumResourcePerGrant = 100_000_000;

    public static AllianceRewardValidationError? Validate(AllianceRewardRequest request)
    {
        if (request.Grant is null)
            return new("ALLIANCE_REWARD_REQUIRED", "必须指定要发放的 Credits 或矿物");
        if (request.Confirmation is null || request.Confirmation.Length > 64)
            return new("DANGER_CONFIRMATION_REQUIRED", "联盟奖励确认文本无效");

        var grant = request.Grant;
        if (grant.Credits is < 0 or > MaximumCreditsPerGrant)
            return new("ALLIANCE_REWARD_LIMIT", $"单次 Credits 必须在 0 到 {MaximumCreditsPerGrant} 之间");
        if (grant.Resources is null)
            return new("ALLIANCE_REWARD_REQUIRED", "必须提供七种矿物的发放数量");
        var amounts = new[]
        {
            grant.Resources.Iron, grant.Resources.Titanium, grant.Resources.Naonite, grant.Resources.Trinium,
            grant.Resources.Xanion, grant.Resources.Ogonite, grant.Resources.Avorion
        };
        if (amounts.Any(amount => amount is < 0 or > MaximumResourcePerGrant))
            return new("ALLIANCE_REWARD_LIMIT", $"单种矿物单次必须在 0 到 {MaximumResourcePerGrant} 之间");
        if (grant.Credits == 0 && amounts.All(amount => amount == 0))
            return new("ALLIANCE_REWARD_EMPTY", "至少需要发放 1 Credits 或 1 单位矿物");
        return null;
    }
}
