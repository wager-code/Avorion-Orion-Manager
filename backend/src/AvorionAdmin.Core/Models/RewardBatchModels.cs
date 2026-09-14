namespace AvorionAdmin.Core.Models;

public static class RewardDeliveryModes
{
    public const string Mail = "mail";
    public const string Direct = "direct";
}

public sealed record RewardMailMessage(string? Subject, string? Body);

public sealed record RewardBatchRequest(
    string? Delivery,
    IReadOnlyList<int>? PlayerIndexes,
    PlayerRewardGrant? Grant,
    RewardMailMessage? Mail,
    string? Confirmation);

public sealed record PlayerMailDeliveryResult(
    int PlayerIndex,
    string PlayerName,
    bool Online,
    string DeliveryId,
    string MailId,
    long MailIndex,
    bool Replayed,
    PlayerRewardGrant Grant,
    string Outcome,
    DateTimeOffset VerifiedAt,
    string Source,
    string ComponentVersion);

public sealed record RewardBatchTargetResult(
    string OperationId,
    int PlayerIndex,
    string? PlayerName,
    string Status,
    string? DeliveryId,
    string? Outcome,
    PlayerAssetsSnapshot? Before,
    PlayerAssetsSnapshot? After,
    string? MailId,
    long? MailIndex,
    string? ErrorCode,
    string? ErrorMessage);

public sealed record RewardBatchResult(
    string BatchOperationId,
    string Delivery,
    int TargetTotal,
    int Succeeded,
    int Failed,
    IReadOnlyList<RewardBatchTargetResult> Targets,
    string Outcome,
    DateTimeOffset CompletedAt);

public sealed record RewardBatchValidationError(string Code, string Message);

public static class RewardBatchPolicy
{
    public const int MaximumTargets = 50;
    public const int MaximumSubjectLength = 80;
    public const int MaximumBodyLength = 500;

    public static RewardBatchValidationError? Validate(RewardBatchRequest request)
    {
        if (request.Delivery is not (RewardDeliveryModes.Mail or RewardDeliveryModes.Direct))
            return new("REWARD_DELIVERY_INVALID", "发放方式必须是游戏内邮件或直接到账");
        if (request.PlayerIndexes is null || request.PlayerIndexes.Count is < 1 or > MaximumTargets)
            return new("REWARD_TARGETS_INVALID", $"每个批次必须选择 1 到 {MaximumTargets} 个玩家");
        if (request.PlayerIndexes.Any(index => index is < 1 or > 100000) ||
            request.PlayerIndexes.Distinct().Count() != request.PlayerIndexes.Count)
            return new("REWARD_TARGETS_INVALID", "玩家索引必须有效且不能重复");
        if (request.Grant is null)
            return new("REWARD_GRANT_REQUIRED", "批次必须包含 Credits 或矿物");
        var grantValidation = PlayerRewardPolicy.Validate(new PlayerRewardRequest(request.Grant, "GRANT PLAYER 1"));
        if (grantValidation is not null)
            return new(grantValidation.Code, grantValidation.Message);

        if (request.Delivery == RewardDeliveryModes.Mail)
        {
            if (request.Mail?.Subject is not { Length: >= 1 and <= MaximumSubjectLength } ||
                request.Mail.Body is not { Length: >= 1 and <= MaximumBodyLength })
                return new("REWARD_MAIL_INVALID",
                    $"邮件标题必须为 1–{MaximumSubjectLength} 字符，正文必须为 1–{MaximumBodyLength} 字符");
            if (ContainsUnsafeControl(request.Mail.Subject) || ContainsUnsafeControl(request.Mail.Body))
                return new("REWARD_MAIL_INVALID", "邮件标题或正文包含不支持的控制字符");
        }
        else if (request.Mail is not null)
            return new("REWARD_MAIL_NOT_ALLOWED", "直接到账批次不能携带邮件内容");

        if (request.Confirmation is null || request.Confirmation.Length > 64)
            return new("DANGER_CONFIRMATION_REQUIRED", "奖励批次确认文本无效");
        return null;
    }

    private static bool ContainsUnsafeControl(string value) =>
        value.Any(character => char.IsControl(character) && character is not ('\r' or '\n' or '\t'));
}
