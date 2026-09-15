# 当前交接

最后更新：2026-09-15。
当前任务：M0-10（后端大文件职责拆分）。
工作分支：`cleanup/management-bridge-2026-09-15`。
目标分支：`main`。
当前 Pull Request：#7（待创建/验证）。

## 用户当前目标

保持 GitHub 公开，让其他人可以查看、Fork 和提交 Pull Request，但不能直接修改受保护的 `main`。项目主要由 AI 辅助开发，因此稳定代码只通过独立分支和 PR 进入 `main`。

用户要求继续清理长期 AI 开发留下的重复、多余和过大的源码结构，同时不得为了“整理”破坏现有功能、真实数据边界、API 行为、数据库行为、管理桥接协议或 UI。

## 已完成的整理

- PR #1：仓库卫生清理与接续收敛，已合并。
- PR #2：前端更新模块去重复并加入 GitHub Actions CI，已合并；CI 通过。
- PR #3：全局 `styles.css` 按原顺序拆为 8 个区块，已合并；CI 通过。
- PR #4：Core `Services.cs` / `ApiModels.cs` 按职责拆分，已合并；CI 通过。
- PR #5：`ProvisioningEndpoints.cs` 按更新环境、服务器配置、安装、文件系统职责拆分，已合并；CI 通过。
- PR #6：`SqliteStore.cs` 按共享核心、自动任务/内存策略、更新/配置、性能、Operation 职责拆分，已合并。首轮 CI 暴露拆分时遗漏接口声明，恢复原接口列表后第二轮 CI 全部通过再合并；数据库文件、schema 与 SQL 未改变。

## M0-10 当前改动

本轮处理原 `backend/src/AvorionAdmin.Agent/ManagementBridge.cs`。为降低风险，当前先按两个顶层职责拆分，不重写协议和业务方法：

- `ManagementBridgeProtocol.cs`：保留 `ManagementBridgeProtocol` 的帧解析、版本/能力检查、响应字段验证和命令编码 helper。
- `ManagedServerControlService.ManagementBridge.cs`：保留原 `ManagedServerControlService` 中玩家/联盟奖励、邮件、Inventory、System Upgrade、管理查询、星区卸载等桥接方法。

拆分过程按原文件两个顶层 class 的明确边界切开；两个 class 的正文均原样移动，命令字符串、协议版本、capability 校验、异常映射和安全验证没有重写。一次性拆分工作流已经从最终差异删除。

## 当前验证状态

PR #6 已在修复接口声明后通过完整 CI 并合并到 `main`。当前 ManagementBridge 分支已完成第一阶段拆分，下一步创建 PR #7，并等待现有 GitHub CI 完整执行：架构边界、.NET Release 构建、后端验证套件、前端 TypeScript 类型检查、Vite 生产构建和 Sites wrapper 测试。

本轮没有修改 API Contract、SQLite、Lua MOD、Avorion 游戏命令语义、前端页面逻辑或视觉样式。

## 后续清理顺序

1. 创建 PR #7，CI 通过后合并 ManagementBridge 第一阶段职责拆分。
2. 评估 `ManagedServerControlService.ManagementBridge.cs` 是否仍有必要按“奖励/库存/查询与星区”进一步细分；只有在能保持方法块原样移动且收益明显时才继续拆。
3. 进入 M0-11，整理超大测试入口，保留 FakeServer 和真实数据边界测试。
4. 完成基线实际运行验收后，再继续普通炮塔、舰船、活动和星区建设等新功能。

任何历史测试通过都不能替代当前代码版本的 CI 或真实游戏验收。
