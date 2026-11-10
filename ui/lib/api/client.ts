/**
 * The only way the frontend talks to the backend. Every request goes to the BFF on the same origin, and the browser
 * sends the session cookie by itself. This module never reads, stores or sends a token.
 */

export class ApiError extends Error {
  constructor(
    readonly status: number,
    message: string,
  ) {
    super(message);
    this.name = "ApiError";
  }
}

/** Sent on every POST and on every /api/* call. The BFF rejects requests without it (cross-site protection). */
const CSRF_HEADERS = { "X-CSRF": "1" };

type UnauthorizedListener = () => void;

let unauthorizedListener: UnauthorizedListener | null = null;

/** Registered by the AuthProvider. Called when a protected request is answered with 401. */
export function onUnauthorized(listener: UnauthorizedListener | null): void {
  unauthorizedListener = listener;
}

export interface ApiRequestOptions {
  method?: "GET" | "POST" | "PUT" | "PATCH" | "DELETE";
  /** Sent as JSON. */
  body?: unknown;
  /** Extra headers, such as Idempotency-Key. Authorization is never accepted here. */
  headers?: Record<string, string>;
  /**
   * When true (the default), a 401 clears the local auth state and sends the browser to the login page.
   * Set it to false where 401 is an ordinary answer, such as a wrong password or a session check.
   */
  handleUnauthorized?: boolean;
}

export async function apiFetch<T>(path: string, options: ApiRequestOptions = {}): Promise<T> {
  const { method = "GET", body, headers, handleUnauthorized = true } = options;

  const response = await fetch(path, {
    method,
    credentials: "same-origin",
    headers: {
      ...CSRF_HEADERS,
      ...(body !== undefined ? { "Content-Type": "application/json" } : {}),
      ...headers,
    },
    body: body !== undefined ? JSON.stringify(body) : undefined,
  });

  if (response.status === 401 && handleUnauthorized) {
    unauthorizedListener?.();
    redirectToLogin();
  }

  if (!response.ok) {
    throw new ApiError(response.status, await readErrorMessage(response));
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return (await response.json()) as T;
}

type RedirectHandler = (url: string) => void;

const defaultRedirect: RedirectHandler = (url) => window.location.replace(url);

let redirectHandler: RedirectHandler = defaultRedirect;

/**
 * Replaces how the browser is sent to another page. The default performs a full navigation. Tests replace it, because
 * jsdom does not allow stubbing window.location.
 */
export function setRedirectHandler(handler: RedirectHandler | null): void {
  redirectHandler = handler ?? defaultRedirect;
}

/** Sends the browser to the login page, and remembers the page it came from. */
function redirectToLogin(): void {
  if (typeof window === "undefined") {
    return;
  }

  const current = `${window.location.pathname}${window.location.search}`;
  if (window.location.pathname.startsWith("/login")) {
    return;
  }

  redirectHandler(`/login?returnUrl=${encodeURIComponent(current)}`);
}

async function readErrorMessage(response: Response): Promise<string> {
  try {
    const problem = (await response.json()) as { detail?: unknown; title?: unknown };
    if (typeof problem.detail === "string") {
      return problem.detail;
    }

    if (typeof problem.title === "string") {
      return problem.title;
    }
  } catch {
    // The body is not JSON. The status code is still a useful message.
  }

  return response.statusText || `Request failed with status ${response.status}`;
}
