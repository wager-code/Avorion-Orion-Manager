import { Activity, BellRing, Check, Info, Layers3, LoaderCircle, MemoryStick, RefreshCw, TrendingUp } from "lucide-react";
import { useEffect, useMemo, useRef, useState } from "react";
import { CartesianGrid, Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from "recharts";
import { Button, Card, StatusPill } from "../components/ui";
import { apiFetch, createIdempotencyKey } from "../lib/api";
import { formatDecimal } from "../lib/utils";
import type { PerformanceHistory, PerformanceSnapshot } from "../types";

const bytesPerGiB = 1024 ** 3;

function toGiB(value: number | null) {
  return value === null ? null : value / bytesPerGiB;
}

function valueText(value: number | null, suffix = " GB") {
  return value === null ? "—" : `${formatDecimal(value, 2)}${suffix}`;
}

function timeLabel(value: string) {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? "—" : new Intl.DateTimeFormat("zh-CN", {
    hour: "2-digit", minute: "2-digit", hour12: false,
  }).format(date);
}

function MemoryTooltip({ active, payload, label }: {
  active?: boolean;
  payload?: Array<{ value?: number }>;
  label?: string;
}) {
  if (!active || !payload?.length) return null;
  return <div className="memory-chart-tooltip"><span>{label}</span><strong>{valueText(payload[0].value ?? null)}</strong></div>;
}

type MemoryPolicy = {
  warningThresholdBytes: number;
  notify: boolean;
  tryUnloadIdleSectors: boolean;
  safeRestartAtCritical: boolean;
  updatedAt: string;
};

type MemoryOverview = {
  currentWorkingSetBytes: number | null;
  startupBaselineBytes: number | null;
  growthBytes: number | null;
  growthBytesPerHour: number | null;
  warningThresholdBytes: number | null;
  alertLevel: "normal" | "warning" | "unknown";
  loadedSectorCount: number | null;
  playerSectorCount: number | null;
  idleSectorCount: number | null;
};

type SectorPage = {
  total: number;
  offset: number;
  limit: number;
  items: Array<{ x: number; y: number; playerCount: number; eligible: boolean }>;
};

type SectorUnloadOperation = {
  operationId: string;
  status: "queued" | "running" | "succeeded" | "failed" | "cancelled";
  progressPercent: number | null;
  currentStep: string | null;
  result: { x: number; y: number; accepted: boolean; unloaded: boolean; outcome: string } | null;
  error: { message?: string; code?: string } | null;
};

async function readApiError(response: Response) {
  const body = await response.json().catch(() => null) as { error?: { message?: string } } | null;
  return body?.error?.message ?? `服务器请求失败（HTTP ${response.status}）`;
}

export function ServerMemoryPage({ onNotify }: { onNotify: (message: string) => void }) {
  const [snapshot, setSnapshot] = useState<PerformanceSnapshot | null>(null);
  const [history, setHistory] = useState<PerformanceHistory | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [refreshVersion, setRefreshVersion] = useState(0);
  const [policy, setPolicy] = useState<MemoryPolicy | null>(null);
  const [thresholdGiB, setThresholdGiB] = useState("14");
  const [notifyEnabled, setNotifyEnabled] = useState(true);
  const [policySaving, setPolicySaving] = useState(false);
  const [policyError, setPolicyError] = useState<string | null>(null);
  const [overview, setOverview] = useState<MemoryOverview | null>(null);
  const [sectors, setSectors] = useState<SectorPage | null>(null);
  const [sectorError, setSectorError] = useState<string | null>(null);
  const [sectorOperationId, setSectorOperationId] = useState<string | null>(null);
  const [sectorOperation, setSectorOperation] = useState<SectorUnloadOperation | null>(null);
  const [submittingSector, setSubmittingSector] = useState<string | null>(null);
  const notifiedSectorOperation = useRef<string | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    const load = async () => {
      setLoading(true);
      try {
        const [snapshotResponse, historyResponse, policyResponse, overviewResponse, sectorsResponse] = await Promise.all([
          apiFetch("/api/v1/servers/local/performance/current", { headers: { Accept: "application/json" }, signal: controller.signal }),
          apiFetch("/api/v1/servers/local/performance/history?range=24h", { headers: { Accept: "application/json" }, signal: controller.signal }),
          apiFetch("/api/v1/servers/local/memory-policy", { headers: { Accept: "application/json" }, signal: controller.signal }),
          apiFetch("/api/v1/servers/local/memory", { headers: { Accept: "application/json" }, signal: controller.signal }),
          apiFetch("/api/v1/servers/local/sectors?offset=0&limit=50", { headers: { Accept: "application/json" }, signal: controller.signal }),
        ]);
        if (!snapshotResponse.ok || !historyResponse.ok || !policyResponse.ok || !overviewResponse.ok) throw new Error("无法读取真实内存采样或策略");
        setSnapshot(await snapshotResponse.json() as PerformanceSnapshot);
        setHistory(await historyResponse.json() as PerformanceHistory);
        const loadedPolicy = await policyResponse.json() as MemoryPolicy;
        setPolicy(loadedPolicy);
        setThresholdGiB(String(Number((loadedPolicy.warningThresholdBytes / bytesPerGiB).toFixed(2))));
        setNotifyEnabled(loadedPolicy.notify);
        setOverview(await overviewResponse.json() as MemoryOverview);
        if (sectorsResponse.ok) {
          setSectors(await sectorsResponse.json() as SectorPage);
          setSectorError(null);
        } else {
          setSectors(null);
          setSectorError(await readApiError(sectorsResponse));
        }
        setPolicyError(null);
        setError(null);
      } catch (caught) {
        if (caught instanceof DOMException && caught.name === "AbortError") return;
        setError(caught instanceof Error ? caught.message : "无法读取真实内存采样");
      } finally {
        if (!controller.signal.aborted) setLoading(false);
      }
    };
    void load();
    return () => controller.abort();
  }, [refreshVersion]);

  useEffect(() => {
    if (!sectorOperationId) return;
    const controller = new AbortController();
    let timer: number | undefined;
    const poll = async () => {
      try {
        const response = await apiFetch(`/api/v1/operations/${encodeURIComponent(sectorOperationId)}`, {
          headers: { Accept: "application/json" }, signal: controller.signal,
        });
        if (!response.ok) throw new Error(await readApiError(response));
        const operation = await response.json() as SectorUnloadOperation;
        setSectorOperation(operation);
        if (operation.status === "queued" || operation.status === "running") {
          timer = window.setTimeout(() => void poll(), 600);
          return;
        }
        if (notifiedSectorOperation.current !== operation.operationId) {
          notifiedSectorOperation.current = operation.operationId;
          if (operation.status === "succeeded") {
            onNotify(operation.result?.unloaded
              ? `星区 (${operation.result.x}, ${operation.result.y}) 已确认卸载`
              : "Avorion 已接受请求，但该星区仍因游戏活动保持加载");
            setRefreshVersion((value) => value + 1);
          } else onNotify(operation.error?.message ?? "空闲星区卸载失败");
        }
      } catch (caught) {
        if (caught instanceof DOMException && caught.name === "AbortError") return;
        onNotify(caught instanceof Error ? caught.message : "无法读取星区卸载进度");
      }
    };
    void poll();
    return () => { controller.abort(); if (timer !== undefined) window.clearTimeout(timer); };
  }, [onNotify, sectorOperationId]);

  const startSectorUnload = async (x: number, y: number) => {
    if (!window.confirm(`尝试卸载空闲星区 (${x}, ${y}) 吗？系统会在服务端再次确认没有在线玩家；若游戏仍在使用该星区，不会强制清除。`)) return;
    const key = `${x}:${y}`;
    setSubmittingSector(key);
    setSectorError(null);
    try {
      const response = await apiFetch("/api/v1/servers/local/sectors/unload-attempts", {
        method: "POST",
        headers: { Accept: "application/json", "Content-Type": "application/json", "Idempotency-Key": createIdempotencyKey("sector-unload") },
        body: JSON.stringify({ x, y, confirmation: `UNLOAD ${x} ${y}` }),
      });
      if (!response.ok) throw new Error(await readApiError(response));
      const accepted = await response.json() as { operationId: string };
      setSectorOperationId(accepted.operationId);
      setSectorOperation({ operationId: accepted.operationId, status: "queued", progressPercent: 0, currentStep: "validating-empty-sector", result: null, error: null });
    } catch (caught) {
      const message = caught instanceof Error ? caught.message : "无法提交空闲星区卸载";
      setSectorError(message);
      onNotify(message);
    } finally {
      setSubmittingSector(null);
    }
  };

  const savePolicy = async () => {
    const value = Number(thresholdGiB);
    if (!Number.isFinite(value) || value < 0.5 || value > 1024) {
      setPolicyError("告警阈值必须在 0.5 GB 到 1024 GB 之间");
      return;
    }
    setPolicySaving(true);
    setPolicyError(null);
    try {
      const response = await apiFetch("/api/v1/servers/local/memory-policy", {
        method: "PATCH",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          warningThresholdBytes: Math.round(value * bytesPerGiB),
          notify: notifyEnabled,
          tryUnloadIdleSectors: false,
          safeRestartAtCritical: false,
        }),
      });
      if (!response.ok) throw new Error(await readApiError(response));
      const saved = await response.json() as MemoryPolicy;
      setPolicy(saved);
      setThresholdGiB(String(Number((saved.warningThresholdBytes / bytesPerGiB).toFixed(2))));
      onNotify("内存告警策略已保存到本机管理数据库");
    } catch (caught) {
      const message = caught instanceof Error ? caught.message : "内存策略保存失败";
      setPolicyError(message);
      onNotify(message);
    } finally {
      setPolicySaving(false);
    }
  };

  const series = useMemo(() => (history?.points ?? [])
    .filter((point) => point.memoryWorkingSetBytes !== null)
    .map((point) => ({ label: timeLabel(point.at), memory: toGiB(point.memoryWorkingSetBytes) })), [history]);
  const current = toGiB(snapshot?.memory.workingSetBytes ?? null);
  const capacity = toGiB(snapshot?.memory.hostTotalBytes ?? null);
  const baseline = series.length ? series[0].memory : null;
  const growth = current !== null && baseline !== null ? current - baseline : null;
  const processShare = current !== null && capacity ? Math.min(100, current / capacity * 100) : null;
  const sampleTime = snapshot?.provenance.sampledAt ? timeLabel(snapshot.provenance.sampledAt) : "尚未取得";
  const overThreshold = overview?.alertLevel === "warning" && notifyEnabled;

  return (
    <div className="memory-page">
      <div className="memory-page__heading">
        <div className="memory-page__title-row">
          <h2>内存与星区</h2>
          <StatusPill tone={overThreshold ? "danger" : current !== null ? "success" : "warning"} compact>{overThreshold ? "超过告警阈值" : current !== null ? "真实进程数据" : "数据不可用"}</StatusPill>
        </div>
        <p>内存来自 Windows 上的真实 Avorion 进程；未接入的数据明确显示为不可用。</p>
      </div>

      {error && <Card className="update-api-error" role="alert"><Info size={20} /><div><strong>真实内存采样不可用</strong><span>{error}</span></div></Card>}
      {overThreshold && <Card className="update-api-error" role="alert"><BellRing size={20} /><div><strong>Avorion 内存已超过告警阈值</strong><span>当前 {valueText(current)}，策略阈值 {valueText(toGiB(overview?.warningThresholdBytes ?? null))}。</span></div></Card>}

      <Card className="memory-summary-band" aria-label="真实内存摘要">
        <div className="memory-summary-grid">
          <div className="memory-summary-item memory-summary-item--current">
            <span className="memory-summary-item__icon memory-summary-item__icon--purple"><MemoryStick size={24} /></span>
            <div><span>Avorion 当前内存</span><strong>{valueText(current)} <small>{capacity === null ? "主机容量不可用" : `/ 主机 ${formatDecimal(capacity, 1)} GB`}</small></strong><i><span style={{ width: `${processShare ?? 0}%` }} /></i></div>
          </div>
          <div className="memory-summary-item"><span className="memory-summary-item__icon memory-summary-item__icon--blue"><Activity size={24} /></span><div><span>区间首个样本</span><strong>{valueText(baseline)}</strong></div></div>
          <div className="memory-summary-item"><span className="memory-summary-item__icon memory-summary-item__icon--green"><TrendingUp size={24} /></span><div><span>区间变化</span><strong>{growth === null ? "—" : `${growth >= 0 ? "+" : ""}${formatDecimal(growth, 2)} GB`}</strong></div></div>
          <div className="memory-summary-item"><span className="memory-summary-item__icon memory-summary-item__icon--blue"><Layers3 size={24} /></span><div><span>已加载星区</span><strong>{sectors?.total ?? overview?.loadedSectorCount ?? "不可用"}</strong></div></div>
        </div>
        <div className="memory-capacity-track"><div className="memory-capacity-track__label"><span>进程占主机物理内存</span><strong>{processShare === null ? "—" : `${formatDecimal(processShare, 1)}%`}</strong></div><div className="memory-capacity-track__rail"><span style={{ width: `${processShare ?? 0}%` }} /></div></div>
      </Card>

      <div className="memory-workspace">
        <div className="memory-workspace__primary">
          <Card className="sector-table-card">
            <div className="sector-table-card__header"><div><h2>加载星区</h2><p>实时读取 Avorion 加载列表；仅可请求卸载服务端再次确认无玩家的星区</p></div><StatusPill tone={sectors ? "success" : "warning"} compact>{sectors ? "真实数据" : "组件未连接"}</StatusPill></div>
            {sectors ? <div className="sector-live-table-wrap"><table className="sector-live-table"><thead><tr><th>坐标</th><th>在线玩家</th><th>安全状态</th><th>操作</th></tr></thead><tbody>{sectors.items.map((sector) => {
              const key = `${sector.x}:${sector.y}`;
              const busy = submittingSector === key || (sectorOperation?.status === "queued" || sectorOperation?.status === "running");
              return <tr key={key}><td><strong>({sector.x}, {sector.y})</strong></td><td>{sector.playerCount}</td><td><span className={sector.eligible ? "text-success" : "tasks-table__paused"}>{sector.eligible ? "无人停留" : "玩家所在"}</span></td><td><Button variant="ghost" size="sm" disabled={!sector.eligible || busy} onClick={() => void startSectorUnload(sector.x, sector.y)}>{submittingSector === key ? <LoaderCircle className="spin" size={14} /> : <Layers3 size={14} />}{sector.eligible ? "尝试卸载" : "不可卸载"}</Button></td></tr>;
            })}{sectors.items.length === 0 && <tr><td colSpan={4}>当前没有已加载星区</td></tr>}</tbody></table>
              {(sectorOperation?.status === "queued" || sectorOperation?.status === "running") && <div className="update-operation-progress" aria-live="polite"><div><span>{sectorOperation.currentStep ?? "正在验证并请求卸载"}</span><strong>{Math.round(sectorOperation.progressPercent ?? 0)}%</strong></div><progress max={100} value={sectorOperation.progressPercent ?? 0} /><small>Operation ID：{sectorOperation.operationId}</small></div>}
              {sectorOperation?.status === "failed" && <div className="memory-policy-error" role="alert">{sectorOperation.error?.message ?? "空闲星区卸载失败"}</div>}
            </div>
              : <div className="sector-table-empty" role="status"><Info size={20} /><strong>管理组件尚未返回星区</strong><span>{sectorError ?? "服务器启动并加载 OrionAdminBridge 后会显示真实星区。"}</span></div>}
          </Card>
        </div>

        <aside className="memory-workspace__rail" aria-label="内存观察">
          <Card className="memory-strategy-card">
            <div className="rail-card-heading"><div><h2>内存告警策略</h2><p>{policy ? `已持久化 · ${timeLabel(policy.updatedAt)}` : "正在读取策略"}</p></div><BellRing size={20} /></div>
            <label className="memory-policy-field"><span>告警阈值（GB）</span><input type="number" min="0.5" max="1024" step="0.5" value={thresholdGiB} onChange={(event) => setThresholdGiB(event.target.value)} disabled={policySaving || !policy} /></label>
            <label className="memory-policy-toggle"><span><strong>页面告警</strong><small>达到阈值时在内存页明确标记</small></span><input type="checkbox" checked={notifyEnabled} onChange={(event) => setNotifyEnabled(event.target.checked)} disabled={policySaving || !policy} /></label>
            <div className="setup-blocked-note"><Info size={19} /><div><strong>自动动作保持关闭</strong><span>可在左侧逐个尝试安全卸载空闲星区；自动批量卸载和临界自动重启仍不会随此策略启用。</span></div></div>
            {policyError && <div className="memory-policy-error" role="alert">{policyError}</div>}
            <Button className="memory-strategy-card__save" variant="secondary" onClick={() => void savePolicy()} disabled={policySaving || !policy}>{policySaving ? <LoaderCircle className="spin" size={16} /> : <Check size={16} />}{policySaving ? "正在保存" : "保存告警策略"}</Button>
          </Card>

          <Card className="memory-trend-card">
            <div className="memory-trend-card__header"><div><h2>近 24 小时进程内存</h2><p>最后采样：{sampleTime} · Windows Agent</p></div><Button variant="ghost" size="sm" onClick={() => setRefreshVersion((value) => value + 1)} disabled={loading}><RefreshCw className={loading ? "spin" : undefined} size={15} />刷新</Button></div>
            <div className="memory-trend-chart" role="img" aria-label="Avorion 进程近 24 小时真实内存趋势">
              {series.length >= 2 ? <ResponsiveContainer width="100%" height="100%"><LineChart data={series} margin={{ top: 18, right: 16, bottom: 0, left: 2 }}><CartesianGrid stroke="#e8edf4" strokeDasharray="3 4" vertical={false} /><XAxis dataKey="label" axisLine={false} tickLine={false} interval="preserveStartEnd" minTickGap={25} tick={{ fill: "#7a8799", fontSize: 10 }} /><YAxis domain={[0, "auto"]} width={48} axisLine={false} tickLine={false} tick={{ fill: "#7a8799", fontSize: 10 }} tickFormatter={(value) => `${formatDecimal(value, 1)} GB`} /><Tooltip content={<MemoryTooltip />} /><Line type="monotone" dataKey="memory" name="进程内存" stroke="#7657e6" strokeWidth={2.4} dot={false} activeDot={{ r: 4 }} isAnimationActive={false} /></LineChart></ResponsiveContainer> : <div className="sector-table-empty"><Info size={18} /><span>真实历史样本不足，暂不绘制趋势。</span></div>}
            </div>
            <div className="memory-trend-card__footer"><span>区间首值 <strong>{valueText(baseline)}</strong></span><span>当前 <strong>{valueText(current)}</strong></span></div>
          </Card>
        </aside>
      </div>
    </div>
  );
}
