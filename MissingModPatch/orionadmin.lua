-- Only authenticated server-console/RCON requests reach the read-only bridge.
-- No entity/player scripts, callbacks, saved values, or game writes in v0.1.
local function quote(value)
    return '"' .. tostring(value):gsub('[%z\1-\31\\"]', function(c)
        return string.format('\\u%04x', string.byte(c))
    end) .. '"'
end

local function response(requestId, action, payload)
    return 'ORION_BEGIN:{"protocolVersion":1,"componentVersion":"0.1.0",' ..
        '"requestId":' .. quote(requestId) .. ',"action":' .. quote(action) ..
        ',"source":"avorion-lua-api","readOnly":true,' .. payload .. '}:ORION_END'
end

function execute(sender, commandName, action, requestId, offsetText, limitText, ...)
    if sender ~= nil then return 0, "", "ORION_CONSOLE_ONLY" end
    if type(requestId) ~= "string" or #requestId ~= 32 or not requestId:match('^[a-f0-9]+$') then
        return 0, "", "ORION_INVALID_REQUEST_ID"
    end
    if select('#', ...) ~= 0 then return 0, "", "ORION_INVALID_ARGUMENTS" end
    if action == "hello" and offsetText == nil and limitText == nil then
        return 1, response(requestId, action,
            '"capabilities":["bridge.hello","players.online"],"serverSideOnly":true'), ""
    end
    if action ~= "players" then return 0, "", "ORION_ACTION_NOT_ALLOWED" end
    local offset, limit = tonumber(offsetText), tonumber(limitText)
    if not offset or not limit or offset % 1 ~= 0 or limit % 1 ~= 0 or
        offset < 0 or offset > 100000 or limit < 1 or limit > 10 then
        return 0, "", "ORION_INVALID_PAGE"
    end
    local ok, payload = pcall(function()
        local players = {Server():getOnlinePlayers()}
        table.sort(players, function(a, b) return a.index < b.index end)
        local items = {}
        for i = offset + 1, math.min(#players, offset + limit) do
            local player = players[i]
            -- Keep names bounded; UTF-8 bytes are preserved rather than sliced.
            if #player.name > 512 then error("name exceeds protocol limit") end
            items[#items + 1] = '{"index":' .. tostring(player.index) ..
                ',"name":' .. quote(player.name) .. ',"online":true}'
        end
        return '"total":' .. #players .. ',"offset":' .. offset .. ',"limit":' .. limit ..
            ',"items":[' .. table.concat(items, ',') .. ']'
    end)
    if not ok then return 0, "", "ORION_QUERY_FAILED" end
    return 1, response(requestId, action, payload), ""
end

function getDescription() return "Read-only Orion administrator bridge (console only)" end
function getHelp() return "/orionadmin hello <requestId> | players <requestId> <offset> <limit>" end
