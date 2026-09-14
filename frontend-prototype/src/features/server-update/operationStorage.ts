const operationIdPattern = /^op_[a-zA-Z0-9]+$/;

export const activeOperationStorageKeys = {
  installation: "avorion.update.active-installation-operation",
  setup: "avorion.update.active-setup-operation",
  initialization: "avorion.update.active-initialization-operation",
  updateCheck: "avorion.update.latest-check-operation",
  fileVerification: "avorion.update.latest-verification-operation",
  rollbackPoint: "avorion.update.latest-rollback-point-operation",
} as const;

export function readStoredOperationId(key: string) {
  try {
    const value = (window.sessionStorage.getItem(key) ?? "").trim();
    return operationIdPattern.test(value) ? value : null;
  } catch {
    return null;
  }
}

export function storeOperationId(key: string, operationId: string) {
  if (!operationIdPattern.test(operationId)) return;
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

export function readOperationQuery(searchParams: URLSearchParams) {
  return {
    installation: searchParams.get("operation"),
    setup: searchParams.get("setupOperation"),
    initialization: searchParams.get("initializeOperation"),
  };
}
