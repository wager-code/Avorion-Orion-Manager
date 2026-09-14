import {
  Activity, AlertTriangle, Box, Check, CirclePlay, Clock3, DatabaseBackup, Download,
  FileText, Info, Power, RefreshCw, Save, Server, ShieldAlert, TerminalSquare,
  UserRoundPlus, Users, Wifi, Wrench,
} from "lucide-react";
import { useCallback, useEffect, useRef, useState, type ReactNode } from "react";
import { useNavigate } from "react-router-dom";
import { Button, Card, ConfirmDialog, IconBox } from "../components/ui";
import { apiFetch, createIdempotencyKey } from "../lib/api";
import { cn } from "../lib/utils";
import type { ApiOperation, ServerAction, ServerEvent, ServerLifecycle, ServerStatus } from "../types";

const operationStorageKey = "avorion.control.active-operation";
type ControlAction = Extract<ServerAction, "start" | "save" | "shutdown" | "restart" | "force-stop">;

const actionLabels: Record<ControlAction, string> = {
  start: "启动服务器", save: "保存世界", shutdown: "安全关闭", restart: "安全重启", "force-stop": "强制终止",
};

const stepLabels: Record<string, string> = {
  "validating-managed-profile": "验证受控启动档案",
  "resolving-managed-process": "确认受管服务端进程",
  "starting-managed-server": "启动 Avorion 服务端",
  "waiting-for-rcon": "等待本机 RCON 健康检查",
  "verifying-server-stability": "RCON 已连接，正在确认服务端持续稳定运行",
  "authenticating-rcon": "通过本机 RCON 认证",
  "save-command-acknowledged": "服务端已确认保存命令",
  "saving-before-stop": "停服前保存世界",
  "requesting-safe-stop": "请求服务端安全停止",
  "waiting-for-process-exit": "等待服务端进程退出",
  "safe-stop-confirmed": "已确认服务端安全退出",
  "rcon-authenticated": "RCON 健康检查通过",
  "restart-rcon-authenticated": "重启后 RCON 健康检查通过",
  "already-running-verified": "服务器已运行并通过验证",
  "already-stopped-verified": "服务器已处于停止状态",
  "force-stopping-exact-process": "正在终止已确认的受管进程",
  "force-stop-confirmed": "已确认受管进程退出",
};

function controlStatus(status: ServerLifecycle) {
  if (status === "running") return { title: "正常运行", subtitle: "受管进程已确认", tone: "blue" };
  if (status === "stopped") return { title: "已停止", subtitle: "未发现受管进程", tone: "gray" };
  if (status === "starting") return { title: "正在启动", subtitle: "等待健康检查", tone: "blue" };
  if (status === "stopping") return { title: "正在关闭", subtitle: "正在保存并停服", tone: "orange" };
  if (status === "restarting") return { title: "正在重启", subtitle: "正在安全重启", tone: "orange" };
  return { title: "状态未知", subtitle: "等待 Server Agent", tone: "gray" };
}

function formatDuration(seconds: number | null) {
  if (seconds === null) return "不可用";
  const days = Math.floor(seconds / 86_400);
  const hours = Math.floor((seconds % 86_400) / 3_600);
  const minutes = Math.floor((seconds % 3_600) / 60);
  return [days > 0 ? `${days} 天` : null, hours > 0 ? `${hours} 小时` : null, `${minutes} 分钟`].filter(Boolean).join(" ");
}

function formatTime(value: string | null) {
  if (!value) return "暂无记录";
  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) return "时间不可用";
  return new Intl.DateTimeFormat("zh-CN", { month: "2-digit", day: "2-digit", hour: "2-digit", minute: "2-digit" }).format(parsed);
}

function reasonText(reason: string | null | undefined, fallback: string) {
  const reasons: Record<string, string> = {
    SERVER_NOT_RUNNING: "服务器未运行",
    RCON_AUTHENTICATION_FAILED: "本机 RCON 认证失败",
    STEAM_QUERY_PROTOCOL_NOT_IMPLEMENTED: "Steam Query 探测尚未接入",
    STEAM_QUERY_TIMEOUT: "UDP 端口已监听，但 A2S_INFO 查询超时",
    STEAM_QUERY_RESPONSE_INVALID: "Steam Query 返回了无法识别的数据",
    STEAM_QUERY_UDP_NOT_LISTENING: "Steam Query UDP 端口没有监听",
    STEAM_QUERY_PORT_UNKNOWN: "当前启动日志没有 Steam Query 端口证据",
    NO_SAVE_FILES_FOUND: "未找到可识别的存档文件",
    SAVE_PATH_UNREADABLE: "Server Agent 无法读取存档目录",
    MANAGED_PROFILE_MISSING: "尚未完成服务器配置",
    MANAGED_PROFILE_INVALID: "受控启动档案验证失败",
  };
  return reason ? reasons[reason] ?? reason : fallback;
}

function sourceText(source: string | null | undefined) {
  if (source === "filesystem") return "服务器文件系统";
  if (source === "managed-profile+rcon") return "受控启动档案 + 本机 RCON";
  return source ?? "来源不可用";
}

async function readApiError(response: Response) {
  const body = await response.json().catch(() => null) as { error?: { message?: string } } | null;
  return body?.error?.message || `请求失败（HTTP ${response.status}）`;
}

function actionFromOperation(operation: ApiOperation | null): ControlAction | null {
  if (!operation) return null;
  const action = operation.type.split(".").at(-1);
  return action === "start" || action === "save" || action === "shutdown" || action === "restart" || action === "force-stop" ? action : null;
}

function StatusCard({ label, value, detail, tone, icon, healthy = true }: {
  label: string; value: string; detail: string; tone: string; icon: ReactNode; healthy?: boolean;
}) {
  return (
    <Card className="control-status-card">
      <IconBox tone={tone}>{icon}</IconBox>
      <div className="control-status-card__copy"><span>{label}</span><strong>{value}</strong><small title={detail}>{detail}</small></div>
      <span className={cn("control-status-card__health", healthy && "control-status-card__health--ok")} aria-label={healthy ? "状态正常" : "状态不可用"}>
        {healthy ? <Check size={15} strokeWidth={3} /> : <span aria-hidden="true">—</span>}
      </span>
    </Card>
  );
}

function ActionTile({ icon, title, description, tone = "blue", disabled, busy, onClick }: {
  icon: ReactNode; title: string; description: string; tone?: "blue" | "red" | "green" | "orange";
  disabled?: boolean; busy?: boolean; onClick: () => void;
}) {
  return (
    <button type="button" className={cn("action-tile", `action-tile--${tone}`)} disabled={disabled || busy} onClick={onClick}>
      <span className="action-tile__icon" aria-hidden="true">{busy ? <RefreshCw className="spin" size={27} /> : icon}</span>
      <span className="action-tile__copy"><strong>{title}</strong><small>{busy ? "正在执行真实操作" : description}</small></span>
    </button>
  );
}

function eventIcon(kind: ServerEvent["kind"]) {
  if (kind === "success") return <Check size={14} strokeWidth={3} />;
  if (kind === "warning") return <AlertTriangle size={15} strokeWidth={2.4} />;
  if (kind === "player") return <UserRoundPlus size={15} strokeWidth={2.5} />;
  return <Info size={15} strokeWidth={2.5} />;
}

export function ServerControlPage({ status, onStatusRefresh, onNotify }: {
  status: ServerStatus | null; onStatusRefresh: () => Promise<void>; onNotify: (message: string) => void;
}) {
  const navigate = useNavigate();
  const [confirm, setConfirm] = useState<"restart" | "shutdown" | "force-stop" | null>(null);
  const [operationId, setOperationId] = useState<string | null>(() => {
    try { return window.sessionStorage.getItem(operationStorageKey); } catch { return null; }
  });
  const [operation, setOperation] = useState<ApiOperation | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const [events, setEvents] = useState<ServerEvent[]>([]);
  const [eventsUnavailable, setEventsUnavailable] = useState<string | null>(null);
  const notifiedOperation = useRef<string | null>(null);

  const loadEvents = useCallback(async () => {
    try {
      const response = await apiFetch("/api/v1/servers/local/events?limit=6", { headers: { Accept: "application/json" } });
      if (!response.ok) throw new Error(await readApiError(response));
      const body = await response.json() as { items: Array<{ eventId: string; occurredAt: string; kind: ServerEvent["kind"]; message: string }> };
      setEvents(body.items.map((item) => ({ id: item.eventId, time: formatTime(item.occurredAt), kind: item.kind, message: item.message })));
      setEventsUnavailable(null);
    } catch (caught) {
      setEvents([]);
      setEventsUnavailable(caught instanceof Error ? caught.message : "无法读取服务端事件");
    }
  }, []);

  useEffect(() => {
    void loadEvents();
    const timer = window.setInterval(() => void loadEvents(), 5000);
    return () => window.clearInterval(timer);
  }, [loadEvents]);

  useEffect(() => {
    if (!operationId) return;
    let disposed = false;
    let timer: number | null = null;
    const poll = async () => {
      try {
        const response = await apiFetch(`/api/v1/operations/${encodeURIComponent(operationId)}`, { headers: { Accept: "application/json" } });
        if (!response.ok) throw new Error(await readApiError(response));
        const current = await response.json() as ApiOperation;
        if (disposed) return;
        setOperation(current);
        setActionError(current.status === "failed" ? current.error?.message ?? "服务器操作失败" : null);
        if (current.status === "queued" || current.status === "running") {
          timer = window.setTimeout(() => void poll(), 600);
          return;
        }
        try { window.sessionStorage.removeItem(operationStorageKey); } catch { }
        await onStatusRefresh();
        await loadEvents();
        if (notifiedOperation.current !== current.operationId) {
          notifiedOperation.current = current.operationId;
          const action = actionFromOperation(current);
          onNotify(current.status === "succeeded" ? `${action ? actionLabels[action] : "服务器操作"}已完成` : current.error?.message ?? "服务器操作未完成");
        }
      } catch (caught) {
        if (!disposed) {
          setActionError(caught instanceof Error ? caught.message : "无法读取操作进度");
          timer = window.setTimeout(() => void poll(), 1500);
        }
      }
    };
    void poll();
    return () => { disposed = true; if (timer) window.clearTimeout(timer); };
  }, [operationId, loadEvents, onNotify, onStatusRefresh]);

  const runAction = async (action: ControlAction) => {
    if (operation?.status === "queued" || operation?.status === "running") return;
    setActionError(null);
    setOperation(null);
    try {
      const response = await apiFetch(`/api/v1/servers/local/actions/${action}`, {
        method: "POST",
        headers: { Accept: "application/json", "Content-Type": "application/json", "Idempotency-Key": createIdempotencyKey(`control-${action}`) },
        body: action === "force-stop" && status?.processId
          ? JSON.stringify({ expectedProcessId: status.processId, confirmation: `FORCE STOP ${status.processId}` })
          : undefined,
      });
      if (!response.ok) throw new Error(await readApiError(response));
      const accepted = await response.json() as { operationId: string };
      try { window.sessionStorage.setItem(operationStorageKey, accepted.operationId); } catch { }
      setOperationId(accepted.operationId);
      onNotify(`${actionLabels[action]}任务已提交`);
    } catch (caught) {
      setActionError(caught instanceof Error ? caught.message : `${actionLabels[action]}请求失败`);
    }
  };

  const lifecycle = status?.lifecycle ?? "unknown";
  const displayStatus = controlStatus(lifecycle);
  const connected = lifecycle === "running";
  const pendingAction = actionFromOperation(operation);
  const operationRunning = operation?.status === "queued" || operation?.status === "running";
  const busy = operationRunning || ["starting", "stopping", "restarting"].includes(lifecycle);
  const rconConnected = status?.rcon.status === "connected";
  const queryConnected = status?.steamQuery.status === "connected";

  return (
    <div className="control-page">
      <div className="control-status-grid" aria-label="服务器状态摘要">
        <StatusCard label="服务器状态" value={displayStatus.title} detail={displayStatus.subtitle} tone={displayStatus.tone} icon={<Server size={29} strokeWidth={2.1} />} healthy={connected} />
        <StatusCard label="RCON" value={rconConnected ? "已连接" : status?.rcon.status === "degraded" ? "连接异常" : "未连接"} detail={reasonText(status?.rcon.unavailableReason, rconConnected ? "本机认证正常" : "暂无连接证据")} tone="purple" icon={<TerminalSquare size={29} strokeWidth={2.1} />} healthy={rconConnected} />
        <StatusCard label="Steam Query" value={queryConnected ? "正常" : "不可用"} detail={reasonText(status?.steamQuery.unavailableReason, queryConnected ? "查询响应正常" : "暂无真实查询证据")} tone="green" icon={<Wifi size={29} strokeWidth={2.25} />} healthy={queryConnected} />
        <StatusCard label="最近保存" value={formatTime(status?.lastSave.at ?? null)} detail={status?.lastSave.at ? sourceText(status.lastSave.source) : reasonText(status?.lastSave.unavailableReason, "暂无真实保存记录")} tone="orange" icon={<Save size={29} strokeWidth={2.1} />} healthy={Boolean(status?.lastSave.at)} />
      </div>

      {(operation || actionError) && (
        <Card className={cn("control-operation", actionError && "control-operation--error")} role={actionError ? "alert" : "status"} aria-live="polite">
          {operationRunning ? <RefreshCw className="spin" size={20} /> : operation?.status === "succeeded" ? <Check size={20} /> : <AlertTriangle size={20} />}
          <div>
            <strong>{actionError ? "操作未完成" : operationRunning ? `${pendingAction ? actionLabels[pendingAction] : "服务器操作"}进行中` : "操作已完成"}</strong>
            <span>{actionError ?? stepLabels[operation?.currentStep ?? ""] ?? operation?.currentStep ?? "等待 Server Agent 返回进度"}</span>
            {operationRunning && <progress max={100} value={operation?.progressPercent ?? 0}>{operation?.progressPercent ?? 0}%</progress>}
          </div>
          {operation?.progressPercent !== null && operationRunning && <b>{Math.round(operation?.progressPercent ?? 0)}%</b>}
        </Card>
      )}

      <div className="control-layout">
        <div className="control-layout__column">
          <Card className="control-panel">
            <div className="section-heading">
              <div><h2>服务器控制</h2><p>所有操作均由 Agent 验证受管进程并通过本机 RCON 执行</p></div>
              <span className="section-heading__status"><span className={cn("status-dot", connected ? "status-dot--success" : "status-dot--neutral")} />{displayStatus.title}</span>
            </div>
            {lifecycle === "stopped" ? (
              <div className="start-server-panel">
                <IconBox tone="green"><CirclePlay size={29} strokeWidth={2.1} /></IconBox>
                <div><strong>服务器当前已停止</strong><span>启动后会核对可执行文件、进程身份，并等待 RCON 认证。</span></div>
                <Button variant="primary" size="lg" disabled={busy} onClick={() => void runAction("start")}><CirclePlay size={19} />启动服务器</Button>
              </div>
            ) : (
              <div className="control-actions">
                <ActionTile icon={<Save size={28} />} title="保存世界" description="通过 RCON 请求保存" disabled={!rconConnected || busy} busy={pendingAction === "save" && operationRunning} onClick={() => void runAction("save")} />
                <ActionTile icon={<RefreshCw size={28} />} title="安全重启" description="保存、停服并重新验证" disabled={!rconConnected || busy} busy={pendingAction === "restart" && operationRunning} onClick={() => setConfirm("restart")} />
                <ActionTile icon={<Power size={28} />} title="安全关闭" description="保存后等待进程退出" tone="orange" disabled={!rconConnected || busy} busy={pendingAction === "shutdown" && operationRunning} onClick={() => setConfirm("shutdown")} />
              </div>
            )}
            <div className="danger-zone">
              <div className="danger-zone__copy"><span className="danger-zone__icon"><ShieldAlert size={20} /></span><div><strong>紧急强制终止</strong><span>仅在安全关闭失败时使用；Agent 会再次核对受管路径和当前 PID。</span></div></div>
              <Button variant="danger" size="lg" disabled={!connected || !status?.processId || busy} onClick={() => setConfirm("force-stop")}><Power size={20} />强制终止</Button>
            </div>
          </Card>

          <Card className="maintenance-panel">
            <div className="section-heading section-heading--compact"><div><h2>快速维护</h2><p>进入已规划的服务器维护页面</p></div></div>
            <div className="maintenance-grid">
              <ActionTile icon={<DatabaseBackup size={28} />} title="存档备份" description="读取服务器备份" onClick={() => navigate("/server/backup")} />
              <ActionTile icon={<Download size={28} />} title="检查更新" description="进入更新管理" onClick={() => navigate("/server/update")} />
              <ActionTile icon={<FileText size={28} />} title="查看日志" description="查看真实日志" onClick={() => navigate("/server/logs")} />
              <ActionTile icon={<Activity size={28} />} title="运行诊断" description="检测服务器状态" onClick={() => navigate("/server/diagnostics")} />
            </div>
          </Card>
        </div>

        <div className="control-layout__column">
          <Card className="running-info-panel">
            <div className="section-heading section-heading--compact"><div><h2>运行信息</h2><p>来自受管进程与服务器配置的实时结果</p></div></div>
            <dl className="info-list">
              <div><dt><Box size={20} />Avorion 版本</dt><dd>{status?.version ?? "不可用"}</dd></div>
              <div><dt><Clock3 size={20} />运行时间</dt><dd>{connected ? formatDuration(status?.uptimeSeconds ?? null) : "—"}</dd></div>
              <div><dt><Users size={20} />在线玩家</dt><dd>{status?.onlinePlayers === null || status?.onlinePlayers === undefined ? "不可用" : `${status.onlinePlayers} / ${status.maxPlayers ?? "?"}`}</dd></div>
              <div><dt><DatabaseBackup size={20} />当前存档</dt><dd>{status?.galaxyName ?? "未配置"}</dd></div>
            </dl>
          </Card>

          <Card className="events-panel">
            <div className="section-heading section-heading--compact"><div><h2>最近事件</h2><p>从服务器日志文件读取，不使用 Mock</p></div><button className="text-link" type="button" onClick={() => navigate("/server/logs")}>查看全部</button></div>
            {eventsUnavailable ? <div className="control-empty-state"><Info size={18} /><span>{eventsUnavailable}</span></div> : events.length === 0 ? <div className="control-empty-state"><Info size={18} /><span>日志中暂无可显示事件</span></div> : (
              <ol className="event-list">{events.map((event) => <li key={event.id}><span className={cn("event-list__icon", `event-list__icon--${event.kind}`)}>{eventIcon(event.kind)}</span><span className="event-list__message">{event.message}</span><time>{event.time}</time></li>)}</ol>
            )}
          </Card>

          <Card className="connection-note">
            <span className="connection-note__icon"><Wrench size={20} /></span>
            <div><strong>{status?.agent.connected ? "Server Agent 已连接" : "Server Agent 状态不可用"}</strong><span>{status?.provenance.unavailableReason ? reasonText(status.provenance.unavailableReason, "状态不可用") : `状态来源：${sourceText(status?.provenance.source)}`}</span></div>
          </Card>
        </div>
      </div>

      <ConfirmDialog open={confirm === "restart"} onOpenChange={(open) => setConfirm(open ? "restart" : null)} title="安全重启服务器？" description="Agent 将先通过 RCON 保存世界，再请求安全停服；确认原进程退出后才会重新启动并验证 RCON。" actionLabel="确认安全重启" onConfirm={() => void runAction("restart")} />
      <ConfirmDialog open={confirm === "shutdown"} onOpenChange={(open) => setConfirm(open ? "shutdown" : null)} title="安全关闭服务器？" description="Agent 将先通过 RCON 保存世界，再请求安全停服并等待受管进程退出。不会使用强制终止。" actionLabel="确认安全关闭" onConfirm={() => void runAction("shutdown")} />
      <ConfirmDialog open={confirm === "force-stop"} onOpenChange={(open) => setConfirm(open ? "force-stop" : null)} title="强制终止受管服务器？" description={`只会终止当前已核对路径的 Avorion 进程 PID ${status?.processId ?? "未知"}。不会先保存，可能丢失最近进度；仅在安全关闭无响应时使用。`} actionLabel="确认强制终止" danger onConfirm={() => void runAction("force-stop")} />
    </div>
  );
}
