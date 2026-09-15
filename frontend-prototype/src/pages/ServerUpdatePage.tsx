import {
  AlertCircle,
  Box,
  CheckCircle2,
  Clock3,
  Database,
  Download,
  FolderOpen,
  Gamepad2,
  Info,
  LoaderCircle,
  RefreshCw,
  Search,
  ServerCog,
  Settings,
  ShieldCheck,
  SquareTerminal,
} from "lucide-react";

import { useEffect, useRef, useState } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";
import { apiFetch } from "../lib/api";
import { Button, Card } from "../components/ui";
import type { ServerStatus, UpdateSetupStage } from "../types";
import { UpdateManagementView } from "../features/server-update/UpdateManagementView";
import { SetupWizardView } from "../features/server-update/SetupWizardView";
import { FreshInstallView, ManualPathsView } from "../features/server-update/InstallationPathViews";
import { UpdateHeading, SummaryItem, VerificationCard } from "../features/server-update/UpdateSummaryComponents";
import type {
  ApiErrorEnvelope,
  BusyAction,
  SetupStep,
  UpdateEnvironmentCurrent,
  UpdateEnvironmentDetection,
  UpdateEnvironmentValidation,
} from "../features/server-update/types";
export type {
  ApiErrorEnvelope,
  AvorionServerInstallationResult,
  BusyAction,
  InstallationResult,
  InstallOperation,
  ServerInitializationOperation,
  ServerInitializationResult,
  ServerSetupApplicationOperation,
  ServerSetupApplicationResult,
  ServerSetupDraftConfiguration,
  ServerSetupPreflightResult,
  SetupStep,
  SteamCmdInstallationResult,
  UpdateCheckResult,
  UpdateComponentValidation,
  UpdateEnvironmentConfiguration,
  UpdateEnvironmentCurrent,
  UpdateEnvironmentDetection,
  UpdateEnvironmentValidation,
  UpdateInspectionOperation,
  UpdateRollbackPointAvailability,
  UpdateRollbackPointResult,
  UpdateVerificationResult,
} from "../features/server-update/types";
import {
  activeOperationStorageKeys,
  clearStoredOperationId,
  defaultSteamCmdInstallDirectory,
  formatBytes,
  formatValidationIssues,
  legacyInvalidSteamCmdInstallDirectory,
  readApiError,
  readInstallDirectory,
  readSessionValue,
  readStoredOperationId,
  serverConfigurationChecks,
  storeOperationId,
} from "../features/server-update/utils";

export function ServerUpdatePage({
  stage,
  onStageChange,
  onNotify,
}: {
  stage: UpdateSetupStage;
  onStageChange: (stage: UpdateSetupStage) => void;
  onNotify: (message: string) => void;
}) {
  const navigate = useNavigate();
  const [busyAction, setBusyAction] = useState<BusyAction>(null);
  const [serverName, setServerName] = useState("本机 Avorion 服务器");
  const [galaxyName, setGalaxyName] = useState("MyGalaxy");
  const [galaxyMode, setGalaxyMode] = useState<"new" | "existing">("new");
  const [maxPlayers, setMaxPlayers] = useState(10);
  const [galaxyDirectory, setGalaxyDirectory] = useState("");
  const [listenAddress, setListenAddress] = useState("0.0.0.0");
  const [gamePort, setGamePort] = useState(27000);
  const [queryPort, setQueryPort] = useState(27003);
  const [rconPort, setRconPort] = useState(27015);
  const [rconEnabled, setRconEnabled] = useState(true);
  const [rconPassword, setRconPassword] = useState("");
  const [allowFirewallChange, setAllowFirewallChange] = useState(false);
  const [steamCmdInstallDirectory, setSteamCmdInstallDirectory] = useState(() => readInstallDirectory(
    "avorion.install.steamcmd",
    defaultSteamCmdInstallDirectory,
    legacyInvalidSteamCmdInstallDirectory,
  ));
  const [serverInstallDirectory, setServerInstallDirectory] = useState(() => readSessionValue("avorion.install.server", "D:\\AvorionServer"));
  const [manualSteamCmdPath, setManualSteamCmdPath] = useState(() => readSessionValue("avorion.manual.steamcmd", ""));
  const [manualServerDirectory, setManualServerDirectory] = useState(() => readSessionValue("avorion.manual.server", ""));
  const [detectedEnvironment, setDetectedEnvironment] = useState<UpdateEnvironmentDetection | null>(null);
  const [validatedEnvironment, setValidatedEnvironment] = useState<UpdateEnvironmentValidation | null>(null);
  const [managementServerStatus, setManagementServerStatus] = useState<ServerStatus | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const [searchParams, setSearchParams] = useSearchParams();
  const resumeAttemptedRef = useRef(false);

  useEffect(() => {
    const installOperation = searchParams.get("operation");
    const setupOperation = searchParams.get("setupOperation");
    const initializationOperation = searchParams.get("initializeOperation");
    if (installOperation) storeOperationId(activeOperationStorageKeys.installation, installOperation);
    if (setupOperation) storeOperationId(activeOperationStorageKeys.setup, setupOperation);
    if (initializationOperation) storeOperationId(activeOperationStorageKeys.initialization, initializationOperation);
  }, [searchParams]);

  useEffect(() => {
    if (window.location.pathname !== "/server/update" || searchParams.toString() || resumeAttemptedRef.current) return;
    resumeAttemptedRef.current = true;

    const initializationOperation = readStoredOperationId(activeOperationStorageKeys.initialization);
    const setupOperation = readStoredOperationId(activeOperationStorageKeys.setup);
    const installOperation = readStoredOperationId(activeOperationStorageKeys.installation);
    const updateCheckOperation = readStoredOperationId(activeOperationStorageKeys.updateCheck);
    const fileVerificationOperation = readStoredOperationId(activeOperationStorageKeys.fileVerification);

    if (initializationOperation) {
      setSearchParams(setupOperation
        ? { step: "3", setupOperation, initializeOperation: initializationOperation }
        : { step: "3", initializeOperation: initializationOperation }, { replace: true });
      onStageChange("setup-basic");
      onNotify("已恢复服务器初始化任务，正在同步真实状态");
      return;
    }
    if (setupOperation) {
      setSearchParams({ step: "3", setupOperation }, { replace: true });
      onStageChange("setup-basic");
      onNotify("已恢复服务器配置任务，正在同步真实状态");
      return;
    }
    if (installOperation) {
      setSearchParams({ flow: "install", step: "2", operation: installOperation }, { replace: true });
      onStageChange("install-plan");
      onNotify("已恢复后台安装任务，正在同步真实进度");
      return;
    }
    if (updateCheckOperation || fileVerificationOperation) {
      onStageChange("management");
      onNotify("已恢复更新检查记录，正在同步真实状态");
    }
  }, [onNotify, onStageChange, searchParams, setSearchParams]);

  const requestedStep = Number(searchParams.get("step"));
  const setupStep: SetupStep = requestedStep === 4
    ? 3
    : [1, 2, 3].includes(requestedStep)
      ? requestedStep as SetupStep
      : 1;

  const goToSetupStep = (step: SetupStep) => {
    const nextParams: Record<string, string> = { step: String(step) };
    const setupOperation = searchParams.get("setupOperation");
    const initializeOperation = searchParams.get("initializeOperation");
    if (setupOperation) nextParams.setupOperation = setupOperation;
    if (initializeOperation) nextParams.initializeOperation = initializeOperation;
    setSearchParams(nextParams);
    onStageChange("setup-basic");
  };

  const flowStep = searchParams.get("step") === "2" ? 2 : 1;

  const openSetupFlow = (flow: "install" | "manual", step: 1 | 2 = 1) => {
    setSearchParams({ flow, step: String(step) });
    onStageChange(flow === "install" ? "install-plan" : "manual-paths");
  };

  const leaveSetupFlow = () => {
    setSearchParams({});
    onStageChange("unconfigured");
  };

  useEffect(() => {
    let disposed = false;
    const loadStatus = async () => {
      try {
        const response = await fetch("/api/v1/servers/local/status", { headers: { Accept: "application/json" } });
        if (!response.ok) return;
        const status = await response.json() as ServerStatus;
        if (!disposed) setManagementServerStatus(status);
      } catch {
        // The update environment remains usable when management status is temporarily unavailable.
      }
    };
    void loadStatus();
    const timer = window.setInterval(() => void loadStatus(), 5_000);
    return () => { disposed = true; window.clearInterval(timer); };
  }, []);

  useEffect(() => {
    if (window.location.pathname !== "/server/update" || window.location.search ||
      readStoredOperationId(activeOperationStorageKeys.installation) ||
      readStoredOperationId(activeOperationStorageKeys.setup) ||
      readStoredOperationId(activeOperationStorageKeys.initialization) ||
      readStoredOperationId(activeOperationStorageKeys.updateCheck) ||
      readStoredOperationId(activeOperationStorageKeys.fileVerification)) return;
    const controller = new AbortController();
    void fetch("/api/v1/servers/local/update-environment", {
      headers: { Accept: "application/json" },
      signal: controller.signal,
    }).then(async (response) => {
      if (!response.ok) throw new Error(await readApiError(response));
      return response.json() as Promise<UpdateEnvironmentCurrent>;
    }).then((current) => {
      if (!current?.configured || !current.configuration || !current.validation) return;
      setManualSteamCmdPath(current.configuration.steamCmdPath);
      setManualServerDirectory(current.configuration.serverDirectory);
      setValidatedEnvironment(current.validation);
      if (current.validation.valid) onStageChange("ready");
      else {
        setActionError(formatValidationIssues(current.validation));
        onStageChange("manual-paths");
        setSearchParams({ flow: "manual", step: "1" });
      }
    }).catch((caught) => {
      if (caught instanceof DOMException && caught.name === "AbortError") return;
      setActionError(caught instanceof Error ? caught.message : "无法读取更新环境配置");
    });
    return () => controller.abort();
  }, [onStageChange, setSearchParams]);

  useEffect(() => {
    setSteamCmdInstallDirectory((current) => current.toLowerCase() === legacyInvalidSteamCmdInstallDirectory.toLowerCase()
      ? defaultSteamCmdInstallDirectory
      : current);
  }, []);

  useEffect(() => {
    try {
      window.sessionStorage.setItem("avorion.install.steamcmd", steamCmdInstallDirectory);
      window.sessionStorage.setItem("avorion.install.server", serverInstallDirectory);
      window.sessionStorage.setItem("avorion.manual.steamcmd", manualSteamCmdPath);
      window.sessionStorage.setItem("avorion.manual.server", manualServerDirectory);
    } catch {
      // The form remains usable when browser storage is unavailable.
    }
  }, [manualServerDirectory, manualSteamCmdPath, serverInstallDirectory, steamCmdInstallDirectory]);

  const detectPaths = async () => {
    if (busyAction) return;
    setBusyAction("detect");
    setActionError(null);
    onNotify("正在扫描本机 SteamCMD 与 Avorion 服务端");
    try {
      const response = await fetch("/api/v1/servers/local/update-environment/detection", { headers: { Accept: "application/json" } });
      if (!response.ok) throw new Error(await readApiError(response));
      const detection = await response.json() as UpdateEnvironmentDetection;
      setDetectedEnvironment(detection);
      if (detection.steamCmd) setManualSteamCmdPath(detection.steamCmd.requestedPath);
      if (detection.server) setManualServerDirectory(detection.server.requestedPath);

      if (detection.status === "complete") {
        onStageChange("detected");
        onNotify("已检测到真实 SteamCMD 与 Avorion 服务端");
      } else if (detection.status === "partial") {
        setActionError(`只检测到部分安装：${detection.steamCmd ? "SteamCMD" : "Avorion 服务端"}。请补充另一个路径。`);
        openSetupFlow("manual");
      } else {
        setActionError("没有在已配置位置、常用目录或 Steam 库中检测到现有安装。可选择全新安装，或手动选择已有路径。");
        onNotify("未检测到现有安装");
      }
    } catch (caught) {
      setActionError(caught instanceof Error ? caught.message : "真实路径扫描失败");
    } finally {
      setBusyAction(null);
    }
  };

  const saveEnvironment = async (steamCmdPath: string, serverDirectory: string): Promise<boolean> => {
    if (busyAction) return false;
    setBusyAction("validate");
    setActionError(null);
    onNotify("正在验证路径、权限与服务端文件");
    try {
      const response = await apiFetch("/api/v1/servers/local/update-environment", {
        method: "PUT",
        headers: { "Content-Type": "application/json", Accept: "application/json" },
        body: JSON.stringify({ steamCmdPath, serverDirectory }),
      });
      if (!response.ok) throw new Error(await readApiError(response));
      const validation = await response.json() as UpdateEnvironmentValidation;
      if (!validation.valid) {
        setValidatedEnvironment(validation);
        setActionError(formatValidationIssues(validation));
        onNotify("真实路径验证未通过");
        return false;
      }
      setValidatedEnvironment(validation);
      setManualSteamCmdPath(validation.steamCmd.requestedPath);
      setManualServerDirectory(validation.server.requestedPath);
      setSearchParams({});
      onStageChange("ready");
      onNotify("真实路径验证完成并已保存");
      return true;
    } catch (caught) {
      setActionError(caught instanceof Error ? caught.message : "路径验证失败");
      return false;
    } finally {
      setBusyAction(null);
    }
  };

  const confirmPaths = () => {
    if (!detectedEnvironment?.steamCmd || !detectedEnvironment.server) return;
    void saveEnvironment(detectedEnvironment.steamCmd.requestedPath, detectedEnvironment.server.requestedPath);
  };

  const revalidatePaths = async () => {
    if (!validatedEnvironment || busyAction) return;
    setBusyAction("validate");
    setActionError(null);
    try {
      const response = await apiFetch("/api/v1/servers/local/update-environment/validation", {
        method: "POST",
        headers: { "Content-Type": "application/json", Accept: "application/json" },
        body: JSON.stringify({
          steamCmdPath: validatedEnvironment.steamCmd.requestedPath,
          serverDirectory: validatedEnvironment.server.requestedPath,
        }),
      });
      if (!response.ok) throw new Error(await readApiError(response));
      const validation = await response.json() as UpdateEnvironmentValidation;
      setValidatedEnvironment(validation);
      if (validation.valid) onNotify("真实路径重新验证通过");
      else {
        setActionError(formatValidationIssues(validation));
        setManualSteamCmdPath(validation.steamCmd.requestedPath);
        setManualServerDirectory(validation.server.requestedPath);
        openSetupFlow("manual");
      }
    } catch (caught) {
      setActionError(caught instanceof Error ? caught.message : "重新验证失败");
    } finally {
      setBusyAction(null);
    }
  };

  if (stage === "install-plan") {
    return (
      <FreshInstallView
        step={flowStep}
        steamCmdDirectory={steamCmdInstallDirectory}
        serverDirectory={serverInstallDirectory}
        onSteamCmdDirectoryChange={setSteamCmdInstallDirectory}
        onServerDirectoryChange={setServerInstallDirectory}
        onStepChange={(nextStep) => openSetupFlow("install", nextStep)}
        onBack={leaveSetupFlow}
        operationId={searchParams.get("operation")}
        onOperationStarted={(operationId) => {
          storeOperationId(activeOperationStorageKeys.installation, operationId);
          setSearchParams({ flow: "install", step: "2", operation: operationId });
        }}
        onInstalled={(result) => {
          setManualSteamCmdPath(result.executablePath);
          onNotify(result.reusedExisting
            ? "现有 SteamCMD 已通过 Valve 签名验证并复用"
            : "SteamCMD 已从 Valve 官方源安装并通过签名验证");
        }}
        onServerInstalled={(result) => {
          setManualSteamCmdPath(result.steamCmdPath);
          setManualServerDirectory(result.installDirectory);
          void saveEnvironment(result.steamCmdPath, result.installDirectory).then((saved) => {
            if (saved) clearStoredOperationId(activeOperationStorageKeys.installation);
          });
        }}
      />
    );
  }

  if (stage === "manual-paths") {
    return (
      <ManualPathsView
        step={flowStep}
        steamCmdPath={manualSteamCmdPath}
        serverDirectory={manualServerDirectory}
        onSteamCmdPathChange={setManualSteamCmdPath}
        onServerDirectoryChange={setManualServerDirectory}
        onStepChange={(nextStep) => openSetupFlow("manual", nextStep)}
        onBack={leaveSetupFlow}
        busy={busyAction === "validate"}
        apiError={actionError}
        onValidate={(steamCmdPath, serverDirectory) => void saveEnvironment(steamCmdPath, serverDirectory)}
      />
    );
  }

  if (stage === "setup-basic") {
    return (
      <SetupWizardView
        step={setupStep}
        serverName={serverName}
        galaxyName={galaxyName}
        galaxyMode={galaxyMode}
        maxPlayers={maxPlayers}
        galaxyDirectory={galaxyDirectory}
        onServerNameChange={setServerName}
        onGalaxyNameChange={setGalaxyName}
        onGalaxyModeChange={setGalaxyMode}
        onMaxPlayersChange={setMaxPlayers}
        onGalaxyDirectoryChange={setGalaxyDirectory}
        listenAddress={listenAddress}
        gamePort={gamePort}
        queryPort={queryPort}
        rconPort={rconPort}
        rconEnabled={rconEnabled}
        rconPassword={rconPassword}
        allowFirewallChange={allowFirewallChange}
        onListenAddressChange={setListenAddress}
        onGamePortChange={setGamePort}
        onQueryPortChange={setQueryPort}
        onRconPortChange={setRconPort}
        onRconEnabledChange={setRconEnabled}
        onRconPasswordChange={setRconPassword}
        onAllowFirewallChange={setAllowFirewallChange}
        onStepChange={goToSetupStep}
        onBack={() => onStageChange("ready")}
        onNotify={onNotify}
        operationId={searchParams.get("setupOperation")}
        onOperationStarted={(operationId) => {
          storeOperationId(activeOperationStorageKeys.setup, operationId);
          setSearchParams({ step: "3", setupOperation: operationId });
        }}
        initializationOperationId={searchParams.get("initializeOperation")}
        onInitializationStarted={(initializationOperationId) => {
          const setupOperation = searchParams.get("setupOperation");
          storeOperationId(activeOperationStorageKeys.initialization, initializationOperationId);
          setSearchParams(setupOperation
            ? { step: "3", setupOperation, initializeOperation: initializationOperationId }
            : { step: "3", initializeOperation: initializationOperationId });
        }}
        onComplete={() => navigate("/server/control")}
      />
    );
  }

  if (stage === "management") {
    return (
      <UpdateManagementView
        onBack={() => onStageChange("ready")}
        onNotify={onNotify}
      />
    );
  }

  if (stage === "unconfigured") {
    return (
      <div className="setup-update-page">
        <UpdateHeading detail="配置 SteamCMD 与 Avorion 服务端安装位置后，才能检查与安装更新。" />

        {actionError && <Card className="update-api-error" role="alert"><AlertCircle size={20} /><div><strong>真实扫描结果</strong><span>{actionError}</span></div></Card>}

        <Card className="setup-update-summary setup-update-summary--three" aria-label="安装环境状态">
          <SummaryItem icon={SquareTerminal} label="SteamCMD" value="未配置" tone="warning" />
          <SummaryItem icon={Box} label="Avorion 服务端" value="未配置" tone="warning" />
          <SummaryItem icon={Clock3} label="更新功能" value="暂不可用" tone="warning" />
        </Card>

        <Card className="setup-choice-card" aria-labelledby="setup-choice-title">
          <h3 id="setup-choice-title">请选择接入方式</h3>
          <div className="setup-choice-grid">
            <article className="setup-choice setup-choice--recommended">
              <span className="setup-choice__badge">推荐</span>
              <span className="setup-choice__icon setup-choice__icon--blue"><Download size={43} /></span>
              <h4>全新安装</h4>
              <p>自动安装 SteamCMD 与 Avorion 服务端</p>
              <Button variant="primary" size="lg" onClick={() => openSetupFlow("install")}>开始安装</Button>
            </article>

            <article className="setup-choice">
              <span className="setup-choice__icon setup-choice__icon--purple"><Search size={43} /></span>
              <h4>自动检测</h4>
              <p>扫描本机已有的 SteamCMD 与服务端</p>
              <Button variant="secondary" size="lg" onClick={detectPaths} disabled={busyAction === "detect"}>
                {busyAction === "detect" && <LoaderCircle className="spin" size={18} />}
                {busyAction === "detect" ? "正在扫描" : "开始扫描"}
              </Button>
            </article>

            <article className="setup-choice">
              <span className="setup-choice__icon setup-choice__icon--green"><FolderOpen size={43} /></span>
              <h4>选择已有路径</h4>
              <p>分别选择 SteamCMD 与服务端安装位置</p>
              <Button variant="secondary" size="lg" onClick={() => openSetupFlow("manual")}>选择路径</Button>
            </article>
          </div>
        </Card>

        <Card className="setup-info-strip">
          <Info size={22} />
          <div>
            <strong>路径以后仍可管理</strong>
            <span>配置完成后，可在本页重新检测 SteamCMD，或在停服并验证文件后安全迁移服务端目录。</span>
          </div>
        </Card>
      </div>
    );
  }

  const detectedRows = detectedEnvironment ? [
    detectedEnvironment.steamCmd && {
      title: "SteamCMD",
      icon: SquareTerminal,
      label: "检测路径",
      path: detectedEnvironment.steamCmd.requestedPath,
      helper: detectedEnvironment.steamCmd.version ? `文件版本 ${detectedEnvironment.steamCmd.version}` : null,
      checks: detectedEnvironment.steamCmd.checks,
    },
    detectedEnvironment.server && {
      title: "Avorion 服务端",
      icon: Box,
      label: "安装目录",
      path: detectedEnvironment.server.requestedPath,
      helper: detectedEnvironment.server.version ? `程序版本 ${detectedEnvironment.server.version}` : "已找到 AvorionServer.exe，文件未提供版本信息",
      checks: detectedEnvironment.server.checks,
    },
  ].filter(Boolean) as Array<{
    title: string;
    icon: typeof SquareTerminal;
    label: string;
    path: string;
    helper: string | null;
    checks: string[];
  }> : [];

  if (stage === "detected") {
    return (
      <div className="setup-update-page">
        <UpdateHeading detail="已检测到可用安装，请确认后保存为当前服务器的更新位置。" />

        <Card className="setup-update-summary setup-update-summary--three" aria-label="检测状态">
          <SummaryItem icon={SquareTerminal} label="SteamCMD" value="已检测" tone="success" />
          <SummaryItem icon={Box} label="Avorion 服务端" value="已检测" tone="success" />
          <SummaryItem icon={Clock3} label="配置状态" value="等待确认" tone="warning" />
        </Card>

        <Card className="detected-card" aria-labelledby="detected-title">
          <div className="detected-card__heading">
            <h3 id="detected-title">检测结果</h3>
            <p>确认前不会保存配置、移动文件或执行更新。</p>
          </div>

          {actionError && <div className="path-error-summary" role="alert"><strong>验证未完成</strong><span>{actionError}</span></div>}

          <div className="detected-list">
            {detectedRows.map((row) => {
              const Icon = row.icon;
              return (
                <article className="detected-row" key={row.title}>
                  <span className="detected-row__icon"><Icon size={27} /></span>
                  <strong>{row.title}</strong>
                  <div className="detected-row__path">
                    <span>{row.label}</span>
                    <code>{row.path}</code>
                    {row.helper && <small>{row.helper}</small>}
                  </div>
                  <div className="detected-row__checks">
                    {row.checks.map((check) => <span key={check}><CheckCircle2 size={17} />{check}</span>)}
                  </div>
                  <Button variant="secondary" size="md" onClick={() => openSetupFlow("manual")}>更换路径</Button>
                </article>
              );
            })}
          </div>

          <div className="detected-validation-note">
            <Info size={22} />
            <div>
              <strong>确认后会执行完整验证</strong>
              <span>检查文件是否真实存在并可读取，同时记录磁盘可用空间；写入权限与文件完整性将在正式安装或更新前单独验证。</span>
            </div>
          </div>

          <footer className="detected-actions">
            <Button variant="secondary" size="lg" onClick={detectPaths} disabled={busyAction === "detect"}>
              {busyAction === "detect" ? <LoaderCircle className="spin" size={19} /> : <RefreshCw size={19} />}
              {busyAction === "detect" ? "正在扫描" : "重新扫描"}
            </Button>
            <Button variant="ghost" size="lg" onClick={() => onStageChange("unconfigured")}>取消</Button>
            <div className="detected-actions__primary">
              <Button variant="primary" size="lg" onClick={confirmPaths} disabled={busyAction === "validate"}>
                {busyAction === "validate" && <LoaderCircle className="spin" size={19} />}
                {busyAction === "validate" ? "正在验证" : "确认并使用"}
              </Button>
              <span>确认路径不会立即更新服务端文件。</span>
            </div>
          </footer>
        </Card>
      </div>
    );
  }

  if (!validatedEnvironment) {
    return (
      <div className="setup-update-page">
        <UpdateHeading detail="尚未取得真实更新环境验证结果。" />
        <Card className="update-api-error" role="alert">
          <AlertCircle size={20} />
          <div><strong>更新环境不可用</strong><span>{actionError ?? "请返回接入方式，重新检测或选择真实路径。"}</span></div>
          <Button variant="secondary" onClick={() => onStageChange("unconfigured")}>返回接入方式</Button>
        </Card>
      </div>
    );
  }

  const updateEnvironmentChecks = [
    { label: "SteamCMD 可执行文件", result: validatedEnvironment.steamCmd.readable ? "文件可读取" : "不可用", icon: SquareTerminal },
    { label: "服务端安装目录", result: validatedEnvironment.server.readable ? "目录与程序可读取" : "不可用", icon: FolderOpen },
    { label: "AvorionServer.exe", result: validatedEnvironment.server.version ?? "已找到，版本信息未知", icon: Box },
    { label: "服务端磁盘空间", result: formatBytes(validatedEnvironment.serverDriveAvailableBytes), icon: Database },
  ];
  const managementConfigured = Boolean(managementServerStatus?.galaxyName);
  const managementConfigurationChecks = managementConfigured ? [
    { label: "Galaxy / 存档", result: managementServerStatus?.galaxyName ?? "已配置", icon: Database },
    { label: "基础服务器配置", result: `已完成 · 最大 ${managementServerStatus?.maxPlayers ?? "—"} 人`, icon: ServerCog },
    { label: "游戏与查询端口", result: "游戏端口已配置；Query 探测待接入", icon: Gamepad2 },
    { label: "RCON", result: managementServerStatus?.rcon.status === "connected" ? "已配置并连接" : "已配置；当前未连接", icon: ShieldCheck },
  ] : serverConfigurationChecks;

  return (
    <div className="setup-update-page">
      <UpdateHeading detail={managementConfigured ? "安装路径与服务器管理配置均已验证，可以进入更新管理。" : "安装路径验证完成；更新功能已就绪，服务器管理配置仍需完成。"} />

      <Card className="setup-update-summary setup-update-summary--four" aria-label="验证结果摘要">
        <SummaryItem icon={SquareTerminal} label="SteamCMD" value="正常" tone="success" />
        <SummaryItem icon={Box} label="Avorion 服务端" value={validatedEnvironment.server.version ?? "已识别"} tone="success" />
        <SummaryItem icon={CheckCircle2} label="更新能力" value="已就绪" tone="success" />
        <SummaryItem icon={Settings} label="管理配置" value={managementConfigured ? "已完成" : "未完成"} tone={managementConfigured ? "success" : "warning"} />
      </Card>

      <div className="verification-grid">
        <VerificationCard title="更新环境验证" helper="用于检查、验证和更新服务端文件" items={updateEnvironmentChecks} tone="success" />
        <VerificationCard title="服务器配置检测" helper={managementConfigured ? "来自受控启动档案与实时服务状态" : "以下项目不影响文件更新，但会影响完整管理功能"} items={managementConfigurationChecks} tone={managementConfigured ? "success" : "warning"} showNote={!managementConfigured} />
      </div>

      {actionError && <Card className="update-api-error" role="alert"><AlertCircle size={20} /><div><strong>重新验证失败</strong><span>{actionError}</span></div></Card>}

      <Card className="verification-actions">
        <Button variant="ghost" size="lg" onClick={() => void revalidatePaths()} disabled={busyAction === "validate"}>
          {busyAction === "validate" ? <LoaderCircle className="spin" size={19} /> : <RefreshCw size={19} />}
          {busyAction === "validate" ? "正在验证" : "重新验证路径"}
        </Button>
        <div className="verification-actions__secondary">
          <Button variant="secondary" size="lg" onClick={() => onStageChange("management")}>进入更新管理</Button>
          <span>可先检查版本或验证文件</span>
        </div>
        <div className="verification-actions__primary">
          {managementConfigured
            ? <Button variant="primary" size="lg" onClick={() => navigate("/server/control")}>进入服务器控制</Button>
            : <Button variant="primary" size="lg" onClick={() => goToSetupStep(1)}>继续完成服务器配置</Button>}
          <span>{managementConfigured ? "管理配置已验证" : "配置 Galaxy、端口与 RCON"}</span>
        </div>
      </Card>
    </div>
  );
}
