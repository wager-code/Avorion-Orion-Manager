import { useCallback, useEffect, useRef, useState } from "react";
import { AppRoutes } from "./app/AppRoutes";
import { Toast } from "./components/ui";
import { apiFetch } from "./lib/api";
import type { ServerStatus, UpdateSetupStage } from "./types";

function initialUpdateStage(): UpdateSetupStage {
  if (window.location.pathname !== "/server/update") return "unconfigured";
  const params = new URLSearchParams(window.location.search);
  const flow = params.get("flow");
  if (flow === "install") return "install-plan";
  if (flow === "manual") return "manual-paths";
  return [1, 2, 3, 4].includes(Number(params.get("step")))
    ? "setup-basic"
    : "unconfigured";
}

export function App() {
  const [serverStatus, setServerStatus] = useState<ServerStatus | null>(null);
  const [updateSetupStage, setUpdateSetupStage] =
    useState<UpdateSetupStage>(initialUpdateStage);
  const [toast, setToast] = useState<string | null>(null);
  const toastTimer = useRef<number | null>(null);

  const notify = useCallback((message: string) => {
    setToast(message);
    if (toastTimer.current) window.clearTimeout(toastTimer.current);
    toastTimer.current = window.setTimeout(() => setToast(null), 2600);
  }, []);

  const refreshServerStatus = useCallback(async () => {
    try {
      const response = await apiFetch("/api/v1/servers/local/status", {
        headers: { Accept: "application/json" },
      });
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      setServerStatus(await response.json() as ServerStatus);
    } catch {
      setServerStatus((current) => current ? {
        ...current,
        lifecycle: "unknown",
        agent: {
          connected: false,
          lastHeartbeatAt: current.agent.lastHeartbeatAt,
        },
        provenance: {
          ...current.provenance,
          unavailableReason: "无法连接本机 Server Agent",
        },
      } : null);
    }
  }, []);

  useEffect(() => {
    void refreshServerStatus();
    const timer = window.setInterval(() => void refreshServerStatus(), 3000);
    return () => window.clearInterval(timer);
  }, [refreshServerStatus]);

  const handleUnavailable = useCallback((label: string) => {
    notify(`${label}将在后续确认阶段开放`);
  }, [notify]);

  return (
    <>
      <AppRoutes
        serverStatus={serverStatus}
        updateSetupStage={updateSetupStage}
        onUpdateSetupStageChange={setUpdateSetupStage}
        onServerStatusRefresh={refreshServerStatus}
        onNotify={notify}
        onUnavailable={handleUnavailable}
      />
      <Toast message={toast} />
    </>
  );
}
