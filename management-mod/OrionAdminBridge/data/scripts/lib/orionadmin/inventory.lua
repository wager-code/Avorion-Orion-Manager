OrionAdminInventory = OrionAdminInventory or {}

include("data/scripts/lib/weapontypeutility")

local whitelist = {
    ["battery-booster"] = {script = "data/scripts/systems/batterybooster.lua", name = "Battery Booster"},
    ["cargo-extension"] = {script = "data/scripts/systems/cargoextension.lua", name = "Cargo Extension"},
    ["energy-booster"] = {script = "data/scripts/systems/energybooster.lua", name = "Energy Booster"},
    ["engine-booster"] = {script = "data/scripts/systems/enginebooster.lua", name = "Engine Booster"},
    ["hyperspace-booster"] = {script = "data/scripts/systems/hyperspacebooster.lua", name = "Hyperspace Booster"},
    ["radar-booster"] = {script = "data/scripts/systems/radarbooster.lua", name = "Radar Booster"},
    ["scanner-booster"] = {script = "data/scripts/systems/scannerbooster.lua", name = "Scanner Booster"},
    ["shield-booster"] = {script = "data/scripts/systems/shieldbooster.lua", name = "Shield Booster"},
    ["mining-system"] = {script = "data/scripts/systems/miningsystem.lua", name = "Mining System"},
    ["trading-system"] = {script = "data/scripts/systems/tradingoverview.lua", name = "Trading System"},
    ["valuables-detector"] = {script = "data/scripts/systems/valuablesdetector.lua", name = "Valuables Detector"}
}

local rarities = {
    common = {value = RarityType.Common, name = "Common"},
    uncommon = {value = RarityType.Uncommon, name = "Uncommon"},
    rare = {value = RarityType.Rare, name = "Rare"},
    exceptional = {value = RarityType.Exceptional, name = "Exceptional"},
    exotic = {value = RarityType.Exotic, name = "Exotic"},
    legendary = {value = RarityType.Legendary, name = "Legendary"}
}

local function findPlayer(index)
    for _, player in ipairs({Server():getPlayers()}) do
        if player.index == index then return player end
    end
    return nil
end

local function findOwner(kind, index)
    if kind == "player" then
        local player = findPlayer(index)
        if not player then return nil, "ORION_PLAYER_NOT_FOUND" end
        return player, nil
    end
    if kind == "alliance" then
        if not Galaxy():allianceFactionExists(index) then return nil, "ORION_ALLIANCE_NOT_FOUND" end
        return Alliance(index), nil
    end
    return nil, "ORION_INVENTORY_OWNER_INVALID"
end

local function identityJson(kind, owner)
    if type(owner.name) ~= "string" or #owner.name == 0 or #owner.name > 512 then
        error("invalid inventory owner name")
    end
    return '"owner":{"kind":' .. OrionAdminProtocol.quote(kind) ..
        ',"index":' .. tostring(owner.index) ..
        ',"name":' .. OrionAdminProtocol.quote(owner.name) ..
        ',"online":' .. tostring(Server():isOnline(owner.index)) .. '}'
end

local function itemTypeName(itemType)
    if itemType == InventoryItemType.Turret then return "turret" end
    if itemType == InventoryItemType.TurretTemplate then return "turret-template" end
    if itemType == InventoryItemType.SystemUpgrade then return "system-upgrade" end
    if itemType == InventoryItemType.VanillaItem then return "vanilla-item" end
    if itemType == InventoryItemType.UsableItem then return "usable-item" end
    return "unknown"
end

local function optionalRarity(item)
    local ok, value = pcall(function() return item.rarity.type end)
    if not ok or type(value) ~= "number" or value % 1 ~= 0 or value < -1 or value > 5 then return "null" end
    return tostring(value)
end

local function optionalValue(item, key)
    local ok, value = pcall(function() return item[key] end)
    if not ok then return nil end
    return value
end

local function optionalNestedValue(item, key, nestedKey)
    local value = optionalValue(item, key)
    if value == nil then return nil end
    local ok, nested = pcall(function() return value[nestedKey] end)
    if not ok then return nil end
    return nested
end

local function nullableString(value, maximumLength)
    if type(value) ~= "string" or #value == 0 or #value > maximumLength then return "null" end
    return OrionAdminProtocol.quote(value)
end

local function nullableNumber(value, minimum, maximum, integer)
    if type(value) ~= "number" or value ~= value or value < minimum or value > maximum then return "null" end
    if integer and value % 1 ~= 0 then return "null" end
    return string.format("%.17g", value)
end

local function nullableBoolean(value)
    if type(value) ~= "boolean" then return "null" end
    return tostring(value)
end

local weaponTypeKeys = {}
for key, value in pairs(WeaponType) do weaponTypeKeys[value] = key end

local function optionalWeaponType(item)
    local ok, value = pcall(function() return WeaponTypes.getTypeOfItem(item) end)
    if not ok then return nil end
    return weaponTypeKeys[value]
end

local function optionalWeaponCategory(item)
    local value = optionalValue(item, "category")
    if value == WeaponCategory.Armed then return "armed" end
    if value == WeaponCategory.Mining then return "mining" end
    if value == WeaponCategory.Salvaging then return "salvaging" end
    if value == WeaponCategory.Heal then return "heal" end
    if value == WeaponCategory.None then return "none" end
    return nil
end

local function optionalTurretSlotType(item)
    local value = optionalValue(item, "slotType")
    if value == TurretSlotType.Unspecified then return "unspecified" end
    if value == TurretSlotType.Armed then return "armed" end
    if value == TurretSlotType.Unarmed then return "unarmed" end
    if value == TurretSlotType.PointDefense then return "point-defense" end
    return nil
end

local function itemDetailsJson(item)
    local itemType = item.itemType
    local turret = itemType == InventoryItemType.Turret or itemType == InventoryItemType.TurretTemplate
    local icon = turret and optionalValue(item, "weaponIcon") or optionalValue(item, "icon")
    if not turret then
        return '"title":null,"icon":' .. nullableString(icon, 512) ..
            ',"weaponType":null,"weaponCategory":null,"turretSlotType":null,' ..
            '"material":null,"averageTech":null,"maxTech":null,"dps":null,"damage":null,' ..
            '"reach":null,"fireRate":null,"accuracy":null,"turretSlots":null,"size":null,' ..
            '"numWeapons":null,"armed":null,"civil":null,"coaxial":null,"seeker":null,' ..
            '"continuousBeam":null'
    end
    return '"title":' .. nullableString(optionalValue(item, "title"), 512) ..
        ',"icon":' .. nullableString(icon, 512) ..
        ',"weaponType":' .. nullableString(optionalWeaponType(item), 64) ..
        ',"weaponCategory":' .. nullableString(optionalWeaponCategory(item), 32) ..
        ',"turretSlotType":' .. nullableString(optionalTurretSlotType(item), 32) ..
        ',"material":' .. nullableNumber(optionalNestedValue(item, "material", "value"), 0, 6, true) ..
        ',"averageTech":' .. nullableNumber(optionalValue(item, "averageTech"), 0, 10000, true) ..
        ',"maxTech":' .. nullableNumber(optionalValue(item, "maxTech"), 0, 10000, true) ..
        ',"dps":' .. nullableNumber(optionalValue(item, "dps"), 0, 1000000000000000, false) ..
        ',"damage":' .. nullableNumber(optionalValue(item, "damage"), 0, 1000000000000000, false) ..
        ',"reach":' .. nullableNumber(optionalValue(item, "reach"), 0, 1000000000, false) ..
        ',"fireRate":' .. nullableNumber(optionalValue(item, "fireRate"), 0, 1000000, false) ..
        ',"accuracy":' .. nullableNumber(optionalValue(item, "accuracy"), 0, 100, false) ..
        ',"turretSlots":' .. nullableNumber(optionalValue(item, "slots"), 1, 1000, true) ..
        ',"size":' .. nullableNumber(optionalValue(item, "size"), 0, 1000, false) ..
        ',"numWeapons":' .. nullableNumber(optionalValue(item, "numWeapons"), 1, 1000, true) ..
        ',"armed":' .. nullableBoolean(optionalValue(item, "armed")) ..
        ',"civil":' .. nullableBoolean(optionalValue(item, "civil")) ..
        ',"coaxial":' .. nullableBoolean(optionalValue(item, "coaxial")) ..
        ',"seeker":' .. nullableBoolean(optionalValue(item, "seeker")) ..
        ',"continuousBeam":' .. nullableBoolean(optionalValue(item, "continuousBeam"))
end

local function slotJson(inventory, slotIndex, slot)
    if type(slotIndex) ~= "number" or slotIndex % 1 ~= 0 or slotIndex < 0 or slotIndex > 4294967295 or
        type(slot) ~= "table" or not slot.item then
        error("invalid inventory slot")
    end
    local item = slot.item
    local name = tostring(item.name or "")
    if #name == 0 or #name > 512 then name = itemTypeName(item.itemType) end
    local amount = slot.amount
    if type(amount) ~= "number" then amount = inventory:amount(slotIndex) end
    if type(amount) ~= "number" or amount % 1 ~= 0 or amount < 1 or amount > 100000000 then
        error("invalid inventory amount")
    end
    local script, seed = "null", "null"
    if item.itemType == InventoryItemType.SystemUpgrade then
        if type(item.script) ~= "string" or #item.script == 0 or #item.script > 512 then
            error("invalid system upgrade script")
        end
        script = OrionAdminProtocol.quote(item.script)
        seed = OrionAdminProtocol.quote(tostring(item.seed))
    end
    return '{"slot":' .. tostring(slotIndex) ..
        ',"amount":' .. tostring(amount) ..
        ',"itemType":' .. OrionAdminProtocol.quote(itemTypeName(item.itemType)) ..
        ',"name":' .. OrionAdminProtocol.quote(name) ..
        ',"rarity":' .. optionalRarity(item) ..
        ',"script":' .. script .. ',"seed":' .. seed .. ',' .. itemDetailsJson(item) .. '}'
end

local function collectSlots(inventory)
    local slots = {}
    for slotIndex, slot in pairs(inventory:getItems()) do
        slots[#slots + 1] = {index = slotIndex, value = slot}
    end
    table.sort(slots, function(a, b) return a.index < b.index end)
    return slots
end

function OrionAdminInventory.query(kind, ownerIndex, offset, limit)
    local owner, ownerError = findOwner(kind, ownerIndex)
    if ownerError then error(ownerError) end
    local inventory = owner:getInventory()
    local slots = collectSlots(inventory)
    local items = {}
    for position = offset + 1, math.min(#slots, offset + limit) do
        items[#items + 1] = slotJson(inventory, slots[position].index, slots[position].value)
    end
    local occupied, maximum = inventory.occupiedSlots, inventory.maxSlots
    if type(occupied) ~= "number" or occupied % 1 ~= 0 or occupied < 0 or
        type(maximum) ~= "number" or maximum % 1 ~= 0 or maximum < occupied then
        error("invalid inventory capacity")
    end
    return identityJson(kind, owner) ..
        ',"total":' .. tostring(#slots) ..
        ',"occupiedSlots":' .. tostring(occupied) ..
        ',"maxSlots":' .. tostring(maximum) ..
        ',"offset":' .. tostring(offset) ..
        ',"limit":' .. tostring(limit) ..
        ',"items":[' .. table.concat(items, ',') .. ']'
end

local function parseGrant(payload)
    if type(payload) ~= "string" or #payload > 96 then return nil, "ORION_SYSTEM_GRANT_INVALID" end
    local key, rarityKey, seed = payload:match("^([a-z0-9%-]+),([a-z]+),([a-f0-9]+)$")
    local definition, rarity = whitelist[key], rarities[rarityKey]
    if not definition or not rarity or not seed or #seed ~= 32 then
        return nil, "ORION_SYSTEM_GRANT_INVALID"
    end
    return {key = key, definition = definition, rarityKey = rarityKey, rarity = rarity, seed = seed}, nil
end

local function matchingCount(inventory, grant, actualSeed)
    local count = 0
    for _, slot in pairs(inventory:getItemsByType(InventoryItemType.SystemUpgrade)) do
        local item = slot.item
        if item and item.script == grant.definition.script and
            item.rarity.type == grant.rarity.value and tostring(item.seed) == actualSeed then
            count = count + (slot.amount or 1)
        end
    end
    return count
end

function OrionAdminInventory.grantSystem(kind, ownerIndex, payload)
    local grant, grantError = parseGrant(payload)
    if grantError then return false, grantError end
    local owner, ownerError = findOwner(kind, ownerIndex)
    if ownerError then return false, ownerError end
    local inventory = owner:getInventory()
    local template = SystemUpgradeTemplate(grant.definition.script, Rarity(grant.rarity.value), Seed(grant.seed))
    local actualSeed = tostring(template.seed)
    if #actualSeed == 0 or #actualSeed > 128 then return false, "ORION_SYSTEM_GRANT_FAILED" end
    local beforeCount = matchingCount(inventory, grant, actualSeed)
    local ok, slotOrError = pcall(function() return inventory:add(template, true) end)
    if not ok or type(slotOrError) ~= "number" or slotOrError < 0 then
        print("[OrionAdminBridge] system upgrade grant failed: " .. tostring(slotOrError))
        return false, "ORION_SYSTEM_GRANT_FAILED"
    end
    local afterCount = matchingCount(inventory, grant, actualSeed)
    if afterCount ~= beforeCount + 1 then return false, "ORION_SYSTEM_GRANT_VERIFY_FAILED" end
    return true, identityJson(kind, owner) ..
        ',"accepted":true,"upgrade":{"key":' .. OrionAdminProtocol.quote(grant.key) ..
        ',"name":' .. OrionAdminProtocol.quote(grant.definition.name) ..
        ',"script":' .. OrionAdminProtocol.quote(grant.definition.script) ..
        ',"rarity":' .. OrionAdminProtocol.quote(grant.rarityKey) ..
        ',"rarityValue":' .. tostring(grant.rarity.value) ..
        ',"requestSeed":' .. OrionAdminProtocol.quote(grant.seed) ..
        ',"seed":' .. OrionAdminProtocol.quote(actualSeed) .. '},' ..
        '"beforeCount":' .. tostring(beforeCount) ..
        ',"afterCount":' .. tostring(afterCount) ..
        ',"slot":' .. tostring(slotOrError)
end
