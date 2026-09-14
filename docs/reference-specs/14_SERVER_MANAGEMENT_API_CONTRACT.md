# 服务器管理 API Contract（待确认草案）

> 版本：0.3.0  
> 日期：2026-08-30  
> 范围：服务器管理的控制、性能、内存与星区、更新、自动任务、备份、日志、诊断  
> 当前状态：合同持续实施中。2026-09-07 用户确认专用管理 MOD，新增的 M1 桥接接口见 `17_MANAGEMENT_MOD_STAGE_M1.md`；现有初始化兼容字段仍保持 false，组件安装采用独立流程。

## 1. 目标

把已经确认的服务器管理 UI 从 Mock 原型切换为可验证的真实数据。浏览器只访问经过认证的 Web API；Windows 进程、文件、SteamCMD、日志、Steam Query 和 RCON 必须由服务器本机的 Server Agent 处理。游戏内数据通过专用管理 MOD 逐项验证，未验证的能力返回不可用。

本合同的第一原则：**取不到真实数据时返回未知或不可用，不允许返回前端默认值冒充正常。**

## 2. V1 部署边界

```text
React 浏览器前端
      │ HTTPS + Session + CSRF
      ▼
ASP.NET Core Web API
      │ 认证后的内部连接
      ▼
Server Agent（Windows Service，服务器本机）
 ├─ Windows 进程与性能计数器
 ├─ 文件系统 / Galaxy / 备份 / 日志
 ├─ SteamCMD
 ├─ Steam Query
 ├─ RCON
  └─ SQLite：性能历史、任务、审计、操作状态
```

- Web API 不直接把 Agent、RCON 或文件系统暴露到公网。
- 所有资源路径都带 `serverId`，V1 可以只有一个服务器，但数据模型不写死单服务器。
- Agent 离线时 Web UI 仍可打开，但相关数据必须显示“Agent 未连接”。

## 3. 真实数据规则

所有读取响应必须包含：

| 字段 | 类型 | 规则 |
|---|---|---|
| `sampledAt` | ISO 8601 UTC | 实际采样时间，不使用“刚刚”作为后端值 |
| `source` | 枚举 | `windows` / `filesystem` / `steamcmd` / `steam-query` / `rcon` / `database` |
| `freshness` | 枚举 | `live` / `recent` / `stale` / `unavailable` |
| `unavailableReason` | string/null | 数据不可用时给中文可映射错误码，不伪造数值 |

其他强制规则：

1. 数值使用基础单位：字节、百分比、毫秒、秒；中文格式化由前端完成。
2. 所有时间由后端返回 UTC，前端按管理员时区显示。
3. `null` 表示确实无法取得；不能用 `0` 代替未知。
4. 历史性能来自 Agent 定时采样写入 SQLite，不由前端随机生成。
5. 网络数据必须说明 `scope`：`host` 或 `process`。Windows 无法可靠取得进程级网络时，V1 返回配置网卡的主机级数据并明确标识。
6. 最近保存时间来自实际 Galaxy/存档文件修改时间或已验证日志事件，并同时返回来源。
7. 备份列表只能来自配置目录中的真实备份文件扫描；不编造备份类型、来源或计数。
8. 日志必须保留原始行和解析类别；解析失败使用 `Other`，不能丢弃原始内容。

## 4. 通用协议

### 4.1 Base URL

`/api/v1`

### 4.2 身份与防护

- 浏览器使用 `HttpOnly + Secure + SameSite=Strict` 会话 Cookie。
- 所有变更接口要求 `X-CSRF-Token`。
- 所有危险或可重试操作要求 `Idempotency-Key`。
- RCON 密码、管理员密码、文件系统秘密不得出现在响应、日志或审计详情中。
- 每次请求返回 `X-Request-Id`；每次管理操作写审计日志。

### 4.3 错误格式

```json
{
  "error": {
    "code": "AGENT_OFFLINE",
    "message": "服务器节点当前未连接",
    "requestId": "req_01J...",
    "retryable": true,
    "details": {}
  }
}
```

基础错误码：

- `AGENT_OFFLINE`
- `SERVER_NOT_FOUND`
- `CAPABILITY_UNAVAILABLE`
- `STALE_DATA`
- `VALIDATION_FAILED`
- `CONFLICTING_OPERATION`
- `RCON_UNAVAILABLE`
- `STEAMCMD_UNAVAILABLE`
- `FILE_NOT_FOUND`
- `BACKUP_NOT_AVAILABLE`
- `COMMAND_NOT_VERIFIED`
- `OPERATION_FAILED`
- `FORBIDDEN`

### 4.4 长操作

启动、关闭、重启、备份、恢复、更新、验证和诊断统一返回 `202 Accepted`：

```json
{
  "operationId": "op_01J...",
  "status": "queued",
  "acceptedAt": "2026-08-30T08:00:00Z"
}
```

前端通过 `GET /operations/{operationId}` 查询：

- `queued`
- `running`
- `succeeded`
- `failed`
- `cancelled`

不得由前端计时器自行判定操作成功。

## 5. 能力与实现门禁

| 能力 | 真实来源 | 当前证据 | 实现门禁 |
|---|---|---|---|
| 进程启动/状态/强制终止 | Windows Agent | 系统能力明确 | 可实现；强制终止需二次确认 |
| CPU/RAM/磁盘/网络 | Windows Agent | 系统能力明确 | 可实现；网络必须声明范围 |
| Steam Query | Agent 探测 | 标准服务探测 | 可实现；超时返回不可用 |
| RCON 连接状态 | Agent RCON 客户端 | 架构已确认 | 可实现；秘密不得回传 |
| 保存/安全关闭/安全重启 | RCON/控制台命令 | 具体目标版本命令尚未在交接包验证 | 先验证命令，再开放真实按钮 |
| SteamCMD 状态/更新/验证 | Windows Agent + SteamCMD | 系统能力明确 | 更新必须执行广播、备份、停服和健康检查流程 |
| 备份扫描/创建/恢复 | 文件系统 Agent | 系统能力明确 | 目录白名单、路径穿越防护、恢复二次确认 |
| 日志读取与实时追踪 | 文件系统 Agent | 系统能力明确 | 只读白名单文件；SSE 断线可续传 |
| 加载星区与安全卸载 | 当前无非侵入式可靠来源 | 不在当前范围 | 显示不可用，不植入 MOD、不伪造结果 |
| MOD 错误 | 实际日志解析 | 可由日志获得 | 无可靠日志时返回未知 |

## 6. 页面接口

### 6.1 全局服务器信息

#### `GET /servers`

返回管理员有权访问的服务器列表、Agent 在线状态和最小摘要。

#### `GET /servers/{serverId}/status`

供全局 Shell 和“控制”页读取：

- `lifecycle`: `running | stopped | starting | stopping | restarting | unknown`
- `processId`
- `version`
- `galaxyName`
- `uptimeSeconds`
- `onlinePlayers` / `maxPlayers`
- `rcon.status`
- `steamQuery.status`、`latencyMs`
- `lastSave.at`、`lastSave.source`
- `agent.connected`、`agent.lastHeartbeatAt`
- `sampledAt`、`freshness`

### 6.2 控制

#### `GET /servers/{serverId}/events?limit=20`

返回控制页“最近事件”的真实记录。来源可以是结构化日志、Agent 操作或管理审计，但每条必须带 `occurredAt` 和 `source`；不得继续使用前端固定事件数组。

| 方法 | 路径 | 作用 | 风险 |
|---|---|---|---|
| `POST` | `/servers/{serverId}/actions/start` | 启动 Avorion 进程并健康检查 | 中 |
| `POST` | `/servers/{serverId}/actions/save` | 保存世界 | 命令验证后开放 |
| `POST` | `/servers/{serverId}/actions/shutdown` | 保存后安全关闭 | 高，二次确认 |
| `POST` | `/servers/{serverId}/actions/restart` | 保存、关闭、启动、健康检查 | 高，二次确认 |
| `POST` | `/servers/{serverId}/actions/force-stop` | 终止进程 | 极高，强制二次确认 |

每个接口返回 Operation；同一服务器同时只允许一个互斥生命周期操作。`force-stop` 请求必须带：

```json
{
  "confirmation": "FORCE_STOP",
  "reason": "管理员手动强制终止"
}
```

### 6.3 性能

#### `GET /servers/{serverId}/performance/current`

- CPU：`processPercent`、可选 `hostPercent`
- RAM：`workingSetBytes`、`privateBytes`、`hostTotalBytes`
- 磁盘：Galaxy 所在卷 `totalBytes`、`usedBytes`、`availableBytes`
- 网络：`downloadBytesPerSecond`、`uploadBytesPerSecond`、`scope`
- 在线玩家、运行时间
- 每项独立 provenance，部分指标失败时不让整页失败

#### `GET /servers/{serverId}/performance/history?range=1h|24h|7d`

返回按时间排序的真实采样点；空历史返回空数组和 `INSUFFICIENT_HISTORY`，不生成折线。

### 6.4 内存与星区

#### `GET /servers/{serverId}/memory`

- 当前 RAM、启动基线、增长值、增长速率
- 告警阈值和当前告警级别
- 已加载星区总数、玩家星区数、空闲星区数（MOD 可用时）
- 数据来源与采样时间

#### `GET /servers/{serverId}/sectors?filter=all|players|idle|eligible&sort=loaded|idle|memory`

星区字段只包含可可靠取得的内容：坐标、显示名称（若可得）、玩家数、空闲时间、估算内存（若不可可靠取得则 `null`）、是否满足卸载规则和不可卸载原因。

#### `POST /servers/{serverId}/sectors/unload-attempts`

请求体为坐标数组。Agent/MOD 必须在执行前再次确认没有玩家和受保护任务，然后调用已验证的 `Galaxy.tryUnloadSector(x,y)`；逐项返回 `unloaded | skipped | failed`。

#### `GET/PATCH /servers/{serverId}/memory-policy`

允许：

- `notify`
- `try-unload-idle-sectors`
- `safe-restart-at-critical`（默认关闭）

禁止提供“强制释放全部内存”或伪清理结果。

### 6.5 更新

#### `GET /servers/{serverId}/server-setup/draft`

返回本机 SQLite 中的非敏感配置草稿；未保存时返回 `draft: null`。禁止包含 RCON 密码。

#### `PUT /servers/{serverId}/server-setup/draft`

保存服务器名、Galaxy、端口和选择项等非敏感字段；需要会话与 CSRF。不写游戏配置，不启动服务端。

#### `POST /servers/{serverId}/server-setup/preflight`

真实检查更新环境、Galaxy 本地路径、字段约束和本机 TCP/UDP 端口占用。RCON 密码只在单次请求内存中验证，不持久化、不记录、不回传。预检不会修改防火墙、安装 MOD、写 `server.ini` 或启动服务端。

#### `POST /servers/{serverId}/server-setup/applications`

创建幂等的 `server.configure` Operation。后端必须重新预检并确认精确 `AvorionServer.exe` 未运行；始终原子写入不含秘密的受控启动档案。已有 Galaxy 且 `server.ini` 结构无歧义时，只更新 `[Networking]` 的 `port`、`rconIp`、`rconPassword`、`rconPort` 与 `[Administration]` 的 `maxPlayers`、`name`，写后校验，失败回滚。新 Galaxy 不提前伪造 `server.ini`，RCON 延后至首次初始化后。监听地址、独立 Query 端口、防火墙与服务端启动不属于本 Operation；自定义管理 MOD 不属于产品能力，兼容字段必须强制为 `false`。

#### `POST /servers/{serverId}/server-setup/initializations`

创建幂等的 `server.initialize` Operation，仅适用于启用 RCON 的全新 Galaxy。后端重新预检并核验受控启动档案哈希与固定参数后，启动精确 `AvorionServer.exe`，等待服务端自行生成 `server.ini`，通过标准输入依次发送 `/save`、`/stop` 并确认进程安全退出。只有停服确认后才允许原子写入 RCON；随后重启服务端并执行真实 RCON 认证探针。Operation 成功结果必须包含进程 ID、控制台保存/停服确认、RCON 配置/认证布尔值与证据代码，但不得包含 RCON 密码。

#### `GET /servers/{serverId}/updates/status`

返回 SteamCMD 路径与可用性、当前版本、最新可确认版本、最后检查时间、服务器运行状态和更新准备项。

#### `POST /servers/{serverId}/updates/checks`

只检查，不安装；返回可恢复 Operation。Agent 每次重新验证已保存路径与 SteamCMD 的 Valve Authenticode 签名，只能以参数数组执行匿名 `app_info_print 565060`，解析 `public` 分支 Build ID，并与本机 `appmanifest_565060.acf` 比较。不得调用 `app_update`，不得修改服务端文件；SteamCMD 自身按 Valve 机制更新不等同于游戏服务端更新。

#### `POST /servers/{serverId}/updates/verifications`

执行本机只读文件校验；返回可恢复 Operation。后端重新验证路径，并检查 `AvorionServer.exe`、`ServerRunner.exe` 的存在性、读取权限、最小大小、Windows PE 文件头与 SHA-256，同时读取可用的本机 Build ID。该接口明确不运行 SteamCMD `app_update validate`，不下载、修复或覆盖任何服务端文件，因此可以在服务器运行时调用。

#### `GET /servers/{serverId}/updates/rollback-points/latest`

读取与当前服务端路径匹配、清单 SHA-256 有效的最近更新安全点。该记录独立于 Avorion 自动生成的 `.bak` 存档备份。

#### `POST /servers/{serverId}/updates/rollback-points`

仅在服务端已停止时创建更新前安全点：复制完整服务端安装目录和受管 Galaxy，拒绝重解析点，逐文件 SHA-256 复核后原子发布。此接口只创建可验证恢复来源，不执行恢复，也不开始更新。

#### `POST /servers/{serverId}/updates`

完整受控流程：广播 → 备份 → 保存 → 安全停服 → SteamCMD 更新 → 启动 → 健康检查。任一步失败必须停止后续危险步骤并保留操作日志。

### 6.6 自动任务

#### `GET /servers/{serverId}/tasks`

#### `POST /servers/{serverId}/tasks`

#### `PATCH /servers/{serverId}/tasks/{taskId}`

产品模型预留六种任务，但当前真实调度器只开放已完成命令验证的两种：

- `scheduled-save`
- `scheduled-safe-restart`

`scheduled-backup`、`check-server-update`、`check-mod-update`、`broadcast` 在相应真实命令通过验证前必须返回 `INVALID_TASK_SCHEDULE`，不得在浏览器本地伪执行。

任务保存在 SQLite，由 Agent/后端调度；当前计划只接受白名单结构：定时保存为 15/30/60/120 分钟间隔，安全重启为每周日期与 `HH:mm`，时区固定为 `Asia/Shanghai`。响应包括启用状态、下一次执行时间、最后 Operation 状态和错误。不得只保存在浏览器内存中或接受任意计划文字。

### 6.7 备份

#### `GET /servers/{serverId}/backups?cursor=...&limit=50`

只扫描白名单备份目录中的真实文件，返回：

- `backupId`（后端生成，不暴露任意文件路径）
- `total` 与 `totalSizeBytes`（来自本次真实文件扫描）
- `fileName`
- `createdAt`
- `sizeBytes`
- `status`: `available | incomplete | unreadable | restoring`

不返回无法从文件可靠判断的“备份类型/来源”。

#### `POST /servers/{serverId}/backups`

创建备份，完成后再次扫描文件并返回 Operation 结果中的 `backupId`。

#### `POST /servers/{serverId}/backups/refresh`

重新扫描目录；不由前端重复显示旧数组。

#### `POST /servers/{serverId}/backups/{backupId}/restore`

强制流程：验证文件 → 创建当前存档安全副本 → 保存 → 停服 → 恢复 → 启动 → 健康检查。请求需二次确认文本和 Idempotency-Key。

### 6.8 日志

#### `GET /servers/{serverId}/logs?cursor=...&limit=200&category=...&query=...`

用于首次加载、搜索和断线补偿。过滤类别：`Error | Warning | Player | MOD | RCON | Save | Other`。

#### `GET /servers/{serverId}/logs/stream`

使用 SSE 实时推送；支持 `Last-Event-ID` 续传。每条记录包含：

- `id`
- `occurredAt`
- `category`
- `message`
- `rawLine`
- `sourceFile`
- `parseConfidence`

断开时 UI 显示“连接中断”，不能继续用定时器生成日志。

### 6.9 诊断

#### `POST /servers/{serverId}/diagnostics/runs`

创建一次真实诊断 Operation。检查项固定为已经确认的 11 项：

1. Avorion 进程
2. 游戏端口
3. Steam Query
4. RCON
5. SteamCMD
6. Galaxy 路径
7. 磁盘空间
8. 最近保存
9. 最近备份
10. 内存趋势
11. MOD 错误

#### `GET /servers/{serverId}/diagnostics/runs/{runId}`

每项返回 `healthy | warning | error | unknown`、中文结果码、真实证据、采样时间与来源。某项无法检查时返回 `unknown`，不能默认标绿。

## 7. 前端 Mock 替换映射

| 当前 Mock 位置 | 替换接口 |
|---|---|
| `src/data/mock.ts` 中的 `serverMeta` | `GET /servers/{serverId}/status` |
| `src/data/mock.ts` 中的 `initialEvents` | `GET /servers/{serverId}/events` |
| `performanceSeries` | `GET /performance/current` + `/performance/history` |
| `memorySeries` / `sectorRecords` | `GET /memory` + `/sectors` |
| `App.tsx` 的控制计时器 | `/actions/*` + `/operations/{id}` |
| 更新页计时器 | `/updates/checks`、`/updates/verifications`、`/updates` |
| 自动任务本地数组 | `/tasks` |
| 备份页 `initialBackups` 和恢复计时器 | `/backups` + restore Operation |
| 日志页固定数组和 `setInterval` | `/logs` + `/logs/stream` |
| 诊断页固定检查数组 | `/diagnostics/runs` |

接入真实 API 后，页面上的“Mock 数据正常”必须删除，替换为真实连接状态：`实时`、`数据延迟`、`Agent 未连接`或`能力不可用`。

## 8. 第一轮实现顺序

### R0：基础设施

1. ASP.NET Core 解决方案、Windows Service 宿主、SQLite。
2. Agent 注册、心跳、服务器配置和路径白名单。
3. Operation 状态机、审计日志、统一错误格式。

### R1：只读真实闭环

1. 服务器状态。
2. 当前性能与历史采样。
3. 真实备份文件列表。
4. 实时日志与断线续传。
5. 11 项真实诊断。

### R2：低到中风险操作

1. 启动进程。
2. 创建备份。
3. 更新检查和文件验证。
4. 自动任务 CRUD 与调度。
5. MOD 已连接时的安全星区卸载。

### R3：高风险操作

1. 保存、关闭和重启命令完成目标版本验证。
2. 完整更新流程。
3. 备份恢复。
4. 强制终止。

## 9. R1 验收标准

- 断开 Agent 后，所有页面在 5 秒内显示不可用，不保留伪“正常”。
- 修改真实 Avorion 进程状态后，控制页在规定刷新时间内正确变化。
- 性能趋势全部来自 SQLite 采样；空历史不生成假线。
- 备份目录新增/删除文件后，刷新结果与实际目录一致。
- 新日志写入后通过 SSE 到达浏览器；断线重连不重复、不丢失可恢复范围内的日志。
- 诊断 11 项可逐项追溯到真实来源；不可检查项显示未知。
- 所有响应和审计中均不出现 RCON 密码或管理员秘密。
- 未经二次确认，任何高风险操作不得执行。

## 10. 本轮明确不实现

- 不在本轮直接连接或控制用户的 Avorion 服务器。
- 不在命令未验证前实现保存、安全停服或安全重启。
- 不实现伪 RAM 清理、星区重置、自动修复 MOD 冲突或交接包明确禁止的功能。
- 不把 Agent 端口、RCON 或文件路径直接暴露给浏览器。

用户确认本合同后，下一步只启动 R0 与 R1；R2/R3 需要在真实测试环境和回滚方案准备完成后再进入。
