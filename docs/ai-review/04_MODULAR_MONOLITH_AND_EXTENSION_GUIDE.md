# 模块化单体与扩展维护指南

## 1. 结论

源码方向已经确定为“模块化单体”，不改成微服务：一个本机程序、一个 API 进程、一个前端、一个专用管理 MOD、一个 SQLite 数据库和一个部署包。

当前健康度不是简单的“已经完全模块化”：

- 后端：健康。`Program.cs` 约 48 行，只做组合；17 个端点模块均低于 550 行，最长 `ProvisioningEndpoints.cs` 455 行。
- 管理 MOD：健康。命令入口是薄路由，10 个领域模块中最长 `inventory.lua` 277 行。
- 前端路由：健康。`App.tsx` 不堆路由，`AppRoutes.tsx` 做懒加载和页面装配。
- 前端页面：混合。10 个页面在 300–497 行之间；`ServerUpdatePage.tsx` 已达到 2403 行，是当前最大技术债。

所以“以后加功能会不会麻烦”的答案是：按本指南接入就不会越来越乱；但必须先拆更新页，且新大功能不能继续只用一个 page 文件承载全部状态、合同和交互。

## 2. 当前模块地图

```text
frontend-prototype/src/
  App.tsx                     会话级状态、通知、顶层轮询
  app/AppRoutes.tsx           路由、懒加载、页面装配
  pages/                      页面壳与页面级编排
  components/                 跨页面或领域内复用组件
  lib/api.ts                  HTTP 客户端
  types.ts                    前端 API 类型

backend/src/
  AvorionAdmin.Api/
    Program.cs                唯一 composition root
    DependencyInjection/      全进程依赖注册
    Endpoints/                按业务域拆分 HTTP 合同
    Security/                 会话、CSRF、写保护、中间件
    Capabilities/             已验证命令/能力白名单
    Operations/               Operation、队列、Worker、调度
    Infrastructure/           通用序列化和 API 适配
  AvorionAdmin.Core/
    Abstractions/             Agent、存储、运行时接口
    Models/                   领域模型、请求、结果、审计 DTO
    Configuration/            强类型配置
  AvorionAdmin.Agent/         RCON、SteamCMD、进程、文件、SQLite 的真实实现

management-mod/OrionAdminBridge/data/scripts/
  commands/orionadmin.lua     守卫 + 固定 action 路由
  lib/orionadmin/
    protocol.lua              协议、版本、帧、能力声明
    players.lua               玩家查询
    playerassets.lua          玩家资产读取
    rewards.lua               玩家资产奖励
    mailrewards.lua           游戏内邮件奖励
    alliances.lua             联盟查询
    allianceassets.lua        联盟资产和奖励
    inventory.lua             Inventory 读取和白名单发放
    sectors.lua               星区读取和受保护卸载
    dispatch.lua              固定动作登记与参数分派
```

## 3. 当前文件规模证据

### 后端 HTTP 模块

| 文件 | 行数 |
|---|---:|
| `ProvisioningEndpoints.cs` | 455 |
| `InventoryEndpoints.cs` | 164 |
| `PlayerEndpoints.cs` | 152 |
| `AllianceEndpoints.cs` | 138 |
| `SectorEndpoints.cs` | 134 |
| `ServerControlEndpoints.cs` | 133 |
| `UpdateCommandEndpoints.cs` | 131 |
| 其余 10 个端点模块 | 52–117 |

### 前端页面

| 文件 | 行数 | 判断 |
|---|---:|---|
| `ServerUpdatePage.tsx` | 2403 | 必须拆分 |
| `AllianceManagementPage.tsx` | 497 | 接近需要拆 feature 组件的区间 |
| `ServerPerformancePage.tsx` | 389 | 可接受 |
| `PlayerManagementPage.tsx` | 387 | 可接受，但新增舰船等功能必须走独立 feature |
| `ServerDiagnosticsPage.tsx` | 374 | 可接受 |
| 其他 6 个页面 | 300–335 | 可接受 |

### 管理 MOD 模块

| 文件 | 行数 |
|---|---:|
| `inventory.lua` | 277 |
| `mailrewards.lua` | 126 |
| `allianceassets.lua` | 112 |
| `rewards.lua` | 100 |
| `protocol.lua` | 99 |
| 其余 5 个模块 | 36–95 |

## 4. 强制依赖规则

### 4.1 后端

允许的方向：

```text
Endpoints -> Core abstractions/models
Workers   -> Core abstractions/models + stores
Agent     -> Core abstractions/models
Program   -> dependency registration + endpoint mapping only
```

禁止：

- 在 `Program.cs` 写路由体或业务规则。
- 在 Endpoint 里拼任意 RCON/Lua/控制台命令。
- Endpoint 直接执行长时间安装、更新、恢复或游戏写操作。
- 一个领域直接读取另一个领域的私有 store 或复制其业务实现。
- 因为前端需要一个字段，就把未验证游戏能力加入通用命令入口。

### 4.2 管理 MOD

- `commands/orionadmin.lua` 只做调用来源守卫、协议解析、固定 action 查找和返回。
- 每个新游戏领域新增/扩展一个 `lib/orionadmin/*.lua`，不能把游戏逻辑塞回命令入口。
- 所有 action 必须显式登记，不允许任意函数名、任意 Lua 片段或任意脚本路径。
- 读写都要限制响应大小、分页/数量、参数范围、协议版本和请求编号。
- 写操作必须能返回足够数据供 Agent 复核，而不是只回一个 `ok`。

### 4.3 前端

- `App.tsx` 只负责会话级状态和全局通知。
- `AppRoutes.tsx` 只负责路由、懒加载和页面装配。
- 页面只负责页面级编排；复杂领域状态进入 `features/<domain>/hooks`。
- API 调用集中到 `features/<domain>/api` 或已有 `lib/api.ts` 的明确分区，不在展示组件散落 `fetch`。
- 可复用组件必须有清晰归属：通用组件放 `components/ui`，领域组件放 `features/<domain>/components`。
- 一个功能只在一个主要页面出现；跨页面只复用目标选择器、状态徽章等最小组件，不复制整页。

## 5. 前端目标结构

不要求一次迁移所有文件。先从更新页和下一批新功能开始：

```text
frontend-prototype/src/
  app/
    AppRoutes.tsx
  components/
    ui/
    AppShell.tsx
  features/
    server-update/
      api/
      components/
        UpdateEntrySummary.tsx
        InstallPlanStep.tsx
        ExistingServerPathsStep.tsx
        BasicSetupStep.tsx
        EnvironmentValidation.tsx
        UpdateCheckPanel.tsx
        SafetyPointPanel.tsx
      hooks/
        useUpdateSetupFlow.ts
        useUpdateOperations.ts
      model/
        types.ts
        validation.ts
      index.ts
    inventory/
      api/
      components/
        InventoryPanel.tsx
        ItemWorkbench.tsx
        ItemCatalogList.tsx
        ItemInspector.tsx
        TurretGenerationForm.tsx
      hooks/
      model/
    ships/
    game-events/
    sector-construction/
  pages/
    ServerUpdatePage.tsx       只装配 server-update feature
    PlayerManagementPage.tsx
    AllianceManagementPage.tsx
    ShipManagementPage.tsx
    GameManagementPage.tsx
```

目标不是追求越多文件越好，而是让每个文件只承担一种变化原因。拆分后 `ServerUpdatePage.tsx` 建议低于约 250 行；流程状态、请求、视图步骤可以独立测试。

## 6. 新功能固定接入流程

### 6.1 先定产品归属

1. 在 `06_CAPABILITY_MATRIX.md` 找到或新增能力。
2. 标记为已确认可开发或待验证，记录官方依据。
3. 确定唯一主页面和允许引用的最小上下文。
4. 明确读操作还是写操作；写操作先写安全边界。

### 6.2 定合同，不从 UI 直接倒推实现

1. 在 Core Models 定义请求、预览、执行结果和审计 DTO。
2. 在 Core Abstractions 定义最小接口。
3. 对写操作明确：额度、数量、白名单、确认文本、幂等语义、可重试性和最终复核字段。
4. 定义不可用、超时、冲突、不确定结果和部分成功的错误合同。

### 6.3 接入游戏能力

1. 在 OrionAdminBridge 新增领域模块或扩展正确模块。
2. 在协议中声明精确 capability。
3. 在薄路由中登记固定 action。
4. Agent 实现 RCON/文件/进程调用和严格响应校验。
5. 不让 HTTP 层知道 Lua 命令细节。

### 6.4 写操作进入 Operation

1. API 创建持久 Operation 或父/子 Operations。
2. Worker 从队列执行，记录阶段、进度、错误和不可确定状态。
3. 幂等键防止重复写；同键不同参数返回冲突。
4. 网络或 RCON 结果不确定时不自动重试高风险动作。
5. 通过重新读取资产、Inventory、实体或文件哈希验证最终结果。

### 6.5 前端只装配领域 feature

1. 在主要归属页加入入口。
2. 高密度工作流使用列表/网格 + 详情检查器，不连续打开弹窗。
3. 明确加载、空态、不可用、部分成功、失败和不确定结果。
4. 复用最小目标选择器，不复制玩家/联盟/舰船完整列表。

### 6.6 通过验证才更新“已支持”

1. 单元/合同/安全边界测试。
2. 架构边界检查。
3. 1440×900 与 1920×1080 UI 检查。
4. 隔离 Galaxy 真正执行成功、拒绝、幂等重放、错误、重启保存。
5. 更新 `02_CURRENT_STATUS.md`、`06_CAPABILITY_MATRIX.md`、相关设计 QA 和证据。

## 7. 示例：普通炮塔生成与单件发放

这是下一阶段最适合验证模块化模式的垂直切片。

### 应新增/调整的职责

- `Core/Models`：炮塔类型、材质、科技、稀有度、Seed、生成预算、预览结果、发放结果 DTO。
- `Core/Abstractions`：`ITurretGenerationService` 或 Inventory 领域中的最小炮塔接口。
- `Agent`：把固定参数转成管理 MOD action，验证响应类型和边界。
- `Api/Endpoints/InventoryEndpoints.cs`：预览和创建 Operation 的 HTTP 合同；如果明显增长，再拆 `TurretInventoryEndpoints.cs`，仍属于 Inventory 领域。
- `Api/Operations/TurretGrantWorker.cs`：执行单件发放、关联请求 Seed/游戏实际 Seed、复核 before/after。
- `Capabilities`：只登记 19 种普通炮塔和确认过的材质/科技/稀有度范围。
- `management-mod/.../inventory.lua` 或新的 `turrets.lua`：调用原版生成器、包装 `InventoryTurret`、加入 Inventory、返回实际参数和增量。
- `frontend/features/inventory`：三栏物品工作台、右栏参数表单、生成预览、二次确认、Operation 结果。

### 不应该做

- 把炮塔生成器写进 `Program.cs` 或 React 页面。
- 开放任意炮塔脚本路径、任意 Lua 字符串或任意数值。
- 因为目录已有 19 个图标就宣称 19 种都可发放。
- 在奖励中心复制完整 Inventory 页面。
- 一次发放多件或失败后自动重试，直到单件真实验证完成。

## 8. 数据与版本维护

- API 合同要有明确版本或向后兼容策略；前端和 Agent 不依赖未声明字段。
- SQLite 结构变化使用顺序迁移，迁移必须幂等并有旧数据库测试。
- Operation 记录保留类型、目标、规范化请求摘要、阶段、时间、错误和结果引用；不要存明文密码。
- 管理 MOD 协议版本和 capability 精确匹配；前端不能只凭版本号猜能力。
- 每个功能在能力矩阵中只有一个状态来源；页面文案不能自行升级状态。
- 图标继续运行时发现和白名单读取，版权未确认前不把客户端全部资源复制进发行包。

## 9. 完成定义

一个新功能只有同时满足以下条件才能称为完成：

- 产品归属和非重复边界已写清楚。
- 官方依据或真实能力依据已记录。
- HTTP、Core、Agent、MOD 的职责没有跨层。
- 写操作有白名单、上限、会话、CSRF、幂等、审计、队列和结果复核。
- 有成功、拒绝、错误、不确定结果和重放测试。
- 有隔离 Galaxy 的真实证据；仅 UI/Mock/单元测试不够。
- 有两档桌面视觉 QA，无横向溢出和未处理页面错误。
- 文档和能力矩阵已更新。

## 10. 当前自动检查

项目已有 `tools/check-architecture.ps1`，`01-prepare.cmd` 会在构建前调用。2026-09-10 本次重新验证：

```text
PASS: modular-monolith architecture boundaries are intact.
.NET build: 0 warnings, 0 errors.
Backend assertions: PASS.
TypeScript typecheck: PASS.
Frontend route/hosting tests: 4/4 PASS.
Vite production build: PASS.
```

当前边界检查可以防止入口文件重新膨胀，但还需要新增前端约束，例如限制页面文件规模、禁止 page 直接散落 HTTP 调用，并把 `ServerUpdatePage.tsx` 拆分列为下一次架构检查的验收项。
