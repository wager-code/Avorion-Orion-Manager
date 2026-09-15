# 当前交接

最后更新：2026-09-15。
当前任务：M0-01～M0-05（基线实际运行与页面/游戏验收）。
工作分支：`verification/baseline-smoke-2026-09-15`。
目标分支：`main`。
当前 Pull Request：待创建。

## 用户当前目标

保持 GitHub 公开，让其他人可以查看、Fork 和提交 Pull Request，但不能直接修改受保护的 `main`。项目主要由 AI 辅助开发，因此稳定代码只通过独立分支和 PR 进入 `main`。

仓库清理阶段已经收口。现在不继续为了文件更小而机械拆分源码，优先证明当前版本可以真实启动、页面状态准确，并在已确认隔离服上完成 Avorion / RCON / OrionAdminBridge 验收。

## 已完成的整理

- PR #1：仓库卫生清理与接续收敛，已合并。
- PR #2：前端更新模块去重复并加入 GitHub Actions CI，已合并；CI 通过。
- PR #3：全局 `styles.css` 按原顺序拆为 8 个区块，已合并；CI 通过。
- PR #4：Core `Services.cs` / `ApiModels.cs` 按职责拆分，已合并；CI 通过。
- PR #5：`ProvisioningEndpoints.cs` 按职责拆分，已合并；CI 通过。
- PR #6：`SqliteStore.cs` 按持久化职责拆分，已合并；修复首轮 CI 暴露的接口声明遗漏后完整通过。
- PR #7：`ManagementBridge.cs` 拆为协议验证与服务桥接职责，已合并；CI 通过。
- PR #8：将测试 fixture / test-double 从 `Program.cs` 原样移动到 `TestFixtures.cs`，已合并；CI 通过。
- PR #9：移除 Server Update 已无必要的兼容转发层和重复 helper，已合并；作为当前静态清理阶段收尾。

M0-10 与 M0-11 至此收口；保留现有大测试主流程，不为了形式继续拆分。

## 当前基线验收改动

本分支开始 M0-01～M0-05 的实际验收准备。

1. 在现有 `.github/workflows/ci.yml` 中新增真实启动冒烟步骤：

```powershell
./tools/run-local.ps1 -ApiPort 5188 -WebPort 4273 -SmokeTest
```

该步骤会在 Windows runner 上启动已构建的 .NET API 和 Vite 前端，通过前端代理检查 `/api/v1/health`，并确认 `/server/control` 返回 HTTP 200，然后只停止自己启动的进程。

2. 新增 `docs/development/BASELINE_ACCEPTANCE.md`，把自动启动、本机页面、隔离游戏服务器三层验收分开记录。

3. `PLAN.md` 已切换到基线验收阶段：M0-11 标记完成，M0-01 进入进行中，M0-02/M0-03 保持已实现待验证，M0-04/M0-05 等待现场验收。

## 当前验证状态

当前分支改动尚未经过 PR CI。创建 PR 后必须等待完整 CI，重点确认新增的 `Smoke-test API and frontend startup` 真实通过；原有架构检查、.NET 构建、后端验证、前端类型检查、生产构建和 Sites 测试也必须继续全绿。

自动启动冒烟通过后，只能证明 API/前端和代理链路能启动，不能替代本机 UI 和真实 Avorion 游戏验收。

## 下一步

1. 创建当前基线验收 PR并等待完整 CI。
2. CI 通过后，在用户 Windows 电脑运行 `01-prepare.cmd` 和 `02-start.cmd`，实际检查导航、更新页、玩家、联盟、Inventory、备份、日志和未配置状态；需要时由用户提供截图/报错，我再按实际结果修复。
3. 本机 UI 稳定后，选择已确认的隔离 Avorion 测试服，记录游戏版本、MOD 版本与连接状态，验证 RCON、Bridge hello、玩家/联盟/Inventory 读取及现有已授权写操作。
4. M0 基线通过后再进入 M1 普通炮塔预览和单件发放。

任何 CI 通过都不能替代真实游戏验收；任何真实游戏写操作只在已确认隔离服和现有授权边界内进行。
