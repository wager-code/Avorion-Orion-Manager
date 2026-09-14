# Inventory 物品目录、炮塔与图标方案

更新时间：2026-09-10  
目标游戏版本：Avorion 2.5.13  
状态：官方能力与本机同版本原版资源已核对；共享目录与安全静态图标解析已实现，炮塔生成预览与发放仍未实现。实际支持状态仍以 `06_CAPABILITY_MATRIX.md` 为准。

## 一、结论

1. Inventory 不能只按“船插”设计。官方 `InventoryItemType` 明确包含五类：`Turret`、`TurretTemplate`、`SystemUpgrade`、`VanillaItem`、`UsableItem`。
2. 原版炮塔至少有 19 种生成类型，而且每个类型还会随种子、科技等级、材质、稀有度、DPS 与特殊词条产生大量实例。页面不能维护一张有限的“炮塔名称表”来冒充完整目录。
3. 系统插件也不止当前已开放的 11 种。本机 Avorion 2.5.13 原版 `data/scripts/systems/` 共核对到 39 个脚本，其中包含普通系统、尚待隔离验证的系统，以及剧情、钥匙、BOSS 或特殊系统；不能全部直接加入发放白名单。
4. 普通物品和系统插件有静态 `icon` 路径；炮塔有静态 `weaponIcon` 类型图标。游戏库存格里显示的具体炮塔外形是客户端根据 `TurretTemplate` 实时渲染的程序化预览，不是每件炮塔对应一张现成 PNG。
5. 本机 Steam 客户端 `data` 中找到 1716 个图片文件，其中 `data/textures/icons` 下 922 个；专用服务端对应目录只有 2 个图片文件。因此无客户端的纯服务端机器不能依赖完整图标库。

## 二、官方 Inventory 类型

| 官方类型 | 含义 | 可取得的图像字段 | 当前 OrionAdmin |
|---|---|---|---|
| `Turret` | 可放入库存的炮塔实例 | `weaponIcon`；不是具体三维炮塔缩略图 | 已读取核心详情；19 种普通类型已进入共享目录并显示静态类型图标；尚未发放 |
| `TurretTemplate` | 炮塔模板/蓝图类物品 | `weaponIcon` | 与炮塔共用核心详情合同；尚未做生成预览与独立目录/发放 |
| `SystemUpgrade` | 舰船系统插件（船插） | `icon` | 39 个原版脚本已分层进入共享目录；其中仅 11 个白名单系统可发放 |
| `VanillaItem` | 原版普通库存物品 | `icon`、`iconColor` | 已能识别类型；尚未做目录与发放白名单 |
| `UsableItem` | 可使用的脚本物品 | `icon`、`iconColor`、`script` | 已能识别类型；尚未做目录与发放白名单 |

官方稀有度为 `Petty`、`Common`、`Uncommon`、`Rare`、`Exceptional`、`Exotic`、`Legendary`；材质为铁、钛、纳奥石、崔钢、赛安金属、欧格石、阿沃里昂。是否允许某一组合发放，应由安全白名单决定，不能仅因为枚举存在就开放。

## 三、原版炮塔类型

同版本原版 `data/scripts/lib/weapontype.lua` 与 `turretgenerator.lua` 明确登记以下 19 种：

| 分组 | 类型 |
|---|---|
| 武装 | 机枪 `ChainGun`、激光 `Laser`、等离子炮 `PlasmaGun`、火箭发射器 `RocketLauncher`、加农炮 `Cannon`、轨道炮 `RailGun`、爆能炮 `Bolter`、闪电炮 `LightningGun`、特斯拉炮 `TeslaGun`、脉冲炮 `PulseCannon` |
| 点防御 | 点防机枪 `PointDefenseChainGun`、点防激光 `PointDefenseLaser`、防空炮 `AntiFighter` |
| 工业/非武装 | 采矿激光 `MiningLaser`、R 型采矿激光 `RawMiningLaser`、打捞激光 `SalvagingLaser`、R 型打捞激光 `RawSalvagingLaser`、维修光束 `RepairBeam`、力场炮 `ForceGun` |

官方生成链已经确认：

```text
TurretGenerator.generateSeeded(seed, weaponType, dps, tech, rarity, material, coaxialAllowed)
  -> TurretTemplate
  -> InventoryTurret(template)
  -> Inventory.add / addOrDrop
```

### 刷炮塔时可指定的参数

原版 `TurretGenerator.generateSeeded()` 的直接输入是：`seed`、`weaponType`、`dps`、`tech`、`rarity`、`material`、`coaxialAllowed`。这些参数不是全部都代表“最终成品必须等于输入值”。

| 参数 | 能否直接指定 | 建议页面控件 | 准确含义与边界 |
|---|---|---|---|
| 武器类型 `weaponType` | 是，精确 | 19 种原版类型选择 | 决定机枪、激光、采矿、打捞、维修、点防等主类型 |
| 材质 `material` | 是，精确 | 铁至阿沃里昂 7 级 | 生成武器会写入指定 `Material`；与科技等级是两个独立参数 |
| 科技等级 `tech` | 是，精确输入 | 1–52 滑块/数字 | 原版银河正常科技映射为 1–52；影响武器强度、尺寸/槽位档位等，但槽位仍可能被生成器随机降档 |
| 稀有度 `rarity` | 是，精确 | Petty 至 Legendary 7 级 | 影响名称、伤害倍率和出现特殊词条的机会；正式开放范围仍由白名单决定 |
| Seed | 是，确定性 | 自动生成、重新随机、高级模式手填 | 同一游戏版本、同一生成器和同一组输入使用相同 Seed，可复现同一候选炮塔 |
| 基础 DPS 输入 `dps` | 是，但不是最终 DPS | 推荐值/倍率或高级数字 | 它是生成器的强度预算；稀有度、槽位、同轴、多发和特殊词条还会改变最终显示 DPS。力场炮会忽略该输入并按科技等级平衡 |
| 允许同轴 `coaxialAllowed` | 是，但只能允许/禁止 | “禁止/允许随机出现” | `false` 可保证不生成同轴；`true` 不保证同轴，原版只在高槽位候选上以概率生成。采矿/打捞类强制不能同轴 |
| 数量 | OrionAdmin 可控制 | 默认 1 件 | 游戏可以循环生成多件，但管理端第一阶段仍应每次 1 件并逐件复核 |

### 不能由原版顶层生成接口精确指定的成品参数

以下结果主要由武器类型、科技等级、稀有度和 Seed 派生，适合显示为“生成结果”或作为重新随机后的筛选条件，不适合第一版直接硬改：

- 槽位数和炮塔尺寸。科技等级决定可进入的档位，但原版有随机降档逻辑；不同武器类型的槽位上限也不同。
- 最终 DPS、单发伤害、射速、射程、精准度、弹速、过热/充能/冷却、能耗和船员需求。
- 炮管/可见武器数量、连发数量、弹丸或光束外观与颜色。
- 追踪导弹、伤害类型、护盾穿透，以及高伤害、高射程、高射速、高精准、高效率、长持续射击、低能耗、爆发射击、离子弹等特殊词条。
- 最终名称、前缀和序列文字。

技术上可以在生成后取得 `Weapon` 并修改大量底层字段，也可以直接改 `TurretTemplate.slots/size/coaxial`；但这会绕过原版平衡关系，容易产生数值、描述、售价、槽位和实际行为不一致的异常炮塔。第一版不开放“任意属性编辑器”，而是让管理员指定安全的生成输入，然后展示最终结果，满意后再发放。

建议页面分两种生成方式：

1. **原版平衡模式**：选择武器类型、材质、科技等级、稀有度；DPS 使用 `Balancing_TechWeaponDPS(tech)` 或对应工业武器平衡值，点击“换一个”只更换 Seed。
2. **高级受限模式**：在同一组参数上允许有限的 DPS 倍率、同轴允许/禁止和 Seed 输入；生成后必须显示实际 DPS、槽位、射程、词条、能耗等，再二次确认。

系统插件与炮塔不同：插件通常只指定原版脚本、稀有度和 Seed，没有炮塔的材质、科技等级、DPS 或槽位生成参数。

`InventoryTurret` 还能读取 DPS、伤害、射程、射速、精准度、槽位、尺寸、材质、稀有度、武器分类、点防/武装/非武装、同轴、追踪、连续光束、耗能、过热、采矿/打捞效率、船员需求等属性。0.10.0 已接通前述核心字段中的类型/分类、材质、科技、DPS、伤害、射程、射速、精准度、槽位/尺寸/武器数和主要布尔特征；能耗、过热、效率与船员需求仍待扩展。未来物品查找不能只显示“炮塔 + 稀有度”。

剧情 BOSS 脚本还会生成特殊炮塔，例如特殊打捞激光、追踪加农炮、派对喇叭、发射器电池和萤火等离子炮。这些属于剧情/特殊掉落，不进入普通管理员发放白名单。

## 四、原版系统插件范围

共核对 39 个原版系统脚本，按产品安全边界分三层：

### A. 当前已经实测开放的 11 个

`batterybooster`、`cargoextension`、`energybooster`、`enginebooster`、`hyperspacebooster`、`miningsystem`、`radarbooster`、`scannerbooster`、`shieldbooster`、`tradingoverview`、`valuablesdetector`。

### B. 原版存在、可继续隔离验证的 14 个

`arbitrarytcs`、`autotcs`、`civiltcs`、`militarytcs`、`defensesystem`、`energytoshieldconverter`、`excessvolumebooster`、`fightersquadsystem`、`lootrangebooster`、`resistancesystem`、`shieldimpenetrator`、`transportersoftware`、`velocitybypass`、`weaknesssystem`。

这些脚本存在并不等于 OrionAdmin 当前可发放。每个脚本仍需核对 DLC、生成参数、稀有度、重启保存、客户端显示和副作用后，才能逐项加入白名单。

### C. 默认禁止普通发放的 14 个特殊系统

四个 `behemoth*` 系统、`smugglerblocker`、`teleporterkey1` 至 `teleporterkey8`、`wormholeopener`。它们带有巨兽、剧情、钥匙或特殊进度语义，只能作为目录中的“特殊/不可发放”条目显示。

## 五、图标和炮塔图片能做到什么

### 已确认能做

- 系统插件：读取 `SystemUpgradeTemplate.icon`。
- 普通/可使用物品：读取 `VanillaInventoryItem.icon`、`UsableInventoryItem.icon` 及 `iconColor`。
- 炮塔/炮塔模板：读取 `weaponIcon`，显示官方的武器类型图标。
- 本机游戏客户端已存在常用武器图标，例如 `chaingun.png`、`laser-gun.png`、`mining-laser.png`、`r-mining-laser.png`、`salvage-laser.png`、`r-salvaging-laser.png`、`plasma-gun.png`、`rocket-launcher.png`、`cannon.png`、`rail-gun.png`、`repair-beam.png`、`bolter-gun.png`、`lightning-gun.png`、`tesla-gun.png`、`force-gun.png`、`pulsecannon.png`、`anti-fighter-gun.png` 等。
- 页面可用真实图标、稀有度边框、材质颜色和真实属性组合成可搜索的物品卡片。

### 目前不能承诺

- 截图中每一门炮塔的具体三维外形不是独立 PNG，而是客户端库存控件渲染的程序化预览。
- 2.5.13 官方脚本文档没有找到把 `TurretTemplate` 直接导出为 PNG/JPG 的接口；`InventorySelectionItem` 只接收物品，`Picture`/`UIRenderer` 只在客户端绘制，也没有文件导出方法。
- 因此“显示每件生成炮塔的真实三维缩略图”需要另行验证客户端渲染/截图流水线。完成该验证前，第一版使用官方 `weaponIcon`，不能伪装成已经取得了截图里的三维缩略图。

### 资源部署与版权边界

- 专用服务端不含完整图标库，OrionAdmin 不能直接把服务端路径当图片源。
- 后端不能提供任意文件路径读取接口。只允许规范化的 `data/textures/icons/...` 路径、固定扩展名和已扫描白名单，防止路径穿越。
- 开发机可以从已安装客户端建立本地图标索引；发布包是否能重新分发 Boxelware 的全部游戏美术资源，需要单独确认许可。许可未确认前，不把 922 个客户端图标整体复制进发布包。
- 可行的第一版是：运行时发现合法的 Avorion 客户端资源目录，或只随产品携带经许可确认的最小图标集合；缺失时使用 OrionAdmin 自有占位图并明确标记。
- 已把本次发现的 922 个候选图标写入 `docs/evidence/avorion-2.5.13-client-icon-index.csv`，记录相对路径、字节数和 SHA-256。该文件只是资源索引，不表示每张图都是 Inventory 物品，也不包含原图。

## 六、物品目录/查找实现

没有新增重复的“玩家库存列表”页面。共享“物品目录”对话框由玩家详情和联盟详情内同一个 `InventoryPanel` 打开；批量奖励中心没有复制库存信息。

当前实现固定登记 19 种普通炮塔与 39 个原版系统插件，提供中文名、英文名、脚本/武器类型搜索，以及物品类型和发放状态筛选。11 个已验证系统插件可回填现有发放表单；其余条目只展示目录状态，没有写按钮。运行时从合法 Avorion 客户端目录索引图标，本机共 922 个；目录 58 个条目在两档视觉验收中全部加载真实图标。服务端未安装客户端或图标缺失时使用占位图，不伪造资源。

目录有两层数据：

1. **物品定义**：类型、脚本/武器类型、中文名、官方图标、分类、DLC/剧情属性、是否可发放。
2. **生成实例**：Seed、稀有度、材质、科技等级、DPS、槽位及生成后真实属性。炮塔必须先生成预览数据，再由管理员确认发放。

建议筛选项：

- 大类：炮塔、炮塔蓝图、系统插件、普通物品、可使用物品。
- 炮塔：武器类型、武装/工业/点防、材质、稀有度、科技等级、槽位、DPS、射程、同轴/追踪等。
- 系统插件：功能分类、稀有度、普通/特殊/DLC、是否已验证可发放。
- 通用：中文名/英文名/脚本名搜索、仅显示可发放、仅显示本机图标可用。

建议每个目录条目保存 `grantPolicy`：

| 值 | 行为 |
|---|---|
| `verified-grantable` | 已进入固定白名单，允许走受保护发放流程 |
| `catalog-only` | 可以查看与搜索，但没有发放按钮 |
| `story-blocked` | 剧情/钥匙/BOSS 物品，明确禁止普通发放 |
| `dlc-required` | 需要对应 DLC，未满足时不可发放 |
| `unavailable` | 资源或脚本在当前安装中不存在 |

## 七、模块化接入边界

后续实现时按模块化单体拆分，不把类型表、图标扫描和发放逻辑继续塞进现有单文件：

```text
Core/Models/InventoryCatalog*          目录 DTO、筛选、GrantPolicy
Core/Abstractions/IInventoryCatalog*  最小目录接口
Agent/.../InventoryCatalogService     原版脚本/资源扫描、缓存和校验
Api/Endpoints/InventoryCatalogEndpoints
management-mod/lib/orionadmin/turrets.lua
management-mod/lib/orionadmin/items.lua
frontend/components/inventory-catalog/*
```

炮塔发放仍需独立 Worker、固定 `WeaponType` 白名单、参数上限、管理员会话、CSRF、幂等、持久 Operation、生成前后 Inventory 精确复核，并且默认每次 1 件、不自动重试。剧情特殊炮塔不得借用通用参数入口绕过白名单。

## 八、建议实施顺序

1. **已完成**：扩充 Inventory 只读详情，把炮塔真实类型、`weaponIcon`、材质、科技、DPS、射程、槽位等核心字段读出，并在现有玩家/联盟详情复用展示。
2. **已完成**：建立物品目录与安全图标解析器，完成 58 个已核对定义的搜索/筛选和“可发放/仅目录/剧情禁止”状态；只复用已经存在的 11 个系统插件发放表单，没有为未验证物品开放写入。
3. 实现 19 种普通炮塔的受限生成预览与单件发放，先在隔离 Galaxy 验证，再开放给玩家和联盟。
4. 对 B 组 14 个系统插件逐项验证，通过一个加入一个，不整批放开。
5. 再核对 `VanillaItem`、`UsableItem` 的具体原版脚本和副作用，建立独立白名单。
6. 最后评估是否值得开发真实三维炮塔缩略图导出；这不阻塞物品目录和发放功能。

## 九、核对依据

- `source_docs/Documentation_Avorion_2.5.13.zip`：官方 2.5.13 Scripting API。
- 官方对象：`InventoryTurret`、`TurretTemplate`、`SystemUpgradeTemplate`、`VanillaInventoryItem`、`UsableInventoryItem`、`InventorySelectionItem`、`Picture`、`UIRenderer`。
- 官方枚举：`InventoryItemType`、`RarityType`、`MaterialType`、`TurretSlotType`、`WeaponAppearance`、`WeaponCategory`。
- 同版本原版脚本：`data/scripts/lib/weapontype.lua`、`turretgenerator.lua`、`weapontypeutility.lua`、`data/scripts/systems/*.lua` 以及原版商店/奖励脚本。
- 本机同版本客户端静态资源：`data/textures/icons/`；数量只表示本次扫描结果，不是发布包承诺。
- `docs/evidence/avorion-2.5.13-client-icon-index.csv`：本机 Avorion 2.5.13 客户端 922 个图标候选的可复核索引。
