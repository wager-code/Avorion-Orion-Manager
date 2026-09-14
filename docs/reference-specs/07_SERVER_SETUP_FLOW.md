# 首次部署 UX

## 起点

首次打开：

```text
欢迎使用 Avorion 中文服务器管理中心

[新建服务器]
从零安装 SteamCMD + Avorion Server

[自动检测已有服务器]
扫描本机已有安装

[手动导入已有服务器]
选择路径导入
```

## 新建服务器向导
1. 系统环境检查
2. SteamCMD 检测/安装
3. Avorion Dedicated Server 安装
4. Galaxy 创建
5. 基础服务器配置
6. RCON 配置（建议自动生成安全密码）
7. 端口/网络自检
8. 首次启动
9. 健康检查

边界（2026-09-07）：专用 OrionAdminBridge 已获授权，采用单独管理组件流程；现有安装与配置向导不自动安装 MOD。M1 仅做隔离能力验证，见 `17_MANAGEMENT_MOD_STAGE_M1.md`。
11. 完成 → 总览

## 当前首次初始化实现边界

- 只对“创建新 Galaxy + 启用 RCON”开放 `server.initialize`。
- 首次启动必须等待 Avorion 自己生成 `server.ini`，不得提前伪造。
- 初始化完成后按顺序发送 `/save`、`/stop`，并确认进程安全退出后才允许写 RCON。
- RCON 写入完成后重新启动；只有进程仍在运行且真实 RCON 认证通过，页面才显示“服务器已启动并验证”。
- 浏览器刷新或断线后通过 Operation ID 恢复进度；RCON 密码不得持久化或出现在 Operation 结果中。

## 用户体验原则
- 不给小白直接显示 SteamCMD 命令作为主要交互
- 可以提供“高级/查看执行日志”
- 安装失败要给人类可读错误，例如“SteamCMD 下载失败”“安装目录无写入权限”
- Agent 自己记录完整技术日志供排错
