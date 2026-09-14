import {
  ChartNoAxesCombined,
  CircleCheck,
  CircleHelp,
  CircleX,
  DatabaseBackup,
  FolderOpen,
  Gamepad2,
  HardDrive,
  LoaderCircle,
  MonitorCog,
  Network,
  RefreshCw,
  Save,
  ServerCog,
  ShieldAlert,
  ShieldCheck,
  ShieldQuestion,
  SquareTerminal,
  TriangleAlert,
} from "lucide-react";
import { useEffect, useMemo, useState, type ReactNode } from "react";
import {
  CartesianGrid,
  Line,
  LineChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";
import { Button, Card, StatusPill } from "../components/ui";
import { apiFetch, createIdempotencyKey } from "../lib/api";
import { cn } from "../lib/utils";
import type { ApiOperation, DiagnosticItem, DiagnosticRun, DiagnosticState } from "../types";

type CheckDefinition = { key: string; label: string; icon: ReactNode };
type MemoryChartPoint = { at: string; time: string; value: number };
type OperationAccepted = { operationId: string; status: ApiOperation["status"]; acceptedAt: string };

const operationStorageKey = "avorion.diagnostics.operation-id";
const idempotencyStorageKey = "avorion.diagnostics.pending-idempotency-key";
const notifiedStorageKey = "avorion.diagnostics.notified-operation-id";
const bytesPerGiB = 1024 ** 3;

const connectionChecks: CheckDefinition[] = [
  { key: "process", label: "Avorion 进程", icon: <MonitorCog /> },
  { key: "game-port", label: "游戏端口", icon: <Network /> },
  { key: "steam-query", label: "Steam Query", icon: <Gamepad2 /> },
  { key: "rcon", label: "RCON", icon: <SquareTerminal /> },
];

const environmentChecks: CheckDefinition[] = [
  { key: "steamcmd", label: "SteamCMD", icon: <ServerCog /> },
  { key: "galaxy-path", label: "Galaxy 路径", icon: <FolderOpen /> },
  { key: "disk-space", label: "磁盘空间", icon: <HardDrive /> },
];

const runtimeChecks: CheckDefinition[] = [
  { key: "last-save", label: "最近保存", icon: <Save /> },
  { key: "last-backup", label: "最近备份", icon: <DatabaseBackup /> },
  { key: "memory-trend", label: "内存趋势", icon: <ChartNoAxesCombined /> },
  { key: "mod-errors", label: "现有 MOD 日志错误", icon: <TriangleAlert /> },
];

const allChecks = [...connectionChecks, ...environmentChecks, ...runtimeChecks];

function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}

function isDiagnosticRun(value: unknown): value is DiagnosticRun {
  return isObject(value) && typeof value.runId === "string" && Array.isArray(value.items);
}

async function readApiError(response: Response) {
  try {
    const payload = await response.json() as { error?: { message?: string }; message?: string };
    return payload.error?.message ?? payload.message ?? `请求失败（HTTP ${response.status}）`;
  } catch {
    return `请求失败（HTTP ${response.status}）`;
  }
}

function readNumber(item: DiagnosticItem | null, key: string) {
  const value = item?.evidence[key];
  return typeof value === "number" && Number.isFinite(value) ? value : null;
}

function readString(item: DiagnosticItem | null, key: string) {
  const value = item?.evidence[key];
  return typeof value === "string" ? value : null;
}

function formatGiB(value: number | null, digits = 1) {
  return value === null ? "—" : `${(value / bytesPerGiB).toFixed(digits)} GB`;
}

function formatRelativeTime(value: string | null) {
  if (!value) return "时间未知";
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return "时间未知";
  const seconds = Math.round((date.getTime() - Date.now()) / 1000);
  const formatter = new Intl.RelativeTimeFormat("zh-CN", { numeric: "auto" });
  if (Math.abs(seconds) < 60) return formatter.format(seconds, "second");
  const minutes = Math.round(seconds / 60);
  if (Math.abs(minutes) < 60) return formatter.format(minutes, "minute");
  const hours = Math.round(minutes / 60);
  if (Math.abs(hours) < 24) return formatter.format(hours, "hour");
  return formatter.format(Math.round(hours / 24), "day");
}

function formatClock(value: string | null | undefined) {
  if (!value) return "尚未完成";
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return "完成时间未知";
  return `${new Intl.DateTimeFormat("zh-CN", { hour: "2-digit", minute: "2-digit", second: "2-digit", hour12: false }).format(date)} 完成`;
}

function resultLabel(item: DiagnosticItem | null) {
  if (!item) return "待检查";
  if (item.key === "process") {
    if (item.status === "healthy") return "运行中";
    if (item.status === "error") return "未运行";
  }
  if (item.key === "game-port" || item.key === "steam-query" || item.key === "rcon") {
    return item.status === "healthy" ? "正常" : item.status === "unknown" ? "不可用" : item.status === "error" ? "异常" : "需关注";
  }
  if (item.key === "steamcmd" || item.key === "galaxy-path") return item.status === "healthy" ? "可用" : "不可用";
  if (item.key === "disk-space") {
    const available = readNumber(item, "availableBytes");
    return available === null ? "不可用" : `可用 ${formatGiB(available)}`;
  }
  if (item.key === "last-save") return formatRelativeTime(readString(item, "at"));
  if (item.key === "last-backup") return formatRelativeTime(readString(item, "createdAt"));
  if (item.key === "mod-errors") {
    const count = readNumber(item, "count");
    if (count === null) return "无日志";
    return count > 0 ? `${count} 项异常` : "未发现";
  }
  if (item.status === "healthy") return "正常";
  if (item.status === "warning") return "需关注";
  if (item.status === "error") return "异常";
  return "不可用";
}

function ResultMark({ status, scanning }: { status: DiagnosticState; scanning: boolean }) {
  if (scanning) return <LoaderCircle className="diagnostics-spin" size={17} aria-hidden="true" />;
  if (status === "healthy") return <CircleCheck size={17} aria-hidden="true" />;
  if (status === "warning") return <TriangleAlert size={18} aria-hidden="true" />;
  if (status === "error") return <CircleX size={18} aria-hidden="true" />;
  return <CircleHelp size={18} aria-hidden="true" />;
}

function DiagnosticRow({ definition, item, scanning }: { definition: CheckDefinition; item: DiagnosticItem | null; scanning: boolean }) {
  const status = item?.status ?? "unknown";
  const description = scanning ? "正在读取真实检查结果…" : item?.message ?? "运行诊断后显示真实结果";
  return (
    <div className={cn("diagnostics-row", `diagnostics-row--${status}`)}>
      <span className="diagnostics-row__icon" aria-hidden="true">{definition.icon}</span>
      <div className="diagnostics-row__copy">
        <strong>{definition.label}</strong>
        <span title={description}>{description}</span>
      </div>
      <span className="diagnostics-row__result">
        <ResultMark status={status} scanning={scanning} />
        {scanning ? "检查中" : resultLabel(item)}
      </span>
    </div>
  );
}

function DiagnosticGroup({ title, definitions, items, scanning }: { title: string; definitions: CheckDefinition[]; items: Map<string, DiagnosticItem>; scanning: boolean }) {
  return (
    <Card className="diagnostics-group" aria-label={title}>
      <h3>{title}</h3>
      <div className="diagnostics-group__body">
        {definitions.map((definition) => <DiagnosticRow key={definition.key} definition={definition} item={items.get(definition.key) ?? null} scanning={scanning} />)}
      </div>
    </Card>
  );
}

function MemoryTooltip({ active, payload, label }: { active?: boolean; payload?: Array<{ value?: number }>; label?: string }) {
  if (!active || !payload?.length) return null;
  return <div className="diagnostics-chart-tooltip"><span>{label}</span><strong>{payload[0].value?.toFixed(2)} GB</strong></div>;
}

function memoryChartPoints(item: DiagnosticItem | null): MemoryChartPoint[] {
  const raw = item?.evidence.points;
  if (!Array.isArray(raw)) return [];
  return raw.flatMap((point) => {
    if (!isObject(point) || typeof point.at !== "string" || typeof point.valueBytes !== "number" || !Number.isFinite(point.valueBytes)) return [];
    const date = new Date(point.at);
    if (Number.isNaN(date.getTime())) return [];
    return [{ at: point.at, time: new Intl.DateTimeFormat("zh-CN", { hour: "2-digit", minute: "2-digit", hour12: false }).format(date), value: point.valueBytes / bytesPerGiB }];
  });
}

function memorySummary(points: MemoryChartPoint[], item: DiagnosticItem | null) {
  if (points.length < 4) return item?.message ?? "运行诊断后读取最近 6 小时真实样本";
  const delta = points[points.length - 1].value - points[0].value;
  if (Math.abs(delta) < 0.05) return `最近 6 小时波动较小 · ${points.length} 个真实样本`;
  return `最近 6 小时较起点${delta > 0 ? "增加" : "下降"} ${Math.abs(delta).toFixed(2)} GB · ${points.length} 个真实样本`;
}

function RuntimeGroup({ items, scanning }: { items: Map<string, DiagnosticItem>; scanning: boolean }) {
  const memoryDefinition = runtimeChecks[2];
  const memoryItem = items.get(memoryDefinition.key) ?? null;
  const points = memoryChartPoints(memoryItem);
  const currentBytes = readNumber(memoryItem, "currentBytes");
  const memoryStatus = memoryItem?.status ?? "unknown";
  const chartReady = points.length >= 4;
  const chartLabel = chartReady
    ? `最近 6 小时 Avorion 进程内存趋势，共 ${points.length} 个真实样本，从 ${points[0].value.toFixed(2)} GB 变化到 ${points[points.length - 1].value.toFixed(2)} GB`
    : "最近 6 小时真实内存样本不足，未绘制趋势图";

  return (
    <Card className="diagnostics-group diagnostics-group--runtime" aria-label="数据与运行状态">
      <h3>数据与运行状态</h3>
      <div className="diagnostics-group__body">
        <DiagnosticRow definition={runtimeChecks[0]} item={items.get("last-save") ?? null} scanning={scanning} />
        <DiagnosticRow definition={runtimeChecks[1]} item={items.get("last-backup") ?? null} scanning={scanning} />
        <div className={cn("diagnostics-memory-row", `diagnostics-memory-row--${memoryStatus}`)}>
          <span className="diagnostics-row__icon" aria-hidden="true">{memoryDefinition.icon}</span>
          <div className="diagnostics-row__copy"><strong>内存趋势</strong><span title={memorySummary(points, memoryItem)}>{scanning ? "正在读取真实内存样本…" : memorySummary(points, memoryItem)}</span></div>
          <span className="diagnostics-memory-row__current"><ResultMark status={memoryStatus} scanning={scanning} />{scanning ? "检查中" : currentBytes === null ? resultLabel(memoryItem) : `当前 ${formatGiB(currentBytes, 2)}`}</span>
          {scanning ? (
            <div className="diagnostics-chart diagnostics-chart--empty" role="status"><LoaderCircle className="diagnostics-spin" size={18} aria-hidden="true" />正在读取最近 6 小时样本</div>
          ) : chartReady ? (
            <div className="diagnostics-chart" role="img" aria-label={chartLabel} tabIndex={0}>
              <span className="diagnostics-chart__unit" aria-hidden="true">GB</span>
              <ResponsiveContainer width="100%" height="100%">
                <LineChart data={points} margin={{ top: 22, right: 14, bottom: 0, left: -7 }}>
                  <CartesianGrid stroke="#e7edf5" strokeDasharray="3 4" vertical={false} />
                  <XAxis dataKey="time" axisLine={{ stroke: "#dbe4ef" }} tickLine={false} tick={{ fill: "#74839a", fontSize: 10 }} interval="preserveStartEnd" minTickGap={24} />
                  <YAxis domain={["auto", "auto"]} axisLine={false} tickLine={false} tick={{ fill: "#74839a", fontSize: 10 }} width={38} tickFormatter={(value) => Number(value).toFixed(1)} />
                  <Tooltip cursor={{ stroke: "#afbed1", strokeDasharray: "3 3" }} content={<MemoryTooltip />} />
                  <Line type="monotone" dataKey="value" name="内存" stroke="#315efb" strokeWidth={2.2} dot={false} activeDot={{ r: 4, fill: "#315efb", stroke: "#ffffff", strokeWidth: 2 }} isAnimationActive={false} />
                </LineChart>
              </ResponsiveContainer>
            </div>
          ) : <div className="diagnostics-chart diagnostics-chart--empty" role="status"><CircleHelp size={18} aria-hidden="true" />真实样本不足，暂不绘制趋势</div>}
        </div>
        <DiagnosticRow definition={runtimeChecks[3]} item={items.get("mod-errors") ?? null} scanning={scanning} />
      </div>
    </Card>
  );
}

export function ServerDiagnosticsPage({ onNotify }: { onNotify: (message: string) => void }) {
  const [operationId, setOperationId] = useState<string | null>(() => window.sessionStorage.getItem(operationStorageKey));
  const [operation, setOperation] = useState<ApiOperation | null>(null);
  const [run, setRun] = useState<DiagnosticRun | null>(null);
  const [starting, setStarting] = useState(false);
  const [restoring, setRestoring] = useState(Boolean(operationId));
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!operationId) { setRestoring(false); return; }
    let disposed = false;
    let timer: number | null = null;
    const controller = new AbortController();

    const poll = async () => {
      try {
        const response = await apiFetch(`/api/v1/operations/${encodeURIComponent(operationId)}`, { headers: { Accept: "application/json" }, signal: controller.signal });
        if (response.status === 404) {
          window.sessionStorage.removeItem(operationStorageKey);
          if (!disposed) { setOperationId(null); setOperation(null); setRun(null); setRestoring(false); }
          return;
        }
        if (!response.ok) throw new Error(await readApiError(response));
        const next = await response.json() as ApiOperation;
        if (disposed) return;
        setOperation(next);
        setRestoring(false);
        if (next.status === "queued" || next.status === "running") { timer = window.setTimeout(() => void poll(), 650); return; }
        if (next.status === "failed" || next.status === "cancelled") {
          setError(next.error?.message ?? (next.status === "cancelled" ? "诊断任务已取消" : "真实诊断执行失败"));
          return;
        }

        const runResponse = await apiFetch(`/api/v1/servers/local/diagnostics/runs/${encodeURIComponent(operationId)}`, { headers: { Accept: "application/json" }, signal: controller.signal });
        if (!runResponse.ok) throw new Error(await readApiError(runResponse));
        const payload: unknown = await runResponse.json();
        if (!isDiagnosticRun(payload)) throw new Error("诊断接口返回的数据格式无效");
        setRun(payload);
        setError(null);
        if (window.sessionStorage.getItem(notifiedStorageKey) !== operationId) {
          const issues = payload.items.filter((item) => item.status === "error" || item.status === "warning").length;
          const unavailable = payload.items.filter((item) => item.status === "unknown").length;
          const failures = payload.items.filter((item) => item.status === "error").length;
          const warnings = payload.items.filter((item) => item.status === "warning").length;
          onNotify(failures > 0 ? `诊断完成，${failures} 项异常` : warnings > 0 ? `诊断完成，${warnings} 项需关注` : unavailable > 0 ? `诊断完成，${unavailable} 项能力不可用` : "诊断完成，未发现异常");
          window.sessionStorage.setItem(notifiedStorageKey, operationId);
        }
      } catch (caught) {
        if (disposed || (caught instanceof DOMException && caught.name === "AbortError")) return;
        setRestoring(false);
        setError(caught instanceof Error ? caught.message : "无法读取真实诊断结果");
      }
    };

    void poll();
    return () => { disposed = true; controller.abort(); if (timer !== null) window.clearTimeout(timer); };
  }, [operationId, onNotify]);

  const startDiagnostics = async () => {
    if (starting || operation?.status === "queued" || operation?.status === "running") return;
    setStarting(true);
    setError(null);
    setRun(null);
    setOperation(null);
    const idempotencyKey = window.sessionStorage.getItem(idempotencyStorageKey) ?? createIdempotencyKey("diagnostics");
    window.sessionStorage.setItem(idempotencyStorageKey, idempotencyKey);
    try {
      const response = await apiFetch("/api/v1/servers/local/diagnostics/runs", { method: "POST", headers: { Accept: "application/json", "Idempotency-Key": idempotencyKey } });
      if (!response.ok) throw new Error(await readApiError(response));
      const accepted = await response.json() as OperationAccepted;
      if (!accepted.operationId) throw new Error("诊断任务没有返回操作编号");
      window.sessionStorage.setItem(operationStorageKey, accepted.operationId);
      window.sessionStorage.removeItem(idempotencyStorageKey);
      setOperationId(accepted.operationId);
      setOperation({ operationId: accepted.operationId, type: "diagnostics", status: accepted.status, progressPercent: null, currentStep: null, acceptedAt: accepted.acceptedAt, startedAt: null, completedAt: null, result: null, error: null });
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "无法启动真实诊断");
    } finally {
      setStarting(false);
    }
  };

  const itemMap = useMemo(() => new Map((run?.items ?? []).map((item) => [item.key, item])), [run]);
  const statuses = allChecks.map((definition) => itemMap.get(definition.key)?.status ?? "unknown");
  const errorCount = statuses.filter((status) => status === "error").length;
  const warningCount = statuses.filter((status) => status === "warning").length;
  const unknownCount = statuses.filter((status) => status === "unknown").length;
  const healthyCount = statuses.filter((status) => status === "healthy").length;
  const scanning = starting || restoring || operation?.status === "queued" || operation?.status === "running";
  const overall = error
    ? { tone: "danger" as const, kind: "danger", pill: "诊断失败", title: "诊断未完成", detail: "没有生成新的诊断结论，请查看错误后重试。" }
    : scanning
      ? { tone: "info" as const, kind: "scanning", pill: "真实诊断中", title: "正在检查服务器", detail: "正在逐项读取 Server Agent、文件系统和历史采样。" }
      : !run
        ? { tone: "neutral" as const, kind: "unknown", pill: "等待诊断", title: "尚未运行诊断", detail: "点击“开始诊断”后读取 11 项真实检查结果。" }
        : errorCount > 0
          ? { tone: "danger" as const, kind: "danger", pill: `${errorCount} 项异常`, title: "发现需要处理的异常", detail: `11 项检查已完成：${healthyCount} 项正常、${errorCount} 项异常、${warningCount} 项需关注、${unknownCount} 项不可用。` }
          : warningCount > 0
            ? { tone: "warning" as const, kind: "warning", pill: `${warningCount} 项需关注`, title: "诊断完成，有项目需关注", detail: `11 项检查已完成：${healthyCount} 项正常、${warningCount} 项需关注、${unknownCount} 项不可用。` }
            : unknownCount > 0
              ? { tone: "info" as const, kind: "unknown", pill: `${unknownCount} 项不可用`, title: "诊断完成，部分能力不可用", detail: `11 项检查已完成；${unknownCount} 项因探测能力或真实数据不足无法判断。` }
              : { tone: "success" as const, kind: "success", pill: "全部正常", title: "服务器检查未发现异常", detail: "11 项真实检查均已完成且状态正常。" };
  const healthIcon = overall.kind === "danger" || overall.kind === "warning" ? <ShieldAlert /> : overall.kind === "success" ? <ShieldCheck /> : overall.kind === "scanning" ? <LoaderCircle className="diagnostics-spin" /> : <ShieldQuestion />;

  return (
    <div className="diagnostics-page" aria-busy={scanning}>
      <header className="diagnostics-page__heading">
        <div><div className="diagnostics-page__title-row"><h2>服务器诊断</h2><StatusPill tone={overall.tone} compact>{overall.pill}</StatusPill></div><p>检查服务器运行环境、连接和数据状态，并输出可追溯的真实中文结果。</p></div>
        <Button variant="primary" size="lg" onClick={() => void startDiagnostics()} disabled={scanning}>{scanning ? <LoaderCircle className="diagnostics-spin" size={18} aria-hidden="true" /> : <RefreshCw size={18} aria-hidden="true" />}{scanning ? "诊断中…" : run || error ? "重新诊断" : "开始诊断"}</Button>
      </header>
      {error && <div className="diagnostics-error" role="alert"><CircleX size={20} aria-hidden="true" /><div><strong>无法完成真实诊断</strong><span>{error}</span></div></div>}
      <Card className={cn("diagnostics-health", `diagnostics-health--${overall.kind}`)} aria-live="polite">
        <span className="diagnostics-health__shield" aria-hidden="true">{healthIcon}</span>
        <div><h3>{overall.title}</h3><p>{overall.detail}</p></div>
        <span className="diagnostics-health__time">{scanning ? operation?.currentStep === "running-diagnostics" ? "逐项检查中" : "任务准备中" : formatClock(run?.completedAt)}</span>
      </Card>
      <div className="diagnostics-grid">
        <DiagnosticGroup title="服务与连接" definitions={connectionChecks} items={itemMap} scanning={scanning} />
        <DiagnosticGroup title="运行环境" definitions={environmentChecks} items={itemMap} scanning={scanning} />
        <RuntimeGroup items={itemMap} scanning={scanning} />
      </div>
    </div>
  );
}
