namespace AvorionAdmin.Core.Models;

public static class InventoryOwnerKinds
{
    public const string Player = "player";
    public const string Alliance = "alliance";
}

public sealed record InventoryItemSummary(
    long Slot,
    int Amount,
    string ItemType,
    string Name,
    int? Rarity,
    string? Script,
    string? Seed,
    string? Title,
    string? Icon,
    string? WeaponType,
    string? WeaponCategory,
    string? TurretSlotType,
    int? Material,
    int? AverageTech,
    int? MaxTech,
    double? Dps,
    double? Damage,
    double? Reach,
    double? FireRate,
    double? Accuracy,
    int? TurretSlots,
    double? Size,
    int? NumWeapons,
    bool? Armed,
    bool? Civil,
    bool? Coaxial,
    bool? Seeker,
    bool? ContinuousBeam);

public sealed record InventorySnapshot(
    string OwnerKind,
    int OwnerIndex,
    string OwnerName,
    bool Online,
    int Total,
    int OccupiedSlots,
    int MaxSlots,
    int Offset,
    int Limit,
    IReadOnlyList<InventoryItemSummary> Items,
    DateTimeOffset SampledAt,
    string Freshness,
    string Source,
    string ComponentVersion);

public sealed record SystemUpgradeDefinition(string Key, string DisplayName, string Script);

public static class SystemUpgradeCatalog
{
    public static readonly IReadOnlyDictionary<string, SystemUpgradeDefinition> Items =
        new Dictionary<string, SystemUpgradeDefinition>(StringComparer.Ordinal)
        {
            ["battery-booster"] = new("battery-booster", "电池增容插件", "data/scripts/systems/batterybooster.lua"),
            ["cargo-extension"] = new("cargo-extension", "货舱扩展插件", "data/scripts/systems/cargoextension.lua"),
            ["energy-booster"] = new("energy-booster", "能量增幅插件", "data/scripts/systems/energybooster.lua"),
            ["engine-booster"] = new("engine-booster", "引擎增幅插件", "data/scripts/systems/enginebooster.lua"),
            ["hyperspace-booster"] = new("hyperspace-booster", "超空间增幅插件", "data/scripts/systems/hyperspacebooster.lua"),
            ["radar-booster"] = new("radar-booster", "雷达增幅插件", "data/scripts/systems/radarbooster.lua"),
            ["scanner-booster"] = new("scanner-booster", "扫描器增幅插件", "data/scripts/systems/scannerbooster.lua"),
            ["shield-booster"] = new("shield-booster", "护盾增幅插件", "data/scripts/systems/shieldbooster.lua"),
            ["mining-system"] = new("mining-system", "采矿系统插件", "data/scripts/systems/miningsystem.lua"),
            ["trading-system"] = new("trading-system", "贸易系统插件", "data/scripts/systems/tradingoverview.lua"),
            ["valuables-detector"] = new("valuables-detector", "贵重物探测插件", "data/scripts/systems/valuablesdetector.lua")
        };

    public static readonly IReadOnlyDictionary<string, int> Rarities =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["common"] = 0,
            ["uncommon"] = 1,
            ["rare"] = 2,
            ["exceptional"] = 3,
            ["exotic"] = 4,
            ["legendary"] = 5
        };
}

public sealed record SystemUpgradeGrantRequest(string? UpgradeKey, string? Rarity, string? Confirmation);

public sealed record SystemUpgradeGrantResult(
    string OwnerKind,
    int OwnerIndex,
    string OwnerName,
    bool Online,
    SystemUpgradeDefinition Upgrade,
    string Rarity,
    int RarityValue,
    string Seed,
    long Slot,
    int BeforeCount,
    int AfterCount,
    string Outcome,
    DateTimeOffset VerifiedAt,
    string Source,
    string ComponentVersion);

public sealed record InventoryValidationError(string Code, string Message);

public static class InventoryPolicy
{
    public const int MaximumPageSize = 50;

    public static InventoryValidationError? ValidateOwner(string ownerKind, int ownerIndex)
    {
        if (ownerKind is not (InventoryOwnerKinds.Player or InventoryOwnerKinds.Alliance))
            return new("INVENTORY_OWNER_INVALID", "库存归属类型无效");
        if (ownerIndex is < 1 or > 100000)
            return new("INVENTORY_OWNER_INVALID", "库存归属索引超出允许范围");
        return null;
    }

    public static InventoryValidationError? ValidateGrant(SystemUpgradeGrantRequest request)
    {
        if (request.UpgradeKey is null || !SystemUpgradeCatalog.Items.ContainsKey(request.UpgradeKey))
            return new("SYSTEM_UPGRADE_NOT_ALLOWED", "系统插件不在固定白名单中");
        if (request.Rarity is null || !SystemUpgradeCatalog.Rarities.ContainsKey(request.Rarity))
            return new("SYSTEM_UPGRADE_RARITY_INVALID", "系统插件稀有度无效");
        if (request.Confirmation is null || request.Confirmation.Length > 64)
            return new("DANGER_CONFIRMATION_REQUIRED", "插件发放确认文本无效");
        return null;
    }
}
