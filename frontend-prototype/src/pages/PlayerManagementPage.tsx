import * as Dialog from "@radix-ui/react-dialog";
import { Boxes, CircleCheck, Gift, Info, LocateFixed, RefreshCw, Server, ShieldCheck, UserRound, UserRoundPlus, Users, X } from "lucide-react";
import { useCallback, useEffect, useMemo, useState } from "react";
import { Button, Card, ConfirmDialog, StatusPill } from "../components/ui";
import { InventoryWorkbench, type InventoryWorkbenchOwner } from "../features/inventory/InventoryWorkbench";
import { apiFetch, createIdempotencyKey } from "../lib/api";
import type { ApiOperation, ServerStatus } from "../types";

type BridgePlayer = {
  index: number;
  name: string;
  online: true;
  x: number | null;
  y: number | null;
};

type PlayerBridgeResponse = {
  sampledAt: string;
  freshness: string;
  data: {
    componentVersion: string;
    total: number;
    offset: number;
    limit: number;
    items: BridgePlayer[];
  };
};

type PlayerEvent = {
  eventId: string;
  occurredAt: string;
  message: string;
};

type PlayerAssets = {
  playerIndex: number;
  playerName: string;
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

type RewardAssetKey = "credits" | keyof PlayerAssets["resources"];

type PlayerRewardResult = {
  playerIndex: number;
  playerName: string;
  after: PlayerAssets;
  outcome: string;
};

const resourceLabels: Array<[keyof PlayerAssets["resources"], string]> = [
  ["iron", "铁"],
  ["titanium", "钛"],
  ["naonite", "纳奥石"],
  ["trinium", "崔钢"],
  ["xanion", "赛安金属"],
  ["ogonite", "欧格石"],
  ["avorion", "阿沃里昂"],
];

const rewardLabels: Array<[RewardAssetKey, string]> = [
  ["credits", "Credits"],
  ...resourceLabels,
];

const amountFormatter = new Intl.NumberFormat("zh-CN", { maximumFractionDigits: 0 });

async function readApiError(response: Response) {
  const body = await response.json().catch(() => null) as { error?: { message?: string } } | null;
  return body?.error?.message ?? `玩家数据请求失败（HTTP ${response.status}）`;
}

function formatRelativeTime(value: string) {
  const time = new Date(value).getTime();
  if (!Number.isFinite(time)) return "时间不可用";
  const seconds = Math.max(0, Math.floor((Date.now() - time) / 1000));
  if (seconds < 60) return "刚刚";
  if (seconds < 3600) return `${Math.floor(seconds / 60)} 分钟前`;
  if (seconds < 86400) return `${Math.floor(seconds / 3600)} 小时前`;
  return `${Math.floor(seconds / 86400)} 天前`;
}

function sectorText(player: BridgePlayer) {
  return player.x === null || player.y === null ? "正在进入星区" : `(${player.x}, ${player.y})`;
}

function presentPlayerEvent(event: PlayerEvent & { kind: string }): PlayerEvent | null {
  const content = event.message.replace(/^\d{4}-\d{2}-\d{2} \d{2}[-:]\d{2}[-:]\d{2}\|\s*/, "");
  let match = content.match(/^Player logged in:\s*(.+), index:\s*\d+/i);
  if (match) return { ...event, message: `${match[1]} 登录服务器` };
  match = content.match(/^<Server> Player (.+) joined the galaxy$/i);
  if (match) return { ...event, message: `${match[1]} 加入服务器` };
  match = content.match(/^Player (.+) moved to sector \((-?\d+):(-?\d+)\)/i);
  if (match) return { ...event, message: `${match[1]} 进入星区 (${match[2]}, ${match[3]})` };
  match = content.match(/^Player logged out:\s*(.+?)(?:,|$)/i) ?? content.match(/^<Server> Player (.+) left the galaxy$/i);
  if (match) return { ...event, message: `${match[1]} 离开服务器` };
  return null;
}

export function PlayerManagementPage({ serverStatus, onNotify }: {
  serverStatus: ServerStatus | null;
  onNotify: (message: string) => void;
}) {
  const [bridge, setBridge] = useState<PlayerBridgeResponse | null>(null);
  const [events, setEvents] = useState<PlayerEvent[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [selected, setSelected] = useState<BridgePlayer | null>(null);
  const [inventoryWorkbenchOwner, setInventoryWorkbenchOwner] = useState<InventoryWorkbenchOwner | null>(null);
  const [assets, setAssets] = useState<PlayerAssets | null>(null);
  const [assetsLoading, setAssetsLoading] = useState(false);
  const [assetsError, setAssetsError] = useState<string | null>(null);
  const [rewardPanelOpen, setRewardPanelOpen] = useState(false);
  const [rewardAsset, setRewardAsset] = useState<RewardAssetKey>("credits");
  const [rewardAmount, setRewardAmount] = useState("1000");
  const [rewardConfirmOpen, setRewardConfirmOpen] = useState(false);
  const [rewardSubmitting, setRewardSubmitting] = useState(false);
  const [rewardOperation, setRewardOperation] = useState<ApiOperation | null>(null);
  const [rewardError, setRewardError] = useState<string | null>(null);

  const load = useCallback(async (announce = false) => {
    setLoading(true);
    try {
      const [playersResponse, eventsResponse] = await Promise.all([
        apiFetch("/api/v1/servers/local/management-bridge/players?offset=0&limit=10", { headers: { Accept: "application/json" } }),
        apiFetch("/api/v1/servers/local/events?category=Player&limit=6", { headers: { Accept: "application/json" } }),
      ]);
      if (!playersResponse.ok) throw new Error(await readApiError(playersResponse));
      const nextBridge = await playersResponse.json() as PlayerBridgeResponse;
      setBridge(nextBridge);
      if (eventsResponse.ok) {
        const body = await eventsResponse.json() as { items: Array<PlayerEvent & { kind: string }> };
        setEvents(body.items
          .filter((item) => item.kind === "player")
          .map(presentPlayerEvent)
          .filter((item): item is PlayerEvent => item !== null)
          .slice(0, 6));
      } else setEvents([]);
      setSelected((current) => current ? nextBridge.data.items.find((item) => item.index === current.index) ?? null : null);
      setError(null);
      if (announce) onNotify(`已刷新 ${nextBridge.data.total} 名真实在线玩家`);
    } catch (caught) {
      const message = caught instanceof Error ? caught.message : "无法读取真实玩家数据";
      setBridge(null);
      setError(message);
      if (announce) onNotify(message);
    } finally {
      setLoading(false);
    }
  }, [onNotify]);

  useEffect(() => {
    void load();
    const timer = window.setInterval(() => void load(), 3000);
    return () => window.clearInterval(timer);
  }, [load]);

  useEffect(() => {
    if (!selected) {
      setAssets(null);
      setAssetsError(null);
      return;
    }
    const controller = new AbortController();
    setAssetsLoading(true);
    setAssets(null);
    setAssetsError(null);
    void apiFetch(`/api/v1/servers/local/players/${selected.index}/assets`, {
      headers: { Accept: "application/json" },
      signal: controller.signal,
    }).then(async (response) => {
      if (!response.ok) throw new Error(await readApiError(response));
      const body = await response.json() as PlayerAssets;
      if (body.playerIndex !== selected.index) throw new Error("玩家资产响应与当前选择不一致");
      setAssets(body);
    }).catch((caught) => {
      if (caught instanceof DOMException && caught.name === "AbortError") return;
      setAssetsError(caught instanceof Error ? caught.message : "无法读取玩家资产");
    }).finally(() => {
      if (!controller.signal.aborted) setAssetsLoading(false);
    });
    return () => controller.abort();
  }, [selected?.index]);

  useEffect(() => {
    setRewardPanelOpen(false);
    setRewardAsset("credits");
    setRewardAmount("1000");
    setRewardConfirmOpen(false);
    setRewardSubmitting(false);
    setRewardOperation(null);
    setRewardError(null);
  }, [selected?.index]);

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
      const response = await apiFetch(`/api/v1/servers/local/players/${target.index}/reward-grants`, {
        method: "POST",
        headers: {
          Accept: "application/json",
          "Content-Type": "application/json",
          "Idempotency-Key": createIdempotencyKey("player-reward"),
        },
        body: JSON.stringify({
          grant: { credits, resources },
          confirmation: `GRANT PLAYER ${target.index}`,
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
        throw new Error("奖励操作仍在执行，可在操作记录中继续查看");
      if (completed.status !== "succeeded")
        throw new Error(completed.error?.message ?? "玩家奖励发放失败");
      const result = completed.result as PlayerRewardResult | null;
      if (!result || result.playerIndex !== target.index || result.outcome !== "granted-and-verified")
        throw new Error("奖励结果与当前玩家不一致，请刷新资产核对");
      setAssets(result.after);
      setRewardAmount("1000");
      onNotify(`已向 ${target.name} 发放 ${amountFormatter.format(parsedRewardAmount)} ${rewardLabel}`);
    } catch (caught) {
      const message = caught instanceof Error ? caught.message : "玩家奖励发放失败";
      setRewardError(message);
      onNotify(message);
    } finally {
      setRewardSubmitting(false);
    }
  };

  const players = bridge?.data.items ?? [];
  const playerSectors = useMemo(() => new Set((bridge?.data.items ?? [])
    .filter((player) => player.x !== null && player.y !== null)
    .map((player) => `${player.x}:${player.y}`)).size, [bridge]);
  const online = bridge?.data.total ?? serverStatus?.onlinePlayers;
  const componentVersion = bridge?.data.componentVersion ?? null;

  const openInventoryWorkbench = () => {
    if (!selected) return;
    setInventoryWorkbenchOwner({ ownerKind: "player", ownerIndex: selected.index, ownerName: selected.name });
    setSelected(null);
  };

  const closeInventoryWorkbench = (open: boolean) => {
    if (open) return;
    const owner = inventoryWorkbenchOwner;
    if (owner) {
      const restored = bridge?.data.items.find((player) => player.index === owner.ownerIndex);
      if (restored) setSelected(restored);
    }
    setInventoryWorkbenchOwner(null);
  };

  return (
    <div className="players-page">
      <header className="players-heading">
        <div className="players-heading__title"><h1>玩家管理</h1><StatusPill tone={bridge ? "success" : "warning"} compact>{bridge ? "实时连接" : "数据不可用"}</StatusPill></div>
        <div className="players-heading__actions">
          <p>{componentVersion ? `OrionAdminBridge ${componentVersion} · 服务端实时` : "仅展示服务端能够确认的真实数据"}</p>
          <Button variant="secondary" onClick={() => void load(true)} disabled={loading}><RefreshCw className={loading ? "spin" : undefined} size={17} />刷新</Button>
        </div>
      </header>

      {error && <Card className="players-error" role="alert"><Info size={20} /><div><strong>真实玩家数据不可用</strong><span>{error}</span></div></Card>}

      <Card className="players-summary" aria-label="玩家状态摘要">
        <div className="players-summary__item"><span className="players-summary__icon players-summary__icon--blue"><Users size={25} /></span><div><span>当前在线</span><strong>{online === null || online === undefined ? "—" : `${online} / ${serverStatus?.maxPlayers ?? "?"}`}</strong></div></div>
        <div className="players-summary__item"><span className="players-summary__icon players-summary__icon--green"><LocateFixed size={25} /></span><div><span>玩家所在星区</span><strong>{bridge ? playerSectors : "—"}</strong></div></div>
        <div className="players-summary__item"><span className="players-summary__icon players-summary__icon--purple"><Server size={25} /></span><div><span>管理组件</span><strong>{bridge ? "已连接" : "未连接"}</strong></div></div>
      </Card>

      <div className="players-workspace">
        <Card className="players-roster">
          <div className="players-panel-heading"><div><h2>当前在线玩家</h2><p>玩家退出后会从列表移除，不保留离线档案</p></div><span>{bridge ? `${bridge.data.total} / ${serverStatus?.maxPlayers ?? "?"}` : "—"}</span></div>
          <div className="players-table-wrap">
            <table className="players-table">
              <thead><tr><th>玩家名称</th><th>状态</th><th>所在星区</th><th>玩家索引</th><th>更新时间</th><th>操作</th></tr></thead>
              <tbody>
                {players.map((player) => <tr key={player.index}><td><span className="players-avatar"><UserRound size={18} /></span><strong>{player.name}</strong></td><td><span className="players-online"><span />在线</span></td><td>{sectorText(player)}</td><td>#{player.index}</td><td>{bridge ? formatRelativeTime(bridge.sampledAt) : "—"}</td><td><button className="text-link" type="button" onClick={() => setSelected(player)}>查看</button></td></tr>)}
                {!loading && bridge && players.length === 0 && <tr className="players-table__empty"><td colSpan={6}><Users size={23} /><strong>当前没有在线玩家</strong><span>玩家进入服务器后会自动出现在这里。</span></td></tr>}
                {loading && !bridge && <tr className="players-table__empty"><td colSpan={6}><RefreshCw className="spin" size={22} /><strong>正在读取服务端玩家</strong></td></tr>}
              </tbody>
            </table>
          </div>
          <div className="players-roster__note"><Info size={17} /><span>仅显示服务端当前确认在线的玩家；不展示推测字段。</span><small>{bridge ? `最后刷新 ${formatRelativeTime(bridge.sampledAt)}` : "等待管理组件"}</small></div>
        </Card>

        <Card className="players-events">
          <div className="players-panel-heading"><div><h2>最近玩家事件</h2><p>来自真实服务器日志</p></div></div>
          {events.length > 0 ? <ol>{events.map((event) => <li key={event.eventId}><span><UserRoundPlus size={17} /></span><div><strong>{event.message}</strong><time>{formatRelativeTime(event.occurredAt)}</time></div></li>)}</ol>
            : <div className="players-events__empty"><Info size={21} /><strong>暂无玩家事件</strong><span>服务器日志出现加入或离开记录后会显示在这里。</span></div>}
          <div className="players-events__source"><CircleCheck size={17} /><span>{componentVersion ? `OrionAdminBridge ${componentVersion} · 服务端实时` : "未使用模拟玩家数据"}</span></div>
        </Card>
      </div>

      <Dialog.Root open={selected !== null} onOpenChange={(open) => { if (!open) setSelected(null); }}>
        <Dialog.Portal>
          <Dialog.Overlay className="dialog-overlay" />
          <Dialog.Content className="player-detail-dialog player-assets-dialog" aria-describedby="player-detail-description">
            <Dialog.Title>在线玩家详情</Dialog.Title>
            <Dialog.Description id="player-detail-description">身份、位置与资产来自 OrionAdminBridge；奖励操作会持久记录并复核变更结果。</Dialog.Description>
            <Dialog.Close className="dialog-close" aria-label="关闭玩家详情"><X size={18} /></Dialog.Close>
            {selected && <dl><div><dt>玩家名称</dt><dd>{selected.name}</dd></div><div><dt>在线状态</dt><dd><span className="players-online"><span />在线</span></dd></div><div><dt>所在星区</dt><dd>{sectorText(selected)}</dd></div><div><dt>玩家索引</dt><dd>#{selected.index}</dd></div><div><dt>数据来源</dt><dd>服务端实时</dd></div></dl>}
            <section className="player-assets" aria-label="玩家资产">
              <div className="player-assets__heading">
                <h3>玩家资产</h3>
                <div>
                  <StatusPill tone={assets ? "success" : assetsError ? "danger" : "info"} compact>{assets ? "实时" : assetsError ? "不可用" : "读取中"}</StatusPill>
                  <Button size="sm" variant="secondary" onClick={() => { setRewardPanelOpen((open) => !open); setRewardError(null); }} disabled={!assets || rewardSubmitting}>
                    <Gift size={15} />发放奖励
                  </Button>
                </div>
              </div>
              {assetsLoading && <div className="player-assets__state"><RefreshCw className="spin" size={18} />正在读取 Credits 与资源…</div>}
              {assetsError && <div className="player-assets__state player-assets__state--error"><Info size={18} />{assetsError}</div>}
              {assets && <>
                <div className="player-assets__credits"><span>Credits</span><strong>{amountFormatter.format(assets.credits)}</strong></div>
                <div className="player-assets__resources">
                  {resourceLabels.map(([key, label]) => <div key={key}><span>{label}</span><strong>{amountFormatter.format(assets.resources[key])}</strong></div>)}
                </div>
                <p>OrionAdminBridge {assets.componentVersion} · {formatRelativeTime(assets.sampledAt)}</p>
              </>}
              {rewardPanelOpen && selected && assets && <div className="player-reward">
                <div className="player-reward__title"><span><Gift size={16} />单人奖励</span><small>每次只发一种资产，便于审计和核对</small></div>
                <div className="player-reward__fields">
                  <label><span>资产类型</span><select value={rewardAsset} onChange={(event) => { setRewardAsset(event.target.value as RewardAssetKey); setRewardError(null); }}>{rewardLabels.map(([key, label]) => <option key={key} value={key}>{label}</option>)}</select></label>
                  <label><span>发放数量</span><input value={rewardAmount} onChange={(event) => { setRewardAmount(event.target.value.replace(/\D/g, "").slice(0, 10)); setRewardError(null); }} inputMode="numeric" aria-label="发放数量" /></label>
                  <Button variant="primary" onClick={reviewReward} disabled={rewardSubmitting || !rewardAmountValid}>{rewardSubmitting ? <RefreshCw className="spin" size={16} /> : <ShieldCheck size={16} />}{rewardSubmitting ? "正在复核" : "核对发放"}</Button>
                </div>
                <p>单次上限：{rewardAsset === "credits" ? "10 亿 Credits" : "1 亿单位矿物"}；不允许负数或自动重试。</p>
                {rewardOperation && <div className="player-reward__operation"><CircleCheck size={15} /><span>{rewardOperation.status === "succeeded" ? "奖励已发放并复核" : `操作 ${rewardOperation.status} · ${rewardOperation.progressPercent ?? 0}%`}</span><code>{rewardOperation.operationId}</code></div>}
                {rewardError && <div className="player-reward__error" role="alert"><Info size={16} />{rewardError}</div>}
              </div>}
            </section>
            {selected && <Button variant="secondary" onClick={openInventoryWorkbench}><Boxes size={16} />打开 Inventory 工作台</Button>}
            <Dialog.Close asChild><Button variant="primary">关闭详情</Button></Dialog.Close>
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
        title="确认发放玩家奖励"
        description={selected ? `将向玩家 ${selected.name}（#${selected.index}）永久增加 ${amountFormatter.format(rewardAmountValid ? parsedRewardAmount : 0)} ${rewardLabel}。提交后系统会记录操作并核对变更前后余额。` : "玩家已不在线，请取消操作。"}
        actionLabel="确认发放"
        onConfirm={() => void submitReward()}
      />
    </div>
  );
}
