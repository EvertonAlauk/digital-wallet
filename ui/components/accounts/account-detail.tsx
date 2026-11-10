"use client";

import { useEffect, useState } from "react";
import { ApiError } from "@/lib/api/client";
import { activateAccount, deactivateAccount, getAccount, getStatement, renameAccount } from "@/lib/api/accounts";
import { deposit, transfer, withdraw } from "@/lib/api/movements";
import { waitForOperation } from "@/lib/api/operations";
import { formatMoney } from "@/lib/accounts/money";
import { MAX_ACCOUNT_NAME_LENGTH } from "@/lib/accounts/ids";
import { accountStatusLabel, isInactive } from "@/lib/accounts/status";
import {
  NO_STATEMENT_FILTER,
  hasStatementFilter,
  type StatementFilter,
} from "@/lib/accounts/statement-filter";
import type { Account, OperationAccepted, Operation, StatementItem } from "@/lib/accounts/types";
import { BackLink } from "./back-link";
import { MovementForm } from "./movement-form";
import { StatementFilters } from "./statement-filters";
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

type NoticeGroup = "movement" | "transfer";

interface AccountDetailProps {
  accountId: string;
}

/**
 * Everything the user can do with one account: rename it, switch it on or off, see the balance, move money, and read
 * the statement. An inactive account keeps its balance and statement, but refuses movements until it is activated.
 */
export function AccountDetail({ accountId }: AccountDetailProps) {
  const [loadState, setLoadState] = useState<LoadState>({ status: "loading" });
  const [statement, setStatement] = useState<{ items: StatementItem[]; nextCursor: string | null }>({
    items: [],
    nextCursor: null,
  });
  const [loadingMore, setLoadingMore] = useState(false);
  // The filter the statement currently shows. Every page request carries it, including "load older entries".
  const [appliedFilter, setAppliedFilter] = useState<StatementFilter>(NO_STATEMENT_FILTER);
  // Each box shows the outcome of its own operations: deposits and withdrawals, or transfers.
  const [notices, setNotices] = useState<Record<NoticeGroup, OperationNotice | null>>({ movement: null, transfer: null });
  const [busy, setBusy] = useState(false);
  const [editingName, setEditingName] = useState(false);
  const [nameDraft, setNameDraft] = useState("");
  const [accountError, setAccountError] = useState<string | null>(null);

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
    const [account, page] = await Promise.all([
      getAccount(accountId),
      getStatement(accountId, { filter: appliedFilter }),
    ]);
    setLoadState({ status: "ready", account });
    setStatement({ items: page.items, nextCursor: page.nextCursor });
  }

  /** Shows the first page of the statement for a new filter. The filter is kept for the older pages too. */
  async function applyStatementFilter(filter: StatementFilter) {
    const page = await getStatement(accountId, { filter });
    setAppliedFilter(filter);
    setStatement({ items: page.items, nextCursor: page.nextCursor });
  }

  /**
   * Money movements are accepted (202) and processed later. This waits for the outcome and shows it, so the user
   * never sees a balance that has not moved yet presented as done. Errors before acceptance propagate to the form.
   */
  async function runOperation(label: string, group: NoticeGroup, start: () => Promise<OperationAccepted>) {
    setBusy(true);
    try {
      const accepted = await start();
      setNotices((current) => ({ ...current, [group]: { label, outcome: "pending" } }));

      const operation = await waitForOperation(accepted.operationId);
      setNotices((current) => ({ ...current, [group]: describeOperation(label, operation) }));
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
      const page = await getStatement(accountId, { cursor: statement.nextCursor, filter: appliedFilter });
      setStatement((current) => ({
        items: [...current.items, ...page.items],
        nextCursor: page.nextCursor,
      }));
    } finally {
      setLoadingMore(false);
    }
  }

  function startEditingName(current: Account) {
    setNameDraft(current.name ?? "");
    setAccountError(null);
    setEditingName(true);
  }

  async function saveName(current: Account) {
    const trimmed = nameDraft.trim();
    if (trimmed.length > MAX_ACCOUNT_NAME_LENGTH) {
      setAccountError(`The name can have at most ${MAX_ACCOUNT_NAME_LENGTH} characters.`);
      return;
    }

    setAccountError(null);
    try {
      const updated = await renameAccount(current.id, trimmed === "" ? null : trimmed);
      setLoadState({ status: "ready", account: updated });
      setEditingName(false);
    } catch (error) {
      setAccountError(error instanceof ApiError ? error.message : "Could not save the name. Try again.");
    }
  }

  async function toggleActive(current: Account) {
    setAccountError(null);
    try {
      const updated = isInactive(current)
        ? await activateAccount(current.id)
        : await deactivateAccount(current.id);
      setLoadState({ status: "ready", account: updated });
    } catch (error) {
      setAccountError(error instanceof ApiError ? error.message : "Could not change the account. Try again.");
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
        <BackLink />
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
  const inactive = isInactive(account);

  return (
    <div className={styles.stack}>
      <BackLink />

      <section className={styles.section} aria-labelledby="account-title">
        {editingName ? (
          <div className={styles.form}>
            <label htmlFor="account-name-edit">Account name</label>
            <div className={styles.row}>
              <input
                id="account-name-edit"
                className={styles.input}
                value={nameDraft}
                onChange={(event) => setNameDraft(event.target.value)}
              />
              <button type="button" className={styles.button} onClick={() => saveName(account)}>
                Save name
              </button>
              <button type="button" className={styles.secondaryButton} onClick={() => setEditingName(false)}>
                Cancel
              </button>
            </div>
          </div>
        ) : (
          <div className={styles.inline}>
            <div>
              <h1 id="account-title" className={styles.sectionTitle}>
                {account.name ?? "Untitled account"}
              </h1>
              <p className={styles.muted}>Status: {accountStatusLabel(account)}</p>
            </div>
            <div className={styles.row}>
              <button type="button" className={styles.secondaryButton} onClick={() => startEditingName(account)}>
                Edit name
              </button>
              <button type="button" className={styles.secondaryButton} onClick={() => toggleActive(account)}>
                {inactive ? "Activate account" : "Deactivate account"}
              </button>
            </div>
          </div>
        )}

        {inactive && (
          <p role="status" className={styles.notice}>
            This account is inactive. Deposits, withdrawals and transfers are disabled until you activate it. Its
            balance and statement stay available.
          </p>
        )}

        {accountError && (
          <p role="alert" className={styles.error}>
            {accountError}
          </p>
        )}

        <p className={styles.balance} aria-label="Balance">
          {formatMoney(account.balance, account.currency)}
        </p>
        <p className={styles.muted}>
          Account id: <code>{account.id}</code>. Share it so others can send you a transfer.
        </p>
      </section>

      {/* An inactive account has no movement forms at all: they are not shown, rather than shown disabled. */}
      {!inactive && (
        <>
          <section className={styles.section} aria-label="Move money">
            <div className={styles.movementRow}>
              <MovementForm
                id="deposit"
                title="Deposit"
                submitLabel="Deposit"
                disabled={busy}
                onSubmit={(amount) => runOperation("Deposit", "movement", () => deposit(accountId, amount))}
              />
              <MovementForm
                id="withdrawal"
                title="Withdraw"
                submitLabel="Withdraw"
                disabled={busy}
                onSubmit={(amount) => runOperation("Withdrawal", "movement", () => withdraw(accountId, amount))}
              />
            </div>
            {notices.movement && <OperationNoticeView notice={notices.movement} />}
          </section>
          <section className={styles.section} aria-label="Transfer money">
            <TransferForm
              disabled={busy}
              onSubmit={(destination, amount) =>
                runOperation("Transfer", "transfer", () => transfer(accountId, destination, amount))
              }
            />
            {notices.transfer && <OperationNoticeView notice={notices.transfer} />}
          </section>
        </>
      )}

      <section className={styles.section} aria-labelledby="statement-title">
        <h2 id="statement-title" className={styles.sectionTitle}>
          Statement
        </h2>
        <StatementFilters applied={appliedFilter} onApply={applyStatementFilter} />
        <StatementTable
          items={statement.items}
          nextCursor={statement.nextCursor}
          loadingMore={loadingMore}
          filtered={hasStatementFilter(appliedFilter)}
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
