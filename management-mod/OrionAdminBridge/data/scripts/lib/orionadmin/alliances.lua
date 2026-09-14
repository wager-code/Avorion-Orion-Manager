OrionAdminAlliances = OrionAdminAlliances or {}

local function summary(alliance, playerNames)
    if type(alliance.name) ~= "string" or #alliance.name == 0 or #alliance.name > 512 then
        error("invalid alliance name")
    end
    local members = {alliance:getMembers()}
    local onlineMembers = {alliance:getOnlineMembers()}
    local leaderIndex = alliance.leader
    local leaderName = playerNames[leaderIndex]
    if type(leaderIndex) ~= "number" or leaderIndex % 1 ~= 0 or leaderIndex == 0 or
        type(leaderName) ~= "string" or #leaderName == 0 or #leaderName > 512 then
        error("invalid alliance leader")
    end
    local homeX, homeY = alliance:getHomeSectorCoordinates()
    local home = OrionAdminProtocol.nullableSector(homeX, homeY)
        :gsub('"x"', '"homeX"'):gsub('"y"', '"homeY"')
    if type(alliance.numCrafts) ~= "number" or alliance.numCrafts < 0 or alliance.numCrafts % 1 ~= 0 or
        type(alliance.numStations) ~= "number" or alliance.numStations < 0 or alliance.numStations % 1 ~= 0 then
        error("invalid alliance craft counts")
    end
    local online = Server():isOnline(alliance.index)
    return '{"index":' .. tostring(alliance.index) ..
        ',"name":' .. OrionAdminProtocol.quote(alliance.name) ..
        ',"online":' .. tostring(online) ..
        ',"leaderIndex":' .. tostring(leaderIndex) ..
        ',"leaderName":' .. OrionAdminProtocol.quote(leaderName) ..
        ',"memberTotal":' .. tostring(#members) ..
        ',"onlineMembers":' .. tostring(#onlineMembers) .. ',' .. home ..
        ',"numCrafts":' .. tostring(alliance.numCrafts) ..
        ',"numStations":' .. tostring(alliance.numStations) .. '}',
        #members, #onlineMembers, online
end

local function list(offset, limit, playerNames, allianceIndexes)
    local items, totalMembers, totalOnlineMembers, onlineAlliances = {}, 0, 0, 0
    for position, index in ipairs(allianceIndexes) do
        local row, memberTotal, onlineMemberTotal, online =
            summary(Alliance(index), playerNames)
        totalMembers = totalMembers + memberTotal
        totalOnlineMembers = totalOnlineMembers + onlineMemberTotal
        if online then onlineAlliances = onlineAlliances + 1 end
        if position > offset and position <= offset + limit then
            items[#items + 1] = row
        end
    end
    return '"total":' .. #allianceIndexes ..
        ',"totalMembers":' .. totalMembers ..
        ',"totalOnlineMembers":' .. totalOnlineMembers ..
        ',"onlineAlliances":' .. onlineAlliances ..
        ',"offset":' .. offset .. ',"limit":' .. limit ..
        ',"items":[' .. table.concat(items, ',') .. ']'
end

local function detail(index, limit, playerNames, allianceIndexes)
    local found = false
    for _, candidate in ipairs(allianceIndexes) do
        if candidate == index then found = true break end
    end
    if not found or not Galaxy():allianceFactionExists(index) then
        error("alliance not found")
    end

    local alliance = Alliance(index)
    local allianceSummary = summary(alliance, playerNames)
    local memberIndexes = {alliance:getMembers()}
    table.sort(memberIndexes)
    local rows = {}
    for i = 1, math.min(#memberIndexes, limit) do
        local memberIndex = memberIndexes[i]
        local memberName = playerNames[memberIndex]
        local rank = alliance:getMemberRank(memberIndex)
        local rankName = rank and rank.name
        if type(memberName) ~= "string" or #memberName == 0 or #memberName > 512 or
            type(rankName) ~= "string" or #rankName == 0 or #rankName > 512 then
            error("invalid alliance member")
        end
        local memberX, memberY = alliance:getMemberLocation(memberIndex)
        rows[#rows + 1] = '{"index":' .. tostring(memberIndex) ..
            ',"name":' .. OrionAdminProtocol.quote(memberName) ..
            ',"rank":' .. OrionAdminProtocol.quote(rankName) ..
            ',"online":' .. tostring(Server():isOnline(memberIndex)) ..
            ',' .. OrionAdminProtocol.nullableSector(memberX, memberY) .. '}'
    end
    return '"alliance":' .. allianceSummary .. ',"memberLimit":' .. limit ..
        ',"members":[' .. table.concat(rows, ',') .. ']'
end

function OrionAdminAlliances.query(action, offset, limit)
    local _, playerNames, allianceIndexes = OrionAdminPlayers.allPlayersAndAlliances()
    if action == "alliances" then
        return list(offset, limit, playerNames, allianceIndexes)
    end
    return detail(offset, limit, playerNames, allianceIndexes)
end
