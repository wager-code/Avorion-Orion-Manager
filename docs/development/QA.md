# QA 指南

## CI 基线

Pull Request 的 Windows CI 会依次执行：

1. `tools/check-architecture.ps1`
2. .NET 8 Release 构建
3. 后端验证套件
4. 前端依赖安装、类型检查与生产构建
5. Sites wrapper 测试

架构检查同时对浏览器 QA 脚本执行 `node --check`，并拒绝写死的 Playwright 模块目录或浏览器可执行文件路径。

## 浏览器 QA 运行时

所有 `frontend-prototype/scripts/*.mjs` 和 `tools/capture-*.cjs` 通过 `tools/qa-browser-runtime.cjs` 启动 Chromium。发现顺序为：

1. `QA_PLAYWRIGHT_PATH` 指定的 Playwright 模块
2. 当前 Node 工作区中的 `playwright`
3. 当前用户目录下 Codex bundled runtime 的 Playwright

浏览器发现顺序为：

1. `QA_BROWSER_EXECUTABLE` 指定的可执行文件
2. Windows 上已安装的 Chrome 或 Edge
3. Linux 上常见的 Chrome 或 Chromium
4. 都未发现时，由 Playwright 使用自己的 bundled browser

路径必须来自环境变量或当前机器发现结果，脚本不得提交个人用户名、盘符或固定安装目录。

## 真实安装 fixture

涉及真实 SteamCMD 或 Avorion Dedicated Server 文件的脚本还需要：

- `QA_STEAMCMD_ROOT`：包含 `steamcmd.exe` 的目录
- `QA_AVORION_SERVER_ROOT`：包含 `bin/AvorionServer.exe` 与 `bin/ServerRunner.exe` 的目录
- `QA_STEAMCMD_INSTALL_TARGET`：可选，SteamCMD 安装失败用例要确认未残留的目标目录

这些变量只用于测试进程，不写入源码、报告或 Git。运行视觉脚本前仍需按脚本要求启动 API、前端预览和对应 fixture。
