OrionAdminPlayers = OrionAdminPlayers or {}

function OrionAdminPlayers.allPlayersAndAlliances()
    local players = {Server():getPlayers()}
    local names, indexes, seen = {}, {}, {}
    for _, player in ipairs(players) do
        names[player.index] = player.name
        local index = player.allianceIndex
        if type(index) == "number" and index % 1 == 0 and index ~= 0 and not seen[index] then
            seen[index] = true
            indexes[#indexes + 1] = index
        end
    end
    table.sort(indexes)
    return players, names, indexes
end

function OrionAdminPlayers.online()
    local players = {Server():getOnlinePlayers()}
    table.sort(players, function(a, b) return a.index < b.index end)
    return players
end

function OrionAdminPlayers.query(offset, limit)
    local players = OrionAdminPlayers.online()
    local items = {}
    for i = offset + 1, math.min(#players, offset + limit) do
        local player = players[i]
        if #player.name > 512 then error("name exceeds protocol limit") end
        local x, y = player:getSectorCoordinates()
        items[#items + 1] = '{"index":' .. tostring(player.index) ..
            ',"name":' .. OrionAdminProtocol.quote(player.name) ..
            ',"online":true,' .. OrionAdminProtocol.nullableSector(x, y) .. '}'
    end
    return '"total":' .. #players .. ',"offset":' .. offset ..
        ',"limit":' .. limit .. ',"items":[' .. table.concat(items, ',') .. ']'
end

function OrionAdminPlayers.queryKnown(offset, limit)
    local players = {Server():getPlayers()}
    table.sort(players, function(a, b) return a.index < b.index end)
    local items = {}
    for i = offset + 1, math.min(#players, offset + limit) do
        local player = players[i]
        if type(player.index) ~= "number" or player.index % 1 ~= 0 or
            type(player.name) ~= "string" or #player.name == 0 or #player.name > 512 then
            error("invalid known player")
        end
        items[#items + 1] = '{"index":' .. tostring(player.index) ..
            ',"name":' .. OrionAdminProtocol.quote(player.name) ..
            ',"online":' .. tostring(Server():isOnline(player.index)) .. '}'
    end
    return '"total":' .. #players .. ',"offset":' .. offset ..
        ',"limit":' .. limit .. ',"items":[' .. table.concat(items, ',') .. ']'
end
