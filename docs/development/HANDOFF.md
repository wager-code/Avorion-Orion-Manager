# 当前交接

最后更新：2026-09-15。
当前任务：M0-08（前端更新模块去重复）与 SYNC-04（PR 自动检查）。
工作分支：`cleanup/frontend-dedup-2026-09-15`。
目标分支：`main`。
当前 Pull Request：#2。

## 用户当前目标

保持 GitHub 公开，让其他人可以查看、Fork 和提交 Pull Request，但不能直接修改受保护的 `main`。项目主要由 AI 辅助开发，因此稳定代码只通过独立分支和 PR 进入 `main`。

用户要求继续清理长期 AI 开发留下的重复、多余和过期内容，同时不得为了“整理”破坏现有功能、真实数据边界或 UI。

## 已完成的仓库清理

M0-07 已通过 PR #1 合并进 `main`：旧交接包、一次性迁移补丁、过期 AI 接续提示、旧 MANIFEST 和一张字节级重复截图已移除；README、AGENTS、WORKFLOW、PLAN、HANDOFF、DECISIONS 已收敛成当前接续体系。

## M0-08 当前改动

- `ServerUpdatePage.tsx` 不再维护第二套 Server Update 类型定义；统一复用 `features/server-update/types.ts`。
- 保留原有类型重导出，避免已有引用因为整理而失效。
- 删除更新页拆分后遗留的无用 icon、组件和 helper import。
- 页面不再复制 `readApiError`，改用统一 helper。
- Operation sessionStorage 逻辑由 `operationStorage.ts` 负责；`utils.ts` 暂时保留兼容重导出，后续确认所有调用点后再删除兼容层。
- `serverUpdateApi.ts` 缩减为直接重导出；暂时保留该兼容入口，避免本轮同时改动多个大组件文件。
- 新增 `.github/workflows/ci.yml`，PR 自动执行架构边界检查、后端构建、后端验证套件、前端依赖安装、TypeScript 类型检查、生产构建和 Sites wrapper 测试。

## 当前验证状态

PR #2 已创建。GitHub Actions CI 已启动，必须以本次运行结果为准；在 CI 完成前不把 M0-08 标记为已验证，也不合并。

本轮没有修改 C# 业务逻辑、Lua MOD、数据库结构、API Contract、Avorion 游戏写操作或现有页面视觉设计。

## 后续清理顺序

1. CI 通过并合并 PR #2。
2. 单独处理前端全局 `styles.css` 的分区，不改变视觉结果。
3. 分独立 PR 拆分 `ManagementBridge.cs`、`SqliteStore.cs`、`ProvisioningEndpoints.cs`，以及 Core 中职责过大的类型/接口文件。
4. 整理测试文件结构，但保留 FakeServer 和真实数据边界测试。
5. 基线稳定后再继续普通炮塔、舰船、活动和星区建设等新功能。

任何历史测试通过都不能替代当前代码版本的 CI 或真实游戏验收。
