# Avorion 2.5.13 官方 Documentation 已核对能力

源：用户提供 `Documentation.zip`。
`index.html`：`Documentation of Avorion Version: 2.5.13 0417ab29738c`。

下面仅列已经在文档中实际核对到的方法/属性。

## Server
来源：`Server.html`
- `Server.getOnlinePlayers()`
- `Server.getPlayers()`

## Galaxy [Server]
来源：`Galaxy [Server].html`
- `findPlayer(identifier)`
- `getLocalFaction(x, y)`
- `getNearestFaction(x, y)`
- `getPirateFaction(level)`
- `loadSector(x, y)`
- `tryUnloadSector(x, y)`

### 重要规则
`tryUnloadSector` 是官方安全尝试卸载星区接口。不要把 Lua GC 或 Windows working-set trick 包装成“官方清内存”。

## Player [Server]
来源：`Player [Server].html`

属性/基础：
- `allianceIndex` (read-only)
- `craftFaction`
- `craftIndex`
- `playtime` (read-only)
- `money`

位置/消息：
- `getSectorCoordinates()`
- `sendChatMessage(...)`

资源/库存：
- `getInventory()`
- `getResources()`
- `pay(...)`
- `payResource(...)`
- `receive(...)`
- `receiveResource(...)`

舰船：
- `getShipNames()`
- `getShipCargos(name)`
- `getShipCrew(name)`
- `getShipPlan(name)`
- `getShipPosition(name)`
- `getShipStatus(name)`
- `getShipSystems(name)`
- `getShipTurretDesigns(name)`

## Player Callbacks
来源：`Player Callbacks.html`
- `onAllianceChanged(allianceIndex)`
- `onChatMessage(playerIndex, text, channel)`
- `onResourcesChanged(playerIndex)`
- `onSectorEntered(playerIndex, x, y, sectorChangeType)`
- `onSectorLeft(playerIndex, x, y, sectorChangeType)`
- `onShipChanged(playerIndex, craftId)`

## Server Callbacks
来源：`Server Callbacks.html`
- `onPlayerLogIn(playerIndex)`
- `onPlayerLogOff(playerIndex)`

## Inventory
来源：`Inventory.html`
- `add(item, recent)`
- `getItems()`
- `getItemsByType(type)`
- `remove(index)`

## Alliance [Server]
来源：`Alliance [Server].html`

属性：
- `leader`
- `numShips` (read-only)
- `numStations` (read-only)
- `money`

成员/权限：
- `getMembers()`
- `getOnlineMembers()`
- `getMemberLocation(playerIndex)`
- `getMemberRank(playerIndex)`
- `setMemberRank(playerIndex, rank)`
- `addRank(name, lowerName)`
- `removeRank(name)`
- `addRankPrivilege(rank, privilege)`
- `removeRankPrivilege(rankName, privilege)`
- `hasPrivilege(playerIndex, privilege)`

资源/库存：
- `getInventory()`
- `getResources()`
- `pay(...)`
- `payResource(...)`
- `receive(...)`
- `receiveResource(...)`

联盟舰船：
- `getShipNames()`
- `getShipCargos(name)`
- `getShipCrew(name)`
- `getShipPlan(name)`
- `getShipPosition(name)`
- `getShipStatus(name)`
- `getShipSystems(name)`
- `getShipTurretDesigns(name)`

## Alliance Callbacks
来源：`Alliance [Server] Callbacks.html`
已核对到包括：
- `onLeaderChanged`
- `onMemberChanged`
- `onMemberLeft`
- `onRankChanged`
- `onRankRemoved`
- `onResourcesChanged`
- `onShipAvailabilityUpdated`
- `onShipCargoUpdated`
- `onShipCrewUpdated`
- `onShipInfoAdded`
- `onShipInfoRemoved`
- `onShipInfoUpdated`
- `onShipNameUpdated`
- `onShipOrderInfoUpdated`
- `onShipPlanUpdated`
- `onShipPositionUpdated`
- `onShipStatusMessageUpdated`
- 等舰船相关变化 callback

## Sector [Server]
来源：`Sector [Server].html`
- `createShip(faction, name, plan, position, arrivalType)`
- `createStation(faction, plan, position, script, ...)`
- `transferEntity(entity, x, y, type)`

### 结论
- 新建一艘直接属于玩家/联盟 Faction 的舰船：支持。
- 多次 createShip，批量赠送多艘舰船：支持基础能力。
- 创建属于玩家/联盟的空间站：支持基础能力。

## Plan
来源：`Functions.html`
- `LoadPlanFromFile(file)`
- `LoadPlanFromString(content)`

因此可以建立服务器舰船蓝图库。

## ShipDatabaseEntry [Server]
来源：`ShipDatabaseEntry [Server].html`

说明明确：这是 Player/Alliance 舰船数据库条目的服务端接口。

已确认读写能力包括：
- `getCargo()` / `setCargo()`
- `getCoordinates()` / `setCoordinates()`
- `getCrew()` / `setCrew()`
- `getHangar()` / `setHangar()`
- `getOrderInfo()` / `setOrderInfo()`
- `getPlan()` / `setPlan()`

### 重要限制
官方文档明确：**Changing the ship's faction is not possible with this.**
因此第一版不做“把现有舰船直接转让给另一玩家/联盟”。

## ShipAI
来源：`ShipAI.html`
- `registerEnemyFaction(index)`
- `setAggressive(attackCivilShips, canFinish)`
- `setAttack(target)`
- `setEscort(escortedShip)`
- `setGuard(location)`

## 管理命令桥
来源：`CommandFunctions.html`
- 自定义命令入口 `execute(playerIndex, ...)`
- 文档明确：若命令由 **RCON interface or console** 发起，`playerIndex` 为 `nil`

因此可以设计：Web → Agent → RCON → 自定义管理 Command → Lua MOD → 官方 Scripting API。
