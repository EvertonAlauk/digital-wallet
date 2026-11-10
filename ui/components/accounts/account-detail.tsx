"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { ApiError } from "@/lib/api/client";
import { getAccount, getStatement } from "@/lib/api/accounts";
import { deposit, transfer, withdraw } from "@/lib/api/movements";
import { waitForOperation } from "@/lib/api/operations";
import { formatMoney } from "@/lib/accounts/money";
import type { Account, OperationAccepted, Operation, StatementItem } from "@/lib/accounts/types";
import { MovementForm } from "./movement-form";
import { StatementTable } from "./statement-table";
import { TransferForm } from "./transfer-form";
import styles from "./accounts.module.css";

type LoadState =
  | { status: "loading" }
  | { status: "ready"; account: Account }
  | { status: "not-found" }
  | { status: "error"; message: string };

interface OperationNotice {
  label: string;
  outcome: "pending" | "completed" | "rejected";
  detail?: string;
}

interface AccountDetailProps {
  accountId: string;
}

/** Everything the user can do with one account: see the balance, move money, and read the statement. */
export function AccountDetail({ accountId }: AccountDetailProps) {
  const [loadState, setLoadState] = useState<LoadState>({ status: "loading" });
  const [statement, setStatement] = useState<{ items: StatementItem[]; nextCursor: string | null }>({
    items: [],
    nextCursor: null,
  });
  const [loadingMore, setLoadingMore] = useState(false);
  const [notice, setNotice] = useState<OperationNotice | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    let active = true;
    Promise.all([getAccount(accountId), getStatement(accountId)])
      .then(([account, page]) => {
        if (active) {
          setLoadState({ status: "ready", account });
          setStatement({ items: page.items, nextCursor: page.nextCursor });
        }
      })
      .catch((error: unknown) => {
        if (!active) {
          return;
        }

        if (error instanceof ApiError && error.status === 404) {
          setLoadState({ status: "not-found" });
        } else {
          setLoadState({ status: "error", message: "Could not load this account. Try again." });
        }
      });

    return () => {
      active = false;
    };
  }, [accountId]);

  async function refreshAccountAndStatement() {
    const [account, page] = await Promise.all([getAccount(accountId), getStatement(accountId)]);
    setLoadState({ status: "ready", account });
    setStatement({ items: page.items, nextCursor: page.nextCursor });
  }

  /**
   * Money movements are accepted (202) and processed later. This waits for the outcome and shows it, so the user
   * never sees a balance that has not moved yet presented as done. Errors before acceptance propagate to the form.
   */
  async function runOperation(label: string, start: () => Promise<OperationAccepted>) {
    setBusy(true);
    try {
      const accepted = await start();
      setNotice({ label, outcome: "pending" });

      const operation = await waitForOperation(accepted.operationId);
      setNotice(describeOperation(label, operation));
      await refreshAccountAndStatement();
    } finally {
      setBusy(false);
    }
  }

  async function loadOlderEntries() {
    if (!statement.nextCursor) {
      return;
    }

    setLoadingMore(true);
    try {
      const page = await getStatement(accountId, statement.nextCursor);
      setStatement((current) => ({
        items: [...current.items, ...page.items],
        nextCursor: page.nextCursor,
      }));
    } finally {
      setLoadingMore(false);
    }
  }

  if (loadState.status === "loading") {
    return <p className={styles.muted}>Loading account…</p>;
  }

  if (loadState.status === "not-found") {
    return (
      <section className={styles.section}>
        <h1 className={styles.sectionTitle}>Account not found</h1>
        <p className={styles.muted}>It may not exist, or it may belong to another user.</p>
        <Link href="/">Back to your accounts</Link>
      </section>
    );
  }

  if (loadState.status === "error") {
    return (
      <p role="alert" className={styles.error}>
        {loadState.message}
      </p>
    );
  }

  const { account } = loadState;

  return (
    <div className={styles.stack}>
      <section className={styles.section} aria-labelledby="account-title">
        <div>
          <h1 id="account-title" className={styles.sectionTitle}>
            {account.name ?? "Untitled account"}
          </h1>
          <p className={styles.muted}>Status: {account.status}</p>
        </div>
        <p className={styles.balance} aria-label="Balance">
          {formatMoney(account.balance, account.currency)}
        </p>
        <p className={styles.muted}>
          Account id: <code>{account.id}</code>. Share it so others can send you a transfer.
        </p>
        <Link href="/">Back to your accounts</Link>
      </section>

      <section className={styles.section} aria-label="Move money">
        <div className={styles.movementRow}>
          <MovementForm
            id="deposit"
            title="Deposit"
            submitLabel="Deposit"
            disabled={busy}
            onSubmit={(amount) => runOperation("Deposit", () => deposit(accountId, amount))}
          />
          <MovementForm
            id="withdrawal"
            title="Withdraw"
            submitLabel="Withdraw"
            disabled={busy}
            onSubmit={(amount) => runOperation("Withdrawal", () => withdraw(accountId, amount))}
          />
        </div>
      </section>
      <section className={styles.section} aria-label="Transfer money">
        <TransferForm
          disabled={busy}
          onSubmit={(destination, amount) =>
            runOperation("Transfer", () => transfer(accountId, destination, amount))
          }
        />
        {notice && <OperationNoticeView notice={notice} />}
      </section>

      <section className={styles.section} aria-labelledby="statement-title">
        <h2 id="statement-title" className={styles.sectionTitle}>
          Statement
        </h2>
        <StatementTable
          items={statement.items}
          nextCursor={statement.nextCursor}
          loadingMore={loadingMore}
          onLoadMore={loadOlderEntries}
        />
      </section>
    </div>
  );
}

function OperationNoticeView({ notice }: { notice: OperationNotice }) {
  const text =
    notice.outcome === "pending"
      ? `${notice.label}: processing…`
      : notice.outcome === "completed"
        ? `${notice.label}: completed.`
        : `${notice.label}: rejected.`;

  return (
    <p
      role="status"
      aria-live="polite"
      className={`${styles.notice} ${notice.outcome === "rejected" ? styles.error : ""}`}
    >
      {text} {notice.detail}
    </p>
  );
}

function describeOperation(label: string, operation: Operation): OperationNotice {
  if (operation.status === "Completed") {
    return { label, outcome: "completed" };
  }

  if (operation.status === "Rejected") {
    return { label, outcome: "rejected", detail: operation.failureReason ?? "The request was refused." };
  }

  return {
    label,
    outcome: "pending",
    detail: "Still processing. The balance will update when it finishes.",
  };
}
