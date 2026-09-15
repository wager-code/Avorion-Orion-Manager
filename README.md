# OrionAdmin — Avorion 中文服务器管理后台

React / TypeScript 前端、.NET 8 API 与 Agent、SQLite、专用服务端 Lua MOD。保持模块化单体。

仓库：[wager-code/Avorion-Orion-Manager](https://github.com/wager-code/Avorion-Orion-Manager)（公开仓库）。主分支：`main`。

## 开发接续入口

换电脑或更换 AI 时，只按下面这套当前入口恢复进度，不依赖聊天记录：

1. [当前交接](docs/development/HANDOFF.md)：当前任务、完成情况、阻塞、下一步。
2. [步骤清单](docs/development/PLAN.md)：稳定任务编号、状态和验收条件。
3. [基线实际验收](docs/development/BASELINE_ACCEPTANCE.md)：自动启动、本机页面、隔离游戏服务器三层验收。
4. [协作与同步流程](docs/development/WORKFLOW.md)：分支、验证、提交和 Pull Request 流程。
5. [决策记录](docs/development/DECISIONS.md)：当前有效的用户级决定。
6. [用户需求](01_USER_REQUIREMENTS.md)、[能力矩阵](06_CAPABILITY_MATRIX.md)、[架构](07_MODULAR_ARCHITECTURE.md)。
7. 根目录及相关子目录的 `AGENTS.md`，然后阅读本次任务相关源码。

用户当前明确要求优先。`docs/reference-specs/`、`docs/evidence/`、`docs/ai-review/` 属于历史规格、证据或评审资料，可以用于追溯，但不能覆盖当前任务、当前能力矩阵或当前机器事实。

## 运行与验证

见 [环境准备](03_NEW_COMPUTER_SETUP.md)。准备入口为 `01-prepare.cmd`，启动入口为 `02-start.cmd`；游戏运行环境需单独核实。

## 当前功能范围

源码已有服务器管理八个子页面、玩家/联盟管理、奖励中心、Inventory 读取和受限系统插件发放。普通炮塔发放、舰船、活动和星区建设的后续阶段见 `PLAN.md`。源码存在不等于当前机器已验收。

## 版本管理

`main` 是受保护的稳定分支。功能、修复和仓库整理都从独立分支开始，通过 Pull Request 合并；不直接在 `main` 开发，不强推覆盖历史。

代码、计划、交接和验证摘要随 Git 提交同步。游戏存档、数据库、密码、本机运行数据和完整日志不作为源码提交。

历史 ZIP 交接包的清单、恢复提示和一次性补丁已由 Git 历史取代；需要追溯旧记录时使用 Git 提交历史，不在当前根目录维护第二套接续体系。

## 当前接续状态

2026-09-15 已完成仓库卫生、前端去重复、样式分区、Core/Endpoint/SQLite/ManagementBridge 职责拆分和第一阶段测试结构整理；后续不再为了文件更小机械拆分。

当前进入 M0 基线实际验收：先由 CI 做 API + 前端真实启动冒烟，再在本机检查页面状态，最后在已确认的隔离 Avorion 测试服验证 RCON、OrionAdminBridge 和游戏数据链路。通过后再进入 M1 普通炮塔预览和单件发放。
