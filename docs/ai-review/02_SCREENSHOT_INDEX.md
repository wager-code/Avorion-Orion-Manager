# 全部页面截图索引

## 1. 采集说明

- 采集时间：2026-09-10 10:12（Asia/Shanghai）。
- 来源：当前本机运行的 OrionAdmin，未 Mock 应用 API。
- 视口：1440×900（全页截图）和 1920×1080（首屏截图）。
- 页面数：11 个实际路由，共 22 张当前状态截图。
- 横向溢出：0。
- JavaScript page error：0。
- Avorion 服务端在采集时已停止，因此内存/星区、玩家、联盟、奖励目标请求出现预期 503；页面有明确不可用说明。
- 本轮没有执行危险写操作，也没有覆盖手机、键盘全流程、屏幕阅读器或运行态全部交互。

## 2. 页面清单与健康判断

| # | 页面 | 路由 | 一般健康度 | 本轮重点观察 |
|---:|---|---|---|---|
| 1 | 服务器控制 | `/server/control` | 良好 | 停止态、关键状态和启动动作清楚；首屏信息略多但结构稳定 |
| 2 | 性能 | `/server/performance` | 良好，需优化语义 | 历史曲线与当前停服状态并存，需更明显标注采样中断 |
| 3 | 内存与星区 | `/server/memory` | 良好，停服降级 | 真实 503 被正确转成不可用；空态面积偏大 |
| 4 | 更新 | `/server/update` | 可用，需简化 | 信息层级清楚；“进入更新管理”仍处于同一更新页，流程命名略绕 |
| 5 | 自动任务 | `/server/tasks` | 可用，空态重复 | 顶部统计、列表空态、右侧空态三次表达同一事实 |
| 6 | 备份 | `/server/backup` | 良好 | 真实备份与高风险恢复边界清楚 |
| 7 | 日志 | `/server/logs` | 良好 | 信息密度高但适合运维；操作记录可进一步支持详情/筛选 |
| 8 | 诊断 | `/server/diagnostics` | 可用，首屏偏空 | 未执行时每项都重复“待检查”，可压缩成检查清单 |
| 9 | 玩家管理 | `/players` | 良好，停服空态偏空 | 不造假且职责正确；应增加明确的恢复动作 |
| 10 | 联盟管理 | `/alliances` | 良好，停服空态偏空 | 不造假且职责正确；左列表和右详情在空态浪费较多空间 |
| 11 | 游戏管理·奖励中心 | `/game/rewards` | 良好 | 不重复玩家在线/资产；停服时禁用逻辑清楚 |

## 3. 当前页面截图

### 1）服务器管理 · 控制

健康度：良好。当前是完整停止态，服务、RCON、Query 和最近保存之间的关系可见。

![服务器控制 1440x900](screenshots/current/01-server-control-1440x900-full.png)

![服务器控制 1920x1080](screenshots/current/01-server-control-1920x1080.png)

### 2）服务器管理 · 性能

健康度：良好。当前值不可用时仍保留历史；需要更醒目标出历史数据对应的运行区间。

![性能 1440x900](screenshots/current/02-server-performance-1440x900-full.png)

![性能 1920x1080](screenshots/current/02-server-performance-1920x1080.png)

### 3）服务器管理 · 内存与星区

健康度：良好，停服降级符合真实数据原则。本次星区接口 503 是预期运行状态，不是页面崩溃。

![内存与星区 1440x900](screenshots/current/03-server-memory-1440x900-full.png)

![内存与星区 1920x1080](screenshots/current/03-server-memory-1920x1080.png)

### 4）服务器管理 · 更新

健康度：可用。当前是已配置环境的入口摘要，不是完整安装向导的全部状态。

![更新 1440x900](screenshots/current/04-server-update-1440x900-full.png)

![更新 1920x1080](screenshots/current/04-server-update-1920x1080.png)

### 5）服务器管理 · 自动任务

健康度：可用。当前数据库中没有任务，页面真实显示空态。

![自动任务 1440x900](screenshots/current/05-server-tasks-1440x900-full.png)

![自动任务 1920x1080](screenshots/current/05-server-tasks-1920x1080.png)

### 6）服务器管理 · 备份

健康度：良好。显示 5 个实际 `.bak`，恢复动作有风险警告和五步流程。

![备份 1440x900](screenshots/current/06-server-backup-1440x900-full.png)

![备份 1920x1080](screenshots/current/06-server-backup-1920x1080.png)

### 7）服务器管理 · 日志

健康度：良好。管理 Operation 和服务器日志并列，内容来自真实记录。

![日志 1440x900](screenshots/current/07-server-logs-1440x900-full.png)

![日志 1920x1080](screenshots/current/07-server-logs-1920x1080.png)

### 8）服务器管理 · 诊断

健康度：可用。当前尚未执行诊断，11 项只展示待检查状态。

![诊断 1440x900](screenshots/current/08-server-diagnostics-1440x900-full.png)

![诊断 1920x1080](screenshots/current/08-server-diagnostics-1920x1080.png)

### 9）玩家管理

健康度：良好，停服空态真实。当前请求 503，未生成玩家示例数据。

![玩家管理 1440x900](screenshots/current/09-players-1440x900-full.png)

![玩家管理 1920x1080](screenshots/current/09-players-1920x1080.png)

### 10）联盟管理

健康度：良好，停服空态真实。联盟发现依赖运行中的管理组件。

![联盟管理 1440x900](screenshots/current/10-alliances-1440x900-full.png)

![联盟管理 1920x1080](screenshots/current/10-alliances-1920x1080.png)

### 11）游戏管理 · 奖励中心

健康度：良好。只显示目标选择所需的最小身份，没有复制玩家在线、位置或资产。

![奖励中心 1440x900](screenshots/current/11-game-rewards-1440x900-full.png)

![奖励中心 1920x1080](screenshots/current/11-game-rewards-1920x1080.png)

## 4. 当前停服时看不到的已验证深层状态

以下截图不是本次停服快照，而是对应 QA 运行保存的功能状态。它们只用于说明真实 Inventory 能力和目录布局；各目录附原始报告，不能和本次当前运行状态混为一谈。

### 玩家真实 Inventory 详情

报告记录：2 门真实炮塔，能显示 MiningLaser、ChainGun、材质、科技、DPS、射程，未泄漏 Lua userdata；两档视口无横向溢出、console error、page error 或失败请求。

![真实 Inventory 1440x900](screenshots/feature-states/inventory-real-1440x900.png)

![真实 Inventory 1920x1080](screenshots/feature-states/inventory-real-1920x1080.png)

原始报告：[inventory-real-report.json](screenshots/feature-states/inventory-real-report.json)

### 共享物品目录

报告记录：58 个目录定义、58 个图标成功加载、19 种普通炮塔、11 个已验证可发放条目；能够搜索采矿条目，并显示剧情禁止策略。两档视口无横向溢出、console error、page error 或失败目录请求。

![Inventory 目录 1440x900](screenshots/feature-states/inventory-catalog-1440x900.png)

![Inventory 目录 1920x1080](screenshots/feature-states/inventory-catalog-1920x1080.png)

原始报告：[inventory-catalog-report.json](screenshots/feature-states/inventory-catalog-report.json)

## 5. 如何复现截图

前提是本机前端已经运行在测试地址，且管理员会话可用。脚本：

```powershell
cd E:\OrionAdmin\frontend-prototype
node .\scripts\capture-ai-review-pages.mjs
```

脚本会覆盖 `docs/ai-review/screenshots/current/` 中同名截图并刷新 `report.json`。它只读取页面，不点击启停、发放、恢复等写操作。
