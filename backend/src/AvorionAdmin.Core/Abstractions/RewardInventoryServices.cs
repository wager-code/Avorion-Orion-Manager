using AvorionAdmin.Core.Models;

namespace AvorionAdmin.Core.Abstractions;

public interface IPlayerRewardService
{
    Task<PlayerRewardResult> GrantAsync(
        int playerIndex,
        PlayerRewardGrant grant,
        CancellationToken cancellationToken = default);
}

public interface IAllianceRewardService
{
    Task<AllianceRewardResult> GrantAllianceAsync(
        int allianceIndex,
        AllianceRewardGrant grant,
        CancellationToken cancellationToken = default);
}

public interface IPlayerMailService
{
    Task<PlayerMailDeliveryResult> SendMailAsync(
        int playerIndex,
        PlayerRewardGrant grant,
        RewardMailMessage mail,
        string deliveryId,
        CancellationToken cancellationToken = default);
}

public interface IInventoryService
{
    Task<InventorySnapshot> QueryInventoryAsync(
        string ownerKind,
        int ownerIndex,
        int offset,
        int limit,
        CancellationToken cancellationToken = default);

    Task<SystemUpgradeGrantResult> GrantSystemUpgradeAsync(
        string ownerKind,
        int ownerIndex,
        string upgradeKey,
        string rarity,
        string seed,
        CancellationToken cancellationToken = default);
}

public interface IInventoryCatalogService
{
    Task<InventoryCatalogPage> QueryAsync(
        InventoryCatalogQuery query,
        CancellationToken cancellationToken = default);

    Task<InventoryIconFile?> ReadIconAsync(
        string iconPath,
        CancellationToken cancellationToken = default);
}
