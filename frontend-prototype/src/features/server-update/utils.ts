import { Database, ServerCog, Gamepad2, ShieldCheck } from 'lucide-react';
import type { ApiErrorEnvelope,  InstallationResult, SteamCmdInstallationResult, AvorionServerInstallationResult, UpdateEnvironmentValidation } from './types';
export function isSteamCmdInstallationResult(result: InstallationResult | null): result is SteamCmdInstallationResult {
  return Boolean(result && "signer" in result && !("appId" in result));
}

export function isAvorionServerInstallationResult(result: InstallationResult | null): result is AvorionServerInstallationResult {
  return Boolean(result && "appId" in result);
}

export function readSessionValue(key: string, fallback: string) {
  try {
    return window.sessionStorage.getItem(key) ?? fallback;
  } catch {
    return fallback;
  }
}

export const activeOperationStorageKeys = {
  installation: "avorion.update.active-installation-operation",
  setup: "avorion.update.active-setup-operation",
  initialization: "avorion.update.active-initialization-operation",
  updateCheck: "avorion.update.latest-check-operation",
  fileVerification: "avorion.update.latest-verification-operation",
  rollbackPoint: "avorion.update.latest-rollback-point-operation",
} as const;

export function readStoredOperationId(key: string) {
  const value = readSessionValue(key, "").trim();
  return /^op_[a-zA-Z0-9]+$/.test(value) ? value : null;
}

export function storeOperationId(key: string, operationId: string) {
  if (!/^op_[a-zA-Z0-9]+$/.test(operationId)) return;
  try {
    window.sessionStorage.setItem(key, operationId);
  } catch {
    // The URL still keeps the operation recoverable while this page is open.
  }
}

export function clearStoredOperationId(key: string) {
  try {
    window.sessionStorage.removeItem(key);
  } catch {
    // Storage may be unavailable; there is nothing else to clear.
  }
}

export function readInstallDirectory(key: string, fallback: string, legacyInvalidDefault?: string) {
  const stored = readSessionValue(key, fallback);
  return legacyInvalidDefault && stored.toLowerCase() === legacyInvalidDefault.toLowerCase()
    ? fallback
    : stored;
}

export const defaultSteamCmdInstallDirectory = "C:\\Users\\Public\\AvorionAdmin-SteamCMD";
export const legacyInvalidSteamCmdInstallDirectory = "C:\\AvorionAdmin\\tools\\steamcmd";

export async function readApiError(response: Response) {
  const body = await response.json().catch(() => null) as ApiErrorEnvelope | null;
  return body?.error?.message || `请求失败（HTTP ${response.status}）`;
}

export function formatValidationIssues(validation: UpdateEnvironmentValidation) {
  const issues = [...validation.steamCmd.issues, ...validation.server.issues];
  return issues.length > 0 ? issues.join("；") : "路径验证未通过";
}

export function formatBytes(value: number | null) {
  if (value === null) return "未知";
  const gibibytes = value / 1024 / 1024 / 1024;
  return `${gibibytes >= 100 ? gibibytes.toFixed(0) : gibibytes.toFixed(1)} GB 可用`;
}

export function formatDataSize(value: number) {
  if (value < 1024 * 1024 * 1024) return `${(value / 1024 / 1024).toFixed(1)} MB`;
  return `${(value / 1024 / 1024 / 1024).toFixed(2)} GB`;
}

export const serverConfigurationChecks = [
  { label: "Galaxy / 存档", result: "尚未配置", icon: Database },
  { label: "基础服务器配置", result: "尚未配置", icon: ServerCog },
  { label: "游戏与查询端口", result: "尚未验证", icon: Gamepad2 },
  { label: "RCON", result: "尚未配置", icon: ShieldCheck },
] as const;


