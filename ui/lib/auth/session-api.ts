import { ApiError, apiFetch } from "@/lib/api/client";
import type { SessionUser } from "@/lib/auth/types";

interface UserResponse {
  user: SessionUser;
}

/**
 * Signs in through the BFF. A wrong password or a locked account is a 401 with the same answer, so the caller shows
 * a generic message. The 401 is not treated as "send to login": the user is already on the login page.
 */
export async function loginRequest(username: string, password: string): Promise<SessionUser> {
  const response = await apiFetch<UserResponse>("/bff/login", {
    method: "POST",
    body: { username, password },
    handleUnauthorized: false,
  });

  return response.user;
}

/** Restores the session on page load. Returns null when there is no valid session. */
export async function fetchSession(): Promise<SessionUser | null> {
  try {
    const response = await apiFetch<UserResponse>("/bff/session", { handleUnauthorized: false });
    return response.user;
  } catch (error) {
    if (error instanceof ApiError && error.status === 401) {
      return null;
    }

    throw error;
  }
}

/** Ends the session in the BFF, which also ends the Keycloak session. Idempotent on the server. */
export async function logoutRequest(): Promise<void> {
  await apiFetch<void>("/bff/logout", { method: "POST", body: {}, handleUnauthorized: false });
}
