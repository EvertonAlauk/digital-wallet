"use client";

import { usePathname, useRouter } from "next/navigation";
import { useEffect, useState, type ReactNode } from "react";
import { useAuth } from "@/components/auth/auth-provider";
import { displayName } from "@/lib/auth/types";
import styles from "./protected.module.css";

/**
 * Route guard for the signed-in pages.
 *
 * This guard only improves the experience: it sends visitors without a session to the login page. The real protection
 * is in the BFF and the API, which reject every request that has no valid session. Hiding a page here does not protect
 * the data behind it.
 */
export default function ProtectedLayout({ children }: { children: ReactNode }) {
  const { user, status, logout } = useAuth();
  const router = useRouter();
  const pathname = usePathname();
  const [signingOut, setSigningOut] = useState(false);
  const [logoutError, setLogoutError] = useState<string | null>(null);

  useEffect(() => {
    // After the user signs out on purpose, the guard must not send them back with a returnUrl.
    if (status === "unauthenticated" && !signingOut) {
      // The query string is part of the page (for example /account?id=...), so it is kept in returnUrl.
      const current = `${pathname}${window.location.search}`;
      router.replace(`/login?returnUrl=${encodeURIComponent(current)}`);
    }
  }, [status, signingOut, pathname, router]);

  async function handleLogout() {
    setSigningOut(true);
    setLogoutError(null);
    try {
      await logout();
      router.replace("/login");
    } catch {
      setSigningOut(false);
      setLogoutError("Could not sign out right now. Try again.");
    }
  }

  if (status !== "authenticated" || !user) {
    return (
      <p className={styles.loading} role="status">
        Loading…
      </p>
    );
  }

  return (
    <div className={styles.shell}>
      <header className={styles.header}>
        <span className={styles.userName}>{displayName(user)}</span>
        <button type="button" className={styles.logout} onClick={handleLogout} disabled={signingOut}>
          Sign out
        </button>
      </header>
      {logoutError && (
        <p role="alert" className={styles.alert}>
          {logoutError}
        </p>
      )}
      <main className={styles.main}>{children}</main>
    </div>
  );
}
