OrionAdminDispatch = OrionAdminDispatch or {}

local function protectedQuery(label, callback, notFoundMarker, notFoundCode)
    local ok, payload = pcall(callback)
    if ok then return true, true, payload end
    if notFoundMarker and tostring(payload):find(notFoundMarker, 1, true) then
        return true, false, notFoundCode
    end
    print("[OrionAdminBridge] " .. label .. " failed: " .. tostring(payload))
    return true, false, "ORION_QUERY_FAILED"
end

local function parseInventoryPage(payload)
    if type(payload) ~= "string" then return nil, nil, "ORION_INVALID_PAGE" end
    local offsetText, limitText = payload:match("^(%d+),(%d+)$")
    local offset, limit = tonumber(offsetText), tonumber(limitText)
    if not offset or not limit or offset % 1 ~= 0 or limit % 1 ~= 0 or
        offset < 0 or offset > 100000 or limit < 1 or limit > 50 then
        return nil, nil, "ORION_INVALID_PAGE"
    end
    return offset, limit, nil
end

function OrionAdminDispatch.execute(action, firstText, secondText)
    if action == "unload" then
        local success, payload = OrionAdminSectors.unload(tonumber(firstText), tonumber(secondText))
        return true, success, false, payload
    end
    if action == "player-assets" then
        local index, parseError = OrionAdminProtocol.parsePlayerIndex(firstText, secondText)
        if parseError then return true, false, true, parseError end
        local handled, success, payload = protectedQuery("player asset query",
            function() return OrionAdminPlayerAssets.query(index) end,
            "ORION_PLAYER_NOT_FOUND", "ORION_PLAYER_NOT_FOUND")
        return handled, success, true, payload
    end
    if action == "player-grant" then
        local index, parseError = OrionAdminProtocol.parsePlayerIndex(firstText, 1)
        if parseError then return true, false, false, parseError end
        local success, payload = OrionAdminRewards.grant(index, secondText)
        return true, success, false, payload
    end
    if action == "player-mail" then
        local index, parseError = OrionAdminProtocol.parsePlayerIndex(firstText, 1)
        if parseError then return true, false, false, parseError end
        local success, payload = OrionAdminMailRewards.send(index, secondText)
        return true, success, false, payload
    end
    if action == "player-inventory" or action == "alliance-inventory" then
        local kind = action == "player-inventory" and "player" or "alliance"
        local index, parseError
        if kind == "player" then
            index, parseError = OrionAdminProtocol.parsePlayerIndex(firstText, 1)
        else
            index, parseError = OrionAdminProtocol.parseAllianceIndex(firstText, 1)
        end
        if parseError then return true, false, true, parseError end
        local offset, limit, pageError = parseInventoryPage(secondText)
        if pageError then return true, false, true, pageError end
        local notFound = kind == "player" and "ORION_PLAYER_NOT_FOUND" or "ORION_ALLIANCE_NOT_FOUND"
        local handled, success, payload = protectedQuery(kind .. " inventory query",
            function() return OrionAdminInventory.query(kind, index, offset, limit) end,
            notFound, notFound)
        return handled, success, true, payload
    end
    if action == "player-system-grant" or action == "alliance-system-grant" then
        local kind = action == "player-system-grant" and "player" or "alliance"
        local index, parseError
        if kind == "player" then
            index, parseError = OrionAdminProtocol.parsePlayerIndex(firstText, 1)
        else
            index, parseError = OrionAdminProtocol.parseAllianceIndex(firstText, 1)
        end
        if parseError then return true, false, false, parseError end
        local success, payload = OrionAdminInventory.grantSystem(kind, index, secondText)
        return true, success, false, payload
    end
    if action == "alliance-assets" then
        local index, parseError = OrionAdminProtocol.parseAllianceIndex(firstText, secondText)
        if parseError then return true, false, true, parseError end
        local handled, success, payload = protectedQuery("alliance asset query",
            function() return OrionAdminAllianceAssets.query(index) end,
            "ORION_ALLIANCE_NOT_FOUND", "ORION_ALLIANCE_NOT_FOUND")
        return handled, success, true, payload
    end
    if action == "alliance-grant" then
        local index, parseError = OrionAdminProtocol.parseAllianceIndex(firstText, 1)
        if parseError then return true, false, false, parseError end
        local success, payload = OrionAdminAllianceAssets.grant(index, secondText)
        return true, success, false, payload
    end
    return false, false, true, "ORION_ACTION_NOT_ALLOWED"
end
