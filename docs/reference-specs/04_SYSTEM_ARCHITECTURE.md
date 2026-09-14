# 系统架构（目标方案）

## 总体

```text
管理员电脑 / 手机浏览器
          │ HTTPS
          ▼
Avorion Web 管理后台
          │
          ▼
Avorion Server Agent（服务器本机，建议 Windows Service）
   ├─ 进程管理
   ├─ CPU/RAM/磁盘/网络
   ├─ 文件系统
   ├─ SteamCMD
   ├─ server.ini
   ├─ 备份/恢复
   ├─ 日志
   ├─ 定时任务
   └─ RCON
          │
          ├──────────► Avorion Dedicated Server
          │
          └──────────► SteamCMD / Galaxy / Mods / Logs
```

## 为什么必须有 Server Agent
纯网页/RCON 无法可靠完成：
- 启动/终止 Windows 进程
- SteamCMD 更新
- 读取 CPU/RAM/磁盘/网络
- 文件上传/下载/编辑
- 大型 Galaxy 备份/恢复
- 读取本机日志
- 开机自启

## Agent 原则
- 服务器开机自动运行
- 无需桌面窗口
- 尽量低资源占用
- 即使 Avorion Server 停止，Web 管理页仍可用
- 高权限操作内部隔离
- 管理端对外只暴露经过认证的 HTTPS 接口

## 推荐实现（尚未最终冻结）

### 后端/Agent
推荐优先考虑：
- .NET 8 / ASP.NET Core
- Windows Service
- SQLite 保存管理系统自己的数据（用户、任务、审计、UI 配置、历史性能等）
- WebSocket / SignalR 做实时日志、事件、性能刷新

### 前端
- React + TypeScript
- Tailwind CSS
- shadcn/ui（源码可控）
- Lucide Icons
- Recharts / ECharts

> 如果 Codex 有更合适的方案，可以提出，但不能为了换技术而破坏 Windows 本机运维能力和 AI 可维护性。

## Avorion 游戏内边界

- 2026-09-07 用户确认专用管理 MOD 方案；通过独立 OrionAdminBridge 调用官方 Lua API，先验证只读能力。原禁止管理 MOD 决策仅针对该组件解除。
- Agent 继续使用进程、SteamCMD、文件、现有配置、日志、Steam Query 与 RCON；新增本机 RCON 白名单管理组件查询。
- `MOD 管理` 只读取服务器已经存在或已经配置的 MOD/Workshop ID；当前不执行安装、启停、更新或删除。
- Player / Alliance / Ship 数据、Callback 和游戏写操作按阶段验证；尚未验证的能力继续返回不可用。M1 仅握手和在线玩家基础字段，详细范围见 `17_MANAGEMENT_MOD_STAGE_M1.md`。
