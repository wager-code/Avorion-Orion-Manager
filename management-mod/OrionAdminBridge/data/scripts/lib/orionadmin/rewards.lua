OrionAdminRewards = OrionAdminRewards or {}

local MAX_SAFE_INTEGER = 9007199254740991
local MAX_CREDITS_GRANT = 1000000000
local MAX_RESOURCE_GRANT = 100000000

local function findPlayer(playerIndex)
    for _, player in ipairs({Server():getPlayers()}) do
        if player.index == playerIndex then return player end
    end
    return nil
end

local function validAmount(value, maximum)
    return type(value) == "number" and value % 1 == 0 and value >= 0 and value <= maximum
end

local function parseGrant(payload)
    if type(payload) ~= "string" or #payload < 15 or #payload > 160 or
        not payload:match("^%d+,%d+,%d+,%d+,%d+,%d+,%d+,%d+$") then
        return nil, "ORION_REWARD_INVALID"
    end
    local values = {}
    for value in payload:gmatch("[^,]+") do values[#values + 1] = tonumber(value) end
    if #values ~= 8 or not validAmount(values[1], MAX_CREDITS_GRANT) then
        return nil, "ORION_REWARD_INVALID"
    end
    local total = values[1]
    for index = 2, 8 do
        if not validAmount(values[index], MAX_RESOURCE_GRANT) then
            return nil, "ORION_REWARD_INVALID"
        end
        total = total + values[index]
    end
    if total <= 0 then return nil, "ORION_REWARD_EMPTY" end
    return values, nil
end

local function readAssets(player)
    local iron, titanium, naonite, trinium, xanion, ogonite, avorion = player:getResources()
    return {player.money, iron, titanium, naonite, trinium, xanion, ogonite, avorion}
end

local function safeBalances(values)
    for _, value in ipairs(values) do
        if type(value) ~= "number" or value % 1 ~= 0 or value < 0 or value > MAX_SAFE_INTEGER then
            return false
        end
    end
    return true
end

local function assetsJson(values)
    return '"credits":' .. tostring(values[1]) .. ',"resources":{' ..
        '"iron":' .. tostring(values[2]) .. ',"titanium":' .. tostring(values[3]) ..
        ',"naonite":' .. tostring(values[4]) .. ',"trinium":' .. tostring(values[5]) ..
        ',"xanion":' .. tostring(values[6]) .. ',"ogonite":' .. tostring(values[7]) ..
        ',"avorion":' .. tostring(values[8]) .. '}'
end

function OrionAdminRewards.grant(playerIndex, payload)
    local grant, grantError = parseGrant(payload)
    if grantError then return false, grantError end
    local player = findPlayer(playerIndex)
    if not player then return false, "ORION_PLAYER_NOT_FOUND" end
    if type(player.name) ~= "string" or #player.name == 0 or #player.name > 512 then
        return false, "ORION_REWARD_INVALID_PLAYER"
    end

    local before = readAssets(player)
    if not safeBalances(before) then return false, "ORION_REWARD_BALANCE_INVALID" end
    for index = 1, 8 do
        if before[index] + grant[index] > MAX_SAFE_INTEGER then
            return false, "ORION_REWARD_LIMIT"
        end
    end

    local applied, applyError = pcall(function()
        player:receive("OrionAdmin administrator reward.", grant[1], grant[2], grant[3],
            grant[4], grant[5], grant[6], grant[7], grant[8])
    end)
    if not applied then
        print("[OrionAdminBridge] player reward failed: " .. tostring(applyError))
        return false, "ORION_REWARD_FAILED"
    end

    local after = readAssets(player)
    if not safeBalances(after) then return false, "ORION_REWARD_VERIFY_FAILED" end
    for index = 1, 8 do
        if after[index] ~= before[index] + grant[index] then
            return false, "ORION_REWARD_VERIFY_FAILED"
        end
    end

    return true, '"player":{"index":' .. tostring(player.index) ..
        ',"name":' .. OrionAdminProtocol.quote(player.name) ..
        ',"online":' .. tostring(Server():isOnline(player.index)) .. '},' ..
        '"accepted":true,"grant":{' .. assetsJson(grant) .. '},' ..
        '"before":{' .. assetsJson(before) .. '},"after":{' .. assetsJson(after) .. '}'
end
