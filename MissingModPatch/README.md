# 旧交接包缺失 MOD 源码补丁

这是原开发目录的 orionadmin.lua，不是新写的替代版本。适用于新电脑已经解压旧包、可能已继续开发的情况。

把这个补丁 ZIP 解压到单独文件夹，然后在该补丁文件夹打开 PowerShell 执行（实际项目不在 E:\OrionAdmin 时请修改参数）：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Apply-Patch.ps1 -ProjectRoot "E:\OrionAdmin"
```

也可以把本补丁交给新电脑的 Codex，请它对实际项目目录执行上述脚本。

脚本只做三件事：补入 management-mod/OrionAdminBridge/data/scripts/commands/orionadmin.lua；把错误的全局 **/data/ 忽略规则改为仅忽略后端运行数据；更新 MANIFEST.sha256 中这两个文件对应的条目。不会用旧清单覆盖其他源码的哈希。已有不同 Lua 时拒绝覆盖；旧忽略文件与清单备份在项目 .migration-patch-backup/ 下。

看到 PATCH_PASS 表示补漏完成，不代表真实玩家入服/MOD 实测完成。然后按照原交接文档运行 01-prepare.cmd 和 02-start.cmd，继续独立测试服验证。

尚未在新电脑继续开发的，也可直接使用完整 r2 修正版，无需此补丁。补丁没有补入无页面引用的旧 mock.ts，也不改初始化脚本；要获得完整 r2 的源码预检工具请使用 r2 包或按需人工合并，勿覆盖你的新改动。
