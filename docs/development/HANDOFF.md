# 当前交接

最后更新：2026-09-15。
当前任务：M0-11（测试结构整理）。
工作分支：`cleanup/test-structure-2026-09-15`。
目标分支：`main`。
当前 Pull Request：待创建。

## 用户当前目标

保持 GitHub 公开，让其他人可以查看、Fork 和提交 Pull Request，但不能直接修改受保护的 `main`。项目主要由 AI 辅助开发，因此稳定代码只通过独立分支和 PR 进入 `main`。

用户要求继续清理长期 AI 开发留下的重复、多余和过大的源码结构，同时不得为了“整理”破坏现有功能、真实数据边界、API 行为、数据库行为、管理桥接协议、测试覆盖或 UI。

## 已完成的整理

- PR #1：仓库卫生清理与接续收敛，已合并。
- PR #2：前端更新模块去重复并加入 GitHub Actions CI，已合并；CI 通过。
- PR #3：全局 `styles.css` 按原顺序拆为 8 个区块，已合并；CI 通过。
- PR #4：Core `Services.cs` / `ApiModels.cs` 按职责拆分，已合并；CI 通过。
- PR #5：`ProvisioningEndpoints.cs` 按更新环境、服务器配置、安装、文件系统职责拆分，已合并；CI 通过。
- PR #6：`SqliteStore.cs` 按共享核心、自动任务/内存策略、更新/配置、性能、Operation 职责拆分，已合并；修复首轮 CI 暴露的接口声明遗漏后完整通过。
- PR #7：将原 `ManagementBridge.cs` 拆为 `ManagementBridgeProtocol.cs` 与 `ManagedServerControlService.ManagementBridge.cs`，协议解析/验证与实际服务桥接职责分离；CI 完整通过后已合并。

M0-10 至此收口，不继续为了文件更小而机械拆分 `ManagedServerControlService.ManagementBridge.cs`。当前两个桥接文件仍保持明确职责边界，继续拆分的收益不足以覆盖回归风险。

## M0-11 当前改动

当前先处理 `backend/tests/AvorionAdmin.Tests/Program.cs` 最低风险的一层：将末尾只作为测试依赖的 fixture / test-double 类型原样移到 `TestFixtures.cs`。

已移动类型包括：

- `FixtureArchiveSource`
- `AcceptValveSignature`
- `StaticManagedControl`
- `StaticSteamQueryProbe`
- `FixtureAvorionSteamCmdRunner`
- `TransientLockAvorionSteamCmdRunner`

这些 class 的正文没有重写，`Program.cs` 的主验证流程、断言顺序、FakeServer、真实文件/进程/SQLite 边界以及现有测试入口均保持不变。一次性拆分工作流已经从最终差异删除。

## 当前验证状态

PR #7 当前 head 的完整 CI 已通过并已 squash merge 到 `main`，合并提交为 `049074eb1ecf487570bdf0dbe886b59685fa6ff5`。

当前 M0-11 分支已经完成 fixture/test-double 第一阶段拆分。下一步创建 PR，并等待现有 GitHub CI 完整执行：架构边界、.NET Release 构建、后端验证套件、前端 TypeScript 类型检查、Vite 生产构建和 Sites wrapper 测试。

## 后续清理顺序

1. 创建当前测试结构 PR，CI 通过后合并。
2. 再评估 `Program.cs` 主测试流程是否适合按“基础读写/生命周期与初始化/安装器”继续拆分；只有能保持测试顺序、资源清理和真实数据边界时才继续。
3. 完成 M0-01、M0-02、M0-03、M0-04、M0-05 的实际运行与页面/游戏验收，形成稳定基线。
4. 基线稳定后进入 M1 普通炮塔预览和单件发放，不在清理阶段提前加入新业务功能。

任何历史测试通过都不能替代当前代码版本的 CI 或真实游戏验收。
