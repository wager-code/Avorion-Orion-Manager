export type InventoryItem = {
  slot: number;
  amount: number;
  itemType: "turret" | "turret-template" | "system-upgrade" | "vanilla-item" | "usable-item" | "unknown";
  name: string;
  rarity: number | null;
  script: string | null;
  seed: string | null;
  title: string | null;
  icon: string | null;
  weaponType: string | null;
  weaponCategory: "armed" | "mining" | "salvaging" | "heal" | "none" | null;
  turretSlotType: "unspecified" | "armed" | "unarmed" | "point-defense" | null;
  material: number | null;
  averageTech: number | null;
  maxTech: number | null;
  dps: number | null;
  damage: number | null;
  reach: number | null;
  fireRate: number | null;
  accuracy: number | null;
  turretSlots: number | null;
  size: number | null;
  numWeapons: number | null;
  armed: boolean | null;
  civil: boolean | null;
  coaxial: boolean | null;
  seeker: boolean | null;
  continuousBeam: boolean | null;
};

export type InventorySnapshot = {
  ownerKind: "player" | "alliance";
  ownerIndex: number;
  ownerName: string;
  total: number;
  occupiedSlots: number;
  maxSlots: number;
  items: InventoryItem[];
  sampledAt: string;
  componentVersion: string;
};

export type CatalogEntry = {
  id: string;
  itemType: "turret" | "system-upgrade";
  key: string;
  displayName: string;
  englishName: string;
  category: string;
  iconPath: string | null;
  iconAvailable: boolean;
  grantPolicy: "verified-grantable" | "catalog-only" | "story-blocked";
  script: string | null;
  weaponType: string | null;
  description: string;
};

export type CatalogPage = {
  total: number;
  items: CatalogEntry[];
  indexedIconCount: number;
  iconSource: string;
};

export type GrantRarity = "common" | "uncommon" | "rare" | "exceptional" | "exotic" | "legendary";

export const grantRarities: Array<[GrantRarity, string]> = [
  ["common", "普通"], ["uncommon", "优秀"], ["rare", "稀有"],
  ["exceptional", "卓越"], ["exotic", "异域"], ["legendary", "传奇"],
];

export const itemTypeLabels: Record<InventoryItem["itemType"], string> = {
  turret: "炮塔", "turret-template": "炮塔", "system-upgrade": "系统插件",
  "vanilla-item": "普通物品", "usable-item": "可使用物品", unknown: "其他",
};

export const rarityLabels: Record<number, string> = { [-1]: "简陋", 0: "普通", 1: "优秀", 2: "稀有", 3: "卓越", 4: "异域", 5: "传奇" };
export const materialLabels = ["铁", "钛", "纳奥石", "崔钢", "赛安金属", "欧格石", "阿沃里昂"];
export const weaponTypeLabels: Record<string, string> = {
  ChainGun: "机枪", PointDefenseChainGun: "点防机枪", PointDefenseLaser: "点防激光", Laser: "激光",
  MiningLaser: "采矿激光", RawMiningLaser: "R 型采矿激光", SalvagingLaser: "打捞激光", RawSalvagingLaser: "R 型打捞激光",
  PlasmaGun: "等离子炮", RocketLauncher: "火箭发射器", Cannon: "加农炮", RailGun: "轨道炮", RepairBeam: "维修光束",
  Bolter: "爆能炮", LightningGun: "闪电炮", TeslaGun: "特斯拉炮", ForceGun: "力场炮", PulseCannon: "脉冲炮", AntiFighter: "防空炮",
};

export const catalogTypeLabels = { all: "全部", turret: "炮塔", "system-upgrade": "系统插件" } as const;
export const catalogPolicyLabels = {
  all: "全部状态", "verified-grantable": "已验证可发放", "catalog-only": "仅目录", "story-blocked": "剧情禁止",
} as const;
export const catalogCategoryLabels: Record<string, string> = {
  armed: "武装", "point-defense": "点防", mining: "采矿", salvaging: "打捞", heal: "维修", system: "舰船系统",
};

export function formatValue(value: number, maximumFractionDigits = 1) {
  return value.toLocaleString("zh-CN", { maximumFractionDigits });
}

export function turretFacts(item: InventoryItem) {
  if (item.itemType !== "turret" && item.itemType !== "turret-template") return null;
  const facts = [
    item.weaponType ? (weaponTypeLabels[item.weaponType] ?? item.weaponType) : null,
    item.material === null ? null : materialLabels[item.material] ?? `材质 ${item.material}`,
    item.averageTech === null ? null : `科技 ${item.averageTech}`,
    item.turretSlots === null ? null : `${item.turretSlots} 槽`,
    item.dps === null ? null : `${formatValue(item.dps)} DPS`,
    item.reach === null ? null : `射程 ${formatValue(item.reach, 0)} m`,
    item.coaxial ? "同轴" : null, item.seeker ? "追踪" : null, item.continuousBeam ? "连续光束" : null,
  ].filter((value): value is string => Boolean(value));
  return facts.length > 0 ? facts.join(" · ") : null;
}
