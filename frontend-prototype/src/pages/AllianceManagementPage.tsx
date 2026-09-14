import * as Dialog from "@radix-ui/react-dialog";
import {
  Boxes,
  CircleCheck,
  Crown,
  Gift,
  Info,
  MapPin,
  RefreshCw,
  Rocket,
  Shield,
  ShieldCheck,
  UserRound,
  Users,
  UsersRound,
  Warehouse,
  WalletCards,
  X,
} from "lucide-react";
import { useCallback, useEffect, useMemo, useState } from "react";
import { Button, Card, ConfirmDialog, StatusPill } from "../components/ui";
import { InventoryWorkbench, type InventoryWorkbenchOwner } from "../features/inventory/InventoryWorkbench";
import { apiFetch, createIdempotencyKey } from "../lib/api";
import type { ApiOperation } from "../types";

type AllianceSummary = {
  index: number;
  name: string;
  online: boolean;
  leaderIndex: number;
  leaderName: string;
  memberTotal: number;
  onlineMembers: number;
  homeX: number | null;
  homeY: number | null;
  numCrafts: number;
  numStations: number;
};

type AllianceMember = {
  index: number;
  name: string;
  rank: string;
  online: boolean;
  x: number | null;
  y: number | null;
};

type AllianceListResponse = {
  sampledAt: string;
  freshness: "live";
  data: {
    componentVersion: string;
    total: number;
    totalMembers: number;
    totalOnlineMembers: number;
    onlineAlliances: number;
    offset: number;
    limit: number;
    items: AllianceSummary[];
  };
};

type AllianceDetailResponse = {
  sampledAt: string;
  freshness: "live";
  data: {
    componentVersion: string;
    alliance: AllianceSummary;
    memberLimit: number;
    members: AllianceMember[];
  };
};

type AllianceAssets = {
  allianceIndex: number;
  allianceName: string;
  online: boolean;
  credits: number;
  resources: {
    iron: number;
    titanium: number;
    naonite: number;
    trinium: number;
    xanion: number;
    ogonite: number;
    avorion: number;
  };
  sampledAt: string;
  source: string;
  componentVersion: string;
};

type RewardAssetKey = "credits" | keyof AllianceAssets["resources"];

type AllianceRewardResult = {
  allianceIndex: number;
  allianceName: string;
  after: AllianceAssets;
  outcome: string;
};

const resourceLabels: Array<[keyof AllianceAssets["resources"], string]> = [
  ["iron", "铁"],
  ["titanium", "钛"],
  ["naonite", "纳奥石"],
  ["trinium", "崔钢"],
  ["xanion", "赛安金属"],
  ["ogonite", "欧格石"],
  ["avorion", "阿沃里昂"],
];

const rewardLabels: Array<[RewardAssetKey, string]> = [["credits", "Credits"], ...resourceLabels];
const amountFormatter = new Intl.NumberFormat("zh-CN", { maximumFractionDigits: 0 });

async function readApiError(response: Response) {
  const body = await response.json().catch(() => null) as { error?: { message?: string } } | null;
  return body?.error?.message ?? `联盟数据请求失败（HTTP ${response.status}）`;
}

function sectorText(x: number | null, y: number | null, fallback = "未设置") {
  return x === null || y === null ? fallback : `(${x}, ${y})`;
}

export function AllianceManagementPage({ onNotify }: { onNotify: (message: string) => void }) {
  const [list, setList] = useState<AllianceListResponse | null>(null);
  const [detail, setDetail] = useState<AllianceDetailResponse | null>(null);
  const [selectedIndex, setSelectedIndex] = useState<number | null>(null);
  const [selectedMember, setSelectedMember] = useState<AllianceMember | null>(null);
  const [loading, setLoading] = useState(true);
  const [detailLoading, setDetailLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [assetsOpen, setAssetsOpen] = useState(false);
  const [inventoryWorkbenchOwner, setInventoryWorkbenchOwner] = useState<InventoryWorkbenchOwner | null>(null);
  const [assets, setAssets] = useState<AllianceAssets | null>(null);
  const [assetsLoading, setAssetsLoading] = useState(false);
  const [assetsError, setAssetsError] = useState<string | null>(null);
  const [rewardPanelOpen, setRewardPanelOpen] = useState(false);
  const [rewardAsset, setRewardAsset] = useState<RewardAssetKey>("credits");
  const [rewardAmount, setRewardAmount] = useState("1000");
  const [rewardConfirmOpen, setRewardConfirmOpen] = useState(false);
  const [rewardSubmitting, setRewardSubmitting] = useState(false);
  const [rewardOperation, setRewardOperation] = useState<ApiOperation | null>(null);
  const [rewardError, setRewardError] = useState<string | null>(null);

  const readDetail = useCallback(async (index: number) => {
    setDetailLoading(true);
    try {
      const response = await apiFetch(`/api/v1/servers/local/management-bridge/alliance?index=${encodeURIComponent(index)}&limit=20`, {
        headers: { Accept: "application/json" },
      });
      if (!response.ok) throw new Error(await readApiError(response));
      const next = await response.json() as AllianceDetailResponse;
      setDetail(next);
      setError(null);
    } catch (caught) {
      setDetail(null);
      setError(caught instanceof Error ? caught.message : "无法读取真实联盟成员数据");
    } finally {
      setDetailLoading(false);
    }
  }, []);

  const load = useCallback(async (announce = false) => {
    setLoading(true);
    try {
      const response = await apiFetch("/api/v1/servers/local/management-bridge/alliances?offset=0&limit=10", {
        headers: { Accept: "application/json" },
      });
      if (!response.ok) throw new Error(await readApiError(response));
      const next = await response.json() as AllianceListResponse;
      setList(next);
      const target = next.data.items.some((item) => item.index === selectedIndex)
        ? selectedIndex
        : next.data.items[0]?.index ?? null;
      setSelectedIndex(target);
      if (target === null) {
        setDetail(null);
        setError(null);
      } else {
        await readDetail(target);
      }
      if (announce) onNotify(`已刷新 ${next.data.total} 个真实联盟`);
    } catch (caught) {
      const message = caught instanceof Error ? caught.message : "无法读取真实联盟数据";
      setList(null);
      setDetail(null);
      setError(message);
      if (announce) onNotify(message);
    } finally {
      setLoading(false);
    }
  }, [onNotify, readDetail, selectedIndex]);

  useEffect(() => {
    void load();
    const timer = window.setInterval(() => void load(), 4000);
    return () => window.clearInterval(timer);
  }, [load]);

  const alliances = list?.data.items ?? [];
  const selected = detail?.data.alliance ?? alliances.find((item) => item.index === selectedIndex) ?? null;
  const members = detail?.data.members ?? [];
  const componentVersion = list?.data.componentVersion ?? detail?.data.componentVersion ?? null;
  const lastSample = detail?.sampledAt ?? list?.sampledAt ?? null;
  const summaries = useMemo(() => [
    { label: "联盟总数", value: list?.data.total, icon: Shield, tone: "blue" },
    { label: "在线联盟", value: list?.data.onlineAlliances, icon: CircleCheck, tone: "green" },
    { label: "成员总数", value: list?.data.totalMembers, icon: Users, tone: "purple" },
    { label: "在线成员", value: list?.data.totalOnlineMembers, icon: UserRound, tone: "orange" },
  ], [list]);

  const chooseAlliance = (index: number) => {
    setAssetsOpen(false);
    setSelectedIndex(index);
    void readDetail(index);
  };

  const readAssets = useCallback(async (index: number) => {
    setAssetsLoading(true);
    setAssetsError(null);
    try {
      const response = await apiFetch(`/api/v1/servers/local/alliances/${index}/assets`, {
        headers: { Accept: "application/json" },
      });
      if (!response.ok) throw new Error(await readApiError(response));
      const next = await response.json() as AllianceAssets;
      if (next.allianceIndex !== index) throw new Error("联盟资产响应与当前选择不一致");
      setAssets(next);
    } catch (caught) {
      setAssets(null);
      setAssetsError(caught instanceof Error ? caught.message : "无法读取联盟资产");
    } finally {
      setAssetsLoading(false);
    }
  }, []);

  useEffect(() => {
    setAssetsOpen(false);
    setAssets(null);
    setAssetsError(null);
    setRewardPanelOpen(false);
    setRewardAsset("credits");
    setRewardAmount("1000");
    setRewardConfirmOpen(false);
    setRewardSubmitting(false);
    setRewardOperation(null);
    setRewardError(null);
  }, [selectedIndex]);

  const openAssets = () => {
    if (!selected) return;
    setAssetsOpen(true);
    void readAssets(selected.index);
  };

  const openInventoryWorkbench = () => {
    if (!selected) return;
    setInventoryWorkbenchOwner({ ownerKind: "alliance", ownerIndex: selected.index, ownerName: selected.name });
    setAssetsOpen(false);
  };

  const closeInventoryWorkbench = (open: boolean) => {
    if (!open) {
      setInventoryWorkbenchOwner(null);
      if (selected) setAssetsOpen(true);
    }
  };

  const parsedRewardAmount = Number(rewardAmount);
  const rewardMaximum = rewardAsset === "credits" ? 1_000_000_000 : 100_000_000;
  const rewardAmountValid = Number.isSafeInteger(parsedRewardAmount) &&
    parsedRewardAmount >= 1 && parsedRewardAmount <= rewardMaximum;
  const rewardLabel = rewardLabels.find(([key]) => key === rewardAsset)?.[1] ?? rewardAsset;

  const reviewReward = () => {
    if (!selected || !rewardAmountValid) {
      setRewardError(`数量必须是 1 到 ${amountFormatter.format(rewardMaximum)} 之间的整数`);
      return;
    }
    setRewardError(null);
    setRewardConfirmOpen(true);
  };

  const submitReward = async () => {
    if (!selected || !rewardAmountValid) return;
    const target = selected;
    const resources = {
      iron: 0, titanium: 0, naonite: 0, trinium: 0,
      xanion: 0, ogonite: 0, avorion: 0,
    };
    const credits = rewardAsset === "credits" ? parsedRewardAmount : 0;
    if (rewardAsset !== "credits") resources[rewardAsset] = parsedRewardAmount;

    setRewardSubmitting(true);
    setRewardError(null);
    setRewardOperation(null);
    try {
      const response = await apiFetch(`/api/v1/servers/local/alliances/${target.index}/reward-grants`, {
        method: "POST",
        headers: {
          Accept: "application/json",
          "Content-Type": "application/json",
          "Idempotency-Key": createIdempotencyKey("alliance-reward"),
        },
        body: JSON.stringify({
          grant: { credits, resources },
          confirmation: `GRANT ALLIANCE ${target.index}`,
        }),
      });
      if (!response.ok) throw new Error(await readApiError(response));
      const accepted = await response.json() as { operationId: string };
      let completed: ApiOperation | null = null;
      for (let attempt = 0; attempt < 50; attempt += 1) {
        const operationResponse = await apiFetch(`/api/v1/operations/${encodeURIComponent(accepted.operationId)}`, {
          headers: { Accept: "application/json" },
        });
        if (!operationResponse.ok) throw new Error(await readApiError(operationResponse));
        completed = await operationResponse.json() as ApiOperation;
        setRewardOperation(completed);
        if (completed.status !== "queued" && completed.status !== "running") break;
        await new Promise((resolve) => window.setTimeout(resolve, 500));
      }
      if (!completed || completed.status === "queued" || completed.status === "running")
        throw new Error("联盟奖励仍在执行，可在操作记录中继续查看");
      if (completed.status !== "succeeded")
        throw new Error(completed.error?.message ?? "联盟奖励发放失败");
      const result = completed.result as AllianceRewardResult | null;
      if (!result || result.allianceIndex !== target.index || result.outcome !== "granted-and-verified")
        throw new Error("奖励结果与当前联盟不一致，请刷新资产核对");
      setAssets(result.after);
      setRewardAmount("1000");
      onNotify(`已向联盟 ${target.name} 发放 ${amountFormatter.format(parsedRewardAmount)} ${rewardLabel}`);
    } catch (caught) {
      const message = caught instanceof Error ? caught.message : "联盟奖励发放失败";
      setRewardError(message);
      onNotify(message);
    } finally {
      setRewardSubmitting(false);
    }
  };

  return (
    <div className="alliances-page">
      <header className="alliances-heading">
        <div className="alliances-heading__title">
          <h1>联盟管理</h1>
          <StatusPill tone={list ? "success" : "warning"} compact>{list ? "实时连接" : "数据不可用"}</StatusPill>
        </div>
        <div className="alliances-heading__actions">
          <p>{componentVersion ? `OrionAdminBridge ${componentVersion} · 服务端实时` : "仅展示服务端能够确认的真实联盟"}</p>
          <Button variant="secondary" onClick={() => void load(true)} disabled={loading}>
            <RefreshCw className={loading ? "spin" : undefined} size={17} />刷新
          </Button>
        </div>
      </header>

      {error && <Card className="alliances-error" role="alert"><Info size={20} /><div><strong>真实联盟数据不可用</strong><span>{error}</span></div></Card>}

      <div className="alliances-summary" aria-label="联盟状态摘要">
        {summaries.map(({ label, value, icon: Icon, tone }) => (
          <Card className="alliances-summary__card" key={label}>
            <span className={`alliances-summary__icon alliances-summary__icon--${tone}`}><Icon size={24} /></span>
            <div><span>{label}</span><strong>{value ?? "—"}</strong></div>
          </Card>
        ))}
      </div>

      <div className="alliances-workspace">
        <Card className="alliances-list" aria-label="联盟列表">
          <div className="alliances-panel-heading"><div><h2>联盟列表</h2><p>从玩家所属关系自动发现</p></div><span>{list ? list.data.total : "—"}</span></div>
          <div className="alliances-list__rows">
            {alliances.map((alliance) => (
              <button
                type="button"
                key={alliance.index}
                className={alliance.index === selectedIndex ? "alliances-list__row alliances-list__row--active" : "alliances-list__row"}
                aria-pressed={alliance.index === selectedIndex}
                onClick={() => chooseAlliance(alliance.index)}
              >
                <span className="alliance-mark"><Shield size={20} /></span>
                <span className="alliances-list__copy"><strong>{alliance.name}</strong><small>#{alliance.index} · {alliance.memberTotal} 名成员</small></span>
                <span className={alliance.online ? "alliance-state alliance-state--online" : "alliance-state"}><i />{alliance.online ? "在线" : "离线"}</span>
              </button>
            ))}
            {!loading && list && alliances.length === 0 && (
              <div className="alliances-list__empty"><UsersRound size={28} /><strong>尚未发现联盟</strong><span>玩家加入或创建联盟后会自动出现。</span></div>
            )}
            {loading && !list && <div className="alliances-list__empty"><RefreshCw className="spin" size={24} /><strong>正在读取联盟</strong></div>}
          </div>
          <div className="alliances-source"><CircleCheck size={17} /><span>{componentVersion ? `Bridge ${componentVersion} · 只读实时数据` : "未使用模拟联盟数据"}</span></div>
        </Card>

        <Card className="alliance-detail" aria-label="联盟成员详情">
          {selected ? (
            <>
              <div className="alliances-panel-heading alliance-detail__heading">
                <div><h2>{selected.name} · 成员</h2><p>联盟索引 #{selected.index}</p></div>
                <div className="alliance-detail__actions">
                  {detailLoading && <RefreshCw className="spin alliance-detail__loading" size={18} />}
                  <Button size="sm" variant="secondary" onClick={openAssets}><WalletCards size={16} />资产与发放</Button>
                </div>
              </div>
              <div className="alliance-identity">
                <span className="alliance-identity__mark"><Shield size={29} /></span>
                <dl>
                  <div><dt><Crown size={15} />联盟领袖</dt><dd>{selected.leaderName} <small>#{selected.leaderIndex}</small></dd></div>
                  <div><dt><MapPin size={15} />联盟主星区</dt><dd>{sectorText(selected.homeX, selected.homeY)}</dd></div>
                  <div><dt><Rocket size={15} />舰船</dt><dd>{selected.numCrafts}</dd></div>
                  <div><dt><Warehouse size={15} />空间站</dt><dd>{selected.numStations}</dd></div>
                </dl>
              </div>
              <div className="alliance-members-wrap">
                <table className="alliance-members">
                  <thead><tr><th>成员</th><th>联盟职级</th><th>状态</th><th>所在星区</th><th>玩家索引</th><th>操作</th></tr></thead>
                  <tbody>
                    {members.map((member) => (
                      <tr key={member.index}>
                        <td><span className="alliance-member-avatar"><UserRound size={17} /></span><strong>{member.name}</strong></td>
                        <td>{member.rank}</td>
                        <td><span className={member.online ? "alliance-state alliance-state--online" : "alliance-state"}><i />{member.online ? "在线" : "离线"}</span></td>
                        <td>{sectorText(member.x, member.y, member.online ? "正在进入星区" : "离线位置不可用")}</td>
                        <td>#{member.index}</td>
                        <td><button className="text-link" type="button" onClick={() => setSelectedMember(member)}>查看玩家</button></td>
                      </tr>
                    ))}
                    {!detailLoading && members.length === 0 && <tr className="alliance-members__empty"><td colSpan={6}><Users size={24} /><strong>联盟没有可显示成员</strong></td></tr>}
                  </tbody>
                </table>
              </div>
              <div className="alliance-detail__foot"><Boxes size={16} /><span>成员、位置、舰船与空间站数量均来自 Avorion 服务端 API。</span><small>{lastSample ? new Date(lastSample).toLocaleTimeString("zh-CN", { hour12: false }) : "—"}</small></div>
            </>
          ) : (
            <div className="alliance-detail__empty"><Shield size={38} /><strong>暂无可查看的联盟</strong><span>此页面不会创建示例联盟；游戏内存在玩家联盟后将自动显示成员详情。</span></div>
          )}
        </Card>
      </div>

      <Dialog.Root open={selectedMember !== null} onOpenChange={(open) => { if (!open) setSelectedMember(null); }}>
        <Dialog.Portal>
          <Dialog.Overlay className="dialog-overlay" />
          <Dialog.Content className="player-detail-dialog" aria-describedby="alliance-member-description">
            <Dialog.Title>联盟成员详情</Dialog.Title>
            <Dialog.Description id="alliance-member-description">仅展示 OrionAdminBridge 当前返回的只读字段。</Dialog.Description>
            <Dialog.Close className="dialog-close" aria-label="关闭成员详情"><X size={18} /></Dialog.Close>
            {selectedMember && <dl><div><dt>玩家名称</dt><dd>{selectedMember.name}</dd></div><div><dt>联盟职级</dt><dd>{selectedMember.rank}</dd></div><div><dt>在线状态</dt><dd>{selectedMember.online ? "在线" : "离线"}</dd></div><div><dt>所在星区</dt><dd>{sectorText(selectedMember.x, selectedMember.y, "位置不可用")}</dd></div><div><dt>玩家索引</dt><dd>#{selectedMember.index}</dd></div></dl>}
            <Dialog.Close asChild><Button variant="primary">关闭详情</Button></Dialog.Close>
          </Dialog.Content>
        </Dialog.Portal>
      </Dialog.Root>

      <Dialog.Root open={assetsOpen} onOpenChange={(open) => { setAssetsOpen(open); if (!open) setRewardPanelOpen(false); }}>
        <Dialog.Portal>
          <Dialog.Overlay className="dialog-overlay" />
          <Dialog.Content className="player-detail-dialog player-assets-dialog alliance-assets-dialog" aria-describedby="alliance-assets-description">
            <Dialog.Title>{selected ? `${selected.name} · 联盟资产` : "联盟资产"}</Dialog.Title>
            <Dialog.Description id="alliance-assets-description">Credits 与七种矿物来自 Avorion 联盟 Faction；发放操作会持久记录并复核变更结果。</Dialog.Description>
            <Dialog.Close className="dialog-close" aria-label="关闭联盟资产"><X size={18} /></Dialog.Close>
            <section className="player-assets" aria-label="联盟资产">
              <div className="player-assets__heading">
                <h3>联盟资产</h3>
                <div>
                  <StatusPill tone={assets ? "success" : assetsError ? "danger" : "info"} compact>{assets ? "实时" : assetsError ? "不可用" : "读取中"}</StatusPill>
                  <Button size="sm" variant="secondary" onClick={() => { setRewardPanelOpen((open) => !open); setRewardError(null); }} disabled={!assets || rewardSubmitting}>
                    <Gift size={15} />发放奖励
                  </Button>
                </div>
              </div>
              {assetsLoading && <div className="player-assets__state"><RefreshCw className="spin" size={18} />正在读取联盟 Credits 与资源…</div>}
              {assetsError && <div className="player-assets__state player-assets__state--error"><Info size={18} />{assetsError}</div>}
              {assets && <>
                <div className="player-assets__credits"><span>Credits</span><strong>{amountFormatter.format(assets.credits)}</strong></div>
                <div className="player-assets__resources">
                  {resourceLabels.map(([key, label]) => <div key={key}><span>{label}</span><strong>{amountFormatter.format(assets.resources[key])}</strong></div>)}
                </div>
                <p>OrionAdminBridge {assets.componentVersion} · 服务端实时读取</p>
              </>}
              {rewardPanelOpen && selected && assets && <div className="player-reward">
                <div className="player-reward__title"><span><Gift size={16} />联盟奖励</span><small>每次只发一种资产，便于审计和核对</small></div>
                <div className="player-reward__fields">
                  <label><span>资产类型</span><select value={rewardAsset} onChange={(event) => { setRewardAsset(event.target.value as RewardAssetKey); setRewardError(null); }}>{rewardLabels.map(([key, label]) => <option key={key} value={key}>{label}</option>)}</select></label>
                  <label><span>发放数量</span><input value={rewardAmount} onChange={(event) => { setRewardAmount(event.target.value.replace(/\D/g, "").slice(0, 10)); setRewardError(null); }} inputMode="numeric" aria-label="联盟奖励数量" /></label>
                  <Button variant="primary" onClick={reviewReward} disabled={rewardSubmitting || !rewardAmountValid}>{rewardSubmitting ? <RefreshCw className="spin" size={16} /> : <ShieldCheck size={16} />}{rewardSubmitting ? "正在复核" : "核对发放"}</Button>
                </div>
                <p>单次上限：{rewardAsset === "credits" ? "10 亿 Credits" : "1 亿单位矿物"}；不允许负数或自动重试。</p>
                {rewardOperation && <div className="player-reward__operation"><CircleCheck size={15} /><span>{rewardOperation.status === "succeeded" ? "联盟奖励已发放并复核" : `操作 ${rewardOperation.status} · ${rewardOperation.progressPercent ?? 0}%`}</span><code>{rewardOperation.operationId}</code></div>}
                {rewardError && <div className="player-reward__error" role="alert"><Info size={16} />{rewardError}</div>}
              </div>}
            </section>
            {selected && <Button variant="secondary" onClick={openInventoryWorkbench}><Boxes size={16} />打开 Inventory 工作台</Button>}
            <Dialog.Close asChild><Button variant="primary">关闭资产</Button></Dialog.Close>
          </Dialog.Content>
        </Dialog.Portal>
      </Dialog.Root>

      <InventoryWorkbench
        open={inventoryWorkbenchOwner !== null}
        owner={inventoryWorkbenchOwner}
        onOpenChange={closeInventoryWorkbench}
        onNotify={onNotify}
      />

      <ConfirmDialog
        open={rewardConfirmOpen}
        onOpenChange={setRewardConfirmOpen}
        title="确认发放联盟奖励"
        description={selected ? `将向联盟 ${selected.name}（#${selected.index}）永久增加 ${amountFormatter.format(rewardAmountValid ? parsedRewardAmount : 0)} ${rewardLabel}。提交后系统会记录操作并核对变更前后余额。` : "联盟已不存在，请取消操作。"}
        actionLabel="确认发放"
        onConfirm={() => void submitReward()}
      />
    </div>
  );
}
