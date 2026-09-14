import { lazy, Suspense } from "react";
import { Navigate, Route, Routes } from "react-router-dom";
import { AppShell } from "../components/AppShell";
import { ServerControlPage } from "../pages/ServerControlPage";
import type { ServerStatus, UpdateSetupStage } from "../types";

const ServerPerformancePage = lazy(() =>
  import("../pages/ServerPerformancePage").then((module) => ({
    default: module.ServerPerformancePage,
  })),
);
const ServerMemoryPage = lazy(() =>
  import("../pages/ServerMemoryPage").then((module) => ({
    default: module.ServerMemoryPage,
  })),
);
const ServerUpdatePage = lazy(() =>
  import("../pages/ServerUpdatePage").then((module) => ({
    default: module.ServerUpdatePage,
  })),
);
const ServerTasksPage = lazy(() =>
  import("../pages/ServerTasksPage").then((module) => ({
    default: module.ServerTasksPage,
  })),
);
const ServerBackupPage = lazy(() =>
  import("../pages/ServerBackupPage").then((module) => ({
    default: module.ServerBackupPage,
  })),
);
const ServerLogsPage = lazy(() =>
  import("../pages/ServerLogsPage").then((module) => ({
    default: module.ServerLogsPage,
  })),
);
const ServerDiagnosticsPage = lazy(() =>
  import("../pages/ServerDiagnosticsPage").then((module) => ({
    default: module.ServerDiagnosticsPage,
  })),
);
const PlayerManagementPage = lazy(() =>
  import("../pages/PlayerManagementPage").then((module) => ({
    default: module.PlayerManagementPage,
  })),
);
const AllianceManagementPage = lazy(() =>
  import("../pages/AllianceManagementPage").then((module) => ({
    default: module.AllianceManagementPage,
  })),
);
const GameManagementPage = lazy(() =>
  import("../pages/GameManagementPage").then((module) => ({
    default: module.GameManagementPage,
  })),
);

interface AppRoutesProps {
  serverStatus: ServerStatus | null;
  updateSetupStage: UpdateSetupStage;
  onUpdateSetupStageChange: (stage: UpdateSetupStage) => void;
  onServerStatusRefresh: () => Promise<void>;
  onNotify: (message: string) => void;
  onUnavailable: (label: string) => void;
}

function Loading({ children }: { children: string }) {
  return <div className="route-loading" role="status">{children}</div>;
}

export function AppRoutes({
  serverStatus,
  updateSetupStage,
  onUpdateSetupStageChange,
  onServerStatusRefresh,
  onNotify,
  onUnavailable,
}: AppRoutesProps) {
  const lifecycle = serverStatus?.lifecycle ?? "unknown";
  const shell = (page: Parameters<typeof AppShell>[0]["page"], child: React.ReactNode) => (
    <AppShell
      page={page}
      lifecycle={lifecycle}
      serverStatus={serverStatus}
      updateSetupStage={page === "update" ? updateSetupStage : undefined}
      onUnavailable={onUnavailable}
    >
      {child}
    </AppShell>
  );

  return (
    <Routes>
      <Route path="/" element={<Navigate to="/server/control" replace />} />
      <Route
        path="/players"
        element={shell("players",
          <Suspense fallback={<Loading>正在载入真实玩家数据…</Loading>}>
            <PlayerManagementPage serverStatus={serverStatus} onNotify={onNotify} />
          </Suspense>,
        )}
      />
      <Route
        path="/alliances"
        element={shell("alliances",
          <Suspense fallback={<Loading>正在载入真实联盟数据…</Loading>}>
            <AllianceManagementPage onNotify={onNotify} />
          </Suspense>,
        )}
      />
      <Route
        path="/game/rewards"
        element={shell("game",
          <Suspense fallback={<Loading>正在载入奖励中心…</Loading>}>
            <GameManagementPage serverStatus={serverStatus} onNotify={onNotify} />
          </Suspense>,
        )}
      />
      <Route
        path="/server/control"
        element={shell("control",
          <ServerControlPage
            status={serverStatus}
            onStatusRefresh={onServerStatusRefresh}
            onNotify={onNotify}
          />,
        )}
      />
      <Route
        path="/server/performance"
        element={shell("performance",
          <Suspense fallback={<Loading>正在载入性能数据…</Loading>}>
            <ServerPerformancePage serverStatus={serverStatus} />
          </Suspense>,
        )}
      />
      <Route
        path="/server/memory"
        element={shell("memory",
          <Suspense fallback={<Loading>正在载入内存与星区数据…</Loading>}>
            <ServerMemoryPage onNotify={onNotify} />
          </Suspense>,
        )}
      />
      <Route
        path="/server/update"
        element={shell("update",
          <Suspense fallback={<Loading>正在载入服务器更新…</Loading>}>
            <ServerUpdatePage
              stage={updateSetupStage}
              onStageChange={onUpdateSetupStageChange}
              onNotify={onNotify}
            />
          </Suspense>,
        )}
      />
      <Route
        path="/server/tasks"
        element={shell("tasks",
          <Suspense fallback={<Loading>正在载入自动任务…</Loading>}>
            <ServerTasksPage onNotify={onNotify} />
          </Suspense>,
        )}
      />
      <Route
        path="/server/backup"
        element={shell("backup",
          <Suspense fallback={<Loading>正在载入存档备份…</Loading>}>
            <ServerBackupPage onNotify={onNotify} />
          </Suspense>,
        )}
      />
      <Route
        path="/server/logs"
        element={shell("logs",
          <Suspense fallback={<Loading>正在载入实时日志…</Loading>}>
            <ServerLogsPage onNotify={onNotify} />
          </Suspense>,
        )}
      />
      <Route
        path="/server/diagnostics"
        element={shell("diagnostics",
          <Suspense fallback={<Loading>正在载入服务器诊断…</Loading>}>
            <ServerDiagnosticsPage onNotify={onNotify} />
          </Suspense>,
        )}
      />
      <Route path="*" element={<Navigate to="/server/control" replace />} />
    </Routes>
  );
}
