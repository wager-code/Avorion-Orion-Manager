import {
  AlertCircle,
  ArrowDownToLine,
  ArrowUpFromLine,
  Clock3,
  Cpu,
  Gauge,
  HardDrive,
  Info,
  MemoryStick,
  Network,
  RefreshCw,
  TrendingDown,
  TrendingUp,
  Users,
} from "lucide-react";
import { useEffect, useMemo, useState, type CSSProperties, type ReactNode } from "react";
import { useSearchParams } from "react-router-dom";
import { CartesianGrid, Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from "recharts";
import { Button, Card, IconBox, StatusPill } from "../components/ui";
import { apiFetch } from "../lib/api";
import { cn, formatDecimal } from "../lib/utils";
import type { PerformanceHistory, PerformanceHistoryPoint, PerformanceSnapshot, RangeKey, ServerStatus } from "../types";

const validRanges: RangeKey[] = ["1h", "24h", "7d"];
const rangeLabels: Record<RangeKey, string> = { "1h": "1 小时", "24h": "24 小时", "7d": "7 天" };
const bytesPerGiB = 1024 ** 3;

type ChartPoint = {
  at: string;
  label: string;
  cpu: number | null;
  memory: number | null;
  download: number | null;
  upload: number | null;
  players: number | null;
};

type NumericChartKey = "cpu" | "memory" | "download" | "upload" | "players";

async function readApiError(response: Response) {
  try {
    const payload = await response.json() as { error?: { message?: string }; message?: string };
    return payload.error?.message ?? payload.message ?? `请求失败（HTTP ${response.status}）`;
  } catch {
    return `请求失败（HTTP ${response.status}）`;
  }
}

function bytesToGiB(value: number | null) {
  return value === null ? null : value / bytesPerGiB;
}

function bytesPerSecondToMbps(value: number | null) {
  return value === null ? null : value * 8 / 1_000_000;
}

function formatPointLabel(value: string, range: RangeKey) {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return "—";
  return new Intl.DateTimeFormat("zh-CN", range === "7d"
    ? { month: "numeric", day: "numeric" }
    : { hour: "2-digit", minute: "2-digit", hour12: false }).format(date);
}

function toChartPoint(point: PerformanceHistoryPoint, range: RangeKey): ChartPoint {
  return {
    at: point.at,
    label: formatPointLabel(point.at, range),
    cpu: point.cpuProcessPercent,
    memory: bytesToGiB(point.memoryWorkingSetBytes),
    download: bytesPerSecondToMbps(point.networkDownloadBytesPerSecond),
    upload: bytesPerSecondToMbps(point.networkUploadBytesPerSecond),
    players: point.onlinePlayers,
  };
}

function downsample(points: ChartPoint[], maximum = 180) {
  if (points.length <= maximum) return points;
  const result: ChartPoint[] = [];
  const step = (points.length - 1) / (maximum - 1);
  for (let index = 0; index < maximum; index += 1) result.push(points[Math.round(index * step)]);
  return result;
}

function valuesOf(data: ChartPoint[], key: NumericChartKey) {
  return data.map((item) => item[key]).filter((value): value is number => typeof value === "number" && Number.isFinite(value));
}

function lastValue(data: ChartPoint[], key: NumericChartKey) {
  for (let index = data.length - 1; index >= 0; index -= 1) {
    const value = data[index][key];
    if (typeof value === "number" && Number.isFinite(value)) return value;
  }
  return null;
}

function averageValue(data: ChartPoint[], key: NumericChartKey) {
  const values = valuesOf(data, key);
  return values.length ? values.reduce((sum, value) => sum + value, 0) / values.length : null;
}

function maxValue(data: ChartPoint[], key: NumericChartKey) {
  const values = valuesOf(data, key);
  return values.length ? Math.max(...values) : null;
}

function formatValue(value: number | null, digits = 1) {
  return value === null ? "—" : formatDecimal(value, digits);
}

function formatUptime(seconds: number | null) {
  if (seconds === null) return "—";
  const days = Math.floor(seconds / 86400);
  const hours = Math.floor((seconds % 86400) / 3600);
  const minutes = Math.floor((seconds % 3600) / 60);
  if (days > 0) return `${days}天 ${hours}小时`;
  if (hours > 0) return `${hours}小时 ${minutes}分`;
  return `${minutes}分钟`;
}

function formatSampleTime(value?: string | null) {
  if (!value) return "尚未取得";
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return "时间未知";
  return new Intl.DateTimeFormat("zh-CN", { hour: "2-digit", minute: "2-digit", second: "2-digit", hour12: false }).format(date);
}

function MetricCard({ label, value, unit, detail, icon, tone }: {
  label: string;
  value: string;
  unit?: string;
  detail: string;
  icon: ReactNode;
  tone: string;
}) {
  return (
    <Card className="metric-card">
      <IconBox tone={tone}>{icon}</IconBox>
      <div className="metric-card__copy">
        <span>{label}</span>
        <strong>{value}{unit && value !== "—" ? <small>{unit}</small> : null}</strong>
        <p title={detail}>{detail}</p>
      </div>
    </Card>
  );
}

function ChartKey({ color, label, dashed = false }: { color: string; label: string; dashed?: boolean }) {
  return (
    <span className="chart-key">
      <span className={cn("chart-key__line", dashed && "chart-key__line--dashed")} style={{ "--key-color": color } as CSSProperties} aria-hidden="true" />
      {label}
    </span>
  );
}

function PerformanceTooltip({ active, payload, label, units }: {
  active?: boolean;
  payload?: Array<{ name?: string; value?: number; color?: string; dataKey?: string }>;
  label?: string;
  units: Record<string, string>;
}) {
  if (!active || !payload?.length) return null;
  return (
    <div className="chart-tooltip">
      <strong>{label}</strong>
      {payload.map((item) => (
        <div key={item.dataKey ?? item.name}>
          <span style={{ backgroundColor: item.color }} aria-hidden="true" />
          <em>{item.name}</em>
          <b>{typeof item.value === "number" ? formatDecimal(item.value, item.dataKey === "memory" ? 2 : 1) : "—"}{units[item.dataKey ?? ""] ?? ""}</b>
        </div>
      ))}
    </div>
  );
}

function ChartEmptyState({ message }: { message: string }) {
  return <div className="chart-empty-state" role="status"><Info size={18} aria-hidden="true" /><span>{message}</span></div>;
}

function ResourceMiniChart({ data, dataKey, label, color, unit, domain, ticks, showXAxis = false, description }: {
  data: ChartPoint[];
  dataKey: "cpu" | "memory";
  label: string;
  color: string;
  unit: string;
  domain: [number | "auto", number | "auto"];
  ticks?: number[];
  showXAxis?: boolean;
  description: string;
}) {
  const current = lastValue(data, dataKey);
  const hasData = valuesOf(data, dataKey).length >= 2;
  return (
    <div className="resource-mini-chart">
      <div className="resource-mini-chart__label">
        <ChartKey color={color} label={label} />
        <strong>{formatValue(current, dataKey === "memory" ? 2 : 0)} {current === null ? "" : unit}</strong>
      </div>
      {hasData ? (
        <div className="chart-canvas chart-canvas--resource" role="img" aria-label={description} tabIndex={0}>
          <ResponsiveContainer width="100%" height="100%">
            <LineChart data={data} margin={{ top: 8, right: 12, bottom: showXAxis ? 2 : -3, left: -12 }}>
              <CartesianGrid stroke="#e8edf4" strokeDasharray="3 4" vertical={false} />
              <XAxis dataKey="label" hide={!showXAxis} axisLine={false} tickLine={false} tick={{ fill: "#7a8799", fontSize: 11 }} interval="preserveStartEnd" minTickGap={28} />
              <YAxis domain={domain} ticks={ticks} width={48} axisLine={false} tickLine={false} tick={{ fill: "#7a8799", fontSize: 11 }} tickFormatter={(value) => `${formatDecimal(value, dataKey === "memory" ? 1 : 0)}${unit}`} />
              <Tooltip cursor={{ stroke: "#cbd6e4", strokeDasharray: "3 3" }} content={<PerformanceTooltip units={{ [dataKey]: unit }} />} />
              <Line type="monotone" dataKey={dataKey} name={label} stroke={color} strokeWidth={2.3} dot={false} activeDot={{ r: 4, strokeWidth: 2, fill: "#fff" }} isAnimationActive={false} connectNulls={false} />
            </LineChart>
          </ResponsiveContainer>
        </div>
      ) : <ChartEmptyState message="真实历史样本不足，暂不绘制趋势" />}
    </div>
  );
}

export function ServerPerformancePage({ serverStatus }: { serverStatus: ServerStatus | null }) {
  const [searchParams, setSearchParams] = useSearchParams();
  const requestedRange = searchParams.get("range") as RangeKey | null;
  const range: RangeKey = requestedRange && validRanges.includes(requestedRange) ? requestedRange : "24h";
  const [snapshot, setSnapshot] = useState<PerformanceSnapshot | null>(null);
  const [history, setHistory] = useState<PerformanceHistory | null>(null);
  const [history24h, setHistory24h] = useState<PerformanceHistory | null>(null);
  const [snapshotError, setSnapshotError] = useState<string | null>(null);
  const [historyError, setHistoryError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [refreshVersion, setRefreshVersion] = useState(0);

  useEffect(() => {
    let disposed = false;
    let inFlight = false;
    const load = async () => {
      if (inFlight) return;
      inFlight = true;
      try {
        const response = await apiFetch("/api/v1/servers/local/performance/current", { headers: { Accept: "application/json" } });
        if (!response.ok) throw new Error(await readApiError(response));
        const next = await response.json() as PerformanceSnapshot;
        if (!disposed) { setSnapshot(next); setSnapshotError(null); }
      } catch (caught) {
        if (!disposed) setSnapshotError(caught instanceof Error ? caught.message : "无法读取当前性能数据");
      } finally {
        inFlight = false;
        if (!disposed) setLoading(false);
      }
    };
    void load();
    const timer = window.setInterval(() => void load(), 5000);
    return () => { disposed = true; window.clearInterval(timer); };
  }, [refreshVersion]);

  useEffect(() => {
    let disposed = false;
    let inFlight = false;
    const load = async () => {
      if (inFlight) return;
      inFlight = true;
      try {
        const ranges = range === "24h" ? ["24h"] : [range, "24h"];
        const responses = await Promise.all(ranges.map(async (item) => {
          const response = await apiFetch(`/api/v1/servers/local/performance/history?range=${item}`, { headers: { Accept: "application/json" } });
          if (!response.ok) throw new Error(await readApiError(response));
          return response.json() as Promise<PerformanceHistory>;
        }));
        if (!disposed) {
          const selected = responses.find((item) => item.range === range) ?? responses[0];
          const daily = responses.find((item) => item.range === "24h") ?? selected;
          setHistory(selected);
          setHistory24h(daily);
          setHistoryError(null);
        }
      } catch (caught) {
        if (!disposed) setHistoryError(caught instanceof Error ? caught.message : "无法读取历史性能数据");
      } finally {
        inFlight = false;
        if (!disposed) setLoading(false);
      }
    };
    void load();
    const timer = window.setInterval(() => void load(), 30000);
    return () => { disposed = true; window.clearInterval(timer); };
  }, [range, refreshVersion]);

  const rawData = useMemo(() => (history?.points ?? []).map((point) => toChartPoint(point, range)), [history, range]);
  const data = useMemo(() => downsample(rawData), [rawData]);
  const data24h = useMemo(() => (history24h?.points ?? []).map((point) => toChartPoint(point, "24h")), [history24h]);
  const cpu = snapshot?.cpu.processPercent ?? null;
  const memory = bytesToGiB(snapshot?.memory.workingSetBytes ?? null);
  const memoryTotal = bytesToGiB(snapshot?.memory.hostTotalBytes ?? null);
  const diskAvailable = bytesToGiB(snapshot?.disk.availableBytes ?? null);
  const diskTotal = bytesToGiB(snapshot?.disk.totalBytes ?? null);
  const download = bytesPerSecondToMbps(snapshot?.network.downloadBytesPerSecond ?? null);
  const upload = bytesPerSecondToMbps(snapshot?.network.uploadBytesPerSecond ?? null);
  const players = snapshot?.onlinePlayers ?? serverStatus?.onlinePlayers ?? null;
  const capacity = serverStatus?.maxPlayers ?? null;
  const memoryValues = valuesOf(data24h, "memory");
  const memoryGrowth = memoryValues.length >= 2 ? memoryValues[memoryValues.length - 1] - memoryValues[0] : null;
  const memoryRatio = memory !== null && memoryTotal ? Math.min(memory / memoryTotal * 100, 100) : null;
  const hasLastKnownData = snapshot !== null || rawData.length > 0;
  const unavailable = snapshot?.provenance.freshness === "unavailable";
  const stale = hasLastKnownData && (snapshotError !== null || historyError !== null);
  const historyInsufficient = history?.warningCode === "INSUFFICIENT_HISTORY";
  const statusPresentation = snapshotError && !hasLastKnownData
    ? { tone: "danger" as const, label: "无法连接" }
    : stale
      ? { tone: "warning" as const, label: "数据已停止更新" }
      : unavailable
        ? { tone: "warning" as const, label: "服务器未运行" }
        : historyInsufficient
          ? { tone: "info" as const, label: "历史积累中" }
          : { tone: "success" as const, label: "真实数据正常" };
  const stats = [
    { name: "CPU 使用率", tone: "blue", current: `${formatValue(cpu, 1)}${cpu === null ? "" : "%"}`, average: `${formatValue(averageValue(data24h, "cpu"), 1)}${averageValue(data24h, "cpu") === null ? "" : "%"}`, peak: `${formatValue(maxValue(data24h, "cpu"), 1)}${maxValue(data24h, "cpu") === null ? "" : "%"}` },
    { name: "内存使用", tone: "purple", current: `${formatValue(memory, 2)}${memory === null ? "" : " GB"}`, average: `${formatValue(averageValue(data24h, "memory"), 2)}${averageValue(data24h, "memory") === null ? "" : " GB"}`, peak: `${formatValue(maxValue(data24h, "memory"), 2)}${maxValue(data24h, "memory") === null ? "" : " GB"}` },
    { name: "主机网络下载", tone: "cyan", current: `${formatValue(download, 2)}${download === null ? "" : " Mbps"}`, average: `${formatValue(averageValue(data24h, "download"), 2)}${averageValue(data24h, "download") === null ? "" : " Mbps"}`, peak: `${formatValue(maxValue(data24h, "download"), 2)}${maxValue(data24h, "download") === null ? "" : " Mbps"}` },
    { name: "主机网络上传", tone: "orange", current: `${formatValue(upload, 2)}${upload === null ? "" : " Mbps"}`, average: `${formatValue(averageValue(data24h, "upload"), 2)}${averageValue(data24h, "upload") === null ? "" : " Mbps"}`, peak: `${formatValue(maxValue(data24h, "upload"), 2)}${maxValue(data24h, "upload") === null ? "" : " Mbps"}` },
    { name: "在线玩家", tone: "green", current: `${formatValue(players, 0)}${players === null ? "" : " 人"}`, average: `${formatValue(averageValue(data24h, "players"), 1)}${averageValue(data24h, "players") === null ? "" : " 人"}`, peak: `${formatValue(maxValue(data24h, "players"), 0)}${maxValue(data24h, "players") === null ? "" : " 人"}` },
  ];
  const retry = () => { setLoading(true); setRefreshVersion((value) => value + 1); };

  return (
    <div className="performance-page" aria-busy={loading}>
      <div className="performance-toolbar">
        <div>
          <div className="performance-toolbar__title-row"><h2>性能监控</h2><StatusPill tone={statusPresentation.tone} compact>{statusPresentation.label}</StatusPill></div>
          <p>{snapshot ? `Windows Agent 实时采集 · 最近更新 ${formatSampleTime(snapshot.provenance.sampledAt)}${snapshot.network.scope === "host" ? " · 网络为整机总流量" : ""}` : loading ? "正在读取本机服务器性能数据…" : "尚未取得本机服务器性能数据。"}</p>
        </div>
        <div className="performance-toolbar__actions">
          <div className="range-control" aria-label="选择监控时间范围">
            {validRanges.map((item) => <button key={item} type="button" className={cn("range-control__button", item === range && "range-control__button--active")} aria-pressed={item === range} onClick={() => setSearchParams({ range: item }, { replace: true })}>{rangeLabels[item]}</button>)}
          </div>
          <Button variant="ghost" size="sm" onClick={retry} disabled={loading}><RefreshCw size={15} className={loading ? "spin" : undefined} />刷新</Button>
        </div>
      </div>

      {(snapshotError || historyError) && <div className={cn("performance-state-banner", hasLastKnownData && "performance-state-banner--stale")} role="alert"><AlertCircle size={19} aria-hidden="true" /><div><strong>{hasLastKnownData ? "正在显示最后一次有效数据" : "无法读取真实性能数据"}</strong><span>{snapshotError ?? historyError}</span></div></div>}

      <div className="metric-grid" aria-label="当前性能摘要">
        <MetricCard label="CPU 使用率" value={formatValue(cpu, 1)} unit="%" detail={cpu === null ? "Avorion 进程指标不可用" : "当前 Avorion 进程占用"} tone="blue" icon={<Cpu size={26} strokeWidth={2.2} />} />
        <MetricCard label="内存使用" value={formatValue(memory, 2)} unit="GB" detail={memory === null ? "Avorion 进程指标不可用" : memoryTotal ? `${formatValue(memoryRatio, 1)}% / 主机 ${formatValue(memoryTotal, 1)} GB` : "当前 Avorion 进程工作集"} tone="purple" icon={<MemoryStick size={26} strokeWidth={2.2} />} />
        <MetricCard label="磁盘空间" value={formatValue(diskAvailable, 1)} unit="GB" detail={diskAvailable === null ? "服务器所在磁盘不可用" : `可用 / ${formatValue(diskTotal, 1)} GB${snapshot?.disk.volume ? ` · ${snapshot.disk.volume}` : ""}`} tone="green" icon={<HardDrive size={26} strokeWidth={2.2} />} />
        <MetricCard label="网络流量" value={formatValue(download, 2)} unit="Mbps" detail={download === null ? "等待第二次主机网络采样" : `主机上传 ${formatValue(upload, 2)} Mbps`} tone="cyan" icon={<Network size={26} strokeWidth={2.2} />} />
        <MetricCard label="在线玩家" value={players === null ? "—" : capacity ? `${players} / ${capacity}` : String(players)} detail={players === null ? "Steam Query 尚不可用" : "真实在线人数"} tone="blue" icon={<Users size={26} strokeWidth={2.2} />} />
        <MetricCard label="运行时间" value={formatUptime(snapshot?.uptimeSeconds ?? null)} detail={snapshot?.uptimeSeconds === null || snapshot?.uptimeSeconds === undefined ? "服务端进程未运行" : "Avorion 进程持续时间"} tone="orange" icon={<Clock3 size={26} strokeWidth={2.2} />} />
      </div>

      <div className="performance-charts-grid">
        <Card className="resource-trends-card">
          <div className="section-heading section-heading--compact"><div><h2>CPU 与内存趋势</h2><p>{rangeLabels[range]}范围 · 真实采样 · 独立量纲</p></div><span className="chart-insight"><Gauge size={17} aria-hidden="true" />CPU 当前 {formatValue(cpu, 1)}{cpu === null ? "" : "%"}</span></div>
          <ResourceMiniChart data={data} dataKey="cpu" label="CPU" color="#1677ff" unit="%" domain={[0, 100]} ticks={[0, 50, 100]} description={`${rangeLabels[range]} CPU 使用率趋势，当前 ${formatValue(cpu, 1)}%，区间峰值 ${formatValue(maxValue(rawData, "cpu"), 1)}%`} />
          <ResourceMiniChart data={data} dataKey="memory" label="内存" color="#7c5ce7" unit="GB" domain={["auto", "auto"]} showXAxis description={`${rangeLabels[range]}内存趋势，当前 ${formatValue(memory, 2)} GB，区间峰值 ${formatValue(maxValue(rawData, "memory"), 2)} GB`} />
        </Card>

        <div className="secondary-charts">
          <Card className="compact-chart-card">
            <div className="compact-chart-card__header"><div><h2>主机网络流量</h2><p>整机网卡下载与上传，不代表 Avorion 单进程</p></div><div className="chart-keys" aria-label="网络图例"><ChartKey color="#0e91ad" label="下载" /><ChartKey color="#d97828" label="上传" dashed /></div></div>
            {valuesOf(data, "download").length >= 2 || valuesOf(data, "upload").length >= 2 ? <div className="chart-canvas chart-canvas--compact" role="img" aria-label={`${rangeLabels[range]}主机网络趋势，当前下载 ${formatValue(download, 2)} Mbps，上传 ${formatValue(upload, 2)} Mbps`} tabIndex={0}>
              <ResponsiveContainer width="100%" height="100%"><LineChart data={data} margin={{ top: 10, right: 8, bottom: 0, left: -20 }}><CartesianGrid stroke="#e8edf4" strokeDasharray="3 4" vertical={false} /><XAxis dataKey="label" hide /><YAxis axisLine={false} tickLine={false} tick={{ fill: "#7a8799", fontSize: 10 }} width={48} tickFormatter={(value) => `${formatDecimal(value, 1)}`} /><Tooltip cursor={{ stroke: "#cbd6e4", strokeDasharray: "3 3" }} content={<PerformanceTooltip units={{ download: " Mbps", upload: " Mbps" }} />} /><Line type="monotone" dataKey="download" name="下载" stroke="#0e91ad" strokeWidth={2.1} dot={false} isAnimationActive={false} connectNulls={false} /><Line type="monotone" dataKey="upload" name="上传" stroke="#d97828" strokeWidth={2.1} strokeDasharray="5 4" dot={false} isAnimationActive={false} connectNulls={false} /></LineChart></ResponsiveContainer>
            </div> : <ChartEmptyState message="等待真实网络历史样本" />}
            <div className="compact-chart-card__footer"><span><ArrowDownToLine size={14} aria-hidden="true" />下载 {formatValue(download, 2)} Mbps</span><span><ArrowUpFromLine size={14} aria-hidden="true" />上传 {formatValue(upload, 2)} Mbps</span></div>
          </Card>

          <Card className="compact-chart-card">
            <div className="compact-chart-card__header"><div><h2>在线玩家趋势</h2><p>仅显示可验证的真实人数</p></div><strong className="compact-chart-card__value">{players === null ? "—" : capacity ? `${players} / ${capacity}` : `${players} 人`}</strong></div>
            {valuesOf(data, "players").length >= 2 ? <div className="chart-canvas chart-canvas--compact" role="img" aria-label={`${rangeLabels[range]}在线玩家趋势，当前 ${formatValue(players, 0)} 人，峰值 ${formatValue(maxValue(rawData, "players"), 0)} 人`} tabIndex={0}>
              <ResponsiveContainer width="100%" height="100%"><LineChart data={data} margin={{ top: 10, right: 8, bottom: 0, left: -20 }}><CartesianGrid stroke="#e8edf4" strokeDasharray="3 4" vertical={false} /><XAxis dataKey="label" hide /><YAxis domain={[0, capacity ?? "auto"]} allowDecimals={false} axisLine={false} tickLine={false} tick={{ fill: "#7a8799", fontSize: 10 }} width={48} /><Tooltip cursor={{ stroke: "#cbd6e4", strokeDasharray: "3 3" }} content={<PerformanceTooltip units={{ players: " 人" }} />} /><Line type="stepAfter" dataKey="players" name="在线玩家" stroke="#208f5d" strokeWidth={2.2} dot={false} isAnimationActive={false} connectNulls={false} /></LineChart></ResponsiveContainer>
            </div> : <ChartEmptyState message="Steam Query 未提供真实玩家历史" />}
            <div className="compact-chart-card__footer compact-chart-card__footer--single"><span><Users size={14} aria-hidden="true" />区间峰值 {formatValue(maxValue(rawData, "players"), 0)} 人</span></div>
          </Card>
        </div>
      </div>

      <div className="performance-bottom-grid">
        <Card className="statistics-card">
          <div className="section-heading section-heading--compact"><div><h2>24 小时统计</h2><p>真实样本的当前值、平均值与峰值</p></div>{history24h?.warningCode && <StatusPill tone="info" compact>样本积累中</StatusPill>}</div>
          <div className="table-scroll"><table className="statistics-table"><thead><tr><th scope="col">指标</th><th scope="col">当前</th><th scope="col">24h 平均</th><th scope="col">24h 峰值</th></tr></thead><tbody>{stats.map((row) => <tr key={row.name}><th scope="row"><span className={cn("table-series-dot", `table-series-dot--${row.tone}`)} />{row.name}</th><td>{row.current}</td><td>{row.average}</td><td>{row.peak}</td></tr>)}</tbody></table></div>
        </Card>

        <Card className="memory-alert-card">
          <div className="memory-alert-card__icon" aria-hidden="true">{memoryGrowth !== null && memoryGrowth < 0 ? <TrendingDown size={24} /> : <TrendingUp size={24} />}</div>
          <div><span className="memory-alert-card__eyebrow">内存观察</span><h2>{memoryGrowth === null ? "等待 24 小时真实历史样本" : memoryGrowth >= 0 ? "进程内存较区间起点增长" : "进程内存较区间起点下降"}</h2><p>{memoryGrowth === null ? "历史样本不足时不会生成趋势结论。" : `从 ${formatValue(memoryValues[0], 2)} GB 变为 ${formatValue(memoryValues[memoryValues.length - 1], 2)} GB，变化 ${memoryGrowth >= 0 ? "+" : ""}${formatValue(memoryGrowth, 2)} GB。`}</p></div>
          <div className="memory-threshold"><div><span>进程 / 主机内存容量</span><strong>{formatValue(memoryRatio, 1)}{memoryRatio === null ? "" : "%"}</strong></div><div className="memory-threshold__track" aria-label={`Avorion 进程占主机内存容量 ${formatValue(memoryRatio, 1)}%`}><span style={{ width: `${memoryRatio ?? 0}%` }} /></div><p><Info size={14} aria-hidden="true" />此比例不是主机总内存使用率</p></div>
        </Card>
      </div>
    </div>
  );
}
