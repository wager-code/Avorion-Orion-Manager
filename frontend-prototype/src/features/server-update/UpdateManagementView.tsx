import type { ServerStatus } from "../../types";
import { MessageSquareText, Power, Play, Activity, AlertCircle, ArrowLeft, Box, CheckCircle2, Circle, Clock3, Database, Download, FileCheck2, FolderOpen, Info, LoaderCircle, RefreshCw, Search, ServerCog, Settings, ShieldCheck, SquareTerminal } from "lucide-react";
import { useEffect, useRef, useState } from "react";
import { apiFetch, createIdempotencyKey } from "../../lib/api";
import { Button, Card, StatusPill } from "../../components/ui";
import type { UpdateEnvironmentCurrent, UpdateCheckResult, UpdateVerificationResult, UpdateRollbackPointResult, UpdateRollbackPointAvailability, UpdateInspectionOperation, ManagementBridgeStatus } from "./types";
import { readStoredOperationId, activeOperationStorageKeys, storeOperationId } from "./operationStorage";
import { readApiError, formatDataSize } from "./utils";
import { ManagementSummary, UpdateHeading } from "./UpdateSummaryComponents";

export function UpdateManagementView({ onBack, onNotify }: { onBack: () => void; onNotify: (message: string) => void }) {
  const [environment, setEnvironment] = useState<UpdateEnvironmentCurrent | null>(null);
  const [serverStatus, setServerStatus] = useState<ServerStatus | null>(null);
  const [bridgeStatus, setBridgeStatus] = useState<ManagementBridgeStatus | null>(null);
  const [bridgeConnected, setBridgeConnected] = useState(false);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const [checkOperationId, setCheckOperationId] = useState<string | null>(() => readStoredOperationId(activeOperationStorageKeys.updateCheck));
  const [verificationOperationId, setVerificationOperationId] = useState<string | null>(() => readStoredOperationId(activeOperationStorageKeys.fileVerification));
  const [rollbackOperationId, setRollbackOperationId] = useState<string | null>(() => readStoredOperationId(activeOperationStorageKeys.rollbackPoint));
  const [checkOperation, setCheckOperation] = useState<UpdateInspectionOperation<UpdateCheckResult> | null>(null);
  const [verificationOperation, setVerificationOperation] = useState<UpdateInspectionOperation<UpdateVerificationResult> | null>(null);
  const [rollbackOperation, setRollbackOperation] = useState<UpdateInspectionOperation<UpdateRollbackPointResult> | null>(null);
  const [latestRollbackPoint, setLatestRollbackPoint] = useState<UpdateRollbackPointResult | null>(null);
  const notifiedOperations = useRef(new Set<string>());

  useEffect(() => {
    let disposed = false;
    const load = async () => {
      try {
        const [environmentResponse, statusResponse, rollbackResponse, bridgeResponse, bridgeHelloResponse] = await Promise.all([
          fetch("/api/v1/servers/local/update-environment", { headers: { Accept: "application/json" } }),
          fetch("/api/v1/servers/local/status", { headers: { Accept: "application/json" } }),
          fetch("/api/v1/servers/local/updates/rollback-points/latest", { headers: { Accept: "application/json" } }),
          fetch("/api/v1/servers/local/management-bridge/status", { headers: { Accept: "application/json" } }),
          fetch("/api/v1/servers/local/management-bridge/hello", { headers: { Accept: "application/json" } }),
        ]);
        if (!environmentResponse.ok) throw new Error(await readApiError(environmentResponse));
        if (!statusResponse.ok) throw new Error(await readApiError(statusResponse));
        if (!rollbackResponse.ok) throw new Error(await readApiError(rollbackResponse));
        if (!bridgeResponse.ok) throw new Error(await readApiError(bridgeResponse));
        const [nextEnvironment, nextStatus, rollbackAvailability, nextBridgeStatus] = await Promise.all([
          environmentResponse.json() as Promise<UpdateEnvironmentCurrent>,
          statusResponse.json() as Promise<ServerStatus>,
          rollbackResponse.json() as Promise<UpdateRollbackPointAvailability>,
          bridgeResponse.json() as Promise<ManagementBridgeStatus>,
        ]);
        if (!disposed) {
          setEnvironment(nextEnvironment);
          setServerStatus(nextStatus);
          setBridgeStatus(nextBridgeStatus);
          setBridgeConnected(bridgeHelloResponse.ok);
          setLatestRollbackPoint(rollbackAvailability.available ? rollbackAvailability.point : null);
          setLoadError(null);
        }
      } catch (error) {
        if (!disposed) setLoadError(error instanceof Error ? error.message : "无法读取真实更新环境");
      }
    };
    void load();
    const timer = window.setInterval(() => void load(), 5_000);
    return () => { disposed = true; window.clearInterval(timer); };
  }, []);

  useEffect(() => {
    if (!checkOperationId && !verificationOperationId && !rollbackOperationId) return;
    let disposed = false;
    const poll = async () => {
      const requests = [
        checkOperationId ? fetch(`/api/v1/operations/${encodeURIComponent(checkOperationId)}`, { headers: { Accept: "application/json" } }) : null,
        verificationOperationId ? fetch(`/api/v1/operations/${encodeURIComponent(verificationOperationId)}`, { headers: { Accept: "application/json" } }) : null,
        rollbackOperationId ? fetch(`/api/v1/operations/${encodeURIComponent(rollbackOperationId)}`, { headers: { Accept: "application/json" } }) : null,
      ] as const;
      try {
        const [checkResponse, verificationResponse, rollbackResponse] = await Promise.all(requests.map((request) => request ?? Promise.resolve(null)));
        if (checkResponse) {
          if (!checkResponse.ok) throw new Error(await readApiError(checkResponse));
          const operation = await checkResponse.json() as UpdateInspectionOperation<UpdateCheckResult>;
          if (!disposed) setCheckOperation(operation);
        }
        if (verificationResponse) {
          if (!verificationResponse.ok) throw new Error(await readApiError(verificationResponse));
          const operation = await verificationResponse.json() as UpdateInspectionOperation<UpdateVerificationResult>;
          if (!disposed) setVerificationOperation(operation);
        }
        if (rollbackResponse) {
          if (!rollbackResponse.ok) throw new Error(await readApiError(rollbackResponse));
          const operation = await rollbackResponse.json() as UpdateInspectionOperation<UpdateRollbackPointResult>;
          if (!disposed) {
            setRollbackOperation(operation);
            if (operation.status === "succeeded" && operation.result) setLatestRollbackPoint(operation.result);
          }
        }
      } catch (error) {
        if (!disposed) setActionError(error instanceof Error ? error.message : "无法恢复更新检查进度");
      }
    };
    void poll();
    const timer = window.setInterval(() => void poll(), 1_000);
    return () => { disposed = true; window.clearInterval(timer); };
  }, [checkOperationId, verificationOperationId, rollbackOperationId]);

  useEffect(() => {
    for (const operation of [checkOperation, verificationOperation, rollbackOperation]) {
      if (!operation || operation.status === "queued" || operation.status === "running" || notifiedOperations.current.has(operation.operationId)) continue;
      notifiedOperations.current.add(operation.operationId);
      if (operation.status === "succeeded") {
        if (operation.type === "avorion.update.check") onNotify("真实更新检查已完成");
        else if (operation.type === "avorion.files.verify") onNotify("服务端文件只读验证已完成");
        else onNotify("更新前安全点已创建并完成全文件校验");
      }
      else onNotify(operation.error?.message ?? "更新检查操作失败");
    }
  }, [checkOperation, verificationOperation, rollbackOperation, onNotify]);

  const startInspection = async (kind: "check" | "verify") => {
    setActionError(null);
    const route = kind === "check" ? "checks" : "verifications";
    try {
      const response = await apiFetch(`/api/v1/servers/local/updates/${route}`, {
        method: "POST",
        headers: { Accept: "application/json", "Idempotency-Key": createIdempotencyKey(`update-${kind}`) },
      });
      if (!response.ok) throw new Error(await readApiError(response));
      const accepted = await response.json() as { operationId: string; status: UpdateInspectionOperation<unknown>["status"]; acceptedAt: string };
      const operation = { operationId: accepted.operationId, type: kind === "check" ? "avorion.update.check" : "avorion.files.verify", status: accepted.status, progressPercent: 0, currentStep: null, acceptedAt: accepted.acceptedAt, result: null, error: null };
      if (kind === "check") {
        storeOperationId(activeOperationStorageKeys.updateCheck, accepted.operationId);
        setCheckOperationId(accepted.operationId);
        setCheckOperation(operation as UpdateInspectionOperation<UpdateCheckResult>);
      } else {
        storeOperationId(activeOperationStorageKeys.fileVerification, accepted.operationId);
        setVerificationOperationId(accepted.operationId);
        setVerificationOperation(operation as UpdateInspectionOperation<UpdateVerificationResult>);
      }
    } catch (error) {
      setActionError(error instanceof Error ? error.message : "无法开始真实检查");
    }
  };

  const createRollbackPoint = async () => {
    setActionError(null);
    try {
      const response = await apiFetch("/api/v1/servers/local/updates/rollback-points", {
        method: "POST",
        headers: { Accept: "application/json", "Idempotency-Key": createIdempotencyKey("update-rollback-point") },
      });
      if (!response.ok) throw new Error(await readApiError(response));
      const accepted = await response.json() as { operationId: string; status: UpdateInspectionOperation<unknown>["status"]; acceptedAt: string };
      const operation: UpdateInspectionOperation<UpdateRollbackPointResult> = {
        operationId: accepted.operationId,
        type: "avorion.update.rollback-point.create",
        status: accepted.status,
        progressPercent: 0,
        currentStep: null,
        acceptedAt: accepted.acceptedAt,
        result: null,
        error: null,
      };
      storeOperationId(activeOperationStorageKeys.rollbackPoint, accepted.operationId);
      setRollbackOperationId(accepted.operationId);
      setRollbackOperation(operation);
    } catch (error) {
      setActionError(error instanceof Error ? error.message : "无法创建更新前安全点");
    }
  };

  const checkRunning = checkOperation?.status === "queued" || checkOperation?.status === "running";
  const verificationRunning = verificationOperation?.status === "queued" || verificationOperation?.status === "running";
  const rollbackRunning = rollbackOperation?.status === "queued" || rollbackOperation?.status === "running";
  const anyRunning = checkRunning || verificationRunning || rollbackRunning;
  const environmentValid = Boolean(environment?.configured && environment.validation?.valid);
  const checkResult = checkOperation?.status === "succeeded" ? checkOperation.result : null;
  const verificationResult = verificationOperation?.status === "succeeded" ? verificationOperation.result : null;
  const failedOperation = checkOperation?.status === "failed"
    ? checkOperation
    : verificationOperation?.status === "failed"
      ? verificationOperation
      : rollbackOperation?.status === "failed"
        ? rollbackOperation
        : null;
  const currentVersion = checkResult?.currentVersion ?? serverStatus?.version ?? environment?.validation?.server.version ?? (latestRollbackPoint?.installedBuildId ? `Build ${latestRollbackPoint.installedBuildId}` : "版本不可用");
  const updateState = checkResult?.updateAvailable === true ? "发现更新" : checkResult?.updateAvailable === false ? "已是最新" : checkResult ? "无法比较" : "尚未检查";
  const lifecycleText = serverStatus?.lifecycle === "running" ? "运行中" : serverStatus?.lifecycle === "stopped" ? "已停止" : serverStatus ? "状态变化中" : "读取中";
  const readiness = [
    { icon: SquareTerminal, title: "SteamCMD 路径", detail: environment?.configuration?.steamCmdPath ?? "尚未配置路径", status: environmentValid ? "已验证" : "未就绪", tone: environmentValid ? "success" : "error" },
    { icon: Box, title: "服务端路径", detail: environment?.configuration?.serverDirectory ?? "尚未配置路径", status: environmentValid ? "已验证" : "未就绪", tone: environmentValid ? "success" : "error" },
    { icon: Search, title: "官方版本查询", detail: checkResult ? `App 565060 · public · Build ${checkResult.latestBuildId}` : "尚未向 Steam 官方查询", status: checkResult ? "已完成" : checkOperation?.status === "failed" ? "失败" : "待检查", tone: checkResult ? "success" : checkOperation?.status === "failed" ? "error" : "pending" },
    { icon: ShieldCheck, title: "本地文件只读验证", detail: verificationResult ? `已计算 ${verificationResult.files.length} 个必需文件的 SHA-256` : "不会修复、覆盖或下载文件", status: verificationResult ? "已通过" : verificationOperation?.status === "failed" ? "失败" : "待验证", tone: verificationResult ? "success" : verificationOperation?.status === "failed" ? "error" : "pending" },
    { icon: Database, title: "更新前安全点", detail: latestRollbackPoint ? `服务端 + Galaxy · ${formatDataSize(latestRollbackPoint.totalBytes)} · ${new Date(latestRollbackPoint.verifiedAt).toLocaleString("zh-CN")}` : "独立于 Avorion 自动存档备份，尚未创建", status: latestRollbackPoint ? "已验证" : rollbackOperation?.status === "failed" ? "失败" : "待创建", tone: latestRollbackPoint ? "success" : rollbackOperation?.status === "failed" ? "error" : "pending" },
    { icon: Settings, title: "OrionAdminBridge", detail: bridgeConnected ? `已连接 · 版本 ${bridgeStatus?.installedVersion ?? "未知"}` : bridgeStatus?.current && bridgeStatus.configured ? `已安装 ${bridgeStatus.installedVersion ?? ""} · 启动服务器后验证连接` : bridgeStatus?.installed ? `已安装 ${bridgeStatus.installedVersion ?? "未知"} · 需要维护或启用` : bridgeStatus?.packageAvailable ? `随包版本 ${bridgeStatus.packageVersion} · 可在配置向导安装` : "随包组件不可用", status: bridgeConnected ? "已连接" : bridgeStatus?.current && bridgeStatus.configured ? "待连接" : bridgeStatus?.installed ? "需维护" : "未安装", tone: bridgeConnected ? "success" : bridgeStatus?.current && bridgeStatus.configured ? "pending" : "error" },
    { icon: MessageSquareText, title: "完整更新执行", detail: checkResult?.updateAvailable === false ? "本机 Build 与 Steam 官方 public Build 一致，无需停服" : "发现新 Build 时仍需先完成安全点与停服验证", status: checkResult?.updateAvailable === false ? "无需更新" : "安全锁定", tone: checkResult?.updateAvailable === false ? "success" : "locked" },
  ];

  const flow = [
    { icon: MessageSquareText, title: "更新前广播", detail: "通知在线玩家" },
    { icon: Database, title: "创建备份", detail: "备份服务器数据" },
    { icon: Power, title: "安全停服", detail: "停止服务器运行" },
    { icon: SquareTerminal, title: "SteamCMD 更新并验证", detail: "更新服务端文件", blue: true },
    { icon: Play, title: "启动服务器", detail: "启动服务器运行" },
    { icon: Activity, title: "健康检查", detail: "验证运行健康" },
  ];

  return (
    <div className="update-page">
      <header className="update-page__heading update-page__heading--with-back">
        <div>
          <div className="update-page__title-row"><h2>服务器更新</h2><StatusPill tone={environmentValid ? "success" : "warning"}>{environmentValid ? "真实环境已验证" : "环境未就绪"}</StatusPill></div>
          <p>真实查询 Steam 官方版本，并对本机服务端文件执行只读校验。</p>
        </div>
        <Button variant="ghost" size="md" onClick={onBack}><ArrowLeft size={17} />返回安装与配置状态</Button>
      </header>

      <Card className="update-summary">
        <ManagementSummary icon={SquareTerminal} label="SteamCMD" value={environment?.validation?.steamCmd.version ?? (environmentValid ? "已验证" : "未就绪")} tone="blue" ok={environmentValid} />
        <ManagementSummary icon={Box} label="当前版本" value={currentVersion} tone="blue" />
        <ManagementSummary icon={Clock3} label="更新状态" value={updateState} tone="purple" />
        <ManagementSummary icon={ServerCog} label="服务器状态" value={lifecycleText} tone="purple" running={serverStatus?.lifecycle === "running"} />
      </Card>

      {(loadError || actionError) && <div className="update-inline-error" role="alert"><AlertCircle size={18} /><span>{actionError ?? loadError}</span></div>}

      <div className="update-main-grid">
        <Card className="update-readiness-card">
          <h3>更新就绪检查</h3>
          <div className="update-readiness-list">
            {readiness.map((item) => {
              const Icon = item.icon;
              return (
                <div className="update-readiness-row" key={item.title}>
                  <span className="update-readiness-row__icon"><Icon size={17} /></span>
                  <span className="update-readiness-row__copy"><strong>{item.title}</strong><span>{item.detail}</span></span>
                  <span className={`update-readiness-row__status update-readiness-row__status--${item.tone}`}>
                    {item.tone === "success" ? <CheckCircle2 size={15} /> : item.tone === "error" ? <AlertCircle size={15} /> : <Clock3 size={15} />}{item.status}
                  </span>
                </div>
              );
            })}
          </div>
        </Card>

        <Card className="update-action-card">
          <h3>版本与操作</h3>
          <dl className="update-version-list">
            <div><dt>本机 Build ID</dt><dd>{checkResult?.currentBuildId ?? verificationResult?.installedBuildId ?? latestRollbackPoint?.installedBuildId ?? "不可用"}</dd></div>
            <div><dt>官方 Build ID</dt><dd>{checkResult?.latestBuildId ?? "待检查"}</dd></div>
            <div><dt>更新通道</dt><dd>public（固定）</dd></div>
          </dl>
          {anyRunning && <div className="update-operation-progress" aria-live="polite"><div><span>{checkRunning ? "正在查询 Steam 官方版本" : verificationRunning ? "正在只读验证本机文件" : "正在复制并校验更新前安全点"}</span><strong>{Math.round((checkRunning ? checkOperation?.progressPercent : verificationRunning ? verificationOperation?.progressPercent : rollbackOperation?.progressPercent) ?? 0)}%</strong></div><progress max={100} value={(checkRunning ? checkOperation?.progressPercent : verificationRunning ? verificationOperation?.progressPercent : rollbackOperation?.progressPercent) ?? 0} /></div>}
          <Button className="update-action-card__button" variant="primary" size="lg" onClick={() => void startInspection("check")} disabled={!environmentValid || anyRunning}>
            {checkRunning ? <LoaderCircle className="spin" size={18} /> : <Search size={18} />}
            {checkRunning ? "正在真实检查" : "检查 Steam 官方更新"}
          </Button>
          <Button className="update-action-card__button" variant="secondary" size="lg" disabled><Play size={18} />{checkResult?.updateAvailable === false ? "当前已是最新版本" : "开始安全更新"}</Button>
          <p className="update-action-card__helper">{checkResult?.updateAvailable === false ? `本机与官方均为 Build ${checkResult.latestBuildId}，不会执行无意义的停服更新` : "完整更新仍锁定：更新执行和失败回滚尚未完成验证"}</p>
          <Button className="update-action-card__button update-action-card__verify" variant="ghost" size="lg" onClick={() => void startInspection("verify")} disabled={!environmentValid || anyRunning}>
            {verificationRunning ? <LoaderCircle className="spin" size={18} /> : <ShieldCheck size={18} />}
            {verificationRunning ? "正在只读验证" : verificationResult ? "重新验证服务器文件" : "验证服务器文件"}
          </Button>
          <Button className="update-action-card__button update-action-card__rollback" variant="ghost" size="lg" onClick={() => void createRollbackPoint()} disabled={!environmentValid || serverStatus?.lifecycle !== "stopped" || anyRunning}>
            {rollbackRunning ? <LoaderCircle className="spin" size={18} /> : <Database size={18} />}
            {rollbackRunning ? "正在创建并校验安全点" : latestRollbackPoint ? "重新创建更新前安全点" : "创建更新前安全点"}
          </Button>
          <p className="update-action-card__boundary">复制服务端程序和 Galaxy，只创建安全点；不会恢复文件，也不会开始更新。</p>
          <Button className="update-action-card__button" variant="ghost" size="lg" onClick={onBack}><Settings size={18} />返回配置页安装或升级管理组件</Button>
          {failedOperation && <div className="update-operation-error" role="alert"><strong>{failedOperation.error?.message ?? "操作失败"}</strong><span>错误码：{failedOperation.error?.code ?? "UNKNOWN"}</span>{failedOperation.error?.details?.stage && <span>失败阶段：{failedOperation.error.details.stage}</span>}{failedOperation.error?.details?.systemError && <details><summary>查看系统返回原因</summary><pre>{failedOperation.error.details.systemError}</pre></details>}{failedOperation.error?.details?.logExcerpt && <details><summary>查看 SteamCMD 返回日志</summary><pre>{failedOperation.error.details.logExcerpt}</pre></details>}</div>}
        </Card>
      </div>

      {(checkResult || verificationResult || latestRollbackPoint) && <Card className="update-evidence-card"><h3>最近一次真实检查证据</h3><div className="update-evidence-grid">{checkResult && <section><strong>Steam 官方版本</strong><span>App {checkResult.appId} · {checkResult.branch}</span>{checkResult.evidence.map((item) => <small key={item}>{item}</small>)}</section>}{verificationResult && <section><strong>本地文件校验</strong><span>{verificationResult.files.length} 个必需文件已读取</span>{verificationResult.files.map((file) => <small key={file.path} title={file.path}>{file.name} · {(file.sizeBytes / 1024 / 1024).toFixed(1)} MB · SHA-256 {file.sha256.slice(0, 12)}…</small>)}</section>}{latestRollbackPoint && <section><strong>更新前安全点</strong><span>{latestRollbackPoint.serverFileCount + latestRollbackPoint.galaxyFileCount} 个文件 · {formatDataSize(latestRollbackPoint.totalBytes)}</span><small title={latestRollbackPoint.storagePath}>独立路径：{latestRollbackPoint.storagePath}</small><small title={latestRollbackPoint.galaxyDirectory}>Galaxy：{latestRollbackPoint.galaxyDirectory}</small><small>清单 SHA-256：{latestRollbackPoint.manifestSha256.slice(0, 16)}…</small><small>已全量复核；尚未执行恢复，也未开始更新</small></section>}</div></Card>}

      <Card className="safe-update-flow">
        <h3>安全更新流程</h3>
        <ol>
          {flow.map((item, index) => {
            const Icon = item.icon;
            return (
              <li key={item.title}>
                <span className={`safe-update-flow__icon${item.blue ? " safe-update-flow__icon--blue" : ""}`}><Icon size={20} /></span>
                <div><strong>{item.title}</strong><span>{item.detail}</span></div>
                {index < flow.length - 1 && <span className="safe-update-flow__arrow">→</span>}
              </li>
            );
          })}
        </ol>
      </Card>
    </div>
  );
}




