using System.Net;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using AvorionAdmin.Agent;
using AvorionAdmin.Core;
using AvorionAdmin.Core.Abstractions;
using AvorionAdmin.Core.Configuration;
using AvorionAdmin.Core.Models;
using Microsoft.Extensions.Options;

namespace AvorionAdmin.Api;

public sealed class VerifiedCommandRegistry
{
    private static readonly IReadOnlyList<CommandSafetyStatus> Commands =
    [
        new("server.start", true, "受控启动档案、精确进程路径与本机 RCON 健康检查已验证"),
        new("server.save", true, "精确进程归属、RCON 认证与 /save 命令确认已验证"),
        new("server.shutdown", true, "RCON /save、/stop 与进程退出确认已验证；不会自动强制终止"),
        new("server.restart", true, "安全停服、受控重启与 RCON 健康检查已验证"),
        new("server.force-stop", true, "仅接受当前受管可执行文件的精确 PID，要求二次确认文本并验证进程退出"),
        new("steamcmd.install", true, "固定 Valve 官方源、签名验证、原子落盘与失败清理已验证"),
        new("avorion.install", true, "固定 App 565060 匿名安装、参数隔离、原子落盘与失败清理已验证"),
        new("server.configure", true, "停服检查、非敏感启动档案、受控 server.ini 字段、原子写入与失败回滚已验证"),
        new("server.initialize", true, "固定启动参数、控制台 /save 与 /stop、安全 RCON 回写和认证健康检查已验证"),
        new("avorion.update.check", true, "固定 App 565060 与 public 分支、匿名 SteamCMD 查询、Valve 签名和参数隔离已验证"),
        new("avorion.files.verify", true, "只读路径重验证、PE 文件头、大小和 SHA-256 已验证；不会修复或覆盖文件"),
        new("avorion.update.rollback-point.create", true, "仅在停服状态复制服务端与 Galaxy，拒绝重解析点，全文件 SHA-256 复核后原子发布"),
        new("avorion.update", false, "停服、备份、更新与回滚链尚未验证"),
        new("backup.restore", true, "精确备份 ID、恢复前全量安全点、官方 --backup-file 恢复、保存停服与原状态重启链已验证"),
        new("sector.unload", true, "仅允许控制台组件卸载当前已加载且无在线玩家的单个星区，并在请求后重新查询实际加载状态"),
        new("player.reward.grant", true, "固定玩家索引与 Credits/七种矿物参数，单次额度受限，组件返回变更前后余额并精确复核"),
        new("alliance.reward.grant", true, "固定联盟索引与 Credits/七种矿物参数，单次额度受限，组件返回变更前后余额并精确复核"),
        new("reward.batch.create", true, "最多 50 名已知玩家，逐人持久操作、固定邮件唯一标识、精确附件复核且失败不自动重试"),
        new("player.inventory.system-upgrade.grant", true, "仅固定原版脚本白名单与稀有度，持久幂等操作、唯一 Seed 和库存增量复核"),
        new("alliance.inventory.system-upgrade.grant", true, "仅固定原版脚本白名单与稀有度，持久幂等操作、唯一 Seed 和库存增量复核")
    ];

    public WriteSafetyStatus GetStatus() => new(
        "deny-by-default",
        true,
        true,
        true,
        Commands);

    public bool IsVerified(string command) =>
        Commands.Any(item => item.Command.Equals(command, StringComparison.Ordinal) && item.Verified);
}
