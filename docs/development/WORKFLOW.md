# AI 开发与跨电脑同步流程

## 资料归属

- README：所有 AI 和开发者的当前入口。
- PLAN：任务编号、依赖、状态和验收条件的唯一主表。
- HANDOFF：当前工作现场、验证结果、阻塞和下一步。
- DECISIONS：用户明确决定与方案取舍。
- `01_USER_REQUIREMENTS.md` / `06_CAPABILITY_MATRIX.md` / `07_MODULAR_ARCHITECTURE.md`：需求、能力边界、架构。
- `docs/evidence/`、`docs/reference-specs/`、`docs/ai-review/`：历史证据、历史规格和历史评审，只用于追溯。
- Git 历史：每一次实际代码变更和已删除历史文件的留档。

不要维护两套互相冲突的“当前状态”或“接续提示词”。当前任务只看 README、PLAN、HANDOFF、DECISIONS 和能力矩阵。

## 每次开始

1. 克隆或更新仓库后，先检查 `git status`、当前分支和远端。
2. 有未提交修改先保留，不直接覆盖；分叉时检查原因，不用强推或硬重置规避。
3. 从 `main` 创建独立工作分支，不直接在 `main` 开发。
4. 阅读 HANDOFF 与 PLAN，只领取一个范围清楚的步骤。
5. 检查相关源码和规则；发现历史文档与源码不符时，以当前源码和真实验证为准，并修正文档。

## 每个可交接的小步骤结束

1. 完成修改，并执行与变更相符的验证。只有文档/仓库清理时检查链接、引用和差异；业务修改运行相关构建和测试。
2. 更新 PLAN 和 HANDOFF；能力变化同步能力矩阵，新的用户级决定同步 DECISIONS。
3. 检查 diff 和拟提交文件；不提交密码、运行数据库、Galaxy、完整日志、本机进程档案、SteamCMD/游戏程序、`node_modules` 或构建输出。
4. 提交信息包含任务编号，例如 `M1-02: add turret preview`。
5. 推送工作分支并核对远端 SHA。
6. 创建 Pull Request 合并到 `main`。
7. PR 中明确写：改了什么、如何验证、哪些没有验证、是否存在已知风险。
8. 网络或同步失败时如实说明；没有推送成功就不能称已经备份到 GitHub。

## Pull Request 与 main

`main` 是稳定分支。外部贡献者可以查看、Fork 和提交 Pull Request，但不直接修改 `main`。本项目自己的 AI/Codex 开发也遵守同一模式：独立分支 → 验证 → PR → 合并。

不对 `main` 做强制覆盖，不删除 `main`。仓库规则是保护措施，不是替代测试的理由。

## 换电脑

结束旧电脑工作时提交并推送当前工作分支或已完成的 PR 检查点；新电脑克隆后从 README/HANDOFF/PLAN 恢复。两台电脑不要同时修改同一个未合并工作分支。

Git 仓库保存源码与开发资料；游戏存档和本机秘密单独备份。

## 平台上展示进度

GitHub Issue/Projects 可以展示待开始、进行中、待验证、已完成、阻塞，但仓库里的 PLAN 仍是主表。平台讨论形成的新决定要回写 DECISIONS/HANDOFF，避免上下文只存在网页评论里。

自动 CI 可以验证架构、构建和部分测试，不能替代真实 Avorion 游戏写入、重启保存或人工 UI 验收。

## 本仓库同步方式

远端为 `https://github.com/wager-code/Avorion-Orion-Manager`，公开仓库，主分支 `main`。

当前推荐方式是工作分支 + Pull Request。无论使用 Git 命令行、GitHub 工具还是 Codex，都必须核对远端提交；不要把“本地已 commit”误认为“GitHub 已同步”。
