"use client";

import { AccountsPanel } from "@/components/accounts/accounts-panel";
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
      <AccountsPanel />
    </section>
  );
}
