# 新电脑运行与验证

## 环境

Windows 10/11 x64；Node.js 24.x；.NET 8 SDK（SDK 与运行时都需要，只有 Runtime 无法开发）。
旧机使用 Node 24.19.0、pnpm 11.19.0、.NET SDK 9.0.317 + .NET/ASP.NET Core 8.0.30。项目目标 net8.0。
初始化脚本通过 npx 使用 packageManager 锁定的 pnpm 11.19.0，不要求全局安装 pnpm。需要能访问 npm/NuGet；失败时保留错误，不禁用验证冒充成功。

## 一键流程

- `01-prepare.cmd` → 还原依赖、Release 后端构建、前端类型检查与生产构建。
- `02-start.cmd` → 本机 5088 API + 4173 Vite，自动打开控制页。子服务隐藏运行；保持启动窗口，Ctrl+C 停止该窗口启动的后台进程。
- 启动管理界面不会启动 Avorion 游戏服务端。首次界面应显示未配置/不可用，需在更新页面配置新电脑路径。
- 如果端口已占用，脚本报错而非杀死未知进程。先关闭重复启动窗口或由开发者检查。
- `.local/` 存放新电脑的运行日志和管理数据库，不应提交或打入源码包。

## 手动验证

在项目根目录的 PowerShell 中执行：

```powershell
dotnet build .\backend\AvorionAdmin.sln -c Release
dotnet run --project .\backend\tests\AvorionAdmin.Tests\AvorionAdmin.Tests.csproj -c Release
Set-Location .\frontend-prototype
npx --yes pnpm@11.19.0 install --frozen-lockfile
npm run typecheck
npm run build
npm run test:sites
```

生产前端构建保留 worker/Sites 所需文件，仅为现有构建完整性；`.openai/hosting.json` 只有空资源声明，没有绑定外部项目，本包不会发布网站。
不建议直接使用 `vite preview` 联调，因为当前 API 代理定义在开发服务器配置。用启动脚本或 Vite dev server 联调。

## 修改端口 / 隔离冒烟验证

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\run-local.ps1 -ApiPort 5188 -WebPort 4273 -SmokeTest
```

SmokeTest 等 API 与网页就绪，检查前端 /api 代理后退出，停止自己启动的进程；不启动/安装游戏。
真实游戏测试与项目启动分开。新电脑端口是否开放、Steam 是否可连接、GPU/游戏客户端是否安装，均要现场确认。

## MOD 安装仅用于已确认隔离服

停止目标测试游戏服务端；把 `management-mod/OrionAdminBridge` 放到独立 Mods 目录；按官方 modconfig.lua 格式引用实际绝对路径。
不要复制旧 `D:/AvorionAdmin-E2E` 配置；不要覆盖一个已有的 modconfig.lua。
MOD 为一个薄命令入口、十个领域/协议/分派模块和 modinfo.lua，无原版脚本替换。普通游戏客户端无需安装已在正式本机测试服验证。
参考官方 API：`source_docs/Documentation_Avorion_2.5.13.zip`，可按需解压。
安装器与更新页组件 UI 尚未提供，需要按当前需求继续开发。

## 迁移边界

不迁移旧机器服务端安装、Galaxy、账号/秘密、数据库、浏览器会话和机器权限。
若要继续原存档，需要用户另外提供其游戏备份并重新选路径；不把源码当作游戏备份。
启动脚本只启动 API/前端；关闭脚本不代表游戏服务端已经安全停服，若运行了游戏，应先用控制页安全关闭。
