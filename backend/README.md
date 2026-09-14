# Avorion Admin Backend

服务器管理模块的 R0/R1 真实只读数据与 R2 写操作安全底座。

当前主机采用单节点进程：ASP.NET Core API 通过 `IServerProbe`、`IBackupScanner`、`ILogReader` 等 Agent 边界访问 Windows。后续多节点部署时可以把这些接口替换为认证后的远程 Agent 传输，而不改变浏览器 API Contract。

## 当前安全范围

- 真实读取：进程、CPU/RAM、磁盘、主机网络、Galaxy 最近写入、备份文件、日志、SteamCMD/可执行文件路径。日志路径未单独配置时，会从已验证的受管启动档案自动解析 Galaxy 目录，并且只读取顶层 `serverlog*.txt`。备份路径优先使用明确配置，其次读取受管 Galaxy 的 `server.ini/backupsPath`；该值为空时使用 Avorion 的 `%APPDATA%\Avorion\backups` 默认目录，并且只扫描 `.bak` 文件。
- SQLite：性能历史和长操作状态。
- 写操作安全：仅本机可签发的短期 HttpOnly 管理员会话、SameSite=Strict Cookie、CSRF Header、Origin 限制、任务幂等键、进度持久化与 Agent 重启后的失败关闭。
- 命令策略：固定清单并默认拒绝；除已验证的安装、配置、初始化与服务器控制命令外，`avorion.update.check`、`avorion.files.verify` 和独立的更新安全点创建命令也已按各自边界解锁，不存在任意命令执行入口。
- SteamCMD 安装：只接受本地新目录，从 Valve 官方固定 HTTPS 地址下载，记录 SHA-256，限制归档大小与解压边界，验证 `Valve` / `Valve Corp.` Authenticode 签名并原子落盘；该步骤不会运行 `steamcmd.exe`。
- Avorion 服务端安装：仅运行已验证的 SteamCMD，固定 `+force_install_dir`、匿名登录、App `565060`、`-beta public`、`validate` 与退出参数，不经 Shell；只在精确成功标记和两个服务端程序存在后原子发布，不启动服务端。
- 真实诊断：只对能够取得证据的项目给出正常/异常；RCON、Steam Query、游戏端口和 MOD 连接未实现协议前返回 `unknown`。
- 更新环境：只读浏览服务器目录；在已配置位置、常用目录和已知 Steam 库中有限检测；真实验证 `steamcmd.exe`、`AvorionServer.exe`、读取权限、版本线索和磁盘可用空间；验证通过后把路径保存到本机 SQLite。
- 服务器配置草稿：只把服务器名、Galaxy、端口和选择项等非敏感字段保存到本机 SQLite；RCON 密码不入库、不写日志、查询接口不返回。
- 服务器配置预检：真实检查已保存更新环境、Galaxy 本地路径、字段约束，以及本机 TCP/UDP 监听表中的端口占用；防火墙变更和管理 MOD 只返回“尚未执行”警告，不伪报成功。
- 服务器配置应用：重新预检并确认精确服务端程序已停止后，原子写入不含秘密的受控启动档案；已有 Galaxy 且 `server.ini` 结构无歧义时，只更新已验证的端口、RCON 回环绑定/密码和服务器名/最大玩家数。新 Galaxy 不伪造 `server.ini`，RCON 等首次初始化后再配置；监听地址、独立 Query 端口、防火墙和管理 MOD 仍延后。
- 新 Galaxy 首次初始化：`server.initialize` 只接受已启用 RCON 的全新 Galaxy；按固定启动档案运行精确 `AvorionServer.exe`，等待服务端自身生成 `server.ini`，向标准输入发送 `/save`、`/stop` 并确认安全退出，停服后原子写入 RCON，再重启并通过 Source RCON 认证探针确认健康。密码只存在于请求/工作内存和 Avorion 必需的 `server.ini`，Operation 证据不回传秘密。
- 更新检查：`avorion.update.check` 验证 SteamCMD 的 Valve 签名后，用固定参数匿名查询 App 565060 的 public Build ID；`avorion.files.verify` 只读校验两个服务端程序的 PE 文件头、大小与 SHA-256。两项都通过持久化 Operation 返回进度、结果和失败日志，均不会运行 `app_update validate` 或修改服务端文件。
- 更新前安全点：`avorion.update.rollback-point.create` 只在受管服务端已停止且更新环境/启动档案一致时执行；把完整服务端安装目录和当前 Galaxy 分开复制到服务端同盘的 `.avorion-admin-update-rollback`，拒绝符号链接与目录联接，逐文件记录并复核 SHA-256，最后才把暂存目录原子发布。它不进入 Avorion `.bak` 备份列表，不恢复文件，也不运行更新；Windows 短暂文件占用只进行有界发布重试，失败阶段与系统原因写入 Operation。
- 写操作：普通控制页启动、保存、安全停服和安全重启已经验证；完整服务端更新、备份恢复和强制终止仍保持锁定，返回 `COMMAND_NOT_VERIFIED`。
- 默认只监听 `127.0.0.1:5088`。

当前已具备本机管理员会话和 CSRF 写保护，但还没有面向公网的账号登录、TLS 终止、设备绑定或权限角色体系，因此不得把 API 直接暴露到公网。更新页的目录浏览、检测、路径验证、非敏感服务器配置草稿、真实预检和安全配置应用已接入本 API；其余页面仍需按模块逐步替换 Mock 数据。

浏览器通过 `POST /api/v1/session/local` 在 Agent 本机建立短期会话。所有 POST、PUT、PATCH、DELETE 请求（会话创建本身除外）必须同时携带会话 Cookie 与 `X-CSRF-Token`。会创建长任务的接口还必须提供 8–128 字符的 `Idempotency-Key`。

## 配置

复制 `src/AvorionAdmin.Api/appsettings.json` 中的 `Avorion` 节点到部署配置或环境变量。路径为空或不存在时，API 返回 `unavailable`，不会生成 Mock 数据。

## 运行

```powershell
dotnet restore AvorionAdmin.sln
dotnet run --project src/AvorionAdmin.Api/AvorionAdmin.Api.csproj
```

## 验证

```powershell
dotnet run --project tests/AvorionAdmin.Tests/AvorionAdmin.Tests.csproj
```
