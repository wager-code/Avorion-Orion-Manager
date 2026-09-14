-- Thin console-only router. Feature code lives under data/scripts/lib/orionadmin.
include("data/scripts/lib/orionadmin/protocol")
include("data/scripts/lib/orionadmin/players")
include("data/scripts/lib/orionadmin/playerassets")
include("data/scripts/lib/orionadmin/rewards")
include("data/scripts/lib/orionadmin/mailrewards")
include("data/scripts/lib/orionadmin/inventory")
include("data/scripts/lib/orionadmin/alliances")
include("data/scripts/lib/orionadmin/allianceassets")
include("data/scripts/lib/orionadmin/sectors")
include("data/scripts/lib/orionadmin/dispatch")

function execute(sender, commandName, action, requestId, offsetText, limitText, ...)
    if sender ~= nil then return 0, "", "ORION_CONSOLE_ONLY" end

    local requestError = OrionAdminProtocol.validateRequest(requestId, select('#', ...))
    if requestError then return 0, "", requestError end

    if action == "hello" and offsetText == nil and limitText == nil then
        return 1, OrionAdminProtocol.response(requestId, action, true,
            '"capabilities":' .. OrionAdminProtocol.capabilitiesJson() .. ',"serverSideOnly":true'), ""
    end

    local handled, success, readOnly, payloadOrError =
        OrionAdminDispatch.execute(action, offsetText, limitText)
    if handled then
        if not success then return 0, "", payloadOrError end
        return 1, OrionAdminProtocol.response(requestId, action, readOnly, payloadOrError), ""
    end

    if not OrionAdminProtocol.isReadAction(action) then
        return 0, "", "ORION_ACTION_NOT_ALLOWED"
    end

    local offset, limit, pageError = OrionAdminProtocol.parsePage(action, offsetText, limitText)
    if pageError then return 0, "", pageError end

    local ok, payload = pcall(function()
        if action == "players" then return OrionAdminPlayers.query(offset, limit) end
        if action == "players-known" then return OrionAdminPlayers.queryKnown(offset, limit) end
        if action == "sectors" then return OrionAdminSectors.query(offset, limit) end
        return OrionAdminAlliances.query(action, offset, limit)
    end)
    if not ok then
        print("[OrionAdminBridge] query failed: " .. tostring(payload))
        return 0, "", "ORION_QUERY_FAILED"
    end
    return 1, OrionAdminProtocol.response(requestId, action, true, payload), ""
end

function getDescription() return "Guarded Orion administrator bridge (console only)" end
function getHelp() return "/orionadmin hello <id> | players|players-known|sectors|alliances <id> <offset> <limit> | alliance <id> <index> <limit> | player-assets|alliance-assets <id> <index> 1 | player-inventory|alliance-inventory <id> <index> <offset,limit> | player-grant|alliance-grant <id> <index> <asset-csv> | player-mail <id> <index> <asset-csv;subject-hex;body-hex;delivery-id> | player-system-grant|alliance-system-grant <id> <index> <key,rarity,seed> | unload <id> <x> <y>" end
