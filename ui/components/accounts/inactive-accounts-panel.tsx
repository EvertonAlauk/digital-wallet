"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { ApiError } from "@/lib/api/client";
import { activateAccount, listAccounts } from "@/lib/api/accounts";
import { formatMoney } from "@/lib/accounts/money";
import { ACCOUNTS_PAGE_SIZE } from "@/lib/accounts/paging";
import type { Account } from "@/lib/accounts/types";
import { BackLink } from "./back-link";
import { Pagination } from "./pagination";
import styles from "./accounts.module.css";

interface PageData {
  accounts: Account[];
  totalCount: number;
  offset: number;
}

type PanelState = { status: "loading" } | ({ status: "ready" } & PageData) | { status: "error"; message: string };

/** One page of inactive accounts. If the page is empty after an activation, the previous page is shown. */
async function fetchPage(offset: number): Promise<PageData> {
  const list = await listAccounts("inactive", offset, ACCOUNTS_PAGE_SIZE);

  if (list.items.length === 0 && offset > 0) {
    return fetchPage(Math.max(0, offset - ACCOUNTS_PAGE_SIZE));
  }

  return { accounts: list.items, totalCount: list.totalCount, offset };
}

/** The user's inactive accounts, one page at a time. Each one can be activated again from here. */
export function InactiveAccountsPanel() {
  const [state, setState] = useState<PanelState>({ status: "loading" });
  const [busy, setBusy] = useState(false);
  const [actionError, setActionError] = useState<string | null>(null);

  useEffect(() => {
    let active = true;
    fetchPage(0)
      .then((data) => {
        if (active) {
          setState({ status: "ready", ...data });
        }
      })
      .catch(() => {
        if (active) {
          setState({ status: "error", message: "Could not load your inactive accounts." });
        }
      });

    return () => {
      active = false;
    };
  }, []);

  async function goTo(offset: number) {
    setBusy(true);
    try {
      const data = await fetchPage(offset);
      setState({ status: "ready", ...data });
    } catch {
      setState({ status: "error", message: "Could not load your inactive accounts." });
    } finally {
      setBusy(false);
    }
  }

  async function handleActivate(account: Account) {
    setBusy(true);
    setActionError(null);
    try {
      await activateAccount(account.id);
      const offset = state.status === "ready" ? state.offset : 0;
      const data = await fetchPage(offset);
      setState({ status: "ready", ...data });
    } catch (error) {
      setActionError(error instanceof ApiError ? error.message : "Could not activate the account. Try again.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className={styles.stack}>
      <BackLink />

      <section className={styles.section} aria-labelledby="inactive-title">
        <h1 id="inactive-title" className={styles.sectionTitle}>
          Inactive accounts
        </h1>
        <p className={styles.muted}>
          An inactive account accepts no deposits, withdrawals or transfers. Its balance is still counted in your total.
        </p>

        {actionError && (
          <p role="alert" className={styles.error}>
            {actionError}
          </p>
        )}

        {state.status === "loading" && <p className={styles.muted}>Loading accounts…</p>}

        {state.status === "error" && (
          <p role="alert" className={styles.error}>
            {state.message}
          </p>
        )}

        {state.status === "ready" && state.accounts.length === 0 && (
          <p className={styles.muted}>You have no inactive accounts.</p>
        )}

        {state.status === "ready" && state.accounts.length > 0 && (
          <>
            <ul className={styles.list} aria-label="Inactive accounts">
              {state.accounts.map((account) => (
                <li key={account.id} className={styles.listItem}>
                  <div className={styles.accountRow}>
                    <Link href={`/account?id=${account.id}`} className={styles.accountLink}>
                      <span>{account.name ?? "Untitled account"}</span>
                      <strong>{formatMoney(account.balance, account.currency)}</strong>
                    </Link>
                    <button
                      type="button"
                      className={styles.button}
                      onClick={() => handleActivate(account)}
                      disabled={busy}
                    >
                      {busy ? "Working…" : "Activate"}
                    </button>
                  </div>
                </li>
              ))}
            </ul>
            <Pagination offset={state.offset} totalCount={state.totalCount} disabled={busy} onChange={goTo} />
          </>
        )}
      </section>
    </div>
  );
}
