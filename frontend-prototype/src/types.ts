export type ServerLifecycle =
  | "running"
  | "stopped"
  | "starting"
  | "stopping"
  | "restarting"
  | "unknown";

export type ConnectionState = "connected" | "disconnected" | "degraded" | "unknown";

export interface ServerStatus {
  serverId: string;
  name: string;
  lifecycle: ServerLifecycle;
  processId: number | null;
  version: string | null;
  galaxyName: string | null;
  uptimeSeconds: number | null;
  onlinePlayers: number | null;
  maxPlayers: number | null;
  rcon: { status: ConnectionState; latencyMs: number | null; unavailableReason?: string | null };
  steamQuery: { status: ConnectionState; latencyMs: number | null; unavailableReason?: string | null };
  lastSave: { at: string | null; source: string | null; unavailableReason?: string | null };
  agent: { connected: boolean; lastHeartbeatAt: string | null };
  provenance: { sampledAt: string; source: string; freshness: string; unavailableReason?: string | null };
}

export interface ApiOperation {
  operationId: string;
  type: string;
  status: "queued" | "running" | "succeeded" | "failed" | "cancelled";
  progressPercent: number | null;
  currentStep: string | null;
  acceptedAt: string;
  startedAt: string | null;
  completedAt: string | null;
  result: unknown;
  request?: unknown;
  error: { code: string; message: string; retryable: boolean } | null;
}

export type EventKind = "success" | "info" | "warning" | "player";

export interface ServerEvent {
  id: string;
  time: string;
  kind: EventKind;
  message: string;
}

export type RangeKey = "1h" | "24h" | "7d";

export interface PerformancePoint {
  label: string;
  cpu: number;
  memory: number;
  download: number;
  upload: number;
  players: number;
}

export interface DataProvenance {
  sampledAt: string;
  source: string;
  freshness: "live" | "recent" | "stale" | "unavailable" | string;
  unavailableReason?: string | null;
}

export type DiagnosticState = "healthy" | "warning" | "error" | "unknown";

export interface DiagnosticItem {
  key: string;
  status: DiagnosticState;
  resultCode: string;
  message: string;
  evidence: Record<string, unknown>;
  provenance: DataProvenance;
}

export interface DiagnosticRun {
  runId: string;
  status: string;
  startedAt: string;
  completedAt: string | null;
  items: DiagnosticItem[];
}

export interface ServerLogEntry {
  id: string;
  occurredAt: string;
  category: string;
  message: string;
  rawLine: string;
  sourceFile: string;
  sourceLineNumber: number;
  sourceLastWriteAt: string;
  parseConfidence: number;
}

export interface ServerBackupRecord {
  backupId: string;
  fileName: string;
  createdAt: string;
  sizeBytes: number;
  status: "available" | "incomplete" | "unreadable" | string;
}

export interface PerformanceSnapshot {
  cpu: { processPercent: number | null; hostPercent: number | null };
  memory: { workingSetBytes: number | null; privateBytes: number | null; hostTotalBytes: number | null };
  disk: { totalBytes: number | null; usedBytes: number | null; availableBytes: number | null; volume: string | null };
  network: { scope: "host" | "process" | "unavailable"; downloadBytesPerSecond: number | null; uploadBytesPerSecond: number | null };
  onlinePlayers: number | null;
  uptimeSeconds: number | null;
  provenance: DataProvenance;
}

export interface PerformanceHistoryPoint {
  at: string;
  cpuProcessPercent: number | null;
  memoryWorkingSetBytes: number | null;
  networkDownloadBytesPerSecond: number | null;
  networkUploadBytesPerSecond: number | null;
  onlinePlayers: number | null;
}

export interface PerformanceHistory {
  range: RangeKey;
  points: PerformanceHistoryPoint[];
  warningCode: string | null;
}

export type SectorStatus = "players" | "idle" | "task";

export interface SectorRecord {
  id: string;
  x: number;
  y: number;
  name: string;
  status: SectorStatus;
  players: number;
  idleMinutes: number | null;
  memoryMb: number;
  eligible: boolean;
}

export interface MemoryPoint {
  label: string;
  memory: number;
}

export type MemoryStrategy = "notify" | "unload" | "restart";

export type UpdateSetupStage =
  | "unconfigured"
  | "install-plan"
  | "manual-paths"
  | "detected"
  | "ready"
  | "setup-basic"
  | "management";

export type ServerAction =
  | "save"
  | "restart"
  | "shutdown"
  | "force-stop"
  | "start"
  | "backup"
  | "update-check";
