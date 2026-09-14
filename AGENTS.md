# OrionAdmin 仓库开发规则

本文件只保存长期有效的开发规则，不保存某一台电脑的 PID、路径、临时运行状态或一次性验收结果。

## 开始工作前的阅读顺序

1. `README.md`：项目入口与当前接续方式。
2. `docs/development/HANDOFF.md`：当前工作现场、阻塞与下一步。
3. `docs/development/PLAN.md`：任务编号、依赖、状态与验收条件。
4. `docs/development/WORKFLOW.md`：Git、验证、提交和同步流程。
5. `docs/development/DECISIONS.md`：当前有效的用户决策。
6. 按任务需要阅读 `01_USER_REQUIREMENTS.md`、`06_CAPABILITY_MATRIX.md`、`07_MODULAR_ARCHITECTURE.md`、`08_INVENTORY_ITEM_CATALOG_AND_ICONS.md` 及相关源码。
7. 若子目录存在 `AGENTS.md`，进入该目录工作前再阅读对应规则。

`docs/reference-specs/`、`docs/evidence/`、`docs/ai-review/` 属于历史规格、证据或评审资料。它们可以帮助理解背景，但不能覆盖当前用户要求，也不能自动代表当前机器状态或当前功能状态。功能是否支持以 `06_CAPABILITY_MATRIX.md` 和真实源码/验证为准；任务状态以 `docs/development/PLAN.md` 为准。

## 架构规则

保持模块化单体，不拆微服务：React/TypeScript 前端 + .NET 8 API/Agent/Core + SQLite + 专用服务端 OrionAdminBridge Lua MOD。

- `Program.cs` 只做 composition root，不放业务路由正文。
- HTTP Contract 放到对应业务 Endpoint 模块。
- 游戏与运行时逻辑放 Agent Service，并通过 Core abstraction 暴露。
- 持久化职责放 Store；长时间或危险写操作使用 Operation queue/worker。
- 前端 `App.tsx` 不承载页面业务；路由、页面、领域组件按模块组织。
- OrionAdminBridge 的命令入口保持薄路由；新增游戏能力放独立 lib 模块。
- 新功能优先复用已有领域模块；不要为了“模块化”创建只有一行转发或一个简单 `div` 的无意义包装文件。
- 修改架构后运行 `tools/check-architecture.ps1`。

## 产品与 UI 规则

继续现有中文 UI 和已经确认的 A 风格，不擅自整体重设计。

新增页面或明显改变页面结构时：先规划信息架构和 UI，确认后再实现；实现后必须实际运行并截图检查。页面职责必须清楚，同一份完整信息只归属一个主要模块，其他页面只显示完成当前任务所需的最小关联信息。

## 真实数据与写操作规则

- 没有真实数据时显示未知、不可用或待验证，不允许用 Mock、默认正常值或假成功掩盖缺失能力。
- 测试夹具/FakeServer 只用于隔离自动测试，不得成为生产数据回退。
- 专用 OrionAdminBridge 已获授权；这不自动授权第三方 MOD 写操作。
- 未经能力矩阵或当前任务明确允许的 Avorion 写操作保持关闭。
- 已有的管理员会话、CSRF、幂等、确认文本、Operation 持久化、结果复核和失败闭锁边界不得为了方便而绕过。

## Git 与交付规则

- 不直接向受保护的 `main` 开发；每个功能、修复或整理使用独立分支并提交 Pull Request。
- 不强推 `main`，不删除 `main`，不覆盖未知修改。
- 开工先检查分支、远端和未提交修改；结束前检查 diff。
- 代码变化必须运行与范围相符的验证；未运行就明确写“未验证”。
- 功能状态变化同步 `06_CAPABILITY_MATRIX.md`；任务状态同步 `PLAN.md`；当前现场同步 `HANDOFF.md`；新的用户级决定同步 `DECISIONS.md`。
- 不提交密码、实际 Galaxy、运行数据库、完整日志、本机进程资料、SteamCMD/游戏程序、`node_modules` 或构建输出。

## 当前开发方向

不要从历史评审重复开发已经存在的模块。当前路线和优先级只看 `docs/development/PLAN.md`；历史截图、旧阶段提示词、旧交接包说明只能作为背景证据。
