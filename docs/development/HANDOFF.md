# 当前交接

最后更新：2026-09-15。
当前任务：M0-10（后端大文件职责拆分）。
工作分支：`cleanup/sqlite-store-2026-09-15`。
目标分支：`main`。
当前 Pull Request：#6（待创建/验证）。

## 用户当前目标

保持 GitHub 公开，让其他人可以查看、Fork 和提交 Pull Request，但不能直接修改受保护的 `main`。项目主要由 AI 辅助开发，因此稳定代码只通过独立分支和 PR 进入 `main`。

用户要求继续清理长期 AI 开发留下的重复、多余和过大的源码结构，同时不得为了“整理”破坏现有功能、真实数据边界、API 行为、数据库行为或 UI。

## 已完成的整理

- PR #1：仓库卫生清理与接续收敛，已合并。
- PR #2：前端更新模块去重复并加入 GitHub Actions CI，已合并；CI 通过。
- PR #3：将约 185 KB 的全局 `styles.css` 按原始顺序拆为 8 个区块，已合并；拆分前后重组 SHA-256 一致，CI 通过。
- PR #4：Core `Services.cs` / `ApiModels.cs` 按职责拆分，已合并；CI 通过。
- PR #5：`ProvisioningEndpoints.cs` 按更新环境、服务器配置、安装、文件系统职责拆分，已合并；CI 通过。

## M0-10 当前改动

本轮只处理 `backend/src/AvorionAdmin.Agent/SqliteStore.cs` 的文件职责拆分，不改变 SQLite 数据库文件、schema、SQL、接口签名或持久化行为。

原单一大文件现拆为：

- `SqliteStore.cs`：保留连接配置、统一 schema 初始化、共享数据库 helper。
- `SqliteStore.AutomationMemory.cs`：自动任务与内存策略读写。
- `SqliteStore.UpdateSetup.cs`：更新环境配置与服务器配置草稿。
- `SqliteStore.Performance.cs`：性能样本写入、查询与清理。
- `SqliteStore.Operations.cs`：Operation 创建、幂等、状态、结果与失败记录。

所有领域方法块按原源码连续区间直接移动，没有重写 SQL 或方法正文。拆分仍使用同一个 `avorion-admin.db`、同一 `_connectionString`、同一初始化逻辑和现有表/index。

拆分过程使用一次性工作流从原文件按明确方法锚点切分，并校验拆分前后的领域方法块逐字保持一致；一次性工作流已从最终差异删除。

## 当前验证状态

本轮分支已经完成文件拆分，下一步创建 PR #6 并等待现有 GitHub CI 对当前 head 完整执行：架构边界、.NET Release 构建、后端验证套件、前端 TypeScript 类型检查、Vite 生产构建和 Sites wrapper 测试。

本轮没有修改数据库 schema、迁移规则、API Contract、Lua MOD、Avorion 游戏写操作、前端页面逻辑或视觉样式。

## 后续清理顺序

1. 创建 PR #6，CI 通过后合并 `SqliteStore.cs` 拆分。
2. 单独拆 `ManagementBridge.cs`，保持协议、MOD 版本兼容和现有验证逻辑不变。
3. 整理测试文件结构，但保留 FakeServer 和真实数据边界测试。
4. 完成基线实际运行验收后，再继续普通炮塔、舰船、活动和星区建设等新功能。

任何历史测试通过都不能替代当前代码版本的 CI 或真实游戏验收。
