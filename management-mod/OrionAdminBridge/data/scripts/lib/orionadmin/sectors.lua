OrionAdminSectors = OrionAdminSectors or {}

function OrionAdminSectors.query(offset, limit)
    local players = OrionAdminPlayers.online()
    local playerSectors = {}
    for _, player in ipairs(players) do
        local x, y = player:getSectorCoordinates()
        local key = tostring(x) .. ':' .. tostring(y)
        playerSectors[key] = (playerSectors[key] or 0) + 1
    end

    local sectors = Galaxy():getLoadedSectors()
    table.sort(sectors, function(a, b)
        return a.x == b.x and a.y < b.y or a.x < b.x
    end)
    local items = {}
    for i = offset + 1, math.min(#sectors, offset + limit) do
        local sector = sectors[i]
        local count = playerSectors[tostring(sector.x) .. ':' .. tostring(sector.y)] or 0
        items[#items + 1] = '{"x":' .. tostring(sector.x) ..
            ',"y":' .. tostring(sector.y) ..
            ',"playerCount":' .. tostring(count) ..
            ',"eligible":' .. tostring(count == 0) .. '}'
    end
    return '"total":' .. #sectors .. ',"offset":' .. offset ..
        ',"limit":' .. limit .. ',"items":[' .. table.concat(items, ',') .. ']'
end

function OrionAdminSectors.unload(x, y)
    if not OrionAdminProtocol.validSector(x, y) then
        return false, "ORION_INVALID_COORDINATES"
    end

    local ok, result = pcall(function()
        for _, player in ipairs(OrionAdminPlayers.online()) do
            local playerX, playerY = player:getSectorCoordinates()
            if playerX == x and playerY == y then return "has-player" end
        end
        local loaded = false
        for _, sector in ipairs(Galaxy():getLoadedSectors()) do
            if sector.x == x and sector.y == y then loaded = true break end
        end
        if not loaded then return "not-loaded" end
        return Galaxy():tryUnloadSector(x, y) and "accepted" or "rejected"
    end)

    if not ok then return false, "ORION_UNLOAD_FAILED" end
    if result == "has-player" then return false, "ORION_SECTOR_HAS_PLAYER" end
    if result == "not-loaded" then return false, "ORION_SECTOR_NOT_LOADED" end
    if result ~= "accepted" then return false, "ORION_UNLOAD_REJECTED" end
    return true, '"x":' .. tostring(x) .. ',"y":' .. tostring(y) ..
        ',"playerCount":0,"accepted":true'
end
