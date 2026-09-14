# V1.0 产品功能范围（冻结基础版）

> 2026-09-07：用户确认专用 OrionAdminBridge MOD，原禁止管理 MOD 的约束仅针对该组件解除。玩家/联盟/舰船/活动是分阶段目标，不是已实现能力；M1 仅只读桥接。参见 `17_MANAGEMENT_MOD_STAGE_M1.md`。

## 1. 首次安装与导入

首次打开 Web 管理系统提供三条路径：

### A. 新建服务器
- 系统检测
- 安装/检测 SteamCMD
- 通过 SteamCMD 安装 Avorion Dedicated Server
- Avorion Dedicated Server App ID：`565060`（实现时再次校验）
- 创建 Galaxy
- 图形化配置服务器名称、管理员、最大玩家、难度、密码、RCON
- 专用管理 MOD 采用独立流程；M1 在隔离环境验证，原安装向导不会自动安装组件。
- 网络/端口检查
- 启动并做健康检查

### B. 自动检测已有服务器
- 扫描常见路径
- 识别 AvorionServer.exe / server.ini / Galaxy / SteamCMD / MOD
- 展示检测结果，用户确认后导入

### C. 手动导入
- 用户选择 Avorion Server 安装路径
- 用户选择数据/Galaxy 路径
- 选择 SteamCMD 路径
- Agent 做完整自检

---

## 2. 总览
- 服务器状态
- 在线玩家数
- CPU / 内存 / 磁盘 / 网络
- 运行时间
- Avorion 版本
- MOD 概览
- 最近备份
- 服务器健康摘要
- 最近事件
- 在线玩家摘要
- 快捷操作：保存、安全重启、立即备份

---

## 3. 服务器

### 控制
- 启动
- 保存世界
- 安全关闭
- 安全重启
- 强制终止（危险操作，必须二次确认）
- 进程状态
- RCON 状态
- Steam Query 状态
- 最近保存

### 性能
- CPU
- RAM
- 磁盘空间
- 网络上下行
- 在线玩家数
- 运行时间
- 1h / 24h / 7d（后续可 30d）趋势
- 当前 / 平均 / 峰值
- 内存持续增长提醒

### 内存与星区
- 当前 RAM
- 启动时 RAM
- 增长趋势
- 已加载星区数量（可取得时）
- 有玩家星区/空闲星区列表（基于可获得信息）
- `Galaxy.tryUnloadSector(x, y)` 安全尝试卸载空闲星区
- 内存阈值告警
- 不提供假“清理 RAM”按钮
- 不做星区重置

### 更新
- SteamCMD 状态
- 当前版本
- 检查更新
- 更新服务端
- 验证文件
- 更新前广播
- 更新前备份
- 安全停服后更新
- 更新后启动
- 更新后健康检查

### 自动任务
- 定时保存
- 定时备份
- 定时安全重启
- 定时检查服务端更新
- 定时检查 MOD 更新
- 定时全服广播

### 备份与恢复
- 手动备份
- 定时备份
- 更新前备份
- 恢复前安全副本
- 保留策略
- 显示时间/大小/类型/状态
- 恢复流程：当前存档备份 → 停服 → 恢复 → 启动 → 健康检查

### 日志
- 实时日志
- 搜索
- Error / Warning / Player / MOD / RCON / Save 过滤
- 自动滚动
- 复制

### 诊断
- Avorion 进程
- 游戏端口
- Steam Query
- RCON
- SteamCMD
- Galaxy 路径
- 磁盘空间
- 最近保存
- 最近备份
- 内存趋势
- MOD 错误
- 输出易懂的中文诊断结果

---

## 4. 玩家

### 玩家列表
- 在线玩家
- 全部玩家
- 搜索
- 在线/离线
- playtime
- 所属联盟
- 当前星区

### 玩家详情
Tabs：
- 概览
- 资源
- 库存
- 舰船
- 操作记录

### 概览（只显示已确认字段）
- 玩家名
- 在线状态
- playtime
- allianceIndex / 所属联盟
- 当前星区
- 当前 craft（能可靠取得时）

### 资源
- Credits
- Iron
- Titanium
- Naonite
- Trinium
- Xanion
- Ogonite
- Avorion
- 给资源
- 扣资源

### Inventory
- 读取 Inventory
- 查看物品
- 添加受支持物品
- 删除受支持物品

### 舰船
- 玩家所有舰船名称
- 舰船位置
- 状态
- Plan
- Cargo
- Crew
- Systems
- Turret Designs
- 其他 ShipDatabaseEntry 已确认的安全只读字段可逐步加入

### 管理操作
- 发送聊天/系统消息
- Kick / Ban / Unban：通过 RCON/服务端命令能力实现，开发时先验证目标版本命令
- 管理员权限：通过服务器现有管理命令实现，开发时验证
- 所有高权限操作写审计日志

---

## 5. 奖励中心
- 新手礼包
- 维护补偿
- 活动奖励
- BUG 补偿
- 自定义礼包
- 单玩家/多玩家
- Credits + 七种矿物
- Inventory 物品仅在明确支持并经过限制后加入礼包

---

## 6. 联盟

### 联盟列表/概览
- Leader
- 成员
- 在线成员
- numShips
- numStations
- money

### 联盟详情 Tabs
- 概览
- 成员
- 权限
- 资源
- Inventory
- 舰船

### 成员与权限
- getMembers
- getOnlineMembers
- getMemberLocation
- getMemberRank
- setMemberRank
- addRank / removeRank
- addRankPrivilege / removeRankPrivilege
- hasPrivilege

### 资源
- Credits / 7 种矿物
- receive / receiveResource
- pay / payResource

### 舰船
- getShipNames
- getShipPosition
- getShipStatus
- getShipPlan
- getShipCargos
- getShipCrew
- getShipSystems
- getShipTurretDesigns

---

## 7. 舰船管理 / 蓝图库
- 玩家舰船
- 联盟舰船
- 搜索/筛选
- 舰船详情
- 蓝图库分类：采矿/战斗/运输/其他
- `LoadPlanFromFile` / `LoadPlanFromString`
- 给玩家创建舰船
- 给联盟创建舰船
- 批量创建多艘（“舰船礼包”）
- 创建时直接指定目标 Player/Alliance Faction

明确不叫“自动编队舰队”，除非后续再确认完整编队行为。

---

## 8. 游戏管理

### 聊天
- 接收玩家聊天 callback
- 管理员通过 Web 给玩家/游戏发送消息

### 实时事件
- 玩家登录/退出
- 聊天
- 玩家切换星区
- 玩家资源变化
- 玩家切换舰船
- 联盟 Leader/Member/Rank/Resource/Ship 变化等官方 Callback
- 管理员操作
- 服务器运维事件

### 活动中心
第一版模板：
- 海盗袭击
- 敌对派系袭击
- 波次防御
- 悬赏目标

基础实现能力已确认：
- `Galaxy.getPirateFaction()`
- `Sector.createShip()`
- `ShipAI.registerEnemyFaction()`
- `ShipAI.setAggressive()`
- `ShipAI.setAttack()`

活动完成后的 Credits/矿物奖励使用 Player/Faction receive API。

### 创建空间站
- `Sector.createStation(faction, plan, ...)`
- 可给目标 Player / Alliance 创建
- 第一版只做“创建”，不做完整产业链远程管理

---

## 9. MOD
- 只读扫描服务器已配置或已存在的 MOD
- 展示可可靠读取的 Workshop ID、本地存在状态、版本线索和相关日志
- 不提供 MOD 植入、上传、安装、启用/禁用、更新或删除写操作
- 无法从真实文件、配置、Workshop 元数据或日志确认时显示“不可用”，不得使用 Mock 数据补齐
- 加载错误
- 日志过滤
- 更新失败提示
- 不做“自动修复 MOD 冲突”

---

## 10. 文件与配置

### 文件管理
- 浏览
- 文本查看/编辑
- 上传/下载
- 新建目录
- 重命名
- 删除高风险文件必须二次确认
- 关键目录保护

### server.ini 图形化配置
- 中文名称
- 中文解释
- 当前值
- 常用/高级/专家分级
- 标出“需重启生效”
- 搜索设置

### 配置历史
- 每次修改前快照
- 修改者/时间/改动项
- 恢复历史版本

### 配置差异
- 只显示不同项
- 当前值 vs 对比值

---

## 11. 管理员 / 安全
- 超级管理员
- 服务器管理员
- GM
- 只读
- 第一版可只有一个超级管理员，但数据模型不能写死单用户
- HTTPS
- 安全密码哈希
- 会话过期
- 高风险操作二次确认
- 审计日志
- 管理 API 不直接裸露到公网
- RCON 密码不明文显示/记录
