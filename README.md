# 🎮 OrionAdmin
## Avorion 中文服务器管理后台

> 一个面向 Avorion 专用服务器的中文可视化管理后台。
> 提供服务器状态、更新、备份、日志、玩家/联盟、Inventory、RCON 与专用管理 MOD 等能力。

- 🖥️ 平台：Windows x64
- ⚛️ 前端：React + TypeScript
- 🟣 后端：.NET 8 API / Agent
- 🗃️ 数据：SQLite
- 🧩 游戏侧：OrionAdminBridge Lua MOD
- 🌿 稳定分支：`main`

仓库：[wager-code/Avorion-Orion-Manager](https://github.com/wager-code/Avorion-Orion-Manager)（公开仓库）。

---

## ✨ 项目简介

OrionAdmin 是一个为 Avorion 专用服务器设计的中文管理后台，目标是把常见的服务端维护、状态查看、玩家与联盟管理、日志、备份、更新以及游戏侧管理能力集中到一个界面中。

项目采用模块化单体结构：React / TypeScript 负责前端，.NET 8 API 与 Agent 负责本机管理与受控操作，SQLite 保存管理侧状态，OrionAdminBridge Lua MOD 提供游戏侧受控能力。

---

## 🚀 当前已经实现

### 🖥️ 服务器管理

- ✅ 服务器控制
- ✅ 性能监控
- ✅ 内存与星区状态
- ✅ 服务端更新
- ✅ 自动任务
- ✅ 存档备份
- ✅ 实时日志
- ✅ 服务器诊断

### 👥 游戏管理

- ✅ 玩家管理
- ✅ 联盟管理
- ✅ 奖励中心
- ✅ Inventory 读取
- ✅ 受限系统插件发放
- ✅ RCON 管理链路
- ✅ OrionAdminBridge 游戏侧管理桥接

### 📦 Windows 发布

- ✅ 开发模式启动脚本
- ✅ Windows x64 自包含生产包
- ✅ 前后端同源运行
- ✅ GitHub Actions 自动构建与启动冒烟

> 源码中存在某项能力，不等于所有机器环境都已经完成现场验收；当前验收状态以开发文档与 CI 结果为准。

---

## 📦 Windows 运行

### 开发模式

先查看 [环境准备](03_NEW_COMPUTER_SETUP.md)，然后依次运行：

```text
01-prepare.cmd
02-start.cmd
```

`01-prepare.cmd` 负责准备依赖与构建，`02-start.cmd` 用于开发环境启动。

### 独立运行版

项目已经支持生成 Windows x64 自包含生产包：

```text
tools/publish-windows.ps1
```

生成后的生产包不依赖目标机器预先安装 Node.js 或 .NET，适合后续继续做可重复发布与服务化。

浏览器与视觉 QA 的可移植运行方式见 [QA 指南](docs/development/QA.md)。

---

## 🧪 当前验证状态

- ✅ GitHub Actions CI
- ✅ 架构边界检查
- ✅ OpenAPI 漂移检查
- ✅ .NET Release 构建
- ✅ 后端验证套件
- ✅ TypeScript 类型检查
- ✅ 前端生产构建
- ✅ 前端 / API 启动冒烟
- ✅ 浏览器页面 QA
- ✅ Avorion 隔离 Galaxy 实机验收
- ✅ RCON 真实认证
- ✅ OrionAdminBridge 实际连接
- ✅ 玩家 / 联盟 / Inventory 实际读取
- ✅ 保存与安全停服链路验收
- ✅ Windows 自包含发布包启动验证

---

## 🗺️ 开发路线

### ✅ 已完成

- P0 基线整理与仓库收口
- UI / API / 数据真实性验收
- 隔离 Galaxy 实机验收
- Windows x64 自包含发布包

### 🚧 当前推进

- Windows 服务化与升级 / 数据保留机制
- OrionAdminBridge 安装与维护
- 完整更新与安全点恢复

### 📌 后续阶段

- 🔫 普通炮塔管理
- 🚀 舰船管理
- ⚔️ 活动管理
- 🌌 星区与空间站管理

详细任务编号、状态和验收条件见 [PLAN.md](docs/development/PLAN.md)。

---

## 🧭 开发接续

换电脑、更换 AI 或重新接手项目时，按下面顺序恢复上下文，不依赖聊天记录：

1. [当前交接](docs/development/HANDOFF.md)：当前任务、完成情况、阻塞与下一步。
2. [步骤清单](docs/development/PLAN.md)：稳定任务编号、状态和验收条件。
3. [协作与同步流程](docs/development/WORKFLOW.md)：分支、验证、提交和 Pull Request 流程。
4. [决策记录](docs/development/DECISIONS.md)：当前有效的用户级决定。
5. [用户需求](01_USER_REQUIREMENTS.md)、[能力矩阵](06_CAPABILITY_MATRIX.md)、[架构](07_MODULAR_ARCHITECTURE.md)。
6. 根目录及相关子目录的 `AGENTS.md`，然后阅读本次任务相关源码。

用户当前明确要求优先。`docs/reference-specs/`、`docs/evidence/`、`docs/ai-review/` 属于历史规格、证据或评审资料，可以用于追溯，但不能覆盖当前任务、当前能力矩阵或当前机器事实。

---

## 🔐 版本管理

`main` 是受保护的稳定分支。

功能、修复和仓库整理统一采用：

```text
main
  ↓
独立分支
  ↓
CI / QA
  ↓
Pull Request
  ↓
main
```

不直接在 `main` 开发，不强推覆盖历史。

代码、计划、交接和验证摘要随 Git 提交同步；游戏存档、数据库、密码、本机运行数据和完整日志不作为源码提交。

历史 ZIP 交接包的清单、恢复提示和一次性补丁已由 Git 历史取代；需要追溯旧记录时使用 Git 提交历史，不在当前根目录维护第二套接续体系。

---

## 📍 当前接续状态

P0 页面与隔离 Galaxy 真实验收已经完成，Windows x64 自包含生产包也已进入 `main`。

当前重点已经从基础页面与结构整理，转向可重复 Windows 发布 / 服务化、升级与数据保留、OrionAdminBridge 安装维护，以及完整更新与安全点恢复。完成这些基础发布能力后，再继续推进普通炮塔、舰船、活动和星区建设等游戏管理功能。
