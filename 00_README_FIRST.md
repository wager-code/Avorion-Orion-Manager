# 换电脑交接：先读这里

这是猎户座（Avorion）中文 Web 服务器管理中心的开发交接包，最新整理于 2026-09-09。
包含最新源码、你的要求、已确认设计、接口依据及阶段实测证据；不是完整成品安装包。

## 在新电脑使用

1. 完整解压到可写目录（例如 D:\Projects\OrionAdmin），不要直接在 ZIP 内运行。
2. 安装 Node.js 24.x、.NET 8 SDK（包含 ASP.NET Core 8 运行时）。旧电脑已验证环境为 Node 24.19.0、.NET SDK 9.0.317 + Runtime 8.0.30；新电脑使用 .NET 8 SDK 即可按 net8.0 目标构建。初始化脚本会检查基本条件。
3. 双击 `01-prepare.cmd`：安装锁定版本的前端依赖，构建后端和前端，执行类型检查。首次需要联网；不下载游戏服务端，不启动游戏。
4. 双击 `02-start.cmd`：启动管理后台，浏览器地址 http://127.0.0.1:4173/server/control。保持启动窗口运行。
5. 新电脑开始时没有已配置服务器是正常的。本包不含原机路径档案、数据库、RCON 密码或 Galaxy，需要重新安装测试服或导入新电脑上实际存在的服务端。

新电脑的 Codex：打开解压目录作为项目，再复制 `04_CODEX_RESUME_PROMPT.md` 的正文发送即可。
Codex 原聊天、插件登录和本机权限不会随源码转移；后续助手以本包要求和源码为依据继续。

## 信息阅读优先级

1. 用户在新对话中的最新明确要求。
2. `01_USER_REQUIREMENTS.md`：已确认要求与边界。
3. `02_CURRENT_STATUS.md`：已完成、尚未完成及最新阻塞。
4. `06_CAPABILITY_MATRIX.md`：当前已支持、确认可开发、待验证及明确不做的功能总账。
5. `08_INVENTORY_ITEM_CATALOG_AND_ICONS.md`：Inventory 五类物品、炮塔/系统类型、图标来源和后续物品目录方案。
6. `07_MODULAR_ARCHITECTURE.md`：模块化单体边界、扩展新功能的固定接入方法。
7. `03_NEW_COMPUTER_SETUP.md`：运行、测试、隔离验证方法。
8. `docs/reference-specs/`：保留详细产品规划、设计规则、官方 API 核对、历史决策和流程。旧内容与上述文件冲突时，以本包最新总结为准。

`17_MANAGEMENT_MOD_STAGE_M1.md` 与 `docs/evidence/` 是旧机器的真实实测记录。其中的 D 盘路径、PID、Operation ID、时间和“正在运行”描述只代表记录当时，不代表新电脑状态。

## 保留与排除

保留 React/TypeScript 前端、.NET API/Agent/Core、测试源代码、MOD Lua 源码、依赖锁文件、API 合同、两张 A 风格设计母版、八张代表性已实现页面截图，以及用户提供的 Avorion 2.5.13 官方脚本文档 ZIP。
不包含 node_modules、bin/obj/dist、SteamCMD、Avorion 游戏程序、Galaxy/自动备份、管理数据库、实际 server.ini、密码、日志全集、临时浏览器数据、过时方案图片和原机专用一次性脚本。
测试项目里的 FakeServer 是隔离自动测试所需源代码；它不参与正式 API 提供游戏数据。请保留。

本包只迁移开发项目。若你还想迁移正在使用的游戏世界，需另行备份实际 Galaxy；不要误以为这个 ZIP 包含游戏存档。
源码校验清单位于 `MANIFEST.sha256`，构建验证结果见 `05_TRANSFER_VERIFICATION.md`。
