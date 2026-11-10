"use client";

import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from "react";
import { onUnauthorized } from "@/lib/api/client";
import { fetchSession, loginRequest, logoutRequest } from "@/lib/auth/session-api";
import { openSessionChannel, type SessionChannel } from "@/lib/auth/session-channel";
import type { SessionUser } from "@/lib/auth/types";

export type AuthStatus = "loading" | "authenticated" | "unauthenticated";

export interface AuthContextValue {
  user: SessionUser | null;
  status: AuthStatus;
  login: (username: string, password: string) => Promise<SessionUser>;
  logout: () => Promise<void>;
}

const AuthContext = createContext<AuthContextValue | null>(null);

/**
 * Holds what the browser knows about the session. It never holds a token: the session is a cookie that the BFF
 * manages. On start-up it asks the BFF who the user is, so a page reload keeps the user signed in.
 */
export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<SessionUser | null>(null);
  const [status, setStatus] = useState<AuthStatus>("loading");
  const channelRef = useRef<SessionChannel | null>(null);

  const markSignedOut = useCallback(() => {
    setUser(null);
    setStatus("unauthenticated");
  }, []);

  const restoreSession = useCallback(async () => {
    try {
      const current = await fetchSession();
      if (current) {
        setUser(current);
        setStatus("authenticated");
      } else {
        markSignedOut();
      }
    } catch {
      // The BFF could not be reached. Treat the user as signed out; the next page load asks again.
      markSignedOut();
    }
  }, [markSignedOut]);

  useEffect(() => {
    // Restores the session when the page loads. The state changes happen in the promise callbacks, after the request.
    let active = true;
    fetchSession()
      .then((current) => {
        if (!active) {
          return;
        }

        if (current) {
          setUser(current);
          setStatus("authenticated");
        } else {
          markSignedOut();
        }
      })
      .catch(() => {
        // The BFF could not be reached. Treat the user as signed out; the next page load asks again.
        if (active) {
          markSignedOut();
        }
      });

    onUnauthorized(markSignedOut);

    channelRef.current = openSessionChannel((message) => {
      if (message.type === "logout") {
        markSignedOut();
      } else {
        // Another tab signed in. Ask the BFF instead of trusting the message.
        void restoreSession();
      }
    });

    return () => {
      active = false;
      onUnauthorized(null);
      channelRef.current?.close();
      channelRef.current = null;
    };
  }, [restoreSession, markSignedOut]);

  const login = useCallback(async (username: string, password: string) => {
    const signedIn = await loginRequest(username, password);
    setUser(signedIn);
    setStatus("authenticated");
    channelRef.current?.post({ type: "login" });
    return signedIn;
  }, []);

  // If the BFF cannot end the session, the error reaches the caller and the user stays signed in on screen.
  const logout = useCallback(async () => {
    await logoutRequest();
    markSignedOut();
    channelRef.current?.post({ type: "logout" });
  }, [markSignedOut]);

  const value = useMemo<AuthContextValue>(
    () => ({ user, status, login, logout }),
    [user, status, login, logout],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const value = useContext(AuthContext);
  if (!value) {
    throw new Error("useAuth must be used inside AuthProvider.");
  }

  return value;
}
