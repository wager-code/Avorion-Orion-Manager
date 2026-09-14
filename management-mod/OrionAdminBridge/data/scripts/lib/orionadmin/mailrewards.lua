OrionAdminMailRewards = OrionAdminMailRewards or {}

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

local function decodeHex(value, minimumBytes, maximumBytes)
    if type(value) ~= "string" or #value % 2 ~= 0 or #value < minimumBytes * 2 or
        #value > maximumBytes * 2 or not value:match("^[a-f0-9]+$") then return nil end
    local decoded = value:gsub("%x%x", function(pair) return string.char(tonumber(pair, 16)) end)
    if decoded:find("[%z\1-\8\11\12\14-\31]") then return nil end
    return decoded
end

local function parsePayload(payload)
    if type(payload) ~= "string" or #payload < 84 or #payload > 8192 then
        return nil, "ORION_MAIL_INVALID"
    end
    local csv, subjectHex, bodyHex, deliveryId = payload:match("^([^;]+);([^;]+);([^;]+);([a-f0-9]+)$")
    if not csv or #deliveryId ~= 32 then return nil, "ORION_MAIL_INVALID" end
    local values = {}
    if not csv:match("^%d+,%d+,%d+,%d+,%d+,%d+,%d+,%d+$") then
        return nil, "ORION_MAIL_INVALID"
    end
    for value in csv:gmatch("[^,]+") do values[#values + 1] = tonumber(value) end
    if #values ~= 8 or not validAmount(values[1], MAX_CREDITS_GRANT) then
        return nil, "ORION_MAIL_INVALID"
    end
    local total = values[1]
    for index = 2, 8 do
        if not validAmount(values[index], MAX_RESOURCE_GRANT) then
            return nil, "ORION_MAIL_INVALID"
        end
        total = total + values[index]
    end
    if total <= 0 then return nil, "ORION_MAIL_EMPTY" end
    local subject = decodeHex(subjectHex, 1, 240)
    local body = decodeHex(bodyHex, 1, 2000)
    if not subject or not body then return nil, "ORION_MAIL_INVALID" end
    return {grant = values, subject = subject, body = body, deliveryId = deliveryId}, nil
end

local function safeBalances(values)
    for _, value in ipairs(values) do
        if type(value) ~= "number" or value % 1 ~= 0 or value < 0 or value > MAX_SAFE_INTEGER then
            return false
        end
    end
    return true
end

local function grantJson(values)
    return '"credits":' .. tostring(values[1]) .. ',"resources":{' ..
        '"iron":' .. tostring(values[2]) .. ',"titanium":' .. tostring(values[3]) ..
        ',"naonite":' .. tostring(values[4]) .. ',"trinium":' .. tostring(values[5]) ..
        ',"xanion":' .. tostring(values[6]) .. ',"ogonite":' .. tostring(values[7]) ..
        ',"avorion":' .. tostring(values[8]) .. '}'
end

local function verifyMail(mail, mailId, grant)
    if not mail or mail.id ~= mailId or mail.money ~= grant[1] then return false end
    local resources = {mail:getResources()}
    if #resources ~= 7 or not safeBalances(resources) then return false end
    for index = 1, 7 do
        if resources[index] ~= grant[index + 1] then return false end
    end
    return true
end

function OrionAdminMailRewards.send(playerIndex, payload)
    local request, requestError = parsePayload(payload)
    if requestError then return false, requestError end
    local player = findPlayer(playerIndex)
    if not player then return false, "ORION_PLAYER_NOT_FOUND" end
    if type(player.name) ~= "string" or #player.name == 0 or #player.name > 512 then
        return false, "ORION_MAIL_INVALID_PLAYER"
    end
    local mailId = "orionadmin-" .. request.deliveryId
    local existing = {player:getMailsById(mailId)}
    if #existing > 1 then return false, "ORION_MAIL_VERIFY_FAILED" end
    local mailIndex, replayed = 0, false
    if #existing == 1 then
        if not verifyMail(existing[1], mailId, request.grant) then
            return false, "ORION_MAIL_VERIFY_FAILED"
        end
        replayed = true
    else
        if type(player.numMails) ~= "number" or type(player.maxNumMails) ~= "number" or
            player.numMails >= player.maxNumMails then return false, "ORION_MAILBOX_FULL" end
        local mail = Mail()
        mail.id = mailId
        mail.sender = "OrionAdmin"
        mail.header = request.subject
        mail.text = request.body
        mail.money = request.grant[1]
        mail:setResources(request.grant[2], request.grant[3], request.grant[4], request.grant[5],
            request.grant[6], request.grant[7], request.grant[8])
        local ok, result = pcall(function() return player:addMail(mail) end)
        if not ok or type(result) ~= "number" or result < 0 or result % 1 ~= 0 then
            print("[OrionAdminBridge] player mail failed: " .. tostring(result))
            return false, "ORION_MAIL_FAILED"
        end
        mailIndex = result
        existing = {player:getMailsById(mailId)}
        if #existing ~= 1 or not verifyMail(existing[1], mailId, request.grant) then
            return false, "ORION_MAIL_VERIFY_FAILED"
        end
    end
    return true, '"player":{"index":' .. tostring(player.index) ..
        ',"name":' .. OrionAdminProtocol.quote(player.name) ..
        ',"online":' .. tostring(Server():isOnline(player.index)) .. '},' ..
        '"accepted":true,"deliveryId":' .. OrionAdminProtocol.quote(request.deliveryId) ..
        ',"mailId":' .. OrionAdminProtocol.quote(mailId) .. ',"mailIndex":' .. tostring(mailIndex) ..
        ',"replayed":' .. tostring(replayed) .. ',"grant":{' .. grantJson(request.grant) .. '}'
end
