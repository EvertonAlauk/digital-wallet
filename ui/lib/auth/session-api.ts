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

export interface RegisterRequest {
  email: string;
  password: string;
  firstName: string;
  lastName: string;
}

/**
 * Creates an account. The BFF does not sign the user in: the caller sends them to the login page.
 * A 409 (email taken) is an ordinary answer here, so it is not treated as "send to login".
 */
export async function registerRequest(request: RegisterRequest): Promise<void> {
  await apiFetch<void>("/bff/register", { method: "POST", body: request, handleUnauthorized: false });
}
