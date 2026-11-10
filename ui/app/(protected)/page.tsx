"use client";

import { useAuth } from "@/components/auth/auth-provider";
import { displayName } from "@/lib/auth/types";
import styles from "./protected.module.css";

export default function HomePage() {
  const { user } = useAuth();

  // The layout only renders its children for a signed-in user, so this is a guard for the type checker.
  if (!user) {
    return null;
  }

  return (
    <section className={styles.home}>
      <h1 className={styles.greeting}>Hello, {displayName(user)}</h1>

      {/*
        Wallet content goes here: accounts, balances and operations.
        Call the API with apiFetch from lib/api/client.ts, for example apiFetch("/api/accounts").
        Never add a token or an Authorization header: the BFF attaches the session's token.
      */}
      <section aria-label="Wallet content area" className={styles.contentArea} data-testid="content-area">
        <p>Wallet content will be added here.</p>
      </section>
    </section>
  );
}
