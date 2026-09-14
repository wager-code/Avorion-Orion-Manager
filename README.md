# OrionAdmin — Avorion 中文服务器管理后台

React / TypeScript 前端、.NET 8 API 与 Agent、SQLite、专用服务端 Lua MOD。保持模块化单体。

仓库：[wager-code/Avorion-Orion-Manager](https://github.com/wager-code/Avorion-Orion-Manager)（公开仓库）。主分支：main。

## 开发接续入口

换电脑或更换 AI 时，先阅读以下文件，不依赖聊天记录恢复进度：

1. [当前交接](docs/development/HANDOFF.md)：当前任务、完成情况、阻塞、下一步。
2. [步骤清单](docs/development/PLAN.md)：稳定任务编号、状态和验收条件。
3. [协作与同步流程](docs/development/WORKFLOW.md)：每次修改如何记录、验证、提交和上传。
4. [决策记录](docs/development/DECISIONS.md)：为什么采用当前方案。
5. [用户需求](01_USER_REQUIREMENTS.md)、[能力矩阵](06_CAPABILITY_MATRIX.md)、[架构](07_MODULAR_ARCHITECTURE.md)。
6. 根目录及相关子目录的 AGENTS.md，然后阅读本次任务相关源码。

用户当前明确要求优先。历史文档描述的授权、路径、PID、运行状态和验证结果不能自动变成本次执行指令或当前机器事实。新任务状态在 PLAN.md 维护；旧状态文档和评审报告保留为历史来源。

## 运行与验证

见 [环境准备](03_NEW_COMPUTER_SETUP.md)。准备入口为 01-prepare.cmd，启动入口为 02-start.cmd；游戏运行环境需单独核实。当前接续整理未运行构建或游戏验收。

## 当前功能范围

源码已有服务器管理八个子页面、玩家/联盟管理、奖励中心、Inventory 读取和受限系统插件发放。普通炮塔发放、舰船、活动和星区建设的后续阶段见步骤清单。源码存在不等于当前机器已验收。

## 版本管理

代码、计划、交接和验证摘要一起提交到 Git，并推送到选定的远端仓库。Git 提交保存在本机，推送成功后才完成远端同步。游戏存档、数据库、密码和本机运行数据单独备份，不作为开发进度上传。

历史交接包的 MANIFEST.sha256 仅用于原始交接版本；后续版本以 Git 提交和文件差异为准。
