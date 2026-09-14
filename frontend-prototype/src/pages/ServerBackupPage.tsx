import {
  Activity,
  CircleCheck,
  Clock3,
  FileArchive,
  HardDrive,
  LoaderCircle,
  Play,
  Power,
  RefreshCw,
  RotateCcw,
  Save,
  ShieldAlert,
} from "lucide-react";
import { useEffect, useMemo, useRef, useState } from "react";
import { Button, Card, StatusPill } from "../components/ui";
import { apiFetch, createIdempotencyKey } from "../lib/api";
import { cn } from "../lib/utils";
import type { ServerBackupRecord } from "../types";

type LoadStatus = "loading" | "ready" | "unavailable" | "error";
type RestoreOperation = {
  operationId: string;
  status: "queued" | "running" | "succeeded" | "failed";
  progressPercent: number | null;
  currentStep: string | null;
  error: { message?: string; code?: string } | null;
};

const restoreSteps = [
  { title: "当前存档备份", detail: "先为当前存档建立可验证的安全副本。", icon: Save, tone: "orange" },
  { title: "安全停服", detail: "保存数据并确认服务端已经完全停止。", icon: Power, tone: "blue" },
  { title: "恢复备份", detail: "验证目标文件后替换对应的世界数据。", icon: RotateCcw, tone: "blue" },
  { title: "启动服务器", detail: "重新启动服务器并等待初始化完成。", icon: Play, tone: "green" },
  { title: "健康检查", detail: "确认服务、RCON 与存档加载状态正常。", icon: Activity, tone: "purple" },
] as const;

function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}

function isBackupRecord(value: unknown): value is ServerBackupRecord {
  return isObject(value) && typeof value.backupId === "string" && typeof value.createdAt === "string" &&
    typeof value.sizeBytes === "number" && typeof value.status === "string";
}

async function readApiError(response: Response) {
  try {
    const body: unknown = await response.json();
    if (isObject(body) && isObject(body.error) && typeof body.error.message === "string") return body.error.message;
  } catch {
    // Fall through to the status-based message.
  }
  return `读取备份记录失败（HTTP ${response.status}）`;
}

function formatBytes(bytes: number) {
  if (!Number.isFinite(bytes) || bytes < 0) return "大小未知";
  const units = ["B", "KB", "MB", "GB", "TB"];
  let value = bytes;
  let unit = 0;
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024;
    unit += 1;
  }
  const digits = unit === 0 ? 0 : value >= 100 ? 0 : value >= 10 ? 1 : 2;
  return `${value.toFixed(digits)} ${units[unit]}`;
}

function exactTime(value: string) {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return "时间未知";
  return new Intl.DateTimeFormat("zh-CN", {
    year: "numeric", month: "2-digit", day: "2-digit", hour: "2-digit", minute: "2-digit", second: "2-digit", hour12: false,
  }).format(date).replaceAll("/", "-");
}

function relativeTime(value: string) {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return "时间未知";
  const seconds = Math.max(0, Math.floor((Date.now() - date.getTime()) / 1000));
  if (seconds < 60) return "刚刚";
  if (seconds < 3600) return `${Math.floor(seconds / 60)} 分钟前`;
  if (seconds < 86400) return `${Math.floor(seconds / 3600)} 小时前`;
  if (seconds < 86400 * 7) return `${Math.floor(seconds / 86400)} 天前`;
  return exactTime(value).slice(0, 10);
}

function recordStatus(record: ServerBackupRecord) {
  if (record.status === "available") return { label: "可用", tone: "available" };
  if (record.status === "incomplete") return { label: "文件为空", tone: "warning" };
  return { label: "无法读取", tone: "error" };
}

export function ServerBackupPage({ onNotify }: { onNotify: (message: string) => void }) {
  const [records, setRecords] = useState<ServerBackupRecord[]>([]);
  const [total, setTotal] = useState(0);
  const [totalSizeBytes, setTotalSizeBytes] = useState(0);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [loadStatus, setLoadStatus] = useState<LoadStatus>("loading");
  const [loadError, setLoadError] = useState<string | null>(null);
  const [refreshing, setRefreshing] = useState(false);
  const [refreshVersion, setRefreshVersion] = useState(0);
  const [loadedAt, setLoadedAt] = useState<Date | null>(null);
  const hasLoadedRef = useRef(false);
  const [restoreOperationId, setRestoreOperationId] = useState<string | null>(null);
  const [restoreOperation, setRestoreOperation] = useState<RestoreOperation | null>(null);
  const [submittingRestore, setSubmittingRestore] = useState(false);
  const notifiedRestore = useRef<string | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    const isRefresh = hasLoadedRef.current;
    if (isRefresh) setRefreshing(true);
    else setLoadStatus("loading");

    const load = async () => {
      try {
        const response = await apiFetch("/api/v1/servers/local/backups?limit=100", {
          headers: { Accept: "application/json" },
          signal: controller.signal,
        });
        if (!response.ok) {
          const message = await readApiError(response);
          setRecords([]);
          setTotal(0);
          setTotalSizeBytes(0);
          setSelectedId(null);
          setLoadStatus(response.status === 503 ? "unavailable" : "error");
          setLoadError(message);
          if (isRefresh) onNotify(message);
          return;
        }

        const body: unknown = await response.json();
        if (!isObject(body) || !Array.isArray(body.items)) throw new Error("备份接口返回的数据格式无效");
        const items = body.items.filter(isBackupRecord);
        if (items.length !== body.items.length) throw new Error("备份接口返回了无法识别的记录");
        const nextTotal = typeof body.total === "number" ? body.total : items.length;
        const nextTotalSize = typeof body.totalSizeBytes === "number"
          ? body.totalSizeBytes
          : items.reduce((sum, item) => sum + item.sizeBytes, 0);

        setRecords(items);
        setTotal(nextTotal);
        setTotalSizeBytes(nextTotalSize);
        setSelectedId((current) => current && items.some((item) => item.backupId === current) ? current : items[0]?.backupId ?? null);
        setLoadStatus("ready");
        setLoadError(null);
        setLoadedAt(new Date());
        hasLoadedRef.current = true;
        if (isRefresh) onNotify(`已重新读取 ${nextTotal} 个真实备份文件`);
      } catch (caught) {
        if (caught instanceof DOMException && caught.name === "AbortError") return;
        const message = caught instanceof Error ? caught.message : "无法读取服务器自动备份";
        setLoadStatus("error");
        setLoadError(message);
        if (isRefresh) onNotify(message);
      } finally {
        setRefreshing(false);
      }
    };

    void load();
    return () => controller.abort();
  }, [onNotify, refreshVersion]);

  useEffect(() => {
    if (!restoreOperationId) return;
    const controller = new AbortController();
    let timer: number | undefined;
    const poll = async () => {
      try {
        const response = await apiFetch(`/api/v1/operations/${encodeURIComponent(restoreOperationId)}`, {
          headers: { Accept: "application/json" }, signal: controller.signal,
        });
        if (!response.ok) throw new Error(await readApiError(response));
        const operation = await response.json() as RestoreOperation;
        setRestoreOperation(operation);
        if (operation.status === "queued" || operation.status === "running") {
          timer = window.setTimeout(() => void poll(), 750);
          return;
        }
        if (notifiedRestore.current !== operation.operationId) {
          notifiedRestore.current = operation.operationId;
          if (operation.status === "succeeded") {
            onNotify("备份恢复、健康检查和原运行状态恢复均已完成");
            setRefreshVersion((value) => value + 1);
          } else onNotify(operation.error?.message ?? "备份恢复失败");
        }
      } catch (caught) {
        if (caught instanceof DOMException && caught.name === "AbortError") return;
        onNotify(caught instanceof Error ? caught.message : "无法读取备份恢复进度");
      }
    };
    void poll();
    return () => { controller.abort(); if (timer !== undefined) window.clearTimeout(timer); };
  }, [onNotify, restoreOperationId]);

  const selectedBackup = useMemo(
    () => records.find((record) => record.backupId === selectedId) ?? null,
    [records, selectedId],
  );
  const latestBackup = records[0] ?? null;
  const restoreRunning = restoreOperation?.status === "queued" || restoreOperation?.status === "running";

  const startRestore = async () => {
    if (!selectedBackup || selectedBackup.status !== "available") return;
    if (!window.confirm(`确定恢复备份“${selectedBackup.fileName}”吗？系统会先安全停服并创建完整恢复前安全点。`)) return;
    setSubmittingRestore(true);
    try {
      const response = await apiFetch(`/api/v1/servers/local/backups/${encodeURIComponent(selectedBackup.backupId)}/restore`, {
        method: "POST",
        headers: { Accept: "application/json", "Content-Type": "application/json", "Idempotency-Key": createIdempotencyKey("backup-restore") },
        body: JSON.stringify({ confirmation: `RESTORE ${selectedBackup.backupId}` }),
      });
      if (!response.ok) throw new Error(await readApiError(response));
      const accepted = await response.json() as { operationId: string };
      setRestoreOperationId(accepted.operationId);
      setRestoreOperation({ operationId: accepted.operationId, status: "queued", progressPercent: 0, currentStep: "validating-backup", error: null });
    } catch (caught) {
      onNotify(caught instanceof Error ? caught.message : "无法提交备份恢复");
    } finally {
      setSubmittingRestore(false);
    }
  };
  const statusPresentation = loadStatus === "ready"
    ? { tone: "success" as const, label: records.length > 0 ? "真实备份已读取" : "等待自动备份" }
    : loadStatus === "loading"
      ? { tone: "info" as const, label: "正在读取" }
      : loadStatus === "unavailable"
        ? { tone: "neutral" as const, label: "备份不可用" }
        : { tone: "danger" as const, label: "读取失败" };
  const readStatus = loadStatus === "loading" ? "读取中" : loadStatus === "ready" ? (records.length > 0 ? "正常" : "暂无文件") : "不可用";

  return (
    <div className="backup-page">
      <header className="backup-page__heading">
        <div>
          <div className="backup-page__title-row">
            <h2>存档备份</h2>
            <StatusPill tone={statusPresentation.tone} compact>{statusPresentation.label}</StatusPill>
          </div>
          <p>只读扫描 Avorion 服务器自动生成的真实备份文件。</p>
        </div>
      </header>

      <Card className="backup-summary" aria-label="存档备份摘要" aria-busy={loadStatus === "loading"}>
        <div className="backup-summary__item">
          <span className="backup-summary__icon backup-summary__icon--blue" aria-hidden="true"><FileArchive size={23} /></span>
          <div><span>备份文件</span><strong>{loadStatus === "loading" ? "—" : total}</strong></div>
        </div>
        <div className="backup-summary__item">
          <span className="backup-summary__icon backup-summary__icon--green" aria-hidden="true"><Clock3 size={23} /></span>
          <div><span>最新备份</span><strong>{latestBackup ? relativeTime(latestBackup.createdAt) : "暂无"}</strong></div>
        </div>
        <div className="backup-summary__item">
          <span className="backup-summary__icon backup-summary__icon--purple" aria-hidden="true"><HardDrive size={23} /></span>
          <div><span>占用空间</span><strong>{loadStatus === "loading" ? "—" : formatBytes(totalSizeBytes)}</strong></div>
        </div>
        <div className="backup-summary__item">
          <span className="backup-summary__icon backup-summary__icon--green" aria-hidden="true"><CircleCheck size={23} /></span>
          <div><span>读取状态</span><strong>{readStatus}</strong></div>
        </div>
      </Card>

      <div className="backup-workspace">
        <Card className="backup-list-card" aria-labelledby="backup-records-title" aria-busy={loadStatus === "loading" || refreshing}>
          <div className="backup-list-card__header">
            <div>
              <h3 id="backup-records-title">备份记录</h3>
              <p>{loadStatus === "ready" ? `已读取 ${total} 个真实 .bak 文件` : loadError ?? "正在定位服务器备份目录"}{loadedAt && ` · ${relativeTime(loadedAt.toISOString())}刷新`}</p>
            </div>
            <Button className="backup-refresh" variant="ghost" size="sm" onClick={() => setRefreshVersion((value) => value + 1)} disabled={refreshing || loadStatus === "loading"}>
              <RefreshCw className={cn(refreshing && "spin")} size={16} />
              {refreshing ? "正在读取" : "刷新记录"}
            </Button>
          </div>

          <div className="backup-table-wrap">
            <table className="backup-table">
              <thead><tr><th><span className="sr-only">选择</span></th><th>备份时间</th><th>文件大小</th><th>状态</th></tr></thead>
              <tbody>
                {records.map((record) => {
                  const selected = selectedId === record.backupId;
                  const status = recordStatus(record);
                  return (
                    <tr key={record.backupId} className={cn(selected && "backup-table__row--selected")} onClick={() => setSelectedId(record.backupId)}>
                      <td><input type="radio" name="backup-record" aria-label={`选择 ${exactTime(record.createdAt)} 的备份`} checked={selected} onChange={() => setSelectedId(record.backupId)} /></td>
                      <td><span className="backup-table__time"><strong>{relativeTime(record.createdAt)}</strong><small>（{exactTime(record.createdAt)} Asia/Shanghai）</small></span></td>
                      <td>{formatBytes(record.sizeBytes)}</td>
                      <td><span className={cn("backup-table__status", `backup-table__status--${status.tone}`)}><span aria-hidden="true" />{status.label}</span></td>
                    </tr>
                  );
                })}
                {records.length === 0 && (
                  <tr className="backup-table__empty"><td colSpan={4}>
                    {loadStatus === "loading" && <><LoaderCircle className="spin" size={20} /><strong>正在读取真实备份目录</strong><span>请稍候，不会使用示例记录填充页面。</span></>}
                    {loadStatus === "ready" && <><FileArchive size={22} /><strong>尚未生成自动备份</strong><span>Avorion 生成第一个 .bak 文件后会显示在这里。</span></>}
                    {(loadStatus === "unavailable" || loadStatus === "error") && <><ShieldAlert size={22} /><strong>无法读取备份目录</strong><span>{loadError ?? "请先完成服务器配置"}</span><Button variant="secondary" size="sm" onClick={() => setRefreshVersion((value) => value + 1)}>重新读取</Button></>}
                  </td></tr>
                )}
              </tbody>
            </table>
          </div>

          <p className="backup-retention-note"><CircleCheck size={15} />仅显示 Server Agent 实际读取到的 Avorion .bak 文件</p>
        </Card>

        <Card className="backup-restore-card" aria-labelledby="restore-backup-title">
          <h3 id="restore-backup-title">恢复此备份</h3>
          <dl className="backup-selection-summary">
            <div><dt>备份时间</dt><dd>{selectedBackup ? relativeTime(selectedBackup.createdAt) : "未选择"}</dd></div>
            <div><dt>文件大小</dt><dd>{selectedBackup ? formatBytes(selectedBackup.sizeBytes) : "—"}</dd></div>
            <div><dt>状态</dt><dd className={cn(selectedBackup?.status === "available" && "text-success")}><span />{selectedBackup ? recordStatus(selectedBackup).label : "暂无备份"}</dd></div>
          </dl>

          <ol className="backup-restore-flow">
            {restoreSteps.map((step, index) => {
              const Icon = step.icon;
              return <li key={step.title}><span className="backup-restore-flow__number">{index + 1}</span><span className={cn("backup-restore-flow__icon", `backup-restore-flow__icon--${step.tone}`)} aria-hidden="true"><Icon size={18} /></span><div><strong>{step.title}</strong><span>{step.detail}</span></div></li>;
            })}
          </ol>

          <div className="backup-risk-note"><ShieldAlert size={18} /><span>恢复会改写世界数据；执行前自动创建服务端与 Galaxy 的全量校验安全点。</span></div>
          {restoreRunning && <div className="update-operation-progress" aria-live="polite"><div><span>{restoreOperation?.currentStep ?? "正在恢复备份"}</span><strong>{Math.round(restoreOperation?.progressPercent ?? 0)}%</strong></div><progress max={100} value={restoreOperation?.progressPercent ?? 0} /><small>Operation ID：{restoreOperation?.operationId}</small></div>}
          {restoreOperation?.status === "failed" && <div className="memory-policy-error" role="alert">{restoreOperation.error?.message ?? "备份恢复失败"}（{restoreOperation.error?.code ?? "UNKNOWN"}）</div>}
          <Button className="backup-restore-button" variant="danger" onClick={() => void startRestore()} disabled={!selectedBackup || selectedBackup.status !== "available" || submittingRestore || restoreRunning}>
            {submittingRestore || restoreRunning ? <LoaderCircle className="spin" size={18} /> : <RotateCcw size={18} />}{submittingRestore ? "正在提交" : restoreRunning ? "正在安全恢复" : "恢复所选备份"}
          </Button>
        </Card>
      </div>
    </div>
  );
}
