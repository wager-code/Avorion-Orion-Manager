export type BusyAction = "detect" | "validate" | null;
export type SetupStep = 1 | 2 | 3;

export type UpdateComponentValidation = {
  requestedPath: string;
  resolvedExecutablePath: string | null;
  exists: boolean;
  readable: boolean;
  version: string | null;
  checks: string[];
  issues: string[];
};

export type UpdateEnvironmentValidation = {
  valid: boolean;
  steamCmd: UpdateComponentValidation;
  server: UpdateComponentValidation;
  serverDriveAvailableBytes: number | null;
  validatedAt: string;
};

export type UpdateEnvironmentDetection = {
  status: "complete" | "partial" | "not-found";
  steamCmd: UpdateComponentValidation | null;
  server: UpdateComponentValidation | null;
  scannedAt: string;
};

export type UpdateEnvironmentConfiguration = {
  steamCmdPath: string;
  serverDirectory: string;
  validatedAt: string;
};

export type UpdateEnvironmentCurrent = {
  configured: boolean;
  configuration: UpdateEnvironmentConfiguration | null;
  validation: UpdateEnvironmentValidation | null;
};

export type ServerSetupDraftConfiguration = {
  serverName: string;
  galaxyName: string;
  galaxyMode: "new" | "existing";
  maxPlayers: number;
  galaxyDirectory: string;
  listenAddress: string;
  gamePort: number;
  queryPort: number;
  rconEnabled: boolean;
  rconPort: number;
  allowFirewallChange: boolean;
  installManagementMod: boolean;
  updatedAt: string;
};

export type ServerSetupPreflightResult = {
  valid: boolean;
  updateEnvironmentValid: boolean;
  galaxyPathValid: boolean;
  rconPasswordAccepted: boolean;
  ports: Array<{ purpose: string; port: number; protocol: string; available: boolean; evidence: string }>;
  issues: Array<{ field: string; code: string; message: string; severity: "error" | "warning" }>;
  checkedAt: string;
};

export type ServerSetupApplicationResult = {
  mode: "launch-profile-ready" | "server-ini-updated";
  galaxyDirectory: string;
  launchProfilePath: string;
  serverIniPath: string | null;
  serverIniUpdated: boolean;
  rconConfigured: boolean;
  configurationSha256: string;
  deferredActions: string[];
  appliedAt: string;
  managementBridge?: {
    version: string;
    targetDirectory: string;
    manifestSha256: string;
    backupDirectory: string | null;
    configurationCreated: boolean;
    configured: boolean;
    restartRequired: boolean;
    installedAt: string;
  } | null;
};

export type ServerSetupApplicationOperation = {
  operationId: string;
  type: "server.configure";
  status: "queued" | "running" | "succeeded" | "failed" | "cancelled";
  progressPercent: number | null;
  currentStep: string | null;
  acceptedAt: string;
  result: ServerSetupApplicationResult | null;
  error: { code: string; message: string; retryable: boolean } | null;
  request: { galaxyDirectory?: string; rconPasswordProvided?: boolean } | null;
};

export type ServerInitializationResult = {
  mode: "new-galaxy-running";
  processId: number;
  galaxyDirectory: string;
  serverIniPath: string;
  consoleSaveConfirmed: boolean;
  consoleStopConfirmed: boolean;
  rconConfigured: boolean;
  rconAuthenticated: boolean;
  healthEvidence: string[];
  deferredActions: string[];
  startedAt: string;
  completedAt: string;
};

export type ServerInitializationOperation = {
  operationId: string;
  type: "server.initialize";
  status: "queued" | "running" | "succeeded" | "failed" | "cancelled";
  progressPercent: number | null;
  currentStep: string | null;
  acceptedAt: string;
  result: ServerInitializationResult | null;
  error: { code: string; message: string; retryable: boolean } | null;
  request: { galaxyDirectory?: string; rconPasswordProvided?: boolean } | null;
};

export type ApiErrorEnvelope = { error?: { code?: string; message?: string } };

export type UpdateCheckResult = {
  appId: number;
  branch: string;
  steamCmdPath: string;
  serverDirectory: string;
  currentVersion: string | null;
  currentBuildId: string | null;
  latestBuildId: string;
  updateAvailable: boolean | null;
  evidence: string[];
  checkedAt: string;
};

export type UpdateVerificationResult = {
  valid: boolean;
  appId: number;
  branch: string;
  serverDirectory: string;
  installedBuildId: string | null;
  files: Array<{ name: string; path: string; sizeBytes: number; sha256: string; version: string | null }>;
  checks: string[];
  verifiedAt: string;
};

export type UpdateRollbackPointResult = {
  pointId: string;
  storagePath: string;
  serverDirectory: string;
  galaxyDirectory: string;
  serverFileCount: number;
  galaxyFileCount: number;
  totalBytes: number;
  manifestSha256: string;
  installedBuildId: string | null;
  verified: boolean;
  createdAt: string;
  verifiedAt: string;
  evidence: string[];
};

export type UpdateRollbackPointAvailability = {
  available: boolean;
  point: UpdateRollbackPointResult | null;
};

export type UpdateInspectionOperation<TResult> = {
  operationId: string;
  type: string;
  status: "queued" | "running" | "succeeded" | "failed" | "cancelled";
  progressPercent: number | null;
  currentStep: string | null;
  acceptedAt: string;
  result: TResult | null;
  error: { code: string; message: string; retryable: boolean; details?: { logExcerpt?: string; exitCode?: number | null; stage?: string; systemError?: string; exceptionType?: string } | null } | null;
};

export type SteamCmdInstallationResult = {
  installDirectory: string;
  executablePath: string;
  sourceUrl: string | null;
  archiveSha256: string | null;
  archiveBytes: number | null;
  executableBytes: number;
  signer: string;
  installedAt: string | null;
  reusedExisting?: boolean;
  executableSha256?: string;
  verifiedAt?: string;
};

export type AvorionServerInstallationResult = {
  installDirectory: string;
  executablePath: string;
  runnerPath: string;
  appId: number;
  branch: string;
  steamCmdSigner: string;
  executableBytes: number;
  installedAt: string | null;
  reusedExisting?: boolean;
  executableSha256?: string;
  runnerSha256?: string;
  verifiedAt?: string;
};

export type InstallationResult = SteamCmdInstallationResult | AvorionServerInstallationResult;

export type InstallOperation = {
  operationId: string;
  type: string;
  status: "queued" | "running" | "succeeded" | "failed" | "cancelled";
  progressPercent: number | null;
  currentStep: string | null;
  acceptedAt: string;
  result: InstallationResult | null;
  error: { code: string; message: string; retryable: boolean; details?: { stage?: string; exitCode?: number; logExcerpt?: string } | null } | null;
  request: { installDirectory?: string; plannedServerDirectory?: string; steamCmdPath?: string; appId?: number; branch?: string } | null;
};


export type ManagementBridgeStatus = {
  packageAvailable: boolean;
  packageVersion: string | null;
  installed: boolean;
  installedVersion: string | null;
  current: boolean;
  configured: boolean;
  targetDirectory: string | null;
  state: "galaxy-unconfigured" | "galaxy-invalid" | "missing" | "outdated-or-modified" | "installed-not-enabled" | "current";
  issues: string[];
  checkedAt: string;
};
