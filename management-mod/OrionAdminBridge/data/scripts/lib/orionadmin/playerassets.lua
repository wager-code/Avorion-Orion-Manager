OrionAdminPlayerAssets = OrionAdminPlayerAssets or {}

local function requireAmount(value, field)
    if type(value) ~= "number" or value % 1 ~= 0 or value < 0 or value > 9007199254740991 then
        error("invalid player asset amount: " .. field)
    end
    return tostring(value)
end

function OrionAdminPlayerAssets.query(playerIndex)
    local target = nil
    for _, player in ipairs({Server():getPlayers()}) do
        if player.index == playerIndex then
            target = player
            break
        end
    end
    if not target then error("ORION_PLAYER_NOT_FOUND") end
    if type(target.name) ~= "string" or #target.name == 0 or #target.name > 512 then
        error("invalid player name")
    end

    local iron, titanium, naonite, trinium, xanion, ogonite, avorion = target:getResources()
    return '"player":{"index":' .. tostring(target.index) ..
        ',"name":' .. OrionAdminProtocol.quote(target.name) ..
        ',"online":' .. tostring(Server():isOnline(target.index)) .. '},' ..
        '"credits":' .. requireAmount(target.money, "credits") .. ',' ..
        '"resources":{' ..
        '"iron":' .. requireAmount(iron, "iron") .. ',' ..
        '"titanium":' .. requireAmount(titanium, "titanium") .. ',' ..
        '"naonite":' .. requireAmount(naonite, "naonite") .. ',' ..
        '"trinium":' .. requireAmount(trinium, "trinium") .. ',' ..
        '"xanion":' .. requireAmount(xanion, "xanion") .. ',' ..
        '"ogonite":' .. requireAmount(ogonite, "ogonite") .. ',' ..
        '"avorion":' .. requireAmount(avorion, "avorion") .. '}'
end
