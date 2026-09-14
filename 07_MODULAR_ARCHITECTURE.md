# OrionAdmin 模块化单体架构

更新时间：2026-09-10

## 目标

OrionAdmin 继续保持一个本机管理程序、一个 API 进程、一个前端和一个专用管理 MOD。模块化解决的是源码边界和后续扩展成本，不把本机软件拆成需要独立部署、联网协调的微服务。

基本原则：

1. 入口只组装模块，不承载业务逻辑。
2. 一个功能只设一个主要归属页面；其他页面只显示完成自身任务所需的最小关联信息。
3. 读取能力和写入能力分开。游戏写操作必须经过固定白名单、参数上限、会话、CSRF、幂等、审计、队列和结果复核。
4. 未经真实验证的能力不进入“已支持”，也不以模拟数据或无效按钮占位。
5. 模块间依赖由接口和 DTO 表达，不跨目录复制业务实现。

## 当前目录边界

```text
frontend-prototype/src/
  App.tsx                  会话级状态、轮询、通知
  app/AppRoutes.tsx        页面路由和懒加载
  pages/                   页面级业务编排
  components/              可复用展示与交互
  lib/                     API 客户端和无界面工具
  types.ts                 前端 API 类型

backend/src/
  AvorionAdmin.Api/
    Program.cs             唯一 composition root
    DependencyInjection/   全进程依赖注册
    Endpoints/             按业务域拆分的 HTTP 入口
    Security/              本机会话、CSRF/写保护、中间件
    Capabilities/          已验证命令/能力白名单
    Operations/            后台工作项、队列、Worker、调度
    Infrastructure/        API 通用适配与序列化帮助
  AvorionAdmin.Core/
    Abstractions/          Agent/存储/运行时接口
    Models/                API 与领域模型
    Configuration/         强类型配置
  AvorionAdmin.Agent/      游戏进程、RCON、SteamCMD、文件、SQLite 的真实实现

management-mod/OrionAdminBridge/data/scripts/
  commands/orionadmin.lua  薄路由与守卫
  lib/orionadmin/
    protocol.lua           帧协议、校验、能力声明
    players.lua            玩家查询
    playerassets.lua       Credits 与七种资源只读查询
    rewards.lua            受限单人资产奖励与精确前后余额复核
    mailrewards.lua        唯一邮件投递、附件复核与重放保护
    alliances.lua          联盟查询
    allianceassets.lua     联盟资产查询、受限奖励与精确前后余额复核
    inventory.lua          玩家/联盟库存读取与白名单系统插件发放
    sectors.lua            星区查询与受保护卸载
    dispatch.lua           固定动作登记与参数分派
```

奖励中心后端由 `Endpoints/GameManagementEndpoints.cs` 承担 HTTP 合同，由 `Operations/RewardBatchWorker.cs` 编排持久化父批次和逐目标子操作；游戏邮件能力经 `Core/Abstractions/IPlayerMailService.cs` 进入 Agent，不由端点直接拼 RCON 命令。

Inventory 后端由 `Endpoints/InventoryEndpoints.cs`、`Operations/InventoryGrantWorker.cs` 和 Core 的 `IInventoryService` 组成；前端只复用 `components/InventoryPanel.tsx` 嵌入玩家/联盟既有详情，不创建第二套玩家或联盟页面。MOD 内 `inventory.lua` 独立维护固定原版脚本清单、容量/槽位读取、请求 Seed 与游戏实际 Seed 的关联以及精确 +1 复核。

物品目录是同一 Inventory 领域内的只读子模块：`Core/Models/InventoryCatalogModels.cs` 定义目录与路径策略，`IInventoryCatalogService` 隔离 Agent，`Agent/InventoryCatalogService.cs` 负责固定定义、客户端发现和安全图标索引，`Endpoints/InventoryCatalogEndpoints.cs` 只暴露受会话保护的目录/图标合同。前端由 `InventoryCatalogDialog.tsx` 与 `GameItemIcon.tsx` 复用到现有 `InventoryPanel`，没有新增页面，也没有复制玩家在线状态或资产信息。

## 新增功能的固定接入方法

以“给玩家发放 Credits”为例，按以下顺序接入：

1. 在 `06_CAPABILITY_MATRIX.md` 确认官方依据、归属页面和状态；未确认的先停留在“待验证”。
2. 在 `AvorionAdmin.Core/Models` 增加请求、结果和审计 DTO，在 `Core/Abstractions` 增加最小接口。
3. 在管理 MOD 新增或扩展一个领域专属 `lib/orionadmin/*.lua`，只实现该领域的游戏内校验与调用；在 `protocol.lua` 声明精确能力，在薄路由登记固定 action。
4. 在 `AvorionAdmin.Agent` 实现 RCON 调用和响应复核。禁止从 API 端点直接拼任意控制台命令。
5. 写操作在 `Api/Operations` 增加工作项、队列和 Worker，并在 `Capabilities/VerifiedCommandRegistry.cs` 登记白名单。
6. 在最接近业务的 `Endpoints/*Endpoints.cs` 增加 HTTP 端点；若现有领域不合适，新建一个端点模块并在 `Program.cs` 显式注册。
7. 前端只在主要归属页面新增 API 封装、状态和按钮。单玩家/单联盟资产操作分别放在对应详情；批量奖励属于奖励中心，只复用目标选择与操作组件，不复制完整玩家或联盟列表。
8. 增加自动测试，并在隔离 Galaxy 做真实写入、失败、重试和重启恢复验证；之后才能把矩阵状态改为“已支持”。

读取类功能可以省略写队列，但仍必须有来源、超时、协议版本、分页/数量上限和不可用状态。

## 何时继续拆分

- 一个端点模块超过约 550 行时，按同一业务域的子能力拆分，例如 `Provisioning/SteamCmdEndpoints` 与 `Provisioning/ServerSetupEndpoints`。
- 页面同时承担两个独立用户目标时，拆出 feature 组件或独立页面；不要仅为了复用数据创建重复页面。
- 一个 Agent 实现同时操作 RCON、磁盘和数据库时，拆为独立服务，由 Worker 编排。
- 管理 MOD 的新 action 必须进入新的领域模块；命令入口保持在 100 行以内。

## 验证

在项目根目录运行：

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\check-architecture.ps1
dotnet build .\backend\AvorionAdmin.sln --no-restore
dotnet run --project .\backend\tests\AvorionAdmin.Tests --no-build
cd .\frontend-prototype
pnpm typecheck
pnpm test:sites
pnpm build
```

`01-prepare.cmd` 已自动先执行架构边界检查。边界检查只防止入口文件再次膨胀，不能替代业务测试和真实服务器验证。
