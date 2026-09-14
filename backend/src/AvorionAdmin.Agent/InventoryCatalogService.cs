using System.Security.Cryptography;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using AvorionAdmin.Core.Abstractions;
using AvorionAdmin.Core.Configuration;
using AvorionAdmin.Core.Models;
using Microsoft.Extensions.Options;
using Microsoft.Win32;

namespace AvorionAdmin.Agent;

public sealed class InventoryCatalogService : IInventoryCatalogService
{
    private const long MaximumIconBytes = 2 * 1024 * 1024;
    private readonly IReadOnlyDictionary<string, string> _icons;
    private readonly string _iconSource;
    private readonly DateTimeOffset _sampledAt = DateTimeOffset.UtcNow;

    public InventoryCatalogService(IOptions<ServerNodeOptions> options)
    {
        var root = FindIconRoot(options.Value.ClientDirectory);
        _icons = root is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : ScanIcons(root);
        _iconSource = root is null ? "unavailable" : "avorion-client";
    }

    public Task<InventoryCatalogPage> QueryAsync(
        InventoryCatalogQuery query,
        CancellationToken cancellationToken = default)
    {
        var error = InventoryCatalogPolicy.Validate(query);
        if (error is not null) throw new ArgumentException(error.Message, nameof(query));
        cancellationToken.ThrowIfCancellationRequested();
        var search = query.Query?.Trim();
        IEnumerable<InventoryCatalogDefinition> filtered = Definitions;
        if (!string.IsNullOrEmpty(search))
        {
            filtered = filtered.Where(item =>
                item.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                item.EnglishName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                item.Key.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (item.WeaponType?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (item.Script?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false));
        }
        if (query.ItemType is not null)
            filtered = filtered.Where(item => item.ItemType == query.ItemType);
        if (query.GrantPolicy is not null)
            filtered = filtered.Where(item => item.GrantPolicy == query.GrantPolicy);
        var materialized = filtered.ToArray();
        var items = materialized.Skip(query.Offset).Take(query.Limit).Select(item =>
        {
            var normalizedIcon = InventoryCatalogPolicy.NormalizeIconPath(item.IconPath);
            return new InventoryCatalogEntry(
                item.Id, item.ItemType, item.Key, item.DisplayName, item.EnglishName,
                item.Category, item.IconPath,
                normalizedIcon is not null && _icons.ContainsKey(normalizedIcon),
                item.GrantPolicy, item.Script, item.WeaponType, item.Description);
        }).ToArray();
        return Task.FromResult(new InventoryCatalogPage(
            materialized.Length, query.Offset, query.Limit, items, _icons.Count,
            _iconSource, _sampledAt, "avorion-2.5.13-verified-static-catalog"));
    }

    public async Task<InventoryIconFile?> ReadIconAsync(
        string iconPath,
        CancellationToken cancellationToken = default)
    {
        var normalized = InventoryCatalogPolicy.NormalizeIconPath(iconPath);
        if (normalized is null || !_icons.TryGetValue(normalized, out var fullPath)) return null;
        try
        {
            var info = new FileInfo(fullPath);
            if (!info.Exists || info.Length is <= 0 or > MaximumIconBytes ||
                info.Attributes.HasFlag(FileAttributes.ReparsePoint)) return null;
            var content = await File.ReadAllBytesAsync(fullPath, cancellationToken);
            if (!HasExpectedSignature(content, Path.GetExtension(fullPath))) return null;
            var type = Path.GetExtension(fullPath).ToLowerInvariant() switch
            {
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".webp" => "image/webp",
                _ => "application/octet-stream"
            };
            return new InventoryIconFile(
                content, type, Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static IReadOnlyDictionary<string, string> ScanIcons(string iconRoot)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>();
        pending.Push(iconRoot);
        while (pending.TryPop(out var directory))
        {
            try
            {
                foreach (var child in Directory.EnumerateDirectories(directory))
                {
                    if (!new DirectoryInfo(child).Attributes.HasFlag(FileAttributes.ReparsePoint)) pending.Push(child);
                }
                foreach (var file in Directory.EnumerateFiles(directory))
                {
                    var info = new FileInfo(file);
                    if (info.Attributes.HasFlag(FileAttributes.ReparsePoint) || info.Length is <= 0 or > MaximumIconBytes)
                        continue;
                    if (!HasExpectedFileSignature(info.FullName, info.Extension)) continue;
                    var relative = Path.GetRelativePath(iconRoot, info.FullName).Replace('\\', '/');
                    var normalized = InventoryCatalogPolicy.NormalizeIconPath($"data/textures/icons/{relative}");
                    if (normalized is not null) result.TryAdd(normalized, info.FullName);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Keep the verified subset already scanned; never broaden the path after a read failure.
            }
        }
        return result;
    }

    private static bool HasExpectedFileSignature(string path, string extension)
    {
        try
        {
            Span<byte> header = stackalloc byte[12];
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var count = stream.Read(header);
            return HasExpectedSignature(header[..count], extension);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string? FindIconRoot(string? configuredClientDirectory)
    {
        var steamRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(configuredClientDirectory))
        {
            var configured = ValidateIconRoot(configuredClientDirectory);
            if (configured is not null) return configured;
        }
        if (OperatingSystem.IsWindows())
        {
            AddRegistrySteamPath(steamRoots, Registry.CurrentUser, @"Software\Valve\Steam");
            AddRegistrySteamPath(steamRoots, Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam");
            AddRegistrySteamPath(steamRoots, Registry.LocalMachine, @"SOFTWARE\Valve\Steam");
        }
        try
        {
            foreach (var drive in DriveInfo.GetDrives().Where(drive => drive.IsReady))
            {
                steamRoots.Add(Path.Combine(drive.RootDirectory.FullName, "Steam"));
                steamRoots.Add(Path.Combine(drive.RootDirectory.FullName, "SteamLibrary"));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }

        var libraries = new HashSet<string>(steamRoots, StringComparer.OrdinalIgnoreCase);
        foreach (var steamRoot in steamRoots.ToArray())
        {
            var vdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            try
            {
                if (!File.Exists(vdf) || new FileInfo(vdf).Length > 2 * 1024 * 1024) continue;
                foreach (Match match in Regex.Matches(File.ReadAllText(vdf), "\\\"path\\\"\\s+\\\"([^\\\"]+)\\\""))
                    libraries.Add(match.Groups[1].Value.Replace(@"\\", @"\"));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
        foreach (var library in libraries.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var resolved = ValidateIconRoot(Path.Combine(library, "steamapps", "common", "Avorion"));
            if (resolved is not null) return resolved;
        }
        return null;
    }

    [SupportedOSPlatform("windows")]
    private static void AddRegistrySteamPath(HashSet<string> roots, RegistryKey hive, string subKey)
    {
        try
        {
            using var key = hive.OpenSubKey(subKey);
            if (key?.GetValue("SteamPath") is string path && !string.IsNullOrWhiteSpace(path)) roots.Add(path);
            if (key?.GetValue("InstallPath") is string install && !string.IsNullOrWhiteSpace(install)) roots.Add(install);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException) { }
    }

    private static string? ValidateIconRoot(string clientDirectory)
    {
        try
        {
            var clientRoot = Path.GetFullPath(clientDirectory);
            var iconRoot = Path.GetFullPath(Path.Combine(clientRoot, "data", "textures", "icons"));
            if (!iconRoot.StartsWith(clientRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase) || !Directory.Exists(iconRoot) ||
                new DirectoryInfo(iconRoot).Attributes.HasFlag(FileAttributes.ReparsePoint)) return null;
            return iconRoot;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool HasExpectedSignature(ReadOnlySpan<byte> content, string extension)
    {
        if (extension.Equals(".png", StringComparison.OrdinalIgnoreCase))
            return content.Length >= 8 && content[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        if (extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
            return content.Length >= 3 && content[0] == 0xff && content[1] == 0xd8 && content[2] == 0xff;
        if (extension.Equals(".webp", StringComparison.OrdinalIgnoreCase))
            return content.Length >= 12 && content[..4].SequenceEqual("RIFF"u8) && content.Slice(8, 4).SequenceEqual("WEBP"u8);
        return false;
    }

    private sealed record InventoryCatalogDefinition(
        string Id, string ItemType, string Key, string DisplayName, string EnglishName,
        string Category, string? IconPath, string GrantPolicy, string? Script,
        string? WeaponType, string Description);

    private static InventoryCatalogDefinition Turret(
        string key, string name, string english, string category, string icon, string weaponType) =>
        new($"turret:{key}", "turret", key, name, english, category, icon,
            InventoryGrantPolicies.CatalogOnly, null, weaponType, "原版普通炮塔类型；当前可查看，生成预览与发放尚未开放");

    private static InventoryCatalogDefinition System(
        string key, string name, string english, string icon, string policy) =>
        new($"system:{key}", "system-upgrade", key, name, english, "system", icon, policy,
            $"data/scripts/systems/{SystemScriptName(key)}.lua", null,
            policy == InventoryGrantPolicies.VerifiedGrantable ? "已通过真实库存发放边界，可从现有表单发放" :
            policy == InventoryGrantPolicies.StoryBlocked ? "剧情、钥匙或特殊系统，禁止普通管理员发放" :
            "原版系统已确认存在，尚未完成隔离写入验证");

    private static string SystemScriptName(string key) => key switch
    {
        "battery-booster" => "batterybooster", "cargo-extension" => "cargoextension",
        "energy-booster" => "energybooster", "engine-booster" => "enginebooster",
        "hyperspace-booster" => "hyperspacebooster", "radar-booster" => "radarbooster",
        "scanner-booster" => "scannerbooster", "shield-booster" => "shieldbooster",
        "mining-system" => "miningsystem", "trading-system" => "tradingoverview",
        "valuables-detector" => "valuablesdetector", _ => key
    };

    private static readonly InventoryCatalogDefinition[] Definitions =
    [
        Turret("chain-gun", "机枪", "Chaingun", "armed", "data/textures/icons/chaingun.png", "ChainGun"),
        Turret("point-defense-chain-gun", "点防机枪", "Point Defense Chaingun", "point-defense", "data/textures/icons/point-defense-chaingun.png", "PointDefenseChainGun"),
        Turret("point-defense-laser", "点防激光", "Point Defense Laser", "point-defense", "data/textures/icons/point-defense-laser.png", "PointDefenseLaser"),
        Turret("laser", "激光", "Laser", "armed", "data/textures/icons/laser-gun.png", "Laser"),
        Turret("mining-laser", "采矿激光", "Mining Laser", "mining", "data/textures/icons/mining-laser.png", "MiningLaser"),
        Turret("raw-mining-laser", "R 型采矿激光", "R-Mining Laser", "mining", "data/textures/icons/r-mining-laser.png", "RawMiningLaser"),
        Turret("salvaging-laser", "打捞激光", "Salvaging Laser", "salvaging", "data/textures/icons/salvage-laser.png", "SalvagingLaser"),
        Turret("raw-salvaging-laser", "R 型打捞激光", "R-Salvaging Laser", "salvaging", "data/textures/icons/r-salvaging-laser.png", "RawSalvagingLaser"),
        Turret("plasma-gun", "等离子炮", "Plasma Gun", "armed", "data/textures/icons/plasma-gun.png", "PlasmaGun"),
        Turret("rocket-launcher", "火箭发射器", "Rocket Launcher", "armed", "data/textures/icons/rocket-launcher.png", "RocketLauncher"),
        Turret("cannon", "加农炮", "Cannon", "armed", "data/textures/icons/cannon.png", "Cannon"),
        Turret("rail-gun", "轨道炮", "Railgun", "armed", "data/textures/icons/rail-gun.png", "RailGun"),
        Turret("repair-beam", "维修光束", "Repair Beam", "heal", "data/textures/icons/repair-beam.png", "RepairBeam"),
        Turret("bolter", "爆能炮", "Bolter", "armed", "data/textures/icons/bolter-gun.png", "Bolter"),
        Turret("lightning-gun", "闪电炮", "Lightning Gun", "armed", "data/textures/icons/lightning-gun.png", "LightningGun"),
        Turret("tesla-gun", "特斯拉炮", "Tesla Gun", "armed", "data/textures/icons/tesla-gun.png", "TeslaGun"),
        Turret("force-gun", "力场炮", "Force Gun", "armed", "data/textures/icons/force-gun.png", "ForceGun"),
        Turret("pulse-cannon", "脉冲炮", "Pulse Cannon", "armed", "data/textures/icons/pulsecannon.png", "PulseCannon"),
        Turret("anti-fighter", "防空炮", "Anti-Fighter Turret", "point-defense", "data/textures/icons/anti-fighter-gun.png", "AntiFighter"),

        System("battery-booster", "电池增容插件", "Battery Booster", "data/textures/icons/battery-pack-alt.png", InventoryGrantPolicies.VerifiedGrantable),
        System("cargo-extension", "货舱扩展插件", "Cargo Extension", "data/textures/icons/cargo-hold.png", InventoryGrantPolicies.VerifiedGrantable),
        System("energy-booster", "能量增幅插件", "Energy Booster", "data/textures/icons/electric.png", InventoryGrantPolicies.VerifiedGrantable),
        System("engine-booster", "引擎增幅插件", "Engine Booster", "data/textures/icons/rocket-thruster.png", InventoryGrantPolicies.VerifiedGrantable),
        System("hyperspace-booster", "超空间增幅插件", "Hyperspace Booster", "data/textures/icons/vortex.png", InventoryGrantPolicies.VerifiedGrantable),
        System("radar-booster", "雷达增幅插件", "Radar Booster", "data/textures/icons/radar-sweep.png", InventoryGrantPolicies.VerifiedGrantable),
        System("scanner-booster", "扫描器增幅插件", "Scanner Booster", "data/textures/icons/signal-range.png", InventoryGrantPolicies.VerifiedGrantable),
        System("shield-booster", "护盾增幅插件", "Shield Booster", "data/textures/icons/shield.png", InventoryGrantPolicies.VerifiedGrantable),
        System("mining-system", "采矿系统插件", "Mining System", "data/textures/icons/mining.png", InventoryGrantPolicies.VerifiedGrantable),
        System("trading-system", "贸易系统插件", "Trading System", "data/textures/icons/cash.png", InventoryGrantPolicies.VerifiedGrantable),
        System("valuables-detector", "贵重物探测插件", "Object Detector", "data/textures/icons/movement-sensor.png", InventoryGrantPolicies.VerifiedGrantable),

        System("arbitrarytcs", "全能炮塔控制系统", "All-round Turret Control System", "data/textures/icons/turret.png", InventoryGrantPolicies.CatalogOnly),
        System("autotcs", "自动炮塔控制系统", "Independent Turret Control System", "data/textures/icons/turret.png", InventoryGrantPolicies.CatalogOnly),
        System("civiltcs", "民用炮塔控制系统", "Civil Turret Control System", "data/textures/icons/turret.png", InventoryGrantPolicies.CatalogOnly),
        System("militarytcs", "军用炮塔控制系统", "Military Turret Control System", "data/textures/icons/turret.png", InventoryGrantPolicies.CatalogOnly),
        System("defensesystem", "防御系统", "Defense System", "data/textures/icons/shotgun.png", InventoryGrantPolicies.CatalogOnly),
        System("energytoshieldconverter", "能量转护盾系统", "Energy to Shield Converter", "data/textures/icons/shield.png", InventoryGrantPolicies.CatalogOnly),
        System("excessvolumebooster", "结构扩容系统", "Excess Volume Booster", "data/textures/icons/nanobot-wiring.png", InventoryGrantPolicies.CatalogOnly),
        System("fightersquadsystem", "战斗机中队系统", "Fighter Squadron System", "data/textures/icons/fighter.png", InventoryGrantPolicies.CatalogOnly),
        System("lootrangebooster", "战利品牵引系统", "Loot Collection System", "data/textures/icons/tractor.png", InventoryGrantPolicies.CatalogOnly),
        System("resistancesystem", "伤害抗性系统", "Resistance System", "data/textures/icons/edged-shield.png", InventoryGrantPolicies.CatalogOnly),
        System("shieldimpenetrator", "护盾固化系统", "Shield Impenetrator", "data/textures/icons/bordered-shield.png", InventoryGrantPolicies.CatalogOnly),
        System("transportersoftware", "传送器软件", "Transporter Software", "data/textures/icons/processor.png", InventoryGrantPolicies.CatalogOnly),
        System("velocitybypass", "速度旁路系统", "Velocity Security Control Bypass", "data/textures/icons/bypass.png", InventoryGrantPolicies.CatalogOnly),
        System("weaknesssystem", "弱点分析系统", "Weakness Analyzer", "data/textures/icons/metal-scales-plus.png", InventoryGrantPolicies.CatalogOnly),

        System("behemothcarriersystem", "巨兽航母控制系统", "Behemoth Carrier Control System", "data/textures/icons/behemoth-fighter.png", InventoryGrantPolicies.StoryBlocked),
        System("behemothciviltcs", "巨兽民用炮塔系统", "Behemoth Civil Turret System", "data/textures/icons/behemoth-turret-ctcs.png", InventoryGrantPolicies.StoryBlocked),
        System("behemothhyperspacesystem", "巨兽探索系统", "Behemoth Exploration System", "data/textures/icons/behemoth-hyperspace.png", InventoryGrantPolicies.StoryBlocked),
        System("behemothmilitarytcs", "巨兽军用炮塔系统", "Behemoth Military Turret System", "data/textures/icons/behemoth-turret-mtcs.png", InventoryGrantPolicies.StoryBlocked),
        System("smugglerblocker", "走私者拦截系统", "Smuggler Blocker", "data/textures/icons/smugglerblock.png", InventoryGrantPolicies.StoryBlocked),
        System("teleporterkey1", "传送门钥匙 I", "Xsotan Artifact I", "data/textures/icons/key1.png", InventoryGrantPolicies.StoryBlocked),
        System("teleporterkey2", "传送门钥匙 II", "Xsotan Artifact II", "data/textures/icons/key2.png", InventoryGrantPolicies.StoryBlocked),
        System("teleporterkey3", "传送门钥匙 III", "Xsotan Artifact III", "data/textures/icons/key3.png", InventoryGrantPolicies.StoryBlocked),
        System("teleporterkey4", "传送门钥匙 IV", "Xsotan Artifact IV", "data/textures/icons/key4.png", InventoryGrantPolicies.StoryBlocked),
        System("teleporterkey5", "传送门钥匙 V", "Xsotan Artifact V", "data/textures/icons/key5.png", InventoryGrantPolicies.StoryBlocked),
        System("teleporterkey6", "传送门钥匙 VI", "Xsotan Artifact VI", "data/textures/icons/key6.png", InventoryGrantPolicies.StoryBlocked),
        System("teleporterkey7", "传送门钥匙 VII", "Xsotan Artifact VII", "data/textures/icons/key7.png", InventoryGrantPolicies.StoryBlocked),
        System("teleporterkey8", "传送门钥匙 VIII", "Xsotan Artifact VIII", "data/textures/icons/key8.png", InventoryGrantPolicies.StoryBlocked),
        System("wormholeopener", "虫洞开启器", "Wormhole Opener", "data/textures/icons/wormhole.png", InventoryGrantPolicies.StoryBlocked)
    ];
}
