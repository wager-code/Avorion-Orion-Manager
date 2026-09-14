import { AlertCircle, FileText, ListChecks, LoaderCircle, RefreshCw, Search } from "lucide-react";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Button, Card, StatusPill } from "../components/ui";
import { apiFetch } from "../lib/api";
import { cn } from "../lib/utils";
import type { ApiOperation, ServerLogEntry } from "../types";

type LogCategory = "Error" | "Warning" | "Player" | "MOD" | "RCON" | "Save";
type LogFilter = "全部" | LogCategory;
type StreamStatus = "loading" | "live" | "reconnecting" | "unavailable" | "error";
type TimestampPart = "year" | "month" | "day" | "hour" | "minute" | "second";

const filters: LogFilter[] = ["全部", "Error", "Warning", "Player", "MOD", "RCON", "Save"];
const maximumEntries = 500;

const operationTypeLabels: Record<string, string> = {
  "steamcmd.install": "安装 SteamCMD",
  "avorion.install": "安装 Avorion 服务端",
  "server.configure": "应用服务器配置",
  "server.initialize": "初始化服务器",
  "server.start": "启动服务器",
  "server.save": "保存世界",
  "server.shutdown": "安全停服",
  "server.restart": "安全重启",
  "avorion.update.check": "检查官方更新",
  "avorion.files.verify": "验证服务端文件",
  "avorion.update.rollback-point.create": "创建更新安全点",
  diagnostics: "服务器诊断",
};

const operationStatusLabels: Record<ApiOperation["status"], string> = {
  queued: "等待执行", running: "执行中", succeeded: "成功", failed: "失败", cancelled: "已取消",
};

function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}

function isLogEntry(value: unknown): value is ServerLogEntry {
  return isObject(value) && typeof value.id === "string" && typeof value.occurredAt === "string" &&
    typeof value.category === "string" && typeof value.rawLine === "string" &&
    typeof value.sourceFile === "string" && typeof value.sourceLineNumber === "number" &&
    typeof value.sourceLastWriteAt === "string";
}

async function readApiError(response: Response) {
  try {
    const payload = await response.json() as { error?: { message?: string }; message?: string };
    return payload.error?.message ?? payload.message ?? `请求失败（HTTP ${response.status}）`;
  } catch {
    return `请求失败（HTTP ${response.status}）`;
  }
}

function mergeEntries(current: ServerLogEntry[], incoming: ServerLogEntry[]) {
  const byId = new Map(current.map((entry) => [entry.id, entry]));
  for (const entry of incoming) byId.set(entry.id, entry);
  return Array.from(byId.values())
    .sort((left, right) => {
      const occurredDifference = new Date(right.occurredAt).getTime() - new Date(left.occurredAt).getTime();
      if (occurredDifference !== 0) return occurredDifference;
      const sourceDifference = new Date(right.sourceLastWriteAt).getTime() - new Date(left.sourceLastWriteAt).getTime();
      if (sourceDifference !== 0) return sourceDifference;
      const fileDifference = right.sourceFile.localeCompare(left.sourceFile);
      return fileDifference !== 0 ? fileDifference : right.sourceLineNumber - left.sourceLineNumber;
    })
    .slice(0, maximumEntries);
}

function formatTimestamp(value: string) {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return "时间未知";
  const parts = new Intl.DateTimeFormat("zh-CN", {
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit",
    hour12: false,
  }).formatToParts(date);
  const part = (type: TimestampPart) => parts.find((item) => item.type === type)?.value ?? "00";
  return `${part("year")}-${part("month")}-${part("day")} ${part("hour")}:${part("minute")}:${part("second")}`;
}

function displayMessage(entry: ServerLogEntry) {
  const message = entry.message || entry.rawLine;
  const content = message.replace(/^\d{4}-\d{2}-\d{2}[T ]\d{2}[:-]\d{2}[:-]\d{2}(?:\.\d{1,3})?\s*\|?\s*/, "").trim();
  return content || "（空日志行）";
}

function displayCategory(category: string) {
  return category === "Other" ? "其他" : category;
}

function categoryClass(category: string) {
  const normalized = category.toLocaleLowerCase();
  return ["error", "warning", "player", "mod", "rcon", "save"].includes(normalized) ? normalized : "other";
}

export function ServerLogsPage({ onNotify }: { onNotify: (message: string) => void }) {
  const [logs, setLogs] = useState<ServerLogEntry[]>([]);
  const [filter, setFilter] = useState<LogFilter>("全部");
  const [query, setQuery] = useState("");
  const [autoScroll, setAutoScroll] = useState(true);
  const [copied, setCopied] = useState(false);
  const [streamStatus, setStreamStatus] = useState<StreamStatus>("loading");
  const [loadError, setLoadError] = useState<string | null>(null);
  const [retryVersion, setRetryVersion] = useState(0);
  const [operations, setOperations] = useState<ApiOperation[]>([]);
  const [operationError, setOperationError] = useState<string | null>(null);
  const streamRef = useRef<HTMLDivElement>(null);
  const autoScrollRef = useRef(autoScroll);

  useEffect(() => { autoScrollRef.current = autoScroll; }, [autoScroll]);

  const loadOperations = useCallback(async () => {
    try {
      const response = await apiFetch("/api/v1/operations?limit=30", { headers: { Accept: "application/json" } });
      if (!response.ok) throw new Error(await readApiError(response));
      const payload = await response.json() as { items?: ApiOperation[] };
      if (!Array.isArray(payload.items)) throw new Error("管理操作接口返回的数据格式无效");
      setOperations(payload.items);
      setOperationError(null);
    } catch (caught) {
      setOperationError(caught instanceof Error ? caught.message : "无法读取管理操作历史");
    }
  }, []);

  useEffect(() => {
    void loadOperations();
    const timer = window.setInterval(() => void loadOperations(), 2000);
    return () => window.clearInterval(timer);
  }, [loadOperations, retryVersion]);

  useEffect(() => {
    let disposed = false;
    let source: EventSource | null = null;
    const controller = new AbortController();

    const connect = async () => {
      setStreamStatus("loading");
      setLoadError(null);
      try {
        const response = await apiFetch(`/api/v1/servers/local/logs?limit=${maximumEntries}`, {
          headers: { Accept: "application/json" },
          signal: controller.signal,
        });
        if (!response.ok) {
          const message = await readApiError(response);
          if (!disposed) {
            setStreamStatus(response.status === 503 ? "unavailable" : "error");
            setLoadError(message);
          }
          return;
        }
        const payload: unknown = await response.json();
        const entries = isObject(payload) && Array.isArray(payload.items) ? payload.items.filter(isLogEntry) : null;
        if (!entries) throw new Error("日志接口返回的数据格式无效");
        if (disposed) return;
        setLogs(entries);

        source = new EventSource("/api/v1/servers/local/logs/stream", { withCredentials: true });
        source.onopen = () => {
          if (disposed) return;
          setStreamStatus("live");
          setLoadError(null);
        };
        source.addEventListener("log", (event) => {
          if (disposed) return;
          try {
            const entry: unknown = JSON.parse((event as MessageEvent<string>).data);
            if (!isLogEntry(entry)) return;
            setLogs((current) => mergeEntries(current, [entry]));
            if (autoScrollRef.current) window.requestAnimationFrame(() => streamRef.current?.scrollTo({ top: 0, behavior: "smooth" }));
          } catch {
            // Ignore only the malformed event; the verified stream stays connected.
          }
        });
        source.onerror = () => {
          if (disposed) return;
          setStreamStatus("reconnecting");
          setLoadError("实时日志连接已中断，浏览器正在自动重连");
        };
      } catch (caught) {
        if (disposed || (caught instanceof DOMException && caught.name === "AbortError")) return;
        setStreamStatus("error");
        setLoadError(caught instanceof Error ? caught.message : "无法读取真实服务器日志");
      }
    };

    void connect();
    return () => {
      disposed = true;
      controller.abort();
      source?.close();
    };
  }, [retryVersion]);

  const visibleLogs = useMemo(() => {
    const normalizedQuery = query.trim().toLocaleLowerCase();
    return logs.filter((log) => {
      const matchesFilter = filter === "全部" || log.category.toLocaleLowerCase() === filter.toLocaleLowerCase();
      const matchesQuery = !normalizedQuery || `${formatTimestamp(log.occurredAt)} ${log.category} ${log.message} ${log.rawLine}`.toLocaleLowerCase().includes(normalizedQuery);
      return matchesFilter && matchesQuery;
    });
  }, [filter, logs, query]);

  const copyVisibleLogs = async () => {
    const content = visibleLogs.map((log) => `${formatTimestamp(log.occurredAt)} [${displayCategory(log.category)}] ${displayMessage(log)}`).join("\n");
    try {
      await navigator.clipboard.writeText(content);
      setCopied(true);
      onNotify(`已复制 ${visibleLogs.length} 条真实日志`);
      window.setTimeout(() => setCopied(false), 1800);
    } catch {
      onNotify("浏览器未允许访问剪贴板");
    }
  };

  const statusPresentation = streamStatus === "live"
    ? { tone: "success" as const, label: "真实日志已连接" }
    : streamStatus === "loading"
      ? { tone: "info" as const, label: "正在连接" }
      : streamStatus === "reconnecting"
        ? { tone: "warning" as const, label: "正在重连" }
        : streamStatus === "unavailable"
          ? { tone: "neutral" as const, label: "日志不可用" }
          : { tone: "danger" as const, label: "读取失败" };
  const connectionLabel = streamStatus === "live" ? "实时接收中" : streamStatus === "loading" ? "正在读取历史日志" : streamStatus === "reconnecting" ? "连接中断，正在重连" : "实时连接未建立";

  return (
    <div className="logs-page">
      <header className="logs-page__heading">
        <div className="logs-page__title-row"><h2>实时日志</h2><StatusPill tone={statusPresentation.tone} compact>{statusPresentation.label}</StatusPill></div>
        <p>读取服务器真实日志文件，并按类别快速定位问题。</p>
      </header>

      <Card className="operation-log-card" aria-labelledby="operation-log-title">
        <div className="operation-log-card__heading"><div><h3 id="operation-log-title"><ListChecks size={18} />管理操作记录</h3><p>安装、初始化、控制、更新检查和诊断的真实后台阶段</p></div><Button variant="ghost" size="sm" onClick={() => void loadOperations()}><RefreshCw size={14} />刷新</Button></div>
        {operationError ? <div className="operation-log-empty operation-log-empty--error"><AlertCircle size={18} /><span>{operationError}</span></div> : operations.length === 0 ? <div className="operation-log-empty"><ListChecks size={18} /><span>还没有管理操作记录</span></div> : <div className="operation-log-list">{operations.map((operation) => {
          const active = operation.status === "queued" || operation.status === "running";
          return <div key={operation.operationId} className={cn("operation-log-row", `operation-log-row--${operation.status}`)}>
            <span className="operation-log-row__status">{active && <LoaderCircle className="spin" size={14} />}{operationStatusLabels[operation.status]}</span>
            <strong>{operationTypeLabels[operation.type] ?? operation.type}</strong>
            <span className="operation-log-row__stage">{operation.error?.message ?? operation.currentStep ?? "—"}</span>
            <span className="operation-log-row__progress">{operation.progressPercent === null ? "—" : `${Math.round(operation.progressPercent)}%`}</span>
            <time dateTime={operation.completedAt ?? operation.startedAt ?? operation.acceptedAt}>{formatTimestamp(operation.completedAt ?? operation.startedAt ?? operation.acceptedAt)}</time>
            <code title={operation.operationId}>{operation.operationId.slice(-10)}</code>
          </div>;
        })}</div>}
      </Card>

      <Card className="logs-console-card" aria-labelledby="logs-console-title" aria-busy={streamStatus === "loading"}>
        <h3 id="logs-console-title" className="sr-only">服务器实时日志</h3>
        <div className="logs-toolbar">
          <label className="logs-search">
            <Search size={18} aria-hidden="true" />
            <span className="sr-only">搜索日志内容</span>
            <input type="search" value={query} onChange={(event) => setQuery(event.target.value)} placeholder="搜索日志内容" />
          </label>
          <label className="logs-auto-scroll">
            <span>自动滚动</span>
            <input type="checkbox" checked={autoScroll} onChange={(event) => setAutoScroll(event.target.checked)} />
            <span className="logs-switch" aria-hidden="true"><span /></span>
          </label>
          <Button variant="primary" size="lg" onClick={() => void copyVisibleLogs()} disabled={visibleLogs.length === 0}>{copied ? "已复制" : "复制可见日志"}</Button>
        </div>

        <div className="logs-filter" aria-label="日志类别过滤">
          {filters.map((item) => <button key={item} type="button" className={cn("logs-filter__button", filter === item && "logs-filter__button--active")} aria-pressed={filter === item} onClick={() => setFilter(item)}>{item}</button>)}
        </div>

        <div className="logs-console" ref={streamRef} role="log" aria-live="off">
          {visibleLogs.length > 0 ? (
            <div className="logs-console__rows">
              {visibleLogs.map((log) => {
                const category = categoryClass(log.category);
                return (
                  <div key={log.id} className={cn("logs-row", `logs-row--${category}`)}>
                    <time dateTime={log.occurredAt}>{formatTimestamp(log.occurredAt)}</time>
                    <span className={cn("logs-category", `logs-category--${category}`)}>{displayCategory(log.category)}</span>
                    <span className="logs-message">{displayMessage(log)}</span>
                  </div>
                );
              })}
            </div>
          ) : (
            <div className={cn("logs-empty", loadError && "logs-empty--error")} role="status">
              {streamStatus === "loading" ? <LoaderCircle className="spin" size={23} aria-hidden="true" /> : loadError ? <AlertCircle size={23} aria-hidden="true" /> : query || filter !== "全部" ? <Search size={23} aria-hidden="true" /> : <FileText size={23} aria-hidden="true" />}
              <strong>{streamStatus === "loading" ? "正在读取真实服务器日志" : loadError ? "无法读取真实服务器日志" : query || filter !== "全部" ? "没有符合条件的日志" : "日志文件中暂无可读取内容"}</strong>
              <span>{streamStatus === "loading" ? "正在连接本机 Server Agent…" : loadError ?? (query || filter !== "全部" ? "请调整搜索关键词或日志类别。" : "新日志写入后会自动显示在这里。")}</span>
              {loadError && streamStatus !== "reconnecting" && <Button variant="secondary" size="sm" onClick={() => setRetryVersion((value) => value + 1)}>重新读取</Button>}
            </div>
          )}

          <div className="logs-console__status" role="status" aria-live="polite" aria-atomic="true">
            <span><i className={cn("logs-status-dot", streamStatus === "live" ? "logs-status-dot--live" : streamStatus === "loading" || streamStatus === "reconnecting" ? "logs-status-dot--connecting" : "logs-status-dot--offline")} />{connectionLabel}</span>
            <span className={cn(!autoScroll && "logs-console__status--paused")}><i className="logs-status-dot logs-status-dot--scroll" />{autoScroll ? "新日志将自动滚动到顶部" : "自动滚动已关闭"}</span>
          </div>
        </div>
      </Card>
    </div>
  );
}
