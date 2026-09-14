import { AlertTriangle, Check, CircleDollarSign, Gift, Info, Mail, PackagePlus, RefreshCw, Search, Send, ShieldCheck, Trash2, UserPlus, Users, XCircle } from "lucide-react";
import { useCallback, useEffect, useMemo, useState } from "react";
import { Button, Card, ConfirmDialog, StatusPill } from "../components/ui";
import { apiFetch, createIdempotencyKey } from "../lib/api";
import type { ApiOperation, ServerStatus } from "../types";

type ResourceKey = "iron" | "titanium" | "naonite" | "trinium" | "xanion" | "ogonite" | "avorion";
type AssetKey = "credits" | ResourceKey;
type KnownPlayer = { index: number; name: string };
type AssetRow = { key: AssetKey; amount: number };
type RewardBatchResult = {
  batchOperationId: string;
  delivery: "mail" | "direct";
  targetTotal: number;
  succeeded: number;
  failed: number;
  outcome: string;
  completedAt: string;
};

const assets: Array<{ key: AssetKey; label: string; maximum: number }> = [
  { key: "credits", label: "Credits", maximum: 1_000_000_000 },
  { key: "iron", label: "铁", maximum: 100_000_000 },
  { key: "titanium", label: "钛", maximum: 100_000_000 },
  { key: "naonite", label: "纳奥石", maximum: 100_000_000 },
  { key: "trinium", label: "崔钢", maximum: 100_000_000 },
  { key: "xanion", label: "赛安金属", maximum: 100_000_000 },
  { key: "ogonite", label: "欧格石", maximum: 100_000_000 },
  { key: "avorion", label: "阿沃里昂", maximum: 100_000_000 },
];

const numberFormat = new Intl.NumberFormat("zh-CN", { maximumFractionDigits: 0 });

async function readApiError(response: Response) {
  const body = await response.json().catch(() => null) as { error?: { message?: string } } | null;
  return body?.error?.message ?? `请求失败（HTTP ${response.status}）`;
}
function timeLabel(value: string | null) {
  if (!value) return "等待执行";
  const timestamp = new Date(value).getTime();
  if (!Number.isFinite(timestamp)) return "时间不可用";
  const seconds = Math.max(0, Math.floor((Date.now() - timestamp) / 1000));
  if (seconds < 60) return "刚刚";
  if (seconds < 3600) return `${Math.floor(seconds / 60)} 分钟前`;
  if (seconds < 86400) return `${Math.floor(seconds / 3600)} 小时前`;
  return `${Math.floor(seconds / 86400)} 天前`;
}

export function GameManagementPage({ serverStatus, onNotify }: {
  serverStatus: ServerStatus | null;
  onNotify: (message: string) => void;
}) {
  const [delivery, setDelivery] = useState<"mail" | "direct">("mail");
  const [knownPlayers, setKnownPlayers] = useState<KnownPlayer[]>([]);
  const [selectedIndexes, setSelectedIndexes] = useState<number[]>([]);
  const [playerQuery, setPlayerQuery] = useState("");
  const [subject, setSubject] = useState("服务器活动礼包");
  const [body, setBody] = useState("感谢参与本次活动，请查收奖励。");
  const [assetRows, setAssetRows] = useState<AssetRow[]>([
    { key: "credits", amount: 25_000 },
    { key: "titanium", amount: 2_500 },
  ]);
  const [draftAsset, setDraftAsset] = useState<AssetKey>("iron");
  const [draftAmount, setDraftAmount] = useState("1000");
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [activeOperation, setActiveOperation] = useState<ApiOperation | null>(null);
  const [recent, setRecent] = useState<ApiOperation[]>([]);

  const load = useCallback(async (announce = false) => {
    setLoading(true);
    try {
      const [playersResponse, batchesResponse] = await Promise.all([
        apiFetch("/api/v1/servers/local/management-bridge/players-known?offset=0&limit=50", {
          headers: { Accept: "application/json" },
        }),
        apiFetch("/api/v1/servers/local/reward-batches?limit=8", {
          headers: { Accept: "application/json" },
        }),
      ]);
      if (!playersResponse.ok) throw new Error(await readApiError(playersResponse));
      if (!batchesResponse.ok) throw new Error(await readApiError(batchesResponse));
      const playersBody = await playersResponse.json() as { data: { items: KnownPlayer[] } };
      const batchesBody = await batchesResponse.json() as { items: ApiOperation[] };
      setKnownPlayers(playersBody.data.items.map(({ index, name }) => ({ index, name })));
      setRecent(batchesBody.items);
      setSelectedIndexes((current) => current.filter((index) =>
        playersBody.data.items.some((player) => player.index === index)));
      setError(null);
      if (announce) onNotify("已刷新玩家目标与奖励批次记录");
    } catch (caught) {
      const message = caught instanceof Error ? caught.message : "奖励中心真实数据不可用";
      setError(message);
      if (announce) onNotify(message);
    } finally {
      setLoading(false);
    }
  }, [onNotify]);

  useEffect(() => { void load(); }, [load]);

  const selectedPlayers = useMemo(() => selectedIndexes
    .map((index) => knownPlayers.find((player) => player.index === index))
    .filter((player): player is KnownPlayer => Boolean(player)), [knownPlayers, selectedIndexes]);
  const candidates = useMemo(() => {
    const query = playerQuery.trim().toLocaleLowerCase();
    if (!query) return [];
    return knownPlayers.filter((player) => !selectedIndexes.includes(player.index) &&
      (player.name.toLocaleLowerCase().includes(query) || String(player.index).includes(query))).slice(0, 8);
  }, [knownPlayers, playerQuery, selectedIndexes]);
  const assetSummary = assetRows.map((row) =>
    `${numberFormat.format(row.amount)} ${assets.find((asset) => asset.key === row.key)?.label ?? row.key}`).join(" + ");
  const canReview = selectedIndexes.length > 0 && assetRows.length > 0 &&
    (delivery === "direct" || (subject.trim().length > 0 && body.trim().length > 0));

  const addTarget = (player: KnownPlayer) => {
    if (selectedIndexes.length >= 50) {
      setError("每个批次最多选择 50 名玩家");
      return;
    }
    setSelectedIndexes((current) => [...current, player.index]);
    setPlayerQuery("");
    setError(null);
  };

  const addAsset = () => {
    const definition = assets.find((asset) => asset.key === draftAsset)!;
    const amount = Number(draftAmount);
    if (assetRows.some((row) => row.key === draftAsset)) {
      setError("该资产已经在礼包中，可先移除后重新添加");
      return;
    }
    if (!Number.isSafeInteger(amount) || amount < 1 || amount > definition.maximum) {
      setError(`数量必须是 1 到 ${numberFormat.format(definition.maximum)} 之间的整数`);
      return;
    }
    setAssetRows((current) => [...current, { key: draftAsset, amount }]);
    setError(null);
  };

  const submitBatch = async () => {
    if (!canReview) return;
    const resources: Record<ResourceKey, number> = {
      iron: 0, titanium: 0, naonite: 0, trinium: 0,
      xanion: 0, ogonite: 0, avorion: 0,
    };
    let credits = 0;
    for (const row of assetRows) {
      if (row.key === "credits") credits = row.amount;
      else resources[row.key] = row.amount;
    }

    setSubmitting(true);
    setError(null);
    setActiveOperation(null);
    try {
      const response = await apiFetch("/api/v1/servers/local/reward-batches", {
        method: "POST",
        headers: {
          Accept: "application/json",
          "Content-Type": "application/json",
          "Idempotency-Key": createIdempotencyKey("reward-batch"),
        },
        body: JSON.stringify({
          delivery,
          playerIndexes: selectedIndexes,
          grant: { credits, resources },
          mail: delivery === "mail" ? { subject: subject.trim(), body: body.trim() } : null,
          confirmation: `CREATE PLAYER BATCH ${selectedIndexes.length}`,
        }),
      });
      if (!response.ok) throw new Error(await readApiError(response));
      const accepted = await response.json() as { operationId: string };
      let completed: ApiOperation | null = null;
      for (let attempt = 0; attempt < 120; attempt += 1) {
        const operationResponse = await apiFetch(`/api/v1/operations/${encodeURIComponent(accepted.operationId)}`, {
          headers: { Accept: "application/json" },
        });
        if (!operationResponse.ok) throw new Error(await readApiError(operationResponse));
        completed = await operationResponse.json() as ApiOperation;
        setActiveOperation(completed);
        if (completed.status !== "queued" && completed.status !== "running") break;
        await new Promise((resolve) => window.setTimeout(resolve, 500));
      }
      if (!completed || completed.status === "queued" || completed.status === "running")
        throw new Error("批次仍在执行，可稍后从活动记录继续查看");
      if (completed.status !== "succeeded")
        throw new Error(completed.error?.message ?? "奖励批次执行失败");
      const result = completed.result as RewardBatchResult;
      onNotify(result.failed === 0
        ? `奖励批次完成：${result.succeeded} 名玩家已成功`
        : `奖励批次完成：成功 ${result.succeeded}，失败 ${result.failed}`);
      await load();
    } catch (caught) {
      const message = caught instanceof Error ? caught.message : "奖励批次提交失败";
      setError(message);
      onNotify(message);
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className="game-page">
      <header className="game-heading">
        <div><h1>游戏管理</h1><p>创建礼包、批量发放并追踪每个目标的真实执行结果</p></div>
        <StatusPill tone={serverStatus?.lifecycle === "running" ? "success" : "warning"} compact>
          {serverStatus?.lifecycle === "running" ? "本机 Avorion 服务器 · 运行中" : "服务器当前不可执行"}
        </StatusPill>
      </header>

      <nav className="game-tabs" aria-label="游戏管理页面">
        <button type="button" className="game-tabs__active">奖励中心</button>
        {['活动中心', '星区建设', '活动记录'].map((label) =>
          <button key={label} type="button" onClick={() => onNotify(`${label}将在对应功能完成真实验证后开放`)}>{label}</button>)}
      </nav>

      {error && <Card className="game-error" role="alert"><AlertTriangle size={18} /><span>{error}</span></Card>}

      <section className="game-summary" aria-label="奖励批次摘要">
        <Card><span><Users size={20} /></span><div><small>已选择目标</small><strong>{selectedIndexes.length}</strong></div></Card>
        <Card><span><Gift size={20} /></span><div><small>礼包内容</small><strong>{assetRows.length} 项</strong></div></Card>
        <Card><span><Send size={20} /></span><div><small>预计执行操作</small><strong>{selectedIndexes.length}</strong></div></Card>
        <Card><span><AlertTriangle size={20} /></span><div><small>风险等级</small><strong>{selectedIndexes.length ? "高" : "未计算"}</strong></div></Card>
      </section>

      <div className="reward-workspace">
        <Card className="reward-builder">
          <div className="reward-builder__heading">
            <div><h2>创建奖励批次</h2><p>一个批次固定内容，逐个目标执行并留下独立结果</p></div>
            <StatusPill tone={activeOperation?.status === "succeeded" ? "success" : "neutral"} compact>
              {activeOperation?.status === "succeeded" ? "执行完成" : "草稿未提交"}
            </StatusPill>
          </div>

          <div className="reward-mode" aria-label="发放方式">
            <button type="button" className={delivery === "mail" ? "reward-mode__active" : ""} onClick={() => setDelivery("mail")}><Mail size={16} />游戏内邮件</button>
            <button type="button" className={delivery === "direct" ? "reward-mode__active" : ""} onClick={() => setDelivery("direct")}><CircleDollarSign size={16} />直接到账</button>
            <span>目标：玩家</span>
          </div>

          <section className="reward-section">
            <div className="reward-section__title"><strong>1. 选择接收玩家</strong><small>{knownPlayers.length} 个已知玩家档案 · 仅显示身份</small></div>
            <div className="reward-target-box">
              <label className="reward-search"><Search size={15} /><input value={playerQuery} onChange={(event) => setPlayerQuery(event.target.value)} placeholder="输入玩家名称或索引，添加到批次……" /></label>
              {candidates.length > 0 && <div className="reward-candidates">{candidates.map((player) =>
                <button type="button" key={player.index} onClick={() => addTarget(player)}><UserPlus size={14} />{player.name}<small>#{player.index}</small></button>)}</div>}
              <div className="reward-targets">
                {selectedPlayers.map((player) => <span key={player.index}>{player.name} <small>#{player.index}</small><button type="button" aria-label={`移除 ${player.name}`} onClick={() => setSelectedIndexes((current) => current.filter((index) => index !== player.index))}>×</button></span>)}
                {selectedPlayers.length === 0 && <em>尚未选择目标；这里不会重复显示玩家在线状态、位置或资产详情。</em>}
              </div>
            </div>
          </section>

          {delivery === "mail" && <section className="reward-section">
            <div className="reward-section__title"><strong>2. 邮件内容</strong><small>通过 Player.addMail(Mail) 投递</small></div>
            <div className="reward-mail-fields">
              <label><span>邮件标题</span><input maxLength={80} value={subject} onChange={(event) => setSubject(event.target.value)} /></label>
              <label><span>邮件正文</span><input maxLength={500} value={body} onChange={(event) => setBody(event.target.value)} /></label>
            </div>
          </section>}

          <section className="reward-section">
            <div className="reward-section__title"><strong>{delivery === "mail" ? "3" : "2"}. 礼包资产</strong><small>每名玩家获得相同内容</small></div>
            <div className="reward-add-asset">
              <select value={draftAsset} onChange={(event) => setDraftAsset(event.target.value as AssetKey)}>{assets.map((asset) => <option value={asset.key} key={asset.key}>{asset.label}</option>)}</select>
              <input inputMode="numeric" value={draftAmount} onChange={(event) => setDraftAmount(event.target.value.replace(/\D/g, "").slice(0, 10))} aria-label="资产数量" />
              <Button size="sm" variant="secondary" onClick={addAsset}><PackagePlus size={15} />添加资产</Button>
            </div>
            <div className="reward-assets-table" role="table" aria-label="礼包资产">
              <div role="row"><span>资产类型</span><span>每位玩家数量</span><span>操作</span></div>
              {assetRows.map((row) => <div role="row" key={row.key}><strong>{assets.find((asset) => asset.key === row.key)?.label}</strong><b>{numberFormat.format(row.amount)}</b><button type="button" onClick={() => setAssetRows((current) => current.filter((item) => item.key !== row.key))}><Trash2 size={13} />移除</button></div>)}
              {assetRows.length === 0 && <p>礼包中还没有资产。</p>}
            </div>
          </section>

          {activeOperation && <div className="reward-operation" data-status={activeOperation.status}>
            {activeOperation.status === "succeeded" ? <Check size={17} /> : activeOperation.status === "failed" ? <XCircle size={17} /> : <RefreshCw className="spin" size={17} />}
            <span>{activeOperation.status === "succeeded" ? "批次已完成并记录逐人结果" : activeOperation.status === "failed" ? activeOperation.error?.message : `正在执行 ${Math.round(activeOperation.progressPercent ?? 0)}%`}</span>
            <code>{activeOperation.operationId}</code>
          </div>}

          <footer className="reward-submit-bar">
            <div><ShieldCheck size={16} /><span>将创建 {selectedIndexes.length} 个持久化逐人操作；失败目标不会自动重试。</span></div>
            <Button variant="primary" disabled={!canReview || submitting || serverStatus?.lifecycle !== "running"} onClick={() => setConfirmOpen(true)}>{submitting ? <RefreshCw className="spin" size={16} /> : <ShieldCheck size={16} />}{submitting ? "正在执行批次" : "核对并创建批次"}</Button>
          </footer>
        </Card>

        <Card className="reward-history">
          <div className="reward-history__heading"><div><h2>最近批次</h2><p>批次摘要，不复制玩家列表</p></div><Button size="sm" variant="secondary" onClick={() => void load(true)} disabled={loading}><RefreshCw className={loading ? "spin" : undefined} size={15} />刷新</Button></div>
          <div className="reward-history__list">
            {recent.map((operation) => {
              const request = operation.request as { delivery?: string; playerIndexes?: number[]; grant?: unknown } | null;
              const result = operation.result as RewardBatchResult | null;
              return <article key={operation.operationId}>
                <header><strong>{request?.delivery === "mail" ? "游戏内邮件礼包" : "直接到账批次"}</strong><time>{timeLabel(operation.completedAt ?? operation.acceptedAt)}</time></header>
                <p>{request?.delivery === "mail" ? "游戏内邮件" : "直接到账"} · {request?.playerIndexes?.length ?? result?.targetTotal ?? 0} 个目标</p>
                <div className="reward-history__track"><i style={{ width: result ? `${result.targetTotal ? result.succeeded / result.targetTotal * 100 : 0}%` : `${operation.progressPercent ?? 0}%` }} /></div>
                <footer>{result ? <><span className="reward-history__success">成功 {result.succeeded}</span><span className={result.failed ? "reward-history__failed" : ""}>失败 {result.failed}</span></> : <span>{operation.status === "failed" ? operation.error?.message : "执行中"}</span>}</footer>
              </article>;
            })}
            {!loading && recent.length === 0 && <div className="reward-history__empty"><Info size={21} /><strong>还没有奖励批次</strong><span>首次执行后会在这里显示真实结果。</span></div>}
          </div>
          <div className="reward-boundary"><strong>页面职责边界</strong><p>这里只负责创建礼包、选择最小目标身份和查看批次结果。玩家在线、位置、个人资产仍只在玩家管理展示。</p></div>
        </Card>
      </div>

      <ConfirmDialog
        open={confirmOpen}
        onOpenChange={setConfirmOpen}
        title="确认创建奖励批次"
        description={`将通过${delivery === "mail" ? "游戏内邮件" : "直接到账"}向 ${selectedIndexes.length} 名玩家发放 ${assetSummary || "空礼包"}。每名玩家独立执行并持久记录，失败不会自动重试。`}
        actionLabel="确认创建批次"
        onConfirm={() => void submitBatch()}
      />
    </div>
  );
}
