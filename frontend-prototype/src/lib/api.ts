type AdminSession = {
  authenticated: boolean;
  csrfToken: string | null;
  expiresAt: string | null;
};

let cachedSession: AdminSession | null = null;
let sessionRequest: Promise<AdminSession> | null = null;

const unsafeMethods = new Set(["POST", "PUT", "PATCH", "DELETE"]);

export function createIdempotencyKey(prefix: string): string {
  const nativeUuid = globalThis.crypto?.randomUUID?.();
  if (nativeUuid) return `${prefix}-${nativeUuid}`;
  const bytes = new Uint8Array(16);
  globalThis.crypto.getRandomValues(bytes);
  const random = Array.from(bytes, (value) => value.toString(16).padStart(2, "0")).join("");
  return `${prefix}-${Date.now().toString(36)}-${random}`;
}

async function readSession(): Promise<AdminSession> {
  const response = await fetch("/api/v1/session", {
    credentials: "same-origin",
    headers: { Accept: "application/json" },
  });
  if (!response.ok) throw new Error("无法读取管理员会话");
  return response.json() as Promise<AdminSession>;
}

async function createLocalSession(): Promise<AdminSession> {
  const response = await fetch("/api/v1/session/local", {
    method: "POST",
    credentials: "same-origin",
    headers: { Accept: "application/json" },
  });
  if (!response.ok) throw new Error("无法建立本机管理员会话");
  return response.json() as Promise<AdminSession>;
}

async function ensureAdminSession(forceRefresh = false): Promise<AdminSession> {
  if (!forceRefresh && cachedSession?.authenticated && cachedSession.csrfToken) return cachedSession;
  if (!forceRefresh && sessionRequest) return sessionRequest;

  sessionRequest = (async () => {
    const current = await readSession();
    cachedSession = current.authenticated && current.csrfToken ? current : await createLocalSession();
    return cachedSession;
  })();

  try {
    return await sessionRequest;
  } finally {
    sessionRequest = null;
  }
}

export async function apiFetch(input: RequestInfo | URL, init: RequestInit = {}): Promise<Response> {
  const method = (init.method ?? "GET").toUpperCase();
  if (!unsafeMethods.has(method)) {
    const requestUrl = typeof input === "string" ? input : input instanceof URL ? input.href : input.url;
    if (requestUrl.includes("/management-bridge/") || requestUrl.includes("/reward-batches") ||
      requestUrl.includes("/inventory")) {
      await ensureAdminSession();
    }
    let response = await fetch(input, { ...init, credentials: "same-origin" });
    if (response.status === 401) {
      await ensureAdminSession(true);
      response = await fetch(input, { ...init, credentials: "same-origin" });
    }
    return response;
  }

  const send = async (forceRefresh: boolean) => {
    const session = await ensureAdminSession(forceRefresh);
    if (!session.csrfToken) throw new Error("管理员会话缺少写操作令牌");
    const headers = new Headers(init.headers);
    headers.set("X-CSRF-Token", session.csrfToken);
    return fetch(input, { ...init, headers, credentials: "same-origin" });
  };

  let response = await send(false);
  if (response.status === 401) {
    cachedSession = null;
    response = await send(true);
  }
  return response;
}
