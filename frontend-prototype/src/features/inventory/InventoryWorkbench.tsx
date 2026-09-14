import * as Dialog from "@radix-ui/react-dialog";
import { BookOpen, Boxes, CircleCheck, PackagePlus, RefreshCw, Search, ShieldCheck, X } from "lucide-react";
import { useCallback, useEffect, useMemo, useState } from "react";
import { Button, ConfirmDialog, StatusPill } from "../../components/ui";
import { GameItemIcon } from "../../components/GameItemIcon";
import { apiFetch, createIdempotencyKey } from "../../lib/api";
import type { ApiOperation } from "../../types";
import {
  catalogCategoryLabels, catalogPolicyLabels, catalogTypeLabels, grantRarities, itemTypeLabels,
  rarityLabels, turretFacts, type CatalogEntry, type CatalogPage, type InventoryItem, type InventorySnapshot, type GrantRarity,
} from "./inventoryTypes";

export type InventoryWorkbenchOwner = { ownerKind: "player" | "alliance"; ownerIndex: number; ownerName: string };

type Mode = "inventory" | "catalog";
type Policy = keyof typeof catalogPolicyLabels;
type ItemType = keyof typeof catalogTypeLabels;

type Props = { open: boolean; owner: InventoryWorkbenchOwner | null; onOpenChange: (open: boolean) => void; onNotify: (message: string) => void };

async function apiError(response: Response, fallback: string) {
  const body = await response.json().catch(() => null) as { error?: { message?: string } } | null;
  return body?.error?.message ?? `${fallback}（HTTP ${response.status}）`;
}

export function InventoryWorkbench({ open, owner, onOpenChange, onNotify }: Props) {
  const [mode, setMode] = useState<Mode>("inventory");
  const [query, setQuery] = useState("");
  const [itemType, setItemType] = useState<ItemType>("all");
  const [policy, setPolicy] = useState<Policy>("all");
  const [inventory, setInventory] = useState<InventorySnapshot | null>(null);
  const [catalog, setCatalog] = useState<CatalogPage | null>(null);
  const [selected, setSelected] = useState<InventoryItem | CatalogEntry | null>(null);
  const [loading, setLoading] = useState(false);
  const [catalogLoading, setCatalogLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [catalogError, setCatalogError] = useState<string | null>(null);
  const [upgradeKey, setUpgradeKey] = useState("battery-booster");
  const [rarity, setRarity] = useState<GrantRarity>("rare");
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [operation, setOperation] = useState<ApiOperation | null>(null);
  const ownerKey = owner ? `${owner.ownerKind}:${owner.ownerIndex}` : null;

  const readInventory = useCallback(async (announce = false) => {
    if (!owner) return;
    const requestOwnerKey = ownerKey;
    setLoading(true); setError(null);
    try {
      const base = owner.ownerKind === "player" ? "players" : "alliances";
      const response = await apiFetch(`/api/v1/servers/local/${base}/${owner.ownerIndex}/inventory?offset=0&limit=50`, { headers: { Accept: "application/json" } });
      if (!response.ok) throw new Error(await apiError(response, "库存请求失败"));
      const next = await response.json() as InventorySnapshot;
      if (requestOwnerKey !== ownerKey || next.ownerKind !== owner.ownerKind || next.ownerIndex !== owner.ownerIndex) return;
      setInventory(next); if (announce) onNotify(`已刷新 ${owner.ownerName} 的 ${next.total} 个库存槽位`);
    } catch (caught) { if (requestOwnerKey !== ownerKey) return; setInventory(null); setError(caught instanceof Error ? caught.message : "无法读取真实库存"); }
    finally { if (requestOwnerKey === ownerKey) setLoading(false); }
  }, [onNotify, owner, ownerKey]);

  const readCatalog = useCallback(async () => {
    const requestOwnerKey = ownerKey;
    setCatalogLoading(true); setCatalogError(null);
    try {
      const response = await apiFetch("/api/v1/servers/local/inventory-catalog?offset=0&limit=100", { headers: { Accept: "application/json" } });
      if (!response.ok) throw new Error(await apiError(response, "目录请求失败"));
      const next = await response.json() as CatalogPage;
      if (requestOwnerKey !== ownerKey) return;
      setCatalog(next);
    } catch (caught) { if (requestOwnerKey !== ownerKey) return; setCatalogError(caught instanceof Error ? caught.message : "无法读取物品目录"); }
    finally { if (requestOwnerKey === ownerKey) setCatalogLoading(false); }
  }, [ownerKey]);

  useEffect(() => {
    setSelected(null); setOperation(null); setError(null); setCatalogError(null); setInventory(null); setCatalog(null); setQuery(""); setItemType("all"); setPolicy("all");
    if (owner) void readInventory();
  }, [ownerKey]);
  useEffect(() => { if (open && mode === "catalog" && !catalog) void readCatalog(); }, [catalog, mode, open, readCatalog]);
  useEffect(() => {
    if (selected && "grantPolicy" in selected && selected.grantPolicy === "verified-grantable" && selected.itemType === "system-upgrade")
      setUpgradeKey(selected.key);
  }, [selected]);

  const visibleItems = useMemo(() => {
    const needle = query.trim().toLocaleLowerCase("zh-CN");
    if (mode === "inventory") return (inventory?.items ?? []).filter((item) => itemType === "all" || (itemType === "turret" ? item.itemType.includes("turret") : item.itemType === "system-upgrade"));
    return (catalog?.items ?? []).filter((entry) =>
      (itemType === "all" || entry.itemType === itemType) && (policy === "all" || entry.grantPolicy === policy) &&
      (!needle || [entry.displayName, entry.englishName, entry.key, entry.weaponType, entry.script].some((value) => value?.toLocaleLowerCase("zh-CN").includes(needle))));
  }, [catalog, inventory, itemType, mode, policy, query]);

  const submitGrant = async () => {
    if (!owner || !inventory) return;
    const requestOwnerKey = ownerKey;
    setSubmitting(true); setError(null); setOperation(null);
    try {
      const base = owner.ownerKind === "player" ? "players" : "alliances";
      const ownerWord = owner.ownerKind === "player" ? "PLAYER" : "ALLIANCE";
      const response = await apiFetch(`/api/v1/servers/local/${base}/${owner.ownerIndex}/system-upgrade-grants`, { method: "POST", headers: { Accept: "application/json", "Content-Type": "application/json", "Idempotency-Key": createIdempotencyKey(`${owner.ownerKind}-system-upgrade`) }, body: JSON.stringify({ upgradeKey, rarity, confirmation: `GRANT ${ownerWord} SYSTEM ${owner.ownerIndex}` }) });
      if (!response.ok) throw new Error(await apiError(response, "插件发放失败"));
      const accepted = await response.json() as { operationId: string };
      let completed: ApiOperation | null = null;
      for (let attempt = 0; attempt < 50; attempt += 1) {
        const status = await apiFetch(`/api/v1/operations/${encodeURIComponent(accepted.operationId)}`, { headers: { Accept: "application/json" } });
        if (!status.ok) throw new Error(await apiError(status, "操作状态读取失败"));
        completed = await status.json() as ApiOperation; setOperation(completed);
        if (completed.status !== "queued" && completed.status !== "running") break;
        await new Promise((resolve) => window.setTimeout(resolve, 500));
      }
      if (requestOwnerKey !== ownerKey) return;
      if (!completed || completed.status === "queued" || completed.status === "running") throw new Error("插件发放仍在执行，可在操作记录中继续查看");
      if (completed.status !== "succeeded") throw new Error(completed.error?.message ?? "插件发放失败");
      const result = completed.result as { ownerKind: string; ownerIndex: number; upgrade: { key: string }; rarity: string; beforeCount: number; afterCount: number; outcome: string } | null;
      if (!result || result.ownerKind !== owner.ownerKind || result.ownerIndex !== owner.ownerIndex || result.upgrade.key !== upgradeKey || result.rarity !== rarity || result.afterCount !== result.beforeCount + 1 || result.outcome !== "granted-and-verified") throw new Error("插件回执与当前目标不一致，请刷新库存核对");
      await readInventory(); onNotify(`已向 ${owner.ownerName} 发放插件`);
    } catch (caught) { if (requestOwnerKey === ownerKey) { const message = caught instanceof Error ? caught.message : "插件发放失败"; setError(message); onNotify(message); } }
    finally { if (requestOwnerKey === ownerKey) setSubmitting(false); }
  };

  const isCatalog = mode === "catalog";
  const selectedPolicy = selected && "grantPolicy" in selected ? selected.grantPolicy : null;
  const selectedName = selected && "displayName" in selected ? selected.displayName : selected ? selected.title ?? selected.name : null;
  const selectedIcon = selected && "iconPath" in selected ? (selected.iconAvailable ? selected.iconPath : null) : selected?.icon;
  const selectedType = selected && "displayName" in selected ? (selected.itemType === "turret" ? "炮塔" : "系统插件") : selected ? itemTypeLabels[selected.itemType] : null;
  const selectedDescription = selected && "description" in selected ? selected.description : selected ? turretFacts(selected) : null;
  const canGrant = selectedPolicy === "verified-grantable";

  return <Dialog.Root open={open} onOpenChange={onOpenChange}>
    <Dialog.Portal>
      <Dialog.Overlay className="dialog-overlay" />
      <Dialog.Content className="inventory-workbench" aria-describedby="inventory-workbench-description">
        <header className="inventory-workbench__header">
          <div className="inventory-workbench__title">
            <span className="inventory-workbench__icon"><Boxes size={19} /></span>
            <div>
              <Dialog.Title>Inventory 工作台</Dialog.Title>
              <Dialog.Description id="inventory-workbench-description">
                {owner ? `${owner.ownerKind === "player" ? "玩家" : "联盟"} ${owner.ownerName} · #${owner.ownerIndex}` : "当前目标不可用"}
              </Dialog.Description>
            </div>
          </div>
          <div className="inventory-workbench__actions">
            <Button size="sm" variant="secondary" onClick={() => { if (mode === "catalog") void readCatalog(); else void readInventory(true); }} disabled={!owner || loading || catalogLoading}>
              <RefreshCw className={loading || catalogLoading ? "spin" : undefined} size={15} />刷新
            </Button>
            <Dialog.Close className="dialog-close" aria-label="关闭 Inventory 工作台"><X size={18} /></Dialog.Close>
          </div>
        </header>
        <div className="inventory-workbench__body">
          <div className="inventory-workbench__layout">
    <aside className="inventory-workbench__sidebar">
      <div className="inventory-workbench__modes"><button type="button" className={!isCatalog ? "is-active" : ""} onClick={() => setMode("inventory")}><Boxes size={15} />当前库存</button><button type="button" className={isCatalog ? "is-active" : ""} onClick={() => setMode("catalog")}><BookOpen size={15} />物品目录</button></div>
      <label className="inventory-workbench__search"><Search size={15} /><input value={query} onChange={(event) => setQuery(event.target.value.slice(0, 128))} placeholder="搜索物品" aria-label="搜索 Inventory" /></label>
      <label><span>类型</span><select value={itemType} onChange={(event) => setItemType(event.target.value as ItemType)}>{Object.entries(catalogTypeLabels).map(([value, label]) => <option key={value} value={value}>{label}</option>)}</select></label>
      {isCatalog && <label><span>Grant Policy</span><select value={policy} onChange={(event) => setPolicy(event.target.value as Policy)}>{Object.entries(catalogPolicyLabels).map(([value, label]) => <option key={value} value={value}>{label}</option>)}</select></label>}
      <div className="inventory-workbench__sidebar-note">能力状态只以后端返回的 grantPolicy 为准。</div>
    </aside>
    <section className="inventory-workbench__list" aria-label={isCatalog ? "物品目录列表" : "当前库存列表"}>
      <div className="inventory-workbench__list-heading"><div><h3>{isCatalog ? "物品目录" : "当前库存"}</h3><p>{isCatalog ? "已核对的原版炮塔与系统插件" : "真实槽位只读展示"}</p></div><StatusPill tone={(isCatalog ? catalog : inventory) ? "success" : "info"} compact>{isCatalog ? `${catalog?.total ?? "—"} 项` : `${inventory?.total ?? "—"} 项`}</StatusPill></div>
      {(loading || catalogLoading) && <div className="inventory-workbench__state"><RefreshCw className="spin" size={17} />正在读取…</div>}
      {(error || catalogError) && <div className="inventory-workbench__state inventory-workbench__state--error" role="alert">{error ?? catalogError}</div>}
      {!loading && !catalogLoading && visibleItems.map((item) => {
        const title = "displayName" in item ? item.displayName : item.title ?? item.name;
        const icon = "displayName" in item ? (item.iconAvailable ? item.iconPath : null) : item.icon;
        const active = selected === item;
        return <button type="button" className={`inventory-workbench__item${active ? " is-active" : ""}`} key={"displayName" in item ? item.id : item.slot} onClick={() => setSelected(item)}><GameItemIcon iconPath={icon} label={title} size={30} /><span><strong>{title}</strong><small>{"displayName" in item ? `${item.itemType === "turret" ? "炮塔" : "系统插件"} · ${catalogPolicyLabels[item.grantPolicy]}` : `${itemTypeLabels[item.itemType]} · 槽位 #${item.slot}`}</small></span><b>{"amount" in item ? `×${item.amount}` : ""}</b></button>;
      })}
      {!loading && !catalogLoading && visibleItems.length === 0 && <div className="inventory-workbench__state">没有符合条件的物品。</div>}
    </section>
    <aside className="inventory-workbench__inspector" aria-label="物品 Inspector">
      <div className="inventory-workbench__inspector-heading"><h3>物品详情</h3><button type="button" aria-label="清除选择" onClick={() => setSelected(null)} disabled={!selected}><X size={15} /></button></div>
      {selected ? <><div className="inventory-workbench__inspector-icon"><GameItemIcon iconPath={selectedIcon} label={selectedName ?? "物品"} size={56} /></div><h4>{selectedName}</h4><p className="inventory-workbench__inspector-type">{selectedType}</p>{selected && "rarity" in selected && selected.rarity !== null && <p>稀有度：{rarityLabels[selected.rarity] ?? selected.rarity}</p>}{selectedPolicy && <StatusPill tone={selectedPolicy === "verified-grantable" ? "success" : selectedPolicy === "story-blocked" ? "danger" : "info"} compact>{catalogPolicyLabels[selectedPolicy]}</StatusPill>}{selected && "displayName" in selected && <p className="inventory-workbench__inspector-meta">目录 Key：{selected.key}<br />分类：{catalogCategoryLabels[selected.category] ?? selected.category}{selected.script ? ` · ${selected.script}` : ""}</p>}<p className="inventory-workbench__description">{selectedDescription ?? "暂无额外说明。"}</p>{selectedPolicy === "story-blocked" && <div className="inventory-workbench__blocked">剧情内容禁止发放，仅可浏览。</div>}{canGrant && <div className="inventory-workbench__grant"><div className="inventory-workbench__grant-title"><PackagePlus size={15} />系统插件 Grant</div><label><span>插件 Key</span><input value={upgradeKey} readOnly /></label><label><span>稀有度</span><select value={rarity} onChange={(event) => setRarity(event.target.value as GrantRarity)}>{grantRarities.map(([key, label]) => <option key={key} value={key}>{label}</option>)}</select></label><Button variant="primary" onClick={() => setConfirmOpen(true)} disabled={submitting || !inventory}>{submitting ? <RefreshCw className="spin" size={16} /> : <ShieldCheck size={16} />}核对发放</Button>{operation && <div className="inventory-workbench__operation"><CircleCheck size={14} />{operation.status === "succeeded" ? "插件已加入库存并复核" : `操作 ${operation.status} · ${operation.progressPercent ?? 0}%`}</div>}</div>}</> : <div className="inventory-workbench__state">选择物品查看详情。</div>}
    </aside>
          </div>
        </div>
        <ConfirmDialog open={confirmOpen} onOpenChange={setConfirmOpen} title="确认发放系统插件" description={owner ? `将向${owner.ownerKind === "player" ? "玩家" : "联盟"} ${owner.ownerName}（#${owner.ownerIndex}）永久加入 1 个插件。提交后会核对唯一 Seed 的库存增量。` : "当前目标不可用。"} actionLabel="确认发放" onConfirm={() => void submitGrant()} />
      </Dialog.Content>
    </Dialog.Portal>
  </Dialog.Root>;
}
