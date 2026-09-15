# 当前交接

最后更新：2026-09-15。
当前任务：M0-10（后端大文件职责拆分）。
工作分支：`cleanup/provisioning-endpoints-2026-09-15`。
目标分支：`main`。
当前 Pull Request：#5。

## 用户当前目标

保持 GitHub 公开，让其他人可以查看、Fork 和提交 Pull Request，但不能直接修改受保护的 `main`。项目主要由 AI 辅助开发，因此稳定代码只通过独立分支和 PR 进入 `main`。

用户要求继续清理长期 AI 开发留下的重复、多余和过大的源码结构，同时不得为了“整理”破坏现有功能、真实数据边界、API 行为或 UI。

## 已完成的整理

- PR #1：仓库卫生清理与接续收敛，已合并。
- PR #2：前端更新模块去重复并加入 GitHub Actions CI，已合并；CI 通过。
- PR #3：将约 185 KB 的全局 `styles.css` 按原始顺序拆为 8 个区块，已合并；拆分前后重组 SHA-256 一致，CI 通过。
- PR #4：Core `Services.cs` / `ApiModels.cs` 按职责拆分，已合并；CI 通过。

## M0-10 当前改动

PR #5 只处理 `backend/src/AvorionAdmin.Api/Endpoints/ProvisioningEndpoints.cs` 的职责拆分，不改变路由 URL、HTTP Contract 或业务逻辑。

原单一大文件现拆为：

- `ProvisioningEndpoints.cs`：统一入口，并保持原路由注册顺序。
- `ProvisioningEndpoints.UpdateEnvironment.cs`：更新状态、环境读取、检测、验证与保存。
- `ProvisioningEndpoints.ServerSetup.cs`：配置草稿、预检、配置应用和首次初始化。
- `ProvisioningEndpoints.Installation.cs`：SteamCMD 与 Avorion 服务端安装。
- `ProvisioningEndpoints.FileSystem.cs`：服务器目录浏览。

所有原 `app.Map*` 路由块按原顺序直接移动；幂等键、请求哈希、VerifiedCommand、Operation queue、错误码和安全检查没有重写。

拆分过程使用一次性工作流按明确路由锚点切分原文件，并校验拆分前后 endpoint registration 数量一致；一次性工作流已经从最终 PR 差异中删除。

## 当前验证状态

PR #5 已创建。现有 GitHub CI 必须对当前 head 完整通过后再合并，检查包括架构边界、.NET Release 构建、后端验证套件、前端 TypeScript 类型检查、Vite 生产构建和 Sites wrapper 测试。

本轮没有修改 Agent 业务实现、SQLite 数据库/schema、Core contract、Lua MOD、Avorion 游戏写操作、前端页面逻辑或视觉样式。

## 后续清理顺序

1. PR #5 当前 head 的 CI 通过并合并。
2. 单独拆 `SqliteStore.cs`，继续共享同一个 SQLite 数据库和现有 schema，不改变持久化行为。
3. 单独拆 `ManagementBridge.cs`，保持协议、MOD 版本兼容和现有验证逻辑不变。
4. 整理测试文件结构，但保留 FakeServer 和真实数据边界测试。
5. 完成基线实际运行验收后，再继续普通炮塔、舰船、活动和星区建设等新功能。

任何历史测试通过都不能替代当前代码版本的 CI 或真实游戏验收。
