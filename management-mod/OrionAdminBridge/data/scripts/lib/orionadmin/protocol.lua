OrionAdminProtocol = OrionAdminProtocol or {}

local capabilities = {
    "bridge.hello",
    "players.online",
    "players.position",
    "players.assets",
    "players.reward.grant",
    "players.known",
    "players.mail.send",
    "players.inventory.read",
    "players.inventory.system-upgrade.grant",
    "alliances.read",
    "alliances.members",
    "alliances.assets",
    "alliances.reward.grant",
    "alliances.inventory.read",
    "alliances.inventory.system-upgrade.grant",
    "inventory.item-details.read",
    "sectors.loaded",
    "sectors.unload-attempt"
}

function OrionAdminProtocol.quote(value)
    return '"' .. tostring(value):gsub('[%z\1-\31\\"]', function(c)
        return string.format('\\u%04x', string.byte(c))
    end) .. '"'
end

function OrionAdminProtocol.response(requestId, action, readOnly, payload)
    return 'ORION_BEGIN:{"protocolVersion":1,"componentVersion":"0.10.0",' ..
        '"requestId":' .. OrionAdminProtocol.quote(requestId) .. ',"action":' ..
        OrionAdminProtocol.quote(action) ..
        ',"source":"avorion-lua-api","readOnly":' .. tostring(readOnly) .. ',' ..
        payload .. '}:ORION_END'
end

function OrionAdminProtocol.validSector(x, y)
    return type(x) == "number" and type(y) == "number" and
        x % 1 == 0 and y % 1 == 0 and
        x >= -500 and x <= 500 and y >= -500 and y <= 500
end

function OrionAdminProtocol.nullableSector(x, y)
    if OrionAdminProtocol.validSector(x, y) then
        return '"x":' .. tostring(x) .. ',"y":' .. tostring(y)
    end
    return '"x":null,"y":null'
end

function OrionAdminProtocol.validateRequest(requestId, extraArgumentCount)
    if type(requestId) ~= "string" or #requestId ~= 32 or
        not requestId:match('^[a-f0-9]+$') then
        return "ORION_INVALID_REQUEST_ID"
    end
    if extraArgumentCount ~= 0 then return "ORION_INVALID_ARGUMENTS" end
    return nil
end

function OrionAdminProtocol.isReadAction(action)
    return action == "players" or action == "players-known" or action == "sectors" or
        action == "alliances" or action == "alliance"
end

function OrionAdminProtocol.parsePlayerIndex(indexText, sentinelText)
    local index, sentinel = tonumber(indexText), tonumber(sentinelText)
    if not index or index % 1 ~= 0 or index < 1 or index > 100000 or sentinel ~= 1 then
        return nil, "ORION_INVALID_PLAYER"
    end
    return index, nil
end

function OrionAdminProtocol.parseAllianceIndex(indexText, sentinelText)
    local index, sentinel = tonumber(indexText), tonumber(sentinelText)
    if not index or index % 1 ~= 0 or index < 1 or index > 100000 or sentinel ~= 1 then
        return nil, "ORION_INVALID_ALLIANCE"
    end
    return index, nil
end

function OrionAdminProtocol.parsePage(action, offsetText, limitText)
    local offset, limit = tonumber(offsetText), tonumber(limitText)
    local maxLimit = action == "sectors" and 50 or action == "players-known" and 50 or action == "alliance" and 20 or 10
    if not offset or not limit or offset % 1 ~= 0 or limit % 1 ~= 0 or
        (action ~= "alliance" and (offset < 0 or offset > 100000)) or
        (action == "alliance" and offset == 0) or
        limit < 1 or limit > maxLimit then
        return nil, nil, "ORION_INVALID_PAGE"
    end
    return offset, limit, nil
end

function OrionAdminProtocol.capabilitiesJson()
    local encoded = {}
    for _, capability in ipairs(capabilities) do
        encoded[#encoded + 1] = OrionAdminProtocol.quote(capability)
    end
    return '[' .. table.concat(encoded, ',') .. ']'
end
