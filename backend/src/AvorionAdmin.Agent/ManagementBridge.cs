using System.Globalization;
using System.Net.Sockets;
using System.Text.Json;
using AvorionAdmin.Core.Models;

namespace AvorionAdmin.Agent;

public static class ManagementBridgeProtocol
{
    public const string Begin = "ORION_BEGIN:";
    public const string End = ":ORION_END";

    public static JsonElement Parse(
        string response,
        string requestId,
        string action,
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
        var start = response.IndexOf(Begin, StringComparison.Ordinal);
        var end = response.LastIndexOf(End, StringComparison.Ordinal);
        if (response.Length > 32768 || start < 0 || end <= start)
            throw new InvalidDataException("Missing or ambiguous bridge frame");
        using var json = JsonDocument.Parse(response[(start + Begin.Length)..end]);
        var root = json.RootElement;
        var componentVersion = root.GetProperty("componentVersion").GetString();
        if (root.GetProperty("protocolVersion").GetInt32() != 1 ||
            componentVersion is not ("0.4.0" or "0.5.0" or "0.6.0" or "0.7.0" or "0.8.0" or "0.9.0" or "0.10.0") ||
            root.GetProperty("requestId").GetString() != requestId ||
            root.GetProperty("action").GetString() != action ||
            root.GetProperty("source").GetString() != "avorion-lua-api" ||
            root.GetProperty("readOnly").GetBoolean() !=
                (action is not ("unload" or "player-grant" or "alliance-grant" or "player-mail" or
                    "player-system-grant" or "alliance-system-grant")))
            throw new InvalidDataException("Incompatible or uncorrelated bridge response");
        if (action == "hello")
        {
            if (!root.GetProperty("serverSideOnly").GetBoolean() ||
                !root.GetProperty("capabilities").EnumerateArray().Any(x => x.GetString() == "players.online") ||
                !root.GetProperty("capabilities").EnumerateArray().Any(x => x.GetString() == "players.position") ||
                !root.GetProperty("capabilities").EnumerateArray().Any(x => x.GetString() == "alliances.read") ||
                !root.GetProperty("capabilities").EnumerateArray().Any(x => x.GetString() == "alliances.members") ||
                !root.GetProperty("capabilities").EnumerateArray().Any(x => x.GetString() == "sectors.loaded") ||
                !root.GetProperty("capabilities").EnumerateArray().Any(x => x.GetString() == "sectors.unload-attempt") ||
                componentVersion is ("0.6.0" or "0.7.0" or "0.8.0" or "0.9.0" or "0.10.0") &&
                !root.GetProperty("capabilities").EnumerateArray().Any(x => x.GetString() == "players.reward.grant") ||
                componentVersion is ("0.7.0" or "0.8.0" or "0.9.0" or "0.10.0") &&
                (!root.GetProperty("capabilities").EnumerateArray().Any(x => x.GetString() == "alliances.assets") ||
                 !root.GetProperty("capabilities").EnumerateArray().Any(x => x.GetString() == "alliances.reward.grant")) ||
                componentVersion is ("0.8.0" or "0.9.0" or "0.10.0") &&
                (!root.GetProperty("capabilities").EnumerateArray().Any(x => x.GetString() == "players.known") ||
                 !root.GetProperty("capabilities").EnumerateArray().Any(x => x.GetString() == "players.mail.send")) ||
                componentVersion is ("0.9.0" or "0.10.0") &&
                (!root.GetProperty("capabilities").EnumerateArray().Any(x => x.GetString() == "players.inventory.read") ||
                 !root.GetProperty("capabilities").EnumerateArray().Any(x => x.GetString() == "players.inventory.system-upgrade.grant") ||
                 !root.GetProperty("capabilities").EnumerateArray().Any(x => x.GetString() == "alliances.inventory.read") ||
                  !root.GetProperty("capabilities").EnumerateArray().Any(x => x.GetString() == "alliances.inventory.system-upgrade.grant")) ||
                componentVersion == "0.10.0" &&
                !root.GetProperty("capabilities").EnumerateArray().Any(x => x.GetString() == "inventory.item-details.read"))
                throw new InvalidDataException("Bridge capability missing");
        }
        else if (action is "players" or "players-known" or "sectors" or "alliances")
        {
            var total = root.GetProperty("total").GetInt32();
            var items = root.GetProperty("items");
            if (total < 0 || root.GetProperty("offset").GetInt32() != offset ||
                root.GetProperty("limit").GetInt32() != limit ||
                items.GetArrayLength() != Math.Min(limit, Math.Max(0, total - offset)))
                throw new InvalidDataException("Invalid bridge page");
            if (action is "players" or "players-known")
            {
                var ids = new HashSet<int>();
                foreach (var player in items.EnumerateArray())
                {
                    if (!ids.Add(player.GetProperty("index").GetInt32()) ||
                        player.GetProperty("name").GetString() is not { Length: > 0 and <= 512 })
                        throw new InvalidDataException("Invalid player record");
                    var online = player.GetProperty("online").GetBoolean();
                    if (action == "players")
                    {
                        var x = player.GetProperty("x");
                        var y = player.GetProperty("y");
                        var positionValid = x.ValueKind == JsonValueKind.Null && y.ValueKind == JsonValueKind.Null ||
                            x.ValueKind == JsonValueKind.Number && y.ValueKind == JsonValueKind.Number &&
                            x.TryGetInt32(out var sectorX) && y.TryGetInt32(out var sectorY) &&
                            sectorX is >= -500 and <= 500 && sectorY is >= -500 and <= 500;
                        if (!online || !positionValid) throw new InvalidDataException("Invalid online player record");
                    }
                }
            }
            else if (action == "sectors")
            {
                var coordinates = new HashSet<(int X, int Y)>();
                foreach (var sector in items.EnumerateArray())
                {
                    var x = sector.GetProperty("x").GetInt32();
                    var y = sector.GetProperty("y").GetInt32();
                    var players = sector.GetProperty("playerCount").GetInt32();
                    if (!coordinates.Add((x, y)) || players < 0 || sector.GetProperty("eligible").GetBoolean() != (players == 0))
                        throw new InvalidDataException("Invalid sector record");
                }
            }
            else
            {
                var totalMembers = root.GetProperty("totalMembers").GetInt32();
                var totalOnlineMembers = root.GetProperty("totalOnlineMembers").GetInt32();
                var onlineAlliances = root.GetProperty("onlineAlliances").GetInt32();
                if (totalMembers < 0 || totalOnlineMembers is < 0 || totalOnlineMembers > totalMembers ||
                    onlineAlliances is < 0 || onlineAlliances > total)
                    throw new InvalidDataException("Invalid alliance aggregates");
                var ids = new HashSet<int>();
                var pageMembers = 0;
                var pageOnlineMembers = 0;
                var pageOnlineAlliances = 0;
                foreach (var alliance in items.EnumerateArray())
                {
                    ValidateAlliance(alliance, ids);
                    pageMembers += alliance.GetProperty("memberTotal").GetInt32();
                    pageOnlineMembers += alliance.GetProperty("onlineMembers").GetInt32();
                    if (alliance.GetProperty("online").GetBoolean()) pageOnlineAlliances++;
                }
                if (pageMembers > totalMembers || pageOnlineMembers > totalOnlineMembers || pageOnlineAlliances > onlineAlliances)
                    throw new InvalidDataException("Invalid alliance page aggregates");
            }
        }
        else if (action == "player-assets")
        {
            var player = root.GetProperty("player");
            if (player.GetProperty("index").GetInt32() != offset ||
                player.GetProperty("name").GetString() is not { Length: > 0 and <= 512 })
                throw new InvalidDataException("Invalid player asset identity");
            _ = player.GetProperty("online").GetBoolean();
            RequireNonNegativeSafeInteger(root.GetProperty("credits"), "credits");
            var resources = root.GetProperty("resources");
            foreach (var material in new[] { "iron", "titanium", "naonite", "trinium", "xanion", "ogonite", "avorion" })
                RequireNonNegativeSafeInteger(resources.GetProperty(material), material);
        }
        else if (action is "player-inventory" or "alliance-inventory")
        {
            if (componentVersion is not ("0.9.0" or "0.10.0"))
                throw new InvalidDataException("Unsupported inventory response");
            var ownerKind = action == "player-inventory" ? InventoryOwnerKinds.Player : InventoryOwnerKinds.Alliance;
            ValidateInventoryOwner(root.GetProperty("owner"), ownerKind, offset);
            var total = root.GetProperty("total").GetInt32();
            var occupiedSlots = root.GetProperty("occupiedSlots").GetInt32();
            var maxSlots = root.GetProperty("maxSlots").GetInt32();
            var items = root.GetProperty("items");
            if (total < 0 || occupiedSlots < 0 || maxSlots < occupiedSlots ||
                root.GetProperty("offset").GetInt32() != expectedInventoryOffset ||
                root.GetProperty("limit").GetInt32() != expectedInventoryLimit ||
                items.GetArrayLength() != Math.Min(expectedInventoryLimit, Math.Max(0, total - expectedInventoryOffset)))
                throw new InvalidDataException("Invalid inventory page");
            var slots = new HashSet<long>();
            foreach (var item in items.EnumerateArray())
            {
                var slot = item.GetProperty("slot").GetInt64();
                var amount = item.GetProperty("amount").GetInt32();
                var itemType = item.GetProperty("itemType").GetString();
                if (slot is < 0 or > uint.MaxValue || !slots.Add(slot) || amount is < 1 or > 100_000_000 ||
                    itemType is not ("turret" or "turret-template" or "system-upgrade" or "vanilla-item" or "usable-item" or "unknown") ||
                    item.GetProperty("name").GetString() is not { Length: > 0 and <= 512 })
                    throw new InvalidDataException("Invalid inventory item");
                var rarity = item.GetProperty("rarity");
                if (rarity.ValueKind != JsonValueKind.Null &&
                    (!rarity.TryGetInt32(out var rarityValue) || rarityValue is < -1 or > 5))
                    throw new InvalidDataException("Invalid inventory rarity");
                var script = item.GetProperty("script");
                var seed = item.GetProperty("seed");
                if (itemType == "system-upgrade")
                {
                    if (script.GetString() is not { Length: > 0 and <= 512 } ||
                        seed.GetString() is not { Length: > 0 and <= 128 })
                        throw new InvalidDataException("Invalid system upgrade inventory item");
                }
                else if (script.ValueKind != JsonValueKind.Null || seed.ValueKind != JsonValueKind.Null)
                    throw new InvalidDataException("Unexpected inventory item implementation details");
                if (componentVersion == "0.10.0")
                    ValidateInventoryItemDetails(item, itemType!);
            }
        }
        else if (action == "player-grant")
        {
            if (componentVersion is not ("0.6.0" or "0.7.0" or "0.8.0" or "0.9.0" or "0.10.0") || expectedGrant is null ||
                !root.GetProperty("accepted").GetBoolean())
                throw new InvalidDataException("Unsupported or uncorrelated player reward");
            ValidatePlayerIdentity(root.GetProperty("player"), offset);
            ValidateGrant(root.GetProperty("grant"), expectedGrant);
            var before = root.GetProperty("before");
            var after = root.GetProperty("after");
            var beforeValues = ReadAssetValues(before);
            var afterValues = ReadAssetValues(after);
            var grantValues = RewardValues(expectedGrant);
            for (var index = 0; index < beforeValues.Length; index++)
            {
                if (beforeValues[index] > 9_007_199_254_740_991 - grantValues[index] ||
                    afterValues[index] != beforeValues[index] + grantValues[index])
                    throw new InvalidDataException("Player reward acknowledgement did not prove the exact asset change");
            }
        }
        else if (action == "alliance-assets")
        {
            ValidateAllianceAssetIdentity(root.GetProperty("alliance"), offset);
            _ = ReadAssetValues(root);
        }
        else if (action == "alliance-grant")
        {
            if (componentVersion is not ("0.7.0" or "0.8.0" or "0.9.0" or "0.10.0") || expectedAllianceGrant is null ||
                !root.GetProperty("accepted").GetBoolean())
                throw new InvalidDataException("Unsupported or uncorrelated alliance reward");
            ValidateAllianceAssetIdentity(root.GetProperty("alliance"), offset);
            ValidateAllianceGrant(root.GetProperty("grant"), expectedAllianceGrant);
            var beforeValues = ReadAssetValues(root.GetProperty("before"));
            var afterValues = ReadAssetValues(root.GetProperty("after"));
            var grantValues = AllianceRewardValues(expectedAllianceGrant);
            for (var index = 0; index < beforeValues.Length; index++)
            {
                if (beforeValues[index] > 9_007_199_254_740_991 - grantValues[index] ||
                    afterValues[index] != beforeValues[index] + grantValues[index])
                    throw new InvalidDataException("Alliance reward acknowledgement did not prove the exact asset change");
            }
        }
        else if (action == "player-mail")
        {
            if (componentVersion is not ("0.8.0" or "0.9.0" or "0.10.0") || expectedGrant is null ||
                expectedDeliveryId is null || expectedDeliveryId.Length != 32 ||
                !expectedDeliveryId.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f') ||
                !root.GetProperty("accepted").GetBoolean())
                throw new InvalidDataException("Unsupported or uncorrelated player mail");
            ValidatePlayerIdentity(root.GetProperty("player"), offset);
            ValidateGrant(root.GetProperty("grant"), expectedGrant);
            if (root.GetProperty("deliveryId").GetString() != expectedDeliveryId ||
                root.GetProperty("mailId").GetString() != $"orionadmin-{expectedDeliveryId}" ||
                !root.GetProperty("mailIndex").TryGetInt64(out var mailIndex) || mailIndex < 0)
                throw new InvalidDataException("Player mail acknowledgement identity mismatch");
            _ = root.GetProperty("replayed").GetBoolean();
        }
        else if (action is "player-system-grant" or "alliance-system-grant")
        {
            if (componentVersion is not ("0.9.0" or "0.10.0") || expectedUpgrade is null || expectedRarity is null ||
                expectedSeed is null || !SystemUpgradeCatalog.Rarities.TryGetValue(expectedRarity, out var rarityValue) ||
                !root.GetProperty("accepted").GetBoolean())
                throw new InvalidDataException("Unsupported or uncorrelated system upgrade grant");
            var ownerKind = action == "player-system-grant" ? InventoryOwnerKinds.Player : InventoryOwnerKinds.Alliance;
            ValidateInventoryOwner(root.GetProperty("owner"), ownerKind, offset);
            var upgrade = root.GetProperty("upgrade");
            if (upgrade.GetProperty("key").GetString() != expectedUpgrade.Key ||
                upgrade.GetProperty("name").GetString() is not { Length: > 0 and <= 512 } ||
                upgrade.GetProperty("script").GetString() != expectedUpgrade.Script ||
                upgrade.GetProperty("rarity").GetString() != expectedRarity ||
                upgrade.GetProperty("rarityValue").GetInt32() != rarityValue ||
                upgrade.GetProperty("requestSeed").GetString() != expectedSeed ||
                upgrade.GetProperty("seed").GetString() is not { Length: > 0 and <= 128 })
                throw new InvalidDataException("System upgrade grant identity mismatch");
            var beforeCount = root.GetProperty("beforeCount").GetInt32();
            var afterCount = root.GetProperty("afterCount").GetInt32();
            var slot = root.GetProperty("slot").GetInt64();
            if (beforeCount < 0 || afterCount != beforeCount + 1 || slot is < 0 or > uint.MaxValue)
                throw new InvalidDataException("System upgrade grant did not prove an exact inventory delta");
        }
        else if (action == "alliance")
        {
            var alliance = root.GetProperty("alliance");
            var ids = new HashSet<int>();
            ValidateAlliance(alliance, ids);
            if (alliance.GetProperty("index").GetInt32() != offset || root.GetProperty("memberLimit").GetInt32() != limit)
                throw new InvalidDataException("Invalid alliance detail identity");
            var members = root.GetProperty("members");
            var total = alliance.GetProperty("memberTotal").GetInt32();
            if (members.GetArrayLength() != Math.Min(limit, total))
                throw new InvalidDataException("Invalid alliance member page");
            var memberIds = new HashSet<int>();
            foreach (var member in members.EnumerateArray())
            {
                var x = member.GetProperty("x");
                var y = member.GetProperty("y");
                if (!memberIds.Add(member.GetProperty("index").GetInt32()) ||
                    member.GetProperty("name").GetString() is not { Length: > 0 and <= 512 } ||
                    member.GetProperty("rank").GetString() is not { Length: > 0 and <= 512 } ||
                    !ValidNullableSector(x, y))
                    throw new InvalidDataException("Invalid alliance member record");
                _ = member.GetProperty("online").GetBoolean();
            }
        }
        else if (action == "unload")
        {
            if (root.GetProperty("x").GetInt32() != offset || root.GetProperty("y").GetInt32() != limit ||
                root.GetProperty("playerCount").GetInt32() != 0 || !root.GetProperty("accepted").GetBoolean())
                throw new InvalidDataException("Invalid sector unload acknowledgement");
        }
        else throw new InvalidDataException("Unknown bridge action");
        return root.Clone();
    }

    private static void ValidateAlliance(JsonElement alliance, HashSet<int> ids)
    {
        var index = alliance.GetProperty("index").GetInt32();
        var leaderIndex = alliance.GetProperty("leaderIndex").GetInt32();
        var memberTotal = alliance.GetProperty("memberTotal").GetInt32();
        var onlineMembers = alliance.GetProperty("onlineMembers").GetInt32();
        if (index == 0 || leaderIndex == 0 || !ids.Add(index) ||
            alliance.GetProperty("name").GetString() is not { Length: > 0 and <= 512 } ||
            alliance.GetProperty("leaderName").GetString() is not { Length: > 0 and <= 512 } ||
            memberTotal < 1 || onlineMembers is < 0 || onlineMembers > memberTotal ||
            alliance.GetProperty("numCrafts").GetInt32() < 0 || alliance.GetProperty("numStations").GetInt32() < 0 ||
            !ValidNullableSector(alliance.GetProperty("homeX"), alliance.GetProperty("homeY")))
            throw new InvalidDataException("Invalid alliance record");
        _ = alliance.GetProperty("online").GetBoolean();
    }

    private static bool ValidNullableSector(JsonElement x, JsonElement y) =>
        x.ValueKind == JsonValueKind.Null && y.ValueKind == JsonValueKind.Null ||
        x.ValueKind == JsonValueKind.Number && y.ValueKind == JsonValueKind.Number &&
        x.TryGetInt32(out var sectorX) && y.TryGetInt32(out var sectorY) &&
        sectorX is >= -500 and <= 500 && sectorY is >= -500 and <= 500;

    private static void RequireNonNegativeSafeInteger(JsonElement value, string field)
    {
        if (!value.TryGetInt64(out var amount) || amount is < 0 or > 9_007_199_254_740_991)
            throw new InvalidDataException($"Invalid player asset amount: {field}");
    }

    private static void ValidatePlayerIdentity(JsonElement player, int expectedIndex)
    {
        if (player.GetProperty("index").GetInt32() != expectedIndex ||
            player.GetProperty("name").GetString() is not { Length: > 0 and <= 512 })
            throw new InvalidDataException("Invalid player reward identity");
        _ = player.GetProperty("online").GetBoolean();
    }

    private static void ValidateAllianceAssetIdentity(JsonElement alliance, int expectedIndex)
    {
        if (alliance.GetProperty("index").GetInt32() != expectedIndex ||
            alliance.GetProperty("name").GetString() is not { Length: > 0 and <= 512 })
            throw new InvalidDataException("Invalid alliance asset identity");
        _ = alliance.GetProperty("online").GetBoolean();
    }

    private static void ValidateInventoryOwner(JsonElement owner, string expectedKind, int expectedIndex)
    {
        if (owner.GetProperty("kind").GetString() != expectedKind ||
            owner.GetProperty("index").GetInt32() != expectedIndex ||
            owner.GetProperty("name").GetString() is not { Length: > 0 and <= 512 })
            throw new InvalidDataException("Invalid inventory owner identity");
        _ = owner.GetProperty("online").GetBoolean();
    }

    private static void ValidateInventoryItemDetails(JsonElement item, string itemType)
    {
        RequireNullableString(item.GetProperty("title"), 512, "title");
        RequireNullableString(item.GetProperty("icon"), 512, "icon");
        RequireNullableChoice(item.GetProperty("weaponType"),
            ["ChainGun", "PointDefenseChainGun", "PointDefenseLaser", "Laser", "MiningLaser",
             "RawMiningLaser", "SalvagingLaser", "RawSalvagingLaser", "PlasmaGun", "RocketLauncher",
             "Cannon", "RailGun", "RepairBeam", "Bolter", "LightningGun", "TeslaGun", "ForceGun",
             "PulseCannon", "AntiFighter"], "weaponType");
        RequireNullableChoice(item.GetProperty("weaponCategory"),
            ["armed", "mining", "salvaging", "heal", "none"], "weaponCategory");
        RequireNullableChoice(item.GetProperty("turretSlotType"),
            ["unspecified", "armed", "unarmed", "point-defense"], "turretSlotType");
        RequireNullableInteger(item.GetProperty("material"), 0, 6, "material");
        RequireNullableInteger(item.GetProperty("averageTech"), 0, 10000, "averageTech");
        RequireNullableInteger(item.GetProperty("maxTech"), 0, 10000, "maxTech");
        RequireNullableNumber(item.GetProperty("dps"), 0, 1_000_000_000_000_000, "dps");
        RequireNullableNumber(item.GetProperty("damage"), 0, 1_000_000_000_000_000, "damage");
        RequireNullableNumber(item.GetProperty("reach"), 0, 1_000_000_000, "reach");
        RequireNullableNumber(item.GetProperty("fireRate"), 0, 1_000_000, "fireRate");
        RequireNullableNumber(item.GetProperty("accuracy"), 0, 100, "accuracy");
        RequireNullableInteger(item.GetProperty("turretSlots"), 1, 1000, "turretSlots");
        RequireNullableNumber(item.GetProperty("size"), 0, 1000, "size");
        RequireNullableInteger(item.GetProperty("numWeapons"), 1, 1000, "numWeapons");
        foreach (var field in new[] { "armed", "civil", "coaxial", "seeker", "continuousBeam" })
        {
            var value = item.GetProperty(field);
            if (value.ValueKind is not (JsonValueKind.Null or JsonValueKind.True or JsonValueKind.False))
                throw new InvalidDataException($"Invalid inventory item detail: {field}");
        }

        if (itemType is not ("turret" or "turret-template"))
        {
            foreach (var field in new[] { "title", "weaponType", "weaponCategory", "turretSlotType", "material",
                         "averageTech", "maxTech", "dps", "damage", "reach", "fireRate", "accuracy",
                         "turretSlots", "size", "numWeapons", "armed", "civil", "coaxial", "seeker", "continuousBeam" })
                if (item.GetProperty(field).ValueKind != JsonValueKind.Null)
                    throw new InvalidDataException("Unexpected turret details on another inventory item type");
        }
    }

    private static void RequireNullableString(JsonElement value, int maximumLength, string field)
    {
        if (value.ValueKind == JsonValueKind.Null) return;
        if (value.ValueKind != JsonValueKind.String || value.GetString() is not { Length: > 0 } text || text.Length > maximumLength)
            throw new InvalidDataException($"Invalid inventory item detail: {field}");
    }

    private static void RequireNullableChoice(JsonElement value, string[] choices, string field)
    {
        if (value.ValueKind == JsonValueKind.Null) return;
        if (value.ValueKind != JsonValueKind.String || value.GetString() is not { } text || !choices.Contains(text, StringComparer.Ordinal))
            throw new InvalidDataException($"Invalid inventory item detail: {field}");
    }

    private static void RequireNullableInteger(JsonElement value, int minimum, int maximum, string field)
    {
        if (value.ValueKind == JsonValueKind.Null) return;
        if (!value.TryGetInt32(out var number) || number < minimum || number > maximum)
            throw new InvalidDataException($"Invalid inventory item detail: {field}");
    }

    private static void RequireNullableNumber(JsonElement value, double minimum, double maximum, string field)
    {
        if (value.ValueKind == JsonValueKind.Null) return;
        if (!value.TryGetDouble(out var number) || !double.IsFinite(number) || number < minimum || number > maximum)
            throw new InvalidDataException($"Invalid inventory item detail: {field}");
    }

    private static void ValidateGrant(JsonElement value, PlayerRewardGrant expected)
    {
        var actual = ReadAssetValues(value);
        if (!actual.SequenceEqual(RewardValues(expected)))
            throw new InvalidDataException("Player reward acknowledgement amount mismatch");
    }

    private static void ValidateAllianceGrant(JsonElement value, AllianceRewardGrant expected)
    {
        if (!ReadAssetValues(value).SequenceEqual(AllianceRewardValues(expected)))
            throw new InvalidDataException("Alliance reward acknowledgement amount mismatch");
    }

    private static long[] ReadAssetValues(JsonElement value)
    {
        var resources = value.GetProperty("resources");
        var result = new[]
        {
            RequireNonNegativeSafeIntegerValue(value.GetProperty("credits"), "credits"),
            RequireNonNegativeSafeIntegerValue(resources.GetProperty("iron"), "iron"),
            RequireNonNegativeSafeIntegerValue(resources.GetProperty("titanium"), "titanium"),
            RequireNonNegativeSafeIntegerValue(resources.GetProperty("naonite"), "naonite"),
            RequireNonNegativeSafeIntegerValue(resources.GetProperty("trinium"), "trinium"),
            RequireNonNegativeSafeIntegerValue(resources.GetProperty("xanion"), "xanion"),
            RequireNonNegativeSafeIntegerValue(resources.GetProperty("ogonite"), "ogonite"),
            RequireNonNegativeSafeIntegerValue(resources.GetProperty("avorion"), "avorion")
        };
        return result;
    }

    private static long RequireNonNegativeSafeIntegerValue(JsonElement value, string field)
    {
        RequireNonNegativeSafeInteger(value, field);
        return value.GetInt64();
    }

    private static long[] RewardValues(PlayerRewardGrant grant) =>
    [
        grant.Credits,
        grant.Resources.Iron,
        grant.Resources.Titanium,
        grant.Resources.Naonite,
        grant.Resources.Trinium,
        grant.Resources.Xanion,
        grant.Resources.Ogonite,
        grant.Resources.Avorion
    ];

    private static long[] AllianceRewardValues(AllianceRewardGrant grant) =>
    [
        grant.Credits,
        grant.Resources.Iron,
        grant.Resources.Titanium,
        grant.Resources.Naonite,
        grant.Resources.Trinium,
        grant.Resources.Xanion,
        grant.Resources.Ogonite,
        grant.Resources.Avorion
    ];

    internal static string[] RewardValuesForCommand(PlayerRewardGrant grant) =>
        RewardValues(grant)
            .Select(value => value.ToString(CultureInfo.InvariantCulture))
            .ToArray();

    internal static string[] AllianceRewardValuesForCommand(AllianceRewardGrant grant) =>
        AllianceRewardValues(grant)
            .Select(value => value.ToString(CultureInfo.InvariantCulture))
            .ToArray();

    internal static string EncodeMailText(string value) =>
        Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(value)).ToLowerInvariant();
}

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
