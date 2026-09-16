import * as DropdownMenu from "@radix-ui/react-dropdown-menu";
import * as Tabs from "@radix-ui/react-tabs";
import * as Tooltip from "@radix-ui/react-tooltip";
import {
  Box,
  ChevronDown,
  Gamepad2,
  LayoutDashboard,
  Menu,
  Orbit,
  Puzzle,
  Rocket,
  Server,
  Settings,
  ShieldCheck,
  Users,
  UsersRound,
  X,
} from "lucide-react";
import { useState, type ReactNode } from "react";
import { useLocation, useNavigate } from "react-router-dom";
import { cn } from "../lib/utils";
import type { ServerLifecycle, ServerStatus, UpdateSetupStage } from "../types";
import { StatusPill } from "./ui";

const primaryNav = [
  { label: "总览", icon: LayoutDashboard },
  { label: "服务器管理", icon: Server },
  { label: "玩家管理", icon: Users },
  { label: "联盟管理", icon: UsersRound },
  { label: "游戏管理", icon: Gamepad2 },
  { label: "舰船管理", icon: Rocket },
  { label: "MOD 管理", icon: Puzzle },
  { label: "设置", icon: Settings },
];

const serverTabs = [
  { value: "control", label: "控制", path: "/server/control", enabled: true },
  { value: "performance", label: "性能", path: "/server/performance", enabled: true },
  { value: "memory", label: "内存与星区", path: "/server/memory", enabled: true },
  { value: "update", label: "更新", path: "/server/update", enabled: true },
  { value: "tasks", label: "自动任务", path: "/server/tasks", enabled: true },
  { value: "backup", label: "备份", path: "/server/backup", enabled: true },
  { value: "logs", label: "日志", path: "/server/logs", enabled: true },
  { value: "diagnostics", label: "诊断", path: "/server/diagnostics", enabled: true },
];

function lifecycleCopy(status: ServerLifecycle) {
  switch (status) {
    case "running":
      return { label: "运行中", detail: "服务器运行中", tone: "success" as const };
    case "stopped":
      return { label: "已停止", detail: "服务器已停止", tone: "neutral" as const };
    case "starting":
      return { label: "启动中", detail: "正在启动服务器", tone: "info" as const };
    case "stopping":
      return { label: "关闭中", detail: "正在安全关闭", tone: "warning" as const };
    case "restarting":
      return { label: "重启中", detail: "正在安全重启", tone: "warning" as const };
    case "unknown":
      return { label: "未知", detail: "等待 Agent 状态", tone: "neutral" as const };
  }
}

export function AppShell({
  children,
  page,
  lifecycle,
  serverStatus,
  updateSetupStage,
  onUnavailable,
}: {
  children: ReactNode;
  page: "players" | "alliances" | "game" | "control" | "performance" | "memory" | "update" | "tasks" | "backup" | "logs" | "diagnostics";
  lifecycle: ServerLifecycle;
  serverStatus?: ServerStatus | null;
  updateSetupStage?: UpdateSetupStage;
  onUnavailable: (label: string) => void;
}) {
  const navigate = useNavigate();
  const location = useLocation();
  const [mobileNavOpen, setMobileNavOpen] = useState(false);
  const status = lifecycleCopy(lifecycle);
  const displayName = serverStatus?.name ?? "本机 Avorion 服务器";
  const displayVersion = serverStatus?.version ?? "版本不可用";
  const displayGalaxy = serverStatus?.galaxyName ?? "Galaxy 未配置";
  const serverPage = page !== "players" && page !== "alliances" && page !== "game";
  const pageLabel = serverTabs.find((tab) => tab.value === page)?.label ?? "控制";
  const updateSubpageLabel = page === "update"
    ? updateSetupStage === "install-plan"
      ? "全新安装"
      : updateSetupStage === "manual-paths"
        ? "选择已有路径"
        : updateSetupStage === "setup-basic"
          ? "完成配置"
          : null
    : null;
  const updatePresentation = page === "update" && updateSetupStage
    ? serverStatus?.galaxyName && ["unconfigured", "ready", "management"].includes(updateSetupStage)
      ? {
          serverName: displayName,
          badge: status.label,
          sidebarTitle: status.detail,
          sidebarDetail: displayVersion,
          sidebarBadge: false,
        }
      : updateSetupStage === "unconfigured"
      ? {
          serverName: "尚未配置服务器",
          badge: "待配置",
          sidebarTitle: "尚未配置服务器",
          sidebarDetail: "待配置",
          sidebarBadge: true,
        }
      : updateSetupStage === "install-plan"
        ? {
            serverName: "安装新服务器",
            badge: "安装流程",
            sidebarTitle: "新服务器安装",
            sidebarDetail: "按步骤执行",
            sidebarBadge: true,
          }
        : updateSetupStage === "manual-paths"
          ? {
              serverName: "接入已有服务器",
              badge: "路径配置",
              sidebarTitle: "已有服务器接入",
              sidebarDetail: "等待验证",
              sidebarBadge: true,
            }
          : updateSetupStage === "detected"
        ? {
            serverName: "检测到本机服务器",
            badge: "待确认",
            sidebarTitle: "等待确认安装",
            sidebarDetail: "待确认",
            sidebarBadge: true,
          }
          : updateSetupStage === "setup-basic"
          ? {
              serverName: "本机 Avorion 服务器",
              badge: "配置中",
              sidebarTitle: "服务器配置中",
              sidebarDetail: "草稿与真实预检",
              sidebarBadge: false,
            }
          : {
            serverName: "本机 Avorion 服务器",
            badge: "配置未完成",
            sidebarTitle: "服务器待配置",
            sidebarDetail: "更新环境已就绪",
            sidebarBadge: false,
          }
    : null;

  return (
    <Tooltip.Provider delayDuration={250}>
      <div className={cn("app-shell", page === "alliances" && "app-shell--alliances")}>
        <button
          className="mobile-nav-toggle"
          type="button"
          aria-label={mobileNavOpen ? "关闭导航" : "打开导航"}
          aria-expanded={mobileNavOpen}
          onClick={() => setMobileNavOpen((value) => !value)}
        >
          {mobileNavOpen ? <X size={22} /> : <Menu size={22} />}
        </button>
        <button
          className={cn("mobile-nav-scrim", mobileNavOpen && "mobile-nav-scrim--visible")}
          type="button"
          aria-label="关闭导航"
          onClick={() => setMobileNavOpen(false)}
        />

        <aside className={cn("sidebar", mobileNavOpen && "sidebar--open")}>
          <div className="brand">
            <span className="brand__mark" aria-hidden="true">
              <Orbit size={29} strokeWidth={2.25} />
            </span>
            <span className="brand__copy">
              <strong>AVORION</strong>
              <span>管理中心</span>
            </span>
          </div>

          <nav className="primary-nav" aria-label="主导航">
            {primaryNav.map((item) => {
              const Icon = item.icon;
              const activeLabel = page === "players" ? "玩家管理" : page === "alliances" ? "联盟管理" : page === "game" ? "游戏管理" : "服务器管理";
              const active = item.label === activeLabel;
              return (
                <button
                  key={item.label}
                  type="button"
                  className={cn("primary-nav__item", active && "primary-nav__item--active")}
                  aria-current={active ? "page" : undefined}
                  onClick={() => {
                    if (item.label === "服务器管理") {
                      navigate("/server/control");
                      setMobileNavOpen(false);
                      return;
                    }
                    if (item.label === "玩家管理") {
                      navigate("/players");
                      setMobileNavOpen(false);
                      return;
                    }
                    if (item.label === "联盟管理") {
                      navigate("/alliances");
                      setMobileNavOpen(false);
                      return;
                    }
                    if (item.label === "游戏管理") {
                      navigate("/game/rewards");
                      setMobileNavOpen(false);
                      return;
                    }
                    onUnavailable(item.label);
                  }}
                >
                  <Icon size={22} strokeWidth={2.05} aria-hidden="true" />
                  <span>{item.label}</span>
                </button>
              );
            })}
          </nav>

          <div className={cn("sidebar-status", updatePresentation && "sidebar-status--setup")}>
            <span className={cn("sidebar-status__signal", `sidebar-status__signal--${updatePresentation ? "warning" : status.tone}`)} />
            <div>
              <strong>{updatePresentation?.sidebarTitle ?? status.detail}</strong>
              <span className={cn(updatePresentation?.sidebarBadge && "sidebar-status__badge")}>
                {updatePresentation?.sidebarDetail ?? displayVersion}
              </span>
            </div>
            <ChevronDown size={17} aria-hidden="true" />
          </div>
        </aside>

        <main className="main-panel">
          <div className={cn("page-frame", !serverPage && "page-frame--module")}>
            {serverPage && <header className="page-header">
              <div className="page-header__identity">
                <h1>服务器管理</h1>
                <div className="breadcrumb" aria-label="面包屑">
                  <span>服务器管理</span>
                  <span aria-hidden="true">/</span>
                  <strong>{pageLabel}</strong>
                  {updateSubpageLabel && (
                    <>
                      <span aria-hidden="true">/</span>
                      <strong>{updateSubpageLabel}</strong>
                    </>
                  )}
                </div>
              </div>

              <DropdownMenu.Root>
                <DropdownMenu.Trigger className={cn("server-switcher", updatePresentation && "server-switcher--setup")} aria-label="选择服务器">
                  <span className="server-switcher__icon" aria-hidden="true">
                    <Box size={24} strokeWidth={2.1} />
                  </span>
                  <span className="server-switcher__copy">
                    <strong>{updatePresentation?.serverName ?? displayName}</strong>
                    {!updatePresentation && (
                      <span>
                        {displayVersion}
                        <i aria-hidden="true">·</i>
                        {displayGalaxy}
                      </span>
                    )}
                  </span>
                  <StatusPill tone={updatePresentation ? "warning" : status.tone} compact>
                    {updatePresentation?.badge ?? status.label}
                  </StatusPill>
                  <ChevronDown size={17} aria-hidden="true" />
                </DropdownMenu.Trigger>
                <DropdownMenu.Portal>
                  <DropdownMenu.Content className="server-menu" align="end" sideOffset={8}>
                    <DropdownMenu.Label className="server-menu__label">当前服务器</DropdownMenu.Label>
                    <DropdownMenu.Item className="server-menu__item">
                      <ShieldCheck size={17} aria-hidden="true" />
                      <span>
                        <strong>{displayName}</strong>
                        <small>{displayGalaxy}</small>
                      </span>
                    </DropdownMenu.Item>
                  </DropdownMenu.Content>
                </DropdownMenu.Portal>
              </DropdownMenu.Root>
            </header>}

            {serverPage && <Tabs.Root
              value={page}
              onValueChange={(nextPage) => {
                const target = serverTabs.find((tab) => tab.value === nextPage);
                if (target?.path) navigate(target.path);
              }}
            >
              <Tabs.List className="server-tabs" aria-label="服务器页面">
                {serverTabs.map((tab) => (
                  <Tabs.Trigger
                    key={tab.value}
                    className="server-tabs__trigger"
                    value={tab.value}
                    disabled={!tab.enabled}
                    onClick={() => {
                      if (!tab.enabled) onUnavailable(tab.label);
                    }}
                  >
                    {tab.label}
                  </Tabs.Trigger>
                ))}
              </Tabs.List>
            </Tabs.Root>}

            <div className="page-content" key={location.pathname}>
              {children}
            </div>
          </div>
        </main>
      </div>
    </Tooltip.Provider>
  );
}
