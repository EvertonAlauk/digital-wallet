"use client";

import Link from "next/link";
import { useEffect, useState, type FormEvent } from "react";
import { ApiError } from "@/lib/api/client";
import { createAccount, listAccounts } from "@/lib/api/accounts";
import { formatMoney } from "@/lib/accounts/money";
import { MAX_ACCOUNT_NAME_LENGTH } from "@/lib/accounts/ids";
import type { Account } from "@/lib/accounts/types";
import styles from "./accounts.module.css";

type PanelState =
  | { status: "loading" }
  | { status: "ready"; accounts: Account[] }
  | { status: "error"; message: string };

/** The signed-in user's accounts, with a form to open a new one. */
export function AccountsPanel() {
  const [state, setState] = useState<PanelState>({ status: "loading" });
  const [name, setName] = useState("");
  const [creating, setCreating] = useState(false);
  const [createError, setCreateError] = useState<string | null>(null);

  useEffect(() => {
    let active = true;
    listAccounts()
      .then((list) => {
        if (active) {
          setState({ status: "ready", accounts: list.items });
        }
      })
      .catch(() => {
        if (active) {
          setState({ status: "error", message: "Could not load your accounts." });
        }
      });

    return () => {
      active = false;
    };
  }, []);

  async function refresh() {
    try {
      const list = await listAccounts();
      setState({ status: "ready", accounts: list.items });
    } catch {
      setState({ status: "error", message: "Could not load your accounts." });
    }
  }

  async function handleCreate(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const trimmed = name.trim();
    if (trimmed.length > MAX_ACCOUNT_NAME_LENGTH) {
      setCreateError(`The name can have at most ${MAX_ACCOUNT_NAME_LENGTH} characters.`);
      return;
    }

    setCreating(true);
    setCreateError(null);
    try {
      await createAccount(trimmed === "" ? null : trimmed);
      setName("");
      await refresh();
    } catch (error) {
      setCreateError(error instanceof ApiError ? error.message : "Could not open the account. Try again.");
    } finally {
      setCreating(false);
    }
  }

  return (
    <section className={styles.section} aria-labelledby="accounts-title">
      <h2 id="accounts-title" className={styles.sectionTitle}>
        Your accounts
      </h2>

      <form className={styles.row} onSubmit={handleCreate} noValidate>
        <div className={styles.field}>
          <label htmlFor="new-account-name">Account name (optional)</label>
          <input
            id="new-account-name"
            value={name}
            onChange={(event) => setName(event.target.value)}
            disabled={creating}
          />
        </div>
        <button type="submit" className={styles.button} disabled={creating}>
          {creating ? "Opening…" : "Create account"}
        </button>
      </form>
      {createError && (
        <p role="alert" className={styles.error}>
          {createError}
        </p>
      )}

      {state.status === "loading" && <p className={styles.muted}>Loading accounts…</p>}

      {state.status === "error" && (
        <p role="alert" className={styles.error}>
          {state.message}
        </p>
      )}

      {state.status === "ready" && state.accounts.length === 0 && (
        <p className={styles.muted}>You have no accounts yet. Create one to get started.</p>
      )}

      {state.status === "ready" && state.accounts.length > 0 && (
        <ul className={styles.list} aria-label="Accounts">
          {state.accounts.map((account) => (
            <li key={account.id} className={styles.listItem}>
              <Link href={`/account?id=${account.id}`}>
                <span>{account.name ?? "Untitled account"}</span>
                <strong>{formatMoney(account.balance, account.currency)}</strong>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
