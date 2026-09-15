import { Minus, Plus, Play, ArrowLeft, ArrowRight, CheckCircle2, Circle, Database, FolderOpen, Gamepad2, Info, LoaderCircle, ServerCog, ShieldCheck, SquareTerminal, Box, AlertCircle } from "lucide-react";
import { useEffect, useState } from "react";
import { ServerDirectoryPicker } from "../../components/ServerDirectoryPicker";
import { apiFetch, createIdempotencyKey } from "../../lib/api";
import { Button, Card, StatusPill } from "../../components/ui";
import type { ServerSetupDraftConfiguration, ServerSetupPreflightResult, ServerSetupApplicationOperation, ServerInitializationOperation, ServerInitializationResult, SetupStep } from "./types";
import { clearStoredOperationId, activeOperationStorageKeys } from "./operationStorage";
import { PathFlowHeading } from "./PathFlowHeading";
import { readApiError } from "./utils";

export function SetupWizardView({
  step,
  serverName,
  galaxyName,
  galaxyMode,
  maxPlayers,
  galaxyDirectory,
  onServerNameChange,
  onGalaxyNameChange,
  onGalaxyModeChange,
  onMaxPlayersChange,
  onGalaxyDirectoryChange,
  listenAddress,
  gamePort,
  queryPort,
  rconPort,
  rconEnabled,
  rconPassword,
  allowFirewallChange,
  onListenAddressChange,
  onGamePortChange,
  onQueryPortChange,
  onRconPortChange,
  onRconEnabledChange,
  onRconPasswordChange,
  onAllowFirewallChange,
  onStepChange,
  onBack,
  onNotify,
  operationId,
  onOperationStarted,
  initializationOperationId,
  onInitializationStarted,
  onComplete,
}: {
  step: SetupStep;
  serverName: string;
  galaxyName: string;
  galaxyMode: "new" | "existing";
  maxPlayers: number;
  galaxyDirectory: string;
  onServerNameChange: (value: string) => void;
  onGalaxyNameChange: (value: string) => void;
  onGalaxyModeChange: (value: "new" | "existing") => void;
  onMaxPlayersChange: (value: number) => void;
  onGalaxyDirectoryChange: (value: string) => void;
  listenAddress: string;
  gamePort: number;
  queryPort: number;
  rconPort: number;
  rconEnabled: boolean;
  rconPassword: string;
  allowFirewallChange: boolean;
  onListenAddressChange: (value: string) => void;
  onGamePortChange: (value: number) => void;
  onQueryPortChange: (value: number) => void;
  onRconPortChange: (value: number) => void;
  onRconEnabledChange: (value: boolean) => void;
  onRconPasswordChange: (value: string) => void;
  onAllowFirewallChange: (value: boolean) => void;
  onStepChange: (step: SetupStep) => void;
  onBack: () => void;
  onNotify: (message: string) => void;
  operationId: string | null;
  onOperationStarted: (operationId: string) => void;
  initializationOperationId: string | null;
  onInitializationStarted: (operationId: string) => void;
  onComplete: () => void;
}) {
  const [galaxyPickerOpen, setGalaxyPickerOpen] = useState(false);
  const [setupBusy, setSetupBusy] = useState<"loading" | "saving" | "preflight" | "applying" | "initializing" | null>(null);
  const [setupError, setSetupError] = useState<string | null>(null);
  const [preflightResult, setPreflightResult] = useState<ServerSetupPreflightResult | null>(null);
  const [applicationOperation, setApplicationOperation] = useState<ServerSetupApplicationOperation | null>(null);
  const [applicationError, setApplicationError] = useState<string | null>(null);
  const [initializationPassword, setInitializationPassword] = useState("");
  const [initializationOperation, setInitializationOperation] = useState<ServerInitializationOperation | null>(null);
  const [initializationError, setInitializationError] = useState<string | null>(null);
  const portsValid = [gamePort, queryPort, rconPort].every((port) => port >= 1 && port <= 65535)
    && new Set([gamePort, queryPort, rconPort]).size === 3;
  const stepValid = step === 1
    ? serverName.trim().length > 0 && galaxyName.trim().length > 0 && galaxyDirectory.trim().length > 0
    : step === 2
      ? listenAddress.trim().length > 0 && portsValid && (!rconEnabled || rconPassword.length >= 8)
      : true;
  const steps = ["Galaxy 与基本信息", "网络与 RCON", "检查与启动"];

  const changePort = (setter: (value: number) => void) => (value: string) => {
    setter(Math.min(65535, Math.max(1, Number(value) || 1)));
  };

  const draftPayload = () => ({
    serverName,
    galaxyName,
    galaxyMode,
    maxPlayers,
    galaxyDirectory,
    listenAddress,
    gamePort,
    queryPort,
    rconEnabled,
    rconPort,
    allowFirewallChange,
    installManagementMod: false,
  });

  useEffect(() => {
    const controller = new AbortController();
    setSetupBusy("loading");
    void fetch("/api/v1/servers/local/server-setup/draft", {
      headers: { Accept: "application/json" },
      signal: controller.signal,
    }).then(async (response) => {
      if (!response.ok) throw new Error(await readApiError(response));
      return response.json() as Promise<{ draft: ServerSetupDraftConfiguration | null }>;
    }).then(({ draft }) => {
      if (!draft) return;
      onServerNameChange(draft.serverName);
      onGalaxyNameChange(draft.galaxyName);
      onGalaxyModeChange(draft.galaxyMode);
      onMaxPlayersChange(draft.maxPlayers);
      onGalaxyDirectoryChange(draft.galaxyDirectory);
      onListenAddressChange(draft.listenAddress);
      onGamePortChange(draft.gamePort);
      onQueryPortChange(draft.queryPort);
      onRconEnabledChange(draft.rconEnabled);
      onRconPortChange(draft.rconPort);
      onAllowFirewallChange(draft.allowFirewallChange);
    }).catch((caught) => {
      if (caught instanceof DOMException && caught.name === "AbortError") return;
      setSetupError(caught instanceof Error ? caught.message : "无法读取服务器配置草稿");
    }).finally(() => {
      if (!controller.signal.aborted) setSetupBusy(null);
    });
    return () => controller.abort();
  }, []);

  useEffect(() => {
    if (!operationId) {
      setApplicationOperation(null);
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
        const operation = await response.json() as ServerSetupApplicationOperation;
        if (disposed) return;
        setApplicationOperation(operation);
        setApplicationError(null);
        if (operation.status === "failed") setPreflightResult(null);
        if (operation.status === "succeeded" && operation.result?.mode === "server-ini-updated") {
          clearStoredOperationId(activeOperationStorageKeys.setup);
        }
        if (operation.status === "queued" || operation.status === "running") {
          timer = window.setTimeout(() => void poll(), 600);
        }
      } catch (caught) {
        if (disposed) return;
        setApplicationError(caught instanceof Error ? caught.message : "无法读取配置应用进度");
        timer = window.setTimeout(() => void poll(), 1500);
      }
    };
    void poll();
    return () => {
      disposed = true;
      if (timer) window.clearTimeout(timer);
    };
  }, [operationId]);

  useEffect(() => {
    if (!initializationOperationId) {
      setInitializationOperation(null);
      return;
    }
    let disposed = false;
    let timer: number | null = null;
    const poll = async () => {
      try {
        const response = await fetch(`/api/v1/operations/${encodeURIComponent(initializationOperationId)}`, {
          headers: { Accept: "application/json" },
        });
        if (!response.ok) throw new Error(await readApiError(response));
        const operation = await response.json() as ServerInitializationOperation;
        if (disposed) return;
        setInitializationOperation(operation);
        setInitializationError(null);
        if (operation.status === "succeeded") {
          clearStoredOperationId(activeOperationStorageKeys.initialization);
          clearStoredOperationId(activeOperationStorageKeys.setup);
        }
        if (operation.status === "queued" || operation.status === "running") {
          timer = window.setTimeout(() => void poll(), 600);
        }
      } catch (caught) {
        if (disposed) return;
        setInitializationError(caught instanceof Error ? caught.message : "无法读取首次初始化进度");
        timer = window.setTimeout(() => void poll(), 1500);
      }
    };
    void poll();
    return () => {
      disposed = true;
      if (timer) window.clearTimeout(timer);
    };
  }, [initializationOperationId]);

  const persistDraft = async () => {
    const response = await apiFetch("/api/v1/servers/local/server-setup/draft", {
      method: "PUT",
      headers: { "Content-Type": "application/json", Accept: "application/json" },
      body: JSON.stringify(draftPayload()),
    });
    if (!response.ok) throw new Error(await readApiError(response));
  };

  const saveDraft = async () => {
    if (setupBusy) return;
    setSetupBusy("saving");
    setSetupError(null);
    try {
      await persistDraft();
      onNotify("非敏感配置草稿已保存到本机管理数据库");
    } catch (caught) {
      setSetupError(caught instanceof Error ? caught.message : "保存配置草稿失败");
    } finally {
      setSetupBusy(null);
    }
  };

  const runPreflight = async () => {
    if (setupBusy) return;
    setSetupBusy("preflight");
    setSetupError(null);
    setPreflightResult(null);
    try {
      await persistDraft();
      const response = await apiFetch("/api/v1/servers/local/server-setup/preflight", {
        method: "POST",
        headers: { "Content-Type": "application/json", Accept: "application/json" },
        body: JSON.stringify({ ...draftPayload(), rconPassword: rconEnabled ? rconPassword : null }),
      });
      if (!response.ok) throw new Error(await readApiError(response));
      const result = await response.json() as ServerSetupPreflightResult;
      setPreflightResult(result);
      onNotify(result.valid ? "服务器配置真实预检通过" : "服务器配置预检发现需要修正的项目");
    } catch (caught) {
      setSetupError(caught instanceof Error ? caught.message : "服务器配置预检失败");
    } finally {
      setSetupBusy(null);
    }
  };

  const applyConfiguration = async () => {
    if (setupBusy || !preflightResult?.valid || applicationOperation?.status === "queued" || applicationOperation?.status === "running") return;
    setSetupBusy("applying");
    setSetupError(null);
    setApplicationError(null);
    try {
      const response = await apiFetch("/api/v1/servers/local/server-setup/applications", {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          Accept: "application/json",
          "Idempotency-Key": createIdempotencyKey("server-configure"),
        },
        body: JSON.stringify({ ...draftPayload(), rconPassword: rconEnabled ? rconPassword : null }),
      });
      if (!response.ok) throw new Error(await readApiError(response));
      const accepted = await response.json() as { operationId: string };
      onRconPasswordChange("");
      setApplicationOperation(null);
      onOperationStarted(accepted.operationId);
      onNotify("服务器配置 Operation 已创建；RCON 密码已从浏览器表单清除");
    } catch (caught) {
      setSetupError(caught instanceof Error ? caught.message : "服务器配置应用请求失败");
    } finally {
      setSetupBusy(null);
    }
  };

  const initializeGalaxy = async () => {
    if (setupBusy || initializationPassword.length < 8 || !applicationOperation?.result || applicationOperation.result.mode !== "launch-profile-ready") return;
    setSetupBusy("initializing");
    setSetupError(null);
    setInitializationError(null);
    try {
      const response = await apiFetch("/api/v1/servers/local/server-setup/initializations", {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          Accept: "application/json",
          "Idempotency-Key": createIdempotencyKey("server-initialize"),
        },
        body: JSON.stringify({ ...draftPayload(), rconPassword: initializationPassword }),
      });
      if (!response.ok) throw new Error(await readApiError(response));
      const accepted = await response.json() as { operationId: string };
      setInitializationPassword("");
      setInitializationOperation(null);
      onInitializationStarted(accepted.operationId);
      onNotify("首次初始化 Operation 已创建；RCON 密码已从浏览器表单清除");
    } catch (caught) {
      setInitializationError(caught instanceof Error ? caught.message : "首次初始化请求失败");
    } finally {
      setSetupBusy(null);
    }
  };

  const next = () => {
    if (step < 3 && stepValid) onStepChange((step + 1) as SetupStep);
  };

  const previous = () => {
    if (step === 1) onBack();
    else onStepChange((step - 1) as SetupStep);
  };

  const applicationRunning = applicationOperation?.status === "queued" || applicationOperation?.status === "running";
  const applicationSucceeded = applicationOperation?.status === "succeeded" && applicationOperation.result !== null;
  const applicationFailed = applicationOperation?.status === "failed";
  const applicationProgress = Math.round(applicationOperation?.progressPercent ?? 0);
  const initializationRunning = initializationOperation?.status === "queued" || initializationOperation?.status === "running";
  const initializationSucceeded = initializationOperation?.status === "succeeded" && initializationOperation.result !== null;
  const initializationFailed = initializationOperation?.status === "failed";
  const initializationProgress = Math.round(initializationOperation?.progressPercent ?? 0);
  const environmentVerified = preflightResult?.updateEnvironmentValid === true || applicationSucceeded;
  const applicationStepLabels: Record<string, string> = {
    "revalidating-setup": "重新验证路径、字段与端口",
    "building-launch-profile": "生成受控启动档案",
    "writing-launch-profile": "原子写入启动档案",
    "updating-server-ini": "原子更新 server.ini",
    "verifying-applied-configuration": "验证写入结果",
    completed: "配置应用完成",
  };
  const deferredActionLabels: Record<string, string> = {
    "game-listen-address": "游戏监听地址仅作预检意图记录，当前版本没有已验证的独立写入项",
    "steam-query-port": "Query 端口仅作预检意图记录，当前版本没有已验证的独立写入项",
    "first-galaxy-initialization": "首次启动并创建 Galaxy",
    "rcon-until-first-initialization": "首次初始化生成 server.ini 后再写入 RCON",
    "windows-firewall": "Windows 防火墙变更等待单独确认",
    "management-mod": "历史兼容项已忽略；本系统不会安装或植入自定义 MOD",
  };
  const initializationStepLabels: Record<string, string> = {
    "reapplying-launch-profile": "重新验证并锁定受控启动档案",
    "starting-first-initialization": "首次启动 Avorion 服务端",
    "server-ini-created": "等待 Galaxy 与 server.ini 创建完成",
    "waiting-for-server-ready": "等待游戏服务端完成首次启动",
    "server-ready": "游戏服务端已就绪，准备安全保存",
    "saving-initial-galaxy": "向服务端发送 /save",
    "waiting-for-safe-stop": "发送 /stop 并等待进程安全退出",
    "writing-rcon-after-safe-stop": "停服后原子写入 RCON 配置",
    "starting-configured-server": "按受控启动档案重新启动",
    "verifying-server-stability": "RCON 已连接，正在确认服务端持续稳定运行",
    "rcon-authenticated": "RCON 认证成功，服务器运行中",
    completed: "首次初始化完成",
  };
  const healthEvidenceLabels: Record<string, string> = {
    "process-running": "服务端进程正在运行",
    "server-ini-created": "server.ini 已由服务端首次生成",
    "console-save-sent": "首次 Galaxy 已执行 /save",
    "console-stop-confirmed": "首次进程已执行 /stop 并安全退出",
    "rcon-authenticated": "重启后 RCON 认证通过",
  };

  return (
    <div className="setup-wizard-page">
      <header className="setup-wizard-heading">
        <h2>完成服务器配置</h2>
        <p>{step === 1 ? "设置 Galaxy 与基础服务器信息。" : step === 2 ? "配置监听地址、游戏端口与 RCON。" : "核对并真实预检；确认后只应用已验证配置，不会自动启动服务器。"}</p>
      </header>

      {setupError && <Card className="update-api-error" role="alert"><AlertCircle size={20} /><div><strong>服务器配置操作失败</strong><span>{setupError}</span></div></Card>}

      <Card className="setup-wizard-steps" aria-label="服务器配置进度">
        {steps.map((stepLabel, index) => (
          <div className={`setup-wizard-step${index + 1 === step ? " setup-wizard-step--active" : index + 1 < step ? " setup-wizard-step--done" : ""}`} key={stepLabel}>
            <span>{index + 1}</span>
            <strong>{stepLabel}</strong>
            {index < steps.length - 1 && <i aria-hidden="true" />}
          </div>
        ))}
      </Card>

      <div className="setup-wizard-grid">
        {step === 1 && <Card className="setup-basic-card" aria-labelledby="setup-basic-title">
          <h3 id="setup-basic-title">Galaxy 与基本信息</h3>
          <p>选择创建新存档，或接入已有 Galaxy。</p>

          <div className="galaxy-mode-grid" role="radiogroup" aria-label="Galaxy 接入方式">
            <button
              className={galaxyMode === "new" ? "galaxy-mode galaxy-mode--active" : "galaxy-mode"}
              type="button"
              role="radio"
              aria-checked={galaxyMode === "new"}
              onClick={() => onGalaxyModeChange("new")}
            >
              <span className="galaxy-mode__radio" />
              <span><strong>创建新 Galaxy</strong><small>首次启动时创建新的游戏世界</small></span>
            </button>
            <button
              className={galaxyMode === "existing" ? "galaxy-mode galaxy-mode--active" : "galaxy-mode"}
              type="button"
              role="radio"
              aria-checked={galaxyMode === "existing"}
              onClick={() => onGalaxyModeChange("existing")}
            >
              <span className="galaxy-mode__radio" />
              <span><strong>使用已有 Galaxy</strong><small>选择本机已有的 Galaxy 存档目录</small></span>
            </button>
          </div>

          <div className="setup-basic-form">
            <label>
              <span>服务器显示名称 <b>*</b></span>
              <input value={serverName} onChange={(event) => onServerNameChange(event.target.value)} />
            </label>
            <label>
              <span>Galaxy 名称 <b>*</b></span>
              <input value={galaxyName} onChange={(event) => onGalaxyNameChange(event.target.value)} />
            </label>
            <label>
              <span>最大玩家数 <b>*</b></span>
              <span className="number-field">
                <input type="number" min={1} max={100} value={maxPlayers} onChange={(event) => onMaxPlayersChange(Math.min(100, Math.max(1, Number(event.target.value) || 1)))} />
                <button type="button" aria-label="减少玩家数" onClick={() => onMaxPlayersChange(Math.max(1, maxPlayers - 1))}><Minus size={17} /></button>
                <button type="button" aria-label="增加玩家数" onClick={() => onMaxPlayersChange(Math.min(100, maxPlayers + 1))}><Plus size={17} /></button>
              </span>
            </label>
            <label>
              <span>Galaxy 存档目录 <b>*</b></span>
              <span className="directory-field">
                <input
                  placeholder="尚未选择"
                  value={galaxyDirectory}
                  onChange={(event) => onGalaxyDirectoryChange(event.target.value)}
                />
                <button type="button" onClick={() => setGalaxyPickerOpen(true)}>选择目录</button>
              </span>
            </label>
          </div>

          <div className="setup-basic-note">
            <Info size={19} />
            <span>目录将在下一步前检查写入权限与可用空间；此页面不会立即创建或启动服务器。</span>
          </div>
        </Card>}

        {step === 2 && <Card className="setup-basic-card setup-network-card" aria-labelledby="setup-network-title">
          <h3 id="setup-network-title">网络与 RCON</h3>
          <p>端口检测与防火墙修改分开处理，不会自动开放系统端口。</p>

          <div className="setup-basic-form setup-basic-form--network">
            <label>
              <span>监听地址 <b>*</b></span>
              <input value={listenAddress} onChange={(event) => onListenAddressChange(event.target.value)} />
              <small>通常使用 0.0.0.0 监听全部网卡。</small>
            </label>
            <label>
              <span>游戏端口 <b>*</b></span>
              <input type="number" min={1} max={65535} value={gamePort} onChange={(event) => changePort(onGamePortChange)(event.target.value)} />
            </label>
            <label>
              <span>Steam Query 端口 <b>*</b></span>
              <input type="number" min={1} max={65535} value={queryPort} onChange={(event) => changePort(onQueryPortChange)(event.target.value)} />
            </label>
            <label>
              <span>RCON 端口 <b>*</b></span>
              <input type="number" min={1} max={65535} value={rconPort} disabled={!rconEnabled} onChange={(event) => changePort(onRconPortChange)(event.target.value)} />
            </label>
          </div>

          <div className="setup-toggle-row">
            <div><strong>启用 RCON</strong><span>服务器控制、广播和安全停服需要 RCON。</span></div>
            <button type="button" className={rconEnabled ? "task-switch task-switch--enabled" : "task-switch"} role="switch" aria-checked={rconEnabled} onClick={() => onRconEnabledChange(!rconEnabled)}><span /></button>
          </div>

          {rconEnabled && <label className="setup-password-field">
            <span>RCON 密码 <b>*</b></span>
            <input type="password" autoComplete="new-password" value={rconPassword} onChange={(event) => onRconPasswordChange(event.target.value)} placeholder="至少 8 位；不会显示在复核摘要中" />
            <small>密码不会通过查询接口返回，也不会写入日志或诊断包。</small>
          </label>}

          <label className="setup-checkbox-row">
            <input type="checkbox" checked={allowFirewallChange} onChange={(event) => onAllowFirewallChange(event.target.checked)} />
            <span><strong>允许后续单独请求修改 Windows 防火墙</strong><small>勾选不代表立即修改；执行前仍会再次确认。</small></span>
          </label>

          {!portsValid && <div className="setup-inline-error" role="alert">端口必须在 1–65535 之间，并且不能互相重复。</div>}
        </Card>}

        {step === 3 && <Card className="setup-basic-card setup-review-card" aria-labelledby="setup-review-title">
          <h3 id="setup-review-title">执行前确认</h3>
          <p>请核对将保存的配置，并由 Server Agent 检查真实路径、端口与安装环境。</p>

          <div className="setup-review-sections">
            <section>
              <header><strong>Galaxy 与基础信息</strong><button type="button" onClick={() => onStepChange(1)}>修改</button></header>
              <dl><div><dt>服务器名称</dt><dd>{serverName || "未填写"}</dd></div><div><dt>Galaxy</dt><dd>{galaxyName || "未填写"}</dd></div><div><dt>存档目录</dt><dd>{galaxyDirectory || "未选择"}</dd></div><div><dt>最大玩家数</dt><dd>{maxPlayers}</dd></div></dl>
            </section>
            <section>
              <header><strong>网络与 RCON</strong><button type="button" onClick={() => onStepChange(2)}>修改</button></header>
              <dl><div><dt>监听地址</dt><dd>{listenAddress}</dd></div><div><dt>游戏 / Query</dt><dd>{gamePort} / {queryPort}</dd></div><div><dt>RCON</dt><dd>{rconEnabled ? `${rconPort} · 密码已设置` : "未启用"}</dd></div><div><dt>防火墙</dt><dd>{allowFirewallChange ? "执行前另行确认" : "不修改"}</dd></div></dl>
            </section>
          </div>

          {!preflightResult && !applicationOperation && <div className="setup-blocked-note"><ShieldCheck size={20} /><div><strong>先运行真实预检</strong><span>预检通过后才能安全应用配置；本操作不会启动服务端、修改防火墙，也不会安装或写入任何 MOD。</span></div></div>}
          {preflightResult?.valid && !applicationSucceeded && <div className="setup-basic-note"><CheckCircle2 size={20} /><span><strong>真实预检通过：</strong>更新环境、Galaxy 路径和 {preflightResult.ports.length} 个端口均已检查，现在可以安全应用已验证配置。</span></div>}
          {preflightResult && !preflightResult.valid && <div className="setup-blocked-note"><AlertCircle size={20} /><div><strong>预检未通过</strong><span>{preflightResult.issues.filter((issue) => issue.severity === "error").map((issue) => issue.message).join("；")}</span></div></div>}
          {preflightResult && <div className="setup-capability-list">
            {preflightResult.ports.map((port) => <div key={`${port.protocol}-${port.port}`}>{port.available ? <CheckCircle2 size={18} /> : <AlertCircle size={18} />}<span><strong>{port.purpose} · {port.port}/{port.protocol.toUpperCase()}</strong><small>{port.evidence}</small></span></div>)}
            {preflightResult.issues.filter((issue) => issue.severity === "warning").map((issue) => <div key={issue.code}><Info size={18} /><span><strong>尚未执行</strong><small>{issue.message}</small></span></div>)}
          </div>}
          {applicationRunning && <div className="steamcmd-operation" role="status" aria-live="polite">
            <div><LoaderCircle className="spin" size={20} /><strong>{applicationStepLabels[applicationOperation?.currentStep ?? ""] ?? "正在安全应用配置"}</strong><span>{applicationProgress}%</span></div>
            <progress max="100" value={applicationProgress} aria-label="服务器配置应用进度" />
            <small>Operation ID：{applicationOperation?.operationId}</small>
          </div>}
          {applicationSucceeded && applicationOperation.result && <div className="setup-capability-list">
            <div><CheckCircle2 size={18} /><span><strong>{applicationOperation.result.mode === "server-ini-updated" ? "server.ini 已原子更新" : "受控启动档案已保存"}</strong><small>{applicationOperation.result.mode === "server-ini-updated" ? "已验证写入结果；RCON 仅绑定本机回环地址。" : "这是新 Galaxy；首次启动前不伪造尚不存在的 server.ini。"}</small></span></div>
            {applicationOperation.result.deferredActions.map((action) => <div key={action}><Info size={18} /><span><strong>后续处理</strong><small>{deferredActionLabels[action] ?? action}</small></span></div>)}
          </div>}
          {applicationSucceeded && applicationOperation.result?.mode === "launch-profile-ready" && <section className="setup-initialization-panel" aria-labelledby="initialization-title">
            <div className="setup-initialization-panel__heading">
              <span><Play size={18} /></span>
              <div><h4 id="initialization-title">首次初始化并启动服务器</h4><p>Server Agent 将首次启动 Galaxy，依次执行 <b>/save</b> 与 <b>/stop</b>，停服后写入 RCON，再重启并进行真实认证。</p></div>
            </div>

            {!initializationRunning && !initializationSucceeded && <div className="setup-initialization-panel__form">
              <label className="setup-password-field">
                <span>再次输入 RCON 密码 <b>*</b></span>
                <input
                  type="password"
                  value={initializationPassword}
                  autoComplete="new-password"
                  maxLength={128}
                  placeholder="至少 8 位；仅用于本次初始化"
                  onChange={(event) => setInitializationPassword(event.target.value)}
                />
                <small>上一步的密码已按安全策略从浏览器清除，本次密码只进入 Operation 工作内存，不会写入草稿、日志或响应。</small>
              </label>
              <Button variant="primary" size="lg" onClick={() => void initializeGalaxy()} disabled={setupBusy !== null || initializationPassword.length < 8}>
                {setupBusy === "initializing" ? <LoaderCircle className="spin" size={18} /> : <Play size={18} />}
                {setupBusy === "initializing" ? "正在提交" : "初始化并启动服务器"}
              </Button>
            </div>}

            {initializationRunning && <div className="steamcmd-operation" role="status" aria-live="polite">
              <div><LoaderCircle className="spin" size={20} /><strong>{initializationStepLabels[initializationOperation?.currentStep ?? ""] ?? "正在执行首次初始化"}</strong><span>{initializationProgress}%</span></div>
              <progress max="100" value={initializationProgress} aria-label="Galaxy 首次初始化进度" />
              <small>Operation ID：{initializationOperation?.operationId}</small>
            </div>}

            {initializationSucceeded && initializationOperation.result && <>
              <div className="setup-initialization-result">
                <CheckCircle2 size={21} />
                <div><strong>服务器已启动，RCON 认证通过</strong><span>进程 PID {initializationOperation.result.processId} · 首次保存和安全停服均已确认</span></div>
              </div>
              <div className="setup-capability-list setup-capability-list--health">
                {initializationOperation.result.healthEvidence.map((evidence) => <div key={evidence}><CheckCircle2 size={18} /><span><strong>{healthEvidenceLabels[evidence] ?? evidence}</strong><small>来自本次 Operation 的可验证执行证据</small></span></div>)}
              </div>
            </>}

            {(initializationFailed || initializationError) && <div className="setup-blocked-note"><AlertCircle size={20} /><div><strong>首次初始化未完成</strong><span>{initializationOperation?.error?.message || initializationError || "请确认服务端状态后重新输入 RCON 密码重试。"}</span></div></div>}
          </section>}
          {(applicationFailed || applicationError) && <div className="setup-blocked-note"><AlertCircle size={20} /><div><strong>配置应用未完成</strong><span>{applicationOperation?.error?.message || applicationError || "请返回“网络与 RCON”重新输入密码，再运行预检。"}</span></div></div>}
        </Card>}

        <Card className="setup-overview-card" aria-labelledby="setup-overview-title">
          <h3 id="setup-overview-title">配置概览</h3>
          <ol>
            <li className={`setup-overview-item${environmentVerified ? " setup-overview-item--done" : ""}`}>
              {environmentVerified ? <CheckCircle2 size={20} /> : preflightResult ? <AlertCircle size={20} /> : <Circle size={20} />}
              <span>{environmentVerified ? "SteamCMD 已真实验证" : preflightResult ? "SteamCMD 路径未通过预检" : "SteamCMD 待真实预检"}</span>
            </li>
            <li className={`setup-overview-item${environmentVerified ? " setup-overview-item--done" : ""}`}>
              {environmentVerified ? <CheckCircle2 size={20} /> : preflightResult ? <AlertCircle size={20} /> : <Circle size={20} />}
              <span>{environmentVerified ? "服务端文件已真实验证" : preflightResult ? "服务端路径未通过预检" : "服务端文件待真实预检"}</span>
            </li>
            {steps.map((item, index) => <li key={item} className={`setup-overview-item${index + 1 === step ? " setup-overview-item--active" : index + 1 < step ? " setup-overview-item--done" : ""}`}>{index + 1 < step ? <CheckCircle2 size={20} /> : <Circle size={20} />}<span>{item}</span></li>)}
          </ol>
          <div className="setup-overview-note"><Info size={18} /><span>可以保存进度并稍后继续，未完成配置前不会自动启动服务端。</span></div>
        </Card>
      </div>

      <Card className="setup-wizard-actions">
        <Button variant="ghost" size="lg" onClick={previous}><ArrowLeft size={18} />{step === 1 ? "返回更新页" : "上一步"}</Button>
        <Button variant="secondary" size="lg" onClick={() => void saveDraft()} disabled={setupBusy !== null}>{setupBusy === "saving" ? <LoaderCircle className="spin" size={18} /> : null}{setupBusy === "saving" ? "正在保存" : "保存草稿"}</Button>
        <div>
          {step < 3 ? <Button variant="primary" size="lg" disabled={!stepValid || setupBusy === "loading"} onClick={next}>下一步：{steps[step]}<ArrowRight size={18} /></Button> : initializationSucceeded ? <Button variant="primary" size="lg" onClick={onComplete}><CheckCircle2 size={18} />进入服务器控制</Button> : applicationSucceeded ? <Button variant="primary" size="lg" disabled>{initializationRunning ? <LoaderCircle className="spin" size={18} /> : <CheckCircle2 size={18} />}{initializationRunning ? "首次初始化进行中" : "配置已安全应用"}</Button> : preflightResult?.valid ? <Button variant="primary" size="lg" onClick={() => void applyConfiguration()} disabled={setupBusy !== null || applicationRunning}>{setupBusy === "applying" || applicationRunning ? <LoaderCircle className="spin" size={18} /> : <ShieldCheck size={18} />}{setupBusy === "applying" ? "正在提交" : applicationRunning ? "正在安全应用" : "安全应用已验证配置"}</Button> : <Button variant="primary" size="lg" onClick={() => void runPreflight()} disabled={setupBusy !== null}>{setupBusy === "preflight" ? <LoaderCircle className="spin" size={18} /> : <ShieldCheck size={18} />}{setupBusy === "preflight" ? "正在真实预检" : "保存并运行真实预检"}</Button>}
          {!stepValid && <span>{step === 1 ? "请填写基础信息并选择 Galaxy 存档目录" : "请修正端口并设置至少 8 位的 RCON 密码"}</span>}
          {step === 3 && <span>{initializationSucceeded ? "首次保存、安全停服、RCON 写入与认证均已有真实证据" : applicationSucceeded ? "配置写入已完成；全新 Galaxy 可在上方启动首次初始化" : applicationFailed ? "请返回网络与 RCON 重新输入密码并再次预检" : preflightResult?.valid ? "将写入受控启动档案；已有 server.ini 时才原子更新已验证字段" : "只检查并保存草稿，不会启动服务端"}</span>}
        </div>
      </Card>
      <ServerDirectoryPicker
        open={galaxyPickerOpen}
        onOpenChange={setGalaxyPickerOpen}
        title={galaxyMode === "new" ? "选择新 Galaxy 保存位置" : "选择已有 Galaxy 目录"}
        description={galaxyMode === "new" ? "请选择服务器上的父目录，并确认即将使用的 Galaxy 文件夹名称。" : "请选择服务器上真实存在的 Galaxy 存档目录。"}
        proposedFolderName={galaxyMode === "new" ? galaxyName || "MyGalaxy" : undefined}
        confirmLabel={galaxyMode === "new" ? "使用此保存位置" : "使用当前 Galaxy 目录"}
        onSelect={onGalaxyDirectoryChange}
      />
    </div>
  );
}



