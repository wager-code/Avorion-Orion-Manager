# 当前交接

最后更新：2026-09-15。
当前任务：M0-10（后端大文件职责拆分）。
工作分支：`cleanup/core-contracts-2026-09-15`。
目标分支：`main`。
当前 Pull Request：待创建。

## 用户当前目标

保持 GitHub 公开，让其他人可以查看、Fork 和提交 Pull Request，但不能直接修改受保护的 `main`。项目主要由 AI 辅助开发，因此稳定代码只通过独立分支和 PR 进入 `main`。

用户要求继续清理长期 AI 开发留下的重复、多余和过大的源码结构，同时不得为了“整理”破坏现有功能、真实数据边界、API 行为或 UI。

## 已完成的整理

- PR #1：仓库卫生清理与接续收敛，已合并。
- PR #2：前端更新模块去重复并加入 GitHub Actions CI，已合并；CI 通过。
- PR #3：将约 185 KB 的全局 `styles.css` 按原始顺序拆为 8 个区块，已合并；拆分前后重组 SHA-256 一致，CI 通过。

## M0-10 当前改动

本分支只处理 Core 中两个“万能文件”的职责归类，不改变 public type 名称、命名空间、方法签名、记录字段或业务逻辑。

### Abstractions

原 `backend/src/AvorionAdmin.Core/Abstractions/Services.cs` 已按责任拆为：

- `ServerServices.cs`
- `RewardInventoryServices.cs`
- `ObservabilityServices.cs`
- `UpdateSetupServices.cs`
- `OperationServices.cs`

### Models

原 `backend/src/AvorionAdmin.Core/Models/ApiModels.cs` 已按责任拆为：

- `CommonModels.cs`
- `ServerModels.cs`
- `PerformanceModels.cs`
- `UpdateModels.cs`
- `MaintenanceModels.cs`
- `AutomationModels.cs`
- `OperationModels.cs`
- `SecurityModels.cs`

拆分过程使用一次性工作流读取原文件的所有顶层 public declaration，并校验每个声明恰好进入一个新文件后再删除原万能文件；一次性工作流自身不保留在最终差异中。

## 当前验证状态

本轮业务逻辑未修改。Pull Request 创建后必须等待现有 CI 完整通过：架构检查、.NET 构建、后端验证、前端 TypeScript 类型检查、Vite 生产构建和 Sites wrapper 测试。

本轮没有修改 Lua MOD、数据库结构、API Contract、Avorion 游戏写操作、前端页面逻辑或视觉样式。

## 后续清理顺序

1. 当前 Core contracts 拆分通过 CI 并合并。
2. 单独拆 `ProvisioningEndpoints.cs`，只按子职责移动路由和 helper，不改变 URL/HTTP Contract。
3. 单独拆 `SqliteStore.cs`，共享同一个 SQLite 数据库和现有 schema，不改持久化行为。
4. 单独拆 `ManagementBridge.cs`，保持协议、MOD 版本兼容和现有验证逻辑不变。
5. 整理测试文件结构，但保留 FakeServer 和真实数据边界测试。
6. 完成基线实际运行验收后，再继续普通炮塔、舰船、活动和星区建设等新功能。

任何历史测试通过都不能替代当前代码版本的 CI 或真实游戏验收。
