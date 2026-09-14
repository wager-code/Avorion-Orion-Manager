import { SquareTerminal, Circle, ArrowLeft, ArrowRight, Box, CheckCircle2, Download, FolderOpen, HardDrive, Info, LoaderCircle, ShieldCheck, AlertCircle } from "lucide-react";
import { useEffect, useRef, useState } from "react";
import { ServerDirectoryPicker } from "../../components/ServerDirectoryPicker";
import { apiFetch, createIdempotencyKey } from "./serverUpdateApi";
import { Button, Card, StatusPill } from "../../components/ui";
async function readApiError(response: Response) {
  const body = await response.json().catch(() => null) as { error?: { message?: string } } | null;
  return body?.error?.message || `请求失败（HTTP ${response.status}）`;
}

import type { AvorionServerInstallationResult, InstallOperation, SteamCmdInstallationResult } from "./types";
import { isAvorionServerInstallationResult, isSteamCmdInstallationResult } from "./utils";
import { PathFlowHeading } from "./PathFlowHeading";
import { SummaryItem } from "./UpdateSummaryComponents";

function isWindowsAbsolutePath(value: string) {
  return /^[a-zA-Z]:\\[^<>:"|?*]+/.test(value.trim());
}

function joinWindowsPath(parent: string, child: string) {
  return `${parent.replace(/[\\/]+$/, "")}\\${child}`;
}

export function FreshInstallView({
  step,
  steamCmdDirectory,
  serverDirectory,
  onSteamCmdDirectoryChange,
  onServerDirectoryChange,
  onStepChange,
  onBack,
  operationId,
  onOperationStarted,
  onInstalled,
  onServerInstalled,
}: {
  step: number;
  steamCmdDirectory: string;
  serverDirectory: string;
  onSteamCmdDirectoryChange: (value: string) => void;
  onServerDirectoryChange: (value: string) => void;
  onStepChange: (step: 1 | 2) => void;
  onBack: () => void;
  operationId: string | null;
  onOperationStarted: (operationId: string) => void;
  onInstalled: (result: SteamCmdInstallationResult) => void;
  onServerInstalled: (result: AvorionServerInstallationResult & { steamCmdPath: string }) => void;
}) {
  const [showErrors, setShowErrors] = useState(false);
  const [pickerTarget, setPickerTarget] = useState<"steamcmd" | "server" | null>(null);
  const [installOperation, setInstallOperation] = useState<InstallOperation | null>(null);
  const [installError, setInstallError] = useState<string | null>(null);
  const [startingInstall, setStartingInstall] = useState(false);
  const installedNotified = useRef<string | null>(null);
  const installActionErrorRef = useRef<HTMLSpanElement | null>(null);
  const steamPathValid = isWindowsAbsolutePath(steamCmdDirectory);
  const serverPathValid = isWindowsAbsolutePath(serverDirectory);
  const pathsDifferent = steamCmdDirectory.trim().toLowerCase() !== serverDirectory.trim().toLowerCase();
  const valid = steamPathValid && serverPathValid && pathsDifferent;

  useEffect(() => {
    if (!operationId) {
      setInstallOperation(null);
      return;
    }
    let disposed = false;
    let timer: number | null = null;
    const poll = async () => {
      try {
        const response = await fetch(`/api/v1/operations/${encodeURIComponent(operationId)}`, {
          headers: { Accept: "application/json" },
        });
        if (!response.ok) throw new Error(await readApiError(response));
        const operation = await response.json() as InstallOperation;
        if (disposed) return;
        setInstallOperation(operation);
        if (operation.type === "steamcmd.install" && operation.request?.plannedServerDirectory) {
          onServerDirectoryChange(operation.request.plannedServerDirectory);
        }
        setInstallError(null);
        if (operation.status === "succeeded" && isSteamCmdInstallationResult(operation.result) && installedNotified.current !== operation.operationId) {
          installedNotified.current = operation.operationId;
          onInstalled(operation.result);
        }
        if (operation.status === "queued" || operation.status === "running") {
          timer = window.setTimeout(() => void poll(), 750);
        }
      } catch (caught) {
        if (disposed) return;
        setInstallError(caught instanceof Error ? caught.message : "无法读取 SteamCMD 安装进度");
        timer = window.setTimeout(() => void poll(), 1500);
      }
    };
    void poll();
    return () => {
      disposed = true;
      if (timer) window.clearTimeout(timer);
    };
  }, [onInstalled, onServerDirectoryChange, operationId]);

  const startSteamCmdInstall = async (retry = false) => {
    if (startingInstall || installOperation?.status === "queued" || installOperation?.status === "running") return;
    setStartingInstall(true);
    setInstallError(null);
    try {
      const requestedDirectory = installOperation?.request?.installDirectory ?? steamCmdDirectory;
      const storageKey = "avorion.install.steamcmd.idempotency";
      let idempotencyKey = "";
      try {
        const saved = JSON.parse(window.sessionStorage.getItem(storageKey) || "null") as { path?: string; key?: string; createdAt?: number } | null;
        const recent = typeof saved?.createdAt === "number" && Date.now() - saved.createdAt < 5 * 60 * 1000;
        if (!retry && recent && saved?.path === requestedDirectory && saved.key) idempotencyKey = saved.key;
        if (!idempotencyKey) {
          idempotencyKey = createIdempotencyKey("steamcmd");
          window.sessionStorage.setItem(storageKey, JSON.stringify({ path: requestedDirectory, key: idempotencyKey, createdAt: Date.now() }));
        }
      } catch {
        idempotencyKey = createIdempotencyKey("steamcmd");
      }
      const response = await apiFetch("/api/v1/servers/local/installations/steamcmd", {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          Accept: "application/json",
          "Idempotency-Key": idempotencyKey,
        },
        body: JSON.stringify({
          installDirectory: requestedDirectory,
          plannedServerDirectory: serverDirectory,
        }),
      });
      if (!response.ok) throw new Error(await readApiError(response));
      const accepted = await response.json() as { operationId: string };
      try { window.sessionStorage.removeItem(storageKey); } catch { }
      setInstallOperation(null);
      onOperationStarted(accepted.operationId);
    } catch (caught) {
      const message = caught instanceof Error ? caught.message : "SteamCMD 安装请求失败";
      setInstallError(message);
      window.requestAnimationFrame(() => {
        installActionErrorRef.current?.focus();
        installActionErrorRef.current?.scrollIntoView({ block: "nearest" });
      });
    } finally {
      setStartingInstall(false);
    }
  };

  const operationResult = installOperation?.result ?? null;
  const steamCmdResult: SteamCmdInstallationResult | null = isSteamCmdInstallationResult(operationResult)
    ? operationResult
    : null;
  const avorionServerResult: AvorionServerInstallationResult | null = isAvorionServerInstallationResult(operationResult)
    ? operationResult
    : null;
  const steamCmdExecutable = steamCmdResult?.executablePath ?? installOperation?.request?.steamCmdPath ?? "";

  const startAvorionServerInstall = async (retry = false) => {
    if (!steamCmdExecutable || startingInstall || installOperation?.status === "queued" || installOperation?.status === "running") return;
    setStartingInstall(true);
    setInstallError(null);
    try {
      const requestedDirectory = installOperation?.type === "avorion.install"
        ? installOperation.request?.installDirectory ?? serverDirectory
        : serverDirectory;
      const requestIdentity = `${steamCmdExecutable.toLowerCase()}|${requestedDirectory.toLowerCase()}`;
      const storageKey = "avorion.install.server.idempotency";
      let idempotencyKey = "";
      try {
        const saved = JSON.parse(window.sessionStorage.getItem(storageKey) || "null") as { identity?: string; key?: string; createdAt?: number } | null;
        const recent = typeof saved?.createdAt === "number" && Date.now() - saved.createdAt < 5 * 60 * 1000;
        if (!retry && recent && saved?.identity === requestIdentity && saved.key) idempotencyKey = saved.key;
        if (!idempotencyKey) {
          idempotencyKey = createIdempotencyKey("avorion");
          window.sessionStorage.setItem(storageKey, JSON.stringify({ identity: requestIdentity, key: idempotencyKey, createdAt: Date.now() }));
        }
      } catch {
        idempotencyKey = createIdempotencyKey("avorion");
      }
      const response = await apiFetch("/api/v1/servers/local/installations/avorion-server", {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          Accept: "application/json",
          "Idempotency-Key": idempotencyKey,
        },
        body: JSON.stringify({ steamCmdPath: steamCmdExecutable, installDirectory: requestedDirectory }),
      });
      if (!response.ok) throw new Error(await readApiError(response));
      const accepted = await response.json() as { operationId: string };
      try { window.sessionStorage.removeItem(storageKey); } catch { }
      setInstallOperation(null);
      installedNotified.current = null;
      onOperationStarted(accepted.operationId);
    } catch (caught) {
      const message = caught instanceof Error ? caught.message : "Avorion 服务端安装请求失败";
      setInstallError(message);
      window.requestAnimationFrame(() => {
        installActionErrorRef.current?.focus();
        installActionErrorRef.current?.scrollIntoView({ block: "nearest" });
      });
    } finally {
      setStartingInstall(false);
    }
  };

  const operationRunning = installOperation?.status === "queued" || installOperation?.status === "running";
  const operationSucceeded = installOperation?.status === "succeeded";
  const operationFailed = installOperation?.status === "failed" || installOperation?.status === "cancelled";
  const isServerOperation = installOperation?.type === "avorion.install";
  const steamCmdReady = isServerOperation || (operationSucceeded && steamCmdResult !== null);
  const serverReady = operationSucceeded && avorionServerResult !== null;
  const operationProgress = Math.round(installOperation?.progressPercent ?? (installOperation?.status === "queued" ? 0 : 0));
  const operationInstallDirectory = installOperation?.result?.installDirectory ?? installOperation?.request?.installDirectory ?? steamCmdDirectory;
  const operationStepLabels: Record<string, string> = {
    "validating-target": "正在验证目标目录",
    "validating-existing-installation": "正在检查现有 SteamCMD",
    "hashing-existing-executable": "正在校验现有 SteamCMD 文件",
    "existing-installation-ready": "现有 SteamCMD 已验证，可直接复用",
    "downloading-official-archive": "正在从 Valve 官方 CDN 下载",
    "verifying-archive": "正在校验下载归档",
    "verifying-valve-signature": "正在验证 Valve 数字签名",
    "validating-installation": "正在验证 SteamCMD 与服务端目标目录",
    "starting-steamcmd": "正在启动已验证的 SteamCMD",
    "downloading-avorion-server": "正在下载并验证 Avorion Dedicated Server",
    "restarting-steamcmd-after-self-update": "SteamCMD 首次自更新完成，正在安全重启一次",
    "verifying-steam-result": "正在核验 App 565060 安装结果",
    "publishing-installation": "正在发布已校验的服务端文件",
    "validating-existing-server": "正在检查现有 Avorion 服务端",
    "hashing-existing-server": "正在校验现有服务端文件",
    "existing-server-ready": "现有 Avorion 服务端已验证，可直接复用",
    completed: "安装完成",
  };

  const continueToReview = () => {
    if (!valid) {
      setShowErrors(true);
      return;
    }
    onStepChange(2);
  };

  if (step === 2) {
    return (
      <div className="path-flow-page">
        <PathFlowHeading title="全新安装 · 安装组件" detail="先安全安装 SteamCMD，再由它匿名安装官方 App 565060；整个流程不会启动 Avorion 服务端。" />
        <Card className="setup-update-summary setup-update-summary--three" aria-label="全新安装状态">
          <SummaryItem icon={SquareTerminal} label="SteamCMD" value={steamCmdReady ? steamCmdResult?.reusedExisting ? "已验证并复用" : "已安全安装" : operationFailed ? "安装失败" : operationRunning ? "正在检查或安装" : "等待检查"} tone={steamCmdReady ? "success" : operationFailed ? "danger" : "warning"} />
          <SummaryItem icon={Box} label="Avorion 服务端" value={serverReady ? avorionServerResult?.reusedExisting ? "已验证并复用" : "已安装" : isServerOperation && operationFailed ? "安装失败" : isServerOperation && operationRunning ? "正在检查或安装" : "等待检查"} tone={serverReady ? "success" : isServerOperation && operationFailed ? "danger" : "warning"} />
          <SummaryItem icon={ShieldCheck} label="执行策略" value="App 565060 · public" tone="info" />
        </Card>

        <div className="path-flow-grid">
          <Card className="path-review-card">
            <div className="path-card-heading"><div><h3>完整安装计划</h3><p>两个组件分别验证、分别执行；服务端只会落入已确认的独立目录。</p></div><StatusPill tone={serverReady ? "success" : operationFailed ? "danger" : operationRunning ? "info" : "warning"}>{serverReady ? "已完成" : operationFailed ? "失败" : operationRunning ? "执行中" : steamCmdReady ? "SteamCMD 已完成" : "尚未执行"}</StatusPill></div>
            <dl className="path-review-list">
              <div><dt>SteamCMD</dt><dd>{steamCmdExecutable || operationInstallDirectory}</dd></div>
              <div><dt>服务端安装目录</dt><dd>{isServerOperation ? operationInstallDirectory : serverDirectory}</dd></div>
              <div><dt>应用与通道</dt><dd>Avorion Dedicated Server · 565060 · public</dd></div>
              <div><dt>登录方式</dt><dd>Steam 匿名登录，不收集账号密码</dd></div>
            </dl>
            <div className="operation-plan-list">
              <div><span>1</span><p><strong>安装 SteamCMD</strong><small>固定 Valve 官方源、校验归档与 Valve 数字签名。</small></p></div>
              <div><span>2</span><p><strong>再次确认边界</strong><small>拒绝已有目标、根目录、网络路径、目录联接及路径互相包含。</small></p></div>
              <div><span>3</span><p><strong>安装 App 565060</strong><small>固定匿名登录与 public 通道，参数不会经过命令行 Shell。</small></p></div>
              <div><span>4</span><p><strong>验证并落盘</strong><small>SteamCMD 成功标记与服务端程序同时存在后，才原子发布目录。</small></p></div>
            </div>
          </Card>

          <Card className="path-safety-card">
            <h3>执行边界</h3>
            <div className="path-safety-list">
              <div><CheckCircle2 size={18} /><span><strong>不会覆盖已有目录</strong><small>发现冲突时停止并要求重新选择。</small></span></div>
              <div><CheckCircle2 size={18} /><span><strong>不会自动启动服务端</strong><small>安装成功后仍需完成 Galaxy、端口与管理配置。</small></span></div>
              <div><ShieldCheck size={18} /><span><strong>浏览器断线不取消任务</strong><small>正式执行后由 Operation ID 恢复状态。</small></span></div>
            </div>
            {operationRunning && <div className="steamcmd-operation" role="status"><div><LoaderCircle className="spin" size={20} /><strong>{operationStepLabels[installOperation?.currentStep ?? ""] ?? "安装任务正在运行"}</strong><b>{operationProgress}%</b></div><progress max="100" value={operationProgress} /><small>Operation ID：{installOperation?.operationId}</small></div>}
            {operationSucceeded && steamCmdResult && <div className="setup-basic-note"><CheckCircle2 size={20} /><span><strong>{steamCmdResult.reusedExisting ? "现有 SteamCMD 已验证并复用：" : "Valve 签名验证通过："}</strong>{steamCmdResult.executablePath}<br />{steamCmdResult.reusedExisting ? "文件" : "归档"} SHA-256：{steamCmdResult.reusedExisting ? steamCmdResult.executableSha256 : steamCmdResult.archiveSha256}</span></div>}
            {serverReady && avorionServerResult && <div className="setup-basic-note"><CheckCircle2 size={20} /><span><strong>{avorionServerResult.reusedExisting ? "现有 Avorion Dedicated Server 已验证并复用：" : "Avorion Dedicated Server 已验证："}</strong>{avorionServerResult.executablePath}<br />App {avorionServerResult.appId} · {avorionServerResult.branch}{avorionServerResult.executableSha256 && <><br />文件 SHA-256：{avorionServerResult.executableSha256}</>}</span></div>}
            {operationFailed && <div className="setup-blocked-note"><AlertCircle size={20} /><div><strong>{installOperation?.error?.code ?? "INSTALL_FAILED"}</strong><span>{installOperation?.error?.message ?? "安装未完成，请查看失败原因后重试。"}</span>{installOperation?.error?.details?.logExcerpt && <details className="operation-error-log"><summary>查看 SteamCMD 执行日志</summary><pre>{installOperation.error.details.logExcerpt}</pre></details>}</div></div>}
            {!installOperation && !installError && <div className="setup-basic-note"><ShieldCheck size={20} /><span><strong>两阶段安装已解锁：</strong>每一步都需要前一步真实成功后才能继续。</span></div>}
            {installError && <div className="setup-blocked-note"><AlertCircle size={20} /><div><strong>安装请求失败</strong><span>{installError}</span></div></div>}
          </Card>
        </div>

        <Card className="path-flow-actions">
          <Button variant="ghost" size="lg" onClick={() => onStepChange(1)} disabled={operationRunning}><ArrowLeft size={18} />返回修改路径</Button>
          <div>
            {!steamCmdReady && <Button variant="primary" size="lg" onClick={() => void startSteamCmdInstall(operationFailed)} disabled={startingInstall || operationRunning}>{startingInstall || operationRunning ? <LoaderCircle className="spin" size={18} /> : <Download size={18} />}{startingInstall ? "正在提交" : operationRunning ? "正在检查或安装 SteamCMD" : operationFailed ? "重新检查 SteamCMD" : "检查并安装 SteamCMD"}</Button>}
            {steamCmdReady && !serverReady && <Button variant="primary" size="lg" onClick={() => void startAvorionServerInstall(isServerOperation && operationFailed)} disabled={startingInstall || operationRunning}>{startingInstall || operationRunning ? <LoaderCircle className="spin" size={18} /> : <Download size={18} />}{startingInstall ? "正在提交" : operationRunning ? "正在检查或安装服务端" : isServerOperation && operationFailed ? "重新检查服务端" : "检查并安装 Avorion 服务端"}</Button>}
            {serverReady && avorionServerResult && <Button variant="primary" size="lg" onClick={() => onServerInstalled({ ...avorionServerResult, steamCmdPath: installOperation?.request?.steamCmdPath ?? steamCmdExecutable })}><CheckCircle2 size={18} />验证路径并继续配置</Button>}
            {installError
              ? <span ref={installActionErrorRef} className="path-flow-actions__error" role="alert" aria-live="assertive" tabIndex={-1}><AlertCircle size={14} />{installError}</span>
              : <span>{serverReady ? "不会启动服务端；下一步继续服务器配置" : steamCmdReady ? "将运行 SteamCMD 安装官方 App 565060，不会启动服务端" : "先安装 SteamCMD，不会启动任何服务端程序"}</span>}
          </div>
        </Card>
      </div>
    );
  }

  return (
    <div className="path-flow-page">
      <PathFlowHeading title="全新安装" detail="为尚未安装 SteamCMD 和 Avorion 服务端的用户准备安装位置。" />
      <Card className="setup-update-summary setup-update-summary--three" aria-label="全新安装环境">
        <SummaryItem icon={SquareTerminal} label="SteamCMD" value="未安装" tone="warning" />
        <SummaryItem icon={Box} label="Avorion 服务端" value="未安装" tone="warning" />
        <SummaryItem icon={HardDrive} label="安装位置" value="待确认" tone="warning" />
      </Card>

      <div className="path-flow-grid">
        <Card className="path-form-card" aria-labelledby="fresh-install-paths-title">
          <div className="path-card-heading"><div><h3 id="fresh-install-paths-title">选择安装位置</h3><p>可直接浏览服务器磁盘并自定义目标文件夹；正式执行前还会验证权限和空间。</p></div><StatusPill tone="info">步骤 1 / 2</StatusPill></div>
          {showErrors && !valid && <div className="path-error-summary" role="alert" tabIndex={-1}><strong>请检查安装路径</strong><span>两个目录都必须是有效的 Windows 绝对路径，并且不能相同。</span></div>}
          <div className="path-form-fields">
            <label>
              <span>SteamCMD 安装目录 <b>*</b></span>
              <div className="path-input-row"><input aria-invalid={showErrors && !steamPathValid} value={steamCmdDirectory} onChange={(event) => onSteamCmdDirectoryChange(event.target.value)} onBlur={() => setShowErrors(true)} /><button type="button" onClick={() => setPickerTarget("steamcmd")}><FolderOpen size={15} />选择目录</button></div>
              <small>选择服务器上的父目录后，可修改即将创建的文件夹名称。</small>
              {showErrors && !steamPathValid && <em>请输入类似 C:\AvorionAdmin\tools\steamcmd 的绝对路径。</em>}
            </label>
            <label>
              <span>Avorion 服务端安装目录 <b>*</b></span>
              <div className="path-input-row"><input aria-invalid={showErrors && !serverPathValid} value={serverDirectory} onChange={(event) => onServerDirectoryChange(event.target.value)} onBlur={() => setShowErrors(true)} /><button type="button" onClick={() => setPickerTarget("server")}><FolderOpen size={15} />选择目录</button></div>
              <small>系统将在所选父目录下创建目标文件夹；Galaxy 存档位置稍后配置。</small>
              {showErrors && !serverPathValid && <em>请输入类似 D:\AvorionServer 的绝对路径。</em>}
            </label>
          </div>
          {showErrors && !pathsDifferent && <div className="setup-inline-error" role="alert">SteamCMD 与服务端不能使用同一个目录。</div>}
          <div className="setup-basic-note"><Info size={19} /><span>此步骤只生成安装计划。目标文件夹会在正式安装通过安全检查后创建。</span></div>
        </Card>

        <Card className="path-safety-card">
          <h3>将安装的组件</h3>
          <div className="component-preview-list">
            <div><span className="component-preview-list__icon"><SquareTerminal size={20} /></span><p><strong>SteamCMD</strong><small>用于安装、更新和验证服务端文件。</small></p><StatusPill tone="warning" compact>待安装</StatusPill></div>
            <div><span className="component-preview-list__icon"><Box size={20} /></span><p><strong>Avorion Dedicated Server</strong><small>安装稳定通道的服务端文件。</small></p><StatusPill tone="warning" compact>待安装</StatusPill></div>
          </div>
          <div className="setup-overview-note"><Info size={18} /><span>本页不会安装游戏客户端，也不会创建未确认的附加组件。</span></div>
        </Card>
      </div>

      <Card className="path-flow-actions">
        <Button variant="ghost" size="lg" onClick={onBack}><ArrowLeft size={18} />返回接入方式</Button>
        <Button variant="primary" size="lg" onClick={continueToReview}>下一步：执行前检查<ArrowRight size={18} /></Button>
      </Card>
      <ServerDirectoryPicker
        open={pickerTarget !== null}
        onOpenChange={(open) => { if (!open) setPickerTarget(null); }}
        title={pickerTarget === "steamcmd" ? "选择 SteamCMD 安装位置" : "选择服务端安装位置"}
        description="这里浏览的是 Server Agent 所在服务器的磁盘。请选择父目录，并确认目标文件夹名称。"
        proposedFolderName={pickerTarget === "steamcmd" ? "steamcmd" : "AvorionServer"}
        confirmLabel="使用此安装位置"
        onSelect={(path) => {
          if (pickerTarget === "steamcmd") onSteamCmdDirectoryChange(path);
          if (pickerTarget === "server") onServerDirectoryChange(path);
        }}
      />
    </div>
  );
}

export function ManualPathsView({
  step,
  steamCmdPath,
  serverDirectory,
  onSteamCmdPathChange,
  onServerDirectoryChange,
  onStepChange,
  onBack,
  busy,
  apiError,
  onValidate,
}: {
  step: number;
  steamCmdPath: string;
  serverDirectory: string;
  onSteamCmdPathChange: (value: string) => void;
  onServerDirectoryChange: (value: string) => void;
  onStepChange: (step: 1 | 2) => void;
  onBack: () => void;
  busy: boolean;
  apiError: string | null;
  onValidate: (steamCmdPath: string, serverDirectory: string) => void;
}) {
  const [showErrors, setShowErrors] = useState(false);
  const [pickerTarget, setPickerTarget] = useState<"steamcmd" | "server" | null>(null);
  const steamPathValid = isWindowsAbsolutePath(steamCmdPath) && /steamcmd\.exe$/i.test(steamCmdPath.trim());
  const serverPathValid = isWindowsAbsolutePath(serverDirectory);
  const valid = steamPathValid && serverPathValid;

  const continueToReview = () => {
    if (!valid) {
      setShowErrors(true);
      return;
    }
    onStepChange(2);
  };

  if (step === 2) {
    return (
      <div className="path-flow-page">
        <PathFlowHeading title="已有路径 · 验证前确认" detail="确认要交给 Server Agent 验证的路径；当前不会保存配置或修改文件。" />
        <Card className="setup-update-summary setup-update-summary--three" aria-label="已有路径验证状态">
          <SummaryItem icon={SquareTerminal} label="SteamCMD" value="等待验证" tone="warning" />
          <SummaryItem icon={Box} label="Avorion 服务端" value="等待验证" tone="warning" />
          <SummaryItem icon={ShieldCheck} label="配置保存" value="尚未执行" tone="warning" />
        </Card>
        {apiError && <Card className="update-api-error" role="alert"><AlertCircle size={20} /><div><strong>真实验证未通过</strong><span>{apiError}</span></div></Card>}
        <div className="path-flow-grid">
          <Card className="path-review-card">
            <div className="path-card-heading"><div><h3>待验证路径</h3><p>只有真实检查通过后，页面才会显示版本和可用状态。</p></div><StatusPill tone="warning">未验证</StatusPill></div>
            <dl className="path-review-list">
              <div><dt>SteamCMD 可执行文件</dt><dd>{steamCmdPath}</dd></div>
              <div><dt>Avorion 服务端目录</dt><dd>{serverDirectory}</dd></div>
            </dl>
            <div className="operation-plan-list">
              <div><span>1</span><p><strong>检查文件与目录</strong><small>确认 steamcmd.exe 和 AvorionServer.exe 是否存在。</small></p></div>
              <div><span>2</span><p><strong>检查读取权限与版本</strong><small>验证文件可读取并识别可用版本；写入权限在正式执行前检查。</small></p></div>
              <div><span>3</span><p><strong>保存验证结果</strong><small>只在验证通过后设置为当前服务器路径。</small></p></div>
            </div>
          </Card>
          <Card className="path-safety-card">
            <h3>验证规则</h3>
            <div className="path-safety-list">
              <div><CheckCircle2 size={18} /><span><strong>不会移动文件</strong><small>路径确认只读取并验证现有安装。</small></span></div>
              <div><CheckCircle2 size={18} /><span><strong>不会立即更新</strong><small>验证路径不等于执行 SteamCMD 更新。</small></span></div>
              <div><ShieldCheck size={18} /><span><strong>失败显示真实原因</strong><small>无法读取时显示未知或不可用，不回退 Mock。</small></span></div>
            </div>
            <div className="setup-basic-note"><ShieldCheck size={20} /><span><strong>由 Server Agent 真实验证：</strong>只读取文件与目录并保存验证后的路径；不会移动文件，也不会执行 SteamCMD。</span></div>
          </Card>
        </div>
        <Card className="path-flow-actions">
          <Button variant="ghost" size="lg" onClick={() => onStepChange(1)}><ArrowLeft size={18} />返回修改路径</Button>
          <div><Button variant="primary" size="lg" onClick={() => onValidate(steamCmdPath, serverDirectory)} disabled={busy}>{busy ? <LoaderCircle className="spin" size={18} /> : <ShieldCheck size={18} />}{busy ? "正在验证" : "验证并使用"}</Button><span>验证通过后保存到本机管理数据库</span></div>
        </Card>
      </div>
    );
  }

  return (
    <div className="path-flow-page">
      <PathFlowHeading title="选择已有路径" detail="接入已经安装的 SteamCMD 与 Avorion 服务端，不移动或覆盖现有文件。" />
      <Card className="setup-update-summary setup-update-summary--three" aria-label="已有安装接入状态">
        <SummaryItem icon={SquareTerminal} label="SteamCMD" value="待选择" tone="warning" />
        <SummaryItem icon={Box} label="Avorion 服务端" value="待选择" tone="warning" />
        <SummaryItem icon={ShieldCheck} label="路径验证" value="尚未开始" tone="warning" />
      </Card>

      <div className="path-flow-grid">
        <Card className="path-form-card" aria-labelledby="manual-paths-title">
          <div className="path-card-heading"><div><h3 id="manual-paths-title">选择现有安装位置</h3><p>优先浏览服务器磁盘选择，也可以手动输入完整路径。</p></div><StatusPill tone="info">步骤 1 / 2</StatusPill></div>
          {apiError && <div className="path-error-summary" role="alert"><strong>真实检测结果</strong><span>{apiError}</span></div>}
          {showErrors && !valid && <div className="path-error-summary" role="alert" tabIndex={-1}><strong>路径格式不正确</strong><span>请填写 steamcmd.exe 完整路径和 Avorion 服务端绝对目录。</span></div>}
          <div className="path-form-fields">
            <label>
              <span>SteamCMD 可执行文件 <b>*</b></span>
              <div className="path-input-row"><input aria-invalid={showErrors && !steamPathValid} placeholder="例如 C:\\SteamCMD\\steamcmd.exe" value={steamCmdPath} onChange={(event) => onSteamCmdPathChange(event.target.value)} onBlur={() => setShowErrors(true)} /><button type="button" onClick={() => setPickerTarget("steamcmd")}><FolderOpen size={15} />选择目录</button></div>
              <small>请选择包含 steamcmd.exe 的目录，系统会自动补全文件名。</small>
              {showErrors && !steamPathValid && <em>请输入以 steamcmd.exe 结尾的 Windows 绝对路径。</em>}
            </label>
            <label>
              <span>Avorion 服务端安装目录 <b>*</b></span>
              <div className="path-input-row"><input aria-invalid={showErrors && !serverPathValid} placeholder="例如 D:\\AvorionServer" value={serverDirectory} onChange={(event) => onServerDirectoryChange(event.target.value)} onBlur={() => setShowErrors(true)} /><button type="button" onClick={() => setPickerTarget("server")}><FolderOpen size={15} />选择目录</button></div>
              <small>目录中应包含 AvorionServer.exe；下一步只做验证前确认。</small>
              {showErrors && !serverPathValid && <em>请输入有效的 Windows 绝对目录。</em>}
            </label>
          </div>
          <div className="setup-basic-note"><Info size={19} /><span>目录来自 Server Agent 的实时读取；读取失败时会显示真实原因，不会提供 Mock 目录。</span></div>
        </Card>

        <Card className="path-safety-card">
          <h3>接入后检查</h3>
          <div className="path-safety-list">
            <div><Circle size={18} /><span><strong>SteamCMD 文件</strong><small>存在性、可执行性与版本。</small></span></div>
            <div><Circle size={18} /><span><strong>服务端目录</strong><small>AvorionServer.exe、权限与版本。</small></span></div>
            <div><Circle size={18} /><span><strong>目录安全</strong><small>路径冲突、空间和已有数据。</small></span></div>
          </div>
          <div className="setup-overview-note"><Info size={18} /><span>未读取到的结果会显示“未知”或“不可用”，不会默认显示绿色正常。</span></div>
        </Card>
      </div>

      <Card className="path-flow-actions">
        <Button variant="ghost" size="lg" onClick={onBack}><ArrowLeft size={18} />返回接入方式</Button>
        <Button variant="primary" size="lg" onClick={continueToReview}>下一步：验证前确认<ArrowRight size={18} /></Button>
      </Card>
      <ServerDirectoryPicker
        open={pickerTarget !== null}
        onOpenChange={(open) => { if (!open) setPickerTarget(null); }}
        title={pickerTarget === "steamcmd" ? "选择 SteamCMD 所在目录" : "选择 Avorion 服务端目录"}
        description={pickerTarget === "steamcmd" ? "请选择服务器上包含 steamcmd.exe 的目录。" : "请选择服务器上包含 AvorionServer.exe 的安装目录。"}
        confirmLabel="使用当前目录"
        onSelect={(path) => {
          if (pickerTarget === "steamcmd") onSteamCmdPathChange(joinWindowsPath(path, "steamcmd.exe"));
          if (pickerTarget === "server") onServerDirectoryChange(path);
        }}
      />
    </div>
  );
}



