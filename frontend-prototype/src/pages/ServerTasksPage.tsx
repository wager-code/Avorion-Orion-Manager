import * as Dialog from "@radix-ui/react-dialog";
import {
  CalendarDays,
  CirclePause,
  CirclePlay,
  Clock3,
  LoaderCircle,
  Plus,
  RefreshCw,
  Save,
  Trash2,
  X,
} from "lucide-react";
import { useCallback, useEffect, useMemo, useState, type FormEvent } from "react";
import { Button, Card, StatusPill } from "../components/ui";
import { apiFetch, createIdempotencyKey } from "../lib/api";
import { cn } from "../lib/utils";

type TaskKind = "scheduled-save" | "scheduled-safe-restart";
type TaskFilter = "all" | "enabled" | "paused";
type OperationStatus = "queued" | "running" | "succeeded" | "failed" | "cancelled";
type LoadStatus = "loading" | "ready" | "error";

type AutomationTask = {
  taskId: string;
  kind: TaskKind;
  name: string;
  scheduleKind: "interval" | "weekly";
  intervalMinutes: number | null;
  dayOfWeek: number | null;
  localTime: string | null;
  timeZone: string;
  enabled: boolean;
  nextRunAt: string;
  lastRunAt: string | null;
  lastOperationId: string | null;
  lastOperationStatus: OperationStatus | null;
  lastErrorMessage: string | null;
};

type TaskDraft = {
  kind: TaskKind;
  intervalMinutes: 15 | 30 | 60 | 120;
  dayOfWeek: number;
  localTime: string;
  enabled: boolean;
};

const taskCatalog = {
  "scheduled-save": { name: "定时保存", detail: "通过已验证的 RCON /save 命令保存世界", icon: Save, tone: "blue" },
  "scheduled-safe-restart": { name: "定时安全重启", detail: "先保存并安全停服，再受控启动并完成健康检查", icon: RefreshCw, tone: "orange" },
} as const;

const filterLabels: Array<{ value: TaskFilter; label: string }> = [
  { value: "all", label: "全部" },
  { value: "enabled", label: "已启用" },
  { value: "paused", label: "已暂停" },
];

const dayLabels = ["", "周一", "周二", "周三", "周四", "周五", "周六", "周日"];

function defaultDraft(): TaskDraft {
  return { kind: "scheduled-save", intervalMinutes: 30, dayOfWeek: 1, localTime: "05:00", enabled: true };
}

function isTask(value: unknown): value is AutomationTask {
  if (typeof value !== "object" || value === null) return false;
  const item = value as Partial<AutomationTask>;
  return typeof item.taskId === "string" && (item.kind === "scheduled-save" || item.kind === "scheduled-safe-restart") &&
    typeof item.name === "string" && typeof item.enabled === "boolean" && typeof item.nextRunAt === "string";
}

async function readApiError(response: Response) {
  const body = await response.json().catch(() => null) as { error?: { message?: string } } | null;
  return body?.error?.message ?? `自动任务请求失败（HTTP ${response.status}）`;
}

function scheduleLabel(task: AutomationTask) {
  if (task.scheduleKind === "interval" && task.intervalMinutes) return `每 ${task.intervalMinutes} 分钟`;
  if (task.scheduleKind === "weekly" && task.dayOfWeek && task.localTime) return `每${dayLabels[task.dayOfWeek]} ${task.localTime}`;
  return "计划不可识别";
}

function exactTime(value: string) {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return "时间未知";
  return new Intl.DateTimeFormat("zh-CN", {
    timeZone: "Asia/Shanghai", month: "2-digit", day: "2-digit", hour: "2-digit", minute: "2-digit", hour12: false,
  }).format(date).replaceAll("/", "-");
}

function relativeFuture(value: string) {
  const time = new Date(value).getTime();
  if (!Number.isFinite(time)) return "时间未知";
  const seconds = Math.max(0, Math.round((time - Date.now()) / 1000));
  if (seconds < 60) return "1 分钟内";
  if (seconds < 3600) return `${Math.ceil(seconds / 60)} 分钟后`;
  if (seconds < 86400) return `${Math.ceil(seconds / 3600)} 小时后`;
  return exactTime(value);
}

function lastResult(task: AutomationTask) {
  if (!task.lastOperationStatus) return "尚未执行";
  const labels: Record<OperationStatus, string> = {
    queued: "等待执行", running: "正在执行", succeeded: "执行成功", failed: "执行失败", cancelled: "已取消",
  };
  return task.lastErrorMessage ? `${labels[task.lastOperationStatus]}：${task.lastErrorMessage}` : labels[task.lastOperationStatus];
}

function requestFromDraft(draft: TaskDraft) {
  return draft.kind === "scheduled-save"
    ? { kind: draft.kind, scheduleKind: "interval", intervalMinutes: draft.intervalMinutes, dayOfWeek: null, localTime: null, enabled: draft.enabled }
    : { kind: draft.kind, scheduleKind: "weekly", intervalMinutes: null, dayOfWeek: draft.dayOfWeek, localTime: draft.localTime, enabled: draft.enabled };
}

export function ServerTasksPage({ onNotify }: { onNotify: (message: string) => void }) {
  const [tasks, setTasks] = useState<AutomationTask[]>([]);
  const [filter, setFilter] = useState<TaskFilter>("all");
  const [loadStatus, setLoadStatus] = useState<LoadStatus>("loading");
  const [loadError, setLoadError] = useState<string | null>(null);
  const [dialogOpen, setDialogOpen] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [draft, setDraft] = useState<TaskDraft>(defaultDraft);
  const [saving, setSaving] = useState(false);
  const [pendingId, setPendingId] = useState<string | null>(null);

  const loadTasks = useCallback(async (notify = false) => {
    setLoadStatus("loading");
    try {
      const response = await apiFetch("/api/v1/servers/local/tasks", { headers: { Accept: "application/json" } });
      if (!response.ok) throw new Error(await readApiError(response));
      const body = await response.json() as { items?: unknown[] };
      if (!Array.isArray(body.items) || !body.items.every(isTask)) throw new Error("自动任务接口返回的数据格式无效");
      setTasks(body.items);
      setLoadError(null);
      setLoadStatus("ready");
      if (notify) onNotify(`已读取 ${body.items.length} 个真实自动任务`);
    } catch (caught) {
      const message = caught instanceof Error ? caught.message : "无法读取自动任务";
      setTasks([]);
      setLoadError(message);
      setLoadStatus("error");
      if (notify) onNotify(message);
    }
  }, [onNotify]);

  useEffect(() => { void loadTasks(); }, [loadTasks]);

  const enabledCount = tasks.filter((task) => task.enabled).length;
  const pausedCount = tasks.length - enabledCount;
  const nextTask = useMemo(() => tasks.filter((task) => task.enabled).sort((a, b) => Date.parse(a.nextRunAt) - Date.parse(b.nextRunAt))[0] ?? null, [tasks]);
  const nextTaskCatalog = nextTask ? taskCatalog[nextTask.kind] : null;
  const NextTaskIcon = nextTaskCatalog?.icon ?? Clock3;
  const visibleTasks = useMemo(
    () => tasks.filter((task) => filter === "all" || (filter === "enabled" ? task.enabled : !task.enabled)),
    [filter, tasks],
  );

  const openNewTask = () => {
    setEditingId(null);
    setDraft(defaultDraft());
    setDialogOpen(true);
  };

  const openEditTask = (task: AutomationTask) => {
    setEditingId(task.taskId);
    setDraft({
      kind: task.kind,
      intervalMinutes: (task.intervalMinutes ?? 30) as TaskDraft["intervalMinutes"],
      dayOfWeek: task.dayOfWeek ?? 1,
      localTime: task.localTime ?? "05:00",
      enabled: task.enabled,
    });
    setDialogOpen(true);
  };

  const updateTask = async (task: AutomationTask, enabled: boolean) => {
    setPendingId(task.taskId);
    try {
      const request = {
        kind: task.kind,
        scheduleKind: task.scheduleKind,
        intervalMinutes: task.intervalMinutes,
        dayOfWeek: task.dayOfWeek,
        localTime: task.localTime,
        enabled,
      };
      const response = await apiFetch(`/api/v1/servers/local/tasks/${task.taskId}`, {
        method: "PATCH",
        headers: { "Content-Type": "application/json", "Idempotency-Key": createIdempotencyKey("task-toggle") },
        body: JSON.stringify(request),
      });
      if (!response.ok) throw new Error(await readApiError(response));
      const updated = await response.json() as AutomationTask;
      setTasks((current) => current.map((item) => item.taskId === updated.taskId ? updated : item));
      onNotify(`${task.name}已${enabled ? "启用" : "暂停"}`);
    } catch (caught) {
      onNotify(caught instanceof Error ? caught.message : "自动任务状态修改失败");
    } finally {
      setPendingId(null);
    }
  };

  const saveTask = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setSaving(true);
    try {
      const response = await apiFetch(editingId ? `/api/v1/servers/local/tasks/${editingId}` : "/api/v1/servers/local/tasks", {
        method: editingId ? "PATCH" : "POST",
        headers: { "Content-Type": "application/json", "Idempotency-Key": createIdempotencyKey(editingId ? "task-edit" : "task-create") },
        body: JSON.stringify(requestFromDraft(draft)),
      });
      if (!response.ok) throw new Error(await readApiError(response));
      const saved = await response.json() as AutomationTask;
      setTasks((current) => editingId ? current.map((item) => item.taskId === saved.taskId ? saved : item) : [...current, saved]);
      setDialogOpen(false);
      onNotify(`${saved.name}已${editingId ? "更新" : "创建"}并写入管理数据库`);
    } catch (caught) {
      onNotify(caught instanceof Error ? caught.message : "自动任务保存失败");
    } finally {
      setSaving(false);
    }
  };

  const deleteTask = async (task: AutomationTask) => {
    if (!window.confirm(`确定删除“${task.name}”吗？删除后不会再由 Server Agent 调度。`)) return;
    setPendingId(task.taskId);
    try {
      const response = await apiFetch(`/api/v1/servers/local/tasks/${task.taskId}`, { method: "DELETE" });
      if (!response.ok) throw new Error(await readApiError(response));
      setTasks((current) => current.filter((item) => item.taskId !== task.taskId));
      onNotify(`${task.name}已删除`);
    } catch (caught) {
      onNotify(caught instanceof Error ? caught.message : "自动任务删除失败");
    } finally {
      setPendingId(null);
    }
  };

  const statusPresentation = loadStatus === "loading"
    ? { tone: "info" as const, label: "正在读取" }
    : loadStatus === "error"
      ? { tone: "danger" as const, label: "连接失败" }
      : { tone: "success" as const, label: "调度器已连接" };

  return (
    <div className="tasks-page">
      <header className="tasks-page__heading">
        <div>
          <div className="tasks-page__title-row">
            <h2>自动任务</h2>
            <StatusPill tone={statusPresentation.tone} compact>{statusPresentation.label}</StatusPill>
          </div>
          <p>任务保存在本机数据库；当前只开放已验证的定时保存与安全重启。</p>
        </div>
        <Button variant="primary" size="lg" onClick={openNewTask} disabled={loadStatus !== "ready"}>
          <Plus size={19} />新建任务
        </Button>
      </header>

      <Card className="tasks-summary" aria-label="自动任务摘要" aria-busy={loadStatus === "loading"}>
        <div className="tasks-summary__item"><span className="tasks-summary__icon tasks-summary__icon--blue" aria-hidden="true"><CalendarDays size={23} /></span><div><span>任务总数</span><strong>{loadStatus === "loading" ? "—" : tasks.length}</strong></div></div>
        <div className="tasks-summary__item"><span className="tasks-summary__icon tasks-summary__icon--green" aria-hidden="true"><CirclePlay size={23} /></span><div><span>已启用</span><strong>{loadStatus === "loading" ? "—" : enabledCount}</strong></div></div>
        <div className="tasks-summary__item"><span className="tasks-summary__icon tasks-summary__icon--orange" aria-hidden="true"><CirclePause size={23} /></span><div><span>已暂停</span><strong>{loadStatus === "loading" ? "—" : pausedCount}</strong></div></div>
        <div className="tasks-summary__item tasks-summary__item--next"><span className="tasks-summary__icon tasks-summary__icon--blue" aria-hidden="true"><Clock3 size={23} /></span><div><span>下一任务</span><strong>{nextTask ? `${nextTask.name} · ${relativeFuture(nextTask.nextRunAt)}` : "暂无已启用任务"}</strong></div></div>
      </Card>

      <div className="tasks-workspace">
        <Card className="tasks-list-card" aria-labelledby="tasks-list-title">
          <div className="tasks-list-card__heading">
            <h3 id="tasks-list-title">任务列表</h3>
            <button type="button" className="tasks-refresh" onClick={() => void loadTasks(true)} disabled={loadStatus === "loading"}><RefreshCw size={15} className={cn(loadStatus === "loading" && "spin")} />刷新</button>
          </div>
          <div className="tasks-filter" aria-label="任务筛选">
            {filterLabels.map((item) => {
              const count = item.value === "all" ? tasks.length : item.value === "enabled" ? enabledCount : pausedCount;
              return <button key={item.value} type="button" className={cn("tasks-filter__button", filter === item.value && "tasks-filter__button--active")} aria-pressed={filter === item.value} onClick={() => setFilter(item.value)}>{item.label}<span>{count}</span></button>;
            })}
          </div>

          <div className="tasks-table-wrap">
            <table className="tasks-table">
              <thead><tr><th>任务</th><th>执行计划</th><th>下次执行</th><th>状态</th><th>操作</th></tr></thead>
              <tbody>
                {loadStatus === "loading" && <tr><td colSpan={5}><div className="tasks-state"><LoaderCircle size={19} className="spin" />正在读取真实任务...</div></td></tr>}
                {loadStatus === "error" && <tr><td colSpan={5}><div className="tasks-state tasks-state--error"><strong>无法读取自动任务</strong><span>{loadError}</span><Button size="sm" onClick={() => void loadTasks()}>重新连接</Button></div></td></tr>}
                {loadStatus === "ready" && visibleTasks.length === 0 && <tr><td colSpan={5}><div className="tasks-state"><Clock3 size={20} /><strong>{tasks.length === 0 ? "还没有自动任务" : "此筛选下没有任务"}</strong><span>{tasks.length === 0 ? "创建后，任务会真实写入本机数据库并由 Server Agent 调度。" : "请选择其他筛选条件。"}</span></div></td></tr>}
                {loadStatus === "ready" && visibleTasks.map((task) => {
                  const catalogItem = taskCatalog[task.kind];
                  const Icon = catalogItem.icon;
                  return (
                    <tr key={task.taskId}>
                      <td><div className="tasks-table__name"><span className={cn("tasks-table__icon", `tasks-table__icon--${catalogItem.tone}`)} aria-hidden="true"><Icon size={18} /></span><span><strong>{task.name}</strong><small title={task.lastErrorMessage ?? undefined}>{lastResult(task)}</small></span></div></td>
                      <td>{scheduleLabel(task)}</td>
                      <td title={exactTime(task.nextRunAt)}>{task.enabled ? relativeFuture(task.nextRunAt) : "暂停后不执行"}</td>
                      <td><div className="tasks-table__status"><button type="button" className={cn("task-switch", task.enabled && "task-switch--enabled")} role="switch" aria-checked={task.enabled} aria-label={`${task.enabled ? "暂停" : "启用"}${task.name}`} disabled={pendingId === task.taskId} onClick={() => void updateTask(task, !task.enabled)}><span /></button><span className={task.enabled ? "text-success" : "tasks-table__paused"}>{pendingId === task.taskId ? "保存中" : task.enabled ? "已启用" : "已暂停"}</span></div></td>
                      <td><div className="tasks-table__actions"><button type="button" className="tasks-table__edit" onClick={() => openEditTask(task)} disabled={pendingId === task.taskId}>编辑</button><button type="button" className="tasks-table__delete" onClick={() => void deleteTask(task)} disabled={pendingId === task.taskId} aria-label={`删除${task.name}`}><Trash2 size={14} />删除</button></div></td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        </Card>

        <Card className="tasks-next-card" aria-labelledby="next-task-title">
          <h3 id="next-task-title">下一次执行</h3>
          {nextTask ? <div className="tasks-next-card__body"><span className={cn("tasks-next-card__icon", nextTaskCatalog && `tasks-next-card__icon--${nextTaskCatalog.tone}`)} aria-hidden="true"><NextTaskIcon size={31} /></span><strong>{nextTask.name}</strong><b>{relativeFuture(nextTask.nextRunAt)}</b><p>{exactTime(nextTask.nextRunAt)} · 中国标准时间</p><p>{nextTaskCatalog?.detail}</p></div> : <div className="tasks-next-card__body tasks-next-card__body--empty"><span className="tasks-next-card__icon" aria-hidden="true"><Clock3 size={31} /></span><strong>暂无已启用任务</strong><p>启用任务后将在此显示后端计算的真实执行时间。</p></div>}
        </Card>
      </div>

      <Dialog.Root open={dialogOpen} onOpenChange={(open) => !saving && setDialogOpen(open)}>
        <Dialog.Portal>
          <Dialog.Overlay className="dialog-overlay" />
          <Dialog.Content className="task-dialog" aria-describedby="task-dialog-description">
            <div><Dialog.Title className="task-dialog__title">{editingId ? "编辑任务" : "新建任务"}</Dialog.Title><Dialog.Description id="task-dialog-description" className="task-dialog__description">只提供已经接入真实安全命令的任务类型。</Dialog.Description></div>
            <Dialog.Close className="dialog-close" aria-label="关闭任务窗口" disabled={saving}><X size={18} /></Dialog.Close>
            <form className="task-form" onSubmit={(event) => void saveTask(event)}>
              <label><span>任务类型</span><select aria-label="任务类型" value={draft.kind} onChange={(event) => setDraft((current) => ({ ...current, kind: event.target.value as TaskKind }))} disabled={saving}><option value="scheduled-save">定时保存</option><option value="scheduled-safe-restart">定时安全重启</option></select></label>
              {draft.kind === "scheduled-save" ? (
                <label><span>执行间隔</span><select aria-label="执行间隔" value={draft.intervalMinutes} onChange={(event) => setDraft((current) => ({ ...current, intervalMinutes: Number(event.target.value) as TaskDraft["intervalMinutes"] }))} disabled={saving}><option value={15}>每 15 分钟</option><option value={30}>每 30 分钟</option><option value={60}>每 60 分钟</option><option value={120}>每 120 分钟</option></select></label>
              ) : (
                <div className="task-form__schedule-row"><label><span>每周</span><select aria-label="每周执行日" value={draft.dayOfWeek} onChange={(event) => setDraft((current) => ({ ...current, dayOfWeek: Number(event.target.value) }))} disabled={saving}>{dayLabels.slice(1).map((label, index) => <option key={label} value={index + 1}>{label}</option>)}</select></label><label><span>执行时间</span><input type="time" aria-label="执行时间" value={draft.localTime} onChange={(event) => setDraft((current) => ({ ...current, localTime: event.target.value }))} required disabled={saving} /></label></div>
              )}
              <p className="task-form__boundary">定时备份、服务端更新、MOD 检查和广播尚未通过真实命令验证，因此暂不开放。</p>
              <label className="task-form__enabled"><span><strong>保存后启用</strong><small>启用后由 Server Agent 在后台调度，切换页面不会中断</small></span><button type="button" className={cn("task-switch", draft.enabled && "task-switch--enabled")} role="switch" aria-label="保存后启用" aria-checked={draft.enabled} onClick={() => setDraft((current) => ({ ...current, enabled: !current.enabled }))} disabled={saving}><span /></button></label>
              <div className="task-form__actions"><Dialog.Close asChild><Button variant="secondary" disabled={saving}>取消</Button></Dialog.Close><Button type="submit" variant="primary" disabled={saving}>{saving && <LoaderCircle size={16} className="spin" />}{saving ? "正在保存" : editingId ? "保存修改" : "创建任务"}</Button></div>
            </form>
          </Dialog.Content>
        </Dialog.Portal>
      </Dialog.Root>
    </div>
  );
}
