using System.Globalization;
using System.Net.Sockets;
using System.Text.Json;
using AvorionAdmin.Core.Models;

namespace AvorionAdmin.Agent;

public sealed partial class ManagedServerControlService
{
    public async Task<PlayerRewardResult> GrantAsync(
        int playerIndex,
        PlayerRewardGrant grant,
        CancellationToken cancellationToken = default)
    {
        if (playerIndex is < 1 or > 100000)
            throw new ServerControlException("PLAYER_INDEX_INVALID", "玩家索引超出允许范围");
        await _actionLock.WaitAsync(cancellationToken);
        try
        {
            var context = LoadContext();
            RequireRunningProcess(context.Profile);
            var requestId = Guid.NewGuid().ToString("N");
            var values = ManagementBridgeProtocol.RewardValuesForCommand(grant);
            var command = $"/orionadmin player-grant {requestId} {playerIndex} {string.Join(',', values)}";
            var response = await SourceRconProbe.QueryBridgeAsync(
                context.Address, context.Port, context.Password, command, cancellationToken);
            var acknowledgement = ManagementBridgeProtocol.Parse(
                response, requestId, "player-grant", playerIndex, 0, grant);
            RequireRunningProcess(context.Profile);

            var player = acknowledgement.GetProperty("player");
            var componentVersion = acknowledgement.GetProperty("componentVersion").GetString()!;
            var verifiedAt = DateTimeOffset.UtcNow;
            var before = ReadPlayerAssetsSnapshot(player, acknowledgement.GetProperty("before"), verifiedAt, componentVersion);
            var after = ReadPlayerAssetsSnapshot(player, acknowledgement.GetProperty("after"), verifiedAt, componentVersion);
            return new PlayerRewardResult(
                playerIndex,
                player.GetProperty("name").GetString()!,
                player.GetProperty("online").GetBoolean(),
                grant,
                before,
                after,
                "granted-and-verified",
                verifiedAt,
                "avorion-lua-api",
                componentVersion);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (ServerControlException) { throw; }
        catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException or
            JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException or
            System.Text.DecoderFallbackException)
        {
            throw new ServerControlException(
                "PLAYER_REWARD_OUTCOME_UNKNOWN",
                "奖励命令的最终结果无法确认；请刷新玩家资产后再决定是否重新提交，系统不会自动重试",
                false,
                exception);
        }
        finally { _actionLock.Release(); }
    }

    public async Task<AllianceRewardResult> GrantAllianceAsync(
        int allianceIndex,
        AllianceRewardGrant grant,
        CancellationToken cancellationToken = default)
    {
        if (allianceIndex is < 1 or > 100000)
            throw new ServerControlException("ALLIANCE_INDEX_INVALID", "联盟索引超出允许范围");
        await _actionLock.WaitAsync(cancellationToken);
        try
        {
            var context = LoadContext();
            RequireRunningProcess(context.Profile);
            var requestId = Guid.NewGuid().ToString("N");
            var values = ManagementBridgeProtocol.AllianceRewardValuesForCommand(grant);
            var command = $"/orionadmin alliance-grant {requestId} {allianceIndex} {string.Join(',', values)}";
            var response = await SourceRconProbe.QueryBridgeAsync(
                context.Address, context.Port, context.Password, command, cancellationToken);
            var acknowledgement = ManagementBridgeProtocol.Parse(
                response, requestId, "alliance-grant", allianceIndex, 0, null, grant);
            RequireRunningProcess(context.Profile);

            var alliance = acknowledgement.GetProperty("alliance");
            var componentVersion = acknowledgement.GetProperty("componentVersion").GetString()!;
            var verifiedAt = DateTimeOffset.UtcNow;
            var before = ReadAllianceAssetsSnapshot(alliance, acknowledgement.GetProperty("before"), verifiedAt, componentVersion);
            var after = ReadAllianceAssetsSnapshot(alliance, acknowledgement.GetProperty("after"), verifiedAt, componentVersion);
            return new AllianceRewardResult(
                allianceIndex,
                alliance.GetProperty("name").GetString()!,
                alliance.GetProperty("online").GetBoolean(),
                grant,
                before,
                after,
                "granted-and-verified",
                verifiedAt,
                "avorion-lua-api",
                componentVersion);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (ServerControlException) { throw; }
        catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException or
            JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException or
            System.Text.DecoderFallbackException)
        {
            throw new ServerControlException(
                "ALLIANCE_REWARD_OUTCOME_UNKNOWN",
                "联盟奖励命令的最终结果无法确认；请刷新联盟资产后再决定是否重新提交，系统不会自动重试",
                false,
                exception);
        }
        finally { _actionLock.Release(); }
    }

    public async Task<PlayerMailDeliveryResult> SendMailAsync(
        int playerIndex,
        PlayerRewardGrant grant,
        RewardMailMessage mail,
        string deliveryId,
        CancellationToken cancellationToken = default)
    {
        if (playerIndex is < 1 or > 100000)
            throw new ServerControlException("PLAYER_INDEX_INVALID", "玩家索引超出允许范围");
        if (deliveryId.Length != 32 ||
            !deliveryId.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f'))
            throw new ServerControlException("MAIL_DELIVERY_ID_INVALID", "邮件投递标识无效");
        if (mail.Subject is not { Length: >= 1 and <= RewardBatchPolicy.MaximumSubjectLength } ||
            mail.Body is not { Length: >= 1 and <= RewardBatchPolicy.MaximumBodyLength })
            throw new ServerControlException("REWARD_MAIL_INVALID", "邮件标题或正文超出允许范围");

        await _actionLock.WaitAsync(cancellationToken);
        try
        {
            var context = LoadContext();
            RequireRunningProcess(context.Profile);
            var requestId = Guid.NewGuid().ToString("N");
            var values = ManagementBridgeProtocol.RewardValuesForCommand(grant);
            var payload = $"{string.Join(',', values)};{ManagementBridgeProtocol.EncodeMailText(mail.Subject)};" +
                $"{ManagementBridgeProtocol.EncodeMailText(mail.Body)};{deliveryId}";
            var response = await SourceRconProbe.QueryBridgeAsync(
                context.Address, context.Port, context.Password,
                $"/orionadmin player-mail {requestId} {playerIndex} {payload}",
                cancellationToken);
            var acknowledgement = ManagementBridgeProtocol.Parse(
                response, requestId, "player-mail", playerIndex, 0, grant, null, deliveryId);
            RequireRunningProcess(context.Profile);
            var player = acknowledgement.GetProperty("player");
            return new PlayerMailDeliveryResult(
                playerIndex,
                player.GetProperty("name").GetString()!,
                player.GetProperty("online").GetBoolean(),
                deliveryId,
                acknowledgement.GetProperty("mailId").GetString()!,
                acknowledgement.GetProperty("mailIndex").GetInt64(),
                acknowledgement.GetProperty("replayed").GetBoolean(),
                grant,
                "delivered-and-verified",
                DateTimeOffset.UtcNow,
                "avorion-lua-api",
                acknowledgement.GetProperty("componentVersion").GetString()!);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (ServerControlException) { throw; }
        catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException or
            JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException or
            System.Text.DecoderFallbackException)
        {
            throw new ServerControlException(
                "PLAYER_MAIL_OUTCOME_UNKNOWN",
                "邮件投递结果无法确认；系统不会自动重发，请在游戏内邮箱或操作记录中核对",
                false,
                exception);
        }
        finally { _actionLock.Release(); }
    }

    public async Task<InventorySnapshot> QueryInventoryAsync(
        string ownerKind,
        int ownerIndex,
        int offset,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var ownerValidation = InventoryPolicy.ValidateOwner(ownerKind, ownerIndex);
        if (ownerValidation is not null || offset is < 0 or > 100000 ||
            limit is < 1 or > InventoryPolicy.MaximumPageSize)
            throw new ServerControlException("BRIDGE_INVALID_QUERY", "不支持的库存查询或分页参数");
        await _actionLock.WaitAsync(cancellationToken);
        try
        {
            var context = LoadContext();
            RequireRunningProcess(context.Profile);
            var requestId = Guid.NewGuid().ToString("N");
            var action = ownerKind == InventoryOwnerKinds.Player ? "player-inventory" : "alliance-inventory";
            var response = await SourceRconProbe.QueryBridgeAsync(
                context.Address, context.Port, context.Password,
                $"/orionadmin {action} {requestId} {ownerIndex} {offset},{limit}",
                cancellationToken);
            var data = ManagementBridgeProtocol.Parse(
                response, requestId, action, ownerIndex, limit,
                expectedInventoryOffset: offset, expectedInventoryLimit: limit);
            RequireRunningProcess(context.Profile);
            var owner = data.GetProperty("owner");
            var items = data.GetProperty("items").EnumerateArray().Select(item =>
            {
                var rarity = item.GetProperty("rarity");
                var script = item.GetProperty("script");
                var seed = item.GetProperty("seed");
                return new InventoryItemSummary(
                    item.GetProperty("slot").GetInt64(),
                    item.GetProperty("amount").GetInt32(),
                    item.GetProperty("itemType").GetString()!,
                    item.GetProperty("name").GetString()!,
                    rarity.ValueKind == JsonValueKind.Null ? null : rarity.GetInt32(),
                    script.ValueKind == JsonValueKind.Null ? null : script.GetString(),
                    seed.ValueKind == JsonValueKind.Null ? null : seed.GetString(),
                    ReadNullableString(item, "title"),
                    ReadNullableString(item, "icon"),
                    ReadNullableString(item, "weaponType"),
                    ReadNullableString(item, "weaponCategory"),
                    ReadNullableString(item, "turretSlotType"),
                    ReadNullableInt32(item, "material"),
                    ReadNullableInt32(item, "averageTech"),
                    ReadNullableInt32(item, "maxTech"),
                    ReadNullableDouble(item, "dps"),
                    ReadNullableDouble(item, "damage"),
                    ReadNullableDouble(item, "reach"),
                    ReadNullableDouble(item, "fireRate"),
                    ReadNullableDouble(item, "accuracy"),
                    ReadNullableInt32(item, "turretSlots"),
                    ReadNullableDouble(item, "size"),
                    ReadNullableInt32(item, "numWeapons"),
                    ReadNullableBoolean(item, "armed"),
                    ReadNullableBoolean(item, "civil"),
                    ReadNullableBoolean(item, "coaxial"),
                    ReadNullableBoolean(item, "seeker"),
                    ReadNullableBoolean(item, "continuousBeam"));
            }).ToArray();
            return new InventorySnapshot(
                ownerKind,
                ownerIndex,
                owner.GetProperty("name").GetString()!,
                owner.GetProperty("online").GetBoolean(),
                data.GetProperty("total").GetInt32(),
                data.GetProperty("occupiedSlots").GetInt32(),
                data.GetProperty("maxSlots").GetInt32(),
                offset,
                limit,
                items,
                DateTimeOffset.UtcNow,
                "live",
                "avorion-lua-api",
                data.GetProperty("componentVersion").GetString()!);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (ServerControlException) { throw; }
        catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException or
            JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException or
            System.Text.DecoderFallbackException)
        {
            throw new ServerControlException(
                "INVENTORY_UNAVAILABLE",
                "库存读取失败或管理组件返回了不兼容的数据",
                true,
                exception);
        }
        finally { _actionLock.Release(); }
    }

    public async Task<SystemUpgradeGrantResult> GrantSystemUpgradeAsync(
        string ownerKind,
        int ownerIndex,
        string upgradeKey,
        string rarity,
        string seed,
        CancellationToken cancellationToken = default)
    {
        var ownerValidation = InventoryPolicy.ValidateOwner(ownerKind, ownerIndex);
        if (ownerValidation is not null ||
            !SystemUpgradeCatalog.Items.TryGetValue(upgradeKey, out var definition) ||
            !SystemUpgradeCatalog.Rarities.TryGetValue(rarity, out var rarityValue) ||
            seed.Length != 32 || !seed.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f'))
            throw new ServerControlException("SYSTEM_UPGRADE_GRANT_INVALID", "系统插件发放参数无效");
        await _actionLock.WaitAsync(cancellationToken);
        try
        {
            var context = LoadContext();
            RequireRunningProcess(context.Profile);
            var requestId = Guid.NewGuid().ToString("N");
            var action = ownerKind == InventoryOwnerKinds.Player ? "player-system-grant" : "alliance-system-grant";
            var response = await SourceRconProbe.QueryBridgeAsync(
                context.Address, context.Port, context.Password,
                $"/orionadmin {action} {requestId} {ownerIndex} {upgradeKey},{rarity},{seed}",
                cancellationToken);
            var data = ManagementBridgeProtocol.Parse(
                response, requestId, action, ownerIndex, 1,
                expectedUpgrade: definition, expectedRarity: rarity, expectedSeed: seed);
            RequireRunningProcess(context.Profile);
            var owner = data.GetProperty("owner");
            return new SystemUpgradeGrantResult(
                ownerKind,
                ownerIndex,
                owner.GetProperty("name").GetString()!,
                owner.GetProperty("online").GetBoolean(),
                definition,
                rarity,
                rarityValue,
                data.GetProperty("upgrade").GetProperty("seed").GetString()!,
                data.GetProperty("slot").GetInt64(),
                data.GetProperty("beforeCount").GetInt32(),
                data.GetProperty("afterCount").GetInt32(),
                "granted-and-verified",
                DateTimeOffset.UtcNow,
                "avorion-lua-api",
                data.GetProperty("componentVersion").GetString()!);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (ServerControlException) { throw; }
        catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException or
            JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException or
            System.Text.DecoderFallbackException)
        {
            throw new ServerControlException(
                "SYSTEM_UPGRADE_OUTCOME_UNKNOWN",
                "插件发放结果无法确认；请刷新库存后再决定是否重新提交，系统不会自动重试",
                false,
                exception);
        }
        finally { _actionLock.Release(); }
    }

    public async Task<JsonElement> QueryManagementBridgeAsync(string action, int offset, int limit,
        CancellationToken cancellationToken = default)
    {
        if (action is not ("hello" or "players" or "players-known" or "sectors" or "alliances" or "alliance" or "player-assets" or "alliance-assets") ||
            action is not ("alliance" or "player-assets" or "alliance-assets") && offset is < 0 or > 100000 ||
            action is "alliance" or "player-assets" or "alliance-assets" && offset is < 1 or > 100000 ||
            limit is < 1 or > 50 ||
            action is "players" or "alliances" && limit > 10 ||
            action == "players-known" && limit > 50 ||
            action == "alliance" && limit > 20 ||
            action is "player-assets" or "alliance-assets" && limit != 1)
            throw new ServerControlException("BRIDGE_INVALID_QUERY", "不支持的管理组件查询或分页参数");
        await _actionLock.WaitAsync(cancellationToken);
        try
        {
            var context = LoadContext();
            RequireRunningProcess(context.Profile);
            var result = await QueryBridgeCoreAsync(context, action, offset, limit, cancellationToken);
            RequireRunningProcess(context.Profile);
            return result;
        }
        catch (ServerControlException) { throw; }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new ServerControlException("BRIDGE_UNAVAILABLE", "管理组件没有在时限内返回有效数据，请检查是否安装并加载", true); }
        catch (Exception ex) when (ex is IOException or SocketException or JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException or System.Text.DecoderFallbackException)
        { throw new ServerControlException("BRIDGE_RESPONSE_INVALID", "管理组件连接中断或返回了不兼容的数据", true); }
        finally { _actionLock.Release(); }
    }

    public async Task<SectorUnloadResult> TryUnloadSectorAsync(int x, int y,
        CancellationToken cancellationToken = default)
    {
        if (x is < -500 or > 500 || y is < -500 or > 500)
            throw new ServerControlException("SECTOR_COORDINATES_INVALID", "星区坐标必须在 -500 到 500 之间");
        await _actionLock.WaitAsync(cancellationToken);
        try
        {
            var context = LoadContext();
            RequireRunningProcess(context.Profile);
            var acknowledgement = await QueryBridgeCoreAsync(context, "unload", x, y, cancellationToken);
            var evidence = new List<string>
            {
                $"管理组件确认目标星区 ({x}, {y}) 已加载且没有在线玩家",
                "Avorion Galaxy():tryUnloadSector() 已接受卸载请求"
            };
            var unloaded = false;
            for (var attempt = 0; attempt < 10; attempt++)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
                var page = await QueryBridgeCoreAsync(context, "sectors", 0, 50, cancellationToken);
                var stillLoaded = page.GetProperty("items").EnumerateArray().Any(item =>
                    item.GetProperty("x").GetInt32() == x && item.GetProperty("y").GetInt32() == y);
                if (!stillLoaded)
                {
                    unloaded = true;
                    break;
                }
            }
            RequireRunningProcess(context.Profile);
            evidence.Add(unloaded
                ? "后续实时查询确认目标星区已从加载列表消失"
                : "5 秒复核期后目标仍由游戏保持加载；没有强制清除或修改星区数据");
            return new SectorUnloadResult(
                acknowledgement.GetProperty("x").GetInt32(),
                acknowledgement.GetProperty("y").GetInt32(),
                true,
                unloaded,
                unloaded ? "unloaded" : "accepted-kept-alive",
                evidence,
                DateTimeOffset.UtcNow);
        }
        catch (ServerControlException) { throw; }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new ServerControlException("SECTOR_UNLOAD_TIMEOUT", "空闲星区卸载请求或复核超时", true); }
        catch (Exception ex) when (ex is IOException or SocketException or JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException or System.Text.DecoderFallbackException)
        { throw new ServerControlException("SECTOR_UNLOAD_FAILED", "空闲星区卸载连接中断或返回了不兼容的数据", true, ex); }
        finally { _actionLock.Release(); }
    }

    private static async Task<JsonElement> QueryBridgeCoreAsync(ManagedControlContext context, string action,
        int first, int second, CancellationToken cancellationToken)
    {
        var requestId = Guid.NewGuid().ToString("N");
        var command = $"/orionadmin {action} {requestId}" +
            (action is "players" or "players-known" or "sectors" or "alliances" or "alliance" or "player-assets" or "alliance-assets" or "unload" ? $" {first} {second}" : "");
        var response = await SourceRconProbe.QueryBridgeAsync(
            context.Address, context.Port, context.Password, command, cancellationToken);
        return ManagementBridgeProtocol.Parse(response, requestId, action, first, second);
    }

    private static string? ReadNullableString(JsonElement item, string propertyName) =>
        item.TryGetProperty(propertyName, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.GetString()
            : null;

    private static int? ReadNullableInt32(JsonElement item, string propertyName) =>
        item.TryGetProperty(propertyName, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.GetInt32()
            : null;

    private static double? ReadNullableDouble(JsonElement item, string propertyName) =>
        item.TryGetProperty(propertyName, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.GetDouble()
            : null;

    private static bool? ReadNullableBoolean(JsonElement item, string propertyName) =>
        item.TryGetProperty(propertyName, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.GetBoolean()
            : null;

    private static PlayerAssetsSnapshot ReadPlayerAssetsSnapshot(
        JsonElement player,
        JsonElement value,
        DateTimeOffset sampledAt,
        string componentVersion)
    {
        var resources = value.GetProperty("resources");
        return new PlayerAssetsSnapshot(
            player.GetProperty("index").GetInt32(),
            player.GetProperty("name").GetString()!,
            player.GetProperty("online").GetBoolean(),
            value.GetProperty("credits").GetInt64(),
            new PlayerResourceBalances(
                resources.GetProperty("iron").GetInt64(),
                resources.GetProperty("titanium").GetInt64(),
                resources.GetProperty("naonite").GetInt64(),
                resources.GetProperty("trinium").GetInt64(),
                resources.GetProperty("xanion").GetInt64(),
                resources.GetProperty("ogonite").GetInt64(),
                resources.GetProperty("avorion").GetInt64()),
            sampledAt,
            "live",
            "avorion-lua-api",
            componentVersion);
    }

    private static AllianceAssetsSnapshot ReadAllianceAssetsSnapshot(
        JsonElement alliance,
        JsonElement value,
        DateTimeOffset sampledAt,
        string componentVersion)
    {
        var resources = value.GetProperty("resources");
        return new AllianceAssetsSnapshot(
            alliance.GetProperty("index").GetInt32(),
            alliance.GetProperty("name").GetString()!,
            alliance.GetProperty("online").GetBoolean(),
            value.GetProperty("credits").GetInt64(),
            new AllianceResourceBalances(
                resources.GetProperty("iron").GetInt64(),
                resources.GetProperty("titanium").GetInt64(),
                resources.GetProperty("naonite").GetInt64(),
                resources.GetProperty("trinium").GetInt64(),
                resources.GetProperty("xanion").GetInt64(),
                resources.GetProperty("ogonite").GetInt64(),
                resources.GetProperty("avorion").GetInt64()),
            sampledAt,
            "live",
            "avorion-lua-api",
            componentVersion);
    }
}

public static partial class ManagementBridgeProtocolExtensions
{
}
