# 当前交接

最后更新：2026-09-15。
当前任务：M0-07（仓库卫生清理与接续收敛）。
工作分支：`cleanup/repository-hygiene-2026-09-15`。
目标分支：`main`。

## 用户当前目标

保持 GitHub 公开，让其他人可以查看、Fork 和提交 Pull Request，但不直接修改 `main`。项目主要由 AI/Codex 开发，因此 `main` 作为稳定分支；每次功能、修复和整理使用独立分支，通过 PR 合并。

用户同时要求对仓库做完整评估，删除重复、多余、过期文件，避免 AI 长期开发后出现多套交接资料和重复证据。

## M0-07 本次已做

- 建立独立清理分支，没有直接修改 `main`。
- 重写根目录 `AGENTS.md`，只保留长期开发规则和一套当前阅读入口。
- 更新 `README.md`、`WORKFLOW.md`、`DECISIONS.md`、`PLAN.md`，统一分支/PR 开发方式和当前文档主入口。
- 删除旧 ZIP 交接入口、旧状态交接、旧 AI 接续提示、旧迁移验证记录、旧 MOD 阶段说明、旧 `MANIFEST.sha256`。
- 删除 `MissingModPatch/` 一次性迁移补丁目录。
- 删除 `docs/ai-review/06_PROMPT_FOR_OTHER_AI.md`，避免旧提示词继续影响当前 AI。
- 删除一张已确认与 `docs/ai-review/screenshots/current/04-server-update-1920x1080.png` 字节完全相同的重复 QA 截图。

被删除文件仍存在于 Git 历史，需要追溯时可恢复；本轮没有删除业务源码、测试源码、能力矩阵、架构、API 合同、官方 Avorion 文档 ZIP、UI 母版或 OrionAdminBridge 正式源码。

## 本轮没有做

- 没有修改 React、C#、Lua 业务逻辑。
- 没有运行 Avorion 游戏服或执行任何游戏写操作。
- 没有把旧历史评审目录整包删除；它仍保留为历史快照，后续再按价值清理。
- 没有开始拆 `ManagementBridge.cs`、`SqliteStore.cs`、`ProvisioningEndpoints.cs`、全局 `styles.css` 等大文件；这些应放在后续独立 PR 中。

## 验证原则

本次属于文档/仓库结构清理，不应把历史旧机器的构建结果冒充当前验证。合并前需要：

1. 比较 `main...cleanup/repository-hygiene-2026-09-15` 的文件差异。
2. 检查当前 README/AGENTS/PLAN/HANDOFF/WORKFLOW/DECISIONS 不再引用已删除的当前入口文件。
3. 确认删除范围没有业务源码和正式测试源码。
4. 创建 Pull Request，由用户确认后再合并。

## 当前代码事实

- React/TypeScript、.NET 8 API/Agent/Core、SQLite、Lua OrionAdminBridge；保持模块化单体。
- 玩家和联盟页共用 `features/inventory/InventoryWorkbench.tsx`。
- `features/server-update` 已经存在，旧评审中“更新页尚未拆分”的描述属于历史快照。
- 普通炮塔生成/发放、舰船、活动和星区建设仍按 PLAN 分阶段推进，规划不等于已实现。

## 清理之后的下一步

M0-07 合并后，优先完成 M0-01 / SYNC-03：在当前环境重新执行后端构建、自动测试、前端类型检查和生产构建；随后完成 SYNC-04 GitHub CI。之后再进入前端去重复、后端大文件拆分和新的游戏功能。
