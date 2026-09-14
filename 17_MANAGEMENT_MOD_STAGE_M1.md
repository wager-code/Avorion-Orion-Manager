# 管理 MOD M1：只读桥接验证

2026-09-07 用户确认改为专用服务端管理 MOD 方案，并授权开始基础验证。
本决定覆盖旧文档中禁止自定义管理 MOD 的限制，仅针对 OrionAdminBridge；第三方 MOD 管理范围不因此扩大。

本阶段：组件握手、协议版本、能力清单、在线玩家分页（index/name/online）、管理员会话认证、真实错误。
不实现游戏写操作、不修改既有 UI；玩家页继续先出图确认。

包：management-mod/OrionAdminBridge；版本 0.1.0；协议 1；仅声明兼容 Avorion 2.5.13。
只增加独立命令文件，不覆盖原版脚本，无回调/持续轮询/玩家或实体脚本，无持久化游戏数据。
serverSideOnly=true；客户端无需安装是设计目标，必须单独实测，不能由握手结果推断。

调用：浏览器 → 管理员会话 API → 已验证本机受管进程/RCON → /orionadmin → 官方 Server():getOnlinePlayers()。
仅 hello / players，拒绝游戏玩家调用、任意代码和未知动作；请求关联 ID、防止旧结果混入；单页上限 10；5 秒超时；响应有大小上限。
GET /api/v1/servers/local/management-bridge/hello
GET /api/v1/servers/local/management-bridge/players?offset=0&limit=10
读取前需通过现有 POST /api/v1/session/local 建立本机管理员会话。返回 live 时间戳；错误为 4xx/503，不伪造空列表。

验证仅使用 D:/AvorionAdmin-E2E，独立 API 5089；正式 5088 和 MyGalaxy 不安装组件。
需要验证：真实加载、握手、真实空列表、在线玩家实际字段、离线/断线恢复、重复分页/错误请求、停用后不可用、普通客户端加入。
离线玩家/联盟/舰船/事件在后续阶段逐项验证。

## 2026-09-07 本机验证结果

- Release 构建：0 错误、0 警告；完整后端回归通过。新增协议校验与 RCON 分包/中文字节重组测试通过。
- 真实 Avorion 2.5.13 日志确认加载 1 个 MOD；返回 hello / 0.1.0 / protocol 1。
- getOnlinePlayers 实际调用成功，当前无人在线：total=0，items=[]。没有使用测试玩家代替真实玩家。
- 无管理员会话 401，负 offset 和 limit=11 为 400，未知动作 404。
- 服务端停止时 503/SERVER_NOT_RUNNING；服务端运行但停用 MOD 时 503/BRIDGE_UNAVAILABLE。
- 停用组件后游戏服正常启动；重新启用后只读查询恢复。
- Agent 重启后管理查询恢复，游戏进程 PID 27356 未变。证据：D:/AvorionAdmin-E2E/BridgeEvidence/agent-restart-recovered.json。
- 已配置独立测试备份目录 D:/AvorionAdmin-E2E/LiveBackups，原 ini 中其他字节保持不变；真实日志确认采用该路径。
- 正式 5088 Agent 健康检查正常，MyGalaxy 未安装 MOD；正式前端未改动页面。

当前可复核：D:/AvorionAdmin-E2E/BridgeEvidence 下的 first-live-query、disabled-mod-unavailable、reenabled-query、agent-restart-recovered 等 JSON。
测试服已启动，地址 127.0.0.1:27100；Agent 5089（与正式 5088 分离）。

尚未验收：普通未装组件的游戏客户端连接、有真实在线玩家的非空列表、游戏内玩家发起该命令被拒绝的现场测试、多玩家分页/离线竞态及繁忙服务器性能。
这些需要真实客户端/玩家场景；不得将空服接口通过写成全部玩家能力通过。
下一步完成上述客户端验证，再按本轮确认流程规划玩家列表 UI 图片；更新页组件安装 UI 尚未开发。
