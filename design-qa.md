# 页面视觉 QA

## P0 当前基线浏览器 QA（2026-09-16）

- 更新向导在 1920×1080、1440×900 验证“保存并运行真实预检”的即时忙碌文案、`aria-busy`、失败原因和重试恢复；草稿保存与预检写请求均由浏览器拦截，未覆盖当前服务器配置，两个视口均无横向溢出。
- GitHub `main` 的复核摘要使用 `rconPassword.length >= 8` 判断密码状态；空值或短密码明确提示返回第 2 步重新输入，不再显示“密码已设置”。
- 共享 Inventory 的真实目录与图标接口在 1920×1080、1440×900、1672×941 加载 58/58 个图标；筛选得到 11 个已验证可发放项、19 种炮塔，搜索“采矿激光”得到 2 项。目录/图标接口无失败，三个视口均无横向溢出。
- 全站 11 个页面在 1440×900、1920×1080 共采集 22 个只读状态；页面错误与横向溢出均为 0。未配置受管运行档案时，控制、内存、备份、日志、玩家、联盟和奖励的真实数据接口按合同返回 503，页面均显示明确不可用原因并禁用危险操作，没有伪造成功或数据。
- 当前运行环境已真实验证 SteamCMD `D:\\SteamCMD\\steamcmd.exe`、Avorion 服务端 `E:\\AvorionServer` 和 58 项物品目录，但服务端状态仍为 `MANAGED_PROFILE_MISSING`；本轮不启动游戏服、不应用配置、不发放物品。隔离 Galaxy 端到端验收归 M0-05。

final result: passed（M0-02～M0-04）

## M0-05 隔离 Galaxy 真实验收（2026-09-16）

- 目标固定为 `E:\\AvorionServer` 与 `E:\\MyGalaxy`；发现并修正草稿中错误的嵌套路径后，SteamCMD、服务端、Galaxy 与 27000/UDP、27003/UDP、27115/TCP 真实预检全部通过。
- 原 `server.ini` 已备份至 `E:\\MyGalaxy\\.orionadmin-backups\\m0-05-20260916-213316`；配置通过原子写入与回读校验，RCON 仅绑定 `127.0.0.1:27115`。
- Avorion Build 22295362 两次受控启动成功，均验证精确进程路径与 RCON 认证；运行态来源为 `managed-profile+rcon+orion-bridge`，OrionAdminBridge 0.10.0 返回 1 个已知玩家、1 个联盟及玩家 4 个真实库存槽位。
- 全站运行态 11 个页面 × 2 个视口无页面错误或横向溢出；严格 Inventory 回归逐一打开两门炮塔，确认采矿激光、机枪、铁材质、科技、DPS、射程均显示且未泄漏 `userdata`。
- `/save` Operation 返回 `save-command-acknowledged`；两次安全停服均返回 `rcon-authenticated`、`stop-command-acknowledged` 和精确 PID 退出证据。未调用强制终止，服务器最终保持 stopped。
- Steam Query 在运行态超时并明确显示 degraded；这属于当前未完成的 Query 协议/端口验证范围，不影响 RCON、Bridge 与安全停服结论。

final result: passed（M0-05）

## 玩家/联盟 Inventory（2026-09-09）

- Inventory 作为共享组件嵌入既有玩家详情和联盟资产弹窗，没有增加重复页面、玩家列表或联盟列表。
- 正式 Galaxy 联盟 #5 的真实 Inventory 显示 1 / 1000 已用槽位、真实货舱扩展系统插件、游戏实际稀有度和槽位；白名单插件与稀有度选择、二次确认均可操作。
- 1440×1000 与 1920×1080 下弹窗完整位于视口，页面无横向溢出；console error 和 page error 均为 0。
- 截图：`.e2e/inventory-alliance-1440x1000.png`、`.e2e/inventory-alliance-1920x1080.png`、`.e2e/inventory-confirm-1440x1000.png`。

final result: passed

## 游戏管理 · 奖励中心（2026-09-09）

### 对照上下文

- 用户确认的视觉参考：`E:\AvorionCNManager\reward-center-proposal.png`。
- 最终实现：`E:\OrionAdmin\.e2e\reward-center-1440x900.png`、`E:\OrionAdmin\.e2e\reward-center-1920x1080.png`。
- 同视口完整并排对照：`E:\OrionAdmin\.e2e\reward-center-comparison-1440x900.png`。
- 参考与实现均按 1440×900、deviceScaleFactor 1 对照；另以 1920×1080 验证宽屏响应式。
- 页面状态：正式本机 Avorion 服务器运行，OrionAdminBridge 0.8.0 已连接，已知真实玩家 #1 被选为最小身份目标；最近批次来自真实持久化 Operation。

### 视觉与职责结论

- 深海军蓝侧栏、浅灰工作区、白色卡片、蓝色主要操作、绿色运行状态、四张摘要卡和左右批次工作区均与确认参考保持同一 A 风格。
- 真实数据只有 1 个已知目标，参考图示例为 3 个；实现保持真实数量，没有伪造玩家或批次。
- 目标选择器只显示名称和索引，不重复玩家在线状态、所在星区或个人资产；最近批次只显示执行摘要，不复制玩家列表。
- 1440×900 与 1920×1080 均无页面级横向溢出；主要表单、批次摘要和确认层没有裁切或错位。
- 完整并排对照未发现 P0、P1 或 P2 视觉问题。动态数量和真实历史文案差异属于数据状态，不是实现偏差。

### 交互与技术验收

- 搜索真实已知玩家、选择目标、邮件/直接到账切换、资产编辑、二次确认和取消均可操作；直接到账会隐藏邮件字段。
- 确认层显示精确目标数和资产摘要；本次 UI 验收只打开并取消确认，没有再次执行真实发放。
- 1440×900、1920×1080 Playwright 检查：目标身份最小化、直接模式隐藏邮件、确认摘要、按钮可用、最近批次和无横向溢出全部通过。
- 首轮截图发现并发读取奖励历史时产生预期外 401 控制台噪音；API 客户端改为对奖励批次读取预先建立管理员会话，复测 console errors 0、page errors 0。

final result: passed

## 联盟资产与奖励（2026-09-08）

- 在既有联盟详情增加「资产与发放」弹窗，没有创建第二个联盟页面或重复联盟列表。
- 1440×900 与 1920×1080 均展示真实 Credits 与七种矿物、单资产发放表单和额度说明，无横向溢出。
- 二次确认显示精确联盟名称、联盟索引、资产类型与数量；Playwright 没有 console error 或 page error。
- 截图：`.e2e/alliance-assets-1440x900.png`、`.e2e/alliance-assets-1920x1080.png`、`.e2e/alliance-assets-confirm-1440x900.png`。

## 玩家单人奖励（2026-09-08）

- 在既有玩家详情资产区内增加「发放奖励」，没有创建重复玩家页或奖励玩家列表。
- 1440×900 与 1920×1080 均无横向溢出，详情弹窗保持在视口内并可纵向滚动。
- 单人奖励表单明确一次只发一种资产、数量上限和禁止自动重试；确认窗口显示精确玩家名称、索引、资产类型和数量。
- Playwright 检查没有 console error 或 page error；截图：`.e2e/player-reward-1440x900.png`、`.e2e/player-reward-1920x1080.png`、`.e2e/player-reward-confirm-1440x900.png`。

## 玩家资产详情增量（2026-09-08）

- 正式主 API 5088、Galaxy 与 OrionAdminBridge 0.5.0 联调：玩家 #1 `超人特工队` 返回 Credits 5000 和七种资源；协议能力 `players.assets`、玩家身份关联及 401/400/404 边界均通过。
- 玩家资产没有创建重复页面，放在玩家管理的现有详情弹窗内；玩家列表仍只负责当前在线玩家。
- Playwright 使用只读 UI 夹具让当前离线玩家出现在列表，仅用于打开弹窗做布局检查；资产数值来自正式 5088 接口。1440×900、1920×1080 均无横向溢出，弹窗完整位于视口，Credits、七种资源、0.5.0 来源均可见。
- 浏览器 console error 与 page error 均为 0。截图：`.e2e/player-assets-1440x900.png`、`.e2e/player-assets-1920x1080.png`。

## 对照上下文

- 视觉基准：`E:\OrionAdmin\references\联盟管理_方案2_已确认.png`
- 最终实现：`E:\OrionAdmin\.e2e\alliance-management-implementation-1672x941.png`
- 完整同屏对照：`E:\OrionAdmin\.e2e\alliance-management-comparison-full.png`
- 主工作区聚焦对照：`E:\OrionAdmin\.e2e\alliance-management-comparison-focused.png`
- 源图与实现视口：1672x941，deviceScaleFactor 1。
- 页面状态：正式本机服务器运行；真实联盟 `#5`、真实成员 `#1`；OrionAdminBridge 0.4.0 已连接；成员详情弹窗关闭。

## 最终视觉对照

- 布局：桌面宽度下将联盟页侧栏收敛到 284px；标题、四张摘要卡、330px 联盟列表、18px 工作区间隔和 636px 双卡高度已对齐基准。
- 字体与间距：标题、摘要数字、联盟名、身份条、表头和成员行层级清楚；采用项目既有字体栈与 A 风格间距。
- 颜色与表面：保留深海军蓝侧栏、主蓝选中态、浅灰背景、白色卡片、绿色实时状态和细边框/轻阴影。
- 图标：全部使用项目现有 Lucide 图标；当前只读协议未提供官方联盟徽记位图，因此统一使用 Shield 图标，不伪造不同联盟徽章、不使用自绘 SVG/CSS 图像。
- 内容：基准中的 3 个示例联盟和 5 名示例成员未写入产品。截图展示游戏当前实际返回的 1 个联盟、1 名成员、坐标、职级、舰船与空间站数量。
- 完整对照和主工作区聚焦对照未发现 P0、P1 或 P2 视觉问题；动态内容数量差异属于真实数据状态，不是实现缺失。

## 交互、响应式与可访问性

- “刷新”成功重读真实联盟；联盟行可选择；“查看玩家”成功打开成员详情，Escape 可关闭。
- “玩家管理”与“联盟管理”往返导航成功，最终 URL 为 `/alliances`。
- 1366x768、1024x768、768x900 均无页面级横向溢出；窄屏下摘要、联盟列表和成员详情按层级折叠。
- 按钮均为语义控件，选中联盟使用 `aria-pressed`，列表/详情具有区域标签，键盘焦点样式可见。
- 最终 Playwright：console errors 0，page errors 0；三个响应式视口 errors 0。

## 真实数据与协议验证

- 正式服务端 hello 返回协议 1、组件 0.4.0，并声明 `alliances.read`、`alliances.members`。
- 正式联盟列表返回联盟 `#5`、成员总数 1、领袖索引 1、主星区 `(-447, 47)`；成员详情返回真实名称、`Leader` 职级和离线状态。
- 联盟发现仅来自 `Server():getPlayers()` 的玩家所属关系；成员、排名、位置和在线状态来自 Avorion Alliance/Server API。
- 后端协议校验覆盖版本、能力、请求关联、聚合、分页、唯一索引、坐标成对性和成员数量；控制台验收测试通过。
- 前端 TypeScript/Vite production build 通过；后端 Debug/Release build 均 0 warning / 0 error。

## 迭代记录

1. 首次正式查询发现 Avorion 2.5.13 运行时的 `Alliance:getMembers()` 实际返回可变参数成员索引，与自动生成文档的表类型描述不同；已按真实运行时改为索引列表，并使用 `getMemberRank()`、`getMemberLocation()` 读取详情。
2. 初次截图发现侧栏宽度、摘要卡高度、左右工作区表面和整体高度偏离确认图；改为联盟页专用 284px 桌面侧栏、120px 摘要卡、分离双卡和 636px 工作区。
3. 重新用同一 1672x941 视口生成完整/聚焦并排对照，复测刷新、成员弹窗、导航和三个响应式视口。

final result: passed
## 玩家/联盟库存 · 炮塔只读详情（2026-09-10）

- 保持既有玩家/联盟详情内的共享 `InventoryPanel`，没有新增库存页面，也没有复制玩家在线状态或资产列表。
- 正式本机 Galaxy 加载 OrionAdminBridge 0.10.0；玩家 #1 的真实 Inventory 返回 2 门炮塔，分别识别为采矿激光与机枪，并显示材质、科技等级、槽位、DPS、射程和连续光束等真实属性。
- Avorion 的炮塔 `title` 在服务端运行时是 userdata；组件已拒绝把它字符串化并回退到真实 `name`，页面未显示内存地址。
- Playwright 只用浏览器路由让离线的已知玩家 #1 出现在在线专属列表，以便打开既有详情弹窗；库存、资产和 Bridge 版本均来自正式 5088 API，没有伪造库存物品。
- 1920×1080 与 1440×900 均显示 2 门炮塔，无横向溢出；console errors、page errors 和失败响应均为 0。截图及机器可读报告位于 `frontend-prototype/qa/inventory-real/`。

final result: passed

## 共享物品目录与真实静态图标（2026-09-10）

- 目录作为共享对话框嵌入既有玩家/联盟 Inventory，不新增页面，也不重复在线玩家、资产或联盟信息。
- 正式本机 API 自动发现 Avorion 客户端资源，安全索引 `data/textures/icons/` 下 922 个图片；58 个目录条目均成功加载真实静态图标。
- 目录包含 19 种普通炮塔和 39 个原版系统插件；筛选实测得到 19 个炮塔、11 个已验证可发放系统插件，搜索“采矿激光”得到 2 个匹配。仅 11 个已验证项可回填原有发放表单，目录项不会绕过既有写入边界。
- 1920×1080 与 1440×900 均无横向溢出；console errors、page errors 和失败响应均为 0。截图和机器可读报告位于 `frontend-prototype/qa/inventory-catalog/`。

final result: passed
