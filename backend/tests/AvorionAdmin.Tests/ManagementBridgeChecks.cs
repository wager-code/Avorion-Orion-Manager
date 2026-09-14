using System.Text.Json;
using AvorionAdmin.Agent;
using AvorionAdmin.Core.Models;

internal static class ManagementBridgeChecks
{
    public static void Run(Action<bool, string> check)
    {
        const string id = "0123456789abcdef0123456789abcdef";
        string Frame(object value) => ManagementBridgeProtocol.Begin + JsonSerializer.Serialize(value) + ManagementBridgeProtocol.End;
        var empty = Frame(new { protocolVersion = 1, componentVersion = "0.4.0", requestId = id,
            action = "players", source = "avorion-lua-api", readOnly = true, total = 0, offset = 0, limit = 10, items = Array.Empty<object>() });
        check(ManagementBridgeProtocol.Parse(empty, id, "players").GetProperty("total").GetInt32() == 0, "bridge accepts an explicit valid empty page");
        void Reject(
            string frame,
            string requestId,
            string action,
            string label,
            int offset = 0,
            int limit = 10,
            PlayerRewardGrant? expectedGrant = null,
            AllianceRewardGrant? expectedAllianceGrant = null,
            string? expectedDeliveryId = null,
            SystemUpgradeDefinition? expectedUpgrade = null,
            string? expectedRarity = null,
            string? expectedSeed = null,
            int expectedInventoryOffset = 0,
            int expectedInventoryLimit = 50)
        {
            try { ManagementBridgeProtocol.Parse(frame, requestId, action, offset, limit, expectedGrant, expectedAllianceGrant,
                expectedDeliveryId, expectedUpgrade, expectedRarity, expectedSeed, expectedInventoryOffset, expectedInventoryLimit); check(false, label); }
            catch (Exception ex) when (ex is InvalidDataException or JsonException or KeyNotFoundException or InvalidOperationException) { check(true, label); }
        }
        Reject("", id, "players", "missing mod cannot become a fake empty player list");
        Reject(empty, new string('a', 32), "players", "bridge rejects stale/mismatched request IDs");
        Reject(empty + empty, id, "players", "bridge rejects ambiguous multiple frames");
        Reject(empty.Replace("0.4.0", "9.9.9"), id, "players", "bridge rejects unsupported component versions");
        Reject(empty.Replace("\"readOnly\":true", "\"readOnly\":false"), id, "players", "bridge rejects unexpected write capability");
        Reject(empty.Replace("\"total\":0", "\"total\":1"), id, "players", "bridge rejects incomplete pages");
        Reject(empty[..^5], id, "players", "bridge rejects truncated frames");
        var hello7 = Frame(new { protocolVersion = 1, componentVersion = "0.7.0", requestId = id,
            action = "hello", source = "avorion-lua-api", readOnly = true, serverSideOnly = true,
            capabilities = new[] { "players.online", "players.position", "players.reward.grant",
                "alliances.read", "alliances.members", "alliances.assets", "alliances.reward.grant",
                "sectors.loaded", "sectors.unload-attempt" } });
        check(ManagementBridgeProtocol.Parse(hello7, id, "hello").GetProperty("componentVersion").GetString() == "0.7.0",
            "bridge accepts the complete 0.7.0 alliance asset capability declaration");
        Reject(hello7.Replace(",\"alliances.reward.grant\"", ""), id, "hello",
            "bridge rejects 0.7.0 without alliance reward capability");
        var hello8 = Frame(new { protocolVersion = 1, componentVersion = "0.8.0", requestId = id,
            action = "hello", source = "avorion-lua-api", readOnly = true, serverSideOnly = true,
            capabilities = new[] { "players.online", "players.position", "players.reward.grant", "players.known", "players.mail.send",
                "alliances.read", "alliances.members", "alliances.assets", "alliances.reward.grant",
                "sectors.loaded", "sectors.unload-attempt" } });
        check(ManagementBridgeProtocol.Parse(hello8, id, "hello").GetProperty("componentVersion").GetString() == "0.8.0",
            "bridge accepts the complete 0.8.0 known-player and mail capability declaration");
        Reject(hello8.Replace(",\"players.mail.send\"", ""), id, "hello",
            "bridge rejects 0.8.0 without player mail capability");
        var hello9 = Frame(new { protocolVersion = 1, componentVersion = "0.9.0", requestId = id,
            action = "hello", source = "avorion-lua-api", readOnly = true, serverSideOnly = true,
            capabilities = new[] { "players.online", "players.position", "players.reward.grant", "players.known", "players.mail.send",
                "players.inventory.read", "players.inventory.system-upgrade.grant",
                "alliances.read", "alliances.members", "alliances.assets", "alliances.reward.grant",
                "alliances.inventory.read", "alliances.inventory.system-upgrade.grant",
                "sectors.loaded", "sectors.unload-attempt" } });
        check(ManagementBridgeProtocol.Parse(hello9, id, "hello").GetProperty("componentVersion").GetString() == "0.9.0",
            "bridge accepts the complete 0.9.0 inventory capability declaration");
        Reject(hello9.Replace(",\"players.inventory.read\"", ""), id, "hello",
            "bridge rejects 0.9.0 without player inventory read capability");
        var hello10 = Frame(new { protocolVersion = 1, componentVersion = "0.10.0", requestId = id,
            action = "hello", source = "avorion-lua-api", readOnly = true, serverSideOnly = true,
            capabilities = new[] { "players.online", "players.position", "players.reward.grant", "players.known", "players.mail.send",
                "players.inventory.read", "players.inventory.system-upgrade.grant",
                "alliances.read", "alliances.members", "alliances.assets", "alliances.reward.grant",
                "alliances.inventory.read", "alliances.inventory.system-upgrade.grant", "inventory.item-details.read",
                "sectors.loaded", "sectors.unload-attempt" } });
        check(ManagementBridgeProtocol.Parse(hello10, id, "hello").GetProperty("componentVersion").GetString() == "0.10.0",
            "bridge accepts the complete 0.10.0 inventory detail capability declaration");
        Reject(hello10.Replace(",\"inventory.item-details.read\"", ""), id, "hello",
            "bridge rejects 0.10.0 without inventory detail capability");
        var knownPlayers = Frame(new { protocolVersion = 1, componentVersion = "0.8.0", requestId = id,
            action = "players-known", source = "avorion-lua-api", readOnly = true, total = 2, offset = 0, limit = 50,
            items = new[] { new { index = 1, name = "离线玩家", online = false }, new { index = 2, name = "在线玩家", online = true } } });
        check(ManagementBridgeProtocol.Parse(knownPlayers, id, "players-known", 0, 50)
                .GetProperty("items")[0].GetProperty("online").GetBoolean() == false,
            "known-player directory accepts offline identities without duplicating position data");
        Reject(knownPlayers.Replace("\"index\":2", "\"index\":1"), id, "players-known",
            "known-player directory rejects duplicate player identities", 0, 50);
        var inventory = Frame(new { protocolVersion = 1, componentVersion = "0.9.0", requestId = id,
            action = "player-inventory", source = "avorion-lua-api", readOnly = true,
            owner = new { kind = "player", index = 1, name = "领航员", online = false },
            total = 2, occupiedSlots = 2, maxSlots = 1000, offset = 0, limit = 50,
            items = new object[] {
                new { slot = 3L, amount = 1, itemType = "system-upgrade", name = "Energy Booster",
                    rarity = 2, script = "data/scripts/systems/energybooster.lua", seed = "abcdef" },
                new { slot = 9L, amount = 4, itemType = "vanilla-item", name = "物品",
                    rarity = (int?)null, script = (string?)null, seed = (string?)null } } });
        check(ManagementBridgeProtocol.Parse(inventory, id, "player-inventory", 1, 50,
                expectedInventoryOffset: 0, expectedInventoryLimit: 50)
                .GetProperty("items").GetArrayLength() == 2,
            "bridge accepts a bounded player inventory page with minimal item fields");
        Reject(inventory.Replace("\"slot\":9", "\"slot\":3"), id, "player-inventory",
            "bridge rejects duplicate inventory slots", 1, 50, expectedInventoryOffset: 0, expectedInventoryLimit: 50);
        Reject(inventory.Replace("\"kind\":\"player\"", "\"kind\":\"alliance\""), id, "player-inventory",
            "bridge rejects inventory data for another owner kind", 1, 50, expectedInventoryOffset: 0, expectedInventoryLimit: 50);
        var detailedInventory = Frame(new { protocolVersion = 1, componentVersion = "0.10.0", requestId = id,
            action = "player-inventory", source = "avorion-lua-api", readOnly = true,
            owner = new { kind = "player", index = 1, name = "领航员", online = false },
            total = 1, occupiedSlots = 1, maxSlots = 1000, offset = 0, limit = 50,
            items = new object[] {
                new { slot = 7L, amount = 1, itemType = "turret", name = "Railgun", rarity = 4,
                    script = (string?)null, seed = (string?)null, title = "Long-Range Railgun X-1",
                    icon = "data/textures/icons/rail-gun.png", weaponType = "RailGun", weaponCategory = "armed",
                    turretSlotType = "armed", material = 4, averageTech = 42, maxTech = 42, dps = 1250.5,
                    damage = 5000.0, reach = 1750.0, fireRate = 0.25, accuracy = 0.99, turretSlots = 4,
                    size = 2.0, numWeapons = 2, armed = true, civil = false, coaxial = false,
                    seeker = false, continuousBeam = false } } });
        check(ManagementBridgeProtocol.Parse(detailedInventory, id, "player-inventory", 1, 50,
                expectedInventoryOffset: 0, expectedInventoryLimit: 50)
                .GetProperty("items")[0].GetProperty("averageTech").GetInt32() == 42,
            "bridge accepts bounded real turret details in a 0.10.0 inventory page");
        Reject(detailedInventory.Replace("\"material\":4", "\"material\":7"), id, "player-inventory",
            "bridge rejects an invalid turret material", 1, 50, expectedInventoryOffset: 0, expectedInventoryLimit: 50);
        Reject(detailedInventory.Replace("\"weaponType\":\"RailGun\"", "\"weaponType\":\"StoryCannon\""), id, "player-inventory",
            "bridge rejects an unknown turret type", 1, 50, expectedInventoryOffset: 0, expectedInventoryLimit: 50);

        const string upgradeSeed = "abcdef0123456789abcdef0123456789";
        var upgradeDefinition = SystemUpgradeCatalog.Items["energy-booster"];
        var systemGrant = Frame(new { protocolVersion = 1, componentVersion = "0.9.0", requestId = id,
            action = "player-system-grant", source = "avorion-lua-api", readOnly = false,
            owner = new { kind = "player", index = 1, name = "领航员", online = false }, accepted = true,
            upgrade = new { key = upgradeDefinition.Key, name = "Energy Booster", script = upgradeDefinition.Script,
                rarity = "rare", rarityValue = 2, requestSeed = upgradeSeed, seed = "-284938293848" },
            beforeCount = 0, afterCount = 1, slot = 17L });
        check(ManagementBridgeProtocol.Parse(systemGrant, id, "player-system-grant", 1, 1,
                expectedUpgrade: upgradeDefinition, expectedRarity: "rare", expectedSeed: upgradeSeed)
                .GetProperty("slot").GetInt64() == 17,
            "bridge accepts a whitelisted system upgrade with its correlated request seed and exact inventory delta");
        Reject(systemGrant.Replace(upgradeSeed, "ffffffffffffffffffffffffffffffff"), id, "player-system-grant",
            "bridge rejects a system upgrade response correlated to another request seed", 1, 1,
            expectedUpgrade: upgradeDefinition, expectedRarity: "rare", expectedSeed: upgradeSeed);
        Reject(systemGrant.Replace("\"afterCount\":1", "\"afterCount\":2"), id, "player-system-grant",
            "bridge rejects an inexact system upgrade inventory delta", 1, 1, expectedUpgrade: upgradeDefinition,
            expectedRarity: "rare", expectedSeed: upgradeSeed);
        Reject(systemGrant.Replace(upgradeDefinition.Script, "data/scripts/systems/teleporterkey1.lua"),
            id, "player-system-grant", "bridge rejects a different or story-related script", 1, 1,
            expectedUpgrade: upgradeDefinition, expectedRarity: "rare", expectedSeed: upgradeSeed);
        var validSystemGrant = new SystemUpgradeGrantRequest("energy-booster", "rare", "GRANT PLAYER SYSTEM 1");
        check(InventoryPolicy.ValidateGrant(validSystemGrant) is null,
            "inventory policy accepts a fixed whitelisted system upgrade");
        check(InventoryPolicy.ValidateGrant(validSystemGrant with { UpgradeKey = "teleporter-key" })?.Code == "SYSTEM_UPGRADE_NOT_ALLOWED",
            "inventory policy rejects plugins outside the whitelist");
        check(InventoryPolicy.ValidateGrant(validSystemGrant with { Rarity = "mythic" })?.Code == "SYSTEM_UPGRADE_RARITY_INVALID",
            "inventory policy rejects unsupported rarities");
        var unicode = Frame(new { protocolVersion = 1, componentVersion = "0.4.0", requestId = id,
            action = "players", source = "avorion-lua-api", readOnly = true, total = 1, offset = 0, limit = 10,
            items = new[] { new { index = 1, name = "中文\"玩家\\测试", online = true, x = -447, y = 47 } } });
        check(ManagementBridgeProtocol.Parse(unicode, id, "players").GetProperty("items")[0].GetProperty("name").GetString() == "中文\"玩家\\测试", "bridge preserves Chinese names and escaped characters");
        var markerName = unicode.Replace(@"\u4E2D\u6587", "ORION_BEGIN::ORION_END");
        check(ManagementBridgeProtocol.Parse(markerName, id, "players").GetProperty("items")[0].GetProperty("name").GetString()!.StartsWith("ORION_BEGIN::ORION_END"), "protocol markers inside JSON strings do not truncate valid responses");
        Reject(unicode.Replace("\"x\":-447", "\"x\":null"), id, "players", "bridge rejects half-missing player coordinates");
        var sectors = Frame(new { protocolVersion = 1, componentVersion = "0.4.0", requestId = id,
            action = "sectors", source = "avorion-lua-api", readOnly = true, total = 2, offset = 0, limit = 50,
            items = new[] { new { x = 0, y = 0, playerCount = 1, eligible = false }, new { x = 4, y = -2, playerCount = 0, eligible = true } } });
        check(ManagementBridgeProtocol.Parse(sectors, id, "sectors", 0, 50).GetProperty("items").GetArrayLength() == 2,
            "bridge validates real loaded-sector pages and their player safety eligibility");
        Reject(sectors.Replace("\"eligible\":false", "\"eligible\":true"), id, "sectors", "bridge rejects unsafe sector eligibility claims");
        var alliances = Frame(new { protocolVersion = 1, componentVersion = "0.4.0", requestId = id,
            action = "alliances", source = "avorion-lua-api", readOnly = true, total = 1, totalMembers = 2,
            totalOnlineMembers = 1, onlineAlliances = 1, offset = 0, limit = 10,
            items = new[] { new { index = 7, name = "星海联合", online = true, leaderIndex = 1,
                leaderName = "领航员", memberTotal = 2, onlineMembers = 1, homeX = -4, homeY = 12, numCrafts = 3, numStations = 1 } } });
        check(ManagementBridgeProtocol.Parse(alliances, id, "alliances").GetProperty("totalMembers").GetInt32() == 2,
            "bridge accepts validated real alliance summaries");
        Reject(alliances.Replace("\"totalMembers\":2", "\"totalMembers\":0"), id, "alliances", "bridge rejects impossible alliance aggregates");
        Reject(alliances.Replace("\"homeX\":-4", "\"homeX\":null"), id, "alliances", "bridge rejects half-missing alliance coordinates");
        var alliance = Frame(new { protocolVersion = 1, componentVersion = "0.4.0", requestId = id,
            action = "alliance", source = "avorion-lua-api", readOnly = true,
            alliance = new { index = 7, name = "星海联合", online = true, leaderIndex = 1, leaderName = "领航员",
                memberTotal = 2, onlineMembers = 1, homeX = -4, homeY = 12, numCrafts = 3, numStations = 1 },
            memberLimit = 20, members = new[] {
                new { index = 1, name = "领航员", rank = "Leader", online = true, x = (int?)-4, y = (int?)12 },
                new { index = 2, name = "探索者", rank = "Member", online = false, x = (int?)null, y = (int?)null } } });
        check(ManagementBridgeProtocol.Parse(alliance, id, "alliance", 7, 20).GetProperty("members").GetArrayLength() == 2,
            "bridge accepts validated alliance members");
        Reject(alliance.Replace("\"memberLimit\":20", "\"memberLimit\":10"), id, "alliance", "bridge rejects mismatched alliance detail limits", 7, 20);
        Reject(alliance.Replace("\"x\":-4", "\"x\":null"), id, "alliance", "bridge rejects half-missing member coordinates", 7, 20);
        var playerAssets = Frame(new { protocolVersion = 1, componentVersion = "0.5.0", requestId = id,
            action = "player-assets", source = "avorion-lua-api", readOnly = true,
            player = new { index = 1, name = "领航员", online = false }, credits = 125000L,
            resources = new { iron = 10L, titanium = 20L, naonite = 30L, trinium = 40L,
                xanion = 50L, ogonite = 60L, avorion = 70L } });
        check(ManagementBridgeProtocol.Parse(playerAssets, id, "player-assets", 1, 1)
                .GetProperty("resources").GetProperty("avorion").GetInt64() == 70,
            "bridge accepts validated credits and all seven player resources");
        Reject(playerAssets.Replace("\"index\":1", "\"index\":2"), id, "player-assets",
            "bridge rejects a player asset response for another player", 1, 1);
        Reject(playerAssets.Replace("\"iron\":10", "\"iron\":-1"), id, "player-assets",
            "bridge rejects negative player resource balances", 1, 1);
        Reject(playerAssets.Replace("\"credits\":125000", "\"credits\":9007199254740992"), id, "player-assets",
            "bridge rejects player balances outside the lossless protocol range", 1, 1);
        var rewardGrant = new PlayerRewardGrant(1, new PlayerResourceBalances(2, 0, 0, 0, 0, 0, 0));
        var playerGrant = Frame(new { protocolVersion = 1, componentVersion = "0.6.0", requestId = id,
            action = "player-grant", source = "avorion-lua-api", readOnly = false,
            player = new { index = 1, name = "领航员", online = false }, accepted = true,
            grant = new { credits = 1L, resources = new { iron = 2L, titanium = 0L, naonite = 0L,
                trinium = 0L, xanion = 0L, ogonite = 0L, avorion = 0L } },
            before = new { credits = 125000L, resources = new { iron = 10L, titanium = 20L, naonite = 30L,
                trinium = 40L, xanion = 50L, ogonite = 60L, avorion = 70L } },
            after = new { credits = 125001L, resources = new { iron = 12L, titanium = 20L, naonite = 30L,
                trinium = 40L, xanion = 50L, ogonite = 60L, avorion = 70L } } });
        check(ManagementBridgeProtocol.Parse(playerGrant, id, "player-grant", 1, 0, rewardGrant)
                .GetProperty("accepted").GetBoolean(),
            "bridge accepts an exact player reward with proven before and after balances");
        Reject(playerGrant.Replace("\"credits\":125001", "\"credits\":125002"), id, "player-grant",
            "bridge rejects a reward response whose after balance is not the exact change", 1, 0, rewardGrant);
        Reject(playerGrant.Replace("\"iron\":2", "\"iron\":3"), id, "player-grant",
            "bridge rejects a reward acknowledgement for different requested amounts", 1, 0, rewardGrant);
        Reject(playerGrant.Replace("\"readOnly\":false", "\"readOnly\":true"), id, "player-grant",
            "bridge requires explicit write semantics for player rewards", 1, 0, rewardGrant);
        Reject(playerGrant.Replace("0.6.0", "0.5.0"), id, "player-grant",
            "older bridge versions cannot claim the player reward action", 1, 0, rewardGrant);

        const string deliveryId = "abcdef0123456789abcdef0123456789";
        var playerMail = Frame(new { protocolVersion = 1, componentVersion = "0.8.0", requestId = id,
            action = "player-mail", source = "avorion-lua-api", readOnly = false,
            player = new { index = 1, name = "领航员", online = false }, accepted = true,
            deliveryId, mailId = $"orionadmin-{deliveryId}", mailIndex = 4L, replayed = false,
            grant = new { credits = 1L, resources = new { iron = 2L, titanium = 0L, naonite = 0L,
                trinium = 0L, xanion = 0L, ogonite = 0L, avorion = 0L } } });
        check(ManagementBridgeProtocol.Parse(playerMail, id, "player-mail", 1, 0, rewardGrant, null, deliveryId)
                .GetProperty("mailIndex").GetInt64() == 4,
            "bridge accepts a player mail acknowledgement with exact unique delivery and attachments");
        Reject(playerMail.Replace(deliveryId, new string('1', 32)), id, "player-mail",
            "bridge rejects a mail acknowledgement for another delivery", 1, 0, rewardGrant, null, deliveryId);
        Reject(playerMail.Replace("\"iron\":2", "\"iron\":3"), id, "player-mail",
            "bridge rejects mail attachments that differ from the requested grant", 1, 0, rewardGrant, null, deliveryId);
        Reject(playerMail.Replace("\"readOnly\":false", "\"readOnly\":true"), id, "player-mail",
            "bridge requires explicit write semantics for player mail", 1, 0, rewardGrant, null, deliveryId);
        Reject(playerMail.Replace("0.8.0", "0.7.0"), id, "player-mail",
            "older bridge versions cannot claim player mail", 1, 0, rewardGrant, null, deliveryId);

        var validReward = new PlayerRewardRequest(rewardGrant, "GRANT PLAYER 1");
        check(PlayerRewardPolicy.Validate(validReward) is null,
            "player reward policy accepts a bounded non-empty grant");
        check(PlayerRewardPolicy.Validate(validReward with { Grant = rewardGrant with { Credits = -1 } })?.Code == "PLAYER_REWARD_LIMIT",
            "player reward policy rejects negative Credits");
        check(PlayerRewardPolicy.Validate(validReward with {
                Grant = rewardGrant with { Resources = rewardGrant.Resources with { Iron = PlayerRewardPolicy.MaximumResourcePerGrant + 1 } }
            })?.Code == "PLAYER_REWARD_LIMIT",
            "player reward policy enforces the per-resource maximum");
        check(PlayerRewardPolicy.Validate(validReward with {
                Grant = new PlayerRewardGrant(0, new PlayerResourceBalances(0, 0, 0, 0, 0, 0, 0))
            })?.Code == "PLAYER_REWARD_EMPTY",
            "player reward policy rejects empty grants");

        var validMailBatch = new RewardBatchRequest(
            RewardDeliveryModes.Mail,
            new[] { 1, 2 },
            rewardGrant,
            new RewardMailMessage("活动礼包", "感谢参与，请查收奖励。"),
            "CREATE PLAYER BATCH 2");
        check(RewardBatchPolicy.Validate(validMailBatch) is null,
            "reward batch policy accepts bounded mail delivery to unique player targets");
        check(RewardBatchPolicy.Validate(validMailBatch with { PlayerIndexes = new[] { 1, 1 } })?.Code == "REWARD_TARGETS_INVALID",
            "reward batch policy rejects duplicate targets");
        check(RewardBatchPolicy.Validate(validMailBatch with { PlayerIndexes = Enumerable.Range(1, 51).ToArray() })?.Code == "REWARD_TARGETS_INVALID",
            "reward batch policy enforces the 50-player target maximum");
        check(RewardBatchPolicy.Validate(validMailBatch with { Mail = new RewardMailMessage("", "正文") })?.Code == "REWARD_MAIL_INVALID",
            "reward batch policy requires bounded non-empty mail text");
        var validDirectBatch = validMailBatch with {
            Delivery = RewardDeliveryModes.Direct,
            Mail = null,
            Confirmation = "CREATE PLAYER BATCH 2"
        };
        check(RewardBatchPolicy.Validate(validDirectBatch) is null,
            "reward batch policy accepts direct delivery without mail content");
        check(RewardBatchPolicy.Validate(validDirectBatch with { Mail = new RewardMailMessage("标题", "正文") })?.Code == "REWARD_MAIL_NOT_ALLOWED",
            "reward batch policy rejects hidden mail content in direct delivery");

        var allianceAssets = Frame(new { protocolVersion = 1, componentVersion = "0.7.0", requestId = id,
            action = "alliance-assets", source = "avorion-lua-api", readOnly = true,
            alliance = new { index = 7, name = "星海联合", online = false }, credits = 500000L,
            resources = new { iron = 100L, titanium = 200L, naonite = 300L, trinium = 400L,
                xanion = 500L, ogonite = 600L, avorion = 700L } });
        check(ManagementBridgeProtocol.Parse(allianceAssets, id, "alliance-assets", 7, 1)
                .GetProperty("resources").GetProperty("avorion").GetInt64() == 700,
            "bridge accepts validated alliance Credits and all seven resources");
        Reject(allianceAssets.Replace("\"index\":7", "\"index\":8"), id, "alliance-assets",
            "bridge rejects alliance assets for another alliance", 7, 1);
        Reject(allianceAssets.Replace("\"credits\":500000", "\"credits\":-1"), id, "alliance-assets",
            "bridge rejects negative alliance assets", 7, 1);

        var allianceRewardGrant = new AllianceRewardGrant(3, new AllianceResourceBalances(0, 4, 0, 0, 0, 0, 0));
        var allianceGrant = Frame(new { protocolVersion = 1, componentVersion = "0.7.0", requestId = id,
            action = "alliance-grant", source = "avorion-lua-api", readOnly = false,
            alliance = new { index = 7, name = "星海联合", online = false }, accepted = true,
            grant = new { credits = 3L, resources = new { iron = 0L, titanium = 4L, naonite = 0L,
                trinium = 0L, xanion = 0L, ogonite = 0L, avorion = 0L } },
            before = new { credits = 500000L, resources = new { iron = 100L, titanium = 200L, naonite = 300L,
                trinium = 400L, xanion = 500L, ogonite = 600L, avorion = 700L } },
            after = new { credits = 500003L, resources = new { iron = 100L, titanium = 204L, naonite = 300L,
                trinium = 400L, xanion = 500L, ogonite = 600L, avorion = 700L } } });
        check(ManagementBridgeProtocol.Parse(allianceGrant, id, "alliance-grant", 7, 0, null, allianceRewardGrant)
                .GetProperty("accepted").GetBoolean(),
            "bridge accepts an exact alliance reward with proven before and after balances");
        Reject(allianceGrant.Replace("\"credits\":500003", "\"credits\":500004"), id, "alliance-grant",
            "bridge rejects an alliance reward with an inexact balance change", 7, 0, null, allianceRewardGrant);
        Reject(allianceGrant.Replace("\"titanium\":4", "\"titanium\":5"), id, "alliance-grant",
            "bridge rejects an alliance reward acknowledgement for different amounts", 7, 0, null, allianceRewardGrant);
        Reject(allianceGrant.Replace("\"readOnly\":false", "\"readOnly\":true"), id, "alliance-grant",
            "bridge requires explicit write semantics for alliance rewards", 7, 0, null, allianceRewardGrant);
        Reject(allianceGrant.Replace("0.7.0", "0.6.0"), id, "alliance-grant",
            "older bridge versions cannot claim alliance rewards", 7, 0, null, allianceRewardGrant);

        var validAllianceReward = new AllianceRewardRequest(allianceRewardGrant, "GRANT ALLIANCE 7");
        check(AllianceRewardPolicy.Validate(validAllianceReward) is null,
            "alliance reward policy accepts a bounded non-empty grant");
        check(AllianceRewardPolicy.Validate(validAllianceReward with { Grant = allianceRewardGrant with { Credits = -1 } })?.Code == "ALLIANCE_REWARD_LIMIT",
            "alliance reward policy rejects negative Credits");
        check(AllianceRewardPolicy.Validate(validAllianceReward with {
                Grant = allianceRewardGrant with { Resources = allianceRewardGrant.Resources with { Titanium = AllianceRewardPolicy.MaximumResourcePerGrant + 1 } }
            })?.Code == "ALLIANCE_REWARD_LIMIT",
            "alliance reward policy enforces the per-resource maximum");
        check(AllianceRewardPolicy.Validate(validAllianceReward with {
                Grant = new AllianceRewardGrant(0, new AllianceResourceBalances(0, 0, 0, 0, 0, 0, 0))
            })?.Code == "ALLIANCE_REWARD_EMPTY",
            "alliance reward policy rejects empty grants");
        var unload = Frame(new { protocolVersion = 1, componentVersion = "0.4.0", requestId = id,
            action = "unload", source = "avorion-lua-api", readOnly = false, x = -4, y = 12, playerCount = 0, accepted = true });
        check(ManagementBridgeProtocol.Parse(unload, id, "unload", -4, 12).GetProperty("accepted").GetBoolean(),
            "bridge accepts an exact guarded empty-sector unload acknowledgement");
        Reject(unload.Replace("\"playerCount\":0", "\"playerCount\":1"), id, "unload", "bridge rejects unload acknowledgements with players");
        Reject(unload.Replace("\"readOnly\":false", "\"readOnly\":true"), id, "unload", "bridge requires explicit write semantics for unload acknowledgements");
    }

    public static async Task CheckTransportAsync(Action<bool, string> check)
    {
        // A local protocol fixture is not a game/player fixture: exercise split packets and UTF-8 bytes.
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            var expected = "ORION_BEGIN:{\"name\":\"中文\"}:ORION_END";
            async Task<int> ReadId(Stream stream)
            {
                var bytes = new byte[4];
                await stream.ReadExactlyAsync(bytes, deadline.Token);
                var size = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(bytes);
                var body = new byte[size];
                await stream.ReadExactlyAsync(body, deadline.Token);
                return System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(body);
            }
            async Task Send(Stream stream, int id, int type, byte[] body)
            {
                var packet = new byte[body.Length + 14];
                System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(packet, body.Length + 10);
                System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(4), id);
                System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(8), type);
                body.CopyTo(packet, 12);
                await stream.WriteAsync(packet, deadline.Token);
            }
            var serve = Task.Run(async () =>
            {
                using var socket = await listener.AcceptTcpClientAsync(deadline.Token);
                await using var stream = socket.GetStream();
                var auth = await ReadId(stream);
                await Send(stream, auth, 2, []);
                var request = await ReadId(stream);
                var bytes = System.Text.Encoding.UTF8.GetBytes(expected);
                var split = Array.IndexOf(bytes, (byte)0xe4) + 1;
                await Send(stream, request, 0, bytes[..7]);
                await Send(stream, request, 0, bytes[7..split]);
                await Send(stream, request, 0, bytes[split..]);
            });
            var actual = await SourceRconProbe.QueryBridgeAsync(System.Net.IPAddress.Loopback, port, "test-only-password", "/orionadmin hello 0123456789abcdef0123456789abcdef", deadline.Token);
            await serve;
            check(actual == expected, "RCON bridge reassembles split frame markers and Chinese UTF-8 bytes");
        }
        finally { listener.Stop(); }
    }
}
