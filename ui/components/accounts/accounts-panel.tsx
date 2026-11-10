"use client";

import Link from "next/link";
import { useEffect, useState, type FormEvent } from "react";
import { ApiError } from "@/lib/api/client";
import {
  createAccount,
  deactivateAccount,
  getAccountSummary,
  listAccounts,
  type AccountSummary,
} from "@/lib/api/accounts";
import { formatMoney } from "@/lib/accounts/money";
import { MAX_ACCOUNT_NAME_LENGTH } from "@/lib/accounts/ids";
import { ACCOUNTS_PAGE_SIZE } from "@/lib/accounts/paging";
import type { Account } from "@/lib/accounts/types";
import { Pagination } from "./pagination";
import styles from "./accounts.module.css";

interface PageData {
  accounts: Account[];
  totalCount: number;
  offset: number;
  summary: AccountSummary;
}

type PanelState = { status: "loading" } | ({ status: "ready" } & PageData) | { status: "error"; message: string };

/** Loads one page of active accounts, and the total. If the page is empty after a change, the previous page is shown. */
async function fetchPage(offset: number): Promise<PageData> {
  const [list, summary] = await Promise.all([
    listAccounts("active", offset, ACCOUNTS_PAGE_SIZE),
    getAccountSummary(),
  ]);

  if (list.items.length === 0 && offset > 0) {
    return fetchPage(Math.max(0, offset - ACCOUNTS_PAGE_SIZE));
  }

  return { accounts: list.items, totalCount: list.totalCount, offset, summary };
}

/**
 * The home page's account section: the total across all accounts, the active accounts one page at a time (each can be
 * deactivated from here), and a form to open a new one. Inactive accounts are listed on their own page.
 */
export function AccountsPanel() {
  const [state, setState] = useState<PanelState>({ status: "loading" });
  const [name, setName] = useState("");
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
          setState({ status: "error", message: "Could not load your accounts." });
        }
      });

    return () => {
      active = false;
    };
  }, []);

  async function goTo(offset: number) {
    setBusy(true);
    setActionError(null);
    try {
      const data = await fetchPage(offset);
      setState({ status: "ready", ...data });
    } catch {
      setState({ status: "error", message: "Could not load your accounts." });
    } finally {
      setBusy(false);
    }
  }

  async function reloadCurrentPage() {
    const offset = state.status === "ready" ? state.offset : 0;
    await goTo(offset);
  }

  async function handleCreate(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const trimmed = name.trim();
    if (trimmed.length > MAX_ACCOUNT_NAME_LENGTH) {
      setActionError(`The name can have at most ${MAX_ACCOUNT_NAME_LENGTH} characters.`);
      return;
    }

    setBusy(true);
    setActionError(null);
    try {
      await createAccount(trimmed === "" ? null : trimmed);
      setName("");
      await goTo(0);
    } catch (error) {
      setActionError(error instanceof ApiError ? error.message : "Could not open the account. Try again.");
      setBusy(false);
    }
  }

  async function handleDeactivate(account: Account) {
    setBusy(true);
    setActionError(null);
    try {
      await deactivateAccount(account.id);
      await reloadCurrentPage();
    } catch (error) {
      setActionError(error instanceof ApiError ? error.message : "Could not deactivate the account. Try again.");
      setBusy(false);
    }
  }

  return (
    <div className={styles.stack}>
      {state.status === "ready" && <TotalCard summary={state.summary} />}

      <section className={styles.section} aria-labelledby="accounts-title">
        <div className={styles.inline}>
          <h2 id="accounts-title" className={styles.sectionTitle}>
            Your accounts
          </h2>
          <Link href="/inactive-accounts">View inactive accounts</Link>
        </div>

        <form className={styles.row} onSubmit={handleCreate} noValidate>
          <div className={styles.field}>
            <label htmlFor="new-account-name">Account name (optional)</label>
            <input
              id="new-account-name"
              value={name}
              onChange={(event) => setName(event.target.value)}
              disabled={busy}
            />
          </div>
          <button type="submit" className={styles.button} disabled={busy}>
            {busy ? "Working…" : "Create account"}
          </button>
        </form>

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
          <p className={styles.muted}>You have no active accounts. Create one to get started.</p>
        )}

        {state.status === "ready" && state.accounts.length > 0 && (
          <>
            <ul className={styles.list} aria-label="Active accounts">
              {state.accounts.map((account) => (
                <li key={account.id} className={styles.listItem}>
                  <div className={styles.accountRow}>
                    <Link href={`/account?id=${account.id}`} className={styles.accountLink}>
                      <span>{account.name ?? "Untitled account"}</span>
                      <strong>{formatMoney(account.balance, account.currency)}</strong>
                    </Link>
                    <button
                      type="button"
                      className={styles.secondaryButton}
                      onClick={() => handleDeactivate(account)}
                      disabled={busy}
                    >
                      Deactivate
                    </button>
                  </div>
                </li>
              ))}
            </ul>
            <Pagination
              offset={state.offset}
              totalCount={state.totalCount}
              disabled={busy}
              onChange={goTo}
            />
          </>
        )}
      </section>
    </div>
  );
}

function TotalCard({ summary }: { summary: AccountSummary }) {
  const accountCount = summary.activeCount + summary.inactiveCount;

  return (
    <section className={styles.section} aria-labelledby="total-title">
      <h2 id="total-title" className={styles.sectionTitle}>
        Total balance
      </h2>
      {summary.totals.length === 0 ? (
        <p className={styles.total}>{formatMoney(0, "BRL")}</p>
      ) : (
        summary.totals.map((total) => (
          <p key={total.currency} className={styles.total} aria-label="Total amount">
            {formatMoney(total.amount, total.currency)}
          </p>
        ))
      )}
      <p className={styles.muted}>
        Across {accountCount} {accountCount === 1 ? "account" : "accounts"} ({summary.activeCount} active,{" "}
        {summary.inactiveCount} inactive).
      </p>
    </section>
  );
}
